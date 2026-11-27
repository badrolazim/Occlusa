using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Occlusa.Desktop.Themes;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.ViewModels;

internal static class RowTextHelper
{
    public static string NormalizeSingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static string FormatPaymentMethod(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Cash",
        PaymentMethod.Card => "Card",
        PaymentMethod.QrTransfer => "DuitNow QR",
        PaymentMethod.CorporatePanel => "Panel",
        PaymentMethod.PatientCredit => "Credit",
        _ => method.ToString()
    };

    public static string FormatSexAge(string? gender, DateOnly dob)
    {
        string sex = string.IsNullOrWhiteSpace(gender)
            ? string.Empty
            : gender.Trim().StartsWith("F", StringComparison.OrdinalIgnoreCase)
                ? "F"
                : gender.Trim().StartsWith("M", StringComparison.OrdinalIgnoreCase)
                    ? "M"
                    : gender.Trim();

        string age = string.Empty;
        if (dob != default)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            int years = today.Year - dob.Year;
            if (dob > today.AddYears(-years))
            {
                years--;
            }

            if (years >= 0 && years <= 130)
            {
                age = $"{years}y";
            }
        }

        if (!string.IsNullOrEmpty(sex) && !string.IsNullOrEmpty(age))
        {
            return $"{sex} · {age}";
        }

        return !string.IsNullOrEmpty(sex) ? sex : (!string.IsNullOrEmpty(age) ? age : "—");
    }
}

/// <summary>
/// Full-width, strict single-line presentation row for the Live Queue UniformPagedGrid
/// supporting both Simple and Detailed Operations views with atomic columns, reminder badges,
/// optional live wait timers, and complete hover tooltips.
/// </summary>
public sealed partial class QueueRowItem : ObservableObject
{
    private static readonly StatusPillPalette WaitingPalette = new(CalmThemePalette.StatusWarningBgBrush, CalmThemePalette.BorderDefaultBrush, CalmThemePalette.StatusWarningBrush, CalmThemePalette.StatusWarningBrush);
    private static readonly StatusPillPalette LongWaitPalette = new(CalmThemePalette.StatusErrorBgBrush, CalmThemePalette.StatusErrorBrush, CalmThemePalette.StatusErrorBrush, CalmThemePalette.StatusErrorBrush);
    private static readonly StatusPillPalette InChairPalette = new(CalmThemePalette.BrandTintBrush, CalmThemePalette.BrandPrimaryBrush, CalmThemePalette.BrandPrimaryBrush, CalmThemePalette.BrandHoverBrush);
    private static readonly StatusPillPalette CheckoutPalette = new(CalmThemePalette.StatusInfoBgBrush, CalmThemePalette.BorderDefaultBrush, CalmThemePalette.StatusInfoBrush, CalmThemePalette.StatusInfoBrush);
    private static readonly StatusPillPalette AiPendingPalette = new(CalmThemePalette.StatusWarningBgBrush, CalmThemePalette.StatusWarningBrush, CalmThemePalette.StatusWarningBrush, CalmThemePalette.StatusWarningBrush);
    private static readonly StatusPillPalette ScheduledPalette = new(CalmThemePalette.BgSubtleBrush, CalmThemePalette.BorderDefaultBrush, CalmThemePalette.TextSecondaryBrush, CalmThemePalette.TextSecondaryBrush);
    private static readonly StatusPillPalette CompletedPalette = new(CalmThemePalette.StatusSuccessBgBrush, CalmThemePalette.BorderDefaultBrush, CalmThemePalette.StatusSuccessBrush, CalmThemePalette.StatusSuccessBrush);
    private static readonly StatusPillPalette CancelledPalette = new(CalmThemePalette.StatusErrorBgBrush, CalmThemePalette.BorderDefaultBrush, CalmThemePalette.StatusErrorBrush, CalmThemePalette.StatusErrorBrush);

    private static readonly StatusPillPalette ReminderConfirmedPalette = new(CalmThemePalette.StatusSuccessBgBrush, CalmThemePalette.BorderDefaultBrush, CalmThemePalette.StatusSuccessBrush, CalmThemePalette.StatusSuccessBrush);
    private static readonly StatusPillPalette ReminderSentPalette = new(CalmThemePalette.StatusInfoBgBrush, CalmThemePalette.BorderDefaultBrush, CalmThemePalette.StatusInfoBrush, CalmThemePalette.StatusInfoBrush);
    private static readonly StatusPillPalette ReminderNotSentPalette = new(CalmThemePalette.StatusWarningBgBrush, CalmThemePalette.BorderDefaultBrush, CalmThemePalette.StatusWarningBrush, CalmThemePalette.StatusWarningBrush);
    private static readonly StatusPillPalette ReminderNoReplyPalette = new(CalmThemePalette.StatusErrorBgBrush, CalmThemePalette.StatusErrorBrush, CalmThemePalette.StatusErrorBrush, CalmThemePalette.StatusErrorBrush);
    private static readonly StatusPillPalette ReminderQuietPalette = new(CalmThemePalette.BgSubtleBrush, CalmThemePalette.BorderDefaultBrush, CalmThemePalette.TextSecondaryBrush, CalmThemePalette.TextSecondaryBrush);

    private readonly bool _showWaitTimers;

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

    public QueueRowItem(
        AppointmentQueueItemDto source,
        bool showDoctorInSubline = false,
        bool showWaitTimers = false,
        bool showInlineReceptionNotes = false)
    {
        Source = source;
        _showWaitTimers = showWaitTimers;
        IsWalkIn = source.VisitType == VisitType.WalkIn;
        IsNoAppointment = IsWalkIn;
        ApptTimeDisplay = IsWalkIn
            ? "Walk-In"
            : source.ScheduledAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        SimpleTimeDisplay = ApptTimeDisplay;

        if (IsWalkIn)
        {
            var arrived = (source.ArrivedAt ?? source.ScheduledAt).ToLocalTime();
            ArrivalTimeDisplay = $"{arrived:HH:mm} · Walk-In";
            ArrivedShortDisplay = arrived.ToString("HH:mm", CultureInfo.InvariantCulture);
            StackedTimePrimaryDisplay = "Walk-In";
            StackedTimeSecondaryDisplay = $"Arr {arrived:HH:mm}";
            HasArrivalTime = true;
        }
        else if (source.ArrivedAt.HasValue)
        {
            var arrivedLocal = source.ArrivedAt.Value.ToLocalTime();
            ArrivalTimeDisplay = arrivedLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
            ArrivedShortDisplay = ArrivalTimeDisplay;
            StackedTimePrimaryDisplay = ApptTimeDisplay;
            StackedTimeSecondaryDisplay = $"Arr {arrivedLocal:HH:mm}";
            HasArrivalTime = true;
        }
        else
        {
            ArrivalTimeDisplay = "—";
            ArrivedShortDisplay = "—";
            StackedTimePrimaryDisplay = ApptTimeDisplay;
            StackedTimeSecondaryDisplay = "Upcoming";
            HasArrivalTime = false;
        }

        TimeDisplay = IsWalkIn ? ArrivalTimeDisplay : ApptTimeDisplay;
        VisitTypeDisplay = source.VisitType switch
        {
            VisitType.WalkIn => "Walk-In",
            VisitType.AiBooked => "WhatsApp AI",
            _ => "Booked"
        };

        PatientNameDisplay = source.PatientName;
        PatientIcDisplay = string.IsNullOrWhiteSpace(source.PatientIc) ? "—" : source.PatientIc;
        HasCriticalAlert = source.HasCriticalMedicalAlert;
        PatientMeasureText = HasCriticalAlert
            ? $"{source.PatientName}   ALERT"
            : source.PatientName;
        PatientDisplay = HasCriticalAlert
            ? $"{source.PatientName} [ALERT]"
            : source.PatientName;

        TreatmentDisplay = string.IsNullOrWhiteSpace(source.ProcedureSummary)
            ? "Consultation"
            : RowTextHelper.NormalizeSingleLine(source.ProcedureSummary);
        DurationDisplay = $"{source.DurationMinutes}m";
        DoctorDisplay = string.IsNullOrWhiteSpace(source.DoctorName) ? "Unassigned" : source.DoctorName.Trim();
        RoomDisplay = string.IsNullOrWhiteSpace(source.RoomName) ? "—" : source.RoomName.Trim();
        DoctorRoomDisplay = string.IsNullOrWhiteSpace(source.RoomName)
            ? DoctorDisplay
            : $"{DoctorDisplay} · {RoomDisplay}";

        string fullNote = RowTextHelper.NormalizeSingleLine(source.Notes);
        ReceptionNoteDisplay = fullNote;
        HasReceptionNote = showInlineReceptionNotes && !string.IsNullOrWhiteSpace(fullNote);
        NoteColumnDisplay = HasReceptionNote ? fullNote : "—";

        SimplePatientSubline = showDoctorInSubline
            ? $"{DoctorDisplay} · {TreatmentDisplay}"
            : TreatmentDisplay;
        DetailedPatientSubline = HasReceptionNote ? fullNote : TreatmentDisplay;
        HasDetailedPatientSubline = !string.IsNullOrWhiteSpace(DetailedPatientSubline);

        IsFinished = AppointmentCalendarHelper.IsFinishedQueueStatus(source.Status);
        RowContentOpacity = IsFinished ? 0.58 : 1.0;

        ReminderDisplay = AppointmentCalendarHelper.FormatReminderBadgeLabel(source);
        IsReminderQuiet = !AppointmentCalendarHelper.IsUpcomingRemindableAppointment(source);
        var reminderPalette = IsReminderQuiet
            ? ReminderQuietPalette
            : source.ReminderStatus switch
            {
                ReminderStatus.Confirmed => ReminderConfirmedPalette,
                ReminderStatus.Reminded => ReminderSentPalette,
                ReminderStatus.NoReply => ReminderNoReplyPalette,
                _ => ReminderNotSentPalette
            };
        ReminderBadgeBackground = reminderPalette.Background;
        ReminderBadgeBorder = reminderPalette.Border;
        ReminderDotBrush = reminderPalette.Dot;
        ReminderBadgeForeground = reminderPalette.Foreground;

        RefreshElapsed(DateTimeOffset.Now);
    }

    public AppointmentQueueItemDto Source { get; }
    public string TimeDisplay { get; }
    public string SimpleTimeDisplay { get; }
    public string StackedTimePrimaryDisplay { get; }
    public string StackedTimeSecondaryDisplay { get; }
    public string ApptTimeDisplay { get; }
    public bool IsNoAppointment { get; }
    public bool IsWalkIn { get; }
    public string ArrivalTimeDisplay { get; }
    public string ArrivedShortDisplay { get; }
    public bool HasArrivalTime { get; }
    public string VisitTypeDisplay { get; }
    public string PatientNameDisplay { get; }
    public string PatientMeasureText { get; }
    public string PatientIcDisplay { get; }
    public string TreatmentDisplay { get; }
    public string DurationDisplay { get; }
    public string DoctorDisplay { get; }
    public string RoomDisplay { get; }
    public string NoteColumnDisplay { get; }
    public string SimplePatientSubline { get; }
    public string DetailedPatientSubline { get; }
    public bool HasDetailedPatientSubline { get; }
    public string ReceptionNoteDisplay { get; }
    public bool HasReceptionNote { get; }
    public bool HasCriticalAlert { get; }
    public string PatientDisplay { get; }
    public string DoctorRoomDisplay { get; }
    public string ReminderDisplay { get; }
    public bool IsReminderQuiet { get; }
    public Brush ReminderBadgeBackground { get; }
    public Brush ReminderBadgeBorder { get; }
    public Brush ReminderDotBrush { get; }
    public Brush ReminderBadgeForeground { get; }
    public bool IsFinished { get; }
    public double RowContentOpacity { get; }

    public void RefreshElapsed(DateTimeOffset now)
    {
        var normalized = AppointmentCalendarHelper.NormalizeQueueStatus(Source.Status);

        StatusDisplay = normalized switch
        {
            QueueStatus.Waiting => _showWaitTimers ? FormatWaitingStatus(now) : "Waiting",
            QueueStatus.InChair => _showWaitTimers ? FormatInChairStatus(now) : "In Chair",
            QueueStatus.AwaitingCheckout => "Checkout",
            QueueStatus.AiBookedPending => "AI Pending",
            QueueStatus.Scheduled => "Scheduled",
            QueueStatus.Completed => "Done",
            QueueStatus.Cancelled => "Cancelled",
            _ => normalized.ToString()
        };

        var palette = normalized switch
        {
            QueueStatus.Waiting => (_showWaitTimers && AppointmentCalendarHelper.IsLongWait(Source, now)) ? LongWaitPalette : WaitingPalette,
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
            $"Treatment: {TreatmentDisplay} ({Source.DurationMinutes} min)",
            $"Clinician: {DoctorRoomDisplay}",
            $"Status: {StatusDisplay}",
            $"Reminder: {AppointmentCalendarHelper.FormatReminderDetailSummary(Source)}"
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
            lines.Add($"Reception Note: {RowTextHelper.NormalizeSingleLine(Source.Notes)}");
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

        string doubleClickTarget = normalized switch
        {
            QueueStatus.AwaitingCheckout => "Double-click to open Invoice Checkout",
            QueueStatus.Waiting or QueueStatus.InChair => "Double-click to open Clinical Chart",
            _ => "Double-click to open Appointment Info"
        };
        lines.Add(doubleClickTarget);
        return string.Join(Environment.NewLine, lines);
    }

    private sealed record StatusPillPalette(Brush Background, Brush Border, Brush Dot, Brush Foreground);
}

/// <summary>
/// Atomic single-line presentation row for the Patients Registry UniformPagedGrid.
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
        SexAgeDisplay = RowTextHelper.FormatSexAge(source.Gender, source.DateOfBirth);
        Phone = string.IsNullOrWhiteSpace(source.Phone) ? "—" : source.Phone;
        BranchDisplay = string.IsNullOrWhiteSpace(source.PrimaryBranchId) ? "—" : source.PrimaryBranchId;
        VisitsDisplay = source.VisitCount == 1 ? "1 Visit" : $"{source.VisitCount} Visits";
        LastVisitDisplay = source.LastVisitAt.HasValue
            ? source.LastVisitAt.Value.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
            : "Never";
        CreditDisplay = $"RM {source.CreditBalance:N2}";
        ConsentDisplay = source.ConsentSignedAt.HasValue ? "Signed" : "Pending";
        ContactVisitsDisplay = source.CreditBalance > 0m
            ? $"{Phone} · {VisitsDisplay} · Cr RM {source.CreditBalance:N0}"
            : $"{Phone} · {VisitsDisplay}";
        AlertsDisplay = source.MedicalAlerts.HasAnyCriticalAlert
            ? source.MedicalAlerts.BuildAlertSummary()
            : "Clear";

        var tipLines = new List<string>
        {
            $"{FullName} ({IcDisplay})",
            $"Demographics: {SexAgeDisplay}{(source.DateOfBirth != default ? $" · DOB {source.DateOfBirth:dd MMM yyyy}" : string.Empty)}",
            $"Phone: {Phone} · Primary Branch: {BranchDisplay}",
            $"Visits: {VisitsDisplay} · Last Visit: {LastVisitDisplay} · Credit: {CreditDisplay}",
            $"PDPA Consent: {(source.ConsentSignedAt.HasValue ? $"Signed {source.ConsentSignedAt.Value.ToLocalTime():dd MMM yyyy}" : "Pending")}",
            $"Medical Alerts: {AlertsDisplay}"
        };
        if (!string.IsNullOrWhiteSpace(source.MedicalAlerts.Notes))
        {
            tipLines.Add($"Alert Notes: {RowTextHelper.NormalizeSingleLine(source.MedicalAlerts.Notes)}");
        }
        if (!string.IsNullOrWhiteSpace(source.Address))
        {
            tipLines.Add($"Address: {RowTextHelper.NormalizeSingleLine(source.Address)}");
        }
        tipLines.Add("Double-click to open Patient Profile & Visit Records");
        ToolTipText = string.Join(Environment.NewLine, tipLines);
    }

    public PatientDto Source { get; }
    public string IcDisplay { get; }
    public string FullName { get; }
    public string SexAgeDisplay { get; }
    public string Phone { get; }
    public string BranchDisplay { get; }
    public string VisitsDisplay { get; }
    public string LastVisitDisplay { get; }
    public string CreditDisplay { get; }
    public string ConsentDisplay { get; }
    public string ContactVisitsDisplay { get; }
    public string AlertsDisplay { get; }
    public string ToolTipText { get; }
}

/// <summary>
/// Atomic single-line presentation row for a patient's chronological Visit Records inside PatientDetailModal.
/// </summary>
public sealed class PatientVisitRowItem
{
    public PatientVisitRowItem(PatientVisitDto source)
    {
        Source = source;
        VisitNumberDisplay = $"#{source.VisitNumber}";
        VisitDateDisplay = source.VisitDate.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        VisitDisplay = $"{VisitNumberDisplay} · {VisitDateDisplay}";
        BranchDisplay = source.BranchId;
        DoctorDisplay = source.DoctorName;
        ClinicianDisplay = $"{source.BranchId} · {source.DoctorName}";

        string rawSummary = !string.IsNullOrWhiteSpace(source.Assessment)
            ? source.Assessment
            : !string.IsNullOrWhiteSpace(source.Subjective)
                ? source.Subjective
                : !string.IsNullOrWhiteSpace(source.Plan)
                    ? source.Plan
                    : "Clinical chart recorded";
        SummaryDisplay = RowTextHelper.NormalizeSingleLine(rawSummary);

        int charted = source.ChartedTeethCount;
        int done = source.CompletedProceduresCount;
        OdontogramDisplay = charted == 0
            ? "Uncharted"
            : done > 0
                ? $"{charted} Teeth · {done} Done"
                : $"{charted} Teeth";

        var tipLines = new List<string>
        {
            $"Visit {VisitNumberDisplay} · {VisitDateDisplay}",
            $"Clinician: {DoctorDisplay} ({BranchDisplay})",
            $"Odontogram: {OdontogramDisplay}"
        };
        if (!string.IsNullOrWhiteSpace(source.Subjective))
        {
            tipLines.Add($"S: {RowTextHelper.NormalizeSingleLine(source.Subjective)}");
        }
        if (!string.IsNullOrWhiteSpace(source.Objective))
        {
            tipLines.Add($"O: {RowTextHelper.NormalizeSingleLine(source.Objective)}");
        }
        if (!string.IsNullOrWhiteSpace(source.Assessment))
        {
            tipLines.Add($"A: {RowTextHelper.NormalizeSingleLine(source.Assessment)}");
        }
        if (!string.IsNullOrWhiteSpace(source.Plan))
        {
            tipLines.Add($"P: {RowTextHelper.NormalizeSingleLine(source.Plan)}");
        }
        tipLines.Add("Double-click to open Visit Odontogram & SOAP Notes");
        ToolTipText = string.Join(Environment.NewLine, tipLines);
    }

    public PatientVisitDto Source { get; }
    public string VisitNumberDisplay { get; }
    public string VisitDateDisplay { get; }
    public string VisitDisplay { get; }
    public string BranchDisplay { get; }
    public string DoctorDisplay { get; }
    public string ClinicianDisplay { get; }
    public string SummaryDisplay { get; }
    public string OdontogramDisplay { get; }
    public string ToolTipText { get; }
}

/// <summary>
/// Atomic single-line presentation row for the Billing &amp; Invoices UniformPagedGrid.
/// </summary>
public sealed class InvoiceRowItem
{
    public InvoiceRowItem(InvoiceDto source)
    {
        Source = source;
        InvoiceNumber = source.InvoiceNumber;
        DateDisplay = source.CreatedAt.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.InvariantCulture);
        PatientName = source.IsWalkInRetail ? $"{source.PatientName} (OTC)" : source.PatientName;
        DoctorDisplay = string.IsNullOrWhiteSpace(source.DoctorName)
            ? (source.IsWalkInRetail ? "Counter OTC" : "—")
            : source.DoctorName;
        ItemsCountDisplay = source.Lines.Count == 1 ? "1 Item" : $"{source.Lines.Count} Items";
        SubtotalDisplay = $"RM {source.Subtotal:N2}";

        if (source.PanelCoveredAmount > 0m && source.DiscountAmount > 0m)
        {
            string panelLabel = string.IsNullOrWhiteSpace(source.PanelName) ? "Panel" : source.PanelName!;
            PanelDiscountDisplay = $"{panelLabel} (-RM {source.PanelCoveredAmount + source.DiscountAmount:N0})";
        }
        else if (source.PanelCoveredAmount > 0m)
        {
            string panelLabel = string.IsNullOrWhiteSpace(source.PanelName) ? "Panel" : source.PanelName!;
            PanelDiscountDisplay = $"{panelLabel} (RM {source.PanelCoveredAmount:N2})";
        }
        else if (source.DiscountAmount > 0m)
        {
            PanelDiscountDisplay = $"-RM {source.DiscountAmount:N2}";
        }
        else
        {
            PanelDiscountDisplay = "—";
        }

        PayableDisplay = $"RM {source.PatientPayableAmount:N2}";
        PaidDisplay = $"RM {source.PaidAmount:N2}";
        BalanceDueDisplay = source.IsVoided ? "—" : $"RM {source.BalanceDue:N2}";
        PaymentMethodsDisplay = source.Payments.Count == 0
            ? "—"
            : string.Join(" + ", source.Payments.Select(p => RowTextHelper.FormatPaymentMethod(p.Method)).Distinct());

        StatusDisplay = source.IsVoided
            ? "Voided"
            : (source.Payments.Count > 0 && source.BalanceDue <= 0m)
                ? (source.CreditedOverpaymentAmount > 0m ? $"Settled (+Cr RM {source.CreditedOverpaymentAmount:N0})" : "Settled")
                : source.Payments.Count == 0
                    ? "Draft"
                    : "Partial";

        var tipLines = new List<string>
        {
            $"{InvoiceNumber} · {DateDisplay} · {StatusDisplay}",
            $"Patient: {PatientName} · Clinician: {DoctorDisplay}",
            $"Lines ({source.Lines.Count}): {(source.Lines.Count == 0 ? "Blank draft" : string.Join(", ", source.Lines.Select(l => $"{l.ProcedureName} ×{l.Quantity}")))}",
            $"Subtotal: {SubtotalDisplay} · Panel/Disc: {PanelDiscountDisplay} · Payable: {PayableDisplay}",
            $"Paid: {PaidDisplay} ({PaymentMethodsDisplay}) · Balance Due: {BalanceDueDisplay}"
        };
        if (source.CreditedOverpaymentAmount > 0m)
        {
            tipLines.Add($"Overpayment Credited: RM {source.CreditedOverpaymentAmount:N2}");
        }
        if (source.IsVoided && !string.IsNullOrWhiteSpace(source.VoidReason))
        {
            tipLines.Add($"Void Reason: {RowTextHelper.NormalizeSingleLine(source.VoidReason)}");
        }
        tipLines.Add("Double-click to open Split-Payment Checkout & Soft-Void");
        ToolTipText = string.Join(Environment.NewLine, tipLines);
    }

    public InvoiceDto Source { get; }
    public string InvoiceNumber { get; }
    public string DateDisplay { get; }
    public string PatientName { get; }
    public string DoctorDisplay { get; }
    public string ItemsCountDisplay { get; }
    public string SubtotalDisplay { get; }
    public string PanelDiscountDisplay { get; }
    public string PayableDisplay { get; }
    public string PaidDisplay { get; }
    public string BalanceDueDisplay { get; }
    public string PaymentMethodsDisplay { get; }
    public string StatusDisplay { get; }
    public string ToolTipText { get; }
}

/// <summary>
/// Atomic single-line presentation row for the WhatsApp AI Console UniformPagedGrid.
/// </summary>
public sealed class WhatsAppThreadRowItem
{
    public WhatsAppThreadRowItem(WhatsAppThreadDto source)
    {
        Source = source;
        PatientNameDisplay = string.IsNullOrWhiteSpace(source.PatientName)
            ? "Unregistered Caller"
            : source.PatientName;
        PhoneDisplay = source.PatientPhone;
        ContactDisplay = string.IsNullOrWhiteSpace(source.PatientName)
            ? source.PatientPhone
            : $"{source.PatientName} ({source.PatientPhone})";
        MessageCountDisplay = source.Messages.Count == 1 ? "1 Msg" : $"{source.Messages.Count} Msgs";

        var lastMessage = source.Messages.Count > 0 ? source.Messages[^1] : null;
        LatestPreviewDisplay = source.IsEmergencyEscalated && !string.IsNullOrWhiteSpace(source.EmergencyReason)
            ? RowTextHelper.NormalizeSingleLine(source.EmergencyReason)
            : lastMessage is not null && !string.IsNullOrWhiteSpace(lastMessage.Text)
                ? RowTextHelper.NormalizeSingleLine(lastMessage.Text)
                : "No messages yet";

        LastUpdatedDisplay = source.LastUpdatedAt.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.InvariantCulture);
        TriageDisplay = source.IsEmergencyEscalated
            ? "EMERGENCY"
            : source.Messages.Any(m => m.CreatedBookingId.HasValue)
                ? "AI-Booked"
                : "Routine";
        StateDisplay = source.IsEmergencyEscalated ? "Escalated" : "AI Active";

        var tipLines = new List<string>
        {
            $"{PatientNameDisplay} ({PhoneDisplay})",
            $"Triage: {TriageDisplay} · State: {StateDisplay} · {MessageCountDisplay}",
            $"Last Update: {LastUpdatedDisplay}"
        };
        if (source.IsEmergencyEscalated && !string.IsNullOrWhiteSpace(source.EmergencyReason))
        {
            tipLines.Add($"Emergency Reason: {RowTextHelper.NormalizeSingleLine(source.EmergencyReason)}");
        }
        if (lastMessage is not null)
        {
            tipLines.Add($"Latest ({lastMessage.Sender}): {RowTextHelper.NormalizeSingleLine(lastMessage.Text)}");
        }
        tipLines.Add("Double-click to open Paged Transcript & Emergency Takeover");
        ToolTipText = string.Join(Environment.NewLine, tipLines);
    }

    public WhatsAppThreadDto Source { get; }
    public string PatientNameDisplay { get; }
    public string PhoneDisplay { get; }
    public string ContactDisplay { get; }
    public string MessageCountDisplay { get; }
    public string LatestPreviewDisplay { get; }
    public string LastUpdatedDisplay { get; }
    public string TriageDisplay { get; }
    public string StateDisplay { get; }
    public string ToolTipText { get; }
}

/// <summary>
/// Atomic single-line presentation row for InvoiceCheckoutModal line items.
/// </summary>
public sealed class InvoiceLineRowItem
{
    public InvoiceLineRowItem(InvoiceLineDto source)
    {
        Source = source;
        string shortCat = source.Category switch
        {
            BillingCatalogCategory.OtcProduct => "OTC",
            BillingCatalogCategory.Medication => "Med",
            BillingCatalogCategory.Radiograph => "X-Ray",
            _ => "Proc"
        };
        CategoryShortDisplay = shortCat;
        ToothDisplay = source.ToothNumber.HasValue
            ? $"#{source.ToothNumber.Value} · {shortCat}"
            : shortCat;
        ProcedureName = source.ProcedureName;
        CommissionDisplay = source.Category == BillingCatalogCategory.OtcProduct || !source.IsCommissionable ? "0%" : "40%";
        Quantity = source.Quantity;
        QuantityDisplay = source.Quantity.ToString(CultureInfo.InvariantCulture);
        UnitPriceDisplay = $"RM {source.UnitPrice:N2}";
        QtyUnitDisplay = $"{source.Quantity} × RM {source.UnitPrice:N2}";
        LineTotalDisplay = $"RM {source.LineTotal:N2}";
        ToolTipText = $"{ProcedureName}\nCategory: {source.Category}{(source.ToothNumber.HasValue ? $" · Tooth #{source.ToothNumber.Value}" : string.Empty)}\nQty: {QuantityDisplay} × {UnitPriceDisplay} = {LineTotalDisplay} · Commission: {CommissionDisplay}";
    }

    public InvoiceLineDto Source { get; }
    public string CategoryShortDisplay { get; }
    public string ToothDisplay { get; }
    public string ProcedureName { get; }
    public string CommissionDisplay { get; }
    public int Quantity { get; }
    public string QuantityDisplay { get; }
    public string UnitPriceDisplay { get; }
    public string QtyUnitDisplay { get; }
    public string LineTotalDisplay { get; }
    public string ToolTipText { get; }
}

/// <summary>
/// Atomic single-line presentation row for the Unified Master Catalogue UniformPagedGrid inside Settings.
/// </summary>
public sealed class BillingCatalogRowItem
{
    public BillingCatalogRowItem(BillingCatalogItemDto source)
    {
        Source = source;
        Name = source.IsActive ? source.Name : $"{source.Name} (Inactive)";

        CategoryName = source.Category switch
        {
            BillingCatalogCategory.OtcProduct => "OTC Retail",
            BillingCatalogCategory.Medication => "Medication",
            BillingCatalogCategory.Radiograph => "Radiograph",
            _ => "Procedure"
        };
        SubcategoryDisplay = string.IsNullOrWhiteSpace(source.ClinicalSubcategory)
            ? "General"
            : source.ClinicalSubcategory.Trim();
        AppointmentDisplay = source.IsBookableAppointment
            ? $"{source.DefaultDurationMinutes}m"
            : "—";
        ChartPresetDisplay = source.ShowInOdontogramPresets
            ? source.DefaultToothCondition.ToString()
            : "—";
        CommissionDisplay = source.Category != BillingCatalogCategory.OtcProduct && source.IsCommissionable
            ? "40%"
            : "0%";

        var tags = new List<string> { CategoryName };
        if (source.IsBookableAppointment)
        {
            tags.Add($"Appt {source.DefaultDurationMinutes}m");
        }
        if (source.ShowInOdontogramPresets)
        {
            tags.Add("Chart");
        }
        tags.Add(CommissionDisplay == "40%" ? "40%" : "0% Comm");
        CategoryDisplay = string.Join(" · ", tags);

        KeywordsDisplay = string.IsNullOrWhiteSpace(source.NoteKeywords)
            ? "—"
            : RowTextHelper.NormalizeSingleLine(source.NoteKeywords);
        SafetyTagsDisplay = string.IsNullOrWhiteSpace(source.SafetyAlertTags)
            ? "—"
            : RowTextHelper.NormalizeSingleLine(source.SafetyAlertTags);
        PriceDisplay = $"RM {source.UnitPrice:N2}";

        ToolTipText = string.Join(Environment.NewLine, new[]
        {
            $"{Name} — {PriceDisplay}",
            $"Category: {CategoryName} · Subcategory: {SubcategoryDisplay} · Commission: {CommissionDisplay}",
            $"Appointment Booking: {(source.IsBookableAppointment ? $"Yes ({source.DefaultDurationMinutes} min)" : "No")} · Odontogram Preset: {(source.ShowInOdontogramPresets ? $"Yes ({source.DefaultToothCondition})" : "No")}",
            $"SOAP Note Keywords: {KeywordsDisplay}",
            $"Safety Alert Tags: {SafetyTagsDisplay}",
            "Double-click to edit Master Catalogue Item"
        });
    }

    public BillingCatalogItemDto Source { get; }
    public string Name { get; }
    public string CategoryName { get; }
    public string SubcategoryDisplay { get; }
    public string AppointmentDisplay { get; }
    public string ChartPresetDisplay { get; }
    public string CommissionDisplay { get; }
    public string CategoryDisplay { get; }
    public string KeywordsDisplay { get; }
    public string SafetyTagsDisplay { get; }
    public string PriceDisplay { get; }
    public string ToolTipText { get; }
}

/// <summary>
/// Atomic single-line presentation row for Per-Doctor Treatment Suggestion Rules inside Settings.
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
        SubcategoryDisplay = string.IsNullOrWhiteSpace(procedureItem.ClinicalSubcategory)
            ? "General"
            : procedureItem.ClinicalSubcategory.Trim();
        BasePriceDisplay = $"RM {procedureItem.UnitPrice:N2}";
        RuleSourceDisplay = !isDoctorScope
            ? "Clinic Default"
            : (isDoctorOverride ? "Doctor Custom" : "Inherited Default");
        SuggestedSummary = suggestedItems.Count == 0
            ? "None"
            : string.Join(", ", suggestedItems.Select(i => i.Name));
        CountDisplay = suggestedItems.Count == 1 ? "1 Item" : $"{suggestedItems.Count} Items";

        var tipLines = new List<string>
        {
            $"{TreatmentName} ({SubcategoryDisplay} · {BasePriceDisplay})",
            $"Rule Scope: {RuleSourceDisplay} · {CountDisplay}",
            $"Suggested Add-Ons: {(suggestedItems.Count == 0 ? "None" : string.Join(", ", suggestedItems.Select(i => $"{i.Name} (RM {i.UnitPrice:N2})")))}",
            "Double-click to configure companion invoice item suggestions"
        };
        ToolTipText = string.Join(Environment.NewLine, tipLines);
    }

    public BillingCatalogItemDto ProcedureItem { get; }
    public IReadOnlyList<BillingCatalogItemDto> SuggestedItems { get; }
    public bool IsDoctorOverride { get; }
    public string TreatmentName { get; }
    public string SubcategoryDisplay { get; }
    public string BasePriceDisplay { get; }
    public string RuleSourceDisplay { get; }
    public string SuggestedSummary { get; }
    public string CountDisplay { get; }
    public string ToolTipText { get; }
}

/// <summary>
/// Atomic single-line presentation row for DailyReconciliationModal immutable audit trail.
/// </summary>
public sealed class AuditLogRowItem
{
    public AuditLogRowItem(AuditLogDto source)
    {
        Source = source;
        TimestampDisplay = source.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        DateTimeDisplay = source.Timestamp.ToLocalTime().ToString("dd MMM HH:mm:ss", CultureInfo.InvariantCulture);
        Action = source.Action;
        EntityNameDisplay = source.EntityName;
        StaffDisplay = string.IsNullOrWhiteSpace(source.UserId) ? "System" : source.UserId;
        EntityDisplay = $"{source.EntityName} ({StaffDisplay})";
        DetailDisplay = RowTextHelper.NormalizeSingleLine(
            !string.IsNullOrWhiteSpace(source.NewValue) ? source.NewValue : source.EntityId);

        var tipLines = new List<string>
        {
            $"{DateTimeDisplay} · {Action}",
            $"Entity: {EntityNameDisplay} ({source.EntityId}) · Staff: {StaffDisplay} · Branch: {source.BranchId}"
        };
        if (!string.IsNullOrWhiteSpace(source.OldValue))
        {
            tipLines.Add($"Previous: {RowTextHelper.NormalizeSingleLine(source.OldValue)}");
        }
        tipLines.Add($"Detail: {DetailDisplay}");
        ToolTipText = string.Join(Environment.NewLine, tipLines);
    }

    public AuditLogDto Source { get; }
    public string TimestampDisplay { get; }
    public string DateTimeDisplay { get; }
    public string Action { get; }
    public string EntityNameDisplay { get; }
    public string StaffDisplay { get; }
    public string EntityDisplay { get; }
    public string DetailDisplay { get; }
    public string ToolTipText { get; }
}

/// <summary>
/// Atomic single-line presentation row for the Appointments Directory UniformPagedGrid.
/// </summary>
public sealed class AppointmentRowItem
{
    public AppointmentRowItem(AppointmentQueueItemDto source)
    {
        Source = source;
        var local = source.ScheduledAt.ToLocalTime();
        DateTimeDisplay = local.ToString("dd MMM · HH:mm", CultureInfo.InvariantCulture);
        DurationDisplay = $"{source.DurationMinutes}m";
        VisitTypeDisplay = source.VisitType switch
        {
            VisitType.WalkIn => "Walk-In",
            VisitType.AiBooked => "WhatsApp AI",
            _ => "Booked"
        };
        PatientDisplay = source.HasCriticalMedicalAlert
            ? $"{source.PatientName} [ALERT]"
            : source.PatientName;
        TreatmentDisplay = string.IsNullOrWhiteSpace(source.ProcedureSummary)
            ? "Consultation"
            : RowTextHelper.NormalizeSingleLine(source.ProcedureSummary);
        DentistDisplay = source.DoctorName;
        RoomDisplay = string.IsNullOrWhiteSpace(source.RoomName) ? "—" : source.RoomName;
        ReminderDisplay = AppointmentCalendarHelper.FormatReminderBadgeLabel(source);
        StatusDisplay = AppointmentCalendarHelper.FormatStatusLabel(source.Status);
        TreatmentStatusDisplay = $"{TreatmentDisplay} · {StatusDisplay}";

        var tipLines = new List<string>
        {
            $"{DateTimeDisplay} ({DurationDisplay}) · {VisitTypeDisplay}",
            $"Patient: {source.PatientName}{(string.IsNullOrWhiteSpace(source.PatientIc) ? string.Empty : $" ({source.PatientIc})")}",
            $"Treatment: {TreatmentDisplay}",
            $"Clinician: {DentistDisplay} · Room: {RoomDisplay}",
            $"Reminder: {AppointmentCalendarHelper.FormatReminderDetailSummary(source)} · Status: {StatusDisplay}"
        };
        if (source.HasCriticalMedicalAlert)
        {
            tipLines.Add($"Medical Alert: {source.AlertSummary}");
        }
        if (!string.IsNullOrWhiteSpace(source.Notes))
        {
            tipLines.Add($"Reception Note: {RowTextHelper.NormalizeSingleLine(source.Notes)}");
        }
        tipLines.Add("Double-click to view, reschedule, or cancel appointment");
        ToolTipText = string.Join(Environment.NewLine, tipLines);
    }

    public AppointmentQueueItemDto Source { get; }
    public string DateTimeDisplay { get; }
    public string DurationDisplay { get; }
    public string VisitTypeDisplay { get; }
    public string PatientDisplay { get; }
    public string TreatmentDisplay { get; }
    public string DentistDisplay { get; }
    public string RoomDisplay { get; }
    public string ReminderDisplay { get; }
    public string StatusDisplay { get; }
    public string TreatmentStatusDisplay { get; }
    public string ToolTipText { get; }
}
