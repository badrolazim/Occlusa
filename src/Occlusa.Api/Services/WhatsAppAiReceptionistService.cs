using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Occlusa.Api.Options;
using Occlusa.Api.Repositories;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Api.Services;

public interface IWhatsAppAiReceptionistService
{
    Task<WhatsAppThreadDto> ProcessInboundMessageAsync(
        string? threadId,
        string patientPhone,
        string patientName,
        string messageText,
        string branchId,
        bool sendViaMetaCloudApi = false,
        CancellationToken ct = default);

    Task<WhatsAppThreadDto?> SendStaffTakeoverReplyAsync(
        string threadId,
        string messageText,
        string staffName,
        CancellationToken ct = default);
}

/// <summary>
/// Orchestrates Meta WhatsApp Cloud API messages, Gemini API function calling + deterministic local fallback,
/// and enforces all 4 Occlusa clinical & cost-optimization guardrails.
/// </summary>
public sealed class WhatsAppAiReceptionistService : IWhatsAppAiReceptionistService
{
    private static readonly TimeSpan MalaysiaOffset = TimeSpan.FromHours(8);

    private readonly IOcclusaRepository _repository;
    private readonly IRealtimeNotifier _notifier;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeminiOptions _geminiOptions;
    private readonly WhatsAppOptions _whatsAppOptions;
    private readonly ClinicOptions _clinicOptions;
    private readonly ILogger<WhatsAppAiReceptionistService> _logger;

    public WhatsAppAiReceptionistService(
        IOcclusaRepository repository,
        IRealtimeNotifier notifier,
        IHttpClientFactory httpClientFactory,
        IOptions<GeminiOptions> geminiOptions,
        IOptions<WhatsAppOptions> whatsAppOptions,
        IOptions<ClinicOptions> clinicOptions,
        ILogger<WhatsAppAiReceptionistService> logger)
    {
        _repository = repository;
        _notifier = notifier;
        _httpClientFactory = httpClientFactory;
        _geminiOptions = geminiOptions.Value;
        _whatsAppOptions = whatsAppOptions.Value;
        _clinicOptions = clinicOptions.Value;
        _logger = logger;
    }

    public async Task<WhatsAppThreadDto> ProcessInboundMessageAsync(
        string? threadId,
        string patientPhone,
        string patientName,
        string messageText,
        string branchId,
        bool sendViaMetaCloudApi = false,
        CancellationToken ct = default)
    {
        var cleanPhone = string.IsNullOrWhiteSpace(patientPhone) ? "+6012-0000000" : patientPhone.Trim();
        var cleanName = string.IsNullOrWhiteSpace(patientName) ? cleanPhone : patientName.Trim();
        var targetBranch = InferBranchFromText(messageText, ClinicDomainHelpers.NormalizeBranch(branchId, _clinicOptions.DefaultBranchId));
        var resolvedThreadId = string.IsNullOrWhiteSpace(threadId)
            ? $"wa-{NormalizeDigits(cleanPhone)}"
            : threadId.Trim();

        // Record inbound patient message first.
        var inboundMsg = new WhatsAppMessageDto
        {
            Id = Guid.NewGuid(),
            Sender = "Patient",
            Text = messageText?.Trim() ?? string.Empty,
            Timestamp = DateTimeOffset.UtcNow
        };

        var thread = await _repository.AppendWhatsAppMessageAsync(
            resolvedThreadId,
            targetBranch,
            cleanPhone,
            cleanName,
            inboundMsg,
            ct: ct);

        // GUARDRAIL 3 & 2: Deterministic safety pre-classification runs unconditionally before any AI model.
        var triage = EmergencyTriageClassifier.Classify(messageText);

        string replyText;
        Guid? createdBookingId = null;
        bool? setEscalated = null;
        string? emergencyReason = null;

        if (triage.IsEmergency)
        {
            // GUARDRAIL 3: Mandatory emergency escalation - halt booking immediately & emit EmergencyEscalated.
            setEscalated = true;
            emergencyReason = triage.Reason;
            var branchHotline = targetBranch == "PJ-SS2" ? "PJ SS2 (+603-7876-4420)" : "KL Sentral HQ (+603-2274-8810)";
            replyText =
                $"URGENT TRIAGE ALERT: Based on your symptoms, we have paused automated booking and escalated your message to our clinical team at {branchHotline} for immediate callback. If you experience severe swelling, uncontrolled bleeding, fever, or difficulty breathing/swallowing, please go to the nearest hospital A&E immediately.";
        }
        else if (triage.IsComplexProcedureBlocked)
        {
            // GUARDRAIL 2: Complex/surgical procedure blocked from AI auto-booking.
            replyText =
                "Thank you for reaching out to Occlusa Dental. Surgical, implant, wisdom tooth surgery, braces, sedation, and full-mouth rehabilitation cases require personalized dentist review and cannot be booked automatically here. I have flagged this for our front-desk coordinator to call you back during clinic hours (Mon–Sat, 9:00am–6:00pm).";
        }
        else if (!string.IsNullOrWhiteSpace(_geminiOptions.ApiKey))
        {
            var geminiOutcome = await TryProcessWithGeminiAsync(
                thread,
                targetBranch,
                cleanPhone,
                cleanName,
                messageText ?? string.Empty,
                ct);

            if (geminiOutcome.Success)
            {
                replyText = geminiOutcome.ReplyText;
                createdBookingId = geminiOutcome.CreatedBookingId;
                if (geminiOutcome.Escalated)
                {
                    setEscalated = true;
                    emergencyReason = geminiOutcome.EscalationReason;
                }
            }
            else
            {
                var local = await RunLocalReceptionistEngineAsync(
                    triage, targetBranch, cleanPhone, cleanName, messageText ?? string.Empty, ct);
                replyText = local.ReplyText;
                createdBookingId = local.CreatedBookingId;
            }
        }
        else
        {
            var local = await RunLocalReceptionistEngineAsync(
                triage, targetBranch, cleanPhone, cleanName, messageText ?? string.Empty, ct);
            replyText = local.ReplyText;
            createdBookingId = local.CreatedBookingId;
        }

        // GUARDRAIL 4: Combine each AI turn into ONE concise single message bubble.
        replyText = CollapseToSingleBubble(replyText);

        var aiReply = new WhatsAppMessageDto
        {
            Id = Guid.NewGuid(),
            Sender = "AI Receptionist",
            Text = replyText,
            Timestamp = DateTimeOffset.UtcNow,
            CreatedBookingId = createdBookingId
        };

        thread = await _repository.AppendWhatsAppMessageAsync(
            resolvedThreadId,
            targetBranch,
            cleanPhone,
            cleanName,
            aiReply,
            setEmergencyEscalated: setEscalated,
            emergencyReason: emergencyReason,
            ct: ct);

        await _notifier.NotifyWhatsAppUpdatedAsync(thread, ct);
        if (setEscalated == true || thread.IsEmergencyEscalated)
        {
            await _notifier.NotifyEmergencyEscalatedAsync(thread, ct);
        }

        if (sendViaMetaCloudApi)
        {
            await TrySendMetaCloudMessageAsync(cleanPhone, replyText, ct);
        }

        return thread;
    }

    public async Task<WhatsAppThreadDto?> SendStaffTakeoverReplyAsync(
        string threadId,
        string messageText,
        string staffName,
        CancellationToken ct = default)
    {
        var existing = await _repository.GetWhatsAppThreadAsync(threadId, ct);
        if (existing is null)
        {
            return null;
        }

        var senderLabel = string.IsNullOrWhiteSpace(staffName) ? "Staff" : $"Staff ({staffName.Trim()})";
        var msg = new WhatsAppMessageDto
        {
            Id = Guid.NewGuid(),
            Sender = senderLabel,
            Text = CollapseToSingleBubble(messageText),
            Timestamp = DateTimeOffset.UtcNow
        };

        var updated = await _repository.AppendWhatsAppMessageAsync(
            threadId,
            _clinicOptions.DefaultBranchId,
            existing.PatientPhone,
            existing.PatientName,
            msg,
            ct: ct);

        await _notifier.NotifyWhatsAppUpdatedAsync(updated, ct);
        await TrySendMetaCloudMessageAsync(existing.PatientPhone, msg.Text, ct);
        return updated;
    }

    private async Task<(string ReplyText, Guid? CreatedBookingId)> RunLocalReceptionistEngineAsync(
        TriageResult triage,
        string branchId,
        string patientPhone,
        string patientName,
        string messageText,
        CancellationToken ct)
    {
        var branchLabel = branchId == "PJ-SS2" ? "PJ SS2" : "KL Sentral HQ";
        var settings = await _repository.GetBillingSettingsAsync(ct);
        var routineFeeSummary = BuildLiveRoutineFeeSummary(settings.CatalogItems);

        if (!triage.IsRoutineBookingEligible)
        {
            var slots = await GetAvailableSlotsAsync(branchId, null, ct);
            var nextSlotText = slots.Count > 0 ? slots[0] : "tomorrow 10:00 AM";
            return (
                $"Occlusa Dental ({branchLabel}) is open Mon–Sat, 9:00am–6:00pm (KL Sentral HQ & PJ SS2). Standard routine fees: {routineFeeSummary}. Our next open slot is {nextSlotText} — reply with 'book' and your preferred procedure to reserve an AI-Booked (Pending) slot.",
                null);
        }

        var (procedure, feeLabel) = DetectRoutineProcedure(messageText, settings.CatalogItems);
        var slot = await PickNextOpenSlotAsync(branchId, messageText, ct);

        var created = await CreatePendingAiAppointmentAsync(
            branchId,
            patientName,
            patientPhone,
            procedure,
            slot,
            ct);

        var malaysiaLocalTime = created.ScheduledAt.ToOffset(MalaysiaOffset);
        var reply =
            $"Booked! I've reserved a provisional slot for {procedure} ({feeLabel}) at {branchLabel} with {created.DoctorName} on {malaysiaLocalTime:ddd dd MMM, hh:mm tt}. Status: AI-Booked (Pending) — our front-desk team will confirm shortly.";

        return (reply, created.Id);
    }

    private async Task<(bool Success, string ReplyText, Guid? CreatedBookingId, bool Escalated, string? EscalationReason)>
        TryProcessWithGeminiAsync(
            WhatsAppThreadDto thread,
            string branchId,
            string patientPhone,
            string patientName,
            string messageText,
            CancellationToken ct)
    {
        try
        {
            var model = string.IsNullOrWhiteSpace(_geminiOptions.Model) ? "gemini-2.5-flash" : _geminiOptions.Model.Trim();
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_geminiOptions.ApiKey}";

            var settings = await _repository.GetBillingSettingsAsync(ct);
            var routineFeeSummary = BuildLiveRoutineFeeSummary(settings.CatalogItems);
            var systemPrompt = $"""
                You are Occlusa Dental Clinic's WhatsApp AI Receptionist in Malaysia.
                STRICT GUARDRAILS (NEVER VIOLATE):
                1. Allowed scope ONLY: clinic hours (Mon-Sat 9:00am-6:00pm, closed Sunday), clinic locations (KL Sentral HQ [HQ-KL] & PJ SS2 [PJ-SS2]), standard Master Catalogue fees ({routineFeeSummary}), and routine appointment booking.
                2. NEVER diagnose, prescribe, or auto-book complex/surgical procedures (implants, wisdom tooth surgery, full-mouth rehab, braces, sedation). Redirect those to human front-desk callback.
                3. Every booking created MUST go through `create_appointment` (which marks it AiBookedPending).
                4. If the patient reports severe pain, facial swelling, uncontrolled bleeding, trauma, or difficulty breathing/swallowing, IMMEDIATELY call `escalate_emergency_to_staff` and do NOT book.
                5. Respond in ONE concise single message bubble (max 3 short sentences, no bullet lists).
                """;

            var contents = new JsonArray();
            foreach (var msg in thread.Messages.TakeLast(6))
            {
                var role = msg.Sender.Equals("Patient", StringComparison.OrdinalIgnoreCase) ? "user" : "model";
                contents.Add(new JsonObject
                {
                    ["role"] = role,
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = msg.Text } }
                });
            }

            if (contents.Count == 0)
            {
                contents.Add(new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = messageText } }
                });
            }

            var tools = new JsonArray
            {
                new JsonObject
                {
                    ["function_declarations"] = new JsonArray
                    {
                        BuildToolDecl("get_available_slots",
                            "Get next available routine dental slots for a branch (HQ-KL or PJ-SS2).",
                            new[] { ("branch_id", "string", "Branch ID: HQ-KL or PJ-SS2"), ("preferred_date", "string", "Optional YYYY-MM-DD") },
                            new[] { "branch_id" }),
                        BuildToolDecl("create_appointment",
                            "Create a provisional routine appointment with Status = AiBookedPending.",
                            new[]
                            {
                                ("branch_id", "string", "Branch ID: HQ-KL or PJ-SS2"),
                                ("patient_name", "string", "Patient full name"),
                                ("patient_phone", "string", "Patient phone"),
                                ("procedure", "string", "Routine procedure (Consultation, Scaling & Polishing, or Simple Filling)"),
                                ("scheduled_at_iso", "string", "ISO-8601 slot timestamp")
                            },
                            new[] { "branch_id", "procedure", "scheduled_at_iso" }),
                        BuildToolDecl("escalate_emergency_to_staff",
                            "Immediately escalate red-flag dental emergencies to clinic staff and halt booking.",
                            new[] { ("reason", "string", "Clinical triage escalation reason") },
                            new[] { "reason" })
                    }
                }
            };

            Guid? createdBookingId = null;
            var escalated = false;
            string? escalationReason = null;

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            for (var round = 0; round < 3; round++)
            {
                var payload = new JsonObject
                {
                    ["system_instruction"] = new JsonObject
                    {
                        ["parts"] = new JsonArray { new JsonObject { ["text"] = systemPrompt } }
                    },
                    ["contents"] = contents,
                    ["tools"] = tools,
                    ["generationConfig"] = new JsonObject { ["temperature"] = 0.2 }
                };

                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
                };

                using var resp = await client.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Gemini API returned status {StatusCode}; falling back to local receptionist.", resp.StatusCode);
                    return (false, string.Empty, null, false, null);
                }

                var rawJson = await resp.Content.ReadAsStringAsync(ct);
                var doc = JsonNode.Parse(rawJson);
                var candidateContent = doc?["candidates"]?[0]?["content"];
                var parts = candidateContent?["parts"]?.AsArray();
                if (parts is null || parts.Count == 0)
                {
                    return (false, string.Empty, null, false, null);
                }

                var functionCallPart = parts.FirstOrDefault(p => p?["functionCall"] is not null);
                if (functionCallPart is null)
                {
                    var textOut = string.Join(" ", parts.Select(p => p?["text"]?.GetValue<string>()).Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (string.IsNullOrWhiteSpace(textOut))
                    {
                        return (false, string.Empty, null, false, null);
                    }

                    return (true, textOut, createdBookingId, escalated, escalationReason);
                }

                contents.Add(candidateContent!.DeepClone());

                var fnCall = functionCallPart["functionCall"]!;
                var fnName = fnCall["name"]?.GetValue<string>() ?? string.Empty;
                var fnArgs = fnCall["args"]?.AsObject() ?? new JsonObject();

                JsonObject fnResult;
                switch (fnName)
                {
                    case "get_available_slots":
                    {
                        var reqBranch = ClinicDomainHelpers.NormalizeBranch(fnArgs["branch_id"]?.GetValue<string>(), branchId);
                        var prefDate = fnArgs["preferred_date"]?.GetValue<string>();
                        var slots = await GetAvailableSlotsAsync(reqBranch, prefDate, ct);
                        fnResult = new JsonObject
                        {
                            ["branch_id"] = reqBranch,
                            ["slots"] = new JsonArray(slots.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray())
                        };
                        break;
                    }
                    case "create_appointment":
                    {
                        var proc = fnArgs["procedure"]?.GetValue<string>() ?? "Consultation";
                        var procCheck = EmergencyTriageClassifier.Classify(proc);
                        if (procCheck.IsEmergency || procCheck.IsComplexProcedureBlocked)
                        {
                            fnResult = new JsonObject
                            {
                                ["created"] = false,
                                ["error"] = procCheck.Reason
                            };
                            break;
                        }

                        var reqBranch = ClinicDomainHelpers.NormalizeBranch(fnArgs["branch_id"]?.GetValue<string>(), branchId);
                        var iso = fnArgs["scheduled_at_iso"]?.GetValue<string>();
                        var scheduledAt = DateTimeOffset.TryParse(iso, out var parsedDt)
                            ? parsedDt
                            : await PickNextOpenSlotAsync(reqBranch, messageText, ct);

                        var pName = fnArgs["patient_name"]?.GetValue<string>() ?? patientName;
                        var pPhone = fnArgs["patient_phone"]?.GetValue<string>() ?? patientPhone;

                        var appt = await CreatePendingAiAppointmentAsync(reqBranch, pName, pPhone, proc, scheduledAt, ct);
                        createdBookingId = appt.Id;
                        fnResult = new JsonObject
                        {
                            ["created"] = true,
                            ["appointment_id"] = appt.Id.ToString(),
                            ["status"] = appt.Status.ToString(),
                            ["doctor"] = appt.DoctorName,
                            ["scheduled_at"] = appt.ScheduledAt.ToOffset(MalaysiaOffset).ToString("ddd dd MMM, hh:mm tt")
                        };
                        break;
                    }
                    case "escalate_emergency_to_staff":
                    {
                        escalated = true;
                        escalationReason = fnArgs["reason"]?.GetValue<string>() ?? "Emergency escalation triggered by AI receptionist.";
                        fnResult = new JsonObject
                        {
                            ["escalated"] = true,
                            ["booking_halted"] = true
                        };
                        break;
                    }
                    default:
                        fnResult = new JsonObject { ["error"] = $"Unknown function '{fnName}'." };
                        break;
                }

                contents.Add(new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["functionResponse"] = new JsonObject
                            {
                                ["name"] = fnName,
                                ["response"] = fnResult
                            }
                        }
                    }
                });
            }

            return (false, string.Empty, null, false, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini API invocation failed; using local deterministic receptionist fallback.");
            return (false, string.Empty, null, false, null);
        }
    }

    private async Task<AppointmentQueueItemDto> CreatePendingAiAppointmentAsync(
        string branchId,
        string patientName,
        string patientPhone,
        string procedure,
        DateTimeOffset slot,
        CancellationToken ct)
    {
        var patient = await _repository.FindPatientByPhoneOrNameAsync(patientPhone, patientName, ct);
        if (patient is null)
        {
            var digits = NormalizeDigits(patientPhone);
            var placeholderIc = string.IsNullOrEmpty(digits) ? $"WA-{Guid.NewGuid():N}"[..14] : $"WA-{digits}";
            patient = await _repository.UpsertPatientAsync(new UpsertPatientRequest
            {
                FullName = patientName,
                IcNumber = placeholderIc,
                DateOfBirth = new DateOnly(1995, 1, 1),
                Gender = "Unspecified",
                Phone = patientPhone,
                PrimaryBranchId = branchId,
                IsMyKadVerified = false
            }, forceMyKadVerified: false, ct: ct);
        }

        var doctor = branchId == "PJ-SS2" ? "Dr. Marcus Tan" : "Dr. Aina Rahman";
        var room = branchId == "PJ-SS2" ? "PJ Room 2" : "Suite 3";

        // GUARDRAIL 1: Every AI-created booking MUST have Status = QueueStatus.AiBookedPending.
        var created = await _repository.CreateAppointmentAsync(new CreateAppointmentRequest
        {
            BranchId = branchId,
            PatientId = patient.Id,
            DoctorName = doctor,
            RoomName = room,
            ScheduledAt = slot,
            ProcedureSummary = procedure,
            Status = QueueStatus.AiBookedPending,
            VisitType = VisitType.AiBooked,
            CreatedBy = "WhatsApp AI"
        }, ct);

        await _notifier.NotifyQueueUpdatedAsync(created, ct);
        return created;
    }

    private async Task<List<string>> GetAvailableSlotsAsync(string branchId, string? preferredDate, CancellationToken ct)
    {
        var existing = await _repository.GetAppointmentsAsync(branchId, ct);
        var busyTimes = existing
            .Where(a => a.Status != QueueStatus.Cancelled)
            .Select(a => a.ScheduledAt.ToOffset(MalaysiaOffset))
            .ToList();

        var startDate = DateOnly.TryParse(preferredDate, out var parsedDate)
            ? parsedDate
            : DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(MalaysiaOffset).DateTime.AddDays(1));

        var slots = new List<string>();
        for (var dayOffset = 0; dayOffset < 5 && slots.Count < 4; dayOffset++)
        {
            var date = startDate.AddDays(dayOffset);
            if (date.DayOfWeek == DayOfWeek.Sunday)
            {
                continue;
            }

            foreach (var hour in new[] { 10, 11, 14, 16 })
            {
                var candidate = new DateTimeOffset(date.Year, date.Month, date.Day, hour, 0, 0, MalaysiaOffset);
                if (candidate <= DateTimeOffset.UtcNow.ToOffset(MalaysiaOffset).AddHours(1))
                {
                    continue;
                }

                var isBusy = busyTimes.Any(b => Math.Abs((b - candidate).TotalMinutes) < 45);
                if (!isBusy)
                {
                    slots.Add($"{candidate:ddd dd MMM, hh:mm tt} ({candidate:O})");
                    if (slots.Count >= 4)
                    {
                        break;
                    }
                }
            }
        }

        return slots;
    }

    private async Task<DateTimeOffset> PickNextOpenSlotAsync(string branchId, string messageText, CancellationToken ct)
    {
        var existing = await _repository.GetAppointmentsAsync(branchId, ct);
        var busyTimes = existing
            .Where(a => a.Status != QueueStatus.Cancelled)
            .Select(a => a.ScheduledAt.ToOffset(MalaysiaOffset))
            .ToList();

        var nowMyt = DateTimeOffset.UtcNow.ToOffset(MalaysiaOffset);
        var lower = messageText.ToLowerInvariant();
        var preferAfternoon = lower.Contains("afternoon") || lower.Contains("petang") || lower.Contains("pm");
        var hoursToTry = preferAfternoon ? new[] { 14, 15, 16, 10, 11 } : new[] { 10, 11, 14, 15, 16 };

        for (var dayOffset = 1; dayOffset <= 7; dayOffset++)
        {
            var date = DateOnly.FromDateTime(nowMyt.DateTime.AddDays(dayOffset));
            if (date.DayOfWeek == DayOfWeek.Sunday)
            {
                continue;
            }

            foreach (var hour in hoursToTry)
            {
                var candidate = new DateTimeOffset(date.Year, date.Month, date.Day, hour, 0, 0, MalaysiaOffset);
                if (!busyTimes.Any(b => Math.Abs((b - candidate).TotalMinutes) < 45))
                {
                    return candidate.ToUniversalTime();
                }
            }
        }

        return nowMyt.AddDays(1).ToUniversalTime();
    }

    private async Task TrySendMetaCloudMessageAsync(string toPhone, string bodyText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_whatsAppOptions.AccessToken)
            || string.IsNullOrWhiteSpace(_whatsAppOptions.PhoneNumberId))
        {
            return;
        }

        try
        {
            var digits = NormalizeDigits(toPhone);
            if (string.IsNullOrEmpty(digits))
            {
                return;
            }

            var apiVersion = string.IsNullOrWhiteSpace(_whatsAppOptions.ApiVersion) ? "v21.0" : _whatsAppOptions.ApiVersion.Trim();
            var endpoint = $"https://graph.facebook.com/{apiVersion}/{_whatsAppOptions.PhoneNumberId.Trim()}/messages";

            var payload = JsonSerializer.Serialize(new
            {
                messaging_product = "whatsapp",
                to = digits,
                type = "text",
                text = new { body = bodyText }
            });

            var client = _httpClientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _whatsAppOptions.AccessToken.Trim());
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Meta WhatsApp Graph API returned {StatusCode}", resp.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed sending reply via Meta WhatsApp Cloud API.");
        }
    }

    private static string InferBranchFromText(string? text, string fallbackBranch)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallbackBranch;
        }

        var lower = text.ToLowerInvariant();
        if (lower.Contains("pj") || lower.Contains("ss2") || lower.Contains("petaling"))
        {
            return "PJ-SS2";
        }

        if (lower.Contains("kl") || lower.Contains("sentral") || lower.Contains("hq"))
        {
            return "HQ-KL";
        }

        return fallbackBranch;
    }

    private static string BuildLiveRoutineFeeSummary(IReadOnlyList<BillingCatalogItemDto> catalog)
    {
        decimal PriceOf(string fragment, decimal fallback) =>
            catalog.FirstOrDefault(c => c.IsActive && c.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase))?.UnitPrice ?? fallback;

        var consult = PriceOf("Consultation", 50m);
        var scaling = PriceOf("Scaling", 150m);
        var filling = PriceOf("Composite Filling", 200m);
        return $"Consultation RM{consult:0.##}, Scaling & Polishing RM{scaling:0.##}, Composite Filling RM{filling:0.##}";
    }

    private static (string Procedure, string FeeLabel) DetectRoutineProcedure(
        string messageText,
        IReadOnlyList<BillingCatalogItemDto> catalog)
    {
        var lower = messageText.ToLowerInvariant();
        BillingCatalogItemDto? Find(string fragment) =>
            catalog.FirstOrDefault(c => c.IsActive && c.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

        if (lower.Contains("scaling") || lower.Contains("polish") || lower.Contains("clean") || lower.Contains("cuci"))
        {
            var item = Find("Scaling");
            return (item?.Name ?? "Routine Scaling & Polishing", $"RM{(item?.UnitPrice ?? 150m):0.##}");
        }

        if (lower.Contains("filling") || lower.Contains("tampal"))
        {
            var item = Find("Composite Filling");
            return (item?.Name ?? "Posterior Composite Filling", $"RM{(item?.UnitPrice ?? 200m):0.##}");
        }

        var consult = Find("Consultation");
        return (consult?.Name ?? "Dental Consultation & Checkup", $"RM{(consult?.UnitPrice ?? 50m):0.##}");
    }

    private static string CollapseToSingleBubble(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeDigits(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsAsciiDigit).ToArray());

    private static JsonObject BuildToolDecl(
        string name,
        string description,
        (string Name, string Type, string Desc)[] props,
        string[] required)
    {
        var propertiesObj = new JsonObject();
        foreach (var (pName, pType, pDesc) in props)
        {
            propertiesObj[pName] = new JsonObject
            {
                ["type"] = pType.ToUpperInvariant(),
                ["description"] = pDesc
            };
        }

        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["parameters"] = new JsonObject
            {
                ["type"] = "OBJECT",
                ["properties"] = propertiesObj,
                ["required"] = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray())
            }
        };
    }
}
