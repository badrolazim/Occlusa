using System.Text.Json.Serialization;

namespace Occlusa.Shared.Models;

/// <summary>
/// Practicing clinician in the clinic group catalog.
/// </summary>
public class DentistDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string DefaultRoom { get; set; } = string.Empty;
    public List<string> BranchIds { get; set; } = new();
    public List<Guid> SupportedTreatmentIds { get; set; } = new();
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Standardized clinical treatment catalog entry with default appointment duration and RM fee.
/// </summary>
public class TreatmentDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int DefaultDurationMinutes { get; set; } = 30;
    public decimal DefaultFee { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Critical medical alerts pinned across patient views for clinical safety.
/// </summary>
public class MedicalAlertDto
{
    public string Allergies { get; set; } = string.Empty;
    public bool OnAnticoagulants { get; set; }
    public bool HasDiabetes { get; set; }
    public bool IsPregnant { get; set; }
    public string Notes { get; set; } = string.Empty;

    public bool HasAnyCriticalAlert =>
        HasMeaningfulText(Allergies) || OnAnticoagulants || HasDiabetes || IsPregnant;

    /// <summary>
    /// Formats a concise clinical alert string for queue badges and banners.
    /// </summary>
    public string BuildAlertSummary()
    {
        var flags = new List<string>();
        if (HasMeaningfulText(Allergies))
        {
            flags.Add($"Allergy: {Allergies.Trim()}");
        }
        if (OnAnticoagulants)
        {
            flags.Add("Anticoagulants");
        }
        if (HasDiabetes)
        {
            flags.Add("Diabetes");
        }
        if (IsPregnant)
        {
            flags.Add("Pregnant");
        }

        return flags.Count > 0 ? string.Join(" | ", flags) : "No Critical Alerts";
    }

    private static bool HasMeaningfulText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return !trimmed.Equals("None", StringComparison.OrdinalIgnoreCase)
            && !trimmed.Equals("Nil", StringComparison.OrdinalIgnoreCase)
            && !trimmed.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            && trimmed != "-";
    }
}

/// <summary>
/// Core Malaysian dental patient profile shared across branches within a clinic group.
/// </summary>
public class PatientDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string IcNumber { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PrimaryBranchId { get; set; } = "HQ-KL";
    public MedicalAlertDto MedicalAlerts { get; set; } = new();
    public bool IsMyKadVerified { get; set; }
    public DateTimeOffset? ConsentSignedAt { get; set; }
}

/// <summary>
/// Timestamped staff attribution milestone recorded on a visit's activity trail.
/// </summary>
public class VisitActivityEntryDto
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = string.Empty;
    public string StaffName { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

/// <summary>
/// Live appointment and queue item displayed on the clinic floor board and Appointments calendar.
/// </summary>
public class AppointmentQueueItemDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BranchId { get; set; } = "HQ-KL";
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientIc { get; set; } = string.Empty;
    public Guid? DentistId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public Guid? TreatmentId { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; set; } = DateTimeOffset.UtcNow;
    public int DurationMinutes { get; set; } = 30;
    public string ProcedureSummary { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public QueueStatus Status { get; set; } = QueueStatus.Scheduled;
    public VisitType VisitType { get; set; } = VisitType.Booked;
    public DateTimeOffset? ArrivedAt { get; set; }
    public DateTimeOffset? SeatedAt { get; set; }
    public DateTimeOffset? CheckoutAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string CheckedInBy { get; set; } = string.Empty;
    public string SeatedBy { get; set; } = string.Empty;
    public string CheckoutBy { get; set; } = string.Empty;
    public string CompletedBy { get; set; } = string.Empty;
    public string LastUpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset? LastUpdatedAt { get; set; }
    public List<VisitActivityEntryDto> ActivityLog { get; set; } = new();
    public bool HasCriticalMedicalAlert { get; set; }
    public string AlertSummary { get; set; } = string.Empty;

    [JsonIgnore]
    public DateTimeOffset EndsAt => ScheduledAt.AddMinutes(Math.Max(15, DurationMinutes));
}

/// <summary>
/// Single tooth state in a patient's FDI (11-48) odontogram.
/// </summary>
public class OdontogramToothDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public int ToothNumber { get; set; }
    public ToothCondition Condition { get; set; } = ToothCondition.Healthy;
    public string ProcedureName { get; set; } = string.Empty;
    public decimal Fee { get; set; }
    public TreatmentStage Stage { get; set; } = TreatmentStage.Proposed;
    public string DoctorName { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Structured clinical SOAP note for a dental encounter.
/// </summary>
public class SoapNoteDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public string BranchId { get; set; } = "HQ-KL";
    public string DoctorName { get; set; } = string.Empty;
    public DateTimeOffset VisitDate { get; set; } = DateTimeOffset.UtcNow;
    public string Subjective { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string Assessment { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public string AnesthesiaUsed { get; set; } = string.Empty;
    public string ToothShade { get; set; } = string.Empty;
    public string PostOpInstructions { get; set; } = string.Empty;
}

/// <summary>
/// Reusable clinical SOAP template macro with default procedure fee.
/// </summary>
public class SoapMacroDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string SubjectiveTemplate { get; set; } = string.Empty;
    public string ObjectiveTemplate { get; set; } = string.Empty;
    public string AssessmentTemplate { get; set; } = string.Empty;
    public string PlanTemplate { get; set; } = string.Empty;
    public decimal DefaultFee { get; set; }
}

/// <summary>
/// Individual billed line item on a patient invoice.
/// </summary>
public class InvoiceLineDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int? ToothNumber { get; set; }
    public string ProcedureName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal LineTotal { get; set; }
    public TreatmentStage? SourceStage { get; set; }
}

/// <summary>
/// Single payment split entry within a multi-method settlement.
/// </summary>
public class PaymentSplitDto
{
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? ReferenceNo { get; set; }
    public string? PanelName { get; set; }
}

/// <summary>
/// Branch-isolated billing invoice supporting panel claims, split payments, and voiding.
/// </summary>
public class InvoiceDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string InvoiceNumber { get; set; } = string.Empty;
    public string BranchId { get; set; } = "HQ-KL";
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public Guid? AppointmentId { get; set; }
    public List<InvoiceLineDto> Lines { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal PanelCoveredAmount { get; set; }
    public string? PanelName { get; set; }
    public decimal PatientPayableAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal DoctorCommissionAmount { get; set; }
    public List<PaymentSplitDto> Payments { get; set; } = new();
    public bool IsVoided { get; set; }
    public string? VoidReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public decimal BalanceDue => IsVoided ? 0m : Math.Max(0m, PatientPayableAmount - PaidAmount);
}

/// <summary>
/// End-of-day branch financial closing summary.
/// </summary>
public class DailySalesReconciliationDto
{
    public string BranchId { get; set; } = "HQ-KL";
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public int TotalInvoicesCount { get; set; }
    public int VoidedInvoicesCount { get; set; }
    public decimal GrossRevenue { get; set; }
    public decimal TotalDiscounts { get; set; }
    public decimal TotalPanelClaims { get; set; }
    public decimal TotalCashCollected { get; set; }
    public decimal TotalCardCollected { get; set; }
    public decimal TotalQrCollected { get; set; }
    public decimal TotalDoctorCommissions { get; set; }
    public decimal NetCollected { get; set; }
    public bool IsBalanced { get; set; }
    public string ReconciledBy { get; set; } = string.Empty;
    public DateTimeOffset ReconciledAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Append-only audit trail entry for financial and clinical mutations.
/// </summary>
public class AuditLogDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BranchId { get; set; } = "HQ-KL";
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}

/// <summary>
/// Result returned from parsing or reading a Malaysian MyKad smartcard.
/// </summary>
public class MyKadReadResultDto
{
    public bool Success { get; set; }
    public string ReaderName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string IcNumber { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Citizenship { get; set; } = string.Empty;
    public bool IsSimulated { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// WhatsApp AI Receptionist conversation thread and escalation status.
/// </summary>
public class WhatsAppThreadDto
{
    public string ThreadId { get; set; } = string.Empty;
    public string PatientPhone { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public bool IsEmergencyEscalated { get; set; }
    public string? EmergencyReason { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<WhatsAppMessageDto> Messages { get; set; } = new();
}

/// <summary>
/// Individual message in a WhatsApp thread.
/// </summary>
public class WhatsAppMessageDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Sender { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedBookingId { get; set; }
}

/// <summary>
/// Automated backup checksum and restore verification record.
/// </summary>
public class BackupVerificationDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset VerifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public string FileName { get; set; } = string.Empty;
    public string Sha256Checksum { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public bool IsEncrypted { get; set; }
    public bool RestoreTestPassed { get; set; }
    public string VerifiedBy { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

/// <summary>
/// Combined branch dashboard payload used for initial load and 5s polling fallback.
/// </summary>
public class ClinicDashboardSnapshotDto
{
    public string BranchId { get; set; } = "HQ-KL";
    public string DatabaseMode { get; set; } = "LocalSeededFallback";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<AppointmentQueueItemDto> Queue { get; set; } = new();
    public List<PatientDto> Patients { get; set; } = new();
    public List<DentistDto> Dentists { get; set; } = new();
    public List<TreatmentDto> Treatments { get; set; } = new();
    public List<InvoiceDto> Invoices { get; set; } = new();
    public List<SoapMacroDto> SoapMacros { get; set; } = new();
    public List<WhatsAppThreadDto> WhatsAppThreads { get; set; } = new();
    public DailySalesReconciliationDto DailySummary { get; set; } = new();
}

/// <summary>
/// API health and storage mode status payload.
/// </summary>
public class HealthStatusDto
{
    public string Status { get; set; } = "Healthy";
    public string DatabaseMode { get; set; } = "LocalSeededFallback";
    public string DefaultBranchId { get; set; } = "HQ-KL";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public class UpsertPatientRequest
{
    public Guid? Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string IcNumber { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string PrimaryBranchId { get; set; } = "HQ-KL";
    public MedicalAlertDto? MedicalAlerts { get; set; }
    public bool IsMyKadVerified { get; set; }
    public DateTimeOffset? ConsentSignedAt { get; set; }
}

public class SignConsentRequest
{
    public DateTimeOffset? SignedAt { get; set; }
    public string? SignedBy { get; set; }
}

public class CreateAppointmentRequest
{
    public string BranchId { get; set; } = "HQ-KL";
    public Guid PatientId { get; set; }
    public Guid? DentistId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public Guid? TreatmentId { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; set; } = DateTimeOffset.UtcNow;
    public int DurationMinutes { get; set; } = 30;
    public string ProcedureSummary { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public QueueStatus Status { get; set; } = QueueStatus.Scheduled;
    public VisitType VisitType { get; set; } = VisitType.Booked;
    public DateTimeOffset? ArrivedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public class UpdateAppointmentRequest
{
    public Guid PatientId { get; set; }
    public Guid? DentistId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public Guid? TreatmentId { get; set; }
    public string ProcedureSummary { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public QueueStatus Status { get; set; } = QueueStatus.Scheduled;
    public VisitType? VisitType { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
}

public class RegisterPatientAndAppointmentRequest
{
    public UpsertPatientRequest Patient { get; set; } = new();
    public CreateAppointmentRequest Appointment { get; set; } = new();
}

public class RegisterPatientAndAppointmentResponse
{
    public PatientDto Patient { get; set; } = new();
    public AppointmentQueueItemDto Appointment { get; set; } = new();
}

public class UpdateQueueStatusRequest
{
    public QueueStatus NewStatus { get; set; }
    public string? RoomName { get; set; }
    public string? UpdatedBy { get; set; }
}

public class UpdateOdontogramRequest
{
    public int ToothNumber { get; set; }
    public ToothCondition Condition { get; set; } = ToothCondition.Healthy;
    public string ProcedureName { get; set; } = string.Empty;
    public decimal Fee { get; set; }
    public TreatmentStage Stage { get; set; } = TreatmentStage.Proposed;
    public string DoctorName { get; set; } = string.Empty;
    public string BranchId { get; set; } = "HQ-KL";
}

public class CreateSoapNoteRequest
{
    public string BranchId { get; set; } = "HQ-KL";
    public string DoctorName { get; set; } = string.Empty;
    public DateTimeOffset? VisitDate { get; set; }
    public string Subjective { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string Assessment { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public string AnesthesiaUsed { get; set; } = string.Empty;
    public string ToothShade { get; set; } = string.Empty;
    public string PostOpInstructions { get; set; } = string.Empty;
}

public class ApplyPaymentRequest
{
    public decimal DiscountAmount { get; set; }
    public string? PanelName { get; set; }
    public decimal PanelCoveredAmount { get; set; }
    public List<PaymentSplitDto> Payments { get; set; } = new();
    public decimal DoctorCommissionRate { get; set; } = 0.40m;
    public string? CollectedBy { get; set; }
}

public class VoidInvoiceRequest
{
    public string VoidReason { get; set; } = string.Empty;
    public string VoidedBy { get; set; } = string.Empty;
}

public class ReconcileDailySalesRequest
{
    public string BranchId { get; set; } = "HQ-KL";
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public string ReconciledBy { get; set; } = string.Empty;
}

public class WhatsAppSimulateRequest
{
    public string? ThreadId { get; set; }
    public string PatientPhone { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string MessageText { get; set; } = string.Empty;
    public string BranchId { get; set; } = "HQ-KL";
}

public class WhatsAppTakeoverReplyRequest
{
    public string MessageText { get; set; } = string.Empty;
    public string StaffName { get; set; } = string.Empty;
}
