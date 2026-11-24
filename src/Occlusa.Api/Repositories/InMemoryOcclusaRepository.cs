using Microsoft.Extensions.Options;
using Occlusa.Api.Options;
using Occlusa.Api.Security;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Api.Repositories;

/// <summary>
/// Thread-safe, zero-config pre-seeded in-memory repository for immediate $0 local execution
/// when Supabase:ConnectionString is not configured.
/// </summary>
public sealed class InMemoryOcclusaRepository : IOcclusaRepository
{
    private readonly object _sync = new();
    private readonly ClinicOptions _clinicOptions;
    private readonly IHttpContextAccessor _httpContextAccessor;

    private readonly Dictionary<Guid, PatientDto> _patients = new();
    private readonly List<DentistDto> _dentists = new();
    private readonly List<TreatmentDto> _treatments = new();
    private readonly Dictionary<Guid, AppointmentQueueItemDto> _appointments = new();
    private readonly Dictionary<(Guid PatientId, int ToothNumber), OdontogramToothDto> _odontogram = new();
    private readonly Dictionary<Guid, List<SoapNoteDto>> _soapNotes = new();
    private readonly List<SoapMacroDto> _soapMacros = new();
    private readonly Dictionary<Guid, InvoiceDto> _invoices = new();
    private readonly Dictionary<(string BranchId, DateOnly Date), DailySalesReconciliationDto> _reconciliations = new();
    private readonly Dictionary<string, (string BranchId, WhatsAppThreadDto Thread)> _whatsAppThreads = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AuditLogDto> _auditLogs = new();
    private readonly List<BackupVerificationDto> _backups = new();

    private int _invoiceSeq = 1008;

    public string DatabaseMode => "LocalSeededFallback";

    public InMemoryOcclusaRepository(
        IOptions<ClinicOptions> clinicOptions,
        IHttpContextAccessor httpContextAccessor)
    {
        _clinicOptions = clinicOptions.Value;
        _httpContextAccessor = httpContextAccessor;
        SeedData();
    }

    private RequestClaimsContext CurrentClaims =>
        _httpContextAccessor.HttpContext?.RequestServices.GetService<RequestClaimsContext>()
        ?? new RequestClaimsContext();

    public Task<ClinicDashboardSnapshotDto> GetDashboardSnapshotAsync(string branchId, CancellationToken ct = default)
    {
        var normalizedBranch = ClinicDomainHelpers.NormalizeBranch(branchId, _clinicOptions.DefaultBranchId);
        var claims = CurrentClaims;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        lock (_sync)
        {
            var branchAllowed = claims.CanAccessBranch(normalizedBranch);

            var queue = branchAllowed
                ? _appointments.Values
                    .Where(a => string.Equals(a.BranchId, normalizedBranch, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(a => a.ScheduledAt)
                    .Select(ClinicDomainHelpers.Clone)
                    .ToList()
                : new List<AppointmentQueueItemDto>();

            var patients = _patients.Values
                .OrderBy(p => p.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(ClinicDomainHelpers.Clone)
                .ToList();

            var invoices = branchAllowed
                ? _invoices.Values
                    .Where(i => string.Equals(i.BranchId, normalizedBranch, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(i => i.CreatedAt)
                    .Select(ClinicDomainHelpers.Clone)
                    .ToList()
                : new List<InvoiceDto>();

            var threads = branchAllowed
                ? _whatsAppThreads.Values
                    .Where(t => string.Equals(t.BranchId, normalizedBranch, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(t => t.Thread.LastUpdatedAt)
                    .Select(t => ClinicDomainHelpers.Clone(t.Thread))
                    .ToList()
                : new List<WhatsAppThreadDto>();

            var dentists = branchAllowed
                ? _dentists
                    .Where(d => d.IsActive && (d.BranchIds.Count == 0 || d.BranchIds.Contains(normalizedBranch, StringComparer.OrdinalIgnoreCase)))
                    .OrderBy(d => d.FullName, StringComparer.OrdinalIgnoreCase)
                    .Select(ClinicDomainHelpers.Clone)
                    .ToList()
                : new List<DentistDto>();

            var treatments = _treatments
                .Where(t => t.IsActive)
                .OrderBy(t => t.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ClinicDomainHelpers.Clone)
                .ToList();

            var dailySummary = BillingCalculator.CalculateDailySalesReconciliation(
                normalizedBranch,
                today,
                invoices,
                claims.UserId);

            return Task.FromResult(new ClinicDashboardSnapshotDto
            {
                BranchId = normalizedBranch,
                DatabaseMode = DatabaseMode,
                GeneratedAt = DateTimeOffset.UtcNow,
                Queue = queue,
                Patients = patients,
                Dentists = dentists,
                Treatments = treatments,
                Invoices = invoices,
                SoapMacros = _soapMacros.Select(CloneMacro).ToList(),
                WhatsAppThreads = threads,
                DailySummary = dailySummary
            });
        }
    }

    public Task<IReadOnlyList<PatientDto>> GetPatientsAsync(string? search = null, CancellationToken ct = default)
    {
        lock (_sync)
        {
            IEnumerable<PatientDto> query = _patients.Values;
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(p =>
                    p.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    p.IcNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    p.Phone.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyList<PatientDto>>(
                query.OrderBy(p => p.FullName, StringComparer.OrdinalIgnoreCase)
                    .Select(ClinicDomainHelpers.Clone)
                    .ToList());
        }
    }

    public Task<PatientDto?> GetPatientByIdAsync(Guid patientId, CancellationToken ct = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_patients.TryGetValue(patientId, out var p) ? ClinicDomainHelpers.Clone(p) : null);
        }
    }

    public Task<PatientDto?> FindPatientByPhoneOrNameAsync(string? phone, string? name, CancellationToken ct = default)
    {
        lock (_sync)
        {
            var normalizedPhone = NormalizeDigits(phone);
            var match = _patients.Values.FirstOrDefault(p =>
                (!string.IsNullOrEmpty(normalizedPhone) && NormalizeDigits(p.Phone) == normalizedPhone) ||
                (!string.IsNullOrWhiteSpace(name) && string.Equals(p.FullName.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)));

            return Task.FromResult(match is null ? null : ClinicDomainHelpers.Clone(match));
        }
    }

    public Task<PatientDto> UpsertPatientAsync(UpsertPatientRequest request, bool forceMyKadVerified = false, CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            var target = UpsertPatientLocked(request, forceMyKadVerified, claims);
            return Task.FromResult(ClinicDomainHelpers.Clone(target));
        }
    }

    private PatientDto UpsertPatientLocked(UpsertPatientRequest request, bool forceMyKadVerified, RequestClaimsContext claims)
    {
        var parsedNric = MyKadParser.ParseNric(request.IcNumber);
        var normalizedIc = parsedNric.IsValid
            ? parsedNric.NormalizedIc
            : (request.IcNumber ?? string.Empty).Trim();

        PatientDto? existing = null;
        if (request.Id.HasValue && _patients.TryGetValue(request.Id.Value, out var byId))
        {
            existing = byId;
        }
        else if (!string.IsNullOrWhiteSpace(normalizedIc))
        {
            existing = _patients.Values.FirstOrDefault(p =>
                string.Equals(p.IcNumber, normalizedIc, StringComparison.OrdinalIgnoreCase));
        }

        var isNew = existing is null;
        var target = existing ?? new PatientDto
        {
            Id = request.Id ?? Guid.NewGuid()
        };

        var oldSnapshot = existing is null ? null : $"{existing.FullName} ({existing.IcNumber})";

        target.FullName = string.IsNullOrWhiteSpace(request.FullName)
            ? target.FullName
            : request.FullName.Trim();
        target.IcNumber = string.IsNullOrWhiteSpace(normalizedIc) ? target.IcNumber : normalizedIc;
        target.DateOfBirth = request.DateOfBirth != default
            ? request.DateOfBirth
            : (parsedNric.DateOfBirth ?? (target.DateOfBirth != default ? target.DateOfBirth : new DateOnly(1990, 1, 1)));
        target.Gender = !string.IsNullOrWhiteSpace(request.Gender)
            ? request.Gender.Trim()
            : (!string.IsNullOrWhiteSpace(parsedNric.Gender) ? parsedNric.Gender : target.Gender);
        target.Phone = string.IsNullOrWhiteSpace(request.Phone) ? target.Phone : request.Phone.Trim();
        target.Address = string.IsNullOrWhiteSpace(request.Address) ? target.Address : request.Address.Trim();
        target.PrimaryBranchId = ClinicDomainHelpers.NormalizeBranch(request.PrimaryBranchId, _clinicOptions.DefaultBranchId);

        if (request.MedicalAlerts is not null)
        {
            target.MedicalAlerts = new MedicalAlertDto
            {
                Allergies = request.MedicalAlerts.Allergies?.Trim() ?? string.Empty,
                OnAnticoagulants = request.MedicalAlerts.OnAnticoagulants,
                HasDiabetes = request.MedicalAlerts.HasDiabetes,
                IsPregnant = request.MedicalAlerts.IsPregnant,
                Notes = request.MedicalAlerts.Notes?.Trim() ?? string.Empty
            };
        }

        if (forceMyKadVerified || request.IsMyKadVerified)
        {
            target.IsMyKadVerified = true;
        }

        if (request.ConsentSignedAt.HasValue)
        {
            target.ConsentSignedAt = request.ConsentSignedAt;
        }

        _patients[target.Id] = target;

        // Keep queue cards synchronized with patient demographics & medical alert banners.
        foreach (var appt in _appointments.Values.Where(a => a.PatientId == target.Id))
        {
            appt.PatientName = target.FullName;
            appt.PatientIc = target.IcNumber;
            appt.HasCriticalMedicalAlert = target.MedicalAlerts.HasAnyCriticalAlert;
            appt.AlertSummary = target.MedicalAlerts.BuildAlertSummary();
        }

        AppendAudit(
            target.PrimaryBranchId,
            claims.UserId,
            forceMyKadVerified ? "PATIENT_MYKAD_UPSERT" : (isNew ? "PATIENT_CREATED" : "PATIENT_UPDATED"),
            "Patient",
            target.Id.ToString(),
            oldSnapshot,
            $"{target.FullName} ({target.IcNumber}); MyKadVerified={target.IsMyKadVerified}");

        return target;
    }

    public Task<PatientDto?> UpdateMedicalAlertsAsync(Guid patientId, MedicalAlertDto alerts, string branchId, CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            if (!_patients.TryGetValue(patientId, out var patient))
            {
                return Task.FromResult<PatientDto?>(null);
            }

            var oldSummary = patient.MedicalAlerts.BuildAlertSummary();
            patient.MedicalAlerts = new MedicalAlertDto
            {
                Allergies = alerts.Allergies?.Trim() ?? string.Empty,
                OnAnticoagulants = alerts.OnAnticoagulants,
                HasDiabetes = alerts.HasDiabetes,
                IsPregnant = alerts.IsPregnant,
                Notes = alerts.Notes?.Trim() ?? string.Empty
            };

            var newSummary = patient.MedicalAlerts.BuildAlertSummary();
            var hasCritical = patient.MedicalAlerts.HasAnyCriticalAlert;

            foreach (var appt in _appointments.Values.Where(a => a.PatientId == patientId))
            {
                appt.HasCriticalMedicalAlert = hasCritical;
                appt.AlertSummary = newSummary;
            }

            AppendAudit(
                ClinicDomainHelpers.NormalizeBranch(branchId, patient.PrimaryBranchId),
                claims.UserId,
                "MEDICAL_ALERTS_UPDATED",
                "Patient",
                patientId.ToString(),
                oldSummary,
                newSummary);

            return Task.FromResult<PatientDto?>(ClinicDomainHelpers.Clone(patient));
        }
    }

    public Task<PatientDto?> SignConsentAsync(Guid patientId, SignConsentRequest request, string branchId, CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            if (!_patients.TryGetValue(patientId, out var patient))
            {
                return Task.FromResult<PatientDto?>(null);
            }

            var oldValue = patient.ConsentSignedAt?.ToString("O");
            patient.ConsentSignedAt = request.SignedAt ?? DateTimeOffset.UtcNow;

            AppendAudit(
                ClinicDomainHelpers.NormalizeBranch(branchId, patient.PrimaryBranchId),
                string.IsNullOrWhiteSpace(request.SignedBy) ? claims.UserId : request.SignedBy.Trim(),
                "CONSENT_SIGNED",
                "Patient",
                patientId.ToString(),
                oldValue,
                patient.ConsentSignedAt.Value.ToString("O"));

            return Task.FromResult<PatientDto?>(ClinicDomainHelpers.Clone(patient));
        }
    }

    public Task<IReadOnlyList<DentistDto>> GetDentistsAsync(string? branchId = null, CancellationToken ct = default)
    {
        lock (_sync)
        {
            IEnumerable<DentistDto> query = _dentists.Where(d => d.IsActive);
            if (!string.IsNullOrWhiteSpace(branchId))
            {
                var branch = ClinicDomainHelpers.NormalizeBranch(branchId, _clinicOptions.DefaultBranchId);
                query = query.Where(d => d.BranchIds.Count == 0 || d.BranchIds.Contains(branch, StringComparer.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyList<DentistDto>>(
                query.OrderBy(d => d.FullName, StringComparer.OrdinalIgnoreCase)
                    .Select(ClinicDomainHelpers.Clone)
                    .ToList());
        }
    }

    public Task<IReadOnlyList<TreatmentDto>> GetTreatmentsAsync(CancellationToken ct = default)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<TreatmentDto>>(
                _treatments.Where(t => t.IsActive)
                    .OrderBy(t => t.Category, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(ClinicDomainHelpers.Clone)
                    .ToList());
        }
    }

    public Task<IReadOnlyList<AppointmentQueueItemDto>> GetAppointmentsAsync(string? branchId = null, CancellationToken ct = default) =>
        GetAppointmentsAsync(branchId, null, null, null, null, ct);

    public Task<IReadOnlyList<AppointmentQueueItemDto>> GetAppointmentsAsync(
        string? branchId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? dentistId,
        Guid? treatmentId,
        CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            IEnumerable<AppointmentQueueItemDto> query = _appointments.Values
                .Where(a => claims.CanAccessBranch(a.BranchId));

            if (!string.IsNullOrWhiteSpace(branchId))
            {
                var branch = ClinicDomainHelpers.NormalizeBranch(branchId, _clinicOptions.DefaultBranchId);
                query = query.Where(a => string.Equals(a.BranchId, branch, StringComparison.OrdinalIgnoreCase));
            }

            if (from.HasValue)
            {
                query = query.Where(a => a.ScheduledAt >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(a => a.ScheduledAt <= to.Value);
            }

            if (dentistId.HasValue)
            {
                var dentistName = _dentists.FirstOrDefault(d => d.Id == dentistId.Value)?.FullName;
                query = query.Where(a =>
                    a.DentistId == dentistId.Value
                    || (!a.DentistId.HasValue && !string.IsNullOrWhiteSpace(dentistName)
                        && string.Equals(a.DoctorName, dentistName, StringComparison.OrdinalIgnoreCase)));
            }

            if (treatmentId.HasValue)
            {
                var treatmentName = _treatments.FirstOrDefault(t => t.Id == treatmentId.Value)?.Name;
                query = query.Where(a =>
                    a.TreatmentId == treatmentId.Value
                    || (!a.TreatmentId.HasValue && !string.IsNullOrWhiteSpace(treatmentName)
                        && a.ProcedureSummary.Contains(treatmentName, StringComparison.OrdinalIgnoreCase)));
            }

            return Task.FromResult<IReadOnlyList<AppointmentQueueItemDto>>(
                query.OrderBy(a => a.ScheduledAt)
                    .Select(ClinicDomainHelpers.Clone)
                    .ToList());
        }
    }

    public Task<AppointmentQueueItemDto?> GetAppointmentByIdAsync(Guid appointmentId, CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            if (!_appointments.TryGetValue(appointmentId, out var item) || !claims.CanAccessBranch(item.BranchId))
            {
                return Task.FromResult<AppointmentQueueItemDto?>(null);
            }

            return Task.FromResult<AppointmentQueueItemDto?>(ClinicDomainHelpers.Clone(item));
        }
    }

    public Task<AppointmentQueueItemDto> CreateAppointmentAsync(CreateAppointmentRequest request, CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            var item = CreateAppointmentLocked(request, claims);
            return Task.FromResult(ClinicDomainHelpers.Clone(item));
        }
    }

    public Task<RegisterPatientAndAppointmentResponse> RegisterPatientAndCreateAppointmentAsync(
        RegisterPatientAndAppointmentRequest request,
        CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            var patientReq = request.Patient ?? new UpsertPatientRequest();
            if (string.IsNullOrWhiteSpace(patientReq.PrimaryBranchId))
            {
                patientReq.PrimaryBranchId = request.Appointment.BranchId;
            }

            var patient = UpsertPatientLocked(patientReq, forceMyKadVerified: false, claims);
            var apptReq = request.Appointment ?? new CreateAppointmentRequest();
            apptReq.PatientId = patient.Id;
            if (string.IsNullOrWhiteSpace(apptReq.BranchId))
            {
                apptReq.BranchId = patient.PrimaryBranchId;
            }

            var appointment = CreateAppointmentLocked(apptReq, claims);
            return Task.FromResult(new RegisterPatientAndAppointmentResponse
            {
                Patient = ClinicDomainHelpers.Clone(patient),
                Appointment = ClinicDomainHelpers.Clone(appointment)
            });
        }
    }

    private AppointmentQueueItemDto CreateAppointmentLocked(CreateAppointmentRequest request, RequestClaimsContext claims)
    {
        _patients.TryGetValue(request.PatientId, out var patient);
        var branch = ClinicDomainHelpers.NormalizeBranch(request.BranchId, _clinicOptions.DefaultBranchId);
        var now = DateTimeOffset.UtcNow;

        var dentist = ResolveDentist(request.DentistId, request.DoctorName);
        var treatment = ResolveTreatment(request.TreatmentId, request.ProcedureSummary);

        var doctorName = !string.IsNullOrWhiteSpace(request.DoctorName)
            ? request.DoctorName.Trim()
            : (dentist?.FullName ?? "Dr. Aina Rahman");
        var roomName = !string.IsNullOrWhiteSpace(request.RoomName)
            ? request.RoomName.Trim()
            : (dentist?.DefaultRoom ?? "Suite 1");
        var procedureSummary = !string.IsNullOrWhiteSpace(request.ProcedureSummary)
            ? request.ProcedureSummary.Trim()
            : (treatment?.Name ?? "Consultation");
        var duration = request.DurationMinutes > 0
            ? Math.Clamp(request.DurationMinutes, 15, 480)
            : Math.Clamp(treatment?.DefaultDurationMinutes ?? 30, 15, 480);

        var normalizedStatus = AppointmentCalendarHelper.NormalizeQueueStatus(request.Status);
        var visitType = normalizedStatus == QueueStatus.AiBookedPending
            ? VisitType.AiBooked
            : request.VisitType;
        var actor = !string.IsNullOrWhiteSpace(request.CreatedBy)
            ? request.CreatedBy.Trim()
            : claims.UserId;

        DateTimeOffset? arrivedAt = request.ArrivedAt;
        var checkedInBy = string.Empty;
        if (visitType == VisitType.WalkIn || normalizedStatus is QueueStatus.Waiting or QueueStatus.InChair)
        {
            arrivedAt ??= now;
            checkedInBy = actor;
        }

        DateTimeOffset? seatedAt = normalizedStatus == QueueStatus.InChair ? now : null;
        var seatedBy = normalizedStatus == QueueStatus.InChair ? actor : string.Empty;

        var initialAction = visitType switch
        {
            VisitType.WalkIn => "Walk-In Registered",
            VisitType.AiBooked => "AI Booking Created",
            _ => "Appointment Booked"
        };

        var item = new AppointmentQueueItemDto
        {
            Id = Guid.NewGuid(),
            BranchId = branch,
            PatientId = request.PatientId,
            PatientName = patient?.FullName ?? "Walk-In Patient",
            PatientIc = patient?.IcNumber ?? string.Empty,
            DentistId = request.DentistId ?? dentist?.Id,
            DoctorName = doctorName,
            TreatmentId = request.TreatmentId ?? treatment?.Id,
            RoomName = roomName,
            ScheduledAt = request.ScheduledAt == default ? now : request.ScheduledAt,
            DurationMinutes = duration,
            ProcedureSummary = procedureSummary,
            Notes = request.Notes?.Trim() ?? string.Empty,
            Status = normalizedStatus,
            VisitType = visitType,
            ArrivedAt = arrivedAt,
            SeatedAt = seatedAt,
            CreatedBy = actor,
            CheckedInBy = checkedInBy,
            SeatedBy = seatedBy,
            LastUpdatedBy = actor,
            LastUpdatedAt = now,
            ActivityLog = new List<VisitActivityEntryDto>
            {
                new()
                {
                    Timestamp = now,
                    Action = initialAction,
                    StaffName = actor,
                    Details = $"{doctorName} · {roomName} · {procedureSummary}"
                }
            },
            HasCriticalMedicalAlert = patient?.MedicalAlerts.HasAnyCriticalAlert ?? false,
            AlertSummary = patient?.MedicalAlerts.BuildAlertSummary() ?? "No Critical Alerts"
        };

        _appointments[item.Id] = item;

        AppendAudit(
            branch,
            actor,
            "APPOINTMENT_CREATED",
            "Appointment",
            item.Id.ToString(),
            null,
            $"{item.PatientName} | {item.VisitType} | {item.DoctorName} | {item.ProcedureSummary} | {item.DurationMinutes}m | {item.Status}");

        return item;
    }

    public Task<(AppointmentQueueItemDto? Item, string? Error)> UpdateAppointmentAsync(
        Guid appointmentId,
        UpdateAppointmentRequest request,
        CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            if (!_appointments.TryGetValue(appointmentId, out var item) || !claims.CanAccessBranch(item.BranchId))
            {
                return Task.FromResult<(AppointmentQueueItemDto?, string?)>((null, "Appointment not found."));
            }

            var now = DateTimeOffset.UtcNow;
            var oldStatus = item.Status;
            var oldSummary = $"{item.PatientName} | {item.DoctorName} | {item.ProcedureSummary} | {item.ScheduledAt:O} | {item.DurationMinutes}m | {item.Status}";

            if (request.PatientId != Guid.Empty && _patients.TryGetValue(request.PatientId, out var patient))
            {
                item.PatientId = patient.Id;
                item.PatientName = patient.FullName;
                item.PatientIc = patient.IcNumber;
                item.HasCriticalMedicalAlert = patient.MedicalAlerts.HasAnyCriticalAlert;
                item.AlertSummary = patient.MedicalAlerts.BuildAlertSummary();
            }

            var dentist = ResolveDentist(request.DentistId, request.DoctorName);
            var treatment = ResolveTreatment(request.TreatmentId, request.ProcedureSummary);

            item.DentistId = request.DentistId ?? dentist?.Id;
            if (!string.IsNullOrWhiteSpace(request.DoctorName))
            {
                item.DoctorName = request.DoctorName.Trim();
            }
            else if (dentist is not null)
            {
                item.DoctorName = dentist.FullName;
            }

            item.TreatmentId = request.TreatmentId ?? treatment?.Id;
            if (!string.IsNullOrWhiteSpace(request.ProcedureSummary))
            {
                item.ProcedureSummary = request.ProcedureSummary.Trim();
            }
            else if (treatment is not null)
            {
                item.ProcedureSummary = treatment.Name;
            }

            if (!string.IsNullOrWhiteSpace(request.RoomName))
            {
                item.RoomName = request.RoomName.Trim();
            }
            else if (dentist is not null && string.IsNullOrWhiteSpace(item.RoomName))
            {
                item.RoomName = dentist.DefaultRoom;
            }

            if (request.ScheduledAt != default)
            {
                item.ScheduledAt = request.ScheduledAt;
            }

            if (request.DurationMinutes > 0)
            {
                item.DurationMinutes = Math.Clamp(request.DurationMinutes, 15, 480);
            }

            if (request.VisitType.HasValue)
            {
                item.VisitType = request.VisitType.Value;
            }

            item.Notes = request.Notes?.Trim() ?? string.Empty;

            var actor = string.IsNullOrWhiteSpace(request.UpdatedBy) ? claims.UserId : request.UpdatedBy.Trim();
            var normalizedTargetStatus = AppointmentCalendarHelper.NormalizeQueueStatus(request.Status);
            if (AppointmentCalendarHelper.NormalizeQueueStatus(oldStatus) != normalizedTargetStatus)
            {
                ClinicDomainHelpers.ApplyStatusMilestone(item, oldStatus, normalizedTargetStatus, actor, now, item.RoomName);
            }
            else
            {
                item.Status = normalizedTargetStatus;
                item.LastUpdatedBy = actor;
                item.LastUpdatedAt = now;
                item.ActivityLog.Add(new VisitActivityEntryDto
                {
                    Timestamp = now,
                    Action = "Appointment Updated",
                    StaffName = actor,
                    Details = $"{item.DoctorName} · {item.RoomName} · {item.ScheduledAt.ToLocalTime():dd MMM HH:mm}"
                });
            }

            var newSummary = $"{item.PatientName} | {item.DoctorName} | {item.ProcedureSummary} | {item.ScheduledAt:O} | {item.DurationMinutes}m | {item.Status}";
            AppendAudit(
                item.BranchId,
                actor,
                "APPOINTMENT_UPDATED",
                "Appointment",
                item.Id.ToString(),
                oldSummary,
                newSummary);

            return Task.FromResult<(AppointmentQueueItemDto?, string?)>((ClinicDomainHelpers.Clone(item), null));
        }
    }

    public Task<(AppointmentQueueItemDto? Item, string? Error)> UpdateQueueStatusAsync(
        Guid appointmentId,
        UpdateQueueStatusRequest request,
        CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            if (!_appointments.TryGetValue(appointmentId, out var item) || !claims.CanAccessBranch(item.BranchId))
            {
                return Task.FromResult<(AppointmentQueueItemDto?, string?)>((null, "Appointment not found."));
            }

            var normalizedNew = AppointmentCalendarHelper.NormalizeQueueStatus(request.NewStatus);
            if (!ClinicDomainHelpers.IsTransitionAllowed(item.Status, normalizedNew))
            {
                return Task.FromResult<(AppointmentQueueItemDto?, string?)>(
                    (null, $"Invalid queue transition from {item.Status} to {normalizedNew}."));
            }

            var oldStatus = item.Status;
            if (!string.IsNullOrWhiteSpace(request.RoomName))
            {
                item.RoomName = request.RoomName.Trim();
            }

            var actor = string.IsNullOrWhiteSpace(request.UpdatedBy) ? claims.UserId : request.UpdatedBy.Trim();
            ClinicDomainHelpers.ApplyStatusMilestone(item, oldStatus, normalizedNew, actor, DateTimeOffset.UtcNow, request.RoomName);

            AppendAudit(
                item.BranchId,
                actor,
                "QUEUE_STATUS_CHANGED",
                "Appointment",
                item.Id.ToString(),
                oldStatus.ToString(),
                item.Status.ToString());

            return Task.FromResult<(AppointmentQueueItemDto?, string?)>((ClinicDomainHelpers.Clone(item), null));
        }
    }

    public Task<IReadOnlyList<OdontogramToothDto>> GetOdontogramAsync(Guid patientId, CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (!_patients.ContainsKey(patientId))
            {
                return Task.FromResult<IReadOnlyList<OdontogramToothDto>>(Array.Empty<OdontogramToothDto>());
            }

            var result = new List<OdontogramToothDto>(ClinicDomainHelpers.AllFdiTeeth.Length);
            foreach (var toothNum in ClinicDomainHelpers.AllFdiTeeth)
            {
                if (_odontogram.TryGetValue((patientId, toothNum), out var existing))
                {
                    result.Add(ClinicDomainHelpers.Clone(existing));
                }
                else
                {
                    result.Add(new OdontogramToothDto
                    {
                        Id = Guid.Empty,
                        PatientId = patientId,
                        ToothNumber = toothNum,
                        Condition = ToothCondition.Healthy,
                        ProcedureName = string.Empty,
                        Fee = 0m,
                        Stage = TreatmentStage.Proposed,
                        DoctorName = string.Empty,
                        UpdatedAt = DateTimeOffset.UtcNow
                    });
                }
            }

            return Task.FromResult<IReadOnlyList<OdontogramToothDto>>(result);
        }
    }

    public Task<(OdontogramToothDto? Tooth, InvoiceDto? UpdatedInvoice, string? Error)> UpdateOdontogramAsync(
        Guid patientId,
        UpdateOdontogramRequest request,
        CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            if (!_patients.TryGetValue(patientId, out var patient))
            {
                return Task.FromResult<(OdontogramToothDto?, InvoiceDto?, string?)>((null, null, "Patient not found."));
            }

            if (!ClinicDomainHelpers.IsValidFdiTooth(request.ToothNumber))
            {
                return Task.FromResult<(OdontogramToothDto?, InvoiceDto?, string?)>(
                    (null, null, $"Invalid FDI tooth number '{request.ToothNumber}'. Expected 11-18, 21-28, 31-38, or 41-48."));
            }

            if (request.Fee < 0m)
            {
                return Task.FromResult<(OdontogramToothDto?, InvoiceDto?, string?)>(
                    (null, null, "Procedure fee cannot be negative."));
            }

            var key = (patientId, request.ToothNumber);
            var hasExisting = _odontogram.TryGetValue(key, out var tooth);
            var previousStage = hasExisting ? tooth!.Stage : TreatmentStage.Proposed;

            if (!hasExisting || tooth is null)
            {
                tooth = new OdontogramToothDto
                {
                    Id = Guid.NewGuid(),
                    PatientId = patientId,
                    ToothNumber = request.ToothNumber
                };
                _odontogram[key] = tooth;
            }

            tooth.Condition = request.Condition;
            tooth.ProcedureName = request.ProcedureName?.Trim() ?? string.Empty;
            tooth.Fee = BillingCalculator.Round2(request.Fee);
            tooth.Stage = request.Stage;
            tooth.DoctorName = string.IsNullOrWhiteSpace(request.DoctorName) ? "Dr. Aina Rahman" : request.DoctorName.Trim();
            tooth.UpdatedAt = DateTimeOffset.UtcNow;

            var branchId = ClinicDomainHelpers.NormalizeBranch(request.BranchId, patient.PrimaryBranchId);
            InvoiceDto? updatedInvoice = null;

            // CRITICAL INVARIANT: Transitioning a tooth treatment stage to Completed
            // automatically adds/updates the procedure line item on an active draft invoice.
            if (request.Stage == TreatmentStage.Completed)
            {
                var draftInvoice = _invoices.Values
                    .Where(i => i.PatientId == patientId
                             && string.Equals(i.BranchId, branchId, StringComparison.OrdinalIgnoreCase)
                             && !i.IsVoided
                             && i.Payments.Count == 0)
                    .OrderByDescending(i => i.CreatedAt)
                    .FirstOrDefault();

                if (draftInvoice is null)
                {
                    var linkedAppointment = _appointments.Values
                        .Where(a => a.PatientId == patientId
                                 && string.Equals(a.BranchId, branchId, StringComparison.OrdinalIgnoreCase)
                                 && a.Status != QueueStatus.Completed
                                 && a.Status != QueueStatus.Cancelled)
                        .OrderByDescending(a => a.ScheduledAt)
                        .FirstOrDefault();

                    draftInvoice = new InvoiceDto
                    {
                        Id = Guid.NewGuid(),
                        InvoiceNumber = NextInvoiceNumber(branchId),
                        BranchId = branchId,
                        PatientId = patientId,
                        PatientName = patient.FullName,
                        DoctorName = tooth.DoctorName,
                        AppointmentId = linkedAppointment?.Id,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    _invoices[draftInvoice.Id] = draftInvoice;
                }

                var label = ClinicDomainHelpers.BuildCompletedToothProcedureLabel(request);
                var existingLine = draftInvoice.Lines.FirstOrDefault(l =>
                    l.ToothNumber == request.ToothNumber && l.SourceStage == TreatmentStage.Completed);

                if (existingLine is not null)
                {
                    existingLine.ProcedureName = label;
                    existingLine.UnitPrice = tooth.Fee;
                    existingLine.Quantity = 1;
                    existingLine.LineTotal = BillingCalculator.ComputeLineTotal(tooth.Fee, 1);
                }
                else
                {
                    draftInvoice.Lines.Add(new InvoiceLineDto
                    {
                        Id = Guid.NewGuid(),
                        ToothNumber = request.ToothNumber,
                        ProcedureName = label,
                        UnitPrice = tooth.Fee,
                        Quantity = 1,
                        LineTotal = BillingCalculator.ComputeLineTotal(tooth.Fee, 1),
                        SourceStage = TreatmentStage.Completed
                    });
                }

                BillingCalculator.RecalculateInvoice(draftInvoice, _clinicOptions.CommissionRate);
                updatedInvoice = ClinicDomainHelpers.Clone(draftInvoice);
            }

            AppendAudit(
                branchId,
                claims.UserId,
                "ODONTOGRAM_UPDATED",
                "OdontogramTooth",
                $"{patientId}:{request.ToothNumber}",
                previousStage.ToString(),
                $"{tooth.Condition} | {tooth.ProcedureName} | {tooth.Stage} | RM{tooth.Fee:F2}");

            return Task.FromResult<(OdontogramToothDto?, InvoiceDto?, string?)>(
                (ClinicDomainHelpers.Clone(tooth), updatedInvoice, null));
        }
    }

    public Task<IReadOnlyList<SoapNoteDto>> GetSoapNotesAsync(Guid patientId, CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (!_soapNotes.TryGetValue(patientId, out var list))
            {
                return Task.FromResult<IReadOnlyList<SoapNoteDto>>(Array.Empty<SoapNoteDto>());
            }

            return Task.FromResult<IReadOnlyList<SoapNoteDto>>(
                list.OrderByDescending(n => n.VisitDate)
                    .Select(ClinicDomainHelpers.Clone)
                    .ToList());
        }
    }

    public Task<SoapNoteDto?> CreateSoapNoteAsync(Guid patientId, CreateSoapNoteRequest request, CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            if (!_patients.TryGetValue(patientId, out var patient))
            {
                return Task.FromResult<SoapNoteDto?>(null);
            }

            var branch = ClinicDomainHelpers.NormalizeBranch(request.BranchId, patient.PrimaryBranchId);
            var note = new SoapNoteDto
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                BranchId = branch,
                DoctorName = string.IsNullOrWhiteSpace(request.DoctorName) ? "Dr. Aina Rahman" : request.DoctorName.Trim(),
                VisitDate = request.VisitDate ?? DateTimeOffset.UtcNow,
                Subjective = request.Subjective?.Trim() ?? string.Empty,
                Objective = request.Objective?.Trim() ?? string.Empty,
                Assessment = request.Assessment?.Trim() ?? string.Empty,
                Plan = request.Plan?.Trim() ?? string.Empty,
                AnesthesiaUsed = request.AnesthesiaUsed?.Trim() ?? string.Empty,
                ToothShade = request.ToothShade?.Trim() ?? string.Empty,
                PostOpInstructions = request.PostOpInstructions?.Trim() ?? string.Empty
            };

            if (!_soapNotes.TryGetValue(patientId, out var list))
            {
                list = new List<SoapNoteDto>();
                _soapNotes[patientId] = list;
            }

            list.Add(note);

            AppendAudit(
                branch,
                claims.UserId,
                "SOAP_NOTE_CREATED",
                "SoapNote",
                note.Id.ToString(),
                null,
                $"{patient.FullName} | {note.Assessment}");

            return Task.FromResult<SoapNoteDto?>(ClinicDomainHelpers.Clone(note));
        }
    }

    public Task<IReadOnlyList<SoapMacroDto>> GetSoapMacrosAsync(CancellationToken ct = default)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<SoapMacroDto>>(_soapMacros.Select(CloneMacro).ToList());
        }
    }

    public Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(string? branchId = null, CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            IEnumerable<InvoiceDto> query = _invoices.Values
                .Where(i => claims.CanAccessBranch(i.BranchId));

            if (!string.IsNullOrWhiteSpace(branchId))
            {
                var branch = ClinicDomainHelpers.NormalizeBranch(branchId, _clinicOptions.DefaultBranchId);
                query = query.Where(i => string.Equals(i.BranchId, branch, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyList<InvoiceDto>>(
                query.OrderByDescending(i => i.CreatedAt)
                    .Select(ClinicDomainHelpers.Clone)
                    .ToList());
        }
    }

    public Task<InvoiceDto?> GetInvoiceByIdAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            if (!_invoices.TryGetValue(invoiceId, out var invoice) || !claims.CanAccessBranch(invoice.BranchId))
            {
                return Task.FromResult<InvoiceDto?>(null);
            }

            return Task.FromResult<InvoiceDto?>(ClinicDomainHelpers.Clone(invoice));
        }
    }

    public Task<(InvoiceDto? Invoice, AppointmentQueueItemDto? UpdatedQueueItem, string? Error)> ApplyInvoicePaymentAsync(
        Guid invoiceId,
        ApplyPaymentRequest request,
        CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            if (!_invoices.TryGetValue(invoiceId, out var invoice) || !claims.CanAccessBranch(invoice.BranchId))
            {
                return Task.FromResult<(InvoiceDto?, AppointmentQueueItemDto?, string?)>((null, null, "Invoice not found."));
            }

            var cloneForValidation = ClinicDomainHelpers.Clone(invoice);
            var validation = BillingCalculator.ValidateAndApplyPayments(cloneForValidation, request);
            if (!validation.IsValid)
            {
                return Task.FromResult<(InvoiceDto?, AppointmentQueueItemDto?, string?)>((null, null, validation.ErrorMessage));
            }

            _invoices[invoiceId] = validation.Invoice;

            AppointmentQueueItemDto? updatedQueue = null;
            if (validation.Invoice.BalanceDue == 0m)
            {
                AppointmentQueueItemDto? linkedAppt = null;
                if (validation.Invoice.AppointmentId.HasValue)
                {
                    _appointments.TryGetValue(validation.Invoice.AppointmentId.Value, out linkedAppt);
                }

                linkedAppt ??= _appointments.Values
                    .Where(a => a.PatientId == validation.Invoice.PatientId
                             && string.Equals(a.BranchId, validation.Invoice.BranchId, StringComparison.OrdinalIgnoreCase)
                             && a.Status != QueueStatus.Completed
                             && a.Status != QueueStatus.Cancelled)
                    .OrderByDescending(a => a.ScheduledAt)
                    .FirstOrDefault();

                if (linkedAppt is not null && linkedAppt.Status != QueueStatus.Completed && linkedAppt.Status != QueueStatus.Cancelled)
                {
                    var actor = string.IsNullOrWhiteSpace(request.CollectedBy) ? claims.UserId : request.CollectedBy.Trim();
                    ClinicDomainHelpers.ApplyStatusMilestone(
                        linkedAppt,
                        linkedAppt.Status,
                        QueueStatus.Completed,
                        actor,
                        DateTimeOffset.UtcNow,
                        $"Invoice {validation.Invoice.InvoiceNumber}");
                    updatedQueue = ClinicDomainHelpers.Clone(linkedAppt);
                }
            }

            AppendAudit(
                validation.Invoice.BranchId,
                string.IsNullOrWhiteSpace(request.CollectedBy) ? claims.UserId : request.CollectedBy.Trim(),
                "INVOICE_PAYMENT_APPLIED",
                "Invoice",
                validation.Invoice.InvoiceNumber,
                null,
                $"Paid=RM{validation.Invoice.PaidAmount:F2}; Panel=RM{validation.Invoice.PanelCoveredAmount:F2}; BalanceDue=RM{validation.Invoice.BalanceDue:F2}; Commission=RM{validation.Invoice.DoctorCommissionAmount:F2}");

            return Task.FromResult<(InvoiceDto?, AppointmentQueueItemDto?, string?)>(
                (ClinicDomainHelpers.Clone(validation.Invoice), updatedQueue, null));
        }
    }

    public Task<(InvoiceDto? Invoice, string? Error)> VoidInvoiceAsync(
        Guid invoiceId,
        VoidInvoiceRequest request,
        CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            if (!_invoices.TryGetValue(invoiceId, out var invoice) || !claims.CanAccessBranch(invoice.BranchId))
            {
                return Task.FromResult<(InvoiceDto?, string?)>((null, "Invoice not found."));
            }

            if (string.IsNullOrWhiteSpace(request.VoidReason))
            {
                return Task.FromResult<(InvoiceDto?, string?)>((null, "VoidReason is required to void an invoice."));
            }

            if (invoice.IsVoided)
            {
                return Task.FromResult<(InvoiceDto?, string?)>((null, "Invoice is already voided."));
            }

            var oldValue = $"Subtotal=RM{invoice.Subtotal:F2}; Paid=RM{invoice.PaidAmount:F2}; IsVoided=false";
            invoice.IsVoided = true;
            invoice.VoidReason = request.VoidReason.Trim();
            invoice.DoctorCommissionAmount = 0m;

            var voidedBy = string.IsNullOrWhiteSpace(request.VoidedBy) ? claims.UserId : request.VoidedBy.Trim();
            AppendAudit(
                invoice.BranchId,
                voidedBy,
                "INVOICE_VOIDED",
                "Invoice",
                invoice.InvoiceNumber,
                oldValue,
                $"IsVoided=true; Reason={invoice.VoidReason}; VoidedBy={voidedBy}");

            return Task.FromResult<(InvoiceDto?, string?)>((ClinicDomainHelpers.Clone(invoice), null));
        }
    }

    public Task<DailySalesReconciliationDto> GetDailySalesSummaryAsync(string branchId, DateOnly date, CancellationToken ct = default)
    {
        var branch = ClinicDomainHelpers.NormalizeBranch(branchId, _clinicOptions.DefaultBranchId);
        var claims = CurrentClaims;

        lock (_sync)
        {
            if (_reconciliations.TryGetValue((branch, date), out var saved))
            {
                return Task.FromResult(saved);
            }

            var invoices = claims.CanAccessBranch(branch)
                ? _invoices.Values.Where(i => string.Equals(i.BranchId, branch, StringComparison.OrdinalIgnoreCase)).ToList()
                : new List<InvoiceDto>();

            return Task.FromResult(BillingCalculator.CalculateDailySalesReconciliation(branch, date, invoices, claims.UserId));
        }
    }

    public Task<DailySalesReconciliationDto> ReconcileDailySalesAsync(ReconcileDailySalesRequest request, CancellationToken ct = default)
    {
        var branch = ClinicDomainHelpers.NormalizeBranch(request.BranchId, _clinicOptions.DefaultBranchId);
        var claims = CurrentClaims;
        var reconciledBy = string.IsNullOrWhiteSpace(request.ReconciledBy) ? claims.UserId : request.ReconciledBy.Trim();

        lock (_sync)
        {
            var invoices = claims.CanAccessBranch(branch)
                ? _invoices.Values.Where(i => string.Equals(i.BranchId, branch, StringComparison.OrdinalIgnoreCase)).ToList()
                : new List<InvoiceDto>();

            var summary = BillingCalculator.CalculateDailySalesReconciliation(branch, request.Date, invoices, reconciledBy);
            _reconciliations[(branch, request.Date)] = summary;

            AppendAudit(
                branch,
                reconciledBy,
                "DAILY_SALES_RECONCILED",
                "DailySalesReconciliation",
                $"{branch}:{request.Date:yyyy-MM-dd}",
                null,
                $"Invoices={summary.TotalInvoicesCount}; Voided={summary.VoidedInvoicesCount}; Net=RM{summary.NetCollected:F2}; Panel=RM{summary.TotalPanelClaims:F2}; Balanced={summary.IsBalanced}");

            return Task.FromResult(summary);
        }
    }

    public Task<IReadOnlyList<AuditLogDto>> GetAuditLogsAsync(string? branchId = null, CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            IEnumerable<AuditLogDto> query = _auditLogs.Where(a => claims.CanAccessBranch(a.BranchId));
            if (!string.IsNullOrWhiteSpace(branchId))
            {
                var branch = ClinicDomainHelpers.NormalizeBranch(branchId, _clinicOptions.DefaultBranchId);
                query = query.Where(a => string.Equals(a.BranchId, branch, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyList<AuditLogDto>>(
                query.OrderByDescending(a => a.Timestamp).ToList());
        }
    }

    public Task<IReadOnlyList<BackupVerificationDto>> GetBackupVerificationsAsync(CancellationToken ct = default)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<BackupVerificationDto>>(
                _backups.OrderByDescending(b => b.VerifiedAt).ToList());
        }
    }

    public Task<BackupVerificationDto> RecordBackupVerificationAsync(BackupVerificationDto verification, CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            _backups.Add(verification);
            AppendAudit(
                _clinicOptions.DefaultBranchId,
                string.IsNullOrWhiteSpace(verification.VerifiedBy) ? claims.UserId : verification.VerifiedBy,
                "BACKUP_VERIFIED",
                "BackupVerification",
                verification.Id.ToString(),
                null,
                $"{verification.FileName} | SHA256={verification.Sha256Checksum} | RestorePassed={verification.RestoreTestPassed}");

            return Task.FromResult(verification);
        }
    }

    public Task<IReadOnlyList<WhatsAppThreadDto>> GetWhatsAppThreadsAsync(string? branchId = null, CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            IEnumerable<(string BranchId, WhatsAppThreadDto Thread)> query = _whatsAppThreads.Values
                .Where(t => claims.CanAccessBranch(t.BranchId));

            if (!string.IsNullOrWhiteSpace(branchId))
            {
                var branch = ClinicDomainHelpers.NormalizeBranch(branchId, _clinicOptions.DefaultBranchId);
                query = query.Where(t => string.Equals(t.BranchId, branch, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyList<WhatsAppThreadDto>>(
                query.OrderByDescending(t => t.Thread.LastUpdatedAt)
                    .Select(t => ClinicDomainHelpers.Clone(t.Thread))
                    .ToList());
        }
    }

    public Task<WhatsAppThreadDto?> GetWhatsAppThreadAsync(string threadId, CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            if (_whatsAppThreads.TryGetValue(threadId, out var entry) && claims.CanAccessBranch(entry.BranchId))
            {
                return Task.FromResult<WhatsAppThreadDto?>(ClinicDomainHelpers.Clone(entry.Thread));
            }

            return Task.FromResult<WhatsAppThreadDto?>(null);
        }
    }

    public Task<WhatsAppThreadDto> AppendWhatsAppMessageAsync(
        string threadId,
        string branchId,
        string patientPhone,
        string patientName,
        WhatsAppMessageDto message,
        bool? setEmergencyEscalated = null,
        string? emergencyReason = null,
        CancellationToken ct = default)
    {
        var normalizedBranch = ClinicDomainHelpers.NormalizeBranch(branchId, _clinicOptions.DefaultBranchId);
        lock (_sync)
        {
            if (!_whatsAppThreads.TryGetValue(threadId, out var entry))
            {
                var newThread = new WhatsAppThreadDto
                {
                    ThreadId = threadId,
                    PatientPhone = patientPhone,
                    PatientName = string.IsNullOrWhiteSpace(patientName) ? patientPhone : patientName.Trim(),
                    LastUpdatedAt = message.Timestamp
                };
                entry = (normalizedBranch, newThread);
                _whatsAppThreads[threadId] = entry;
            }
            else if (!string.IsNullOrWhiteSpace(patientName) && entry.Thread.PatientName == entry.Thread.PatientPhone)
            {
                entry.Thread.PatientName = patientName.Trim();
            }

            entry.Thread.Messages.Add(message);
            entry.Thread.LastUpdatedAt = message.Timestamp;

            if (setEmergencyEscalated == true)
            {
                entry.Thread.IsEmergencyEscalated = true;
                entry.Thread.EmergencyReason = emergencyReason ?? entry.Thread.EmergencyReason;
            }
            else if (setEmergencyEscalated == false)
            {
                entry.Thread.IsEmergencyEscalated = false;
            }

            return Task.FromResult(ClinicDomainHelpers.Clone(entry.Thread));
        }
    }

    public Task<(AppointmentQueueItemDto? Appointment, WhatsAppThreadDto? Thread, string? Error)> ApproveAiBookingAsync(
        Guid appointmentId,
        string approvedBy,
        CancellationToken ct = default)
    {
        var claims = CurrentClaims;
        lock (_sync)
        {
            if (!_appointments.TryGetValue(appointmentId, out var appt) || !claims.CanAccessBranch(appt.BranchId))
            {
                return Task.FromResult<(AppointmentQueueItemDto?, WhatsAppThreadDto?, string?)>((null, null, "Appointment not found."));
            }

            if (appt.Status != QueueStatus.AiBookedPending && appt.Status != QueueStatus.Scheduled)
            {
                return Task.FromResult<(AppointmentQueueItemDto?, WhatsAppThreadDto?, string?)>(
                    (null, null, $"Cannot approve booking in status {appt.Status}."));
            }

            var oldStatus = appt.Status;
            var actor = string.IsNullOrWhiteSpace(approvedBy) ? claims.UserId : approvedBy.Trim();
            ClinicDomainHelpers.ApplyStatusMilestone(appt, oldStatus, QueueStatus.Scheduled, actor, DateTimeOffset.UtcNow);

            WhatsAppThreadDto? updatedThread = null;
            var matchingEntry = _whatsAppThreads.Values
                .FirstOrDefault(e => e.Thread.Messages.Any(m => m.CreatedBookingId == appointmentId));

            if (matchingEntry.Thread is not null)
            {
                matchingEntry.Thread.Messages.Add(new WhatsAppMessageDto
                {
                    Id = Guid.NewGuid(),
                    Sender = $"Staff ({approvedBy})",
                    Text = $"Appointment approved & confirmed for {appt.ScheduledAt:ddd dd MMM, hh:mm tt} with {appt.DoctorName} at {appt.BranchId}.",
                    Timestamp = DateTimeOffset.UtcNow,
                    CreatedBookingId = appointmentId
                });
                matchingEntry.Thread.LastUpdatedAt = DateTimeOffset.UtcNow;
                updatedThread = ClinicDomainHelpers.Clone(matchingEntry.Thread);
            }

            AppendAudit(
                appt.BranchId,
                string.IsNullOrWhiteSpace(approvedBy) ? claims.UserId : approvedBy.Trim(),
                "AI_BOOKING_APPROVED",
                "Appointment",
                appt.Id.ToString(),
                oldStatus.ToString(),
                appt.Status.ToString());

            return Task.FromResult<(AppointmentQueueItemDto?, WhatsAppThreadDto?, string?)>(
                (ClinicDomainHelpers.Clone(appt), updatedThread, null));
        }
    }

    private DentistDto? ResolveDentist(Guid? dentistId, string? doctorName)
    {
        if (dentistId.HasValue)
        {
            var byId = _dentists.FirstOrDefault(d => d.Id == dentistId.Value);
            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(doctorName))
        {
            var trimmed = doctorName.Trim();
            return _dentists.FirstOrDefault(d =>
                string.Equals(d.FullName, trimmed, StringComparison.OrdinalIgnoreCase));
        }

        return _dentists.FirstOrDefault();
    }

    private TreatmentDto? ResolveTreatment(Guid? treatmentId, string? procedureSummary)
    {
        if (treatmentId.HasValue)
        {
            var byId = _treatments.FirstOrDefault(t => t.Id == treatmentId.Value);
            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(procedureSummary))
        {
            var trimmed = procedureSummary.Trim();
            return _treatments.FirstOrDefault(t =>
                string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains(t.Name, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private string NextInvoiceNumber(string branchId)
    {
        _invoiceSeq++;
        var code = branchId.StartsWith("PJ", StringComparison.OrdinalIgnoreCase) ? "PJ" : "KL";
        return $"INV-{code}-{DateTime.UtcNow:yyyyMMdd}-{_invoiceSeq}";
    }

    private void AppendAudit(
        string branchId,
        string userId,
        string action,
        string entityName,
        string entityId,
        string? oldValue,
        string? newValue)
    {
        _auditLogs.Add(new AuditLogDto
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            UserId = string.IsNullOrWhiteSpace(userId) ? "system" : userId,
            Timestamp = DateTimeOffset.UtcNow,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            OldValue = oldValue,
            NewValue = newValue
        });
    }

    private static string NormalizeDigits(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsAsciiDigit).ToArray());

    private static SoapMacroDto CloneMacro(SoapMacroDto m) => new()
    {
        Id = m.Id,
        Title = m.Title,
        Category = m.Category,
        SubjectiveTemplate = m.SubjectiveTemplate,
        ObjectiveTemplate = m.ObjectiveTemplate,
        AssessmentTemplate = m.AssessmentTemplate,
        PlanTemplate = m.PlanTemplate,
        DefaultFee = m.DefaultFee
    };

    private void SeedData()
    {
        var now = DateTimeOffset.UtcNow;
        var localOffset = DateTimeOffset.Now.Offset;
        var localToday = DateOnly.FromDateTime(DateTime.Now);

        static DateTimeOffset LocalSlot(DateOnly date, int hour, int minute, TimeSpan offset) =>
            new(date.Year, date.Month, date.Day, hour, minute, 0, offset);

        // 0. Seed Treatments & Dentists Catalog (aligned with SQL migrations 002 & 003)
        var t1 = new TreatmentDto { Id = Guid.Parse("60000000-0000-0000-0000-000000000001"), Name = "Dental Consultation & Checkup", Category = "Diagnostic", DefaultDurationMinutes = 30, DefaultFee = 50.00m };
        var t2 = new TreatmentDto { Id = Guid.Parse("60000000-0000-0000-0000-000000000002"), Name = "Routine Scaling & Polishing", Category = "Preventive", DefaultDurationMinutes = 30, DefaultFee = 150.00m };
        var t3 = new TreatmentDto { Id = Guid.Parse("60000000-0000-0000-0000-000000000003"), Name = "Posterior Composite Filling", Category = "Restorative", DefaultDurationMinutes = 45, DefaultFee = 200.00m };
        var t4 = new TreatmentDto { Id = Guid.Parse("60000000-0000-0000-0000-000000000004"), Name = "Simple Tooth Extraction", Category = "Oral Surgery", DefaultDurationMinutes = 30, DefaultFee = 180.00m };
        var t5 = new TreatmentDto { Id = Guid.Parse("60000000-0000-0000-0000-000000000005"), Name = "Root Canal Therapy (Stage 1)", Category = "Endodontics", DefaultDurationMinutes = 60, DefaultFee = 450.00m };
        var t6 = new TreatmentDto { Id = Guid.Parse("60000000-0000-0000-0000-000000000006"), Name = "Crown Preparation & Cementation", Category = "Prosthodontics", DefaultDurationMinutes = 60, DefaultFee = 950.00m };
        var t7 = new TreatmentDto { Id = Guid.Parse("60000000-0000-0000-0000-000000000007"), Name = "Implant Crown Review", Category = "Prosthodontics", DefaultDurationMinutes = 30, DefaultFee = 350.00m };
        _treatments.AddRange(new[] { t1, t2, t3, t4, t5, t6, t7 });

        var d1 = new DentistDto
        {
            Id = Guid.Parse("50000000-0000-0000-0000-000000000001"),
            FullName = "Dr. Aina Rahman",
            Specialty = "General & Restorative",
            DefaultRoom = "Suite 1",
            BranchIds = new List<string> { "HQ-KL", "PJ-SS2" },
            SupportedTreatmentIds = new List<Guid> { t1.Id, t2.Id, t3.Id, t4.Id },
            IsActive = true
        };
        var d2 = new DentistDto
        {
            Id = Guid.Parse("50000000-0000-0000-0000-000000000002"),
            FullName = "Dr. Marcus Tan",
            Specialty = "Endodontics & Prosthodontics",
            DefaultRoom = "Suite 2",
            BranchIds = new List<string> { "HQ-KL", "PJ-SS2" },
            SupportedTreatmentIds = new List<Guid> { t1.Id, t3.Id, t4.Id, t5.Id, t6.Id, t7.Id },
            IsActive = true
        };
        var d3 = new DentistDto
        {
            Id = Guid.Parse("50000000-0000-0000-0000-000000000003"),
            FullName = "Dr. Priya Nair",
            Specialty = "Preventive & Orthodontics",
            DefaultRoom = "Suite 3",
            BranchIds = new List<string> { "HQ-KL" },
            SupportedTreatmentIds = new List<Guid> { t1.Id, t2.Id, t3.Id },
            IsActive = true
        };
        _dentists.AddRange(new[] { d1, d2, d3 });

        // 1. Seed Smart SOAP Macros (aligned with SQL migration)
        _soapMacros.AddRange(new[]
        {
            new SoapMacroDto
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000101"),
                Title = "Routine Scaling & Polishing",
                Category = "Preventive",
                SubjectiveTemplate = "Patient presents for routine 6-monthly dental hygiene scaling and polishing. No acute pain.",
                ObjectiveTemplate = "Mild-to-moderate supragingival calculus lower linguals. Gingiva mildly inflamed, no mobile teeth.",
                AssessmentTemplate = "Generalized mild plaque-induced gingivitis.",
                PlanTemplate = "Full mouth ultrasonic scaling, prophy paste polishing, OHI reinforced. Recall in 6 months.",
                DefaultFee = 150.00m
            },
            new SoapMacroDto
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000102"),
                Title = "Posterior Composite Filling",
                Category = "Restorative",
                SubjectiveTemplate = "Patient reports localized cold sensitivity on posterior tooth. No spontaneous nocturnal pain.",
                ObjectiveTemplate = "Occlusal caries present. Cold vitality test positive with brief response. No percussion tenderness.",
                AssessmentTemplate = "Dentinal caries without pulpal involvement.",
                PlanTemplate = "LA infiltration (2% Lidocaine), caries excavation, selective enamel etch, composite restoration (Shade A2), occlusion checked.",
                DefaultFee = 200.00m
            },
            new SoapMacroDto
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000103"),
                Title = "Simple Tooth Extraction",
                Category = "Oral Surgery",
                SubjectiveTemplate = "Patient complains of broken non-restorable tooth with intermittent discomfort.",
                ObjectiveTemplate = "Grossly carious crown. Adjacent gingiva intact. Pre-op PA confirms favourable root morphology.",
                AssessmentTemplate = "Non-restorable tooth indicated for simple forceps extraction.",
                PlanTemplate = "LA 2% Lidocaine w/ 1:80,000 adrenaline, atraumatic elevation & forceps delivery, hemostasis achieved with gauze pack. Post-op instructions given.",
                DefaultFee = 180.00m
            },
            new SoapMacroDto
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000104"),
                Title = "New Patient Consultation & OPG",
                Category = "Diagnostic",
                SubjectiveTemplate = "New patient seeking general dental assessment and treatment plan.",
                ObjectiveTemplate = "Extra-oral NAD. Intra-oral exam completed; full FDI chart recorded.",
                AssessmentTemplate = "Routine dental assessment completed.",
                PlanTemplate = "Discuss treatment priorities, preventive care, and corporate panel coverage options.",
                DefaultFee = 50.00m
            },
            new SoapMacroDto
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000105"),
                Title = "Root Canal Therapy (Stage 1)",
                Category = "Endodontics",
                SubjectiveTemplate = "Patient reports lingering thermal sensitivity and spontaneous throbbing ache.",
                ObjectiveTemplate = "Deep caries approximating pulp chamber. Positive percussion, prolonged cold test.",
                AssessmentTemplate = "Symptomatic irreversible pulpitis.",
                PlanTemplate = "Rubber dam isolation, LA block, access cavity, working length determination, canal chemo-mechanical prep, Ca(OH)2 dressing & Cavit temporary.",
                DefaultFee = 450.00m
            }
        });

        // 2. Seed 16 Malaysian Patients with valid NRICs and clinical medical alerts
        var p1 = CreateSeedPatient("20000000-0000-0000-0000-000000000001", "Nurul Aisyah binti Zulkifli", "920415-14-5678", "+6012-3456789", "12 Jalan Bangsar, 59000 Kuala Lumpur", "HQ-KL", true, now.AddDays(-14),
            new MedicalAlertDto { Allergies = "Penicillin", Notes = "Use Clindamycin if prophylaxis needed" });
        var p2 = CreateSeedPatient("20000000-0000-0000-0000-000000000002", "Tan Wei Ming", "881103-10-4321", "+6019-8765432", "45 Jalan SS 2/24, 47300 Petaling Jaya, Selangor", "HQ-KL", true, now.AddDays(-30),
            new MedicalAlertDto { HasDiabetes = true, Notes = "Type 2 DM on Metformin; prefer morning slots" });
        var p3 = CreateSeedPatient("20000000-0000-0000-0000-000000000003", "Kavitha a/p Subramaniam", "950722-07-6124", "+6016-2233445", "88 Jalan Brickfields, 50470 Kuala Lumpur", "HQ-KL", true, now.AddDays(-7),
            new MedicalAlertDto { IsPregnant = true, Notes = "2nd trimester (20 weeks); avoid elective radiographs" });
        var p4 = CreateSeedPatient("20000000-0000-0000-0000-000000000004", "Ahmad Faizal bin Mohd Nor", "650219-01-5113", "+6013-9128834", "23 Jalan Ampang Hilir, 55000 Kuala Lumpur", "HQ-KL", true, now.AddDays(-60),
            new MedicalAlertDto { OnAnticoagulants = true, Notes = "On Warfarin; check INR prior to extraction" });
        var p5 = CreateSeedPatient("20000000-0000-0000-0000-000000000005", "Lim Mei Ling", "910908-14-5282", "+6017-5543210", "19 Jalan Tun Razak, 50400 Kuala Lumpur", "HQ-KL", false, now.AddDays(-45),
            new MedicalAlertDto { Allergies = "Latex", Notes = "Use nitrile gloves strictly" });
        var p6 = CreateSeedPatient("20000000-0000-0000-0000-000000000006", "Muhammad Danish bin Hakim", "010612-10-0891", "+6011-23409876", "7 Jalan Bukit Bintang, 55100 Kuala Lumpur", "HQ-KL", true, now.AddDays(-5),
            new MedicalAlertDto());
        var p7 = CreateSeedPatient("20000000-0000-0000-0000-000000000007", "Chong Kah Wai", "791225-08-5433", "+6012-7789012", "101 Jalan Ipoh, 51200 Kuala Lumpur", "HQ-KL", true, now.AddDays(-20),
            new MedicalAlertDto { Allergies = "Ibuprofen", HasDiabetes = true, Notes = "Prescribe Paracetamol only for analgesia" });
        var p8 = CreateSeedPatient("20000000-0000-0000-0000-000000000008", "Siti Sarah binti Abdullah", "980314-03-5546", "+6014-6654321", "34 Jalan Sentral, 50470 Kuala Lumpur", "HQ-KL", true, now.AddDays(-2),
            new MedicalAlertDto());
        var p9 = CreateSeedPatient("20000000-0000-0000-0000-000000000009", "Premkumar a/l Krishnan", "840830-05-5217", "+6018-3345129", "52 Jalan Damansara, 60000 Kuala Lumpur", "HQ-KL", false, null,
            new MedicalAlertDto());
        var p10 = CreateSeedPatient("20000000-0000-0000-0000-000000000010", "Wong Siew Lan", "720518-14-5104", "+6012-6612398", "14 Jalan Cheras, 56100 Kuala Lumpur", "HQ-KL", true, now.AddDays(-90),
            new MedicalAlertDto { OnAnticoagulants = true, Notes = "Low-dose Aspirin 100mg daily" });
        var p11 = CreateSeedPatient("20000000-0000-0000-0000-000000000011", "Hafizuddin bin Roslan", "901009-06-5329", "+6019-2211443", "66 Jalan Mont Kiara, 50480 Kuala Lumpur", "HQ-KL", true, now.AddDays(-10),
            new MedicalAlertDto());
        var p12 = CreateSeedPatient("20000000-0000-0000-0000-000000000012", "Lee Jun Jie", "940127-10-5715", "+6016-9988776", "29 Jalan SS 2/60, 47300 Petaling Jaya, Selangor", "PJ-SS2", true, now.AddDays(-12),
            new MedicalAlertDto());
        var p13 = CreateSeedPatient("20000000-0000-0000-0000-000000000013", "Farah Nadia binti Ismail", "961111-16-5088", "+6013-4433221", "11 Jalan Universiti, 46350 Petaling Jaya, Selangor", "PJ-SS2", true, now.AddDays(-18),
            new MedicalAlertDto { Allergies = "Lidocaine sulfites", Notes = "Use plain Mepivacaine 3% without vasoconstrictor" });
        var p14 = CreateSeedPatient("20000000-0000-0000-0000-000000000014", "Muthu Kumar a/l Rajan", "810704-04-5093", "+6012-8877665", "78 Jalan Sea Park, 46300 Petaling Jaya, Selangor", "PJ-SS2", false, now.AddDays(-25),
            new MedicalAlertDto { HasDiabetes = true, Notes = "HbA1c 7.1%; monitor healing" });
        var p15 = CreateSeedPatient("20000000-0000-0000-0000-000000000015", "Grace Yap Hui Ying", "990529-13-5442", "+6017-8899001", "5 Jalan Tropicana, 47410 Petaling Jaya, Selangor", "PJ-SS2", true, now.AddDays(-3),
            new MedicalAlertDto());
        var p16 = CreateSeedPatient("20000000-0000-0000-0000-000000000016", "Azman Shah bin Ibrahim", "750916-02-5167", "+6019-5566778", "90 Jalan Section 17, 46400 Petaling Jaya, Selangor", "PJ-SS2", true, now.AddDays(-40),
            new MedicalAlertDto());

        foreach (var pat in new[] { p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13, p14, p15, p16 })
        {
            _patients[pat.Id] = pat;
        }

        // 3. Seed Odontogram entries (clinical chart entries across patients)
        SeedTooth(p1.Id, 16, ToothCondition.Filling, "Posterior Composite Filling (DO)", 220m, TreatmentStage.Completed, "Dr. Aina Rahman", now.AddHours(-2));
        SeedTooth(p1.Id, 26, ToothCondition.Caries, "Occlusal Composite Filling", 200m, TreatmentStage.Accepted, "Dr. Aina Rahman", now.AddHours(-1));
        SeedTooth(p1.Id, 46, ToothCondition.Crown, "Zirconia Crown Prep", 1200m, TreatmentStage.Proposed, "Dr. Aina Rahman", now.AddDays(-1));

        SeedTooth(p2.Id, 36, ToothCondition.RootCanal, "Root Canal Therapy (Stage 1)", 450m, TreatmentStage.Accepted, "Dr. Marcus Tan", now.AddMinutes(-30));
        SeedTooth(p2.Id, 11, ToothCondition.Filling, "Mesial Composite Restoration", 180m, TreatmentStage.Completed, "Dr. Marcus Tan", now.AddDays(-20));

        SeedTooth(p3.Id, 24, ToothCondition.Caries, "Glass Ionomer Temporary Filling", 150m, TreatmentStage.Accepted, "Dr. Aina Rahman", now.AddMinutes(-20));
        SeedTooth(p4.Id, 47, ToothCondition.ExtractionNeeded, "Simple Extraction (Post-INR Check)", 180m, TreatmentStage.Proposed, "Dr. Marcus Tan", now.AddDays(-2));
        SeedTooth(p5.Id, 14, ToothCondition.Filling, "Composite Filling", 200m, TreatmentStage.Completed, "Dr. Aina Rahman", now.AddHours(-3));
        SeedTooth(p7.Id, 37, ToothCondition.Crown, "PFM Crown Cementation", 950m, TreatmentStage.Completed, "Dr. Marcus Tan", now.AddHours(-4));
        SeedTooth(p12.Id, 21, ToothCondition.Implant, "Single Implant Crown Review", 350m, TreatmentStage.Completed, "Dr. Marcus Tan", now.AddDays(-5));

        // 4. Seed SOAP notes
        _soapNotes[p1.Id] = new List<SoapNoteDto>
        {
            new()
            {
                Id = Guid.NewGuid(),
                PatientId = p1.Id,
                BranchId = "HQ-KL",
                DoctorName = "Dr. Aina Rahman",
                VisitDate = now.AddDays(-14),
                Subjective = "Routine 6-monthly scaling & checkup; mild cold sensitivity upper left.",
                Objective = "Supragingival calculus lower anteriors. Caries noted on #26 occlusal.",
                Assessment = "Mild gingivitis; #26 dentinal caries.",
                Plan = "Full mouth scaling & polishing today; #26 composite filling scheduled.",
                AnesthesiaUsed = "None",
                ToothShade = "A2",
                PostOpInstructions = "Avoid staining beverages for 24 hours."
            },
            new()
            {
                Id = Guid.NewGuid(),
                PatientId = p1.Id,
                BranchId = "HQ-KL",
                DoctorName = "Dr. Aina Rahman",
                VisitDate = now.AddHours(-2),
                Subjective = "Returns for #16 DO composite restoration. Allergies checked (Penicillin).",
                Objective = "Deep distal-occlusal caries #16; vitality positive.",
                Assessment = "Dentinal caries #16 without pulpal exposure.",
                Plan = "LA 2% Lidocaine infiltration, excavation, composite Shade A2, occlusion adjusted.",
                AnesthesiaUsed = "2% Lidocaine w/ 1:80k Adrenaline (1.8ml)",
                ToothShade = "A2",
                PostOpInstructions = "Avoid chewing on right side until numbness wears off."
            }
        };

        _soapNotes[p2.Id] = new List<SoapNoteDto>
        {
            new()
            {
                Id = Guid.NewGuid(),
                PatientId = p2.Id,
                BranchId = "HQ-KL",
                DoctorName = "Dr. Marcus Tan",
                VisitDate = now.AddDays(-30),
                Subjective = "Routine review; Type 2 DM controlled on Metformin.",
                Objective = "#11 mesial incisal chip; periodontal probing <= 3mm.",
                Assessment = "Enamel-dentin fracture #11.",
                Plan = "Direct composite build-up #11 Shade A1.",
                AnesthesiaUsed = "None",
                ToothShade = "A1",
                PostOpInstructions = "Avoid biting hard nuts/ice with front teeth."
            }
        };

        _soapNotes[p4.Id] = new List<SoapNoteDto>
        {
            new()
            {
                Id = Guid.NewGuid(),
                PatientId = p4.Id,
                BranchId = "HQ-KL",
                DoctorName = "Dr. Marcus Tan",
                VisitDate = now.AddDays(-3),
                Subjective = "Loose molar #47, currently taking Warfarin.",
                Objective = "Grade II mobility #47; INR report requested prior to extraction.",
                Assessment = "Periodontally hopeless #47 on anticoagulant therapy.",
                Plan = "Defer extraction until INR <= 2.5 verified; advise physician letter.",
                AnesthesiaUsed = "N/A",
                ToothShade = "N/A",
                PostOpInstructions = "Continue prescribed Warfarin; do not stop without physician advice."
            }
        };

        // 5. Seed Appointments across HQ-KL (including Walk-Ins, Waiting, InChair, Checkout, AI Pending, Completed) and PJ-SS2
        var a1 = SeedAppt("30000000-0000-0000-0000-000000000001", "HQ-KL", p1, d1, "Suite 1", LocalSlot(localToday, 9, 0, localOffset), t3, "Posterior Composite Filling (#26)", QueueStatus.InChair, 45, "Penicillin allergy noted on chart",
            visitType: VisitType.Booked, arrivedAt: now.AddMinutes(-32), seatedAt: now.AddMinutes(-18), createdBy: "Siti (Reception)", checkedInBy: "Siti (Reception)", seatedBy: "Nurse Farah (Assistant)");
        var a2 = SeedAppt("30000000-0000-0000-0000-000000000002", "HQ-KL", p2, d2, "Suite 2", LocalSlot(localToday, 9, 30, localOffset), t5, "Root Canal Therapy Stage 1 (#36)", QueueStatus.Waiting, 60, "Type 2 DM; morning slot preferred",
            visitType: VisitType.Booked, arrivedAt: now.AddMinutes(-21), createdBy: "Aisyah (Reception)", checkedInBy: "Siti (Reception)");
        var a3 = SeedAppt("30000000-0000-0000-0000-000000000003", "HQ-KL", p3, d1, "Suite 1", LocalSlot(localToday, 10, 0, localOffset), t3, "Temporary Filling (#24)", QueueStatus.Waiting, 30, "2nd trimester pregnancy; avoid radiographs",
            visitType: VisitType.Booked, arrivedAt: now.AddMinutes(-9), createdBy: "Siti (Reception)", checkedInBy: "Siti (Reception)");
        var a4 = SeedAppt("30000000-0000-0000-0000-000000000004", "HQ-KL", p4, d2, "Suite 2", LocalSlot(localToday, 10, 30, localOffset), t1, "INR Review & Consultation (#47)", QueueStatus.Scheduled, 30, "Review Warfarin INR before extraction",
            visitType: VisitType.Booked, createdBy: "Aisyah (Reception)");
        var a5 = SeedAppt("30000000-0000-0000-0000-000000000005", "HQ-KL", p5, d3, "Suite 3", LocalSlot(localToday, 9, 15, localOffset), t3, "Composite Filling (#14)", QueueStatus.AwaitingCheckout, 45, "Latex allergy; nitrile gloves used",
            visitType: VisitType.Booked, arrivedAt: now.AddMinutes(-68), seatedAt: now.AddMinutes(-55), checkoutAt: now.AddMinutes(-7), createdBy: "Siti (Reception)", checkedInBy: "Siti (Reception)", seatedBy: "Nurse Mei Yan (Assistant)", checkoutBy: "Dr. Priya Nair");
        var a6 = SeedAppt("30000000-0000-0000-0000-000000000006", "HQ-KL", p6, d1, "Suite 3", LocalSlot(localToday, 11, 30, localOffset), t2, "Routine Scaling & Polishing", QueueStatus.AiBookedPending, 30, "Provisional WhatsApp AI booking",
            visitType: VisitType.AiBooked, createdBy: "WhatsApp AI");
        var a7 = SeedAppt("30000000-0000-0000-0000-000000000007", "HQ-KL", p7, d2, "Suite 2", LocalSlot(localToday, 9, 0, localOffset), t6, "PFM Crown Cementation (#37)", QueueStatus.Completed, 60, "AIA Dental Panel covered",
            visitType: VisitType.Booked, arrivedAt: now.AddHours(-3), seatedAt: now.AddMinutes(-165), checkoutAt: now.AddHours(-2).AddMinutes(-15), completedAt: now.AddHours(-2), createdBy: "Aisyah (Reception)", checkedInBy: "Siti (Reception)", seatedBy: "Nurse Farah (Assistant)", checkoutBy: "Dr. Marcus Tan", completedBy: "Aisyah (Reception)");
        var a8 = SeedAppt("30000000-0000-0000-0000-000000000008", "HQ-KL", p8, d3, "Suite 3", LocalSlot(localToday, 11, 0, localOffset), t1, "New Patient Consultation", QueueStatus.Scheduled, 30,
            visitType: VisitType.Booked, createdBy: "Siti (Reception)");
        var a9 = SeedAppt("30000000-0000-0000-0000-000000000009", "HQ-KL", p9, d3, "Suite 3", LocalSlot(localToday, 13, 30, localOffset), t2, "Routine Scaling & Polishing", QueueStatus.Scheduled, 30,
            visitType: VisitType.Booked, createdBy: "Siti (Reception)");
        var a10 = SeedAppt("30000000-0000-0000-0000-000000000010", "HQ-KL", p10, d1, "Suite 1", LocalSlot(localToday, 14, 0, localOffset), t1, "Routine Checkup", QueueStatus.Scheduled, 30, "On low-dose Aspirin 100mg",
            visitType: VisitType.Booked, createdBy: "Aisyah (Reception)");
        var a11 = SeedAppt("30000000-0000-0000-0000-000000000011", "HQ-KL", p11, d3, "Suite 3", LocalSlot(localToday, 15, 0, localOffset), t3, "Simple Filling Review", QueueStatus.Waiting, 30,
            visitType: VisitType.WalkIn, arrivedAt: now.AddMinutes(-35), createdBy: "Siti (Reception)", checkedInBy: "Siti (Reception)");
        var a12 = SeedAppt("30000000-0000-0000-0000-000000000012", "HQ-KL", p1, d1, "Suite 1", LocalSlot(localToday, 16, 0, localOffset), t3, "Posterior Composite Filling (#16)", QueueStatus.Completed, 45,
            visitType: VisitType.Booked, arrivedAt: now.AddHours(-2), seatedAt: now.AddMinutes(-105), checkoutAt: now.AddHours(-1).AddMinutes(-10), completedAt: now.AddHours(-1), createdBy: "Siti (Reception)", checkedInBy: "Siti (Reception)", seatedBy: "Nurse Farah (Assistant)", checkoutBy: "Dr. Aina Rahman", completedBy: "Siti (Reception)");
        SeedAppt("30000000-0000-0000-0000-000000000034", "HQ-KL", p9, d1, "Suite 1", LocalSlot(localToday, 12, 0, localOffset), t2, "Routine Scaling & Polishing", QueueStatus.Waiting, 30, "Walk-in patient at front desk",
            visitType: VisitType.WalkIn, arrivedAt: now.AddMinutes(-14), createdBy: "Aisyah (Reception)", checkedInBy: "Aisyah (Reception)");

        // Multi-day HQ-KL appointments across the week and month
        SeedAppt("30000000-0000-0000-0000-000000000018", "HQ-KL", p2, d1, "Suite 1", LocalSlot(localToday.AddDays(-1), 10, 0, localOffset), t2, "Routine Scaling & Polishing", QueueStatus.Completed, 30);
        SeedAppt("30000000-0000-0000-0000-000000000019", "HQ-KL", p6, d3, "Suite 3", LocalSlot(localToday.AddDays(-1), 14, 30, localOffset), t1, "Dental Consultation & Checkup", QueueStatus.Completed, 30);
        SeedAppt("30000000-0000-0000-0000-000000000020", "HQ-KL", p1, d2, "Suite 2", LocalSlot(localToday.AddDays(1), 10, 0, localOffset), t6, "Crown Preparation (#46)", QueueStatus.Scheduled, 60);
        SeedAppt("30000000-0000-0000-0000-000000000021", "HQ-KL", p5, d1, "Suite 1", LocalSlot(localToday.AddDays(1), 10, 0, localOffset), t2, "Routine Scaling & Polishing", QueueStatus.Scheduled, 30, "Follow-up hygiene check");
        SeedAppt("30000000-0000-0000-0000-000000000022", "HQ-KL", p4, d2, "Suite 2", LocalSlot(localToday.AddDays(1), 15, 0, localOffset), t4, "Simple Tooth Extraction (#47)", QueueStatus.Scheduled, 30, "Proceed if INR <= 2.5");
        SeedAppt("30000000-0000-0000-0000-000000000023", "HQ-KL", p3, d3, "Suite 3", LocalSlot(localToday.AddDays(2), 11, 0, localOffset), t2, "Routine Scaling & Polishing", QueueStatus.Scheduled, 30);
        SeedAppt("30000000-0000-0000-0000-000000000024", "HQ-KL", p8, d1, "Suite 1", LocalSlot(localToday.AddDays(2), 11, 0, localOffset), t3, "Posterior Composite Filling", QueueStatus.Scheduled, 45);
        SeedAppt("30000000-0000-0000-0000-000000000025", "HQ-KL", p9, d1, "Suite 1", LocalSlot(localToday.AddDays(2), 11, 15, localOffset), t1, "Dental Consultation & Checkup", QueueStatus.Scheduled, 30);
        SeedAppt("30000000-0000-0000-0000-000000000026", "HQ-KL", p10, d1, "Suite 1", LocalSlot(localToday.AddDays(2), 11, 30, localOffset), t2, "Routine Scaling & Polishing", QueueStatus.Scheduled, 30);
        SeedAppt("30000000-0000-0000-0000-000000000027", "HQ-KL", p7, d2, "Suite 2", LocalSlot(localToday.AddDays(3), 16, 0, localOffset), t7, "Crown Occlusion Review", QueueStatus.Scheduled, 30);
        SeedAppt("30000000-0000-0000-0000-000000000028", "HQ-KL", p11, d3, "Suite 3", LocalSlot(localToday.AddDays(5), 9, 30, localOffset), t2, "Routine Scaling & Polishing", QueueStatus.Scheduled, 30);
        SeedAppt("30000000-0000-0000-0000-000000000029", "HQ-KL", p2, d2, "Suite 2", LocalSlot(localToday.AddDays(7), 10, 30, localOffset), t5, "Root Canal Therapy Stage 2", QueueStatus.Scheduled, 60);

        // PJ-SS2 today + multi-day appointments
        var a13 = SeedAppt("30000000-0000-0000-0000-000000000013", "PJ-SS2", p12, d2, "PJ Room 1", LocalSlot(localToday, 9, 30, localOffset), t7, "Implant Crown Review (#21)", QueueStatus.InChair, 30,
            visitType: VisitType.Booked, arrivedAt: now.AddMinutes(-24), seatedAt: now.AddMinutes(-12), createdBy: "Aisyah (Reception)", checkedInBy: "Aisyah (Reception)", seatedBy: "Nurse Mei Yan (Assistant)");
        var a14 = SeedAppt("30000000-0000-0000-0000-000000000014", "PJ-SS2", p13, d1, "PJ Room 2", LocalSlot(localToday, 10, 30, localOffset), t2, "Routine Scaling & Polishing", QueueStatus.Waiting, 30, "Lidocaine sulfite allergy",
            visitType: VisitType.WalkIn, arrivedAt: now.AddMinutes(-16), createdBy: "Siti (Reception)", checkedInBy: "Siti (Reception)");
        var a15 = SeedAppt("30000000-0000-0000-0000-000000000015", "PJ-SS2", p14, d2, "PJ Room 1", LocalSlot(localToday, 14, 0, localOffset), t1, "Diabetic Periodontal Review", QueueStatus.Scheduled, 30,
            visitType: VisitType.Booked, createdBy: "Siti (Reception)");
        var a16 = SeedAppt("30000000-0000-0000-0000-000000000016", "PJ-SS2", p15, d1, "PJ Room 2", LocalSlot(localToday, 15, 30, localOffset), t1, "Routine Consultation", QueueStatus.AiBookedPending, 30, "Provisional WhatsApp AI booking",
            visitType: VisitType.AiBooked, createdBy: "WhatsApp AI");
        var a17 = SeedAppt("30000000-0000-0000-0000-000000000017", "PJ-SS2", p16, d2, "PJ Room 1", LocalSlot(localToday, 11, 0, localOffset), t2, "Routine Scaling & Polishing", QueueStatus.Completed, 30,
            visitType: VisitType.Booked, arrivedAt: now.AddHours(-2), seatedAt: now.AddMinutes(-95), checkoutAt: now.AddHours(-1).AddMinutes(-15), completedAt: now.AddHours(-1), createdBy: "Siti (Reception)", checkedInBy: "Siti (Reception)", seatedBy: "Nurse Farah (Assistant)", checkoutBy: "Dr. Marcus Tan", completedBy: "Siti (Reception)");

        SeedAppt("30000000-0000-0000-0000-000000000030", "PJ-SS2", p12, d1, "PJ Room 2", LocalSlot(localToday.AddDays(1), 11, 0, localOffset), t2, "Routine Scaling & Polishing", QueueStatus.Scheduled, 30);
        SeedAppt("30000000-0000-0000-0000-000000000031", "PJ-SS2", p13, d1, "PJ Room 2", LocalSlot(localToday.AddDays(2), 14, 0, localOffset), t3, "Posterior Composite Filling", QueueStatus.Scheduled, 45);
        SeedAppt("30000000-0000-0000-0000-000000000032", "PJ-SS2", p15, d1, "PJ Room 2", LocalSlot(localToday.AddDays(4), 10, 0, localOffset), t1, "Dental Consultation & Checkup", QueueStatus.Scheduled, 30);
        SeedAppt("30000000-0000-0000-0000-000000000033", "PJ-SS2", p16, d2, "PJ Room 1", LocalSlot(localToday.AddDays(6), 15, 0, localOffset), t4, "Simple Tooth Extraction", QueueStatus.Scheduled, 30);

        // 6. Seed Invoices (today's date UTC so EOD reconciliation picks them up cleanly)
        // Settled invoice with AIA Dental Panel + Card split payment
        var inv1 = new InvoiceDto
        {
            Id = Guid.Parse("40000000-0000-0000-0000-000000000001"),
            InvoiceNumber = $"INV-KL-{DateTime.UtcNow:yyyyMMdd}-1001",
            BranchId = "HQ-KL",
            PatientId = p7.Id,
            PatientName = p7.FullName,
            DoctorName = "Dr. Marcus Tan",
            AppointmentId = a7.Id,
            PanelName = "AIA Dental Panel",
            CreatedAt = now.AddHours(-2),
            Lines = new List<InvoiceLineDto>
            {
                new() { Id = Guid.NewGuid(), ToothNumber = 37, ProcedureName = "Tooth #37 - PFM Crown Cementation", UnitPrice = 950m, Quantity = 1, LineTotal = 950m, SourceStage = TreatmentStage.Completed },
                new() { Id = Guid.NewGuid(), ProcedureName = "Consultation & Periapical X-Ray", UnitPrice = 50m, Quantity = 1, LineTotal = 50m }
            },
            Payments = new List<PaymentSplitDto>
            {
                new() { Method = PaymentMethod.CorporatePanel, Amount = 600m, PanelName = "AIA Dental Panel", ReferenceNo = "AIA-884120" },
                new() { Method = PaymentMethod.Card, Amount = 400m, ReferenceNo = "VISA-4491" }
            }
        };
        BillingCalculator.RecalculateInvoice(inv1, _clinicOptions.CommissionRate);
        _invoices[inv1.Id] = inv1;

        // Settled invoice with Cash + QR Transfer (DuitNow)
        var inv2 = new InvoiceDto
        {
            Id = Guid.Parse("40000000-0000-0000-0000-000000000002"),
            InvoiceNumber = $"INV-KL-{DateTime.UtcNow:yyyyMMdd}-1002",
            BranchId = "HQ-KL",
            PatientId = p1.Id,
            PatientName = p1.FullName,
            DoctorName = "Dr. Aina Rahman",
            AppointmentId = a12.Id,
            CreatedAt = now.AddHours(-1),
            Lines = new List<InvoiceLineDto>
            {
                new() { Id = Guid.NewGuid(), ToothNumber = 16, ProcedureName = "Tooth #16 - Posterior Composite Filling (DO)", UnitPrice = 220m, Quantity = 1, LineTotal = 220m, SourceStage = TreatmentStage.Completed }
            },
            Payments = new List<PaymentSplitDto>
            {
                new() { Method = PaymentMethod.Cash, Amount = 100m },
                new() { Method = PaymentMethod.QrTransfer, Amount = 120m, ReferenceNo = "DUITNOW-99214" }
            }
        };
        BillingCalculator.RecalculateInvoice(inv2, _clinicOptions.CommissionRate);
        _invoices[inv2.Id] = inv2;

        // Active AwaitingCheckout draft invoice (unpaid) for Lim Mei Ling
        var inv3 = new InvoiceDto
        {
            Id = Guid.Parse("40000000-0000-0000-0000-000000000003"),
            InvoiceNumber = $"INV-KL-{DateTime.UtcNow:yyyyMMdd}-1003",
            BranchId = "HQ-KL",
            PatientId = p5.Id,
            PatientName = p5.FullName,
            DoctorName = "Dr. Aina Rahman",
            AppointmentId = a5.Id,
            PanelName = "PMCare",
            CreatedAt = now.AddMinutes(-25),
            Lines = new List<InvoiceLineDto>
            {
                new() { Id = Guid.NewGuid(), ToothNumber = 14, ProcedureName = "Tooth #14 - Composite Filling", UnitPrice = 200m, Quantity = 1, LineTotal = 200m, SourceStage = TreatmentStage.Completed },
                new() { Id = Guid.NewGuid(), ProcedureName = "Dental Consultation", UnitPrice = 50m, Quantity = 1, LineTotal = 50m }
            }
        };
        BillingCalculator.RecalculateInvoice(inv3, _clinicOptions.CommissionRate);
        _invoices[inv3.Id] = inv3;

        // Soft-Voided sample invoice demonstrating non-destructive immutable financial history
        var inv4 = new InvoiceDto
        {
            Id = Guid.Parse("40000000-0000-0000-0000-000000000004"),
            InvoiceNumber = $"INV-KL-{DateTime.UtcNow:yyyyMMdd}-1004",
            BranchId = "HQ-KL",
            PatientId = p8.Id,
            PatientName = p8.FullName,
            DoctorName = "Dr. Aina Rahman",
            IsVoided = true,
            VoidReason = "Duplicate consultation billing entry created in error",
            CreatedAt = now.AddHours(-3),
            Lines = new List<InvoiceLineDto>
            {
                new() { Id = Guid.NewGuid(), ProcedureName = "New Patient Consultation", UnitPrice = 50m, Quantity = 1, LineTotal = 50m }
            }
        };
        BillingCalculator.RecalculateInvoice(inv4, _clinicOptions.CommissionRate);
        _invoices[inv4.Id] = inv4;

        // PJ-SS2 invoice
        var inv5 = new InvoiceDto
        {
            Id = Guid.Parse("40000000-0000-0000-0000-000000000005"),
            InvoiceNumber = $"INV-PJ-{DateTime.UtcNow:yyyyMMdd}-1005",
            BranchId = "PJ-SS2",
            PatientId = p16.Id,
            PatientName = p16.FullName,
            DoctorName = "Dr. Marcus Tan",
            AppointmentId = a17.Id,
            PanelName = "PMCare",
            CreatedAt = now.AddHours(-1),
            Lines = new List<InvoiceLineDto>
            {
                new() { Id = Guid.NewGuid(), ProcedureName = "Routine Scaling & Polishing", UnitPrice = 150m, Quantity = 1, LineTotal = 150m }
            },
            Payments = new List<PaymentSplitDto>
            {
                new() { Method = PaymentMethod.CorporatePanel, Amount = 120m, PanelName = "PMCare", ReferenceNo = "PMC-55102" },
                new() { Method = PaymentMethod.QrTransfer, Amount = 30m, ReferenceNo = "DUITNOW-77102" }
            }
        };
        BillingCalculator.RecalculateInvoice(inv5, _clinicOptions.CommissionRate);
        _invoices[inv5.Id] = inv5;

        // 7. Seed WhatsApp threads (Routine AI-Booked Pending + Escalated Emergency + PJ-SS2 Inquiry)
        _whatsAppThreads["wa-601123409876"] = ("HQ-KL", new WhatsAppThreadDto
        {
            ThreadId = "wa-601123409876",
            PatientPhone = p6.Phone,
            PatientName = p6.FullName,
            IsEmergencyEscalated = false,
            LastUpdatedAt = now.AddMinutes(-45),
            Messages = new List<WhatsAppMessageDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Sender = "Patient",
                    Text = "Hi, can I book a routine scaling and polishing slot at KL Sentral HQ?",
                    Timestamp = now.AddMinutes(-46)
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Sender = "AI Receptionist",
                    Text = $"I've reserved a provisional slot for Routine Scaling & Polishing (RM120–RM180) at KL Sentral HQ with Dr. Aina Rahman on {a6.ScheduledAt:ddd dd MMM, hh:mm tt}. Status: AI-Booked (Pending) — our reception team will confirm shortly.",
                    Timestamp = now.AddMinutes(-45),
                    CreatedBookingId = a6.Id
                }
            }
        });

        _whatsAppThreads["wa-60129998811"] = ("HQ-KL", new WhatsAppThreadDto
        {
            ThreadId = "wa-60129998811",
            PatientPhone = "+6012-9998811",
            PatientName = "Zainab binti Omar",
            IsEmergencyEscalated = true,
            EmergencyReason = "Emergency red-flag symptom detected (facial swelling, severe pain). Immediate human staff takeover required.",
            LastUpdatedAt = now.AddMinutes(-10),
            Messages = new List<WhatsAppMessageDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Sender = "Patient",
                    Text = "Help! I have severe pain and facial swelling since last night after my molar cracked.",
                    Timestamp = now.AddMinutes(-11)
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Sender = "AI Receptionist",
                    Text = "URGENT TRIAGE ALERT: Because you reported severe pain and facial swelling, I have paused automated booking and escalated this immediately to our clinical front-desk team at KL Sentral HQ (+603-2274-8810). Please seek urgent hospital A&E care if you develop fever, difficulty breathing, or trouble swallowing.",
                    Timestamp = now.AddMinutes(-10)
                }
            }
        });

        _whatsAppThreads["wa-60178899001"] = ("PJ-SS2", new WhatsAppThreadDto
        {
            ThreadId = "wa-60178899001",
            PatientPhone = p15.Phone,
            PatientName = p15.FullName,
            IsEmergencyEscalated = false,
            LastUpdatedAt = now.AddMinutes(-20),
            Messages = new List<WhatsAppMessageDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Sender = "Patient",
                    Text = "Hi, would like to book a consultation at PJ SS2.",
                    Timestamp = now.AddMinutes(-21)
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Sender = "AI Receptionist",
                    Text = $"Provisional booking created for New Patient Consultation (RM50) at PJ SS2 with Dr. Aina Rahman on {a16.ScheduledAt:ddd dd MMM, hh:mm tt}. Status: AI-Booked (Pending) awaiting front-desk confirmation.",
                    Timestamp = now.AddMinutes(-20),
                    CreatedBookingId = a16.Id
                }
            }
        });

        // 8. Seed Audit Logs & Initial Backup Verification
        AppendAudit("HQ-KL", "staff-hq-01", "INVOICE_VOIDED", "Invoice", inv4.InvoiceNumber,
            "Subtotal=RM50.00; IsVoided=false",
            $"IsVoided=true; Reason={inv4.VoidReason}");
        AppendAudit("HQ-KL", "staff-hq-01", "INVOICE_PAYMENT_APPLIED", "Invoice", inv1.InvoiceNumber,
            null,
            "Paid=RM400.00; Panel=RM600.00 (AIA Dental Panel)");

        _backups.Add(new BackupVerificationDto
        {
            Id = Guid.NewGuid(),
            VerifiedAt = now.AddHours(-6),
            FileName = $"occlusa-backup-{DateTime.UtcNow:yyyyMMdd}-050000.dump.enc",
            Sha256Checksum = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            SizeBytes = 1048576,
            IsEncrypted = true,
            RestoreTestPassed = true,
            VerifiedBy = "Occlusa-Backup-Job",
            Notes = "Automated daily AES-256 encrypted backup verification passed."
        });
    }

    private static PatientDto CreateSeedPatient(
        string id,
        string fullName,
        string ic,
        string phone,
        string address,
        string branchId,
        bool isMyKadVerified,
        DateTimeOffset? consentSignedAt,
        MedicalAlertDto alerts)
    {
        var parsed = MyKadParser.ParseNric(ic);
        return new PatientDto
        {
            Id = Guid.Parse(id),
            FullName = fullName,
            IcNumber = parsed.IsValid ? parsed.NormalizedIc : ic,
            DateOfBirth = parsed.DateOfBirth ?? new DateOnly(1990, 1, 1),
            Gender = string.IsNullOrWhiteSpace(parsed.Gender) ? "Female" : parsed.Gender,
            Phone = phone,
            Address = address,
            PrimaryBranchId = branchId,
            MedicalAlerts = alerts,
            IsMyKadVerified = isMyKadVerified,
            ConsentSignedAt = consentSignedAt
        };
    }

    private void SeedTooth(
        Guid patientId,
        int toothNumber,
        ToothCondition condition,
        string procedureName,
        decimal fee,
        TreatmentStage stage,
        string doctorName,
        DateTimeOffset updatedAt)
    {
        _odontogram[(patientId, toothNumber)] = new OdontogramToothDto
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            ToothNumber = toothNumber,
            Condition = condition,
            ProcedureName = procedureName,
            Fee = fee,
            Stage = stage,
            DoctorName = doctorName,
            UpdatedAt = updatedAt
        };
    }

    private AppointmentQueueItemDto SeedAppt(
        string id,
        string branchId,
        PatientDto patient,
        DentistDto dentist,
        string roomName,
        DateTimeOffset scheduledAt,
        TreatmentDto treatment,
        string procedureSummary,
        QueueStatus status,
        int? durationMinutes = null,
        string notes = "",
        VisitType visitType = VisitType.Booked,
        DateTimeOffset? arrivedAt = null,
        DateTimeOffset? seatedAt = null,
        DateTimeOffset? checkoutAt = null,
        DateTimeOffset? completedAt = null,
        string createdBy = "Siti (Reception)",
        string checkedInBy = "",
        string seatedBy = "",
        string checkoutBy = "",
        string completedBy = "")
    {
        var activity = new List<VisitActivityEntryDto>
        {
            new()
            {
                Timestamp = arrivedAt ?? scheduledAt.AddHours(-1),
                Action = visitType == VisitType.WalkIn ? "Walk-In Registered" : visitType == VisitType.AiBooked ? "AI Booking Created" : "Appointment Booked",
                StaffName = createdBy,
                Details = $"{dentist.FullName} · {roomName} · {procedureSummary}"
            }
        };

        if (arrivedAt.HasValue && visitType != VisitType.WalkIn)
        {
            activity.Add(new VisitActivityEntryDto
            {
                Timestamp = arrivedAt.Value,
                Action = "Checked In (Waiting)",
                StaffName = string.IsNullOrWhiteSpace(checkedInBy) ? createdBy : checkedInBy,
                Details = $"{dentist.FullName} · {roomName}"
            });
        }
        if (seatedAt.HasValue)
        {
            activity.Add(new VisitActivityEntryDto
            {
                Timestamp = seatedAt.Value,
                Action = "Called to Chair",
                StaffName = string.IsNullOrWhiteSpace(seatedBy) ? "Nurse Farah (Assistant)" : seatedBy,
                Details = roomName
            });
        }
        if (checkoutAt.HasValue)
        {
            activity.Add(new VisitActivityEntryDto
            {
                Timestamp = checkoutAt.Value,
                Action = "Sent to Checkout",
                StaffName = string.IsNullOrWhiteSpace(checkoutBy) ? dentist.FullName : checkoutBy,
                Details = roomName
            });
        }
        if (completedAt.HasValue)
        {
            activity.Add(new VisitActivityEntryDto
            {
                Timestamp = completedAt.Value,
                Action = "Completed",
                StaffName = string.IsNullOrWhiteSpace(completedBy) ? "Siti (Reception)" : completedBy,
                Details = procedureSummary
            });
        }

        var lastEntry = activity[^1];
        var item = new AppointmentQueueItemDto
        {
            Id = Guid.Parse(id),
            BranchId = branchId,
            PatientId = patient.Id,
            PatientName = patient.FullName,
            PatientIc = patient.IcNumber,
            DentistId = dentist.Id,
            DoctorName = dentist.FullName,
            TreatmentId = treatment.Id,
            RoomName = roomName,
            ScheduledAt = scheduledAt,
            DurationMinutes = durationMinutes ?? treatment.DefaultDurationMinutes,
            ProcedureSummary = procedureSummary,
            Notes = notes,
            Status = AppointmentCalendarHelper.NormalizeQueueStatus(status),
            VisitType = visitType,
            ArrivedAt = arrivedAt,
            SeatedAt = seatedAt,
            CheckoutAt = checkoutAt,
            CompletedAt = completedAt,
            CreatedBy = createdBy,
            CheckedInBy = checkedInBy,
            SeatedBy = seatedBy,
            CheckoutBy = checkoutBy,
            CompletedBy = completedBy,
            LastUpdatedBy = lastEntry.StaffName,
            LastUpdatedAt = lastEntry.Timestamp,
            ActivityLog = activity,
            HasCriticalMedicalAlert = patient.MedicalAlerts.HasAnyCriticalAlert,
            AlertSummary = patient.MedicalAlerts.BuildAlertSummary()
        };
        _appointments[item.Id] = item;
        return item;
    }
}
