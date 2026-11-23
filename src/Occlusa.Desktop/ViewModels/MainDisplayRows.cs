using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.ViewModels;

/// <summary>
/// Concise 4-column presentation row for the Live Queue UniformPagedGrid.
/// </summary>
public sealed class QueueRowItem
{
    public QueueRowItem(AppointmentQueueItemDto source)
    {
        Source = source;
        TimeDisplay = source.ScheduledAt.ToLocalTime().ToString("HH:mm");
        PatientDisplay = source.HasCriticalMedicalAlert
            ? $"{source.PatientName} [ALERT]"
            : source.PatientName;
        DoctorRoomDisplay = string.IsNullOrWhiteSpace(source.RoomName)
            ? source.DoctorName
            : $"{source.DoctorName} · {source.RoomName}";
        StatusDisplay = FormatStatus(source.Status);
    }

    public AppointmentQueueItemDto Source { get; }
    public string TimeDisplay { get; }
    public string PatientDisplay { get; }
    public string DoctorRoomDisplay { get; }
    public string StatusDisplay { get; }

    private static string FormatStatus(QueueStatus status) => status switch
    {
        QueueStatus.AiBookedPending => "AI-Booked (Pending)",
        QueueStatus.InChair => "In Chair",
        QueueStatus.AwaitingCheckout => "Awaiting Checkout",
        _ => status.ToString()
    };
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
        AlertsDisplay = source.MedicalAlerts.HasAnyCriticalAlert
            ? source.MedicalAlerts.BuildAlertSummary()
            : "Clear";
    }

    public PatientDto Source { get; }
    public string IcDisplay { get; }
    public string FullName { get; }
    public string Phone { get; }
    public string AlertsDisplay { get; }
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
        PatientName = source.PatientName;
        PayableDisplay = $"RM {source.PatientPayableAmount:N2}";
        StatusDisplay = source.IsVoided
            ? "Voided"
            : source.BalanceDue <= 0m
                ? "Settled"
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
        ToothDisplay = source.ToothNumber.HasValue ? $"#{source.ToothNumber.Value}" : "General";
        ProcedureName = source.ProcedureName;
        Quantity = source.Quantity;
        LineTotalDisplay = $"RM {source.LineTotal:N2}";
    }

    public InvoiceLineDto Source { get; }
    public string ToothDisplay { get; }
    public string ProcedureName { get; }
    public int Quantity { get; }
    public string LineTotalDisplay { get; }
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
        DateTimeDisplay = $"{local:dd MMM · HH:mm} ({source.DurationMinutes}m)";
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
