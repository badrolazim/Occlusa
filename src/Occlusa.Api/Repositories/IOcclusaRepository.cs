using Occlusa.Shared.Models;

namespace Occlusa.Api.Repositories;

/// <summary>
/// Unified persistence contract implemented by InMemoryOcclusaRepository (zero-config fallback)
/// and NpgsqlSupabaseRepository (PostgreSQL + RLS claims enforcement).
/// </summary>
public interface IOcclusaRepository
{
    string DatabaseMode { get; }

    Task<ClinicDashboardSnapshotDto> GetDashboardSnapshotAsync(string branchId, CancellationToken ct = default);

    Task<IReadOnlyList<PatientDto>> GetPatientsAsync(string? search = null, CancellationToken ct = default);
    Task<PatientDto?> GetPatientByIdAsync(Guid patientId, CancellationToken ct = default);
    Task<PatientDto?> FindPatientByPhoneOrNameAsync(string? phone, string? name, CancellationToken ct = default);
    Task<PatientDto> UpsertPatientAsync(UpsertPatientRequest request, bool forceMyKadVerified = false, CancellationToken ct = default);
    Task<PatientDto?> UpdateMedicalAlertsAsync(Guid patientId, MedicalAlertDto alerts, string branchId, CancellationToken ct = default);
    Task<PatientDto?> SignConsentAsync(Guid patientId, SignConsentRequest request, string branchId, CancellationToken ct = default);

    Task<IReadOnlyList<DentistDto>> GetDentistsAsync(string? branchId = null, CancellationToken ct = default);
    Task<IReadOnlyList<TreatmentDto>> GetTreatmentsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<AppointmentQueueItemDto>> GetAppointmentsAsync(string? branchId = null, CancellationToken ct = default);
    Task<IReadOnlyList<AppointmentQueueItemDto>> GetAppointmentsAsync(
        string? branchId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? dentistId,
        Guid? treatmentId,
        CancellationToken ct = default);
    Task<AppointmentQueueItemDto?> GetAppointmentByIdAsync(Guid appointmentId, CancellationToken ct = default);
    Task<AppointmentQueueItemDto> CreateAppointmentAsync(CreateAppointmentRequest request, CancellationToken ct = default);
    Task<(AppointmentQueueItemDto? Item, string? Error)> UpdateAppointmentAsync(Guid appointmentId, UpdateAppointmentRequest request, CancellationToken ct = default);
    Task<RegisterPatientAndAppointmentResponse> RegisterPatientAndCreateAppointmentAsync(
        RegisterPatientAndAppointmentRequest request,
        CancellationToken ct = default);
    Task<(AppointmentQueueItemDto? Item, string? Error)> UpdateQueueStatusAsync(Guid appointmentId, UpdateQueueStatusRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<PatientVisitDto>> GetPatientVisitsAsync(Guid patientId, CancellationToken ct = default);
    Task<PatientVisitDto?> GetVisitByIdAsync(Guid visitId, CancellationToken ct = default);
    Task<(PatientVisitDto? Visit, AppointmentQueueItemDto? UpdatedAppointment, string? Error)> CreatePatientVisitAsync(
        Guid patientId,
        CreatePatientVisitRequest request,
        CancellationToken ct = default);
    Task<(PatientVisitDto? Visit, AppointmentQueueItemDto? UpdatedAppointment, string? Error)> EnsureVisitForAppointmentAsync(
        Guid appointmentId,
        string? actor = null,
        CancellationToken ct = default);
    Task<(PatientVisitDto? Visit, string? Error)> UpdatePatientVisitAsync(
        Guid visitId,
        UpdatePatientVisitRequest request,
        CancellationToken ct = default);
    Task<IReadOnlyList<OdontogramToothDto>> GetVisitOdontogramAsync(Guid visitId, CancellationToken ct = default);
    Task<(OdontogramToothDto? Tooth, InvoiceDto? UpdatedInvoice, string? Error)> UpdateVisitOdontogramAsync(
        Guid visitId,
        UpdateOdontogramRequest request,
        CancellationToken ct = default);
    Task<(PatientVisitDto? Visit, string? Error)> ResetVisitOdontogramAsync(
        Guid visitId,
        ResetVisitOdontogramRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<OdontogramToothDto>> GetOdontogramAsync(Guid patientId, CancellationToken ct = default);
    Task<(OdontogramToothDto? Tooth, InvoiceDto? UpdatedInvoice, string? Error)> UpdateOdontogramAsync(Guid patientId, UpdateOdontogramRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<SoapNoteDto>> GetSoapNotesAsync(Guid patientId, CancellationToken ct = default);
    Task<SoapNoteDto?> CreateSoapNoteAsync(Guid patientId, CreateSoapNoteRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SoapMacroDto>> GetSoapMacrosAsync(CancellationToken ct = default);

    Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(string? branchId = null, CancellationToken ct = default);
    Task<InvoiceDto?> GetInvoiceByIdAsync(Guid invoiceId, CancellationToken ct = default);
    Task<(InvoiceDto? Invoice, AppointmentQueueItemDto? UpdatedQueueItem, string? Error)> ApplyInvoicePaymentAsync(Guid invoiceId, ApplyPaymentRequest request, CancellationToken ct = default);
    Task<(InvoiceDto? Invoice, string? Error)> VoidInvoiceAsync(Guid invoiceId, VoidInvoiceRequest request, CancellationToken ct = default);

    Task<DailySalesReconciliationDto> GetDailySalesSummaryAsync(string branchId, DateOnly date, CancellationToken ct = default);
    Task<DailySalesReconciliationDto> ReconcileDailySalesAsync(ReconcileDailySalesRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLogDto>> GetAuditLogsAsync(string? branchId = null, CancellationToken ct = default);
    Task<IReadOnlyList<BackupVerificationDto>> GetBackupVerificationsAsync(CancellationToken ct = default);
    Task<BackupVerificationDto> RecordBackupVerificationAsync(BackupVerificationDto verification, CancellationToken ct = default);

    Task<IReadOnlyList<WhatsAppThreadDto>> GetWhatsAppThreadsAsync(string? branchId = null, CancellationToken ct = default);
    Task<WhatsAppThreadDto?> GetWhatsAppThreadAsync(string threadId, CancellationToken ct = default);
    Task<WhatsAppThreadDto> AppendWhatsAppMessageAsync(
        string threadId,
        string branchId,
        string patientPhone,
        string patientName,
        WhatsAppMessageDto message,
        bool? setEmergencyEscalated = null,
        string? emergencyReason = null,
        CancellationToken ct = default);
    Task<(AppointmentQueueItemDto? Appointment, WhatsAppThreadDto? Thread, string? Error)> ApproveAiBookingAsync(
        Guid appointmentId,
        string approvedBy,
        CancellationToken ct = default);
}
