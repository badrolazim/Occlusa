using Occlusa.Shared.Models;

namespace Occlusa.Shared.Services;

/// <summary>
/// Represents one non-scrolling time row inside the Day View grid.
/// </summary>
public sealed record DayTimeSlot(TimeOnly Start, TimeOnly End, string Label);

/// <summary>
/// Represents hourly dentist schedule availability for Step 2 of booking.
/// </summary>
public sealed record DentistSlotAvailability(
    TimeOnly Start,
    TimeOnly End,
    string Label,
    int BookedCount,
    string Summary)
{
    public bool IsAvailable => BookedCount == 0;
    public string StatusLabel => BookedCount == 0 ? "Open" : $"Booked ({BookedCount})";
}

/// <summary>
/// Pure deterministic calendar projection, slot mapping, and filtering helpers
/// shared between Occlusa.Desktop and automated unit tests.
/// </summary>
public static class AppointmentCalendarHelper
{
    private static readonly HashSet<string> NameConnectors = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "binti", "bt", "b.", "a/l", "a/p", "s/o", "d/o"
    };

    /// <summary>
    /// Returns the Monday starting the ISO week containing <paramref name="anchorDate"/>.
    /// </summary>
    public static DateOnly GetWeekStart(DateOnly anchorDate)
    {
        int diff = ((int)anchorDate.DayOfWeek + 6) % 7;
        return anchorDate.AddDays(-diff);
    }

    /// <summary>
    /// Computes the inclusive local date range for the requested calendar view mode.
    /// </summary>
    public static (DateOnly Start, DateOnly End) GetDateRange(
        DateOnly anchorDate,
        AppointmentCalendarViewMode mode)
    {
        switch (mode)
        {
            case AppointmentCalendarViewMode.Day:
                return (anchorDate, anchorDate);

            case AppointmentCalendarViewMode.Week:
                var weekStart = GetWeekStart(anchorDate);
                return (weekStart, weekStart.AddDays(6));

            case AppointmentCalendarViewMode.Month:
                var monthStart = new DateOnly(anchorDate.Year, anchorDate.Month, 1);
                int daysInMonth = DateTime.DaysInMonth(anchorDate.Year, anchorDate.Month);
                return (monthStart, monthStart.AddDays(daysInMonth - 1));

            default:
                return (anchorDate, anchorDate);
        }
    }

    /// <summary>
    /// Returns the 7 days (Monday through Sunday) for the week containing <paramref name="anchorDate"/>.
    /// </summary>
    public static IReadOnlyList<DateOnly> GetWeekDates(DateOnly anchorDate)
    {
        var start = GetWeekStart(anchorDate);
        var days = new List<DateOnly>(7);
        for (int i = 0; i < 7; i++)
        {
            days.Add(start.AddDays(i));
        }

        return days;
    }

    /// <summary>
    /// Returns a Monday-aligned 5-week (35) or 6-week (42) grid covering the month of <paramref name="anchorDate"/>.
    /// </summary>
    public static IReadOnlyList<DateOnly> GetMonthGridDates(DateOnly anchorDate)
    {
        var firstOfMonth = new DateOnly(anchorDate.Year, anchorDate.Month, 1);
        int leadingDays = ((int)firstOfMonth.DayOfWeek + 6) % 7;
        var gridStart = firstOfMonth.AddDays(-leadingDays);
        int daysInMonth = DateTime.DaysInMonth(anchorDate.Year, anchorDate.Month);
        int totalCells = leadingDays + daysInMonth <= 35 ? 35 : 42;

        var cells = new List<DateOnly>(totalCells);
        for (int i = 0; i < totalCells; i++)
        {
            cells.Add(gridStart.AddDays(i));
        }

        return cells;
    }

    /// <summary>
    /// Returns exactly 9 fixed non-scrolling time slots covering clinic operating hours (09:00-18:00).
    /// </summary>
    public static IReadOnlyList<DayTimeSlot> GetDayTimeSlots(DaySlotSession session)
    {
        var slots = new List<DayTimeSlot>(9);

        switch (session)
        {
            case DaySlotSession.Morning:
                for (int i = 0; i < 9; i++)
                {
                    var start = new TimeOnly(9, 0).AddMinutes(i * 30);
                    slots.Add(new DayTimeSlot(start, start.AddMinutes(30), start.ToString("HH:mm")));
                }
                break;

            case DaySlotSession.Afternoon:
                for (int i = 0; i < 9; i++)
                {
                    var start = new TimeOnly(13, 30).AddMinutes(i * 30);
                    slots.Add(new DayTimeSlot(start, start.AddMinutes(30), start.ToString("HH:mm")));
                }
                break;

            default:
                for (int hour = 9; hour <= 17; hour++)
                {
                    var start = new TimeOnly(hour, 0);
                    slots.Add(new DayTimeSlot(start, start.AddHours(1), start.ToString("HH:mm")));
                }
                break;
        }

        return slots;
    }

    /// <summary>
    /// Determines whether an appointment scheduled time belongs in the given Day View slot.
    /// Early-morning (&lt;09:00) and late-evening (&gt;=18:00) entries are anchored to edge slots in FullDay
    /// so no appointments are silently hidden without scrollbars.
    /// </summary>
    public static bool DoesAppointmentMatchSlot(
        DateTimeOffset scheduledAt,
        TimeSpan localOffset,
        DayTimeSlot slot,
        DaySlotSession session)
    {
        var localTime = TimeOnly.FromDateTime(scheduledAt.ToOffset(localOffset).DateTime);

        if (session == DaySlotSession.FullDay)
        {
            if (slot.Start == new TimeOnly(9, 0) && localTime < slot.Start)
            {
                return true;
            }

            if (slot.Start == new TimeOnly(17, 0) && localTime >= slot.End)
            {
                return true;
            }
        }
        else if (session == DaySlotSession.Morning)
        {
            if (slot.Start == new TimeOnly(9, 0) && localTime < slot.Start)
            {
                return true;
            }
        }
        else if (session == DaySlotSession.Afternoon)
        {
            if (slot.Start == new TimeOnly(17, 30) && localTime >= slot.End)
            {
                return true;
            }
        }

        return localTime >= slot.Start && localTime < slot.End;
    }

    /// <summary>
    /// Filters appointments by inclusive local date range, Dentist, Treatment, and Patient search text.
    /// </summary>
    public static IReadOnlyList<AppointmentQueueItemDto> FilterAppointments(
        IEnumerable<AppointmentQueueItemDto> source,
        DateOnly? startDate,
        DateOnly? endDate,
        Guid? dentistId,
        Guid? treatmentId,
        string? patientSearch,
        TimeSpan localOffset,
        string? dentistNameFallback = null,
        string? treatmentNameFallback = null)
    {
        var trimmedSearch = patientSearch?.Trim() ?? string.Empty;

        return source
            .Where(a =>
            {
                var localDate = DateOnly.FromDateTime(a.ScheduledAt.ToOffset(localOffset).DateTime);
                if (startDate.HasValue && localDate < startDate.Value)
                {
                    return false;
                }

                if (endDate.HasValue && localDate > endDate.Value)
                {
                    return false;
                }

                if (dentistId.HasValue)
                {
                    bool idMatches = a.DentistId == dentistId.Value;
                    bool nameMatches = !a.DentistId.HasValue
                        && !string.IsNullOrWhiteSpace(dentistNameFallback)
                        && string.Equals(a.DoctorName.Trim(), dentistNameFallback.Trim(), StringComparison.OrdinalIgnoreCase);
                    if (!idMatches && !nameMatches)
                    {
                        return false;
                    }
                }

                if (treatmentId.HasValue)
                {
                    bool idMatches = a.TreatmentId == treatmentId.Value;
                    bool nameMatches = !a.TreatmentId.HasValue
                        && !string.IsNullOrWhiteSpace(treatmentNameFallback)
                        && a.ProcedureSummary.Contains(treatmentNameFallback.Trim(), StringComparison.OrdinalIgnoreCase);
                    if (!idMatches && !nameMatches)
                    {
                        return false;
                    }
                }

                if (!string.IsNullOrEmpty(trimmedSearch))
                {
                    bool patientMatches = a.PatientName.Contains(trimmedSearch, StringComparison.OrdinalIgnoreCase)
                        || a.PatientIc.Contains(trimmedSearch, StringComparison.OrdinalIgnoreCase);
                    if (!patientMatches)
                    {
                        return false;
                    }
                }

                return true;
            })
            .OrderBy(a => a.ScheduledAt)
            .ToList();
    }

    /// <summary>
    /// Returns true if an appointment belongs on today's Live Queue floor board.
    /// </summary>
    public static bool IsTodayForLiveQueue(
        AppointmentQueueItemDto appt,
        DateOnly localToday,
        TimeSpan localOffset)
    {
        var localDate = DateOnly.FromDateTime(appt.ScheduledAt.ToOffset(localOffset).DateTime);
        if (localDate == localToday)
        {
            return true;
        }

        return appt.Status is QueueStatus.Arrived
            or QueueStatus.Waiting
            or QueueStatus.InChair
            or QueueStatus.AwaitingCheckout;
    }

    /// <summary>
    /// Produces a concise patient display name without ellipsis truncation.
    /// </summary>
    public static string FormatCompactPatientName(string? fullName, bool shortForm = false)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return "Patient";
        }

        var tokens = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return "Patient";
        }

        var meaningful = tokens.Where(t => !NameConnectors.Contains(t)).Take(3).ToList();
        if (meaningful.Count == 0)
        {
            meaningful = tokens.Take(2).ToList();
        }

        if (shortForm)
        {
            if (meaningful.Count >= 2)
            {
                return $"{meaningful[0]} {char.ToUpperInvariant(meaningful[1][0])}.";
            }

            return meaningful[0];
        }

        var joined = string.Join(' ', meaningful);
        if (joined.Length <= 18)
        {
            return joined;
        }

        return meaningful.Count >= 2 ? $"{meaningful[0]} {meaningful[1]}" : meaningful[0];
    }

    /// <summary>
    /// Produces a concise dentist label (e.g. "Dr. Aina") without ellipsis truncation.
    /// </summary>
    public static string FormatCompactDentistName(string? doctorName)
    {
        if (string.IsNullOrWhiteSpace(doctorName))
        {
            return "Dentist";
        }

        var tokens = doctorName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length >= 2 && tokens[0].StartsWith("Dr", StringComparison.OrdinalIgnoreCase))
        {
            return $"{tokens[0]} {tokens[1]}";
        }

        return tokens[0];
    }

    /// <summary>
    /// Trims a treatment label cleanly on word boundaries without emitting ellipsis.
    /// </summary>
    public static string FormatCompactTreatment(string? procedureSummary, int maxLength = 22)
    {
        if (string.IsNullOrWhiteSpace(procedureSummary))
        {
            return "Consultation";
        }

        var normalized = string.Join(' ', procedureSummary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var built = new List<string>();
        int currentLen = 0;

        foreach (var word in words)
        {
            int addedLen = built.Count == 0 ? word.Length : word.Length + 1;
            if (currentLen + addedLen > maxLength)
            {
                break;
            }

            built.Add(word);
            currentLen += addedLen;
        }

        return built.Count > 0 ? string.Join(' ', built) : words[0];
    }

    /// <summary>
    /// Human-readable short status label used across appointment tables and cards.
    /// </summary>
    public static string FormatStatusLabel(QueueStatus status) => status switch
    {
        QueueStatus.AiBookedPending => "AI-Booked",
        QueueStatus.InChair => "In Chair",
        QueueStatus.AwaitingCheckout => "Checkout",
        _ => status.ToString()
    };

    /// <summary>
    /// Returns true if the dentist supports the selected treatment (or when no capabilities/filter are set).
    /// </summary>
    public static bool CanDentistPerformTreatment(DentistDto dentist, Guid? treatmentId)
    {
        if (!treatmentId.HasValue || dentist.SupportedTreatmentIds.Count == 0)
        {
            return true;
        }

        return dentist.SupportedTreatmentIds.Contains(treatmentId.Value);
    }

    /// <summary>
    /// Filters active dentists eligible to perform the specified treatment.
    /// </summary>
    public static IReadOnlyList<DentistDto> GetEligibleDentists(
        IEnumerable<DentistDto> dentists,
        Guid? treatmentId) =>
        dentists.Where(d => d.IsActive && CanDentistPerformTreatment(d, treatmentId)).ToList();

    /// <summary>
    /// Matches a patient flexibly by IC number (with or without dashes), full name, or phone digits.
    /// </summary>
    public static bool MatchesPatientQuery(PatientDto patient, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var trimmed = query.Trim();
        if (patient.FullName.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
            || patient.IcNumber.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
            || patient.Phone.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var queryDigits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());
        if (queryDigits.Length >= 3)
        {
            var icDigits = new string(patient.IcNumber.Where(char.IsAsciiDigit).ToArray());
            var phoneDigits = new string(patient.Phone.Where(char.IsAsciiDigit).ToArray());
            if (icDigits.Contains(queryDigits, StringComparison.Ordinal)
                || phoneDigits.Contains(queryDigits, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Computes hourly (09:00-17:00) schedule availability for a dentist on a given local date.
    /// Cancelled appointments do not block slots.
    /// </summary>
    public static IReadOnlyList<DentistSlotAvailability> GetDentistDaySlotAvailability(
        IEnumerable<AppointmentQueueItemDto> appointments,
        Guid? dentistId,
        string? dentistName,
        DateOnly date,
        TimeSpan localOffset)
    {
        var slots = GetDayTimeSlots(DaySlotSession.FullDay);
        var dayMatches = appointments
            .Where(a => a.Status != QueueStatus.Cancelled)
            .Where(a =>
            {
                var localDate = DateOnly.FromDateTime(a.ScheduledAt.ToOffset(localOffset).DateTime);
                if (localDate != date)
                {
                    return false;
                }

                if (dentistId.HasValue && a.DentistId == dentistId.Value)
                {
                    return true;
                }

                return !string.IsNullOrWhiteSpace(dentistName)
                    && string.Equals(a.DoctorName.Trim(), dentistName.Trim(), StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        var result = new List<DentistSlotAvailability>(slots.Count);
        foreach (var slot in slots)
        {
            var overlapping = dayMatches.Where(a =>
            {
                if (DoesAppointmentMatchSlot(a.ScheduledAt, localOffset, slot, DaySlotSession.FullDay))
                {
                    return true;
                }

                var startLocal = a.ScheduledAt.ToOffset(localOffset);
                var endLocal = a.EndsAt.ToOffset(localOffset);
                var startDate = DateOnly.FromDateTime(startLocal.DateTime);
                var endDate = DateOnly.FromDateTime(endLocal.DateTime);
                var apptStart = TimeOnly.FromDateTime(startLocal.DateTime);
                var apptEnd = endDate > startDate
                    ? new TimeOnly(23, 59, 59)
                    : TimeOnly.FromDateTime(endLocal.DateTime);

                return apptStart < slot.End && apptEnd > slot.Start;
            }).ToList();

            var summary = overlapping.Count == 0
                ? "Available"
                : string.Join(", ", overlapping.Select(a =>
                    $"{a.ScheduledAt.ToOffset(localOffset):HH:mm} {FormatCompactPatientName(a.PatientName)}"));

            result.Add(new DentistSlotAvailability(slot.Start, slot.End, slot.Label, overlapping.Count, summary));
        }

        return result;
    }
}

