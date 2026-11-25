using System.Text.Json.Serialization;

namespace Occlusa.Shared.Models;

/// <summary>
/// Live patient visit workflow states from booking through checkout.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QueueStatus
{
    Scheduled,
    AiBookedPending,
    /// <summary>
    /// Legacy state merged into <see cref="Waiting"/> for current clinic workflows.
    /// </summary>
    Arrived,
    Waiting,
    InChair,
    AwaitingCheckout,
    Completed,
    Cancelled
}

/// <summary>
/// Origin channel of a patient visit on the shared schedule and Live Queue.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VisitType
{
    Booked,
    WalkIn,
    AiBooked
}

/// <summary>
/// Lifecycle stages for an odontogram treatment item.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TreatmentStage
{
    Proposed,
    Accepted,
    Completed
}

/// <summary>
/// Supported settlement methods in Malaysian dental clinics.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PaymentMethod
{
    Cash,
    Card,
    QrTransfer,
    CorporatePanel
}

/// <summary>
/// Clinical status of a tooth in the FDI 32-tooth chart.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ToothCondition
{
    Healthy,
    Caries,
    Filling,
    Crown,
    RootCanal,
    ExtractionNeeded,
    Missing,
    Implant
}

/// <summary>
/// Classification returned by the WhatsApp AI triage classifier.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TriageClassification
{
    RoutineInquiry,
    RoutineBookingEligible,
    ComplexProcedureBlocked,
    EmergencyRedFlag
}

/// <summary>
/// Primary presentation mode inside the Appointments workspace.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppointmentDisplayMode
{
    Calendar,
    Table
}

/// <summary>
/// Date granularity for the Appointments Calendar and Table filter range.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppointmentCalendarViewMode
{
    Day,
    Week,
    Month
}

/// <summary>
/// Non-scrolling slot zoom modes for Day View (all 9 rows per screen covering 09:00-18:00).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DaySlotSession
{
    FullDay,
    Morning,
    Afternoon
}

/// <summary>
/// FDI dentition arch mode on a per-visit odontogram (Permanent 11-48 vs Primary/Deciduous 51-85).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DentitionMode
{
    Permanent,
    Primary
}

/// <summary>
/// Initialization or reset strategy for a visit record's odontogram.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OdontogramInitMode
{
    CarryForward,
    Blank
}
