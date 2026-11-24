using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Occlusa.Api.Options;
using Occlusa.Api.Security;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Api.Repositories;

/// <summary>
/// Supabase PostgreSQL repository enforcing Row-Level Security by propagating JWT claims
/// via `SELECT set_config('request.jwt.claims', @claimsJson, true)` inside every transaction.
/// </summary>
public sealed class NpgsqlSupabaseRepository : IOcclusaRepository
{
    private static readonly JsonSerializerOptions ActivityJsonOptions = CreateActivityJsonOptions();

    private readonly SupabaseOptions _supabaseOptions;
    private readonly ClinicOptions _clinicOptions;
    private readonly RequestClaimsContext _claimsContext;

    public string DatabaseMode => "SupabasePostgreSQL";

    private static JsonSerializerOptions CreateActivityJsonOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        opts.Converters.Add(new JsonStringEnumConverter());
        return opts;
    }

    public NpgsqlSupabaseRepository(
        IOptions<SupabaseOptions> supabaseOptions,
        IOptions<ClinicOptions> clinicOptions,
        RequestClaimsContext claimsContext)
    {
        _supabaseOptions = supabaseOptions.Value;
        _clinicOptions = clinicOptions.Value;
        _claimsContext = claimsContext;
    }

    private async Task<(NpgsqlConnection Conn, NpgsqlTransaction Tx)> OpenScopedTransactionAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(_supabaseOptions.ConnectionString);
        await conn.OpenAsync(ct);
        var tx = await conn.BeginTransactionAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "SELECT set_config('request.jwt.claims', @claimsJson, true);",
            conn,
            tx);
        cmd.Parameters.AddWithValue("claimsJson", _claimsContext.ToRlsClaimsJson());
        await cmd.ExecuteNonQueryAsync(ct);

        return (conn, tx);
    }

    public async Task<ClinicDashboardSnapshotDto> GetDashboardSnapshotAsync(string branchId, CancellationToken ct = default)
    {
        var branch = ClinicDomainHelpers.NormalizeBranch(branchId, _clinicOptions.DefaultBranchId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var queue = await GetAppointmentsAsync(branch, ct);
        var patients = await GetPatientsAsync(null, ct);
        var dentists = await GetDentistsAsync(branch, ct);
        var treatments = await GetTreatmentsAsync(ct);
        var invoices = await GetInvoicesAsync(branch, ct);
        var macros = await GetSoapMacrosAsync(ct);
        var threads = await GetWhatsAppThreadsAsync(branch, ct);
        var summary = BillingCalculator.CalculateDailySalesReconciliation(branch, today, invoices, _claimsContext.UserId);

        return new ClinicDashboardSnapshotDto
        {
            BranchId = branch,
            DatabaseMode = DatabaseMode,
            GeneratedAt = DateTimeOffset.UtcNow,
            Queue = queue.ToList(),
            Patients = patients.ToList(),
            Dentists = dentists.ToList(),
            Treatments = treatments.ToList(),
            Invoices = invoices.ToList(),
            SoapMacros = macros.ToList(),
            WhatsAppThreads = threads.ToList(),
            DailySummary = summary
        };
    }

    public async Task<IReadOnlyList<PatientDto>> GetPatientsAsync(string? search = null, CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            const string sql = """
                SELECT p.id, p.full_name, p.ic_number, p.date_of_birth, p.gender, p.phone, p.address,
                       p.primary_branch_id, p.is_mykad_verified, p.consent_signed_at,
                       COALESCE(m.allergies, ''), COALESCE(m.on_anticoagulants, FALSE),
                       COALESCE(m.has_diabetes, FALSE), COALESCE(m.is_pregnant, FALSE), COALESCE(m.notes, '')
                FROM public.patients p
                LEFT JOIN public.medical_alerts m ON m.patient_id = p.id
                WHERE p.is_deleted = FALSE
                  AND (@search IS NULL OR p.full_name ILIKE '%' || @search || '%' OR p.ic_number ILIKE '%' || @search || '%' OR p.phone ILIKE '%' || @search || '%')
                ORDER BY p.full_name;
                """;

            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("search", string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim());

            var list = new List<PatientDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(ReadPatient(reader));
            }

            await tx.CommitAsync(ct);
            return list;
        }
    }

    public async Task<PatientDto?> GetPatientByIdAsync(Guid patientId, CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var patient = await GetPatientInternalAsync(conn, tx, patientId, ct);
            await tx.CommitAsync(ct);
            return patient;
        }
    }

    public async Task<PatientDto?> FindPatientByPhoneOrNameAsync(string? phone, string? name, CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            const string sql = """
                SELECT p.id, p.full_name, p.ic_number, p.date_of_birth, p.gender, p.phone, p.address,
                       p.primary_branch_id, p.is_mykad_verified, p.consent_signed_at,
                       COALESCE(m.allergies, ''), COALESCE(m.on_anticoagulants, FALSE),
                       COALESCE(m.has_diabetes, FALSE), COALESCE(m.is_pregnant, FALSE), COALESCE(m.notes, '')
                FROM public.patients p
                LEFT JOIN public.medical_alerts m ON m.patient_id = p.id
                WHERE p.is_deleted = FALSE
                  AND (
                    (@phone IS NOT NULL AND regexp_replace(p.phone, '\D', '', 'g') = @phone)
                    OR (@name IS NOT NULL AND LOWER(p.full_name) = LOWER(@name))
                  )
                LIMIT 1;
                """;

            var digits = string.IsNullOrWhiteSpace(phone) ? null : new string(phone.Where(char.IsAsciiDigit).ToArray());
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("phone", string.IsNullOrEmpty(digits) ? DBNull.Value : digits);
            cmd.Parameters.AddWithValue("name", string.IsNullOrWhiteSpace(name) ? DBNull.Value : name.Trim());

            PatientDto? result = null;
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                if (await reader.ReadAsync(ct))
                {
                    result = ReadPatient(reader);
                }
            }

            await tx.CommitAsync(ct);
            return result;
        }
    }

    public async Task<PatientDto> UpsertPatientAsync(UpsertPatientRequest request, bool forceMyKadVerified = false, CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var patient = await UpsertPatientInternalAsync(conn, tx, request, forceMyKadVerified, ct);
            await tx.CommitAsync(ct);
            return patient;
        }
    }

    private async Task<PatientDto> UpsertPatientInternalAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        UpsertPatientRequest request,
        bool forceMyKadVerified,
        CancellationToken ct)
    {
        var parsedNric = MyKadParser.ParseNric(request.IcNumber);
        var normalizedIc = parsedNric.IsValid ? parsedNric.NormalizedIc : (request.IcNumber ?? string.Empty).Trim();
        var branchId = ClinicDomainHelpers.NormalizeBranch(request.PrimaryBranchId, _clinicOptions.DefaultBranchId);
        var dob = request.DateOfBirth != default ? request.DateOfBirth : (parsedNric.DateOfBirth ?? new DateOnly(1990, 1, 1));
        var gender = !string.IsNullOrWhiteSpace(request.Gender) ? request.Gender.Trim() : parsedNric.Gender;
        var verified = forceMyKadVerified || request.IsMyKadVerified;

        var id = request.Id ?? Guid.NewGuid();
        const string upsertPatientSql = """
            INSERT INTO public.patients
                (id, clinic_group_id, primary_branch_id, full_name, ic_number, date_of_birth, gender, phone, address, is_mykad_verified, consent_signed_at, updated_at)
            VALUES
                (@id, @groupId, @branchId, @fullName, @ic, @dob, @gender, @phone, @address, @verified, @consentAt, NOW())
            ON CONFLICT (clinic_group_id, ic_number) DO UPDATE SET
                full_name = CASE WHEN EXCLUDED.full_name <> '' THEN EXCLUDED.full_name ELSE public.patients.full_name END,
                date_of_birth = EXCLUDED.date_of_birth,
                gender = CASE WHEN EXCLUDED.gender <> '' THEN EXCLUDED.gender ELSE public.patients.gender END,
                phone = CASE WHEN EXCLUDED.phone <> '' THEN EXCLUDED.phone ELSE public.patients.phone END,
                address = CASE WHEN EXCLUDED.address <> '' THEN EXCLUDED.address ELSE public.patients.address END,
                is_mykad_verified = public.patients.is_mykad_verified OR EXCLUDED.is_mykad_verified,
                consent_signed_at = COALESCE(EXCLUDED.consent_signed_at, public.patients.consent_signed_at),
                updated_at = NOW()
            RETURNING id;
            """;

        await using (var cmd = new NpgsqlCommand(upsertPatientSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("groupId", _claimsContext.ClinicGroupId);
            cmd.Parameters.AddWithValue("branchId", branchId);
            cmd.Parameters.AddWithValue("fullName", request.FullName?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("ic", normalizedIc);
            cmd.Parameters.AddWithValue("dob", dob);
            cmd.Parameters.AddWithValue("gender", gender ?? string.Empty);
            cmd.Parameters.AddWithValue("phone", request.Phone?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("address", request.Address?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("verified", verified);
            cmd.Parameters.AddWithValue("consentAt", (object?)request.ConsentSignedAt ?? DBNull.Value);

            id = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
        }

        if (request.MedicalAlerts is not null)
        {
            await UpsertMedicalAlertsInternalAsync(conn, tx, id, request.MedicalAlerts, ct);
        }

        var patient = (await GetPatientInternalAsync(conn, tx, id, ct))!;

        await SyncAppointmentAlertsInternalAsync(conn, tx, patient, ct);
        await InsertAuditLogInternalAsync(conn, tx, branchId,
            forceMyKadVerified ? "PATIENT_MYKAD_UPSERT" : "PATIENT_UPSERT",
            "Patient",
            patient.Id.ToString(),
            null,
            $"{patient.FullName} ({patient.IcNumber}); MyKadVerified={patient.IsMyKadVerified}",
            ct);

        return patient;
    }

    public async Task<PatientDto?> UpdateMedicalAlertsAsync(Guid patientId, MedicalAlertDto alerts, string branchId, CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var existing = await GetPatientInternalAsync(conn, tx, patientId, ct);
            if (existing is null)
            {
                return null;
            }

            var oldSummary = existing.MedicalAlerts.BuildAlertSummary();
            await UpsertMedicalAlertsInternalAsync(conn, tx, patientId, alerts, ct);

            var updated = (await GetPatientInternalAsync(conn, tx, patientId, ct))!;
            await SyncAppointmentAlertsInternalAsync(conn, tx, updated, ct);
            await InsertAuditLogInternalAsync(
                conn,
                tx,
                ClinicDomainHelpers.NormalizeBranch(branchId, updated.PrimaryBranchId),
                "MEDICAL_ALERTS_UPDATED",
                "Patient",
                patientId.ToString(),
                oldSummary,
                updated.MedicalAlerts.BuildAlertSummary(),
                ct);

            await tx.CommitAsync(ct);
            return updated;
        }
    }

    public async Task<PatientDto?> SignConsentAsync(Guid patientId, SignConsentRequest request, string branchId, CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var signedAt = request.SignedAt ?? DateTimeOffset.UtcNow;
            const string sql = """
                UPDATE public.patients
                SET consent_signed_at = @signedAt, updated_at = NOW()
                WHERE id = @id AND is_deleted = FALSE;
                """;

            await using (var cmd = new NpgsqlCommand(sql, conn, tx))
            {
                cmd.Parameters.AddWithValue("signedAt", signedAt);
                cmd.Parameters.AddWithValue("id", patientId);
                var rows = await cmd.ExecuteNonQueryAsync(ct);
                if (rows == 0)
                {
                    return null;
                }
            }

            var updated = (await GetPatientInternalAsync(conn, tx, patientId, ct))!;
            await InsertAuditLogInternalAsync(
                conn,
                tx,
                ClinicDomainHelpers.NormalizeBranch(branchId, updated.PrimaryBranchId),
                "CONSENT_SIGNED",
                "Patient",
                patientId.ToString(),
                null,
                signedAt.ToString("O"),
                ct);

            await tx.CommitAsync(ct);
            return updated;
        }
    }

    public async Task<IReadOnlyList<DentistDto>> GetDentistsAsync(string? branchId = null, CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            const string sql = """
                SELECT id, full_name, specialty, default_room, branch_ids, is_active, supported_treatment_ids
                FROM public.dentists
                WHERE is_active = TRUE
                  AND (@branchId IS NULL OR @branchId = ANY(branch_ids))
                ORDER BY full_name ASC;
                """;

            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("branchId",
                string.IsNullOrWhiteSpace(branchId) ? DBNull.Value : ClinicDomainHelpers.NormalizeBranch(branchId, _clinicOptions.DefaultBranchId));

            var list = new List<DentistDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new DentistDto
                {
                    Id = reader.GetGuid(0),
                    FullName = reader.GetString(1),
                    Specialty = reader.GetString(2),
                    DefaultRoom = reader.GetString(3),
                    BranchIds = reader.IsDBNull(4) ? new List<string>() : reader.GetFieldValue<string[]>(4).ToList(),
                    IsActive = reader.GetBoolean(5),
                    SupportedTreatmentIds = reader.IsDBNull(6) ? new List<Guid>() : reader.GetFieldValue<Guid[]>(6).ToList()
                });
            }

            await tx.CommitAsync(ct);
            return list;
        }
    }

    public async Task<IReadOnlyList<TreatmentDto>> GetTreatmentsAsync(CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            const string sql = """
                SELECT id, name, category, default_duration_minutes, default_fee, is_active
                FROM public.treatments
                WHERE is_active = TRUE
                ORDER BY category ASC, name ASC;
                """;

            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            var list = new List<TreatmentDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new TreatmentDto
                {
                    Id = reader.GetGuid(0),
                    Name = reader.GetString(1),
                    Category = reader.GetString(2),
                    DefaultDurationMinutes = reader.GetInt32(3),
                    DefaultFee = reader.GetDecimal(4),
                    IsActive = reader.GetBoolean(5)
                });
            }

            await tx.CommitAsync(ct);
            return list;
        }
    }

    public Task<IReadOnlyList<AppointmentQueueItemDto>> GetAppointmentsAsync(string? branchId = null, CancellationToken ct = default) =>
        GetAppointmentsAsync(branchId, null, null, null, null, ct);

    public async Task<IReadOnlyList<AppointmentQueueItemDto>> GetAppointmentsAsync(
        string? branchId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? dentistId,
        Guid? treatmentId,
        CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            const string sql = """
                SELECT id, branch_id, patient_id, patient_name, patient_ic, dentist_id, doctor_name,
                       treatment_id, room_name, scheduled_at, duration_minutes, procedure_summary,
                       notes, status, has_critical_medical_alert, alert_summary,
                       visit_type, arrived_at, seated_at, checkout_at, completed_at,
                       created_by, checked_in_by, seated_by, checkout_by, completed_by,
                       last_updated_by, updated_at, activity_log
                FROM public.appointments
                WHERE is_deleted = FALSE
                  AND (@branchId IS NULL OR branch_id = @branchId)
                  AND (@from IS NULL OR scheduled_at >= @from)
                  AND (@to IS NULL OR scheduled_at <= @to)
                  AND (@dentistId IS NULL OR dentist_id = @dentistId)
                  AND (@treatmentId IS NULL OR treatment_id = @treatmentId)
                ORDER BY scheduled_at ASC;
                """;

            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("branchId",
                string.IsNullOrWhiteSpace(branchId) ? DBNull.Value : ClinicDomainHelpers.NormalizeBranch(branchId, _clinicOptions.DefaultBranchId));
            cmd.Parameters.AddWithValue("from", (object?)from ?? DBNull.Value);
            cmd.Parameters.AddWithValue("to", (object?)to ?? DBNull.Value);
            cmd.Parameters.AddWithValue("dentistId", (object?)dentistId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("treatmentId", (object?)treatmentId ?? DBNull.Value);

            var list = new List<AppointmentQueueItemDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(ReadAppointment(reader));
            }

            await tx.CommitAsync(ct);
            return list;
        }
    }

    public async Task<AppointmentQueueItemDto?> GetAppointmentByIdAsync(Guid appointmentId, CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var appt = await GetAppointmentInternalAsync(conn, tx, appointmentId, ct);
            await tx.CommitAsync(ct);
            return appt;
        }
    }

    public async Task<AppointmentQueueItemDto> CreateAppointmentAsync(CreateAppointmentRequest request, CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var patient = await GetPatientInternalAsync(conn, tx, request.PatientId, ct);
            var created = await CreateAppointmentInternalAsync(conn, tx, request, patient, ct);
            await tx.CommitAsync(ct);
            return created;
        }
    }

    public async Task<RegisterPatientAndAppointmentResponse> RegisterPatientAndCreateAppointmentAsync(
        RegisterPatientAndAppointmentRequest request,
        CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var patientReq = request.Patient ?? new UpsertPatientRequest();
            if (string.IsNullOrWhiteSpace(patientReq.PrimaryBranchId))
            {
                patientReq.PrimaryBranchId = request.Appointment.BranchId;
            }

            var patient = await UpsertPatientInternalAsync(conn, tx, patientReq, forceMyKadVerified: false, ct);

            var apptReq = request.Appointment ?? new CreateAppointmentRequest();
            apptReq.PatientId = patient.Id;
            if (string.IsNullOrWhiteSpace(apptReq.BranchId))
            {
                apptReq.BranchId = patient.PrimaryBranchId;
            }

            var appointment = await CreateAppointmentInternalAsync(conn, tx, apptReq, patient, ct);
            await tx.CommitAsync(ct);

            return new RegisterPatientAndAppointmentResponse
            {
                Patient = patient,
                Appointment = appointment
            };
        }
    }

    private async Task<AppointmentQueueItemDto> CreateAppointmentInternalAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        CreateAppointmentRequest request,
        PatientDto? patient,
        CancellationToken ct)
    {
        var branch = ClinicDomainHelpers.NormalizeBranch(request.BranchId, _clinicOptions.DefaultBranchId);
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var patientId = patient?.Id ?? request.PatientId;
        var patientName = patient?.FullName ?? "Walk-In Patient";
        var patientIc = patient?.IcNumber ?? string.Empty;
        var hasAlert = patient?.MedicalAlerts.HasAnyCriticalAlert ?? false;
        var alertSummary = patient?.MedicalAlerts.BuildAlertSummary() ?? "No Critical Alerts";
        var doctorName = string.IsNullOrWhiteSpace(request.DoctorName) ? "Dr. Aina Rahman" : request.DoctorName.Trim();
        var roomName = string.IsNullOrWhiteSpace(request.RoomName) ? "Suite 1" : request.RoomName.Trim();
        var scheduledAt = request.ScheduledAt == default ? now : request.ScheduledAt;
        var duration = Math.Clamp(request.DurationMinutes > 0 ? request.DurationMinutes : 30, 15, 480);
        var procedure = string.IsNullOrWhiteSpace(request.ProcedureSummary) ? "Consultation" : request.ProcedureSummary.Trim();
        var notes = request.Notes?.Trim() ?? string.Empty;

        var normalizedStatus = AppointmentCalendarHelper.NormalizeQueueStatus(request.Status);
        var visitType = normalizedStatus == QueueStatus.AiBookedPending
            ? VisitType.AiBooked
            : request.VisitType;
        var actor = !string.IsNullOrWhiteSpace(request.CreatedBy)
            ? request.CreatedBy.Trim()
            : _claimsContext.UserId;

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
        var activityLog = new List<VisitActivityEntryDto>
        {
            new()
            {
                Timestamp = now,
                Action = initialAction,
                StaffName = actor,
                Details = $"{doctorName} · {roomName} · {procedure}"
            }
        };

        const string sql = """
            INSERT INTO public.appointments
                (id, clinic_group_id, branch_id, patient_id, patient_name, patient_ic, dentist_id, doctor_name,
                 treatment_id, room_name, scheduled_at, duration_minutes, procedure_summary, notes, status,
                 has_critical_medical_alert, alert_summary, visit_type, arrived_at, seated_at,
                 created_by, checked_in_by, seated_by, last_updated_by, activity_log, updated_at)
            VALUES
                (@id, @groupId, @branchId, @patientId, @patientName, @patientIc, @dentistId, @doctorName,
                 @treatmentId, @roomName, @scheduledAt, @duration, @procedure, @notes, @status,
                 @hasAlert, @alertSummary, @visitType, @arrivedAt, @seatedAt,
                 @createdBy, @checkedInBy, @seatedBy, @lastUpdatedBy, @activityLog, @updatedAt);
            """;

        await using (var cmd = new NpgsqlCommand(sql, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("groupId", _claimsContext.ClinicGroupId);
            cmd.Parameters.AddWithValue("branchId", branch);
            cmd.Parameters.AddWithValue("patientId", patientId);
            cmd.Parameters.AddWithValue("patientName", patientName);
            cmd.Parameters.AddWithValue("patientIc", patientIc);
            cmd.Parameters.AddWithValue("dentistId", (object?)request.DentistId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("doctorName", doctorName);
            cmd.Parameters.AddWithValue("treatmentId", (object?)request.TreatmentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("roomName", roomName);
            cmd.Parameters.AddWithValue("scheduledAt", scheduledAt);
            cmd.Parameters.AddWithValue("duration", duration);
            cmd.Parameters.AddWithValue("procedure", procedure);
            cmd.Parameters.AddWithValue("notes", notes);
            cmd.Parameters.AddWithValue("status", normalizedStatus.ToString());
            cmd.Parameters.AddWithValue("hasAlert", hasAlert);
            cmd.Parameters.AddWithValue("alertSummary", alertSummary);
            cmd.Parameters.AddWithValue("visitType", visitType.ToString());
            cmd.Parameters.AddWithValue("arrivedAt", (object?)arrivedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("seatedAt", (object?)seatedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("createdBy", actor);
            cmd.Parameters.AddWithValue("checkedInBy", checkedInBy);
            cmd.Parameters.AddWithValue("seatedBy", seatedBy);
            cmd.Parameters.AddWithValue("lastUpdatedBy", actor);
            cmd.Parameters.Add(new NpgsqlParameter("activityLog", NpgsqlDbType.Jsonb)
            {
                Value = JsonSerializer.Serialize(activityLog, ActivityJsonOptions)
            });
            cmd.Parameters.AddWithValue("updatedAt", now);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await InsertAuditLogInternalAsync(
            conn,
            tx,
            branch,
            "APPOINTMENT_CREATED",
            "Appointment",
            id.ToString(),
            null,
            $"{patientName} | {visitType} | {doctorName} | {procedure} | {duration}m | {normalizedStatus}",
            ct,
            actor);

        return (await GetAppointmentInternalAsync(conn, tx, id, ct))!;
    }

    public async Task<(AppointmentQueueItemDto? Item, string? Error)> UpdateAppointmentAsync(
        Guid appointmentId,
        UpdateAppointmentRequest request,
        CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var existing = await GetAppointmentInternalAsync(conn, tx, appointmentId, ct);
            if (existing is null)
            {
                return (null, "Appointment not found.");
            }

            var now = DateTimeOffset.UtcNow;
            var patient = request.PatientId != Guid.Empty
                ? await GetPatientInternalAsync(conn, tx, request.PatientId, ct)
                : null;

            var oldStatus = existing.Status;
            var oldSummary = $"{existing.PatientName} | {existing.DoctorName} | {existing.ProcedureSummary} | {existing.ScheduledAt:O} | {existing.DurationMinutes}m | {existing.Status}";

            existing.PatientId = patient?.Id ?? existing.PatientId;
            existing.PatientName = patient?.FullName ?? existing.PatientName;
            existing.PatientIc = patient?.IcNumber ?? existing.PatientIc;
            existing.HasCriticalMedicalAlert = patient?.MedicalAlerts.HasAnyCriticalAlert ?? existing.HasCriticalMedicalAlert;
            existing.AlertSummary = patient?.MedicalAlerts.BuildAlertSummary() ?? existing.AlertSummary;

            existing.DentistId = request.DentistId ?? existing.DentistId;
            existing.DoctorName = string.IsNullOrWhiteSpace(request.DoctorName) ? existing.DoctorName : request.DoctorName.Trim();
            existing.TreatmentId = request.TreatmentId ?? existing.TreatmentId;
            existing.RoomName = string.IsNullOrWhiteSpace(request.RoomName) ? existing.RoomName : request.RoomName.Trim();
            existing.ProcedureSummary = string.IsNullOrWhiteSpace(request.ProcedureSummary) ? existing.ProcedureSummary : request.ProcedureSummary.Trim();
            existing.ScheduledAt = request.ScheduledAt == default ? existing.ScheduledAt : request.ScheduledAt;
            existing.DurationMinutes = request.DurationMinutes > 0 ? Math.Clamp(request.DurationMinutes, 15, 480) : existing.DurationMinutes;
            existing.Notes = request.Notes?.Trim() ?? string.Empty;
            if (request.VisitType.HasValue)
            {
                existing.VisitType = request.VisitType.Value;
            }

            var actor = string.IsNullOrWhiteSpace(request.UpdatedBy) ? _claimsContext.UserId : request.UpdatedBy.Trim();
            var normalizedTargetStatus = AppointmentCalendarHelper.NormalizeQueueStatus(request.Status);
            if (AppointmentCalendarHelper.NormalizeQueueStatus(oldStatus) != normalizedTargetStatus)
            {
                ClinicDomainHelpers.ApplyStatusMilestone(existing, oldStatus, normalizedTargetStatus, actor, now, existing.RoomName);
            }
            else
            {
                existing.Status = normalizedTargetStatus;
                existing.LastUpdatedBy = actor;
                existing.LastUpdatedAt = now;
                existing.ActivityLog.Add(new VisitActivityEntryDto
                {
                    Timestamp = now,
                    Action = "Appointment Updated",
                    StaffName = actor,
                    Details = $"{existing.DoctorName} · {existing.RoomName} · {existing.ScheduledAt.ToLocalTime():dd MMM HH:mm}"
                });
            }

            const string sql = """
                UPDATE public.appointments
                SET patient_id = @patientId,
                    patient_name = @patientName,
                    patient_ic = @patientIc,
                    dentist_id = @dentistId,
                    doctor_name = @doctorName,
                    treatment_id = @treatmentId,
                    room_name = @roomName,
                    scheduled_at = @scheduledAt,
                    duration_minutes = @duration,
                    procedure_summary = @procedure,
                    notes = @notes,
                    status = @status,
                    visit_type = @visitType,
                    arrived_at = @arrivedAt,
                    seated_at = @seatedAt,
                    checkout_at = @checkoutAt,
                    completed_at = @completedAt,
                    checked_in_by = @checkedInBy,
                    seated_by = @seatedBy,
                    checkout_by = @checkoutBy,
                    completed_by = @completedBy,
                    last_updated_by = @lastUpdatedBy,
                    activity_log = @activityLog,
                    has_critical_medical_alert = @hasAlert,
                    alert_summary = @alertSummary,
                    updated_at = @updatedAt
                WHERE id = @id;
                """;

            await using (var cmd = new NpgsqlCommand(sql, conn, tx))
            {
                cmd.Parameters.AddWithValue("patientId", existing.PatientId);
                cmd.Parameters.AddWithValue("patientName", existing.PatientName);
                cmd.Parameters.AddWithValue("patientIc", existing.PatientIc);
                cmd.Parameters.AddWithValue("dentistId", (object?)existing.DentistId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("doctorName", existing.DoctorName);
                cmd.Parameters.AddWithValue("treatmentId", (object?)existing.TreatmentId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("roomName", existing.RoomName);
                cmd.Parameters.AddWithValue("scheduledAt", existing.ScheduledAt);
                cmd.Parameters.AddWithValue("duration", existing.DurationMinutes);
                cmd.Parameters.AddWithValue("procedure", existing.ProcedureSummary);
                cmd.Parameters.AddWithValue("notes", existing.Notes);
                cmd.Parameters.AddWithValue("status", existing.Status.ToString());
                cmd.Parameters.AddWithValue("visitType", existing.VisitType.ToString());
                cmd.Parameters.AddWithValue("arrivedAt", (object?)existing.ArrivedAt ?? DBNull.Value);
                cmd.Parameters.AddWithValue("seatedAt", (object?)existing.SeatedAt ?? DBNull.Value);
                cmd.Parameters.AddWithValue("checkoutAt", (object?)existing.CheckoutAt ?? DBNull.Value);
                cmd.Parameters.AddWithValue("completedAt", (object?)existing.CompletedAt ?? DBNull.Value);
                cmd.Parameters.AddWithValue("checkedInBy", existing.CheckedInBy);
                cmd.Parameters.AddWithValue("seatedBy", existing.SeatedBy);
                cmd.Parameters.AddWithValue("checkoutBy", existing.CheckoutBy);
                cmd.Parameters.AddWithValue("completedBy", existing.CompletedBy);
                cmd.Parameters.AddWithValue("lastUpdatedBy", existing.LastUpdatedBy);
                cmd.Parameters.Add(new NpgsqlParameter("activityLog", NpgsqlDbType.Jsonb)
                {
                    Value = JsonSerializer.Serialize(existing.ActivityLog, ActivityJsonOptions)
                });
                cmd.Parameters.AddWithValue("hasAlert", existing.HasCriticalMedicalAlert);
                cmd.Parameters.AddWithValue("alertSummary", existing.AlertSummary);
                cmd.Parameters.AddWithValue("updatedAt", now);
                cmd.Parameters.AddWithValue("id", appointmentId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            var newSummary = $"{existing.PatientName} | {existing.DoctorName} | {existing.ProcedureSummary} | {existing.ScheduledAt:O} | {existing.DurationMinutes}m | {existing.Status}";
            await InsertAuditLogInternalAsync(
                conn,
                tx,
                existing.BranchId,
                "APPOINTMENT_UPDATED",
                "Appointment",
                appointmentId.ToString(),
                oldSummary,
                newSummary,
                ct,
                actor);

            var updated = (await GetAppointmentInternalAsync(conn, tx, appointmentId, ct))!;
            await tx.CommitAsync(ct);
            return (updated, null);
        }
    }

    public async Task<(AppointmentQueueItemDto? Item, string? Error)> UpdateQueueStatusAsync(
        Guid appointmentId,
        UpdateQueueStatusRequest request,
        CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var existing = await GetAppointmentInternalAsync(conn, tx, appointmentId, ct);
            if (existing is null)
            {
                return (null, "Appointment not found.");
            }

            var normalizedNew = AppointmentCalendarHelper.NormalizeQueueStatus(request.NewStatus);
            if (!ClinicDomainHelpers.IsTransitionAllowed(existing.Status, normalizedNew))
            {
                return (null, $"Invalid queue transition from {existing.Status} to {normalizedNew}.");
            }

            var oldStatus = existing.Status;
            if (!string.IsNullOrWhiteSpace(request.RoomName))
            {
                existing.RoomName = request.RoomName.Trim();
            }

            var actor = string.IsNullOrWhiteSpace(request.UpdatedBy) ? _claimsContext.UserId : request.UpdatedBy.Trim();
            ClinicDomainHelpers.ApplyStatusMilestone(existing, oldStatus, normalizedNew, actor, DateTimeOffset.UtcNow, request.RoomName);
            await PersistAppointmentLifecycleAsync(conn, tx, existing, ct);

            await InsertAuditLogInternalAsync(
                conn,
                tx,
                existing.BranchId,
                "QUEUE_STATUS_CHANGED",
                "Appointment",
                appointmentId.ToString(),
                oldStatus.ToString(),
                existing.Status.ToString(),
                ct,
                actor);

            var updated = (await GetAppointmentInternalAsync(conn, tx, appointmentId, ct))!;
            await tx.CommitAsync(ct);
            return (updated, null);
        }
    }

    public async Task<IReadOnlyList<OdontogramToothDto>> GetOdontogramAsync(Guid patientId, CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var patient = await GetPatientInternalAsync(conn, tx, patientId, ct);
            if (patient is null)
            {
                return Array.Empty<OdontogramToothDto>();
            }

            const string sql = """
                SELECT id, patient_id, tooth_number, condition, procedure_name, fee, stage, doctor_name, updated_at
                FROM public.odontogram_entries
                WHERE patient_id = @patientId AND is_deleted = FALSE;
                """;

            var map = new Dictionary<int, OdontogramToothDto>();
            await using (var cmd = new NpgsqlCommand(sql, conn, tx))
            {
                cmd.Parameters.AddWithValue("patientId", patientId);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var tooth = new OdontogramToothDto
                    {
                        Id = reader.GetGuid(0),
                        PatientId = reader.GetGuid(1),
                        ToothNumber = reader.GetInt32(2),
                        Condition = Enum.Parse<ToothCondition>(reader.GetString(3)),
                        ProcedureName = reader.GetString(4),
                        Fee = reader.GetDecimal(5),
                        Stage = Enum.Parse<TreatmentStage>(reader.GetString(6)),
                        DoctorName = reader.GetString(7),
                        UpdatedAt = reader.GetFieldValue<DateTimeOffset>(8)
                    };
                    map[tooth.ToothNumber] = tooth;
                }
            }

            await tx.CommitAsync(ct);

            var result = new List<OdontogramToothDto>(ClinicDomainHelpers.AllFdiTeeth.Length);
            foreach (var toothNumber in ClinicDomainHelpers.AllFdiTeeth)
            {
                result.Add(map.TryGetValue(toothNumber, out var existing)
                    ? existing
                    : new OdontogramToothDto
                    {
                        Id = Guid.Empty,
                        PatientId = patientId,
                        ToothNumber = toothNumber,
                        Condition = ToothCondition.Healthy,
                        Stage = TreatmentStage.Proposed,
                        UpdatedAt = DateTimeOffset.UtcNow
                    });
            }

            return result;
        }
    }

    public async Task<(OdontogramToothDto? Tooth, InvoiceDto? UpdatedInvoice, string? Error)> UpdateOdontogramAsync(
        Guid patientId,
        UpdateOdontogramRequest request,
        CancellationToken ct = default)
    {
        if (!ClinicDomainHelpers.IsValidFdiTooth(request.ToothNumber))
        {
            return (null, null, $"Invalid FDI tooth number '{request.ToothNumber}'.");
        }

        if (request.Fee < 0m)
        {
            return (null, null, "Procedure fee cannot be negative.");
        }

        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var patient = await GetPatientInternalAsync(conn, tx, patientId, ct);
            if (patient is null)
            {
                return (null, null, "Patient not found.");
            }

            var branchId = ClinicDomainHelpers.NormalizeBranch(request.BranchId, patient.PrimaryBranchId);
            var doctorName = string.IsNullOrWhiteSpace(request.DoctorName) ? "Dr. Aina Rahman" : request.DoctorName.Trim();
            var fee = BillingCalculator.Round2(request.Fee);
            var now = DateTimeOffset.UtcNow;

            string? prevStage = null;
            await using (var prevCmd = new NpgsqlCommand(
                "SELECT stage FROM public.odontogram_entries WHERE patient_id = @pid AND tooth_number = @tooth;",
                conn, tx))
            {
                prevCmd.Parameters.AddWithValue("pid", patientId);
                prevCmd.Parameters.AddWithValue("tooth", request.ToothNumber);
                prevStage = (await prevCmd.ExecuteScalarAsync(ct)) as string;
            }

            Guid toothId;
            const string upsertToothSql = """
                INSERT INTO public.odontogram_entries
                    (id, clinic_group_id, patient_id, tooth_number, condition, procedure_name, fee, stage, doctor_name, updated_at, is_deleted)
                VALUES
                    (@id, @groupId, @pid, @tooth, @condition, @proc, @fee, @stage, @doctor, @now, FALSE)
                ON CONFLICT (patient_id, tooth_number) DO UPDATE SET
                    condition = EXCLUDED.condition,
                    procedure_name = EXCLUDED.procedure_name,
                    fee = EXCLUDED.fee,
                    stage = EXCLUDED.stage,
                    doctor_name = EXCLUDED.doctor_name,
                    updated_at = EXCLUDED.updated_at,
                    is_deleted = FALSE
                RETURNING id;
                """;

            await using (var cmd = new NpgsqlCommand(upsertToothSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("id", Guid.NewGuid());
                cmd.Parameters.AddWithValue("groupId", _claimsContext.ClinicGroupId);
                cmd.Parameters.AddWithValue("pid", patientId);
                cmd.Parameters.AddWithValue("tooth", request.ToothNumber);
                cmd.Parameters.AddWithValue("condition", request.Condition.ToString());
                cmd.Parameters.AddWithValue("proc", request.ProcedureName?.Trim() ?? string.Empty);
                cmd.Parameters.AddWithValue("fee", fee);
                cmd.Parameters.AddWithValue("stage", request.Stage.ToString());
                cmd.Parameters.AddWithValue("doctor", doctorName);
                cmd.Parameters.AddWithValue("now", now);
                toothId = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
            }

            var updatedTooth = new OdontogramToothDto
            {
                Id = toothId,
                PatientId = patientId,
                ToothNumber = request.ToothNumber,
                Condition = request.Condition,
                ProcedureName = request.ProcedureName?.Trim() ?? string.Empty,
                Fee = fee,
                Stage = request.Stage,
                DoctorName = doctorName,
                UpdatedAt = now
            };

            InvoiceDto? updatedInvoice = null;
            if (request.Stage == TreatmentStage.Completed)
            {
                var draftInvoice = await FindOrCreateDraftInvoiceAsync(conn, tx, patient, branchId, doctorName, ct);
                var label = ClinicDomainHelpers.BuildCompletedToothProcedureLabel(request);
                var existingLine = draftInvoice.Lines.FirstOrDefault(l =>
                    l.ToothNumber == request.ToothNumber && l.SourceStage == TreatmentStage.Completed);

                if (existingLine is not null)
                {
                    existingLine.ProcedureName = label;
                    existingLine.UnitPrice = fee;
                    existingLine.Quantity = 1;
                    existingLine.LineTotal = BillingCalculator.ComputeLineTotal(fee, 1);

                    await using var updateLineCmd = new NpgsqlCommand(
                        "UPDATE public.invoice_lines SET procedure_name = @proc, unit_price = @price, quantity = 1, line_total = @total WHERE id = @id;",
                        conn, tx);
                    updateLineCmd.Parameters.AddWithValue("proc", existingLine.ProcedureName);
                    updateLineCmd.Parameters.AddWithValue("price", existingLine.UnitPrice);
                    updateLineCmd.Parameters.AddWithValue("total", existingLine.LineTotal);
                    updateLineCmd.Parameters.AddWithValue("id", existingLine.Id);
                    await updateLineCmd.ExecuteNonQueryAsync(ct);
                }
                else
                {
                    var newLine = new InvoiceLineDto
                    {
                        Id = Guid.NewGuid(),
                        ToothNumber = request.ToothNumber,
                        ProcedureName = label,
                        UnitPrice = fee,
                        Quantity = 1,
                        LineTotal = BillingCalculator.ComputeLineTotal(fee, 1),
                        SourceStage = TreatmentStage.Completed
                    };
                    draftInvoice.Lines.Add(newLine);

                    await using var insertLineCmd = new NpgsqlCommand("""
                        INSERT INTO public.invoice_lines
                            (id, invoice_id, branch_id, tooth_number, procedure_name, unit_price, quantity, line_total, source_stage)
                        VALUES
                            (@id, @invoiceId, @branchId, @tooth, @proc, @price, 1, @total, 'Completed');
                        """, conn, tx);
                    insertLineCmd.Parameters.AddWithValue("id", newLine.Id);
                    insertLineCmd.Parameters.AddWithValue("invoiceId", draftInvoice.Id);
                    insertLineCmd.Parameters.AddWithValue("branchId", branchId);
                    insertLineCmd.Parameters.AddWithValue("tooth", request.ToothNumber);
                    insertLineCmd.Parameters.AddWithValue("proc", newLine.ProcedureName);
                    insertLineCmd.Parameters.AddWithValue("price", newLine.UnitPrice);
                    insertLineCmd.Parameters.AddWithValue("total", newLine.LineTotal);
                    await insertLineCmd.ExecuteNonQueryAsync(ct);
                }

                BillingCalculator.RecalculateInvoice(draftInvoice, _clinicOptions.CommissionRate);
                await PersistInvoiceTotalsInternalAsync(conn, tx, draftInvoice, ct);
                updatedInvoice = draftInvoice;
            }

            await InsertAuditLogInternalAsync(
                conn,
                tx,
                branchId,
                "ODONTOGRAM_UPDATED",
                "OdontogramTooth",
                $"{patientId}:{request.ToothNumber}",
                prevStage,
                $"{updatedTooth.Condition} | {updatedTooth.ProcedureName} | {updatedTooth.Stage} | RM{updatedTooth.Fee:F2}",
                ct);

            await tx.CommitAsync(ct);
            return (updatedTooth, updatedInvoice, null);
        }
    }

    public async Task<IReadOnlyList<SoapNoteDto>> GetSoapNotesAsync(Guid patientId, CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            const string sql = """
                SELECT id, patient_id, branch_id, doctor_name, visit_date, subjective, objective, assessment, plan,
                       anesthesia_used, tooth_shade, post_op_instructions
                FROM public.soap_notes
                WHERE patient_id = @pid AND is_voided = FALSE
                ORDER BY visit_date DESC;
                """;

            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("pid", patientId);

            var list = new List<SoapNoteDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new SoapNoteDto
                {
                    Id = reader.GetGuid(0),
                    PatientId = reader.GetGuid(1),
                    BranchId = reader.GetString(2),
                    DoctorName = reader.GetString(3),
                    VisitDate = reader.GetFieldValue<DateTimeOffset>(4),
                    Subjective = reader.GetString(5),
                    Objective = reader.GetString(6),
                    Assessment = reader.GetString(7),
                    Plan = reader.GetString(8),
                    AnesthesiaUsed = reader.GetString(9),
                    ToothShade = reader.GetString(10),
                    PostOpInstructions = reader.GetString(11)
                });
            }

            await tx.CommitAsync(ct);
            return list;
        }
    }

    public async Task<SoapNoteDto?> CreateSoapNoteAsync(Guid patientId, CreateSoapNoteRequest request, CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var patient = await GetPatientInternalAsync(conn, tx, patientId, ct);
            if (patient is null)
            {
                return null;
            }

            var branchId = ClinicDomainHelpers.NormalizeBranch(request.BranchId, patient.PrimaryBranchId);
            var note = new SoapNoteDto
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                BranchId = branchId,
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

            const string sql = """
                INSERT INTO public.soap_notes
                    (id, clinic_group_id, branch_id, patient_id, doctor_name, visit_date, subjective, objective, assessment, plan,
                     anesthesia_used, tooth_shade, post_op_instructions)
                VALUES
                    (@id, @groupId, @branchId, @pid, @doctor, @visitDate, @s, @o, @a, @p, @anesth, @shade, @postOp);
                """;

            await using (var cmd = new NpgsqlCommand(sql, conn, tx))
            {
                cmd.Parameters.AddWithValue("id", note.Id);
                cmd.Parameters.AddWithValue("groupId", _claimsContext.ClinicGroupId);
                cmd.Parameters.AddWithValue("branchId", note.BranchId);
                cmd.Parameters.AddWithValue("pid", note.PatientId);
                cmd.Parameters.AddWithValue("doctor", note.DoctorName);
                cmd.Parameters.AddWithValue("visitDate", note.VisitDate);
                cmd.Parameters.AddWithValue("s", note.Subjective);
                cmd.Parameters.AddWithValue("o", note.Objective);
                cmd.Parameters.AddWithValue("a", note.Assessment);
                cmd.Parameters.AddWithValue("p", note.Plan);
                cmd.Parameters.AddWithValue("anesth", note.AnesthesiaUsed);
                cmd.Parameters.AddWithValue("shade", note.ToothShade);
                cmd.Parameters.AddWithValue("postOp", note.PostOpInstructions);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await InsertAuditLogInternalAsync(
                conn,
                tx,
                note.BranchId,
                "SOAP_NOTE_CREATED",
                "SoapNote",
                note.Id.ToString(),
                null,
                $"{patient.FullName} | {note.Assessment}",
                ct);

            await tx.CommitAsync(ct);
            return note;
        }
    }

    public async Task<IReadOnlyList<SoapMacroDto>> GetSoapMacrosAsync(CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            const string sql = """
                SELECT id, title, category, subjective_template, objective_template, assessment_template, plan_template, default_fee
                FROM public.soap_macros
                WHERE is_active = TRUE
                ORDER BY category, title;
                """;

            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            var list = new List<SoapMacroDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new SoapMacroDto
                {
                    Id = reader.GetGuid(0),
                    Title = reader.GetString(1),
                    Category = reader.GetString(2),
                    SubjectiveTemplate = reader.GetString(3),
                    ObjectiveTemplate = reader.GetString(4),
                    AssessmentTemplate = reader.GetString(5),
                    PlanTemplate = reader.GetString(6),
                    DefaultFee = reader.GetDecimal(7)
                });
            }

            await tx.CommitAsync(ct);
            return list;
        }
    }

    public async Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(string? branchId = null, CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var invoices = await QueryInvoicesInternalAsync(conn, tx, branchId, null, ct);
            await tx.CommitAsync(ct);
            return invoices;
        }
    }

    public async Task<InvoiceDto?> GetInvoiceByIdAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var invoices = await QueryInvoicesInternalAsync(conn, tx, null, invoiceId, ct);
            await tx.CommitAsync(ct);
            return invoices.FirstOrDefault();
        }
    }

    public async Task<(InvoiceDto? Invoice, AppointmentQueueItemDto? UpdatedQueueItem, string? Error)> ApplyInvoicePaymentAsync(
        Guid invoiceId,
        ApplyPaymentRequest request,
        CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var invoice = (await QueryInvoicesInternalAsync(conn, tx, null, invoiceId, ct)).FirstOrDefault();
            if (invoice is null)
            {
                return (null, null, "Invoice not found.");
            }

            var existingCount = invoice.Payments.Count;
            var validation = BillingCalculator.ValidateAndApplyPayments(invoice, request);
            if (!validation.IsValid)
            {
                return (null, null, validation.ErrorMessage);
            }

            var collectedBy = string.IsNullOrWhiteSpace(request.CollectedBy) ? _claimsContext.UserId : request.CollectedBy.Trim();
            foreach (var split in validation.Invoice.Payments.Skip(existingCount))
            {
                await using var payCmd = new NpgsqlCommand("""
                    INSERT INTO public.payments
                        (id, invoice_id, branch_id, method, amount, reference_no, panel_name, collected_by)
                    VALUES
                        (@id, @invoiceId, @branchId, @method, @amount, @refNo, @panelName, @collectedBy);
                    """, conn, tx);
                payCmd.Parameters.AddWithValue("id", Guid.NewGuid());
                payCmd.Parameters.AddWithValue("invoiceId", validation.Invoice.Id);
                payCmd.Parameters.AddWithValue("branchId", validation.Invoice.BranchId);
                payCmd.Parameters.AddWithValue("method", split.Method.ToString());
                payCmd.Parameters.AddWithValue("amount", split.Amount);
                payCmd.Parameters.AddWithValue("refNo", (object?)split.ReferenceNo ?? DBNull.Value);
                payCmd.Parameters.AddWithValue("panelName", (object?)split.PanelName ?? DBNull.Value);
                payCmd.Parameters.AddWithValue("collectedBy", collectedBy);
                await payCmd.ExecuteNonQueryAsync(ct);
            }

            await PersistInvoiceTotalsInternalAsync(conn, tx, validation.Invoice, ct);

            AppointmentQueueItemDto? updatedQueue = null;
            if (validation.Invoice.BalanceDue == 0m)
            {
                Guid? apptId = validation.Invoice.AppointmentId;
                if (!apptId.HasValue)
                {
                    await using var findAppt = new NpgsqlCommand("""
                        SELECT id FROM public.appointments
                        WHERE patient_id = @pid AND branch_id = @branchId AND is_deleted = FALSE
                          AND status NOT IN ('Completed', 'Cancelled')
                        ORDER BY scheduled_at DESC
                        LIMIT 1;
                        """, conn, tx);
                    findAppt.Parameters.AddWithValue("pid", validation.Invoice.PatientId);
                    findAppt.Parameters.AddWithValue("branchId", validation.Invoice.BranchId);
                    var scalar = await findAppt.ExecuteScalarAsync(ct);
                    if (scalar is Guid g)
                    {
                        apptId = g;
                    }
                }

                if (apptId.HasValue)
                {
                    var linked = await GetAppointmentInternalAsync(conn, tx, apptId.Value, ct);
                    if (linked is not null && linked.Status != QueueStatus.Completed && linked.Status != QueueStatus.Cancelled)
                    {
                        ClinicDomainHelpers.ApplyStatusMilestone(
                            linked,
                            linked.Status,
                            QueueStatus.Completed,
                            collectedBy,
                            DateTimeOffset.UtcNow,
                            $"Invoice {validation.Invoice.InvoiceNumber}");
                        await PersistAppointmentLifecycleAsync(conn, tx, linked, ct);
                        updatedQueue = await GetAppointmentInternalAsync(conn, tx, apptId.Value, ct);
                    }
                }
            }

            await InsertAuditLogInternalAsync(
                conn,
                tx,
                validation.Invoice.BranchId,
                "INVOICE_PAYMENT_APPLIED",
                "Invoice",
                validation.Invoice.InvoiceNumber,
                null,
                $"Paid=RM{validation.Invoice.PaidAmount:F2}; Panel=RM{validation.Invoice.PanelCoveredAmount:F2}; BalanceDue=RM{validation.Invoice.BalanceDue:F2}; Commission=RM{validation.Invoice.DoctorCommissionAmount:F2}",
                ct);

            await tx.CommitAsync(ct);
            return (validation.Invoice, updatedQueue, null);
        }
    }

    public async Task<(InvoiceDto? Invoice, string? Error)> VoidInvoiceAsync(
        Guid invoiceId,
        VoidInvoiceRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.VoidReason))
        {
            return (null, "VoidReason is required to void an invoice.");
        }

        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var invoice = (await QueryInvoicesInternalAsync(conn, tx, null, invoiceId, ct)).FirstOrDefault();
            if (invoice is null)
            {
                return (null, "Invoice not found.");
            }

            if (invoice.IsVoided)
            {
                return (null, "Invoice is already voided.");
            }

            var oldValue = $"Subtotal=RM{invoice.Subtotal:F2}; Paid=RM{invoice.PaidAmount:F2}; IsVoided=false";
            var voidedBy = string.IsNullOrWhiteSpace(request.VoidedBy) ? _claimsContext.UserId : request.VoidedBy.Trim();
            var reason = request.VoidReason.Trim();

            await using (var cmd = new NpgsqlCommand("""
                UPDATE public.invoices
                SET is_voided = TRUE,
                    void_reason = @reason,
                    voided_by = @voidedBy,
                    voided_at = NOW(),
                    doctor_commission_amount = 0.00
                WHERE id = @id;
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("reason", reason);
                cmd.Parameters.AddWithValue("voidedBy", voidedBy);
                cmd.Parameters.AddWithValue("id", invoiceId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            invoice.IsVoided = true;
            invoice.VoidReason = reason;
            invoice.DoctorCommissionAmount = 0m;

            await InsertAuditLogInternalAsync(
                conn,
                tx,
                invoice.BranchId,
                "INVOICE_VOIDED",
                "Invoice",
                invoice.InvoiceNumber,
                oldValue,
                $"IsVoided=true; Reason={reason}; VoidedBy={voidedBy}",
                ct);

            await tx.CommitAsync(ct);
            return (invoice, null);
        }
    }

    public async Task<DailySalesReconciliationDto> GetDailySalesSummaryAsync(string branchId, DateOnly date, CancellationToken ct = default)
    {
        var branch = ClinicDomainHelpers.NormalizeBranch(branchId, _clinicOptions.DefaultBranchId);
        var invoices = await GetInvoicesAsync(branch, ct);
        return BillingCalculator.CalculateDailySalesReconciliation(branch, date, invoices, _claimsContext.UserId);
    }

    public async Task<DailySalesReconciliationDto> ReconcileDailySalesAsync(ReconcileDailySalesRequest request, CancellationToken ct = default)
    {
        var branch = ClinicDomainHelpers.NormalizeBranch(request.BranchId, _clinicOptions.DefaultBranchId);
        var reconciledBy = string.IsNullOrWhiteSpace(request.ReconciledBy) ? _claimsContext.UserId : request.ReconciledBy.Trim();

        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var invoices = await QueryInvoicesInternalAsync(conn, tx, branch, null, ct);
            var summary = BillingCalculator.CalculateDailySalesReconciliation(branch, request.Date, invoices, reconciledBy);

            const string sql = """
                INSERT INTO public.daily_sales_reconciliations
                    (id, branch_id, sales_date, total_invoices_count, voided_invoices_count, gross_revenue,
                     total_discounts, total_panel_claims, total_cash_collected, total_card_collected, total_qr_collected,
                     total_doctor_commissions, net_collected, is_balanced, reconciled_by, reconciled_at)
                VALUES
                    (@id, @branchId, @salesDate, @totalInvoices, @voidedInvoices, @gross, @discounts, @panelClaims,
                     @cash, @card, @qr, @commissions, @net, @balanced, @reconciledBy, @reconciledAt)
                ON CONFLICT (branch_id, sales_date) DO UPDATE SET
                    total_invoices_count = EXCLUDED.total_invoices_count,
                    voided_invoices_count = EXCLUDED.voided_invoices_count,
                    gross_revenue = EXCLUDED.gross_revenue,
                    total_discounts = EXCLUDED.total_discounts,
                    total_panel_claims = EXCLUDED.total_panel_claims,
                    total_cash_collected = EXCLUDED.total_cash_collected,
                    total_card_collected = EXCLUDED.total_card_collected,
                    total_qr_collected = EXCLUDED.total_qr_collected,
                    total_doctor_commissions = EXCLUDED.total_doctor_commissions,
                    net_collected = EXCLUDED.net_collected,
                    is_balanced = EXCLUDED.is_balanced,
                    reconciled_by = EXCLUDED.reconciled_by,
                    reconciled_at = EXCLUDED.reconciled_at;
                """;

            await using (var cmd = new NpgsqlCommand(sql, conn, tx))
            {
                cmd.Parameters.AddWithValue("id", Guid.NewGuid());
                cmd.Parameters.AddWithValue("branchId", summary.BranchId);
                cmd.Parameters.AddWithValue("salesDate", summary.Date);
                cmd.Parameters.AddWithValue("totalInvoices", summary.TotalInvoicesCount);
                cmd.Parameters.AddWithValue("voidedInvoices", summary.VoidedInvoicesCount);
                cmd.Parameters.AddWithValue("gross", summary.GrossRevenue);
                cmd.Parameters.AddWithValue("discounts", summary.TotalDiscounts);
                cmd.Parameters.AddWithValue("panelClaims", summary.TotalPanelClaims);
                cmd.Parameters.AddWithValue("cash", summary.TotalCashCollected);
                cmd.Parameters.AddWithValue("card", summary.TotalCardCollected);
                cmd.Parameters.AddWithValue("qr", summary.TotalQrCollected);
                cmd.Parameters.AddWithValue("commissions", summary.TotalDoctorCommissions);
                cmd.Parameters.AddWithValue("net", summary.NetCollected);
                cmd.Parameters.AddWithValue("balanced", summary.IsBalanced);
                cmd.Parameters.AddWithValue("reconciledBy", summary.ReconciledBy);
                cmd.Parameters.AddWithValue("reconciledAt", summary.ReconciledAt);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await InsertAuditLogInternalAsync(
                conn,
                tx,
                branch,
                "DAILY_SALES_RECONCILED",
                "DailySalesReconciliation",
                $"{branch}:{request.Date:yyyy-MM-dd}",
                null,
                $"Net=RM{summary.NetCollected:F2}; Balanced={summary.IsBalanced}",
                ct);

            await tx.CommitAsync(ct);
            return summary;
        }
    }

    public async Task<IReadOnlyList<AuditLogDto>> GetAuditLogsAsync(string? branchId = null, CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            const string sql = """
                SELECT id, branch_id, user_id, timestamp, action, entity_name, entity_id, old_value, new_value
                FROM public.audit_logs
                WHERE (@branchId IS NULL OR branch_id = @branchId)
                ORDER BY timestamp DESC
                LIMIT 500;
                """;

            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("branchId",
                string.IsNullOrWhiteSpace(branchId) ? DBNull.Value : ClinicDomainHelpers.NormalizeBranch(branchId, _clinicOptions.DefaultBranchId));

            var logs = new List<AuditLogDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                logs.Add(new AuditLogDto
                {
                    Id = reader.GetGuid(0),
                    BranchId = reader.GetString(1),
                    UserId = reader.GetString(2),
                    Timestamp = reader.GetFieldValue<DateTimeOffset>(3),
                    Action = reader.GetString(4),
                    EntityName = reader.GetString(5),
                    EntityId = reader.GetString(6),
                    OldValue = reader.IsDBNull(7) ? null : reader.GetString(7),
                    NewValue = reader.IsDBNull(8) ? null : reader.GetString(8)
                });
            }

            await tx.CommitAsync(ct);
            return logs;
        }
    }

    public async Task<IReadOnlyList<BackupVerificationDto>> GetBackupVerificationsAsync(CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            const string sql = """
                SELECT id, verified_at, file_name, sha256_checksum, size_bytes, is_encrypted, restore_test_passed, verified_by, notes
                FROM public.backup_verifications
                ORDER BY verified_at DESC
                LIMIT 100;
                """;

            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            var list = new List<BackupVerificationDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new BackupVerificationDto
                {
                    Id = reader.GetGuid(0),
                    VerifiedAt = reader.GetFieldValue<DateTimeOffset>(1),
                    FileName = reader.GetString(2),
                    Sha256Checksum = reader.GetString(3),
                    SizeBytes = reader.GetInt64(4),
                    IsEncrypted = reader.GetBoolean(5),
                    RestoreTestPassed = reader.GetBoolean(6),
                    VerifiedBy = reader.GetString(7),
                    Notes = reader.GetString(8)
                });
            }

            await tx.CommitAsync(ct);
            return list;
        }
    }

    public async Task<BackupVerificationDto> RecordBackupVerificationAsync(BackupVerificationDto verification, CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            const string sql = """
                INSERT INTO public.backup_verifications
                    (id, clinic_group_id, verified_at, file_name, sha256_checksum, size_bytes, is_encrypted, restore_test_passed, verified_by, notes)
                VALUES
                    (@id, @groupId, @verifiedAt, @fileName, @sha, @size, @enc, @passed, @by, @notes);
                """;

            await using (var cmd = new NpgsqlCommand(sql, conn, tx))
            {
                cmd.Parameters.AddWithValue("id", verification.Id);
                cmd.Parameters.AddWithValue("groupId", _claimsContext.ClinicGroupId);
                cmd.Parameters.AddWithValue("verifiedAt", verification.VerifiedAt);
                cmd.Parameters.AddWithValue("fileName", verification.FileName);
                cmd.Parameters.AddWithValue("sha", verification.Sha256Checksum);
                cmd.Parameters.AddWithValue("size", verification.SizeBytes);
                cmd.Parameters.AddWithValue("enc", verification.IsEncrypted);
                cmd.Parameters.AddWithValue("passed", verification.RestoreTestPassed);
                cmd.Parameters.AddWithValue("by", verification.VerifiedBy);
                cmd.Parameters.AddWithValue("notes", verification.Notes);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await InsertAuditLogInternalAsync(
                conn,
                tx,
                _clinicOptions.DefaultBranchId,
                "BACKUP_VERIFIED",
                "BackupVerification",
                verification.Id.ToString(),
                null,
                $"{verification.FileName} | SHA256={verification.Sha256Checksum}",
                ct);

            await tx.CommitAsync(ct);
            return verification;
        }
    }

    public async Task<IReadOnlyList<WhatsAppThreadDto>> GetWhatsAppThreadsAsync(string? branchId = null, CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var threads = await QueryWhatsAppThreadsInternalAsync(conn, tx, branchId, null, ct);
            await tx.CommitAsync(ct);
            return threads;
        }
    }

    public async Task<WhatsAppThreadDto?> GetWhatsAppThreadAsync(string threadId, CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var threads = await QueryWhatsAppThreadsInternalAsync(conn, tx, null, threadId, ct);
            await tx.CommitAsync(ct);
            return threads.FirstOrDefault();
        }
    }

    public async Task<WhatsAppThreadDto> AppendWhatsAppMessageAsync(
        string threadId,
        string branchId,
        string patientPhone,
        string patientName,
        WhatsAppMessageDto message,
        bool? setEmergencyEscalated = null,
        string? emergencyReason = null,
        CancellationToken ct = default)
    {
        var resolvedBranch = ClinicDomainHelpers.NormalizeBranch(branchId, _clinicOptions.DefaultBranchId);
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            await using (var existingBranchCmd = new NpgsqlCommand(
                "SELECT branch_id FROM public.whatsapp_threads WHERE thread_id = @tid LIMIT 1;",
                conn, tx))
            {
                existingBranchCmd.Parameters.AddWithValue("tid", threadId);
                var existingBranch = (await existingBranchCmd.ExecuteScalarAsync(ct)) as string;
                if (!string.IsNullOrWhiteSpace(existingBranch))
                {
                    resolvedBranch = existingBranch;
                }
            }

            const string upsertThreadSql = """
                INSERT INTO public.whatsapp_threads
                    (thread_id, clinic_group_id, branch_id, patient_phone, patient_name, is_emergency_escalated, emergency_reason, last_updated_at)
                VALUES
                    (@threadId, @groupId, @branchId, @phone, @name, @esc, @reason, @ts)
                ON CONFLICT (thread_id) DO UPDATE SET
                    patient_name = CASE WHEN EXCLUDED.patient_name <> '' THEN EXCLUDED.patient_name ELSE public.whatsapp_threads.patient_name END,
                    is_emergency_escalated = CASE WHEN @escSet IS NOT NULL THEN @esc ELSE public.whatsapp_threads.is_emergency_escalated END,
                    emergency_reason = COALESCE(@reason, public.whatsapp_threads.emergency_reason),
                    last_updated_at = EXCLUDED.last_updated_at;
                """;

            await using (var tCmd = new NpgsqlCommand(upsertThreadSql, conn, tx))
            {
                tCmd.Parameters.AddWithValue("threadId", threadId);
                tCmd.Parameters.AddWithValue("groupId", _claimsContext.ClinicGroupId);
                tCmd.Parameters.AddWithValue("branchId", resolvedBranch);
                tCmd.Parameters.AddWithValue("phone", patientPhone);
                tCmd.Parameters.AddWithValue("name", string.IsNullOrWhiteSpace(patientName) ? patientPhone : patientName.Trim());
                tCmd.Parameters.AddWithValue("esc", setEmergencyEscalated ?? false);
                tCmd.Parameters.AddWithValue("escSet", (object?)setEmergencyEscalated ?? DBNull.Value);
                tCmd.Parameters.AddWithValue("reason", (object?)emergencyReason ?? DBNull.Value);
                tCmd.Parameters.AddWithValue("ts", message.Timestamp);
                await tCmd.ExecuteNonQueryAsync(ct);
            }

            const string insertMsgSql = """
                INSERT INTO public.whatsapp_messages
                    (id, thread_id, branch_id, sender, text, timestamp, created_booking_id)
                VALUES
                    (@id, @threadId, @branchId, @sender, @text, @ts, @bookingId);
                """;

            await using (var mCmd = new NpgsqlCommand(insertMsgSql, conn, tx))
            {
                mCmd.Parameters.AddWithValue("id", message.Id);
                mCmd.Parameters.AddWithValue("threadId", threadId);
                mCmd.Parameters.AddWithValue("branchId", resolvedBranch);
                mCmd.Parameters.AddWithValue("sender", message.Sender);
                mCmd.Parameters.AddWithValue("text", message.Text);
                mCmd.Parameters.AddWithValue("ts", message.Timestamp);
                mCmd.Parameters.AddWithValue("bookingId", (object?)message.CreatedBookingId ?? DBNull.Value);
                await mCmd.ExecuteNonQueryAsync(ct);
            }

            var updated = (await QueryWhatsAppThreadsInternalAsync(conn, tx, null, threadId, ct)).First();
            await tx.CommitAsync(ct);
            return updated;
        }
    }

    public async Task<(AppointmentQueueItemDto? Appointment, WhatsAppThreadDto? Thread, string? Error)> ApproveAiBookingAsync(
        Guid appointmentId,
        string approvedBy,
        CancellationToken ct = default)
    {
        var (conn, tx) = await OpenScopedTransactionAsync(ct);
        await using (conn)
        await using (tx)
        {
            var appt = await GetAppointmentInternalAsync(conn, tx, appointmentId, ct);
            if (appt is null)
            {
                return (null, null, "Appointment not found.");
            }

            if (appt.Status != QueueStatus.AiBookedPending && appt.Status != QueueStatus.Scheduled)
            {
                return (null, null, $"Cannot approve booking in status {appt.Status}.");
            }

            var oldStatus = appt.Status;
            var actor = string.IsNullOrWhiteSpace(approvedBy) ? _claimsContext.UserId : approvedBy.Trim();
            ClinicDomainHelpers.ApplyStatusMilestone(appt, oldStatus, QueueStatus.Scheduled, actor, DateTimeOffset.UtcNow);
            await PersistAppointmentLifecycleAsync(conn, tx, appt, ct);

            string? threadId = null;
            await using (var findThread = new NpgsqlCommand(
                "SELECT thread_id FROM public.whatsapp_messages WHERE created_booking_id = @bid LIMIT 1;",
                conn, tx))
            {
                findThread.Parameters.AddWithValue("bid", appointmentId);
                threadId = (await findThread.ExecuteScalarAsync(ct)) as string;
            }

            WhatsAppThreadDto? updatedThread = null;
            if (!string.IsNullOrWhiteSpace(threadId))
            {
                var now = DateTimeOffset.UtcNow;
                await using (var msgCmd = new NpgsqlCommand("""
                    INSERT INTO public.whatsapp_messages (id, thread_id, branch_id, sender, text, timestamp, created_booking_id)
                    VALUES (@id, @tid, @branchId, @sender, @text, @ts, @bid);
                    UPDATE public.whatsapp_threads SET last_updated_at = @ts WHERE thread_id = @tid;
                    """, conn, tx))
                {
                    msgCmd.Parameters.AddWithValue("id", Guid.NewGuid());
                    msgCmd.Parameters.AddWithValue("tid", threadId);
                    msgCmd.Parameters.AddWithValue("branchId", appt.BranchId);
                    msgCmd.Parameters.AddWithValue("sender", $"Staff ({actor})");
                    msgCmd.Parameters.AddWithValue("text", $"Appointment approved & confirmed for {appt.ScheduledAt:ddd dd MMM, hh:mm tt} with {appt.DoctorName} at {appt.BranchId}.");
                    msgCmd.Parameters.AddWithValue("ts", now);
                    msgCmd.Parameters.AddWithValue("bid", appointmentId);
                    await msgCmd.ExecuteNonQueryAsync(ct);
                }

                updatedThread = (await QueryWhatsAppThreadsInternalAsync(conn, tx, null, threadId, ct)).FirstOrDefault();
            }

            await InsertAuditLogInternalAsync(
                conn,
                tx,
                appt.BranchId,
                "AI_BOOKING_APPROVED",
                "Appointment",
                appointmentId.ToString(),
                oldStatus.ToString(),
                QueueStatus.Scheduled.ToString(),
                ct,
                actor);

            var updatedAppt = (await GetAppointmentInternalAsync(conn, tx, appointmentId, ct))!;
            await tx.CommitAsync(ct);
            return (updatedAppt, updatedThread, null);
        }
    }

    private async Task<PatientDto?> GetPatientInternalAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid patientId, CancellationToken ct)
    {
        const string sql = """
            SELECT p.id, p.full_name, p.ic_number, p.date_of_birth, p.gender, p.phone, p.address,
                   p.primary_branch_id, p.is_mykad_verified, p.consent_signed_at,
                   COALESCE(m.allergies, ''), COALESCE(m.on_anticoagulants, FALSE),
                   COALESCE(m.has_diabetes, FALSE), COALESCE(m.is_pregnant, FALSE), COALESCE(m.notes, '')
            FROM public.patients p
            LEFT JOIN public.medical_alerts m ON m.patient_id = p.id
            WHERE p.id = @id AND p.is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", patientId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadPatient(reader) : null;
    }

    private async Task UpsertMedicalAlertsInternalAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid patientId, MedicalAlertDto alerts, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO public.medical_alerts
                (patient_id, clinic_group_id, allergies, on_anticoagulants, has_diabetes, is_pregnant, notes, updated_at)
            VALUES
                (@pid, @groupId, @allergies, @anti, @diab, @preg, @notes, NOW())
            ON CONFLICT (patient_id) DO UPDATE SET
                allergies = EXCLUDED.allergies,
                on_anticoagulants = EXCLUDED.on_anticoagulants,
                has_diabetes = EXCLUDED.has_diabetes,
                is_pregnant = EXCLUDED.is_pregnant,
                notes = EXCLUDED.notes,
                updated_at = NOW();
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("pid", patientId);
        cmd.Parameters.AddWithValue("groupId", _claimsContext.ClinicGroupId);
        cmd.Parameters.AddWithValue("allergies", alerts.Allergies?.Trim() ?? string.Empty);
        cmd.Parameters.AddWithValue("anti", alerts.OnAnticoagulants);
        cmd.Parameters.AddWithValue("diab", alerts.HasDiabetes);
        cmd.Parameters.AddWithValue("preg", alerts.IsPregnant);
        cmd.Parameters.AddWithValue("notes", alerts.Notes?.Trim() ?? string.Empty);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task SyncAppointmentAlertsInternalAsync(NpgsqlConnection conn, NpgsqlTransaction tx, PatientDto patient, CancellationToken ct)
    {
        const string sql = """
            UPDATE public.appointments
            SET patient_name = @name,
                patient_ic = @ic,
                has_critical_medical_alert = @hasAlert,
                alert_summary = @summary,
                updated_at = NOW()
            WHERE patient_id = @pid AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("name", patient.FullName);
        cmd.Parameters.AddWithValue("ic", patient.IcNumber);
        cmd.Parameters.AddWithValue("hasAlert", patient.MedicalAlerts.HasAnyCriticalAlert);
        cmd.Parameters.AddWithValue("summary", patient.MedicalAlerts.BuildAlertSummary());
        cmd.Parameters.AddWithValue("pid", patient.Id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task PersistAppointmentLifecycleAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        AppointmentQueueItemDto item,
        CancellationToken ct)
    {
        var updatedAt = item.LastUpdatedAt ?? DateTimeOffset.UtcNow;
        const string sql = """
            UPDATE public.appointments
            SET status = @status,
                room_name = @roomName,
                arrived_at = @arrivedAt,
                seated_at = @seatedAt,
                checkout_at = @checkoutAt,
                completed_at = @completedAt,
                checked_in_by = @checkedInBy,
                seated_by = @seatedBy,
                checkout_by = @checkoutBy,
                completed_by = @completedBy,
                last_updated_by = @lastUpdatedBy,
                activity_log = @activityLog,
                updated_at = @updatedAt
            WHERE id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("status", item.Status.ToString());
        cmd.Parameters.AddWithValue("roomName", item.RoomName);
        cmd.Parameters.AddWithValue("arrivedAt", (object?)item.ArrivedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("seatedAt", (object?)item.SeatedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("checkoutAt", (object?)item.CheckoutAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("completedAt", (object?)item.CompletedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("checkedInBy", item.CheckedInBy);
        cmd.Parameters.AddWithValue("seatedBy", item.SeatedBy);
        cmd.Parameters.AddWithValue("checkoutBy", item.CheckoutBy);
        cmd.Parameters.AddWithValue("completedBy", item.CompletedBy);
        cmd.Parameters.AddWithValue("lastUpdatedBy", item.LastUpdatedBy);
        cmd.Parameters.Add(new NpgsqlParameter("activityLog", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(item.ActivityLog, ActivityJsonOptions)
        });
        cmd.Parameters.AddWithValue("updatedAt", updatedAt);
        cmd.Parameters.AddWithValue("id", item.Id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<AppointmentQueueItemDto?> GetAppointmentInternalAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id, CancellationToken ct)
    {
        const string sql = """
            SELECT id, branch_id, patient_id, patient_name, patient_ic, dentist_id, doctor_name,
                   treatment_id, room_name, scheduled_at, duration_minutes, procedure_summary,
                   notes, status, has_critical_medical_alert, alert_summary,
                   visit_type, arrived_at, seated_at, checkout_at, completed_at,
                   created_by, checked_in_by, seated_by, checkout_by, completed_by,
                   last_updated_by, updated_at, activity_log
            FROM public.appointments
            WHERE id = @id AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadAppointment(reader) : null;
    }

    private async Task<InvoiceDto> FindOrCreateDraftInvoiceAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        PatientDto patient,
        string branchId,
        string doctorName,
        CancellationToken ct)
    {
        const string findDraftSql = """
            SELECT i.id
            FROM public.invoices i
            WHERE i.patient_id = @pid
              AND i.branch_id = @branchId
              AND i.is_voided = FALSE
              AND NOT EXISTS (SELECT 1 FROM public.payments p WHERE p.invoice_id = i.id)
            ORDER BY i.created_at DESC
            LIMIT 1;
            """;

        Guid? draftId = null;
        await using (var cmd = new NpgsqlCommand(findDraftSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("pid", patient.Id);
            cmd.Parameters.AddWithValue("branchId", branchId);
            var scalar = await cmd.ExecuteScalarAsync(ct);
            if (scalar is Guid g)
            {
                draftId = g;
            }
        }

        if (draftId.HasValue)
        {
            return (await QueryInvoicesInternalAsync(conn, tx, null, draftId.Value, ct)).First();
        }

        Guid? apptId = null;
        await using (var apptCmd = new NpgsqlCommand("""
            SELECT id FROM public.appointments
            WHERE patient_id = @pid AND branch_id = @branchId AND is_deleted = FALSE
              AND status NOT IN ('Completed', 'Cancelled')
            ORDER BY scheduled_at DESC LIMIT 1;
            """, conn, tx))
        {
            apptCmd.Parameters.AddWithValue("pid", patient.Id);
            apptCmd.Parameters.AddWithValue("branchId", branchId);
            var scalar = await apptCmd.ExecuteScalarAsync(ct);
            if (scalar is Guid g)
            {
                apptId = g;
            }
        }

        var code = branchId.StartsWith("PJ", StringComparison.OrdinalIgnoreCase) ? "PJ" : "KL";
        var invoiceNumber = $"INV-{code}-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
        var newInvoice = new InvoiceDto
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = invoiceNumber,
            BranchId = branchId,
            PatientId = patient.Id,
            PatientName = patient.FullName,
            DoctorName = doctorName,
            AppointmentId = apptId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await using (var insCmd = new NpgsqlCommand("""
            INSERT INTO public.invoices
                (id, invoice_number, branch_id, patient_id, patient_name, doctor_name, appointment_id, created_at)
            VALUES
                (@id, @invNo, @branchId, @pid, @pname, @doctor, @apptId, @createdAt);
            """, conn, tx))
        {
            insCmd.Parameters.AddWithValue("id", newInvoice.Id);
            insCmd.Parameters.AddWithValue("invNo", newInvoice.InvoiceNumber);
            insCmd.Parameters.AddWithValue("branchId", newInvoice.BranchId);
            insCmd.Parameters.AddWithValue("pid", newInvoice.PatientId);
            insCmd.Parameters.AddWithValue("pname", newInvoice.PatientName);
            insCmd.Parameters.AddWithValue("doctor", newInvoice.DoctorName);
            insCmd.Parameters.AddWithValue("apptId", (object?)newInvoice.AppointmentId ?? DBNull.Value);
            insCmd.Parameters.AddWithValue("createdAt", newInvoice.CreatedAt);
            await insCmd.ExecuteNonQueryAsync(ct);
        }

        return newInvoice;
    }

    private static async Task PersistInvoiceTotalsInternalAsync(NpgsqlConnection conn, NpgsqlTransaction tx, InvoiceDto invoice, CancellationToken ct)
    {
        const string sql = """
            UPDATE public.invoices
            SET subtotal = @subtotal,
                discount_amount = @discount,
                panel_covered_amount = @panelAmt,
                panel_name = @panelName,
                patient_payable_amount = @payable,
                paid_amount = @paid,
                doctor_commission_amount = @commission
            WHERE id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("subtotal", invoice.Subtotal);
        cmd.Parameters.AddWithValue("discount", invoice.DiscountAmount);
        cmd.Parameters.AddWithValue("panelAmt", invoice.PanelCoveredAmount);
        cmd.Parameters.AddWithValue("panelName", (object?)invoice.PanelName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("payable", invoice.PatientPayableAmount);
        cmd.Parameters.AddWithValue("paid", invoice.PaidAmount);
        cmd.Parameters.AddWithValue("commission", invoice.DoctorCommissionAmount);
        cmd.Parameters.AddWithValue("id", invoice.Id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<List<InvoiceDto>> QueryInvoicesInternalAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string? branchId,
        Guid? invoiceId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT id, invoice_number, branch_id, patient_id, patient_name, doctor_name, appointment_id,
                   subtotal, discount_amount, panel_covered_amount, panel_name, patient_payable_amount,
                   paid_amount, doctor_commission_amount, is_voided, void_reason, created_at
            FROM public.invoices
            WHERE (@branchId IS NULL OR branch_id = @branchId)
              AND (@invoiceId IS NULL OR id = @invoiceId)
            ORDER BY created_at DESC;
            """;

        var list = new List<InvoiceDto>();
        var map = new Dictionary<Guid, InvoiceDto>();
        await using (var cmd = new NpgsqlCommand(sql, conn, tx))
        {
            cmd.Parameters.AddWithValue("branchId",
                string.IsNullOrWhiteSpace(branchId) ? DBNull.Value : ClinicDomainHelpers.NormalizeBranch(branchId, _clinicOptions.DefaultBranchId));
            cmd.Parameters.AddWithValue("invoiceId", (object?)invoiceId ?? DBNull.Value);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var inv = new InvoiceDto
                {
                    Id = reader.GetGuid(0),
                    InvoiceNumber = reader.GetString(1),
                    BranchId = reader.GetString(2),
                    PatientId = reader.GetGuid(3),
                    PatientName = reader.GetString(4),
                    DoctorName = reader.GetString(5),
                    AppointmentId = reader.IsDBNull(6) ? null : reader.GetGuid(6),
                    Subtotal = reader.GetDecimal(7),
                    DiscountAmount = reader.GetDecimal(8),
                    PanelCoveredAmount = reader.GetDecimal(9),
                    PanelName = reader.IsDBNull(10) ? null : reader.GetString(10),
                    PatientPayableAmount = reader.GetDecimal(11),
                    PaidAmount = reader.GetDecimal(12),
                    DoctorCommissionAmount = reader.GetDecimal(13),
                    IsVoided = reader.GetBoolean(14),
                    VoidReason = reader.IsDBNull(15) ? null : reader.GetString(15),
                    CreatedAt = reader.GetFieldValue<DateTimeOffset>(16)
                };
                list.Add(inv);
                map[inv.Id] = inv;
            }
        }

        if (list.Count == 0)
        {
            return list;
        }

        var ids = list.Select(i => i.Id).ToArray();

        const string linesSql = """
            SELECT id, invoice_id, tooth_number, procedure_name, unit_price, quantity, line_total, source_stage
            FROM public.invoice_lines
            WHERE invoice_id = ANY(@ids)
            ORDER BY created_at ASC;
            """;

        await using (var lCmd = new NpgsqlCommand(linesSql, conn, tx))
        {
            lCmd.Parameters.AddWithValue("ids", ids);
            await using var reader = await lCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var invId = reader.GetGuid(1);
                if (map.TryGetValue(invId, out var inv))
                {
                    inv.Lines.Add(new InvoiceLineDto
                    {
                        Id = reader.GetGuid(0),
                        ToothNumber = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                        ProcedureName = reader.GetString(3),
                        UnitPrice = reader.GetDecimal(4),
                        Quantity = reader.GetInt32(5),
                        LineTotal = reader.GetDecimal(6),
                        SourceStage = reader.IsDBNull(7) ? null : Enum.Parse<TreatmentStage>(reader.GetString(7))
                    });
                }
            }
        }

        const string paySql = """
            SELECT invoice_id, method, amount, reference_no, panel_name
            FROM public.payments
            WHERE invoice_id = ANY(@ids)
            ORDER BY paid_at ASC;
            """;

        await using (var pCmd = new NpgsqlCommand(paySql, conn, tx))
        {
            pCmd.Parameters.AddWithValue("ids", ids);
            await using var reader = await pCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var invId = reader.GetGuid(0);
                if (map.TryGetValue(invId, out var inv))
                {
                    inv.Payments.Add(new PaymentSplitDto
                    {
                        Method = Enum.Parse<PaymentMethod>(reader.GetString(1)),
                        Amount = reader.GetDecimal(2),
                        ReferenceNo = reader.IsDBNull(3) ? null : reader.GetString(3),
                        PanelName = reader.IsDBNull(4) ? null : reader.GetString(4)
                    });
                }
            }
        }

        return list;
    }

    private async Task<List<WhatsAppThreadDto>> QueryWhatsAppThreadsInternalAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string? branchId,
        string? threadId,
        CancellationToken ct)
    {
        const string threadSql = """
            SELECT thread_id, patient_phone, patient_name, is_emergency_escalated, emergency_reason, last_updated_at
            FROM public.whatsapp_threads
            WHERE (@branchId IS NULL OR branch_id = @branchId)
              AND (@threadId IS NULL OR thread_id = @threadId)
            ORDER BY last_updated_at DESC;
            """;

        var threads = new List<WhatsAppThreadDto>();
        var map = new Dictionary<string, WhatsAppThreadDto>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new NpgsqlCommand(threadSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("branchId",
                string.IsNullOrWhiteSpace(branchId) ? DBNull.Value : ClinicDomainHelpers.NormalizeBranch(branchId, _clinicOptions.DefaultBranchId));
            cmd.Parameters.AddWithValue("threadId", string.IsNullOrWhiteSpace(threadId) ? DBNull.Value : threadId.Trim());

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var dto = new WhatsAppThreadDto
                {
                    ThreadId = reader.GetString(0),
                    PatientPhone = reader.GetString(1),
                    PatientName = reader.GetString(2),
                    IsEmergencyEscalated = reader.GetBoolean(3),
                    EmergencyReason = reader.IsDBNull(4) ? null : reader.GetString(4),
                    LastUpdatedAt = reader.GetFieldValue<DateTimeOffset>(5)
                };
                threads.Add(dto);
                map[dto.ThreadId] = dto;
            }
        }

        if (threads.Count == 0)
        {
            return threads;
        }

        const string msgSql = """
            SELECT id, thread_id, sender, text, timestamp, created_booking_id
            FROM public.whatsapp_messages
            WHERE thread_id = ANY(@threadIds)
            ORDER BY timestamp ASC;
            """;

        await using (var mCmd = new NpgsqlCommand(msgSql, conn, tx))
        {
            mCmd.Parameters.AddWithValue("threadIds", threads.Select(t => t.ThreadId).ToArray());
            await using var reader = await mCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var tid = reader.GetString(1);
                if (map.TryGetValue(tid, out var th))
                {
                    th.Messages.Add(new WhatsAppMessageDto
                    {
                        Id = reader.GetGuid(0),
                        Sender = reader.GetString(2),
                        Text = reader.GetString(3),
                        Timestamp = reader.GetFieldValue<DateTimeOffset>(4),
                        CreatedBookingId = reader.IsDBNull(5) ? null : reader.GetGuid(5)
                    });
                }
            }
        }

        return threads;
    }

    private async Task InsertAuditLogInternalAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string branchId,
        string action,
        string entityName,
        string entityId,
        string? oldValue,
        string? newValue,
        CancellationToken ct,
        string? actorOverride = null)
    {
        const string sql = """
            INSERT INTO public.audit_logs
                (id, branch_id, user_id, timestamp, action, entity_name, entity_id, old_value, new_value)
            VALUES
                (@id, @branchId, @userId, NOW(), @action, @entityName, @entityId, @oldValue, @newValue);
            """;

        var actor = !string.IsNullOrWhiteSpace(actorOverride)
            ? actorOverride.Trim()
            : (string.IsNullOrWhiteSpace(_claimsContext.UserId) ? "system" : _claimsContext.UserId);

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("branchId", branchId);
        cmd.Parameters.AddWithValue("userId", actor);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("entityName", entityName);
        cmd.Parameters.AddWithValue("entityId", entityId);
        cmd.Parameters.AddWithValue("oldValue", (object?)oldValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("newValue", (object?)newValue ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static PatientDto ReadPatient(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        FullName = reader.GetString(1),
        IcNumber = reader.GetString(2),
        DateOfBirth = reader.GetFieldValue<DateOnly>(3),
        Gender = reader.GetString(4),
        Phone = reader.GetString(5),
        Address = reader.GetString(6),
        PrimaryBranchId = reader.GetString(7),
        IsMyKadVerified = reader.GetBoolean(8),
        ConsentSignedAt = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
        MedicalAlerts = new MedicalAlertDto
        {
            Allergies = reader.GetString(10),
            OnAnticoagulants = reader.GetBoolean(11),
            HasDiabetes = reader.GetBoolean(12),
            IsPregnant = reader.GetBoolean(13),
            Notes = reader.GetString(14)
        }
    };

    private static AppointmentQueueItemDto ReadAppointment(NpgsqlDataReader reader)
    {
        var rawStatus = Enum.Parse<QueueStatus>(reader.GetString(13));
        var normalizedStatus = AppointmentCalendarHelper.NormalizeQueueStatus(rawStatus);
        var visitType = reader.IsDBNull(16) ? VisitType.Booked : Enum.Parse<VisitType>(reader.GetString(16));
        var activityJson = reader.IsDBNull(28) ? "[]" : reader.GetString(28);
        List<VisitActivityEntryDto> activity;
        try
        {
            activity = JsonSerializer.Deserialize<List<VisitActivityEntryDto>>(activityJson, ActivityJsonOptions) ?? new();
        }
        catch (JsonException)
        {
            activity = new();
        }

        return new AppointmentQueueItemDto
        {
            Id = reader.GetGuid(0),
            BranchId = reader.GetString(1),
            PatientId = reader.GetGuid(2),
            PatientName = reader.GetString(3),
            PatientIc = reader.GetString(4),
            DentistId = reader.IsDBNull(5) ? null : reader.GetGuid(5),
            DoctorName = reader.GetString(6),
            TreatmentId = reader.IsDBNull(7) ? null : reader.GetGuid(7),
            RoomName = reader.GetString(8),
            ScheduledAt = reader.GetFieldValue<DateTimeOffset>(9),
            DurationMinutes = reader.GetInt32(10),
            ProcedureSummary = reader.GetString(11),
            Notes = reader.GetString(12),
            Status = normalizedStatus,
            HasCriticalMedicalAlert = reader.GetBoolean(14),
            AlertSummary = reader.GetString(15),
            VisitType = visitType,
            ArrivedAt = reader.IsDBNull(17) ? null : reader.GetFieldValue<DateTimeOffset>(17),
            SeatedAt = reader.IsDBNull(18) ? null : reader.GetFieldValue<DateTimeOffset>(18),
            CheckoutAt = reader.IsDBNull(19) ? null : reader.GetFieldValue<DateTimeOffset>(19),
            CompletedAt = reader.IsDBNull(20) ? null : reader.GetFieldValue<DateTimeOffset>(20),
            CreatedBy = reader.IsDBNull(21) ? string.Empty : reader.GetString(21),
            CheckedInBy = reader.IsDBNull(22) ? string.Empty : reader.GetString(22),
            SeatedBy = reader.IsDBNull(23) ? string.Empty : reader.GetString(23),
            CheckoutBy = reader.IsDBNull(24) ? string.Empty : reader.GetString(24),
            CompletedBy = reader.IsDBNull(25) ? string.Empty : reader.GetString(25),
            LastUpdatedBy = reader.IsDBNull(26) ? string.Empty : reader.GetString(26),
            LastUpdatedAt = reader.IsDBNull(27) ? null : reader.GetFieldValue<DateTimeOffset>(27),
            ActivityLog = activity
        };
    }
}
