using Occlusa.Shared.Models;

namespace Occlusa.Shared.Services;

/// <summary>
/// Represents one non-scrolling time row inside the Day View grid.
/// </summary>
public sealed record DayTimeSlot(TimeOnly Start, TimeOnly End, string Label);

/// <summary>
/// Represents the discrete 15-minute or 30-minute timeline window for proportional calendar rendering.
/// </summary>
public sealed record TimelineWindow(TimeOnly Start, TimeOnly End, int SlotMinutes, int TotalRows);

/// <summary>
/// Proportional row/lane placement for one appointment block inside a column timeline.
/// </summary>
public sealed record TimelineBlockLayout(
    AppointmentQueueItemDto Appointment,
    int StartRow,
    int RowSpan,
    int LaneIndex,
    int LaneSpan,
    int LaneCount);

/// <summary>
/// Computed visible blocks and overflow appointments for a single timeline column.
/// </summary>
public sealed record TimelineColumnLayout(
    IReadOnlyList<TimelineBlockLayout> VisibleBlocks,
    IReadOnlyList<AppointmentQueueItemDto> OverflowAppointments);

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
/// Deterministic 5-stage bucket partition backing the 3-Column Live Queue Kanban Board
/// (Waiting, In Chair, Ready for Payment) plus collapsible Scheduled and Completed drawers.
/// </summary>
public sealed record LiveQueueKanbanBuckets(
    IReadOnlyList<AppointmentQueueItemDto> Waiting,
    IReadOnlyList<AppointmentQueueItemDto> InChair,
    IReadOnlyList<AppointmentQueueItemDto> ReadyForPayment,
    IReadOnlyList<AppointmentQueueItemDto> Scheduled,
    IReadOnlyList<AppointmentQueueItemDto> Completed);

/// <summary>
/// One clamped zero-scrollbar page slice of items inside a Kanban column.
/// </summary>
public sealed record KanbanPageSlice<T>(
    IReadOnlyList<T> Items,
    int CurrentPage,
    int TotalPages);

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
    /// Formats a reception note into a single clean line trimmed on word boundaries without ellipsis
    /// for compact display beneath the patient name in Live Queue.
    /// </summary>
    public static string FormatCompactReceptionNote(string? notes, int maxLength = 48)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return string.Empty;
        }

        var words = notes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return string.Empty;
        }

        var normalized = string.Join(' ', words);
        int limit = Math.Max(8, maxLength);
        if (normalized.Length <= limit)
        {
            return normalized;
        }

        var built = new List<string>();
        int currentLen = 0;

        foreach (var word in words)
        {
            int addedLen = built.Count == 0 ? word.Length : word.Length + 1;
            if (currentLen + addedLen > limit)
            {
                break;
            }

            built.Add(word);
            currentLen += addedLen;
        }

        return built.Count > 0 ? string.Join(' ', built) : words[0][..limit];
    }

    /// <summary>
    /// Normalizes legacy <see cref="QueueStatus.Arrived"/> into <see cref="QueueStatus.Waiting"/>.
    /// </summary>
    public static QueueStatus NormalizeQueueStatus(QueueStatus status) =>
        status == QueueStatus.Arrived ? QueueStatus.Waiting : status;

    /// <summary>
    /// Returns true when the visit is finished (<see cref="QueueStatus.Completed"/> or <see cref="QueueStatus.Cancelled"/>).
    /// </summary>
    public static bool IsFinishedQueueStatus(QueueStatus status) =>
        status is QueueStatus.Completed or QueueStatus.Cancelled;

    /// <summary>
    /// Orders today's Live Queue rows by operational priority (Checkout -> In Chair -> Waiting by longest wait ->
    /// AI Pending -> Scheduled), pushing Completed and Cancelled rows to the bottom.
    /// </summary>
    public static IReadOnlyList<AppointmentQueueItemDto> SortLiveQueue(IEnumerable<AppointmentQueueItemDto> items) =>
        items
            .OrderBy(a => GetLiveQueueSortPriority(a.Status))
            .ThenBy(a =>
            {
                var normalized = NormalizeQueueStatus(a.Status);
                return normalized == QueueStatus.Waiting
                    ? (a.ArrivedAt ?? a.ScheduledAt)
                    : a.ScheduledAt;
            })
            .ThenBy(a => a.ScheduledAt)
            .ThenBy(a => a.Id)
            .ToList();

    private static int GetLiveQueueSortPriority(QueueStatus status) => NormalizeQueueStatus(status) switch
    {
        QueueStatus.AwaitingCheckout => 0,
        QueueStatus.InChair => 1,
        QueueStatus.Waiting => 2,
        QueueStatus.AiBookedPending => 3,
        QueueStatus.Scheduled => 4,
        QueueStatus.Completed => 5,
        QueueStatus.Cancelled => 6,
        _ => 7
    };

    /// <summary>
    /// Formats a concise elapsed duration string (e.g. "&lt;1m", "14m", "1h 05m").
    /// </summary>
    public static string FormatElapsedMinutes(DateTimeOffset? since, DateTimeOffset now)
    {
        if (!since.HasValue)
        {
            return string.Empty;
        }

        int totalMins = Math.Max(0, (int)Math.Floor((now - since.Value).TotalMinutes));
        if (totalMins == 0)
        {
            return "<1m";
        }

        if (totalMins < 60)
        {
            return $"{totalMins}m";
        }

        return $"{totalMins / 60}h {totalMins % 60:00}m";
    }

    /// <summary>
    /// Returns true if a waiting patient has exceeded the wait-time threshold.
    /// </summary>
    public static bool IsLongWait(AppointmentQueueItemDto appt, DateTimeOffset now, int thresholdMinutes = 30) =>
        NormalizeQueueStatus(appt.Status) == QueueStatus.Waiting
        && appt.ArrivedAt.HasValue
        && (now - appt.ArrivedAt.Value).TotalMinutes >= thresholdMinutes;

    /// <summary>
    /// Human-readable canonical 5-stage status label used across appointment tables, Live Queue cards, and drawers.
    /// </summary>
    public static string FormatStatusLabel(QueueStatus status) => NormalizeQueueStatus(status) switch
    {
        QueueStatus.AiBookedPending => "AI-Booked Pending",
        QueueStatus.InChair => "In Chair",
        QueueStatus.AwaitingCheckout => "Ready for Payment",
        _ => NormalizeQueueStatus(status).ToString()
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
                {
                    var walkInSuffix = a.VisitType == VisitType.WalkIn ? " (Walk-In)" : string.Empty;
                    return $"{a.ScheduledAt.ToOffset(localOffset):HH:mm} {FormatCompactPatientName(a.PatientName)}{walkInSuffix}";
                }));

            result.Add(new DentistSlotAvailability(slot.Start, slot.End, slot.Label, overlapping.Count, summary));
        }

        return result;
    }

    /// <summary>
    /// Computes the timeline row window (default 15-minute increments = 4 rows per hour) for a session.
    /// </summary>
    public static TimelineWindow GetTimelineWindow(DaySlotSession session, int slotMinutes = 15)
    {
        int step = slotMinutes == 30 ? 30 : 15;
        return session switch
        {
            DaySlotSession.Morning => new TimelineWindow(new TimeOnly(9, 0), new TimeOnly(13, 30), step, 270 / step),
            DaySlotSession.Afternoon => new TimelineWindow(new TimeOnly(13, 30), new TimeOnly(18, 0), step, 270 / step),
            _ => new TimelineWindow(new TimeOnly(9, 0), new TimeOnly(18, 0), step, 540 / step)
        };
    }

    /// <summary>
    /// Computes proportional row spans and non-overlapping side-by-side lane placement for concurrent appointments.
    /// </summary>
    public static TimelineColumnLayout ComputeTimelineLayout(
        IEnumerable<AppointmentQueueItemDto> appointments,
        DaySlotSession session,
        TimeSpan localOffset,
        int slotMinutes = 15,
        int maxVisibleLanes = 0)
    {
        var window = GetTimelineWindow(session, slotMinutes);
        int windowStartMinutes = window.Start.Hour * 60 + window.Start.Minute;
        int windowTotalMinutes = window.TotalRows * window.SlotMinutes;

        var entries = new List<(AppointmentQueueItemDto Appointment, int StartRow, int EndRow, int RowSpan)>();
        foreach (var appt in appointments)
        {
            var localStart = appt.ScheduledAt.ToOffset(localOffset);
            int startMin = (localStart.Hour * 60 + localStart.Minute) - windowStartMinutes;
            int duration = Math.Max(window.SlotMinutes, appt.DurationMinutes);
            int endMin = startMin + duration;

            if (session != DaySlotSession.FullDay && (endMin <= 0 || startMin >= windowTotalMinutes))
            {
                continue;
            }

            int rawStartRow = (int)Math.Floor(startMin / (double)window.SlotMinutes);
            int rawEndRow = (int)Math.Ceiling(endMin / (double)window.SlotMinutes);
            int startRow = Math.Clamp(rawStartRow, 0, window.TotalRows - 1);
            int endRow = Math.Clamp(rawEndRow, startRow + 1, window.TotalRows);
            int rowSpan = Math.Max(1, endRow - startRow);

            entries.Add((appt, startRow, endRow, rowSpan));
        }

        if (entries.Count == 0)
        {
            return new TimelineColumnLayout(
                Array.Empty<TimelineBlockLayout>(),
                Array.Empty<AppointmentQueueItemDto>());
        }

        var sorted = entries
            .OrderBy(e => e.StartRow)
            .ThenByDescending(e => e.RowSpan)
            .ThenBy(e => e.Appointment.ScheduledAt)
            .ThenBy(e => e.Appointment.Id)
            .ToList();

        var visibleBlocks = new List<TimelineBlockLayout>(sorted.Count);
        var overflow = new List<AppointmentQueueItemDto>();

        var cluster = new List<(AppointmentQueueItemDto Appointment, int StartRow, int EndRow, int RowSpan)>();
        int clusterMaxEndRow = 0;

        foreach (var entry in sorted)
        {
            if (cluster.Count == 0)
            {
                cluster.Add(entry);
                clusterMaxEndRow = entry.EndRow;
            }
            else if (entry.StartRow < clusterMaxEndRow)
            {
                cluster.Add(entry);
                if (entry.EndRow > clusterMaxEndRow)
                {
                    clusterMaxEndRow = entry.EndRow;
                }
            }
            else
            {
                LayoutOverlapCluster(cluster, maxVisibleLanes, visibleBlocks, overflow);
                cluster.Clear();
                cluster.Add(entry);
                clusterMaxEndRow = entry.EndRow;
            }
        }

        if (cluster.Count > 0)
        {
            LayoutOverlapCluster(cluster, maxVisibleLanes, visibleBlocks, overflow);
        }

        return new TimelineColumnLayout(visibleBlocks, overflow);
    }

    private static void LayoutOverlapCluster(
        List<(AppointmentQueueItemDto Appointment, int StartRow, int EndRow, int RowSpan)> cluster,
        int maxVisibleLanes,
        List<TimelineBlockLayout> visibleBlocks,
        List<AppointmentQueueItemDto> overflow)
    {
        var laneEndRows = new List<int>();
        var assigned = new List<((AppointmentQueueItemDto Appointment, int StartRow, int EndRow, int RowSpan) Entry, int Lane)>(cluster.Count);

        foreach (var entry in cluster)
        {
            int chosenLane = -1;
            for (int lane = 0; lane < laneEndRows.Count; lane++)
            {
                if (laneEndRows[lane] <= entry.StartRow)
                {
                    chosenLane = lane;
                    laneEndRows[lane] = entry.EndRow;
                    break;
                }
            }

            if (chosenLane < 0)
            {
                chosenLane = laneEndRows.Count;
                laneEndRows.Add(entry.EndRow);
            }

            assigned.Add((entry, chosenLane));
        }

        int clusterLaneCount = laneEndRows.Count;
        int visibleLaneCount = maxVisibleLanes > 0 ? Math.Min(clusterLaneCount, maxVisibleLanes) : clusterLaneCount;

        var visibleInCluster = assigned.Where(a => a.Lane < visibleLaneCount).ToList();
        foreach (var hidden in assigned.Where(a => a.Lane >= visibleLaneCount))
        {
            overflow.Add(hidden.Entry.Appointment);
        }

        foreach (var item in visibleInCluster)
        {
            int laneSpan = 1;
            while (item.Lane + laneSpan < visibleLaneCount)
            {
                int candidateLane = item.Lane + laneSpan;
                bool occupied = visibleInCluster.Any(other =>
                    other.Lane == candidateLane
                    && other.Entry.StartRow < item.Entry.EndRow
                    && item.Entry.StartRow < other.Entry.EndRow);

                if (occupied)
                {
                    break;
                }

                laneSpan++;
            }

            visibleBlocks.Add(new TimelineBlockLayout(
                item.Entry.Appointment,
                item.Entry.StartRow,
                item.Entry.RowSpan,
                item.Lane,
                laneSpan,
                visibleLaneCount));
        }
    }

    /// <summary>
    /// Converts a timeline row index back into a snapped local start time.
    /// </summary>
    public static TimeOnly SnapRowToStartTime(TimelineWindow window, int rowIndex)
    {
        int clamped = Math.Clamp(rowIndex, 0, Math.Max(0, window.TotalRows - 1));
        return window.Start.AddMinutes(clamped * window.SlotMinutes);
    }

    /// <summary>
    /// Verifies that a target dentist column matches the appointment's assigned dentist (prevents cross-dentist drops).
    /// </summary>
    public static bool IsSameDentist(
        AppointmentQueueItemDto appointment,
        Guid? targetDentistId,
        string? targetDentistName)
    {
        if (appointment.DentistId.HasValue && targetDentistId.HasValue)
        {
            return appointment.DentistId.Value == targetDentistId.Value;
        }

        return !string.IsNullOrWhiteSpace(appointment.DoctorName)
            && !string.IsNullOrWhiteSpace(targetDentistName)
            && string.Equals(appointment.DoctorName.Trim(), targetDentistName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds an UpdateAppointmentRequest for drag-rescheduling that preserves dentist, patient, treatment, room, and notes.
    /// </summary>
    public static UpdateAppointmentRequest BuildDragRescheduleRequest(
        AppointmentQueueItemDto appointment,
        DateOnly targetDate,
        TimeOnly targetStartTime,
        TimeSpan localOffset,
        string updatedBy = "Desktop Calendar Drag")
    {
        var scheduledAt = new DateTimeOffset(targetDate.ToDateTime(targetStartTime), localOffset);
        return new UpdateAppointmentRequest
        {
            PatientId = appointment.PatientId,
            DentistId = appointment.DentistId,
            DoctorName = appointment.DoctorName,
            TreatmentId = appointment.TreatmentId,
            ProcedureSummary = appointment.ProcedureSummary,
            RoomName = appointment.RoomName,
            ScheduledAt = scheduledAt,
            DurationMinutes = appointment.DurationMinutes,
            Status = appointment.Status,
            VisitType = appointment.VisitType,
            Notes = appointment.Notes,
            UpdatedBy = updatedBy
        };
    }

    /// <summary>
    /// Normalizes a staff name into a stable case-insensitive preference key.
    /// </summary>
    public static string NormalizeStaffKey(string? staffName) =>
        string.IsNullOrWhiteSpace(staffName)
            ? "default-staff"
            : string.Join(' ', staffName.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>
    /// Returns true if the active staff name matches a practicing dentist profile.
    /// </summary>
    public static bool IsDentistStaff(string? staffName, IReadOnlyList<DentistDto>? dentists)
    {
        if (string.IsNullOrWhiteSpace(staffName))
        {
            return false;
        }

        var trimmed = staffName.Trim();
        if (dentists is not null && dentists.Any(d =>
            string.Equals(d.FullName.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return trimmed.StartsWith("Dr.", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Dr ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true when an appointment belongs to the specified active dentist staff member.
    /// </summary>
    public static bool MatchesDentistStaff(
        AppointmentQueueItemDto appointment,
        string? staffName,
        IReadOnlyList<DentistDto>? dentists)
    {
        if (string.IsNullOrWhiteSpace(staffName))
        {
            return false;
        }

        var trimmed = staffName.Trim();
        var matchedDentist = dentists?.FirstOrDefault(d =>
            string.Equals(d.FullName.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));

        if (matchedDentist is not null && appointment.DentistId.HasValue)
        {
            return appointment.DentistId.Value == matchedDentist.Id;
        }

        return !string.IsNullOrWhiteSpace(appointment.DoctorName)
            && string.Equals(appointment.DoctorName.Trim(), trimmed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds role-smart default Live Queue preferences for a staff member (Simple View by default).
    /// </summary>
    public static UserWorkspacePreferencesDto BuildDefaultUserPreferences(
        string? staffName,
        IReadOnlyList<DentistDto>? dentists = null)
    {
        var displayName = string.IsNullOrWhiteSpace(staffName) ? "Siti (Reception)" : staffName.Trim();
        bool isDentist = IsDentistStaff(displayName, dentists);
        return new UserWorkspacePreferencesDto
        {
            StaffKey = NormalizeStaffKey(displayName),
            StaffDisplayName = displayName,
            QueueViewMode = LiveQueueViewMode.Simple,
            DefaultQueueScope = isDentist ? LiveQueueScopeMode.MyQueue : LiveQueueScopeMode.AllClinic,
            HideFinishedQueueRows = true,
            ShowWaitTimers = false,
            ShowInlineReceptionNotes = false,
            ShowMyKadQuickBar = false,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Applies a 1-click Simple or Detailed view preset onto a user preferences object.
    /// </summary>
    public static void ApplyViewModePreset(
        UserWorkspacePreferencesDto preferences,
        LiveQueueViewMode mode,
        bool isDentist)
    {
        preferences.QueueViewMode = mode;
        if (mode == LiveQueueViewMode.Simple)
        {
            preferences.DefaultQueueScope = isDentist ? LiveQueueScopeMode.MyQueue : LiveQueueScopeMode.AllClinic;
            preferences.HideFinishedQueueRows = true;
            preferences.ShowWaitTimers = false;
            preferences.ShowInlineReceptionNotes = false;
            preferences.ShowMyKadQuickBar = false;
        }
        else
        {
            preferences.DefaultQueueScope = LiveQueueScopeMode.AllClinic;
            preferences.HideFinishedQueueRows = false;
            preferences.ShowWaitTimers = true;
            preferences.ShowInlineReceptionNotes = true;
            preferences.ShowMyKadQuickBar = !isDentist;
        }
    }

    /// <summary>
    /// Returns true when an appointment is an upcoming booked/AI slot that can receive reminders.
    /// </summary>
    public static bool IsUpcomingRemindableAppointment(AppointmentQueueItemDto appointment)
    {
        if (appointment.VisitType == VisitType.WalkIn)
        {
            return false;
        }

        var normalized = NormalizeQueueStatus(appointment.Status);
        return normalized is QueueStatus.Scheduled or QueueStatus.AiBookedPending;
    }

    /// <summary>
    /// Formats the concise Reminder column badge label without text truncation.
    /// </summary>
    public static string FormatReminderBadgeLabel(AppointmentQueueItemDto appointment)
    {
        if (appointment.VisitType == VisitType.WalkIn)
        {
            return "Walk-In";
        }

        var normalized = NormalizeQueueStatus(appointment.Status);
        if (normalized is QueueStatus.Waiting or QueueStatus.InChair or QueueStatus.AwaitingCheckout or QueueStatus.Completed)
        {
            return "Arrived";
        }

        if (normalized == QueueStatus.Cancelled)
        {
            return "—";
        }

        return appointment.ReminderStatus switch
        {
            ReminderStatus.Confirmed => "✓ Confirmed",
            ReminderStatus.Reminded => appointment.ReminderChannel switch
            {
                ReminderChannel.WhatsApp => "Reminded · WA",
                ReminderChannel.PhoneCall => "Reminded · Call",
                ReminderChannel.Sms => "Reminded · SMS",
                _ => "Reminded"
            },
            ReminderStatus.NoReply => "No Reply",
            _ => "Not Reminded"
        };
    }

    /// <summary>
    /// Formats a clean human-readable reminder description for tooltips and AppointmentDetailModal.
    /// </summary>
    public static string FormatReminderDetailSummary(AppointmentQueueItemDto appointment)
    {
        if (appointment.VisitType == VisitType.WalkIn)
        {
            return "Walk-In (no reminder needed)";
        }

        var channelLabel = appointment.ReminderChannel switch
        {
            ReminderChannel.WhatsApp => "WhatsApp",
            ReminderChannel.PhoneCall => "Phone Call",
            ReminderChannel.Sms => "SMS",
            _ => string.Empty
        };

        return appointment.ReminderStatus switch
        {
            ReminderStatus.Confirmed => appointment.ReminderConfirmedAt.HasValue
                ? $"Confirmed{(string.IsNullOrEmpty(channelLabel) ? string.Empty : $" via {channelLabel}")} ({appointment.ReminderConfirmedAt.Value.ToLocalTime():dd MMM HH:mm})"
                : $"Confirmed{(string.IsNullOrEmpty(channelLabel) ? string.Empty : $" via {channelLabel}")}",
            ReminderStatus.Reminded => appointment.RemindedAt.HasValue
                ? $"Reminded{(string.IsNullOrEmpty(channelLabel) ? string.Empty : $" via {channelLabel}")} ({appointment.RemindedAt.Value.ToLocalTime():dd MMM HH:mm}{(string.IsNullOrWhiteSpace(appointment.RemindedBy) ? string.Empty : $" by {appointment.RemindedBy}")})"
                : $"Reminded{(string.IsNullOrEmpty(channelLabel) ? string.Empty : $" via {channelLabel}")}",
            ReminderStatus.NoReply => appointment.RemindedAt.HasValue
                ? $"No Reply ({appointment.RemindedAt.Value.ToLocalTime():dd MMM HH:mm})"
                : "No Reply",
            _ => "Not Reminded yet"
        };
    }

    /// <summary>
    /// Filters and sorts today's Live Queue rows according to Simple/Detailed scope, doctor filter, status chip, and finished-row visibility.
    /// </summary>
    public static IReadOnlyList<AppointmentQueueItemDto> FilterLiveQueue(
        IEnumerable<AppointmentQueueItemDto> todayItems,
        LiveQueueScopeMode scope,
        string? activeStaffName,
        IReadOnlyList<DentistDto>? dentists,
        Guid? detailedDoctorId = null,
        string? detailedDoctorName = null,
        string? statusChip = null,
        bool includeFinished = false)
    {
        IEnumerable<AppointmentQueueItemDto> query = todayItems;

        if (scope == LiveQueueScopeMode.MyQueue && IsDentistStaff(activeStaffName, dentists))
        {
            query = query.Where(a => MatchesDentistStaff(a, activeStaffName, dentists));
        }
        else if (detailedDoctorId.HasValue || !string.IsNullOrWhiteSpace(detailedDoctorName))
        {
            query = query.Where(a =>
            {
                if (detailedDoctorId.HasValue && a.DentistId.HasValue)
                {
                    return a.DentistId.Value == detailedDoctorId.Value;
                }

                return !string.IsNullOrWhiteSpace(detailedDoctorName)
                    && string.Equals(a.DoctorName.Trim(), detailedDoctorName.Trim(), StringComparison.OrdinalIgnoreCase);
            });
        }

        if (!string.IsNullOrWhiteSpace(statusChip) && !string.Equals(statusChip, "All", StringComparison.OrdinalIgnoreCase))
        {
            query = statusChip.Trim().ToLowerInvariant() switch
            {
                "waiting" => query.Where(a => NormalizeQueueStatus(a.Status) == QueueStatus.Waiting),
                "inchair" => query.Where(a => a.Status == QueueStatus.InChair),
                "checkout" => query.Where(a => a.Status == QueueStatus.AwaitingCheckout),
                "unreminded" => query.Where(a => IsUpcomingRemindableAppointment(a) && a.ReminderStatus is ReminderStatus.NotReminded or ReminderStatus.NoReply),
                _ => query
            };
        }

        if (!includeFinished)
        {
            query = query.Where(a => !IsFinishedQueueStatus(a.Status));
        }

        return SortLiveQueue(query);
    }

    /// <summary>
    /// Partitions today's Live Queue into the 3 active Kanban columns (Waiting, In Chair, Ready for Payment)
    /// and the 2 collapsible drawers (Scheduled including AI-Booked Pending, Completed including Cancelled),
    /// applying dentist My Queue scoping, optional doctor filtering, and optional unreminded filtering.
    /// </summary>
    public static LiveQueueKanbanBuckets PartitionLiveQueueKanban(
        IEnumerable<AppointmentQueueItemDto> todayItems,
        LiveQueueScopeMode scope,
        string? activeStaffName,
        IReadOnlyList<DentistDto>? dentists,
        Guid? doctorFilterId = null,
        string? doctorFilterName = null,
        bool scheduledUnremindedOnly = false)
    {
        IEnumerable<AppointmentQueueItemDto> scoped = todayItems;

        if (scope == LiveQueueScopeMode.MyQueue && IsDentistStaff(activeStaffName, dentists))
        {
            scoped = scoped.Where(a => MatchesDentistStaff(a, activeStaffName, dentists));
        }
        else if (doctorFilterId.HasValue || !string.IsNullOrWhiteSpace(doctorFilterName))
        {
            scoped = scoped.Where(a =>
            {
                if (doctorFilterId.HasValue && a.DentistId.HasValue)
                {
                    return a.DentistId.Value == doctorFilterId.Value;
                }

                return !string.IsNullOrWhiteSpace(doctorFilterName)
                    && string.Equals(a.DoctorName.Trim(), doctorFilterName.Trim(), StringComparison.OrdinalIgnoreCase);
            });
        }

        var list = scoped.ToList();

        var waiting = list
            .Where(a => NormalizeQueueStatus(a.Status) == QueueStatus.Waiting)
            .OrderBy(a => a.ArrivedAt ?? a.ScheduledAt)
            .ThenBy(a => a.ScheduledAt)
            .ThenBy(a => a.Id)
            .ToList();

        var inChair = list
            .Where(a => a.Status == QueueStatus.InChair)
            .OrderBy(a => a.SeatedAt ?? a.ScheduledAt)
            .ThenBy(a => a.ScheduledAt)
            .ThenBy(a => a.Id)
            .ToList();

        var readyForPayment = list
            .Where(a => a.Status == QueueStatus.AwaitingCheckout)
            .OrderBy(a => a.CheckoutAt ?? a.ScheduledAt)
            .ThenBy(a => a.ScheduledAt)
            .ThenBy(a => a.Id)
            .ToList();

        IEnumerable<AppointmentQueueItemDto> scheduledQuery = list
            .Where(a => a.Status is QueueStatus.AiBookedPending or QueueStatus.Scheduled);

        if (scheduledUnremindedOnly)
        {
            scheduledQuery = scheduledQuery.Where(a =>
                IsUpcomingRemindableAppointment(a)
                && a.ReminderStatus is ReminderStatus.NotReminded or ReminderStatus.NoReply);
        }

        var scheduled = scheduledQuery
            .OrderBy(a => a.Status == QueueStatus.AiBookedPending ? 0 : 1)
            .ThenBy(a => a.ScheduledAt)
            .ThenBy(a => a.Id)
            .ToList();

        var completed = list
            .Where(a => a.Status is QueueStatus.Completed or QueueStatus.Cancelled)
            .OrderBy(a => a.Status == QueueStatus.Completed ? 0 : 1)
            .ThenByDescending(a => a.CompletedAt ?? a.LastUpdatedAt ?? a.ScheduledAt)
            .ThenBy(a => a.Id)
            .ToList();

        return new LiveQueueKanbanBuckets(waiting, inChair, readyForPayment, scheduled, completed);
    }

    /// <summary>
    /// Computes a clamped zero-scrollbar page slice for a Kanban column.
    /// </summary>
    public static KanbanPageSlice<T> ComputeKanbanPage<T>(
        IReadOnlyList<T> items,
        int requestedPage,
        int pageSize)
    {
        int safePageSize = Math.Max(1, pageSize);
        int totalPages = Math.Max(1, (int)Math.Ceiling(items.Count / (double)safePageSize));
        int clampedPage = Math.Clamp(requestedPage, 1, totalPages);

        if (items.Count == 0)
        {
            return new KanbanPageSlice<T>(Array.Empty<T>(), 1, 1);
        }

        int skip = (clampedPage - 1) * safePageSize;
        var slice = items.Skip(skip).Take(safePageSize).ToList();
        return new KanbanPageSlice<T>(slice, clampedPage, totalPages);
    }

    /// <summary>
    /// Computes how many fixed-height Kanban cards fit inside a column body without introducing scrollbars.
    /// </summary>
    public static int CalculateKanbanCardsPerPage(
        double availableHeight,
        double cardHeight,
        double cardSpacing = 8.0)
    {
        if (double.IsNaN(availableHeight)
            || double.IsInfinity(availableHeight)
            || availableHeight <= 0
            || cardHeight <= 0)
        {
            return 1;
        }

        double spacing = Math.Max(0.0, cardSpacing);
        return Math.Max(1, (int)Math.Floor((availableHeight + spacing) / (cardHeight + spacing)));
    }

    /// <summary>
    /// Builds a customizable bilingual (EN/BM) WhatsApp appointment reminder message for Malaysian clinics.
    /// </summary>
    public static string BuildBilingualReminderMessage(
        AppointmentQueueItemDto appointment,
        string? branchId,
        TimeSpan localOffset)
    {
        var branchLabel = string.Equals(branchId, "PJ-SS2", StringComparison.OrdinalIgnoreCase)
            ? "Occlusa Dental (PJ SS2)"
            : "Occlusa Dental (KL Sentral HQ)";
        var localTime = appointment.ScheduledAt.ToOffset(localOffset);
        var patientFirst = FormatCompactPatientName(appointment.PatientName);
        var proc = string.IsNullOrWhiteSpace(appointment.ProcedureSummary)
            ? "Dental Appointment"
            : appointment.ProcedureSummary.Trim();

        return $"Hi {patientFirst}, reminder of your {proc} with {appointment.DoctorName} at {branchLabel} on {localTime:ddd dd MMM, hh:mm tt}. Reply YES to confirm. / Peringatan temujanji anda pada {localTime:dd MMM, hh:mm tt}. Balas YA untuk sahkan.";
    }

    private static readonly string[] ConfirmationRejectTokens =
    [
        "no", "not", "cannot", "can't", "cant", "won't", "wont", "cancel", "reschedule",
        "postpone", "change", "delay", "sorry", "tak", "tidak", "batal", "tukar", "tunda", "maaf"
    ];

    private static readonly string[] ConfirmationAcceptTokens =
    [
        "yes", "ya", "yep", "yeah", "yup", "confirm", "confirmed", "ok", "okay", "oke",
        "coming", "datang", "hadir", "sahkan", "sah", "baik", "noted", "roger", "sure", "will be there", "see you"
    ];

    /// <summary>
    /// Detects concise patient confirmation replies in English or Bahasa Malaysia while rejecting negations/reschedules.
    /// </summary>
    public static bool IsReminderConfirmationReply(string? messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return false;
        }

        var normalized = messageText.Trim().ToLowerInvariant();
        if (normalized.Length > 80)
        {
            return false;
        }

        var words = normalized
            .Split([' ', '\t', '\r', '\n', ',', '.', '!', '?', ';', ':', '-', '/'], StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
        {
            return false;
        }

        foreach (var reject in ConfirmationRejectTokens)
        {
            if (words.Contains(reject, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        foreach (var accept in ConfirmationAcceptTokens)
        {
            if (accept.Contains(' '))
            {
                if (normalized.Contains(accept, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (words.Contains(accept, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

