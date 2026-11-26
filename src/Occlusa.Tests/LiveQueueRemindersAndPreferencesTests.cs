using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Occlusa.Api;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Tests;

public class LiveQueueRemindersAndPreferencesTests
{
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        opts.Converters.Add(new JsonStringEnumConverter());
        return opts;
    }

    [Fact]
    public void BuildDefaultUserPreferences_AppliesRoleSmartSimpleDefaultsAndPresets()
    {
        var dentists = new List<DentistDto>
        {
            new() { Id = Guid.NewGuid(), FullName = "Dr. Aina Rahman", DefaultRoom = "Suite 1" },
            new() { Id = Guid.NewGuid(), FullName = "Dr. Marcus Tan", DefaultRoom = "Suite 2" }
        };

        var dentistPref = AppointmentCalendarHelper.BuildDefaultUserPreferences("Dr. Aina Rahman", dentists);
        Assert.Equal("dr. aina rahman", dentistPref.StaffKey);
        Assert.Equal(LiveQueueViewMode.Simple, dentistPref.QueueViewMode);
        Assert.Equal(LiveQueueScopeMode.MyQueue, dentistPref.DefaultQueueScope);
        Assert.True(dentistPref.HideFinishedQueueRows);
        Assert.False(dentistPref.ShowWaitTimers);
        Assert.False(dentistPref.ShowInlineReceptionNotes);
        Assert.False(dentistPref.ShowMyKadQuickBar);

        var receptionPref = AppointmentCalendarHelper.BuildDefaultUserPreferences("Siti (Reception)", dentists);
        Assert.Equal("siti (reception)", receptionPref.StaffKey);
        Assert.Equal(LiveQueueViewMode.Simple, receptionPref.QueueViewMode);
        Assert.Equal(LiveQueueScopeMode.AllClinic, receptionPref.DefaultQueueScope);
        Assert.True(receptionPref.HideFinishedQueueRows);
        Assert.False(receptionPref.ShowMyKadQuickBar);

        AppointmentCalendarHelper.ApplyViewModePreset(receptionPref, LiveQueueViewMode.Detailed, isDentist: false);
        Assert.Equal(LiveQueueViewMode.Detailed, receptionPref.QueueViewMode);
        Assert.False(receptionPref.HideFinishedQueueRows);
        Assert.True(receptionPref.ShowWaitTimers);
        Assert.True(receptionPref.ShowInlineReceptionNotes);
        Assert.True(receptionPref.ShowMyKadQuickBar);

        AppointmentCalendarHelper.ApplyViewModePreset(dentistPref, LiveQueueViewMode.Detailed, isDentist: true);
        Assert.Equal(LiveQueueViewMode.Detailed, dentistPref.QueueViewMode);
        Assert.False(dentistPref.ShowMyKadQuickBar);
        Assert.True(dentistPref.ShowWaitTimers);
    }

    [Fact]
    public void FilterLiveQueue_AppliesMyQueueScopeHideFinishedAndDetailedStatusChips()
    {
        var dAina = new DentistDto { Id = Guid.NewGuid(), FullName = "Dr. Aina Rahman" };
        var dMarcus = new DentistDto { Id = Guid.NewGuid(), FullName = "Dr. Marcus Tan" };
        var dentists = new List<DentistDto> { dAina, dMarcus };
        var now = DateTimeOffset.UtcNow;

        var items = new List<AppointmentQueueItemDto>
        {
            new()
            {
                Id = Guid.NewGuid(), PatientName = "Aina Waiting", DentistId = dAina.Id, DoctorName = dAina.FullName,
                Status = QueueStatus.Waiting, VisitType = VisitType.Booked, ScheduledAt = now, ArrivedAt = now.AddMinutes(-10)
            },
            new()
            {
                Id = Guid.NewGuid(), PatientName = "Aina Unreminded", DentistId = dAina.Id, DoctorName = dAina.FullName,
                Status = QueueStatus.Scheduled, VisitType = VisitType.Booked, ReminderStatus = ReminderStatus.NotReminded, ScheduledAt = now.AddHours(1)
            },
            new()
            {
                Id = Guid.NewGuid(), PatientName = "Aina Completed", DentistId = dAina.Id, DoctorName = dAina.FullName,
                Status = QueueStatus.Completed, VisitType = VisitType.Booked, ScheduledAt = now.AddHours(-2)
            },
            new()
            {
                Id = Guid.NewGuid(), PatientName = "Marcus Confirmed", DentistId = dMarcus.Id, DoctorName = dMarcus.FullName,
                Status = QueueStatus.Scheduled, VisitType = VisitType.Booked, ReminderStatus = ReminderStatus.Confirmed, ScheduledAt = now.AddHours(2)
            },
            new()
            {
                Id = Guid.NewGuid(), PatientName = "Marcus WalkIn", DentistId = dMarcus.Id, DoctorName = dMarcus.FullName,
                Status = QueueStatus.Waiting, VisitType = VisitType.WalkIn, ScheduledAt = now, ArrivedAt = now.AddMinutes(-20)
            }
        };

        // Dentist Simple View: MyQueue + hide finished -> only Aina's 2 active rows
        var ainaActive = AppointmentCalendarHelper.FilterLiveQueue(
            items, LiveQueueScopeMode.MyQueue, dAina.FullName, dentists, includeFinished: false);
        Assert.Equal(2, ainaActive.Count);
        Assert.All(ainaActive, a => Assert.Equal(dAina.Id, a.DentistId));
        Assert.DoesNotContain(ainaActive, a => a.Status == QueueStatus.Completed);

        // Dentist Show Done toggle includes completed row
        var ainaAll = AppointmentCalendarHelper.FilterLiveQueue(
            items, LiveQueueScopeMode.MyQueue, dAina.FullName, dentists, includeFinished: true);
        Assert.Equal(3, ainaAll.Count);

        // Detailed View: AllClinic + Unreminded chip -> only upcoming booked unreminded row (excludes Walk-In & Confirmed)
        var unreminded = AppointmentCalendarHelper.FilterLiveQueue(
            items, LiveQueueScopeMode.AllClinic, "Siti (Reception)", dentists, statusChip: "Unreminded", includeFinished: false);
        Assert.Single(unreminded);
        Assert.Equal("Aina Unreminded", unreminded[0].PatientName);

        // Detailed View: AllClinic + Doctor filter for Dr. Marcus + Waiting chip
        var marcusWaiting = AppointmentCalendarHelper.FilterLiveQueue(
            items, LiveQueueScopeMode.AllClinic, "Siti (Reception)", dentists,
            detailedDoctorId: dMarcus.Id, detailedDoctorName: dMarcus.FullName, statusChip: "Waiting", includeFinished: false);
        Assert.Single(marcusWaiting);
        Assert.Equal("Marcus WalkIn", marcusWaiting[0].PatientName);
    }

    [Fact]
    public void ReminderHelpers_FormatBadgesBuildBilingualMessageAndGuardNegations()
    {
        var offset = TimeSpan.FromHours(8);
        var booked = new AppointmentQueueItemDto
        {
            PatientName = "Nurul Aisyah binti Zulkifli",
            DoctorName = "Dr. Aina Rahman",
            ProcedureSummary = "Routine Scaling & Polishing",
            BranchId = "HQ-KL",
            ScheduledAt = new DateTimeOffset(2026, 11, 26, 14, 0, 0, offset),
            Status = QueueStatus.Scheduled,
            VisitType = VisitType.Booked,
            ReminderStatus = ReminderStatus.NotReminded
        };

        Assert.Equal("Not Reminded", AppointmentCalendarHelper.FormatReminderBadgeLabel(booked));

        booked.ReminderStatus = ReminderStatus.Reminded;
        booked.ReminderChannel = ReminderChannel.WhatsApp;
        Assert.Equal("Reminded · WA", AppointmentCalendarHelper.FormatReminderBadgeLabel(booked));

        booked.ReminderStatus = ReminderStatus.Confirmed;
        Assert.Equal("✓ Confirmed", AppointmentCalendarHelper.FormatReminderBadgeLabel(booked));

        booked.Status = QueueStatus.Waiting;
        Assert.Equal("Arrived", AppointmentCalendarHelper.FormatReminderBadgeLabel(booked));

        var walkIn = new AppointmentQueueItemDto { VisitType = VisitType.WalkIn, Status = QueueStatus.Waiting };
        Assert.Equal("Walk-In", AppointmentCalendarHelper.FormatReminderBadgeLabel(walkIn));

        var msg = AppointmentCalendarHelper.BuildBilingualReminderMessage(booked, "HQ-KL", offset);
        Assert.Contains("Reply YES to confirm", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Balas YA untuk sahkan", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dr. Aina Rahman", msg, StringComparison.Ordinal);

        // Positive confirmation replies (EN & BM)
        Assert.True(AppointmentCalendarHelper.IsReminderConfirmationReply("Yes"));
        Assert.True(AppointmentCalendarHelper.IsReminderConfirmationReply("Ya, saya datang"));
        Assert.True(AppointmentCalendarHelper.IsReminderConfirmationReply("Confirmed, see you!"));
        Assert.True(AppointmentCalendarHelper.IsReminderConfirmationReply("Ok baik"));

        // Negations & reschedule requests MUST NOT falsely confirm
        Assert.False(AppointmentCalendarHelper.IsReminderConfirmationReply("No, I cannot come"));
        Assert.False(AppointmentCalendarHelper.IsReminderConfirmationReply("Tak boleh datang hari ini"));
        Assert.False(AppointmentCalendarHelper.IsReminderConfirmationReply("Can I reschedule to Friday?"));
        Assert.False(AppointmentCalendarHelper.IsReminderConfirmationReply("Sorry need to cancel"));
        Assert.False(AppointmentCalendarHelper.IsReminderConfirmationReply("Hi what are your clinic hours?"));
    }

    [Fact]
    public async Task HttpPipeline_ReminderWorkflow_WhatsAppSendManualUpdateAndAutoConfirmReply()
    {
        await using var app = OcclusaApiApplicationFactory.BuildApplication(
            urls: "http://127.0.0.1:0",
            environmentName: "Development");
        await app.StartAsync();

        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Occlusa-User", "Siti (Reception)");
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Occlusa-Allowed-Branches", "HQ-KL,PJ-SS2");
            var jsonOpts = CreateJsonOptions();

            var snapshot = await http.GetFromJsonAsync<ClinicDashboardSnapshotDto>(
                "/api/dashboard/snapshot?branchId=HQ-KL",
                jsonOpts);
            Assert.NotNull(snapshot);

            // Pick a seeded patient and create a fresh upcoming Scheduled appointment
            var patient = snapshot!.Patients.First(p => !string.IsNullOrWhiteSpace(p.Phone));
            var createResp = await http.PostAsJsonAsync("/api/appointments", new CreateAppointmentRequest
            {
                BranchId = "HQ-KL",
                PatientId = patient.Id,
                DentistId = snapshot.Dentists[0].Id,
                DoctorName = snapshot.Dentists[0].FullName,
                TreatmentId = snapshot.Treatments[0].Id,
                RoomName = snapshot.Dentists[0].DefaultRoom,
                ScheduledAt = DateTimeOffset.UtcNow.AddHours(3),
                DurationMinutes = 30,
                ProcedureSummary = "Routine Scaling & Polishing",
                Status = QueueStatus.Scheduled,
                VisitType = VisitType.Booked,
                CreatedBy = "Siti (Reception)"
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
            var appt = await createResp.Content.ReadFromJsonAsync<AppointmentQueueItemDto>(jsonOpts);
            Assert.NotNull(appt);
            Assert.Equal(ReminderStatus.NotReminded, appt!.ReminderStatus);

            // 1. Send WhatsApp Reminder via hybrid endpoint
            var sendResp = await http.PostAsJsonAsync($"/api/appointments/{appt.Id}/reminder", new UpdateAppointmentReminderRequest
            {
                Status = ReminderStatus.Reminded,
                Channel = ReminderChannel.WhatsApp,
                SendWhatsAppMessage = true,
                UpdatedBy = "Siti (Reception)"
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.OK, sendResp.StatusCode);
            var reminderResult = await sendResp.Content.ReadFromJsonAsync<UpdateAppointmentReminderResponse>(jsonOpts);
            Assert.NotNull(reminderResult);
            Assert.True(reminderResult!.WhatsAppMessageSent);
            Assert.Equal(ReminderStatus.Reminded, reminderResult.Appointment.ReminderStatus);
            Assert.Equal(ReminderChannel.WhatsApp, reminderResult.Appointment.ReminderChannel);
            Assert.NotNull(reminderResult.Appointment.RemindedAt);
            Assert.Equal("Siti (Reception)", reminderResult.Appointment.RemindedBy);
            Assert.NotNull(reminderResult.WhatsAppThread);
            Assert.Contains(reminderResult.WhatsAppThread!.Messages, m => m.Sender.Contains("Reminder", StringComparison.OrdinalIgnoreCase));

            // 2. Patient replies "Ya confirm" on WhatsApp -> auto-flips appointment to Confirmed
            var waReplyResp = await http.PostAsJsonAsync("/api/whatsapp/simulate", new WhatsAppSimulateRequest
            {
                ThreadId = reminderResult.WhatsAppThread.ThreadId,
                PatientPhone = patient.Phone,
                PatientName = patient.FullName,
                MessageText = "Ya confirm, see you later!",
                BranchId = "HQ-KL"
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.OK, waReplyResp.StatusCode);
            var waThread = await waReplyResp.Content.ReadFromJsonAsync<WhatsAppThreadDto>(jsonOpts);
            Assert.NotNull(waThread);
            Assert.Contains("Confirmed", waThread!.Messages.Last().Text, StringComparison.OrdinalIgnoreCase);

            var refreshedSnapshot = await http.GetFromJsonAsync<ClinicDashboardSnapshotDto>(
                "/api/dashboard/snapshot?branchId=HQ-KL",
                jsonOpts);
            var confirmedAppt = refreshedSnapshot!.Queue.First(a => a.Id == appt.Id);
            Assert.Equal(ReminderStatus.Confirmed, confirmedAppt.ReminderStatus);
            Assert.NotNull(confirmedAppt.ReminderConfirmedAt);

            // 3. Manual PhoneCall / NoReply update works cleanly without sending WhatsApp
            var manualResp = await http.PostAsJsonAsync($"/api/appointments/{appt.Id}/reminder", new UpdateAppointmentReminderRequest
            {
                Status = ReminderStatus.NoReply,
                Channel = ReminderChannel.PhoneCall,
                SendWhatsAppMessage = false,
                UpdatedBy = "Aisyah (Reception)"
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.OK, manualResp.StatusCode);
            var manualResult = await manualResp.Content.ReadFromJsonAsync<UpdateAppointmentReminderResponse>(jsonOpts);
            Assert.False(manualResult!.WhatsAppMessageSent);
            Assert.Equal(ReminderStatus.NoReply, manualResult.Appointment.ReminderStatus);
            Assert.Equal(ReminderChannel.PhoneCall, manualResult.Appointment.ReminderChannel);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task HttpPipeline_UserWorkspacePreferences_PersistPerStaffAndHydrateInSnapshot()
    {
        await using var app = OcclusaApiApplicationFactory.BuildApplication(
            urls: "http://127.0.0.1:0",
            environmentName: "Development");
        await app.StartAsync();

        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Occlusa-User", "Dr. Aina Rahman");
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Occlusa-Allowed-Branches", "HQ-KL,PJ-SS2");
            var jsonOpts = CreateJsonOptions();

            var saveAina = await http.PutAsJsonAsync("/api/user-preferences", new SaveUserWorkspacePreferencesRequest
            {
                StaffKey = "Dr. Aina Rahman",
                StaffDisplayName = "Dr. Aina Rahman",
                QueueViewMode = LiveQueueViewMode.Simple,
                DefaultQueueScope = LiveQueueScopeMode.MyQueue,
                HideFinishedQueueRows = true,
                ShowWaitTimers = true,
                ShowInlineReceptionNotes = false,
                ShowMyKadQuickBar = false
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.OK, saveAina.StatusCode);

            var saveSiti = await http.PutAsJsonAsync("/api/user-preferences", new SaveUserWorkspacePreferencesRequest
            {
                StaffKey = "Siti (Reception)",
                StaffDisplayName = "Siti (Reception)",
                QueueViewMode = LiveQueueViewMode.Detailed,
                DefaultQueueScope = LiveQueueScopeMode.AllClinic,
                HideFinishedQueueRows = false,
                ShowWaitTimers = true,
                ShowInlineReceptionNotes = true,
                ShowMyKadQuickBar = true
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.OK, saveSiti.StatusCode);

            var allPrefs = await http.GetFromJsonAsync<List<UserWorkspacePreferencesDto>>("/api/user-preferences", jsonOpts);
            Assert.NotNull(allPrefs);
            Assert.Equal(2, allPrefs!.Count);

            var snapshot = await http.GetFromJsonAsync<ClinicDashboardSnapshotDto>("/api/dashboard/snapshot?branchId=HQ-KL", jsonOpts);
            Assert.NotNull(snapshot);

            var ainaPref = snapshot!.UserPreferences.First(p => p.StaffKey == "dr. aina rahman");
            Assert.Equal(LiveQueueViewMode.Simple, ainaPref.QueueViewMode);
            Assert.Equal(LiveQueueScopeMode.MyQueue, ainaPref.DefaultQueueScope);
            Assert.True(ainaPref.ShowWaitTimers);
            Assert.False(ainaPref.ShowMyKadQuickBar);

            var sitiPref = snapshot.UserPreferences.First(p => p.StaffKey == "siti (reception)");
            Assert.Equal(LiveQueueViewMode.Detailed, sitiPref.QueueViewMode);
            Assert.Equal(LiveQueueScopeMode.AllClinic, sitiPref.DefaultQueueScope);
            Assert.False(sitiPref.HideFinishedQueueRows);
            Assert.True(sitiPref.ShowMyKadQuickBar);
        }
        finally
        {
            await app.StopAsync();
        }
    }
}

