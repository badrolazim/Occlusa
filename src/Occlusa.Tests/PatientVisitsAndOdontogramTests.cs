using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Occlusa.Api;
using Occlusa.Api.Options;
using Occlusa.Api.Repositories;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Tests;

public class PatientVisitsAndOdontogramTests
{
    private static InMemoryOcclusaRepository CreateRepository() =>
        new(Options.Create(new ClinicOptions()), new HttpContextAccessor());

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Occlusa.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static async Task<PatientDto> RegisterPatientAsync(InMemoryOcclusaRepository repo, string name, string ic, string phone) =>
        await repo.UpsertPatientAsync(new UpsertPatientRequest
        {
            FullName = name,
            IcNumber = ic,
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = "Female",
            Phone = phone,
            PrimaryBranchId = "HQ-KL"
        });

    private static async Task<OdontogramToothDto> ChartAsync(
        InMemoryOcclusaRepository repo,
        Guid visitId,
        int tooth,
        ToothCondition condition,
        string surfaces,
        string procedure,
        decimal fee,
        TreatmentStage stage,
        string doctor = "Dr. Aina Rahman")
    {
        var (updated, _, error) = await repo.UpdateVisitOdontogramAsync(visitId, new UpdateOdontogramRequest
        {
            BranchId = "HQ-KL",
            ToothNumber = tooth,
            Condition = condition,
            Surfaces = surfaces,
            ProcedureName = procedure,
            Fee = fee,
            Stage = stage,
            DoctorName = doctor,
            UpdatedBy = doctor
        });
        Assert.Null(error);
        Assert.NotNull(updated);
        return updated!;
    }

    [Fact]
    public void OdontogramHelper_Validates52FdiTeethAndNormalizesFiveSurfaces()
    {
        Assert.Equal(32, OdontogramHelper.PermanentTeeth.Length);
        Assert.Equal(20, OdontogramHelper.PrimaryTeeth.Length);
        Assert.Equal(52, OdontogramHelper.AllFdiTeeth.Length);

        foreach (var tooth in OdontogramHelper.PermanentTeeth)
        {
            Assert.True(OdontogramHelper.IsValidFdiTooth(tooth));
            Assert.False(OdontogramHelper.IsPrimaryTooth(tooth));
        }

        foreach (var tooth in OdontogramHelper.PrimaryTeeth)
        {
            Assert.True(OdontogramHelper.IsValidFdiTooth(tooth));
            Assert.True(OdontogramHelper.IsPrimaryTooth(tooth));
        }

        foreach (var invalid in new[] { -1, 0, 10, 19, 29, 30, 39, 49, 50, 56, 60, 66, 70, 76, 80, 86, 91 })
        {
            Assert.False(OdontogramHelper.IsValidFdiTooth(invalid));
        }

        // Midline-aware orientation: Q2/Q3/Q6/Q7 mirror (Mesial on left)
        Assert.False(OdontogramHelper.IsMirroredLeftQuadrant(16));
        Assert.True(OdontogramHelper.IsMirroredLeftQuadrant(26));
        Assert.True(OdontogramHelper.IsMirroredLeftQuadrant(36));
        Assert.False(OdontogramHelper.IsMirroredLeftQuadrant(85));
        Assert.True(OdontogramHelper.IsUpperTooth(55));
        Assert.False(OdontogramHelper.IsUpperTooth(75));

        Assert.Equal(string.Empty, OdontogramHelper.NormalizeSurfaces(null));
        Assert.Equal(string.Empty, OdontogramHelper.NormalizeSurfaces("   "));
        Assert.Equal("MOD", OdontogramHelper.NormalizeSurfaces("dom"));
        Assert.Equal("OL", OdontogramHelper.NormalizeSurfaces("ip"));
        Assert.Equal("MOBL", OdontogramHelper.NormalizeSurfaces("fblmo"));
        Assert.Equal("OD", OdontogramHelper.NormalizeSurfaces(" d, o "));
        Assert.Equal("MODBL", OdontogramHelper.NormalizeSurfaces("lbdmoMODBL"));

        var summary = OdontogramHelper.FormatCompactVisitSummary(new PatientVisitDto
        {
            Subjective = "  Patient complains of sharp thermal sensitivity on upper right molar when drinking cold water  "
        }, maxLength: 42);
        Assert.True(summary.Length <= 42);
        Assert.DoesNotContain("...", summary, StringComparison.Ordinal);
        Assert.Equal("Clinical chart recorded", OdontogramHelper.FormatCompactVisitSummary(new PatientVisitDto()));
    }

    [Fact]
    public async Task StandalonePatientRegistration_AndPreChairAppointments_CreateZeroVisits()
    {
        var repo = CreateRepository();
        var dentists = await repo.GetDentistsAsync("HQ-KL");
        var treatments = await repo.GetTreatmentsAsync();

        var standalone = await RegisterPatientAsync(repo, "Farhan bin Ismail", "930812-10-5431", "+6017-8899101");
        Assert.Equal(0, standalone.VisitCount);
        Assert.Null(standalone.LastVisitAt);
        Assert.Empty(await repo.GetPatientVisitsAsync(standalone.Id));

        var scheduled = await repo.CreateAppointmentAsync(new CreateAppointmentRequest
        {
            BranchId = "HQ-KL",
            PatientId = standalone.Id,
            DentistId = dentists[0].Id,
            DoctorName = dentists[0].FullName,
            TreatmentId = treatments[0].Id,
            RoomName = dentists[0].DefaultRoom,
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(2),
            DurationMinutes = 30,
            ProcedureSummary = treatments[0].Name,
            Status = QueueStatus.Scheduled,
            VisitType = VisitType.Booked,
            CreatedBy = "Siti (Reception)"
        });
        Assert.Null(scheduled.VisitId);

        var (waiting, waitErr) = await repo.UpdateQueueStatusAsync(scheduled.Id, new UpdateQueueStatusRequest
        {
            NewStatus = QueueStatus.Waiting,
            UpdatedBy = "Siti (Reception)"
        });
        Assert.Null(waitErr);
        Assert.Null(waiting!.VisitId);

        var (cancelled, cancelErr) = await repo.UpdateQueueStatusAsync(scheduled.Id, new UpdateQueueStatusRequest
        {
            NewStatus = QueueStatus.Cancelled,
            UpdatedBy = "Siti (Reception)"
        });
        Assert.Null(cancelErr);
        Assert.Null(cancelled!.VisitId);

        var aiPending = await repo.CreateAppointmentAsync(new CreateAppointmentRequest
        {
            BranchId = "HQ-KL",
            PatientId = standalone.Id,
            DentistId = dentists[0].Id,
            DoctorName = dentists[0].FullName,
            TreatmentId = treatments[0].Id,
            RoomName = dentists[0].DefaultRoom,
            ScheduledAt = DateTimeOffset.UtcNow.AddDays(1),
            DurationMinutes = 30,
            ProcedureSummary = treatments[0].Name,
            Status = QueueStatus.AiBookedPending,
            VisitType = VisitType.Booked,
            CreatedBy = "WhatsApp AI"
        });
        Assert.Null(aiPending.VisitId);

        Assert.Empty(await repo.GetPatientVisitsAsync(standalone.Id));
        var refreshed = await repo.GetPatientByIdAsync(standalone.Id);
        Assert.Equal(0, refreshed!.VisitCount);
        Assert.Null(refreshed.LastVisitAt);
    }

    [Fact]
    public async Task TransitionToInChair_AutoCreatesLinkedVisit_AndEnsureVisitIsIdempotent()
    {
        var repo = CreateRepository();
        var dentists = await repo.GetDentistsAsync("HQ-KL");
        var treatments = await repo.GetTreatmentsAsync();
        var patient = await RegisterPatientAsync(repo, "Nadia binti Kamarudin", "960422-14-6012", "+6012-3344006");

        var appt = await repo.CreateAppointmentAsync(new CreateAppointmentRequest
        {
            BranchId = "HQ-KL",
            PatientId = patient.Id,
            DentistId = dentists[0].Id,
            DoctorName = dentists[0].FullName,
            TreatmentId = treatments[0].Id,
            RoomName = dentists[0].DefaultRoom,
            ScheduledAt = DateTimeOffset.UtcNow,
            DurationMinutes = 30,
            ProcedureSummary = "Scaling & Polishing",
            Notes = "Sensitive lower incisors",
            Status = QueueStatus.Waiting,
            VisitType = VisitType.WalkIn,
            CreatedBy = "Siti (Reception)"
        });
        Assert.Null(appt.VisitId);

        var (seated, seatErr) = await repo.UpdateQueueStatusAsync(appt.Id, new UpdateQueueStatusRequest
        {
            NewStatus = QueueStatus.InChair,
            RoomName = "Suite 1",
            UpdatedBy = "Dr. Aina Rahman"
        });
        Assert.Null(seatErr);
        Assert.NotNull(seated!.VisitId);

        var visit = await repo.GetVisitByIdAsync(seated.VisitId!.Value);
        Assert.NotNull(visit);
        Assert.Equal(1, visit!.VisitNumber);
        Assert.Equal(appt.Id, visit.AppointmentId);
        Assert.Equal(52, visit.Odontogram.Count);
        Assert.Equal("Sensitive lower incisors", visit.Subjective);

        var (ensured, _, ensureErr) = await repo.EnsureVisitForAppointmentAsync(appt.Id, "Dr. Aina Rahman");
        Assert.Null(ensureErr);
        Assert.Equal(visit.Id, ensured!.Id);
        Assert.Single(await repo.GetPatientVisitsAsync(patient.Id));

        var hydrated = await repo.GetPatientByIdAsync(patient.Id);
        Assert.Equal(1, hydrated!.VisitCount);
        Assert.NotNull(hydrated.LastVisitAt);
    }

    [Fact]
    public async Task MultiVisitCarryForward_PreservesIndependentHistoricalOdontograms_AndResetsCleanly()
    {
        var repo = CreateRepository();
        var patient = await RegisterPatientAsync(repo, "Lim Jia Hao", "900215-07-5221", "+6016-7788100");
        var baseDate = DateTimeOffset.UtcNow.AddDays(-30);

        var (visit1, _, v1Err) = await repo.CreatePatientVisitAsync(patient.Id, new CreatePatientVisitRequest
        {
            BranchId = "HQ-KL",
            DoctorName = "Dr. Aina Rahman",
            VisitDate = baseDate,
            Subjective = "Routine checkup and upper right cavity",
            InitMode = OdontogramInitMode.Blank,
            CreatedBy = "Dr. Aina Rahman"
        });
        Assert.Null(v1Err);
        Assert.Equal(1, visit1!.VisitNumber);
        Assert.Equal(52, visit1.Odontogram.Count);

        await ChartAsync(repo, visit1.Id, 16, ToothCondition.Filling, "do", "Posterior Composite", 180m, TreatmentStage.Completed);
        await ChartAsync(repo, visit1.Id, 38, ToothCondition.ExtractionNeeded, "", "Surgical Extraction", 350m, TreatmentStage.Completed);
        await ChartAsync(repo, visit1.Id, 26, ToothCondition.Caries, "mo", "Composite Restoration", 190m, TreatmentStage.Accepted);
        await ChartAsync(repo, visit1.Id, 55, ToothCondition.Caries, "o", "Pediatric GIC Filling", 90m, TreatmentStage.Proposed);

        var (visit2, _, v2Err) = await repo.CreatePatientVisitAsync(patient.Id, new CreatePatientVisitRequest
        {
            BranchId = "HQ-KL",
            DoctorName = "Dr. Marcus Tan",
            VisitDate = baseDate.AddDays(14),
            Subjective = "Follow-up visit for accepted #26 restoration",
            InitMode = OdontogramInitMode.CarryForward,
            CreatedBy = "Dr. Marcus Tan"
        });
        Assert.Null(v2Err);
        Assert.Equal(2, visit2!.VisitNumber);

        // Completed procedure carries forward as structural baseline only (never re-billed)
        var v2Tooth16 = visit2.Odontogram.Single(t => t.ToothNumber == 16);
        Assert.Equal(ToothCondition.Filling, v2Tooth16.Condition);
        Assert.Equal("OD", v2Tooth16.Surfaces);
        Assert.Equal(0m, v2Tooth16.Fee);
        Assert.Equal(string.Empty, v2Tooth16.ProcedureName);
        Assert.True(v2Tooth16.IsBaselineOnly);

        // Completed extraction becomes Missing
        var v2Tooth38 = visit2.Odontogram.Single(t => t.ToothNumber == 38);
        Assert.Equal(ToothCondition.Missing, v2Tooth38.Condition);
        Assert.Equal(string.Empty, v2Tooth38.Surfaces);
        Assert.Equal(0m, v2Tooth38.Fee);

        // Unfinished Accepted / Proposed treatment carries forward intact
        var v2Tooth26 = visit2.Odontogram.Single(t => t.ToothNumber == 26);
        Assert.Equal(ToothCondition.Caries, v2Tooth26.Condition);
        Assert.Equal("MO", v2Tooth26.Surfaces);
        Assert.Equal(TreatmentStage.Accepted, v2Tooth26.Stage);
        Assert.Equal(190m, v2Tooth26.Fee);
        Assert.Equal("Composite Restoration", v2Tooth26.ProcedureName);

        var v2Tooth55 = visit2.Odontogram.Single(t => t.ToothNumber == 55);
        Assert.Equal("O", v2Tooth55.Surfaces);
        Assert.Equal(TreatmentStage.Proposed, v2Tooth55.Stage);
        Assert.Equal(90m, v2Tooth55.Fee);

        // Mutate Visit #2 only
        await ChartAsync(repo, visit2.Id, 26, ToothCondition.Filling, "mod", "Composite Restoration", 220m, TreatmentStage.Completed, "Dr. Marcus Tan");
        await ChartAsync(repo, visit2.Id, 46, ToothCondition.RootCanal, "", "Molar Endodontics", 950m, TreatmentStage.Proposed, "Dr. Marcus Tan");

        // Visit #1 remains independent from Visit #2 mutations
        var v1Chart = await repo.GetVisitOdontogramAsync(visit1.Id);
        Assert.Equal(180m, v1Chart.Single(t => t.ToothNumber == 16).Fee);
        Assert.Equal(TreatmentStage.Completed, v1Chart.Single(t => t.ToothNumber == 16).Stage);
        Assert.Equal(ToothCondition.Caries, v1Chart.Single(t => t.ToothNumber == 26).Condition);
        Assert.Equal("MO", v1Chart.Single(t => t.ToothNumber == 26).Surfaces);
        Assert.Equal(ToothCondition.Healthy, v1Chart.Single(t => t.ToothNumber == 46).Condition);

        // Reset Visit #2 to Blank -> only Visit #2 resets
        var (blankVisit, blankErr) = await repo.ResetVisitOdontogramAsync(visit2.Id, new ResetVisitOdontogramRequest
        {
            Mode = OdontogramInitMode.Blank,
            UpdatedBy = "Dr. Marcus Tan"
        });
        Assert.Null(blankErr);
        Assert.Equal(52, blankVisit!.Odontogram.Count);
        Assert.All(blankVisit.Odontogram, t => Assert.Equal(ToothCondition.Healthy, t.Condition));
        Assert.Equal(ToothCondition.Filling, (await repo.GetVisitOdontogramAsync(visit1.Id)).Single(t => t.ToothNumber == 16).Condition);

        // Re-sync Visit #2 from prior visit
        var (resynced, resyncErr) = await repo.ResetVisitOdontogramAsync(visit2.Id, new ResetVisitOdontogramRequest
        {
            Mode = OdontogramInitMode.CarryForward,
            UpdatedBy = "Dr. Marcus Tan"
        });
        Assert.Null(resyncErr);
        Assert.Equal(ToothCondition.Filling, resynced!.Odontogram.Single(t => t.ToothNumber == 16).Condition);
        Assert.Equal(ToothCondition.Missing, resynced.Odontogram.Single(t => t.ToothNumber == 38).Condition);

        var visits = await repo.GetPatientVisitsAsync(patient.Id);
        Assert.Equal(2, visits.Count);
        Assert.Equal(2, visits[0].VisitNumber);
    }

    [Fact]
    public async Task VisitOdontogramIsStrictlyDecoupledFromInvoices_AndDraftEditsNeverMutateVisitReport()
    {
        var repo = CreateRepository();
        var patient = await RegisterPatientAsync(repo, "Amina binti Razak", "870619-14-5110", "+6019-2211300");

        var (visit1, _, _) = await repo.CreatePatientVisitAsync(patient.Id, new CreatePatientVisitRequest
        {
            BranchId = "HQ-KL",
            DoctorName = "Dr. Aina Rahman",
            InitMode = OdontogramInitMode.Blank,
            CreatedBy = "Dr. Aina Rahman"
        });
        Assert.NotNull(visit1);

        // 1. Charting Completed procedures on Visit #1 never auto-creates or mutates an invoice
        var (_, autoInvoice, chartErr) = await repo.UpdateVisitOdontogramAsync(visit1!.Id, new UpdateOdontogramRequest
        {
            BranchId = "HQ-KL",
            ToothNumber = 16,
            Condition = ToothCondition.Filling,
            Surfaces = "do",
            ProcedureName = "Posterior Composite Filling",
            Fee = 0m,
            Stage = TreatmentStage.Completed,
            DoctorName = "Dr. Aina Rahman"
        });
        Assert.Null(chartErr);
        Assert.Null(autoInvoice);
        Assert.DoesNotContain(await repo.GetInvoicesAsync("HQ-KL"), i => i.PatientId == patient.Id);

        // 2. Dentist initiates Draft Invoice via Suggestion Engine + SaveInvoiceDraftAsync
        var hydratedVisit = await repo.GetVisitByIdAsync(visit1.Id);
        var settings = await repo.GetBillingSettingsAsync();
        var suggestions = InvoiceSuggestionEngine.BuildSuggestions(hydratedVisit, null, patient, settings);
        Assert.Single(suggestions.AutoPopulatedLines);
        Assert.Equal("Tooth #16 (OD) - Posterior Composite Filling", suggestions.AutoPopulatedLines[0].ProcedureName);
        Assert.Equal(200m, suggestions.AutoPopulatedLines[0].UnitPrice);

        var (draft, _, draftErr) = await repo.SaveInvoiceDraftAsync(new SaveInvoiceDraftRequest
        {
            BranchId = "HQ-KL",
            PatientId = patient.Id,
            PatientName = patient.FullName,
            DoctorName = "Dr. Aina Rahman",
            VisitId = visit1.Id,
            Lines = suggestions.AutoPopulatedLines,
            UpdatedBy = "Dr. Aina Rahman"
        });
        Assert.Null(draftErr);
        Assert.NotNull(draft);
        Assert.Equal(200m, draft!.Subtotal);

        // 3. Receptionist changes invoice price from RM 200 -> RM 175 at counter; Visit Report stays untouched
        draft.Lines[0].UnitPrice = 175m;
        var (editedDraft, _, editErr) = await repo.SaveInvoiceDraftAsync(new SaveInvoiceDraftRequest
        {
            InvoiceId = draft.Id,
            BranchId = "HQ-KL",
            PatientId = patient.Id,
            PatientName = patient.FullName,
            DoctorName = "Dr. Aina Rahman",
            VisitId = visit1.Id,
            Lines = draft.Lines,
            UpdatedBy = "Siti (Reception)"
        });
        Assert.Null(editErr);
        Assert.Equal(175m, editedDraft!.Subtotal);

        var visitAfterCounterEdit = await repo.GetVisitByIdAsync(visit1.Id);
        Assert.Equal(0m, visitAfterCounterEdit!.Odontogram.Single(t => t.ToothNumber == 16).Fee);
        Assert.Equal("Posterior Composite Filling", visitAfterCounterEdit.Odontogram.Single(t => t.ToothNumber == 16).ProcedureName);

        // 4. Settle Visit #1's invoice and verify historical edits to Visit #1 never alter settled invoice
        var (settled, _, payErr) = await repo.ApplyInvoicePaymentAsync(editedDraft.Id, new ApplyPaymentRequest
        {
            Payments = [new PaymentSplitDto { Method = PaymentMethod.QrTransfer, Amount = 175m, ReferenceNo = "DN-9001" }],
            CollectedBy = "Siti (Reception)"
        });
        Assert.Null(payErr);
        Assert.Single(settled!.Payments);

        var (updatedV1, soapErr) = await repo.UpdatePatientVisitAsync(visit1.Id, new UpdatePatientVisitRequest
        {
            DoctorName = "Dr. Aina Rahman",
            Subjective = "Amended clinical note: cold test positive prior to restoration",
            Objective = "Distal-occlusal caries #16",
            Assessment = "Reversible pulpitis #16",
            Plan = "Composite restoration completed",
            UpdatedBy = "Dr. Aina Rahman"
        });
        Assert.Null(soapErr);
        Assert.Contains("Amended clinical note", updatedV1!.Subjective);

        var patientInvoices = (await repo.GetInvoicesAsync("HQ-KL")).Where(i => i.PatientId == patient.Id).ToList();
        Assert.Single(patientInvoices);
        Assert.Equal(175m, patientInvoices[0].Subtotal);

        var audits = await repo.GetAuditLogsAsync("HQ-KL");
        Assert.Contains(audits, a => a.Action == "PATIENT_VISIT_UPDATED" && a.EntityId == visit1.Id.ToString());
        Assert.Contains(audits, a => a.Action == "VISIT_ODONTOGRAM_UPDATED" && a.EntityId.StartsWith(visit1.Id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task SeededClinicData_IncludesMultiVisitSingleVisitAndZeroVisitPatients()
    {
        var repo = CreateRepository();
        var patients = await repo.GetPatientsAsync();

        var nurul = patients.Single(p => p.FullName == "Nurul Aisyah binti Zulkifli");
        Assert.Equal(3, nurul.VisitCount);
        Assert.NotNull(nurul.LastVisitAt);

        var nurulVisits = await repo.GetPatientVisitsAsync(nurul.Id);
        Assert.Equal(3, nurulVisits.Count);
        Assert.Equal(3, nurulVisits[0].VisitNumber);
        Assert.Equal(1, nurulVisits[^1].VisitNumber);

        // Visit #3 carries #16 forward as baseline (RM 0) and keeps #26 Accepted
        var v3Tooth16 = nurulVisits[0].Odontogram.Single(t => t.ToothNumber == 16);
        Assert.Equal(ToothCondition.Filling, v3Tooth16.Condition);
        Assert.Equal(0m, v3Tooth16.Fee);
        Assert.Equal(TreatmentStage.Accepted, nurulVisits[0].Odontogram.Single(t => t.ToothNumber == 26).Stage);

        var tan = patients.Single(p => p.FullName == "Tan Wei Ming");
        Assert.Equal(1, tan.VisitCount);

        var siti = patients.Single(p => p.FullName == "Siti Sarah binti Abdullah");
        Assert.Equal(0, siti.VisitCount);
        Assert.Null(siti.LastVisitAt);
        Assert.Empty(await repo.GetPatientVisitsAsync(siti.Id));
    }

    [Fact]
    public async Task HttpPipeline_VisitsAndPerVisitOdontogramEndpoints_WorkEndToEnd()
    {
        await using var app = OcclusaApiApplicationFactory.BuildApplication(
            urls: "http://127.0.0.1:0",
            environmentName: "Development");
        await app.StartAsync();

        try
        {
            var baseAddress = app.Urls.First();
            using var http = new HttpClient { BaseAddress = new Uri(baseAddress) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Occlusa-User", "Dr. Aina Rahman");
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Occlusa-Allowed-Branches", "HQ-KL,PJ-SS2");

            var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };
            jsonOpts.Converters.Add(new JsonStringEnumConverter());

            var snapshot = await http.GetFromJsonAsync<ClinicDashboardSnapshotDto>(
                "/api/dashboard/snapshot?branchId=HQ-KL",
                jsonOpts);
            Assert.NotNull(snapshot);

            // 1. Register standalone patient with 0 visits
            var createPatientResp = await http.PostAsJsonAsync("/api/patients", new UpsertPatientRequest
            {
                FullName = "Hafizah binti Omar",
                IcNumber = "940909-14-5882",
                DateOfBirth = new DateOnly(1994, 9, 9),
                Gender = "Female",
                Phone = "+6013-5566700",
                PrimaryBranchId = "HQ-KL"
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.OK, createPatientResp.StatusCode);
            var patient = await createPatientResp.Content.ReadFromJsonAsync<PatientDto>(jsonOpts);
            Assert.Equal(0, patient!.VisitCount);

            var initialVisits = await http.GetFromJsonAsync<List<PatientVisitDto>>(
                $"/api/patients/{patient.Id}/visits",
                jsonOpts);
            Assert.Empty(initialVisits!);

            // 2. Waiting walk-in (0 visits) -> InChair auto-creates Visit #1
            var apptResp = await http.PostAsJsonAsync("/api/appointments", new CreateAppointmentRequest
            {
                BranchId = "HQ-KL",
                PatientId = patient.Id,
                DentistId = snapshot!.Dentists[0].Id,
                DoctorName = snapshot.Dentists[0].FullName,
                TreatmentId = snapshot.Treatments[0].Id,
                RoomName = snapshot.Dentists[0].DefaultRoom,
                ScheduledAt = DateTimeOffset.Now,
                DurationMinutes = 30,
                ProcedureSummary = "Composite Restoration",
                Status = QueueStatus.Waiting,
                VisitType = VisitType.WalkIn,
                CreatedBy = "Siti (Reception)"
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.Created, apptResp.StatusCode);
            var appt = await apptResp.Content.ReadFromJsonAsync<AppointmentQueueItemDto>(jsonOpts);
            Assert.Null(appt!.VisitId);

            var seatResp = await http.PatchAsJsonAsync($"/api/queue/{appt.Id}/status", new UpdateQueueStatusRequest
            {
                NewStatus = QueueStatus.InChair,
                RoomName = appt.RoomName,
                UpdatedBy = "Dr. Aina Rahman"
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.OK, seatResp.StatusCode);
            var seated = await seatResp.Content.ReadFromJsonAsync<AppointmentQueueItemDto>(jsonOpts);
            Assert.NotNull(seated!.VisitId);
            Assert.Equal(2, seated.ActivityLog.Count);

            var ensureResp = await http.PostAsync($"/api/appointments/{appt.Id}/ensure-visit", null);
            Assert.Equal(HttpStatusCode.OK, ensureResp.StatusCode);
            var ensuredVisit = await ensureResp.Content.ReadFromJsonAsync<PatientVisitDto>(jsonOpts);
            Assert.Equal(seated.VisitId!.Value, ensuredVisit!.Id);

            // 3. Chart #26 MOD Completed and update SOAP notes on Visit #1
            var toothResp = await http.PutAsJsonAsync($"/api/visits/{ensuredVisit.Id}/odontogram", new UpdateOdontogramRequest
            {
                BranchId = "HQ-KL",
                ToothNumber = 26,
                Condition = ToothCondition.Filling,
                Surfaces = "mod",
                ProcedureName = "Posterior Composite",
                Fee = 210m,
                Stage = TreatmentStage.Completed,
                DoctorName = "Dr. Aina Rahman",
                UpdatedBy = "Dr. Aina Rahman"
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.OK, toothResp.StatusCode);
            var updatedTooth = await toothResp.Content.ReadFromJsonAsync<OdontogramToothDto>(jsonOpts);
            Assert.Equal("MOD", updatedTooth!.Surfaces);

            var invalidToothResp = await http.PutAsJsonAsync($"/api/visits/{ensuredVisit.Id}/odontogram", new UpdateOdontogramRequest
            {
                BranchId = "HQ-KL",
                ToothNumber = 59,
                Condition = ToothCondition.Caries,
                Stage = TreatmentStage.Proposed
            }, jsonOpts);
            Assert.False(invalidToothResp.IsSuccessStatusCode);

            var soapResp = await http.PutAsJsonAsync($"/api/visits/{ensuredVisit.Id}", new UpdatePatientVisitRequest
            {
                DoctorName = "Dr. Aina Rahman",
                Subjective = "Food packing upper left molar",
                Objective = "MOD caries #26",
                Assessment = "Dental caries #26",
                Plan = "MOD composite restoration completed",
                ToothShade = "A2",
                UpdatedBy = "Dr. Aina Rahman"
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.OK, soapResp.StatusCode);

            // 4. Visit #2 (CarryForward) -> #26 carried forward at RM 0, then reset to Blank
            var v2Resp = await http.PostAsJsonAsync($"/api/patients/{patient.Id}/visits", new CreatePatientVisitRequest
            {
                BranchId = "HQ-KL",
                DoctorName = "Dr. Aina Rahman",
                VisitDate = DateTimeOffset.UtcNow.AddMinutes(5),
                Subjective = "6-month recall examination",
                InitMode = OdontogramInitMode.CarryForward,
                CreatedBy = "Dr. Aina Rahman"
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.Created, v2Resp.StatusCode);
            var visit2 = await v2Resp.Content.ReadFromJsonAsync<PatientVisitDto>(jsonOpts);
            Assert.Equal(2, visit2!.VisitNumber);
            var v2Tooth26 = visit2.Odontogram.Single(t => t.ToothNumber == 26);
            Assert.Equal(ToothCondition.Filling, v2Tooth26.Condition);
            Assert.Equal("MOD", v2Tooth26.Surfaces);
            Assert.Equal(0m, v2Tooth26.Fee);

            var resetResp = await http.PostAsJsonAsync($"/api/visits/{visit2.Id}/odontogram/reset", new ResetVisitOdontogramRequest
            {
                Mode = OdontogramInitMode.Blank,
                UpdatedBy = "Dr. Aina Rahman"
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.OK, resetResp.StatusCode);
            var resetVisit = await resetResp.Content.ReadFromJsonAsync<PatientVisitDto>(jsonOpts);
            Assert.All(resetVisit!.Odontogram, t => Assert.Equal(ToothCondition.Healthy, t.Condition));

            // Visit #1 still preserves #26 MOD
            var v1Teeth = await http.GetFromJsonAsync<List<OdontogramToothDto>>(
                $"/api/visits/{ensuredVisit.Id}/odontogram",
                jsonOpts);
            Assert.Equal("MOD", v1Teeth!.Single(t => t.ToothNumber == 26).Surfaces);

            var allVisits = await http.GetFromJsonAsync<List<PatientVisitDto>>(
                $"/api/patients/{patient.Id}/visits",
                jsonOpts);
            Assert.Equal(2, allVisits!.Count);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public void DesktopXamlAnatomyCatalogAndMigration_ComplyWithUiAndSchemaContract()
    {
        var repoRoot = ResolveRepoRoot();
        var desktopRoot = Path.Combine(repoRoot, "src", "Occlusa.Desktop");
        var xamlFiles = Directory.GetFiles(desktopRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();
        Assert.NotEmpty(xamlFiles);

        foreach (var file in xamlFiles)
        {
            var content = File.ReadAllText(file);

            Assert.DoesNotContain("VerticalScrollBarVisibility=\"Auto\"", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("VerticalScrollBarVisibility=\"Visible\"", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("HorizontalScrollBarVisibility=\"Auto\"", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("HorizontalScrollBarVisibility=\"Visible\"", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CharacterEllipsis", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("WordEllipsis", content, StringComparison.OrdinalIgnoreCase);

            // Panel-fitted single-line uniformity contract: no multi-line table rows, no narrow content-shrunk tables, no legacy 4-col hard cap
            Assert.DoesNotContain("AllowAutoRowHeight=\"True\"", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("FitColumnsToContent=\"True\"", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("EnforceColumnLimit=\"True\"", content, StringComparison.OrdinalIgnoreCase);

            // Every UniformPagedGrid column definition must declare an explicit responsive Priority
            foreach (Match colMatch in Regex.Matches(content, @"<controls:UniformColumnDef\s[^>]*>", RegexOptions.Singleline))
            {
                Assert.Contains("Priority=", colMatch.Value, StringComparison.Ordinal);
            }

            if (Path.GetFileName(file).EndsWith("Modal.xaml", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains("ModalDialogShell", content, StringComparison.Ordinal);
            }

            // Guard against WPF DataContext-shadowing bug where Visibility and DataContext sit on the same element
            Assert.DoesNotMatch(
                new Regex(@"<[^>]*Visibility=""\{Binding\s+Is\w+Workspace[^>]*DataContext=""\{Binding", RegexOptions.Singleline),
                content);
            Assert.DoesNotMatch(
                new Regex(@"<[^>]*DataContext=""\{Binding[^>]*Visibility=""\{Binding\s+Is\w+Workspace", RegexOptions.Singleline),
                content);
        }

        // Obsolete patient-wide OdontogramModal stays removed
        Assert.False(File.Exists(Path.Combine(desktopRoot, "Views", "OdontogramModal.xaml")));

        // Clinical/patient modals pin MedicalAlertsBanner; list workspaces in MainWindow omit it for visual calm
        Assert.DoesNotContain(
            "MedicalAlertsBanner",
            File.ReadAllText(Path.Combine(desktopRoot, "MainWindow.xaml")),
            StringComparison.Ordinal);

        foreach (var clinicalSurface in new[]
        {
            Path.Combine(desktopRoot, "Views", "PatientDetailModal.xaml"),
            Path.Combine(desktopRoot, "Views", "VisitRecordModal.xaml"),
            Path.Combine(desktopRoot, "Views", "AppointmentDetailModal.xaml")
        })
        {
            Assert.True(File.Exists(clinicalSurface), clinicalSurface);
            Assert.Contains("MedicalAlertsBanner", File.ReadAllText(clinicalSurface), StringComparison.Ordinal);
        }

        // ToothAnatomyCatalog defines right-quadrant templates for permanent & primary upper arches (mirrored for left)
        var catalogSource = File.ReadAllText(Path.Combine(desktopRoot, "Controls", "ToothAnatomyCatalog.cs"));
        foreach (var templateTooth in new[] { 11, 12, 13, 14, 15, 16, 17, 18, 51, 52, 53, 54, 55 })
        {
            Assert.Matches(new Regex($@"\[{templateTooth}\]\s*=\s*new\("), catalogSource);
        }
        Assert.Contains("React-Advanced-Odontogram", catalogSource, StringComparison.Ordinal);

        // VisitRecordModal must remain 100% free of monetary Fee inputs
        var visitModalXaml = File.ReadAllText(Path.Combine(desktopRoot, "Views", "VisitRecordModal.xaml"));
        Assert.DoesNotContain("FeeInput", visitModalXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Fee (RM)", visitModalXaml, StringComparison.Ordinal);

        // SQL migration 005 & 006 include visit tables, decoupled billing, credit ledger, settings, immutability triggers, and RLS
        var migrationSql = File.ReadAllText(Path.Combine(repoRoot, "supabase", "migrations", "005_patient_visits_and_visit_odontograms.sql"));
        Assert.Contains("CREATE TABLE IF NOT EXISTS public.patient_visits", migrationSql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS public.visit_odontogram_entries", migrationSql, StringComparison.Ordinal);
        Assert.Contains("prevent_hard_delete()", migrationSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE public.patient_visits ENABLE ROW LEVEL SECURITY", migrationSql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE public.visit_odontogram_entries ENABLE ROW LEVEL SECURITY", migrationSql, StringComparison.Ordinal);

        var migration006 = File.ReadAllText(Path.Combine(repoRoot, "supabase", "migrations", "006_decoupled_billing_credits_and_settings.sql"));
        Assert.Contains("CREATE TABLE IF NOT EXISTS public.patient_credit_ledger", migration006, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS public.billing_catalog_items", migration006, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS public.doctor_invoice_preferences", migration006, StringComparison.Ordinal);

        var migration007 = File.ReadAllText(Path.Combine(repoRoot, "supabase", "migrations", "007_reminders_and_user_preferences.sql"));
        Assert.Contains("reminder_status", migration007, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS public.user_workspace_preferences", migration007, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE public.user_workspace_preferences ENABLE ROW LEVEL SECURITY", migration007, StringComparison.Ordinal);
    }
}
