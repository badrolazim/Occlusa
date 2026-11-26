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
    CorporatePanel,
    PatientCredit
}

/// <summary>
/// Categorization of billable clinic items controlling doctor commission eligibility.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BillingCatalogCategory
{
    Procedure,
    Radiograph,
    Medication,
    OtcProduct
}

/// <summary>
/// Per-doctor preference controlling how a new draft invoice initializes at patient checkout.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DoctorInvoiceInitMode
{
    AutoPopulateWithSuggestions,
    BlankWithSuggestions,
    BlankManualOnly
}

/// <summary>
/// Explicit cashier choice when tendered amount exceeds invoice balance due.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OverpaymentHandling
{
    None,
    ReturnCashChange,
    StoreAsPatientCredit
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

/// <summary>
/// Patient appointment reminder outreach and confirmation state.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReminderStatus
{
    NotReminded,
    Reminded,
    Confirmed,
    NoReply
}

/// <summary>
/// Communication channel used to remind a patient about an upcoming appointment.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReminderChannel
{
    None,
    WhatsApp,
    PhoneCall,
    Sms
}

/// <summary>
/// Per-user visual density preset for the Live Queue workspace.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LiveQueueViewMode
{
    Simple,
    Detailed
}

/// <summary>
/// Default patient roster scope shown on the Live Queue workspace.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LiveQueueScopeMode
{
    MyQueue,
    AllClinic
}
