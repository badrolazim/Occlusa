using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Occlusa.Api;
using Occlusa.Api.Controllers;
using Occlusa.Api.Hubs;
using Occlusa.Api.Options;
using Occlusa.Api.Repositories;
using Occlusa.Api.Security;
using Occlusa.Api.Services;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Tests;

public class AppointmentSchedulingTests
{
    private static InMemoryOcclusaRepository CreateRepository() =>
        new(Options.Create(new ClinicOptions()), new HttpContextAccessor());

    [Fact]
    public void GetDateRange_ComputesMondayToSundayWeekAndLeapYearMonth()
    {
        // Sunday 2026-11-29 belongs to week Mon 2026-11-23 .. Sun 2026-11-29
        var sunday = new DateOnly(2026, 11, 29);
        var (dayStart, dayEnd) = AppointmentCalendarHelper.GetDateRange(sunday, AppointmentCalendarViewMode.Day);
        Assert.Equal(sunday, dayStart);
        Assert.Equal(sunday, dayEnd);

        var (weekStart, weekEnd) = AppointmentCalendarHelper.GetDateRange(sunday, AppointmentCalendarViewMode.Week);
        Assert.Equal(new DateOnly(2026, 11, 23), weekStart);
        Assert.Equal(new DateOnly(2026, 11, 29), weekEnd);

        var leapFeb = new DateOnly(2028, 2, 14);
        var (monthStart, monthEnd) = AppointmentCalendarHelper.GetDateRange(leapFeb, AppointmentCalendarViewMode.Month);
        Assert.Equal(new DateOnly(2028, 2, 1), monthStart);
        Assert.Equal(new DateOnly(2028, 2, 29), monthEnd);
    }

    [Fact]
    public void GetMonthGridDates_AlwaysReturnsMondayAlignedFiveOrSixWeeks()
    {
        for (int month = 1; month <= 12; month++)
        {
            var anchor = new DateOnly(2026, month, 15);
            var grid = AppointmentCalendarHelper.GetMonthGridDates(anchor);

            Assert.True(grid.Count == 35 || grid.Count == 42);
            Assert.Equal(DayOfWeek.Monday, grid[0].DayOfWeek);
            Assert.Equal(DayOfWeek.Sunday, grid[^1].DayOfWeek);
            Assert.Contains(new DateOnly(2026, month, 1), grid);
            Assert.Contains(new DateOnly(2026, month, DateTime.DaysInMonth(2026, month)), grid);
        }
    }

    [Fact]
    public void GetDayTimeSlots_ReturnsNineSlotsAcrossAllSessions()
    {
        var fullDay = AppointmentCalendarHelper.GetDayTimeSlots(DaySlotSession.FullDay);
        var morning = AppointmentCalendarHelper.GetDayTimeSlots(DaySlotSession.Morning);
        var afternoon = AppointmentCalendarHelper.GetDayTimeSlots(DaySlotSession.Afternoon);

        Assert.Equal(9, fullDay.Count);
        Assert.Equal(new TimeOnly(9, 0), fullDay[0].Start);
        Assert.Equal(new TimeOnly(17, 0), fullDay[^1].Start);

        Assert.Equal(9, morning.Count);
        Assert.Equal(new TimeOnly(9, 0), morning[0].Start);
        Assert.Equal(new TimeOnly(13, 0), morning[^1].Start);

        Assert.Equal(9, afternoon.Count);
        Assert.Equal(new TimeOnly(13, 30), afternoon[0].Start);
        Assert.Equal(new TimeOnly(17, 30), afternoon[^1].Start);
    }

    [Fact]
    public void DoesAppointmentMatchSlot_AnchorsRegularAndEdgeHours()
    {
        var offset = TimeSpan.FromHours(8);
        var fullDay = AppointmentCalendarHelper.GetDayTimeSlots(DaySlotSession.FullDay);

        var early = new DateTimeOffset(2026, 11, 23, 8, 15, 0, offset);
        var mid = new DateTimeOffset(2026, 11, 23, 11, 30, 0, offset);
        var late = new DateTimeOffset(2026, 11, 23, 18, 45, 0, offset);

        Assert.True(AppointmentCalendarHelper.DoesAppointmentMatchSlot(early, offset, fullDay[0], DaySlotSession.FullDay));
        Assert.True(AppointmentCalendarHelper.DoesAppointmentMatchSlot(mid, offset, fullDay[2], DaySlotSession.FullDay));
        Assert.True(AppointmentCalendarHelper.DoesAppointmentMatchSlot(late, offset, fullDay[^1], DaySlotSession.FullDay));
    }

    [Fact]
    public void FilterAppointments_AppliesDateDentistTreatmentAndPatientSearch()
    {
        var offset = TimeSpan.FromHours(8);
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        var items = new[]
        {
            new AppointmentQueueItemDto
            {
                PatientName = "Nurul Aisyah binti Zulkifli",
                PatientIc = "920415-14-5678",
                DentistId = d1,
                DoctorName = "Dr. Aina Rahman",
                TreatmentId = t1,
                ProcedureSummary = "Routine Scaling & Polishing",
                ScheduledAt = new DateTimeOffset(2026, 11, 23, 9, 30, 0, offset)
            },
            new AppointmentQueueItemDto
            {
                PatientName = "Tan Wei Ming",
                PatientIc = "881103-10-4321",
                DentistId = d2,
                DoctorName = "Dr. Marcus Tan",
                TreatmentId = t2,
                ProcedureSummary = "Root Canal Therapy",
                ScheduledAt = new DateTimeOffset(2026, 11, 25, 14, 0, 0, offset)
            }
        };

        var filtered = AppointmentCalendarHelper.FilterAppointments(
            items,
            new DateOnly(2026, 11, 23),
            new DateOnly(2026, 11, 23),
            d1,
            t1,
            "920415",
            offset);

        Assert.Single(filtered);
        Assert.Equal("Nurul Aisyah binti Zulkifli", filtered[0].PatientName);
    }

    [Fact]
    public void IsTodayForLiveQueue_KeepsTodayAndActiveVisits_ExcludesFutureScheduled()
    {
        var offset = TimeSpan.FromHours(8);
        var today = new DateOnly(2026, 11, 23);

        var todayScheduled = new AppointmentQueueItemDto
        {
            ScheduledAt = new DateTimeOffset(2026, 11, 23, 11, 0, 0, offset),
            Status = QueueStatus.Scheduled
        };
        var tomorrowScheduled = new AppointmentQueueItemDto
        {
            ScheduledAt = new DateTimeOffset(2026, 11, 24, 11, 0, 0, offset),
            Status = QueueStatus.Scheduled
        };
        var yesterdayInChair = new AppointmentQueueItemDto
        {
            ScheduledAt = new DateTimeOffset(2026, 11, 22, 16, 0, 0, offset),
            Status = QueueStatus.InChair
        };

        Assert.True(AppointmentCalendarHelper.IsTodayForLiveQueue(todayScheduled, today, offset));
        Assert.False(AppointmentCalendarHelper.IsTodayForLiveQueue(tomorrowScheduled, today, offset));
        Assert.True(AppointmentCalendarHelper.IsTodayForLiveQueue(yesterdayInChair, today, offset));
    }

    [Fact]
    public void CompactFormatting_NeverEmitsEllipsis()
    {
        var nameFull = AppointmentCalendarHelper.FormatCompactPatientName("Nurul Aisyah binti Zulkifli", shortForm: false);
        var nameShort = AppointmentCalendarHelper.FormatCompactPatientName("Nurul Aisyah binti Zulkifli", shortForm: true);
        var treatment = AppointmentCalendarHelper.FormatCompactTreatment("Crown Preparation & Cementation on Molar #46", 20);

        Assert.DoesNotContain("...", nameFull);
        Assert.DoesNotContain("...", nameShort);
        Assert.DoesNotContain("...", treatment);
        Assert.Equal("Nurul A.", nameShort);
    }

    [Fact]
    public void SnapshotDto_WithDentistsTreatmentsAndDuration_RoundTripsCleanly()
    {
        var dentistId = Guid.NewGuid();
        var treatmentId = Guid.NewGuid();
        var snapshot = new ClinicDashboardSnapshotDto
        {
            BranchId = "HQ-KL",
            Dentists =
            [
                new DentistDto
                {
                    Id = dentistId,
                    FullName = "Dr. Aina Rahman",
                    Specialty = "General & Restorative",
                    DefaultRoom = "Suite 1",
                    BranchIds = ["HQ-KL", "PJ-SS2"]
                }
            ],
            Treatments =
            [
                new TreatmentDto
                {
                    Id = treatmentId,
                    Name = "Posterior Composite Filling",
                    Category = "Restorative",
                    DefaultDurationMinutes = 45,
                    DefaultFee = 200.00m
                }
            ],
            Queue =
            [
                new AppointmentQueueItemDto
                {
                    BranchId = "HQ-KL",
                    PatientName = "Nurul Aisyah",
                    DentistId = dentistId,
                    DoctorName = "Dr. Aina Rahman",
                    TreatmentId = treatmentId,
                    DurationMinutes = 45,
                    Notes = "Penicillin allergy"
                }
            ]
        };

        var json = JsonSerializer.Serialize(snapshot);
        var restored = JsonSerializer.Deserialize<ClinicDashboardSnapshotDto>(json);

        Assert.NotNull(restored);
        Assert.Single(restored!.Dentists);
        Assert.Single(restored.Treatments);
        Assert.Equal(dentistId, restored.Queue[0].DentistId);
        Assert.Equal(treatmentId, restored.Queue[0].TreatmentId);
        Assert.Equal(45, restored.Queue[0].DurationMinutes);
        Assert.Equal("Penicillin allergy", restored.Queue[0].Notes);
    }

    [Fact]
    public async Task InMemoryRepository_DentistsAndTreatmentsCatalogs_AreBranchAware()
    {
        var repo = CreateRepository();

        var klDentists = await repo.GetDentistsAsync("HQ-KL");
        var pjDentists = await repo.GetDentistsAsync("PJ-SS2");
        var treatments = await repo.GetTreatmentsAsync();

        Assert.Equal(3, klDentists.Count);
        Assert.Equal(2, pjDentists.Count);
        Assert.Equal(7, treatments.Count);
    }

    [Fact]
    public async Task InMemoryRepository_CreateAllowsOverlappingBookings_AndUpdateLogsAudit()
    {
        var repo = CreateRepository();
        var patients = await repo.GetPatientsAsync();
        var dentists = await repo.GetDentistsAsync("HQ-KL");
        var treatments = await repo.GetTreatmentsAsync();

        var slotTime = new DateTimeOffset(DateTime.Today.AddDays(3).AddHours(10), DateTimeOffset.Now.Offset);

        var first = await repo.CreateAppointmentAsync(new CreateAppointmentRequest
        {
            BranchId = "HQ-KL",
            PatientId = patients[0].Id,
            DentistId = dentists[0].Id,
            TreatmentId = treatments[2].Id,
            ScheduledAt = slotTime,
            DurationMinutes = 0, // Resolves from treatment default (45m)
            Notes = "First overlapping booking"
        });

        // Second overlapping appointment for same dentist & time must succeed unrestricted
        var second = await repo.CreateAppointmentAsync(new CreateAppointmentRequest
        {
            BranchId = "HQ-KL",
            PatientId = patients[1].Id,
            DentistId = dentists[0].Id,
            TreatmentId = treatments[0].Id,
            ScheduledAt = slotTime,
            DurationMinutes = 30,
            Notes = "Concurrent squeeze-in"
        });

        Assert.Equal(dentists[0].FullName, first.DoctorName);
        Assert.Equal(treatments[2].Name, first.ProcedureSummary);
        Assert.Equal(treatments[2].DefaultDurationMinutes, first.DurationMinutes);
        Assert.NotEqual(first.Id, second.Id);

        var newTime = slotTime.AddHours(2);
        var (updated, error) = await repo.UpdateAppointmentAsync(second.Id, new UpdateAppointmentRequest
        {
            PatientId = patients[1].Id,
            DentistId = dentists[1].Id,
            DoctorName = dentists[1].FullName,
            TreatmentId = treatments[4].Id,
            ProcedureSummary = treatments[4].Name,
            RoomName = dentists[1].DefaultRoom,
            ScheduledAt = newTime,
            DurationMinutes = 60,
            Status = QueueStatus.Cancelled,
            Notes = "Cancelled by patient via phone",
            UpdatedBy = "Receptionist"
        });

        Assert.Null(error);
        Assert.NotNull(updated);
        Assert.Equal(dentists[1].Id, updated!.DentistId);
        Assert.Equal(60, updated.DurationMinutes);
        Assert.Equal(QueueStatus.Cancelled, updated.Status);
        Assert.Equal("Cancelled by patient via phone", updated.Notes);

        var logs = await repo.GetAuditLogsAsync("HQ-KL");
        Assert.Contains(logs, l => l.Action == "APPOINTMENT_UPDATED" && l.EntityId == second.Id.ToString());
    }

    [Fact]
    public async Task InMemoryRepository_RegisterPatientAndBook_CreatesPatientAndLinkedAppointmentAtomically()
    {
        var repo = CreateRepository();
        var dentists = await repo.GetDentistsAsync("HQ-KL");
        var treatments = await repo.GetTreatmentsAsync();
        var slotTime = new DateTimeOffset(DateTime.Today.AddDays(4).AddHours(11), DateTimeOffset.Now.Offset);

        var response = await repo.RegisterPatientAndCreateAppointmentAsync(new RegisterPatientAndAppointmentRequest
        {
            Patient = new UpsertPatientRequest
            {
                FullName = "Amina binti Yusoff",
                IcNumber = "960824-14-5218",
                Phone = "+6012-9098877",
                PrimaryBranchId = "HQ-KL",
                MedicalAlerts = new MedicalAlertDto
                {
                    Allergies = "Aspirin",
                    HasDiabetes = true
                }
            },
            Appointment = new CreateAppointmentRequest
            {
                BranchId = "HQ-KL",
                DentistId = dentists[0].Id,
                TreatmentId = treatments[2].Id,
                ScheduledAt = slotTime,
                DurationMinutes = 45,
                ProcedureSummary = treatments[2].Name,
                Notes = "First visit booking"
            }
        });

        Assert.Equal("Amina binti Yusoff", response.Patient.FullName);
        Assert.Equal("960824-14-5218", response.Patient.IcNumber);
        Assert.Equal(new DateOnly(1996, 8, 24), response.Patient.DateOfBirth);
        Assert.Equal("Female", response.Patient.Gender);
        Assert.True(response.Patient.MedicalAlerts.HasAnyCriticalAlert);

        Assert.Equal(response.Patient.Id, response.Appointment.PatientId);
        Assert.Equal("Amina binti Yusoff", response.Appointment.PatientName);
        Assert.Equal("960824-14-5218", response.Appointment.PatientIc);
        Assert.True(response.Appointment.HasCriticalMedicalAlert);
        Assert.Contains("Aspirin", response.Appointment.AlertSummary);
        Assert.Contains("Diabetes", response.Appointment.AlertSummary);
        Assert.Equal(45, response.Appointment.DurationMinutes);

        var allPatients = await repo.GetPatientsAsync("960824-14-5218");
        Assert.Single(allPatients);

        var branchAppointments = await repo.GetAppointmentsAsync("HQ-KL");
        Assert.Contains(branchAppointments, a => a.Id == response.Appointment.Id);

        var logs = await repo.GetAuditLogsAsync("HQ-KL");
        Assert.Contains(logs, l => l.Action == "PATIENT_CREATED" && l.EntityId == response.Patient.Id.ToString());
        Assert.Contains(logs, l => l.Action == "APPOINTMENT_CREATED" && l.EntityId == response.Appointment.Id.ToString());
    }

    [Fact]
    public async Task InMemoryRepository_RegisterPatientAndBook_ExistingIcUpsertsPatientWithoutDuplicate()
    {
        var repo = CreateRepository();
        var existingPatients = await repo.GetPatientsAsync();
        var existing = existingPatients[0];
        int initialPatientCount = existingPatients.Count;

        var dentists = await repo.GetDentistsAsync("HQ-KL");
        var treatments = await repo.GetTreatmentsAsync();

        var response = await repo.RegisterPatientAndCreateAppointmentAsync(new RegisterPatientAndAppointmentRequest
        {
            Patient = new UpsertPatientRequest
            {
                FullName = existing.FullName,
                IcNumber = existing.IcNumber,
                Phone = "+6019-1122334",
                PrimaryBranchId = "HQ-KL"
            },
            Appointment = new CreateAppointmentRequest
            {
                BranchId = "HQ-KL",
                DentistId = dentists[1].Id,
                TreatmentId = treatments[0].Id,
                ScheduledAt = new DateTimeOffset(DateTime.Today.AddDays(5).AddHours(10), DateTimeOffset.Now.Offset),
                DurationMinutes = 30
            }
        });

        Assert.Equal(existing.Id, response.Patient.Id);
        Assert.Equal(existing.Id, response.Appointment.PatientId);

        var afterPatients = await repo.GetPatientsAsync();
        Assert.Equal(initialPatientCount, afterPatients.Count);
    }

    [Fact]
    public void RegisterPatientAndAppointmentDtos_RoundTripCleanlyWithSystemTextJson()
    {
        var request = new RegisterPatientAndAppointmentRequest
        {
            Patient = new UpsertPatientRequest
            {
                FullName = "Daniel Lim",
                IcNumber = "910512-10-4411",
                Phone = "+6017-8889900",
                PrimaryBranchId = "PJ-SS2"
            },
            Appointment = new CreateAppointmentRequest
            {
                BranchId = "PJ-SS2",
                DoctorName = "Dr. Marcus Tan",
                DurationMinutes = 30,
                ProcedureSummary = "Dental Consultation & Checkup"
            }
        };

        var reqJson = JsonSerializer.Serialize(request);
        var restoredReq = JsonSerializer.Deserialize<RegisterPatientAndAppointmentRequest>(reqJson);
        Assert.NotNull(restoredReq);
        Assert.Equal("Daniel Lim", restoredReq!.Patient.FullName);
        Assert.Equal("PJ-SS2", restoredReq.Appointment.BranchId);

        var response = new RegisterPatientAndAppointmentResponse
        {
            Patient = new PatientDto { FullName = "Daniel Lim", IcNumber = "910512-10-4411" },
            Appointment = new AppointmentQueueItemDto { PatientName = "Daniel Lim", DurationMinutes = 30 }
        };

        var respJson = JsonSerializer.Serialize(response);
        var restoredResp = JsonSerializer.Deserialize<RegisterPatientAndAppointmentResponse>(respJson);
        Assert.NotNull(restoredResp);
        Assert.Equal("910512-10-4411", restoredResp!.Patient.IcNumber);
        Assert.Equal(30, restoredResp.Appointment.DurationMinutes);
    }

    [Fact]
    public async Task AppointmentsHttpPipeline_CreateUpdateAndRegisterAndBook_AcceptEmptyOptionalStrings()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.Configure<SupabaseOptions>(_ => { });
        builder.Services.Configure<ClinicOptions>(_ => { });
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<RequestClaimsContext>();
        builder.Services.AddSingleton<IOcclusaRepository, InMemoryOcclusaRepository>();
        builder.Services.AddSingleton<IRealtimeNotifier, QueueRealtimeNotifier>();
        builder.Services.AddControllers(options =>
            {
                options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            })
            .AddApplicationPart(typeof(AppointmentsController).Assembly);
        builder.Services.AddSignalR();

        await using var app = builder.Build();
        app.UseMiddleware<JwtClaimsMiddleware>();
        app.MapControllers();
        app.MapHub<QueueHub>("/hubs/queue");
        await app.StartAsync();

        try
        {
            var baseAddress = app.Urls.First();
            using var http = new HttpClient { BaseAddress = new Uri(baseAddress) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Occlusa-User", "test-desktop");
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Occlusa-Allowed-Branches", "HQ-KL,PJ-SS2");

            var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };
            jsonOpts.Converters.Add(new JsonStringEnumConverter());

            var repo = app.Services.GetRequiredService<IOcclusaRepository>();
            var patients = await repo.GetPatientsAsync();
            var dentists = await repo.GetDentistsAsync("HQ-KL");
            var treatments = await repo.GetTreatmentsAsync();

            // 1. POST /api/appointments with empty Notes
            var createResp = await http.PostAsJsonAsync("/api/appointments", new CreateAppointmentRequest
            {
                BranchId = "HQ-KL",
                PatientId = patients[0].Id,
                DentistId = dentists[0].Id,
                DoctorName = dentists[0].FullName,
                TreatmentId = treatments[0].Id,
                RoomName = dentists[0].DefaultRoom,
                ScheduledAt = DateTimeOffset.Now.AddDays(2),
                DurationMinutes = 30,
                ProcedureSummary = treatments[0].Name,
                Notes = string.Empty,
                Status = QueueStatus.Scheduled
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
            var created = await createResp.Content.ReadFromJsonAsync<AppointmentQueueItemDto>(jsonOpts);
            Assert.NotNull(created);

            // 2. PUT /api/appointments/{id} with empty Notes
            var updateResp = await http.PutAsJsonAsync($"/api/appointments/{created!.Id}", new UpdateAppointmentRequest
            {
                PatientId = patients[0].Id,
                DentistId = dentists[1].Id,
                DoctorName = dentists[1].FullName,
                TreatmentId = treatments[1].Id,
                ProcedureSummary = treatments[1].Name,
                RoomName = dentists[1].DefaultRoom,
                ScheduledAt = created.ScheduledAt.AddHours(1),
                DurationMinutes = 45,
                Status = QueueStatus.Scheduled,
                Notes = string.Empty,
                UpdatedBy = "Desktop Reception"
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
            var updated = await updateResp.Content.ReadFromJsonAsync<AppointmentQueueItemDto>(jsonOpts);
            Assert.NotNull(updated);
            Assert.Equal(45, updated!.DurationMinutes);

            // 3. POST /api/appointments/register-and-book with empty optional strings
            var regResp = await http.PostAsJsonAsync("/api/appointments/register-and-book", new RegisterPatientAndAppointmentRequest
            {
                Patient = new UpsertPatientRequest
                {
                    FullName = "Farah Hani binti Kamal",
                    IcNumber = "970311-10-5422",
                    DateOfBirth = new DateOnly(1997, 3, 11),
                    Gender = "Female",
                    Phone = string.Empty,
                    Address = string.Empty,
                    PrimaryBranchId = "HQ-KL",
                    MedicalAlerts = new MedicalAlertDto
                    {
                        Allergies = string.Empty,
                        Notes = string.Empty
                    }
                },
                Appointment = new CreateAppointmentRequest
                {
                    BranchId = "HQ-KL",
                    DentistId = dentists[0].Id,
                    DoctorName = dentists[0].FullName,
                    TreatmentId = treatments[0].Id,
                    RoomName = dentists[0].DefaultRoom,
                    ScheduledAt = DateTimeOffset.Now.AddDays(3),
                    DurationMinutes = 30,
                    ProcedureSummary = treatments[0].Name,
                    Notes = string.Empty,
                    Status = QueueStatus.Scheduled
                }
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.Created, regResp.StatusCode);
            var regResult = await regResp.Content.ReadFromJsonAsync<RegisterPatientAndAppointmentResponse>(jsonOpts);
            Assert.NotNull(regResult);
            Assert.Equal("Farah Hani binti Kamal", regResult!.Patient.FullName);
            Assert.Equal(regResult.Patient.Id, regResult.Appointment.PatientId);

            // 4. Explicit controller validation still rejects blank FullName
            var badResp = await http.PostAsJsonAsync("/api/appointments/register-and-book", new RegisterPatientAndAppointmentRequest
            {
                Patient = new UpsertPatientRequest { FullName = string.Empty, IcNumber = "970311-10-5422" },
                Appointment = new CreateAppointmentRequest { BranchId = "HQ-KL" }
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.BadRequest, badResp.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task GetEligibleDentists_FiltersByTreatmentCapability()
    {
        var repo = CreateRepository();
        var dentists = await repo.GetDentistsAsync("HQ-KL");

        var t1Consultation = Guid.Parse("60000000-0000-0000-0000-000000000001");
        var t2Scaling = Guid.Parse("60000000-0000-0000-0000-000000000002");
        var t5RootCanal = Guid.Parse("60000000-0000-0000-0000-000000000005");

        var rootCanalDentists = AppointmentCalendarHelper.GetEligibleDentists(dentists, t5RootCanal);
        var onlyRootCanal = Assert.Single(rootCanalDentists);
        Assert.Equal("Dr. Marcus Tan", onlyRootCanal.FullName);

        var scalingDentists = AppointmentCalendarHelper.GetEligibleDentists(dentists, t2Scaling)
            .Select(d => d.FullName)
            .OrderBy(n => n)
            .ToList();
        Assert.Equal(new[] { "Dr. Aina Rahman", "Dr. Priya Nair" }, scalingDentists);

        var consultDentists = AppointmentCalendarHelper.GetEligibleDentists(dentists, t1Consultation);
        Assert.Equal(3, consultDentists.Count);
    }

    [Fact]
    public void MatchesPatientQuery_MatchesIcWithoutDashesNameAndPhone()
    {
        var patient = new PatientDto
        {
            FullName = "Nurul Aisyah binti Zulkifli",
            IcNumber = "920415-14-5678",
            Phone = "+6012-3456789"
        };

        Assert.True(AppointmentCalendarHelper.MatchesPatientQuery(patient, "920415145678"));
        Assert.True(AppointmentCalendarHelper.MatchesPatientQuery(patient, "aisyah"));
        Assert.True(AppointmentCalendarHelper.MatchesPatientQuery(patient, "0123456789"));
        Assert.False(AppointmentCalendarHelper.MatchesPatientQuery(patient, "nomatch-xyz"));
    }

    [Fact]
    public void GetDentistDaySlotAvailability_MarksBookedAndOpenSlotsAndIgnoresCancelled()
    {
        var dentistId = Guid.Parse("50000000-0000-0000-0000-000000000001");
        var date = new DateOnly(2026, 11, 24);
        var offset = TimeSpan.FromHours(8);
        var start0930 = new DateTimeOffset(2026, 11, 24, 9, 30, 0, offset);
        var start1400 = new DateTimeOffset(2026, 11, 24, 14, 0, 0, offset);

        var appointments = new List<AppointmentQueueItemDto>
        {
            new()
            {
                Id = Guid.NewGuid(),
                DentistId = dentistId,
                DoctorName = "Dr. Aina Rahman",
                PatientName = "Ahmad Daniel",
                ScheduledAt = start0930,
                DurationMinutes = 60,
                Status = QueueStatus.Scheduled
            },
            new()
            {
                Id = Guid.NewGuid(),
                DentistId = dentistId,
                DoctorName = "Dr. Aina Rahman",
                PatientName = "Lim Mei Ling",
                ScheduledAt = start1400,
                DurationMinutes = 30,
                Status = QueueStatus.Cancelled
            }
        };

        var slots = AppointmentCalendarHelper.GetDentistDaySlotAvailability(
            appointments,
            dentistId,
            "Dr. Aina Rahman",
            date,
            offset);

        Assert.Equal(9, slots.Count);
        Assert.Equal(new TimeOnly(9, 0), slots[0].Start);
        Assert.Equal(new TimeOnly(17, 0), slots[^1].Start);

        Assert.Equal(1, slots[0].BookedCount);
        Assert.False(slots[0].IsAvailable);
        Assert.Equal(1, slots[1].BookedCount);
        Assert.False(slots[1].IsAvailable);
        Assert.True(slots[2].IsAvailable);

        var slot1400 = Assert.Single(slots, s => s.Start == new TimeOnly(14, 0));
        Assert.True(slot1400.IsAvailable);
        Assert.Equal(0, slot1400.BookedCount);
    }

    [Fact]
    public async Task OcclusaApiApplicationFactory_EmbeddedHost_ServesHealthSnapshotAndAppointmentMutations()
    {
        await using var app = OcclusaApiApplicationFactory.BuildApplication(
            urls: "http://127.0.0.1:0",
            environmentName: "Development");
        await app.StartAsync();

        try
        {
            var baseAddress = app.Urls.First();
            using var http = new HttpClient { BaseAddress = new Uri(baseAddress) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Occlusa-User", "test-desktop");
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Occlusa-Allowed-Branches", "HQ-KL,PJ-SS2");

            var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };
            jsonOpts.Converters.Add(new JsonStringEnumConverter());

            var healthResp = await http.GetAsync("/api/health");
            Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);
            var health = await healthResp.Content.ReadFromJsonAsync<HealthStatusDto>(jsonOpts);
            Assert.NotNull(health);
            Assert.Equal("Healthy", health!.Status);
            Assert.False(string.IsNullOrWhiteSpace(health.DatabaseMode));

            var snapshot = await http.GetFromJsonAsync<ClinicDashboardSnapshotDto>(
                "/api/dashboard/snapshot?branchId=HQ-KL",
                jsonOpts);
            Assert.NotNull(snapshot);
            Assert.NotEmpty(snapshot!.Patients);
            Assert.NotEmpty(snapshot.Dentists);
            Assert.NotEmpty(snapshot.Treatments);

            var createResp = await http.PostAsJsonAsync("/api/appointments", new CreateAppointmentRequest
            {
                BranchId = "HQ-KL",
                PatientId = snapshot.Patients[0].Id,
                DentistId = snapshot.Dentists[0].Id,
                DoctorName = snapshot.Dentists[0].FullName,
                TreatmentId = snapshot.Treatments[0].Id,
                RoomName = snapshot.Dentists[0].DefaultRoom,
                ScheduledAt = DateTimeOffset.Now.AddDays(2),
                DurationMinutes = 30,
                ProcedureSummary = snapshot.Treatments[0].Name,
                Notes = string.Empty,
                Status = QueueStatus.Scheduled
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
            var created = await createResp.Content.ReadFromJsonAsync<AppointmentQueueItemDto>(jsonOpts);
            Assert.NotNull(created);

            var updateResp = await http.PutAsJsonAsync($"/api/appointments/{created!.Id}", new UpdateAppointmentRequest
            {
                PatientId = snapshot.Patients[0].Id,
                DentistId = snapshot.Dentists[0].Id,
                DoctorName = snapshot.Dentists[0].FullName,
                TreatmentId = snapshot.Treatments[0].Id,
                ProcedureSummary = snapshot.Treatments[0].Name,
                RoomName = snapshot.Dentists[0].DefaultRoom,
                ScheduledAt = created.ScheduledAt.AddHours(1),
                DurationMinutes = 45,
                Status = QueueStatus.Scheduled,
                Notes = "Rescheduled via embedded host",
                UpdatedBy = "Desktop Reception"
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
            var updated = await updateResp.Content.ReadFromJsonAsync<AppointmentQueueItemDto>(jsonOpts);
            Assert.NotNull(updated);
            Assert.Equal(45, updated!.DurationMinutes);

            var regResp = await http.PostAsJsonAsync("/api/appointments/register-and-book", new RegisterPatientAndAppointmentRequest
            {
                Patient = new UpsertPatientRequest
                {
                    FullName = "Siti Hajar binti Azman",
                    IcNumber = "950618-10-5124",
                    DateOfBirth = new DateOnly(1995, 6, 18),
                    Gender = "Female",
                    Phone = "+6012-7788990",
                    PrimaryBranchId = "HQ-KL"
                },
                Appointment = new CreateAppointmentRequest
                {
                    BranchId = "HQ-KL",
                    DentistId = snapshot.Dentists[0].Id,
                    DoctorName = snapshot.Dentists[0].FullName,
                    TreatmentId = snapshot.Treatments[0].Id,
                    RoomName = snapshot.Dentists[0].DefaultRoom,
                    ScheduledAt = DateTimeOffset.Now.AddDays(3),
                    DurationMinutes = 30,
                    ProcedureSummary = snapshot.Treatments[0].Name,
                    Notes = string.Empty,
                    Status = QueueStatus.Scheduled
                }
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.Created, regResp.StatusCode);
            var regResult = await regResp.Content.ReadFromJsonAsync<RegisterPatientAndAppointmentResponse>(jsonOpts);
            Assert.NotNull(regResult);
            Assert.Equal("Siti Hajar binti Azman", regResult!.Patient.FullName);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public void ComputeTimelineLayout_FourRowsPerHour_TwoHourAppointmentConsumesEightRows()
    {
        var offset = TimeSpan.FromHours(8);
        var appt = new AppointmentQueueItemDto
        {
            Id = Guid.NewGuid(),
            PatientName = "Nurul Aisyah",
            DoctorName = "Dr. Aina Rahman",
            ScheduledAt = new DateTimeOffset(2026, 11, 24, 14, 0, 0, offset),
            DurationMinutes = 120
        };

        var fullDayWindow = AppointmentCalendarHelper.GetTimelineWindow(DaySlotSession.FullDay, slotMinutes: 15);
        Assert.Equal(36, fullDayWindow.TotalRows);

        var fullDayLayout = AppointmentCalendarHelper.ComputeTimelineLayout(
            [appt],
            DaySlotSession.FullDay,
            offset,
            slotMinutes: 15);

        var block = Assert.Single(fullDayLayout.VisibleBlocks);
        Assert.Equal(20, block.StartRow); // (14:00 - 09:00) = 5h * 4 = 20
        Assert.Equal(8, block.RowSpan);   // 2h * 4 = 8 rows
        Assert.Equal(1, block.LaneCount);
        Assert.Equal(0, block.LaneIndex);

        var afternoonWindow = AppointmentCalendarHelper.GetTimelineWindow(DaySlotSession.Afternoon, slotMinutes: 15);
        Assert.Equal(18, afternoonWindow.TotalRows);

        var afternoonLayout = AppointmentCalendarHelper.ComputeTimelineLayout(
            [appt],
            DaySlotSession.Afternoon,
            offset,
            slotMinutes: 15);

        var pmBlock = Assert.Single(afternoonLayout.VisibleBlocks);
        Assert.Equal(2, pmBlock.StartRow); // (14:00 - 13:30) = 30m = 2 rows
        Assert.Equal(8, pmBlock.RowSpan);
    }

    [Fact]
    public void ComputeTimelineLayout_OverlappingAppointments_DivideColumnIntoTwoCleanLanes()
    {
        var offset = TimeSpan.FromHours(8);
        var aMorning = new AppointmentQueueItemDto
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PatientName = "Ahmad Daniel",
            ScheduledAt = new DateTimeOffset(2026, 11, 24, 10, 0, 0, offset),
            DurationMinutes = 60
        };
        var bLong = new AppointmentQueueItemDto
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            PatientName = "Tan Wei Ming",
            ScheduledAt = new DateTimeOffset(2026, 11, 24, 14, 0, 0, offset),
            DurationMinutes = 120
        };
        var cOverlap = new AppointmentQueueItemDto
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            PatientName = "Lim Mei Ling",
            ScheduledAt = new DateTimeOffset(2026, 11, 24, 14, 30, 0, offset),
            DurationMinutes = 60
        };
        var dBackToBack = new AppointmentQueueItemDto
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            PatientName = "Siti Hajar",
            ScheduledAt = new DateTimeOffset(2026, 11, 24, 15, 30, 0, offset),
            DurationMinutes = 30
        };

        var layout = AppointmentCalendarHelper.ComputeTimelineLayout(
            [aMorning, bLong, cOverlap, dBackToBack],
            DaySlotSession.FullDay,
            offset,
            slotMinutes: 15);

        Assert.Empty(layout.OverflowAppointments);
        Assert.Equal(4, layout.VisibleBlocks.Count);

        var morningBlock = Assert.Single(layout.VisibleBlocks, b => b.Appointment.Id == aMorning.Id);
        Assert.Equal(1, morningBlock.LaneCount);
        Assert.Equal(1, morningBlock.LaneSpan);

        var longBlock = Assert.Single(layout.VisibleBlocks, b => b.Appointment.Id == bLong.Id);
        var overlapBlock = Assert.Single(layout.VisibleBlocks, b => b.Appointment.Id == cOverlap.Id);
        var tailBlock = Assert.Single(layout.VisibleBlocks, b => b.Appointment.Id == dBackToBack.Id);

        Assert.Equal(2, longBlock.LaneCount);
        Assert.Equal(2, overlapBlock.LaneCount);
        Assert.Equal(2, tailBlock.LaneCount);
        Assert.Equal(0, longBlock.LaneIndex);
        Assert.Equal(1, overlapBlock.LaneIndex);
        Assert.Equal(1, tailBlock.LaneIndex); // Back-to-back 15:30 reuses Lane 1 cleanly
    }

    [Fact]
    public void ComputeTimelineLayout_MaxVisibleLanes_RoutesExtraConcurrentBookingsToOverflow()
    {
        var offset = TimeSpan.FromHours(8);
        var start = new DateTimeOffset(2026, 11, 24, 11, 0, 0, offset);
        var appts = Enumerable.Range(1, 4)
            .Select(i => new AppointmentQueueItemDto
            {
                Id = Guid.NewGuid(),
                PatientName = $"Patient {i}",
                ScheduledAt = start,
                DurationMinutes = 45
            })
            .ToList();

        var layout = AppointmentCalendarHelper.ComputeTimelineLayout(
            appts,
            DaySlotSession.FullDay,
            offset,
            slotMinutes: 15,
            maxVisibleLanes: 3);

        Assert.Equal(3, layout.VisibleBlocks.Count);
        Assert.Single(layout.OverflowAppointments);
        Assert.All(layout.VisibleBlocks, b => Assert.Equal(3, b.LaneCount));
    }

    [Fact]
    public async Task DragReschedule_PreservesDentistAndBlocksDifferentDentist()
    {
        var repo = CreateRepository();
        var patients = await repo.GetPatientsAsync();
        var dentists = await repo.GetDentistsAsync("HQ-KL");
        var treatments = await repo.GetTreatmentsAsync();

        var d1 = dentists[0];
        var d2 = dentists[1];
        var offset = TimeSpan.FromHours(8);
        var originalDate = DateOnly.FromDateTime(DateTime.Today.AddDays(2));
        var targetDate = originalDate.AddDays(2);

        var created = await repo.CreateAppointmentAsync(new CreateAppointmentRequest
        {
            BranchId = "HQ-KL",
            PatientId = patients[0].Id,
            DentistId = d1.Id,
            DoctorName = d1.FullName,
            TreatmentId = treatments[0].Id,
            RoomName = d1.DefaultRoom,
            ScheduledAt = new DateTimeOffset(originalDate.ToDateTime(new TimeOnly(14, 0)), offset),
            DurationMinutes = 120,
            ProcedureSummary = treatments[0].Name,
            Notes = "Keep dentist locked",
            Status = QueueStatus.Scheduled
        });

        Assert.True(AppointmentCalendarHelper.IsSameDentist(created, d1.Id, d1.FullName));
        Assert.False(AppointmentCalendarHelper.IsSameDentist(created, d2.Id, d2.FullName));

        var window = AppointmentCalendarHelper.GetTimelineWindow(DaySlotSession.FullDay, slotMinutes: 15);
        var snappedTime = AppointmentCalendarHelper.SnapRowToStartTime(window, 22); // 14:30
        Assert.Equal(new TimeOnly(14, 30), snappedTime);

        var updateReq = AppointmentCalendarHelper.BuildDragRescheduleRequest(
            created,
            targetDate,
            snappedTime,
            offset);

        var (updated, error) = await repo.UpdateAppointmentAsync(created.Id, updateReq);
        Assert.Null(error);
        Assert.NotNull(updated);
        Assert.Equal(d1.Id, updated!.DentistId);
        Assert.Equal(d1.FullName, updated.DoctorName);
        Assert.Equal(created.PatientId, updated.PatientId);
        Assert.Equal(120, updated.DurationMinutes);
        Assert.Equal(new DateTimeOffset(targetDate.ToDateTime(new TimeOnly(14, 30)), offset), updated.ScheduledAt);
    }
}

