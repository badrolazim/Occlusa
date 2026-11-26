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
    public decimal CreditBalance { get; set; }
    public int VisitCount { get; set; }
    public DateTimeOffset? LastVisitAt { get; set; }
}

/// <summary>
/// Append-only ledger entry tracking patient credit deposits, redemptions, and void reversals across branches.
/// </summary>
public class PatientCreditEntryDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public string BranchId { get; set; } = "HQ-KL";
    public Guid? InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal AmountDelta { get; set; }
    public decimal BalanceAfter { get; set; }
    public string EntryType { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string RecordedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
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
    public Guid? VisitId { get; set; }
    public ReminderStatus ReminderStatus { get; set; } = ReminderStatus.NotReminded;
    public ReminderChannel ReminderChannel { get; set; } = ReminderChannel.None;
    public DateTimeOffset? RemindedAt { get; set; }
    public string RemindedBy { get; set; } = string.Empty;
    public DateTimeOffset? ReminderConfirmedAt { get; set; }

    [JsonIgnore]
    public DateTimeOffset EndsAt => ScheduledAt.AddMinutes(Math.Max(15, DurationMinutes));
}

/// <summary>
/// Single tooth state in a visit's FDI (11-48 permanent / 51-85 primary) odontogram.
/// </summary>
public class OdontogramToothDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? VisitId { get; set; }
    public Guid PatientId { get; set; }
    public int ToothNumber { get; set; }
    public ToothCondition Condition { get; set; } = ToothCondition.Healthy;
    public string Surfaces { get; set; } = string.Empty;
    public string ProcedureName { get; set; } = string.Empty;
    public decimal Fee { get; set; }
    public TreatmentStage Stage { get; set; } = TreatmentStage.Proposed;
    public string DoctorName { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public bool IsBaselineOnly =>
        Condition != ToothCondition.Healthy && string.IsNullOrWhiteSpace(ProcedureName);
}

/// <summary>
/// First-class clinical visit record (0..N per patient) owning its own SOAP narrative
/// and independent 52-tooth FDI odontogram snapshot per MDA/MDC and KKM requirements.
/// </summary>
public class PatientVisitDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PatientIc { get; set; } = string.Empty;
    public Guid? AppointmentId { get; set; }
    public Guid? InvoiceId { get; set; }
    public string BranchId { get; set; } = "HQ-KL";
    public Guid? DentistId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public int VisitNumber { get; set; } = 1;
    public DateTimeOffset VisitDate { get; set; } = DateTimeOffset.UtcNow;
    public string Subjective { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string Assessment { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public string AnesthesiaUsed { get; set; } = string.Empty;
    public string ToothShade { get; set; } = string.Empty;
    public string PostOpInstructions { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public string LastUpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<OdontogramToothDto> Odontogram { get; set; } = new();

    [JsonIgnore]
    public int ChartedTeethCount =>
        Odontogram.Count(t => t.Condition != ToothCondition.Healthy || !string.IsNullOrWhiteSpace(t.ProcedureName));

    [JsonIgnore]
    public int CompletedProceduresCount =>
        Odontogram.Count(t => t.Stage == TreatmentStage.Completed && !string.IsNullOrWhiteSpace(t.ProcedureName));

    [JsonIgnore]
    public decimal CompletedTotalFee =>
        Odontogram.Where(t => t.Stage == TreatmentStage.Completed && !string.IsNullOrWhiteSpace(t.ProcedureName)).Sum(t => t.Fee);
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
/// Reusable pure-clinical SOAP template macro (strictly free of monetary fields).
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
}

/// <summary>
/// Individual billed line item on a patient or walk-in retail invoice.
/// </summary>
public class InvoiceLineDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? CatalogItemId { get; set; }
    public int? ToothNumber { get; set; }
    public string ProcedureName { get; set; } = string.Empty;
    public BillingCatalogCategory Category { get; set; } = BillingCatalogCategory.Procedure;
    public bool IsCommissionable { get; set; } = true;
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
/// Branch-isolated billing invoice supporting panel claims, split payments, patient credit, OTC retail, and voiding.
/// </summary>
public class InvoiceDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string InvoiceNumber { get; set; } = string.Empty;
    public string BranchId { get; set; } = "HQ-KL";
    public Guid PatientId { get; set; }
    public bool IsWalkInRetail { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public Guid? AppointmentId { get; set; }
    public Guid? VisitId { get; set; }
    public List<InvoiceLineDto> Lines { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal PanelCoveredAmount { get; set; }
    public string? PanelName { get; set; }
    public decimal PatientPayableAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal CreditedOverpaymentAmount { get; set; }
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
    public decimal TotalCreditRedeemed { get; set; }
    public decimal TotalCreditDeposited { get; set; }
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
    public BillingSettingsSnapshotDto BillingSettings { get; set; } = new();
    public List<UserWorkspacePreferencesDto> UserPreferences { get; set; } = new();
}

/// <summary>
/// Per-staff Live Queue and workspace view preferences persisted across clinic workstations.
/// </summary>
public class UserWorkspacePreferencesDto
{
    public string StaffKey { get; set; } = string.Empty;
    public string StaffDisplayName { get; set; } = string.Empty;
    public LiveQueueViewMode QueueViewMode { get; set; } = LiveQueueViewMode.Simple;
    public LiveQueueScopeMode DefaultQueueScope { get; set; } = LiveQueueScopeMode.AllClinic;
    public bool HideFinishedQueueRows { get; set; } = true;
    public bool ShowWaitTimers { get; set; }
    public bool ShowInlineReceptionNotes { get; set; }
    public bool ShowMyKadQuickBar { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Single unified Master Catalogue item powering Appointment Booking (when IsBookableAppointment == true),
/// Visit Odontogram Quick Presets (when ShowInOdontogramPresets == true), Doctor Suggestion Rules,
/// Invoice Checkout, Walk-In OTC Retail Sales, and WhatsApp AI routine fee quotes.
/// </summary>
public class BillingCatalogItemDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public BillingCatalogCategory Category { get; set; } = BillingCatalogCategory.Procedure;
    public string ClinicalSubcategory { get; set; } = "General";
    public decimal UnitPrice { get; set; }
    public bool IsCommissionable { get; set; } = true;
    public bool IsBookableAppointment { get; set; }
    public int DefaultDurationMinutes { get; set; } = 30;
    public bool ShowInOdontogramPresets { get; set; }
    public ToothCondition DefaultToothCondition { get; set; } = ToothCondition.Healthy;
    public string NoteKeywords { get; set; } = string.Empty;
    public string SafetyAlertTags { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public TreatmentDto ToBookableTreatmentDto() => new()
    {
        Id = Id,
        Name = Name,
        Category = string.IsNullOrWhiteSpace(ClinicalSubcategory) ? Category.ToString() : ClinicalSubcategory,
        DefaultDurationMinutes = Math.Clamp(DefaultDurationMinutes > 0 ? DefaultDurationMinutes : 30, 15, 480),
        DefaultFee = UnitPrice,
        IsActive = IsActive
    };
}

/// <summary>
/// Maps a primary Master Catalogue procedure (TreatmentId == BillingCatalogItemDto.Id)
/// to companion Master Catalogue items to suggest on draft invoices.
/// </summary>
public class TreatmentSuggestionRuleDto
{
    public Guid TreatmentId { get; set; }
    public string TreatmentName { get; set; } = string.Empty;
    public List<Guid> SuggestedCatalogItemIds { get; set; } = new();
}

/// <summary>
/// Clinic-default (DentistId == null) or dentist-specific checkout invoice behavior and treatment suggestion overrides.
/// </summary>
public class DoctorInvoicePreferenceDto
{
    public Guid? DentistId { get; set; }
    public string DoctorName { get; set; } = "Clinic Default (All Doctors)";
    public DoctorInvoiceInitMode InitMode { get; set; } = DoctorInvoiceInitMode.AutoPopulateWithSuggestions;
    public bool EnableNoteKeywordEngine { get; set; } = true;
    public List<TreatmentSuggestionRuleDto> TreatmentRules { get; set; } = new();
}

/// <summary>
/// Unified Master Catalogue, Corporate Panels, and Doctor Invoice Preferences synchronized to workstations.
/// </summary>
public class BillingSettingsSnapshotDto
{
    public List<BillingCatalogItemDto> CatalogItems { get; set; } = new();
    public List<DoctorInvoicePreferenceDto> DoctorPreferences { get; set; } = new();
    public List<string> CorporatePanels { get; set; } =
    [
        "AIA Dental Panel",
        "PMCare",
        "MiCare",
        "Mednefits",
        "CompuMed",
        "Allianz Corporate"
    ];
}

/// <summary>
/// Single suggested invoice item produced by <see cref="Services.InvoiceSuggestionEngine"/>.
/// </summary>
public class InvoiceSuggestionItemDto
{
    public Guid CatalogItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public BillingCatalogCategory Category { get; set; } = BillingCatalogCategory.Procedure;
    public decimal UnitPrice { get; set; }
    public bool IsCommissionable { get; set; }
    public int? ToothNumber { get; set; }
    public string SourceReason { get; set; } = string.Empty;
    public string SafetyWarning { get; set; } = string.Empty;
    public bool HasSafetyWarning => !string.IsNullOrWhiteSpace(SafetyWarning);
}

/// <summary>
/// Output of evaluating a patient visit, appointment, SOAP notes, and doctor settings for draft invoice initiation.
/// </summary>
public class VisitInvoiceSuggestionResultDto
{
    public DoctorInvoiceInitMode AppliedMode { get; set; } = DoctorInvoiceInitMode.AutoPopulateWithSuggestions;
    public string PreferenceSourceLabel { get; set; } = "Clinic Default";
    public List<InvoiceLineDto> AutoPopulatedLines { get; set; } = new();
    public List<InvoiceSuggestionItemDto> SuggestedAddOns { get; set; } = new();
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
    public string Surfaces { get; set; } = string.Empty;
    public string ProcedureName { get; set; } = string.Empty;
    public decimal Fee { get; set; }
    public TreatmentStage Stage { get; set; } = TreatmentStage.Proposed;
    public string DoctorName { get; set; } = string.Empty;
    public string BranchId { get; set; } = "HQ-KL";
    public string? UpdatedBy { get; set; }
}

public class CreatePatientVisitRequest
{
    public string BranchId { get; set; } = "HQ-KL";
    public Guid? AppointmentId { get; set; }
    public Guid? DentistId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public DateTimeOffset? VisitDate { get; set; }
    public OdontogramInitMode InitMode { get; set; } = OdontogramInitMode.CarryForward;
    public string Subjective { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string Assessment { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public string AnesthesiaUsed { get; set; } = string.Empty;
    public string ToothShade { get; set; } = string.Empty;
    public string PostOpInstructions { get; set; } = string.Empty;
    public string? CreatedBy { get; set; }
}

public class UpdatePatientVisitRequest
{
    public string BranchId { get; set; } = "HQ-KL";
    public Guid? DentistId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public DateTimeOffset? VisitDate { get; set; }
    public string Subjective { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string Assessment { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public string AnesthesiaUsed { get; set; } = string.Empty;
    public string ToothShade { get; set; } = string.Empty;
    public string PostOpInstructions { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
}

public class ResetVisitOdontogramRequest
{
    public OdontogramInitMode Mode { get; set; } = OdontogramInitMode.Blank;
    public string? UpdatedBy { get; set; }
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
    public OverpaymentHandling OverpaymentHandling { get; set; } = OverpaymentHandling.None;
    public decimal? AvailablePatientCredit { get; set; }
    public decimal DoctorCommissionRate { get; set; } = 0.40m;
    public string? CollectedBy { get; set; }
}

public class SaveInvoiceDraftRequest
{
    public Guid? InvoiceId { get; set; }
    public string BranchId { get; set; } = "HQ-KL";
    public Guid? PatientId { get; set; }
    public bool IsWalkInRetail { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public Guid? AppointmentId { get; set; }
    public Guid? VisitId { get; set; }
    public List<InvoiceLineDto> Lines { get; set; } = new();
    public decimal DiscountAmount { get; set; }
    public string? PanelName { get; set; }
    public decimal PanelCoveredAmount { get; set; }
    public bool SendToCounterCheckout { get; set; }
    public string? UpdatedBy { get; set; }
}

public class UpsertBillingCatalogItemRequest
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public BillingCatalogCategory Category { get; set; } = BillingCatalogCategory.Procedure;
    public string ClinicalSubcategory { get; set; } = "General";
    public decimal UnitPrice { get; set; }
    public bool IsCommissionable { get; set; } = true;
    public bool IsBookableAppointment { get; set; }
    public int DefaultDurationMinutes { get; set; } = 30;
    public bool ShowInOdontogramPresets { get; set; }
    public ToothCondition DefaultToothCondition { get; set; } = ToothCondition.Healthy;
    public string NoteKeywords { get; set; } = string.Empty;
    public string SafetyAlertTags { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? UpdatedBy { get; set; }
}

public class SaveDoctorInvoicePreferenceRequest
{
    public Guid? DentistId { get; set; }
    public string DoctorName { get; set; } = "Clinic Default (All Doctors)";
    public DoctorInvoiceInitMode InitMode { get; set; } = DoctorInvoiceInitMode.AutoPopulateWithSuggestions;
    public bool EnableNoteKeywordEngine { get; set; } = true;
    public List<TreatmentSuggestionRuleDto> TreatmentRules { get; set; } = new();
    public string? UpdatedBy { get; set; }
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

public class UpdateAppointmentReminderRequest
{
    public ReminderStatus Status { get; set; } = ReminderStatus.Reminded;
    public ReminderChannel Channel { get; set; } = ReminderChannel.WhatsApp;
    public bool SendWhatsAppMessage { get; set; }
    public string? CustomMessageText { get; set; }
    public string? UpdatedBy { get; set; }
}

public class UpdateAppointmentReminderResponse
{
    public AppointmentQueueItemDto Appointment { get; set; } = new();
    public WhatsAppThreadDto? WhatsAppThread { get; set; }
    public bool WhatsAppMessageSent { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class SaveUserWorkspacePreferencesRequest
{
    public string StaffKey { get; set; } = string.Empty;
    public string StaffDisplayName { get; set; } = string.Empty;
    public LiveQueueViewMode QueueViewMode { get; set; } = LiveQueueViewMode.Simple;
    public LiveQueueScopeMode DefaultQueueScope { get; set; } = LiveQueueScopeMode.AllClinic;
    public bool HideFinishedQueueRows { get; set; } = true;
    public bool ShowWaitTimers { get; set; }
    public bool ShowInlineReceptionNotes { get; set; }
    public bool ShowMyKadQuickBar { get; set; }
}
