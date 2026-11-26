using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.ViewModels;

/// <summary>
/// Presentation row for the unified Live Queue UniformPagedGrid with separated appointment vs. arrival
/// times, live wait/chair timers, and status-pill-only color styling.
/// </summary>
public sealed partial class QueueRowItem : ObservableObject
{
    private static readonly StatusPillPalette WaitingPalette = CreatePalette("#FFFBEB", "#FDE68A", "#D97706", "#92400E");
    private static readonly StatusPillPalette LongWaitPalette = CreatePalette("#FEF2F2", "#FECACA", "#DC2626", "#991B1B");
    private static readonly StatusPillPalette InChairPalette = CreatePalette("#E6F4F1", "#99F6E4", "#0F766E", "#0F514B");
    private static readonly StatusPillPalette CheckoutPalette = CreatePalette("#EFF6FF", "#BFDBFE", "#2563EB", "#1E40AF");
    private static readonly StatusPillPalette AiPendingPalette = CreatePalette("#FEF3C7", "#FCD34D", "#B45309", "#78350F");
    private static readonly StatusPillPalette ScheduledPalette = CreatePalette("#F1F5F9", "#CBD5E1", "#64748B", "#334155");
    private static readonly StatusPillPalette CompletedPalette = CreatePalette("#F0FDF4", "#BBF7D0", "#16A34A", "#166534");
    private static readonly StatusPillPalette CancelledPalette = CreatePalette("#F8FAFC", "#E2E8F0", "#94A3B8", "#64748B");

    [ObservableProperty]
    private string _statusDisplay = string.Empty;

    [ObservableProperty]
    private Brush _statusBadgeBackground = ScheduledPalette.Background;

    [ObservableProperty]
    private Brush _statusBadgeBorder = ScheduledPalette.Border;

    [ObservableProperty]
    private Brush _statusDotBrush = ScheduledPalette.Dot;

    [ObservableProperty]
    private Brush _statusBadgeForeground = ScheduledPalette.Foreground;

    [ObservableProperty]
    private string _toolTipText = string.Empty;

    public QueueRowItem(AppointmentQueueItemDto source)
    {
        Source = source;
        IsWalkIn = source.VisitType == VisitType.WalkIn;
        IsNoAppointment = IsWalkIn;
        ApptTimeDisplay = IsWalkIn
            ? "No Appointment"
            : source.ScheduledAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

        if (IsWalkIn)
        {
            var arrived = (source.ArrivedAt ?? source.ScheduledAt).ToLocalTime();
            ArrivalTimeDisplay = $"{arrived:HH:mm} · Walk-In";
            HasArrivalTime = true;
        }
        else if (source.ArrivedAt.HasValue)
        {
            ArrivalTimeDisplay = source.ArrivedAt.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            HasArrivalTime = true;
        }
        else
        {
            ArrivalTimeDisplay = "—";
            HasArrivalTime = false;
        }

        TimeDisplay = IsWalkIn ? ArrivalTimeDisplay : ApptTimeDisplay;
        PatientNameDisplay = source.PatientName;
        ReceptionNoteDisplay = AppointmentCalendarHelper.FormatCompactReceptionNote(source.Notes);
        HasReceptionNote = !string.IsNullOrWhiteSpace(ReceptionNoteDisplay);
        HasCriticalAlert = source.HasCriticalMedicalAlert;
        PatientDisplay = HasCriticalAlert
            ? $"{source.PatientName} [ALERT]"
            : source.PatientName;
        DoctorRoomDisplay = string.IsNullOrWhiteSpace(source.RoomName)
            ? source.DoctorName
            : $"{source.DoctorName} · {source.RoomName}";
        IsFinished = AppointmentCalendarHelper.IsFinishedQueueStatus(source.Status);
        RowContentOpacity = IsFinished ? 0.58 : 1.0;

        RefreshElapsed(DateTimeOffset.Now);
    }

    public AppointmentQueueItemDto Source { get; }
    public string TimeDisplay { get; }
    public string ApptTimeDisplay { get; }
    public bool IsNoAppointment { get; }
    public bool IsWalkIn { get; }
    public string ArrivalTimeDisplay { get; }
    public bool HasArrivalTime { get; }
    public string PatientNameDisplay { get; }
    public string ReceptionNoteDisplay { get; }
    public bool HasReceptionNote { get; }
    public bool HasCriticalAlert { get; }
    public string PatientDisplay { get; }
    public string DoctorRoomDisplay { get; }
    public bool IsFinished { get; }
    public double RowContentOpacity { get; }

    public void RefreshElapsed(DateTimeOffset now)
    {
        var normalized = AppointmentCalendarHelper.NormalizeQueueStatus(Source.Status);

        StatusDisplay = normalized switch
        {
            QueueStatus.Waiting => FormatWaitingStatus(now),
            QueueStatus.InChair => FormatInChairStatus(now),
            QueueStatus.AwaitingCheckout => "Checkout",
            QueueStatus.AiBookedPending => "AI Pending",
            QueueStatus.Scheduled => "Scheduled",
            QueueStatus.Completed => "Completed",
            QueueStatus.Cancelled => "Cancelled",
            _ => normalized.ToString()
        };

        var palette = normalized switch
        {
            QueueStatus.Waiting => AppointmentCalendarHelper.IsLongWait(Source, now) ? LongWaitPalette : WaitingPalette,
            QueueStatus.InChair => InChairPalette,
            QueueStatus.AwaitingCheckout => CheckoutPalette,
            QueueStatus.AiBookedPending => AiPendingPalette,
            QueueStatus.Completed => CompletedPalette,
            QueueStatus.Cancelled => CancelledPalette,
            _ => ScheduledPalette
        };

        StatusBadgeBackground = palette.Background;
        StatusBadgeBorder = palette.Border;
        StatusDotBrush = palette.Dot;
        StatusBadgeForeground = palette.Foreground;
        ToolTipText = BuildToolTip(normalized);
    }

    private string FormatWaitingStatus(DateTimeOffset now)
    {
        var elapsed = AppointmentCalendarHelper.FormatElapsedMinutes(Source.ArrivedAt, now);
        return string.IsNullOrEmpty(elapsed) ? "Waiting" : $"Waiting · {elapsed}";
    }

    private string FormatInChairStatus(DateTimeOffset now)
    {
        var elapsed = AppointmentCalendarHelper.FormatElapsedMinutes(Source.SeatedAt, now);
        return string.IsNullOrEmpty(elapsed) ? "In Chair" : $"In Chair · {elapsed}";
    }

    private string BuildToolTip(QueueStatus normalized)
    {
        var lines = new List<string>
        {
            $"{Source.PatientName}{(string.IsNullOrWhiteSpace(Source.PatientIc) ? string.Empty : $" ({Source.PatientIc})")}",
            $"Visit: {(IsWalkIn ? "Walk-In (No Appointment)" : Source.VisitType == VisitType.AiBooked ? "WhatsApp AI Booking" : "Scheduled Appointment")}",
            $"Treatment: {Source.ProcedureSummary} ({Source.DurationMinutes} min)",
            $"Clinician: {DoctorRoomDisplay}",
            $"Status: {StatusDisplay}"
        };

        if (!IsWalkIn)
        {
            lines.Add($"Appt Time: {Source.ScheduledAt.ToLocalTime():HH:mm}");
        }

        if (Source.ArrivedAt.HasValue)
        {
            lines.Add($"Arrived: {Source.ArrivedAt.Value.ToLocalTime():HH:mm}");
        }

        if (Source.HasCriticalMedicalAlert)
        {
            lines.Add($"Medical Alert: {Source.AlertSummary}");
        }

        if (!string.IsNullOrWhiteSpace(Source.Notes))
        {
            lines.Add($"Reception Note: {Source.Notes}");
        }

        var attribution = new List<string>();
        if (!string.IsNullOrWhiteSpace(Source.CreatedBy))
        {
            attribution.Add($"Added by {Source.CreatedBy}");
        }
        if (!string.IsNullOrWhiteSpace(Source.CheckedInBy))
        {
            attribution.Add($"Checked in by {Source.CheckedInBy}");
        }
        if (!string.IsNullOrWhiteSpace(Source.SeatedBy))
        {
            attribution.Add($"Seated by {Source.SeatedBy}");
        }
        if (!string.IsNullOrWhiteSpace(Source.CheckoutBy))
        {
            attribution.Add($"Checkout by {Source.CheckoutBy}");
        }
        if (!string.IsNullOrWhiteSpace(Source.CompletedBy))
        {
            attribution.Add($"Completed by {Source.CompletedBy}");
        }
        else if (normalized == QueueStatus.Cancelled && !string.IsNullOrWhiteSpace(Source.LastUpdatedBy))
        {
            attribution.Add($"Cancelled by {Source.LastUpdatedBy}");
        }

        if (attribution.Count > 0)
        {
            lines.Add($"Staff Trail: {string.Join(" → ", attribution)}");
        }

        lines.Add("Double-click to open Visit Details (or Odontogram if In Chair)");
        return string.Join(Environment.NewLine, lines);
    }

    private static Brush FreezeBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private static StatusPillPalette CreatePalette(string bgHex, string borderHex, string dotHex, string fgHex) =>
        new(FreezeBrush(bgHex), FreezeBrush(borderHex), FreezeBrush(dotHex), FreezeBrush(fgHex));

    private sealed record StatusPillPalette(Brush Background, Brush Border, Brush Dot, Brush Foreground);
}

/// <summary>
/// Concise 4-column presentation row for the Patients Registry UniformPagedGrid.
/// </summary>
public sealed class PatientRowItem
{
    public PatientRowItem(PatientDto source)
    {
        Source = source;
        IcDisplay = source.IsMyKadVerified
            ? $"{source.IcNumber} ✓"
            : source.IcNumber;
        FullName = source.FullName;
        Phone = string.IsNullOrWhiteSpace(source.Phone) ? "No Phone" : source.Phone;
        string visitWord = source.VisitCount == 1 ? "1 Visit" : $"{source.VisitCount} Visits";
        ContactVisitsDisplay = source.CreditBalance > 0m
            ? $"{Phone} · {visitWord} · Cr RM {source.CreditBalance:N0}"
            : $"{Phone} · {visitWord}";
        AlertsDisplay = source.MedicalAlerts.HasAnyCriticalAlert
            ? source.MedicalAlerts.BuildAlertSummary()
            : "Clear";
    }

    public PatientDto Source { get; }
    public string IcDisplay { get; }
    public string FullName { get; }
    public string Phone { get; }
    public string ContactVisitsDisplay { get; }
    public string AlertsDisplay { get; }
}

/// <summary>
/// Concise 4-column presentation row for a patient's chronological Visit Records inside PatientDetailModal.
/// </summary>
public sealed class PatientVisitRowItem
{
    public PatientVisitRowItem(PatientVisitDto source)
    {
        Source = source;
        VisitDisplay = $"#{source.VisitNumber} · {source.VisitDate.ToLocalTime():dd MMM yyyy}";
        ClinicianDisplay = $"{source.BranchId} · {source.DoctorName}";
        SummaryDisplay = OdontogramHelper.FormatCompactVisitSummary(source, 34);
        int charted = source.ChartedTeethCount;
        int done = source.CompletedProceduresCount;
        OdontogramDisplay = charted == 0
            ? "Uncharted"
            : done > 0
                ? $"{charted} Teeth · {done} Done"
                : $"{charted} Teeth Charted";
    }

    public PatientVisitDto Source { get; }
    public string VisitDisplay { get; }
    public string ClinicianDisplay { get; }
    public string SummaryDisplay { get; }
    public string OdontogramDisplay { get; }
}

/// <summary>
/// Concise 4-column presentation row for the Billing &amp; Invoices UniformPagedGrid.
/// </summary>
public sealed class InvoiceRowItem
{
    public InvoiceRowItem(InvoiceDto source)
    {
        Source = source;
        InvoiceNumber = source.InvoiceNumber;
        PatientName = source.IsWalkInRetail ? $"{source.PatientName} (OTC)" : source.PatientName;
        PayableDisplay = $"RM {source.PatientPayableAmount:N2}";
        StatusDisplay = source.IsVoided
            ? "Voided"
            : (source.Payments.Count > 0 && source.BalanceDue <= 0m)
                ? (source.CreditedOverpaymentAmount > 0m ? $"Settled (+Cr RM {source.CreditedOverpaymentAmount:N0})" : "Settled")
                : source.Payments.Count == 0
                    ? $"Draft · RM {source.BalanceDue:N2}"
                    : $"Due RM {source.BalanceDue:N2}";
    }

    public InvoiceDto Source { get; }
    public string InvoiceNumber { get; }
    public string PatientName { get; }
    public string PayableDisplay { get; }
    public string StatusDisplay { get; }
}

/// <summary>
/// Concise 4-column presentation row for the WhatsApp AI Console UniformPagedGrid.
/// </summary>
public sealed class WhatsAppThreadRowItem
{
    public WhatsAppThreadRowItem(WhatsAppThreadDto source)
    {
        Source = source;
        ContactDisplay = string.IsNullOrWhiteSpace(source.PatientName)
            ? source.PatientPhone
            : $"{source.PatientName} ({source.PatientPhone})";
        LastUpdatedDisplay = source.LastUpdatedAt.ToLocalTime().ToString("dd MMM HH:mm");
        TriageDisplay = source.IsEmergencyEscalated
            ? "EMERGENCY"
            : source.Messages.Any(m => m.CreatedBookingId.HasValue)
                ? "AI-Booked"
                : "Routine";
        StateDisplay = source.IsEmergencyEscalated ? "Escalated" : "AI Active";
    }

    public WhatsAppThreadDto Source { get; }
    public string ContactDisplay { get; }
    public string LastUpdatedDisplay { get; }
    public string TriageDisplay { get; }
    public string StateDisplay { get; }
}

/// <summary>
/// Concise 4-column presentation row for InvoiceCheckoutModal line items.
/// </summary>
public sealed class InvoiceLineRowItem
{
    public InvoiceLineRowItem(InvoiceLineDto source)
    {
        Source = source;
        string shortCat = source.Category switch
        {
            BillingCatalogCategory.OtcProduct => "OTC (0%)",
            BillingCatalogCategory.Medication => "Med",
            BillingCatalogCategory.Radiograph => "X-Ray",
            _ => "Proc"
        };
        ToothDisplay = source.ToothNumber.HasValue
            ? $"#{source.ToothNumber.Value} · {shortCat}"
            : shortCat;
        ProcedureName = source.ProcedureName;
        Quantity = source.Quantity;
        QtyUnitDisplay = $"{source.Quantity} × RM {source.UnitPrice:N2}";
        LineTotalDisplay = $"RM {source.LineTotal:N2}";
    }

    public InvoiceLineDto Source { get; }
    public string ToothDisplay { get; }
    public string ProcedureName { get; }
    public int Quantity { get; }
    public string QtyUnitDisplay { get; }
    public string LineTotalDisplay { get; }
}

/// <summary>
/// Concise 4-column presentation row for the Unified Master Catalogue UniformPagedGrid inside Settings.
/// </summary>
public sealed class BillingCatalogRowItem
{
    public BillingCatalogRowItem(BillingCatalogItemDto source)
    {
        Source = source;
        Name = source.IsActive ? source.Name : $"{source.Name} (Inactive)";

        var tags = new List<string>
        {
            source.Category switch
            {
                BillingCatalogCategory.OtcProduct => "OTC Retail",
                BillingCatalogCategory.Medication => "Medication",
                BillingCatalogCategory.Radiograph => "Radiograph",
                _ => "Procedure"
            }
        };

        if (source.IsBookableAppointment)
        {
            tags.Add($"Appt {source.DefaultDurationMinutes}m");
        }
        if (source.ShowInOdontogramPresets)
        {
            tags.Add("Chart");
        }
        tags.Add(source.Category != BillingCatalogCategory.OtcProduct && source.IsCommissionable ? "40%" : "0% Comm");

        CategoryDisplay = string.Join(" · ", tags);
        KeywordsDisplay = string.IsNullOrWhiteSpace(source.NoteKeywords)
            ? "—"
            : AppointmentCalendarHelper.FormatCompactReceptionNote(source.NoteKeywords, 34);
        PriceDisplay = $"RM {source.UnitPrice:N2}";
    }

    public BillingCatalogItemDto Source { get; }
    public string Name { get; }
    public string CategoryDisplay { get; }
    public string KeywordsDisplay { get; }
    public string PriceDisplay { get; }
}

/// <summary>
/// Concise 4-column presentation row for Per-Doctor Treatment Suggestion Rules inside Settings.
/// </summary>
public sealed class DoctorTreatmentRuleRowItem
{
    public DoctorTreatmentRuleRowItem(
        BillingCatalogItemDto procedureItem,
        IReadOnlyList<BillingCatalogItemDto> suggestedItems,
        bool isDoctorScope,
        bool isDoctorOverride)
    {
        ProcedureItem = procedureItem;
        SuggestedItems = suggestedItems;
        IsDoctorOverride = isDoctorOverride;
        TreatmentName = procedureItem.Name;
        RuleSourceDisplay = !isDoctorScope
            ? "Clinic Default"
            : (isDoctorOverride ? "Doctor Custom" : "Inherited Default");
        SuggestedSummary = suggestedItems.Count == 0
            ? "None (No Add-Ons)"
            : AppointmentCalendarHelper.FormatCompactReceptionNote(string.Join(", ", suggestedItems.Select(i => i.Name)), 44);
        CountDisplay = suggestedItems.Count == 1 ? "1 Item" : $"{suggestedItems.Count} Items";
    }

    public BillingCatalogItemDto ProcedureItem { get; }
    public IReadOnlyList<BillingCatalogItemDto> SuggestedItems { get; }
    public bool IsDoctorOverride { get; }
    public string TreatmentName { get; }
    public string RuleSourceDisplay { get; }
    public string SuggestedSummary { get; }
    public string CountDisplay { get; }
}

/// <summary>
/// Concise 4-column presentation row for DailyReconciliationModal audit trail.
/// </summary>
public sealed class AuditLogRowItem
{
    public AuditLogRowItem(AuditLogDto source)
    {
        Source = source;
        TimestampDisplay = source.Timestamp.ToLocalTime().ToString("HH:mm:ss");
        Action = source.Action;
        EntityDisplay = $"{source.EntityName} ({source.UserId})";
        DetailDisplay = !string.IsNullOrWhiteSpace(source.NewValue)
            ? source.NewValue!
            : source.EntityId;
    }

    public AuditLogDto Source { get; }
    public string TimestampDisplay { get; }
    public string Action { get; }
    public string EntityDisplay { get; }
    public string DetailDisplay { get; }
}

/// <summary>
/// Concise 4-column presentation row for the Appointments UniformPagedGrid.
/// </summary>
public sealed class AppointmentRowItem
{
    public AppointmentRowItem(AppointmentQueueItemDto source)
    {
        Source = source;
        var local = source.ScheduledAt.ToLocalTime();
        DateTimeDisplay = source.VisitType == VisitType.WalkIn
            ? $"{local:dd MMM} · Walk-In ({local:HH:mm})"
            : $"{local:dd MMM · HH:mm} ({source.DurationMinutes}m)";
        PatientDisplay = source.HasCriticalMedicalAlert
            ? $"{source.PatientName} [ALERT]"
            : source.PatientName;
        DentistDisplay = source.DoctorName;
        TreatmentStatusDisplay = $"{source.ProcedureSummary} · {AppointmentCalendarHelper.FormatStatusLabel(source.Status)}";
    }

    public AppointmentQueueItemDto Source { get; }
    public string DateTimeDisplay { get; }
    public string PatientDisplay { get; }
    public string DentistDisplay { get; }
    public string TreatmentStatusDisplay { get; }
}
