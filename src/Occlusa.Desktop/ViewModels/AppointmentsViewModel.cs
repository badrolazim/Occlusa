using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.ViewModels;

public sealed record FilterOptionVm(Guid? Id, string Label);

public sealed record QuickBookSlotArgs(
    DateOnly Date,
    TimeOnly StartTime,
    Guid? DentistId,
    string? DoctorName,
    string? DefaultRoom,
    Guid? TreatmentId = null);

public sealed class CalendarAppointmentCardVm
{
    public CalendarAppointmentCardVm(
        AppointmentQueueItemDto source,
        string timeLabel,
        string primaryLine,
        string secondaryLine,
        string toolTipText)
    {
        Source = source;
        TimeLabel = timeLabel;
        PrimaryLine = primaryLine;
        SecondaryLine = secondaryLine;
        ToolTipText = toolTipText;
    }

    public AppointmentQueueItemDto Source { get; }
    public string TimeLabel { get; }
    public string PrimaryLine { get; }
    public string SecondaryLine { get; }
    public bool HasSecondaryLine => !string.IsNullOrWhiteSpace(SecondaryLine);
    public string ToolTipText { get; }
}

public sealed class DayDentistHeaderVm
{
    public DayDentistHeaderVm(Guid? dentistId, string fullName, string specialty, string defaultRoom, int dayCount)
    {
        DentistId = dentistId;
        FullName = fullName;
        Subtitle = string.IsNullOrWhiteSpace(specialty)
            ? defaultRoom
            : $"{specialty} · {defaultRoom}";
        DefaultRoom = defaultRoom;
        CountLabel = dayCount == 1 ? "1 appt" : $"{dayCount} appts";
    }

    public Guid? DentistId { get; }
    public string FullName { get; }
    public string Subtitle { get; }
    public string DefaultRoom { get; }
    public string CountLabel { get; }
}

public sealed class DaySlotCellVm
{
    public DaySlotCellVm(
        DateOnly date,
        TimeOnly startTime,
        Guid? dentistId,
        string dentistName,
        string defaultRoom,
        IReadOnlyList<CalendarAppointmentCardVm> visibleCards,
        int overflowCount)
    {
        Date = date;
        StartTime = startTime;
        DentistId = dentistId;
        DentistName = dentistName;
        DefaultRoom = defaultRoom;
        VisibleCards = visibleCards;
        OverflowCount = overflowCount;
    }

    public DateOnly Date { get; }
    public TimeOnly StartTime { get; }
    public Guid? DentistId { get; }
    public string DentistName { get; }
    public string DefaultRoom { get; }
    public IReadOnlyList<CalendarAppointmentCardVm> VisibleCards { get; }
    public int OverflowCount { get; }
    public bool HasOverflow => OverflowCount > 0;
    public string OverflowLabel => $"+{OverflowCount} more";
    public bool IsEmpty => VisibleCards.Count == 0 && !HasOverflow;
}

public sealed class DaySlotRowVm
{
    public DaySlotRowVm(string timeLabel, TimeOnly startTime, IReadOnlyList<DaySlotCellVm> cells)
    {
        TimeLabel = timeLabel;
        StartTime = startTime;
        Cells = cells;
    }

    public string TimeLabel { get; }
    public TimeOnly StartTime { get; }
    public IReadOnlyList<DaySlotCellVm> Cells { get; }
}

public sealed class WeekDayColumnVm
{
    public WeekDayColumnVm(
        DateOnly date,
        bool isToday,
        bool isSunday,
        int totalCount,
        IReadOnlyList<CalendarAppointmentCardVm> visibleCards,
        int overflowCount)
    {
        Date = date;
        DayNameLabel = date.ToString("ddd", CultureInfo.InvariantCulture);
        DateLabel = date.ToString("dd MMM", CultureInfo.InvariantCulture);
        IsToday = isToday;
        IsSunday = isSunday;
        TotalCount = totalCount;
        CountLabel = totalCount == 0 ? string.Empty : totalCount.ToString(CultureInfo.InvariantCulture);
        VisibleCards = visibleCards;
        OverflowCount = overflowCount;
    }

    public DateOnly Date { get; }
    public string DayNameLabel { get; }
    public string DateLabel { get; }
    public bool IsToday { get; }
    public bool IsSunday { get; }
    public int TotalCount { get; }
    public string CountLabel { get; }
    public bool HasCount => TotalCount > 0;
    public IReadOnlyList<CalendarAppointmentCardVm> VisibleCards { get; }
    public int OverflowCount { get; }
    public bool HasOverflow => OverflowCount > 0;
    public string OverflowLabel => $"+{OverflowCount} more";
}

public sealed class MonthDayCellVm
{
    public MonthDayCellVm(
        DateOnly date,
        bool isCurrentMonth,
        bool isToday,
        bool isSunday,
        int totalCount,
        IReadOnlyList<CalendarAppointmentCardVm> visibleCards,
        int overflowCount)
    {
        Date = date;
        DayNumberText = date.Day == 1
            ? date.ToString("d MMM", CultureInfo.InvariantCulture)
            : date.Day.ToString(CultureInfo.InvariantCulture);
        IsCurrentMonth = isCurrentMonth;
        IsToday = isToday;
        IsSunday = isSunday;
        TotalCount = totalCount;
        VisibleCards = visibleCards;
        OverflowCount = overflowCount;
    }

    public DateOnly Date { get; }
    public string DayNumberText { get; }
    public bool IsCurrentMonth { get; }
    public bool IsToday { get; }
    public bool IsSunday { get; }
    public int TotalCount { get; }
    public IReadOnlyList<CalendarAppointmentCardVm> VisibleCards { get; }
    public int OverflowCount { get; }
    public bool HasOverflow => OverflowCount > 0;
    public string OverflowLabel => $"+{OverflowCount} more";
}

/// <summary>
/// Workspace ViewModel managing the Appointments Calendar (Day / Week / Month)
/// and paginated 4-column Appointment Table with Dentist, Treatment, and Patient filters.
/// </summary>
public partial class AppointmentsViewModel : ObservableObject
{
    private const double ResolutionHeightThreshold = 1120.0;

    private List<AppointmentQueueItemDto> _allAppointments = new();
    private List<DentistDto> _dentists = new();
    private List<TreatmentDto> _treatments = new();
    private bool _suppressFilterRebuild;
    private int _maxWeekCardsPerDay = 6;
    private int _maxMonthCardsPerDay = 2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCalendarMode))]
    [NotifyPropertyChangedFor(nameof(IsTableMode))]
    [NotifyPropertyChangedFor(nameof(IsDayCalendarView))]
    [NotifyPropertyChangedFor(nameof(IsWeekCalendarView))]
    [NotifyPropertyChangedFor(nameof(IsMonthCalendarView))]
    [NotifyPropertyChangedFor(nameof(IsRangeDayChecked))]
    [NotifyPropertyChangedFor(nameof(IsRangeWeekChecked))]
    [NotifyPropertyChangedFor(nameof(IsRangeMonthChecked))]
    [NotifyPropertyChangedFor(nameof(IsRangeAllChecked))]
    [NotifyPropertyChangedFor(nameof(ActiveRangeTitle))]
    private AppointmentDisplayMode _displayMode = AppointmentDisplayMode.Calendar;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDayCalendarView))]
    [NotifyPropertyChangedFor(nameof(IsWeekCalendarView))]
    [NotifyPropertyChangedFor(nameof(IsMonthCalendarView))]
    [NotifyPropertyChangedFor(nameof(IsRangeDayChecked))]
    [NotifyPropertyChangedFor(nameof(IsRangeWeekChecked))]
    [NotifyPropertyChangedFor(nameof(IsRangeMonthChecked))]
    [NotifyPropertyChangedFor(nameof(IsRangeAllChecked))]
    [NotifyPropertyChangedFor(nameof(ActiveRangeTitle))]
    private AppointmentCalendarViewMode _calendarViewMode = AppointmentCalendarViewMode.Week;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRangeDayChecked))]
    [NotifyPropertyChangedFor(nameof(IsRangeWeekChecked))]
    [NotifyPropertyChangedFor(nameof(IsRangeMonthChecked))]
    [NotifyPropertyChangedFor(nameof(IsRangeAllChecked))]
    [NotifyPropertyChangedFor(nameof(ActiveRangeTitle))]
    private bool _showAllDatesInTable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFullDaySession))]
    [NotifyPropertyChangedFor(nameof(IsMorningSession))]
    [NotifyPropertyChangedFor(nameof(IsAfternoonSession))]
    private DaySlotSession _daySession = DaySlotSession.FullDay;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveRangeTitle))]
    private DateOnly _anchorDate = DateOnly.FromDateTime(DateTime.Now);

    [ObservableProperty]
    private FilterOptionVm? _selectedDentistFilter;

    [ObservableProperty]
    private FilterOptionVm? _selectedTreatmentFilter;

    [ObservableProperty]
    private string _patientFilterText = string.Empty;

    [ObservableProperty]
    private bool _hasActiveFilters;

    [ObservableProperty]
    private AppointmentRowItem? _selectedAppointmentRow;

    [ObservableProperty]
    private int _monthRowCount = 5;

    [ObservableProperty]
    private string _filteredSummaryLabel = "0 appointments";

    public AppointmentsViewModel()
    {
        var allDentists = new FilterOptionVm(null, "All Dentists");
        var allTreatments = new FilterOptionVm(null, "All Treatments");

        DentistFilterOptions = new ObservableCollection<FilterOptionVm> { allDentists };
        TreatmentFilterOptions = new ObservableCollection<FilterOptionVm> { allTreatments };

        _suppressFilterRebuild = true;
        _selectedDentistFilter = allDentists;
        _selectedTreatmentFilter = allTreatments;
        _suppressFilterRebuild = false;

        TableRows = new ObservableCollection<AppointmentRowItem>();
        DayDentistHeaders = new ObservableCollection<DayDentistHeaderVm>();
        DayTimeRows = new ObservableCollection<DaySlotRowVm>();
        WeekDayColumns = new ObservableCollection<WeekDayColumnVm>();
        MonthDayCells = new ObservableCollection<MonthDayCellVm>();
    }

    public event EventHandler<AppointmentQueueItemDto>? AppointmentOpenRequested;
    public event EventHandler<QuickBookSlotArgs>? QuickBookRequested;
    public event EventHandler<AppointmentQueueItemDto>? AppointmentSelectedForBanner;

    public ObservableCollection<FilterOptionVm> DentistFilterOptions { get; }
    public ObservableCollection<FilterOptionVm> TreatmentFilterOptions { get; }
    public ObservableCollection<AppointmentRowItem> TableRows { get; }
    public ObservableCollection<DayDentistHeaderVm> DayDentistHeaders { get; }
    public ObservableCollection<DaySlotRowVm> DayTimeRows { get; }
    public ObservableCollection<WeekDayColumnVm> WeekDayColumns { get; }
    public ObservableCollection<MonthDayCellVm> MonthDayCells { get; }

    public bool IsCalendarMode => DisplayMode == AppointmentDisplayMode.Calendar;
    public bool IsTableMode => DisplayMode == AppointmentDisplayMode.Table;

    public bool IsDayCalendarView => IsCalendarMode && CalendarViewMode == AppointmentCalendarViewMode.Day;
    public bool IsWeekCalendarView => IsCalendarMode && CalendarViewMode == AppointmentCalendarViewMode.Week;
    public bool IsMonthCalendarView => IsCalendarMode && CalendarViewMode == AppointmentCalendarViewMode.Month;

    public bool IsRangeDayChecked => (IsCalendarMode || !ShowAllDatesInTable) && CalendarViewMode == AppointmentCalendarViewMode.Day;
    public bool IsRangeWeekChecked => (IsCalendarMode || !ShowAllDatesInTable) && CalendarViewMode == AppointmentCalendarViewMode.Week;
    public bool IsRangeMonthChecked => (IsCalendarMode || !ShowAllDatesInTable) && CalendarViewMode == AppointmentCalendarViewMode.Month;
    public bool IsRangeAllChecked => IsTableMode && ShowAllDatesInTable;

    public bool IsFullDaySession => DaySession == DaySlotSession.FullDay;
    public bool IsMorningSession => DaySession == DaySlotSession.Morning;
    public bool IsAfternoonSession => DaySession == DaySlotSession.Afternoon;

    public string ActiveRangeTitle
    {
        get
        {
            if (IsTableMode && ShowAllDatesInTable)
            {
                return "All Scheduled Dates";
            }

            return CalendarViewMode switch
            {
                AppointmentCalendarViewMode.Day => AnchorDate.ToString("ddd, dd MMM yyyy", CultureInfo.InvariantCulture),
                AppointmentCalendarViewMode.Week => FormatWeekRange(AnchorDate),
                _ => AnchorDate.ToString("MMMM yyyy", CultureInfo.InvariantCulture)
            };
        }
    }

    public void UpdateData(
        IReadOnlyList<AppointmentQueueItemDto> appointments,
        IReadOnlyList<DentistDto> dentists,
        IReadOnlyList<TreatmentDto> treatments)
    {
        _allAppointments = appointments.ToList();
        _dentists = dentists.ToList();
        _treatments = treatments.ToList();

        RebuildFilterOptions();
        RebuildViews();
    }

    public void UpdateViewportCapacity(double hostHeight)
    {
        int newWeekCap = hostHeight > ResolutionHeightThreshold ? 9 : 6;
        int newMonthCap = hostHeight > ResolutionHeightThreshold ? 3 : 2;
        if (newWeekCap == _maxWeekCardsPerDay && newMonthCap == _maxMonthCardsPerDay)
        {
            return;
        }

        _maxWeekCardsPerDay = newWeekCap;
        _maxMonthCardsPerDay = newMonthCap;
        RebuildViews();
    }

    partial void OnDisplayModeChanged(AppointmentDisplayMode value) => RebuildViews();
    partial void OnCalendarViewModeChanged(AppointmentCalendarViewMode value) => RebuildViews();
    partial void OnShowAllDatesInTableChanged(bool value) => RebuildViews();
    partial void OnDaySessionChanged(DaySlotSession value) => RebuildDayView();
    partial void OnAnchorDateChanged(DateOnly value) => RebuildViews();

    partial void OnSelectedDentistFilterChanged(FilterOptionVm? value)
    {
        if (!_suppressFilterRebuild)
        {
            RebuildViews();
        }
    }

    partial void OnSelectedTreatmentFilterChanged(FilterOptionVm? value)
    {
        if (!_suppressFilterRebuild)
        {
            RebuildDentistFilterOptionsForTreatment();
            RebuildViews();
        }
    }

    partial void OnPatientFilterTextChanged(string value)
    {
        if (!_suppressFilterRebuild)
        {
            RebuildViews();
        }
    }

    partial void OnSelectedAppointmentRowChanged(AppointmentRowItem? value)
    {
        if (value != null)
        {
            AppointmentSelectedForBanner?.Invoke(this, value.Source);
        }
    }

    [RelayCommand]
    private void SetDisplayMode(string modeName)
    {
        if (Enum.TryParse<AppointmentDisplayMode>(modeName, out var mode))
        {
            DisplayMode = mode;
        }
    }

    [RelayCommand]
    private void SetRangeMode(string rangeName)
    {
        if (string.Equals(rangeName, "All", StringComparison.OrdinalIgnoreCase))
        {
            ShowAllDatesInTable = true;
            return;
        }

        if (Enum.TryParse<AppointmentCalendarViewMode>(rangeName, out var viewMode))
        {
            ShowAllDatesInTable = false;
            CalendarViewMode = viewMode;
        }
    }

    [RelayCommand]
    private void SetDaySession(string sessionName)
    {
        if (Enum.TryParse<DaySlotSession>(sessionName, out var session))
        {
            DaySession = session;
        }
    }

    [RelayCommand]
    private void PrevRange()
    {
        if (IsTableMode && ShowAllDatesInTable)
        {
            ShowAllDatesInTable = false;
        }

        AnchorDate = CalendarViewMode switch
        {
            AppointmentCalendarViewMode.Day => AnchorDate.AddDays(-1),
            AppointmentCalendarViewMode.Week => AnchorDate.AddDays(-7),
            _ => AnchorDate.AddMonths(-1)
        };
    }

    [RelayCommand]
    private void NextRange()
    {
        if (IsTableMode && ShowAllDatesInTable)
        {
            ShowAllDatesInTable = false;
        }

        AnchorDate = CalendarViewMode switch
        {
            AppointmentCalendarViewMode.Day => AnchorDate.AddDays(1),
            AppointmentCalendarViewMode.Week => AnchorDate.AddDays(7),
            _ => AnchorDate.AddMonths(1)
        };
    }

    [RelayCommand]
    private void Today()
    {
        if (IsTableMode && ShowAllDatesInTable)
        {
            ShowAllDatesInTable = false;
        }

        AnchorDate = DateOnly.FromDateTime(DateTime.Now);
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _suppressFilterRebuild = true;
        SelectedTreatmentFilter = TreatmentFilterOptions.FirstOrDefault();
        _suppressFilterRebuild = false;
        RebuildDentistFilterOptionsForTreatment();

        _suppressFilterRebuild = true;
        SelectedDentistFilter = DentistFilterOptions.FirstOrDefault();
        PatientFilterText = string.Empty;
        _suppressFilterRebuild = false;

        RebuildViews();
    }

    [RelayCommand]
    private void JumpToDay(DateOnly date)
    {
        _suppressFilterRebuild = true;
        ShowAllDatesInTable = false;
        _suppressFilterRebuild = false;
        AnchorDate = date;
        CalendarViewMode = AppointmentCalendarViewMode.Day;
        DisplayMode = AppointmentDisplayMode.Calendar;
    }

    [RelayCommand]
    private void ShowSlotOverflowInTable(DaySlotCellVm? cell)
    {
        if (cell == null)
        {
            return;
        }

        _suppressFilterRebuild = true;
        AnchorDate = cell.Date;
        CalendarViewMode = AppointmentCalendarViewMode.Day;
        ShowAllDatesInTable = false;
        if (cell.DentistId.HasValue)
        {
            var match = DentistFilterOptions.FirstOrDefault(o => o.Id == cell.DentistId);
            if (match != null)
            {
                SelectedDentistFilter = match;
            }
        }
        _suppressFilterRebuild = false;

        DisplayMode = AppointmentDisplayMode.Table;
    }

    [RelayCommand]
    private void OpenAppointment(AppointmentQueueItemDto? appt)
    {
        if (appt == null)
        {
            return;
        }

        AppointmentSelectedForBanner?.Invoke(this, appt);
        AppointmentOpenRequested?.Invoke(this, appt);
    }

    [RelayCommand]
    private void QuickBookFromDayCell(DaySlotCellVm? cell)
    {
        if (cell == null)
        {
            return;
        }

        QuickBookRequested?.Invoke(this, new QuickBookSlotArgs(
            cell.Date,
            cell.StartTime,
            cell.DentistId,
            cell.DentistName,
            cell.DefaultRoom,
            SelectedTreatmentFilter?.Id));
    }

    [RelayCommand]
    private void QuickBookFromDate(DateOnly date)
    {
        var eligible = AppointmentCalendarHelper.GetEligibleDentists(_dentists, SelectedTreatmentFilter?.Id);
        var dentist = SelectedDentistFilter?.Id is Guid did
            ? eligible.FirstOrDefault(d => d.Id == did) ?? _dentists.FirstOrDefault(d => d.Id == did)
            : eligible.FirstOrDefault() ?? _dentists.FirstOrDefault();

        QuickBookRequested?.Invoke(this, new QuickBookSlotArgs(
            date,
            new TimeOnly(9, 0),
            dentist?.Id,
            dentist?.FullName,
            dentist?.DefaultRoom,
            SelectedTreatmentFilter?.Id));
    }

    private void RebuildFilterOptions()
    {
        Guid? prevTreatmentId = SelectedTreatmentFilter?.Id;

        _suppressFilterRebuild = true;

        TreatmentFilterOptions.Clear();
        var allTreatments = new FilterOptionVm(null, "All Treatments");
        TreatmentFilterOptions.Add(allTreatments);
        foreach (var t in _treatments)
        {
            TreatmentFilterOptions.Add(new FilterOptionVm(t.Id, t.Name));
        }

        SelectedTreatmentFilter = (prevTreatmentId.HasValue
            ? TreatmentFilterOptions.FirstOrDefault(o => o.Id == prevTreatmentId.Value)
            : null) ?? allTreatments;

        _suppressFilterRebuild = false;

        RebuildDentistFilterOptionsForTreatment();
    }

    private void RebuildDentistFilterOptionsForTreatment()
    {
        Guid? prevDentistId = SelectedDentistFilter?.Id;
        Guid? treatmentId = SelectedTreatmentFilter?.Id;
        var eligible = AppointmentCalendarHelper.GetEligibleDentists(_dentists, treatmentId);

        _suppressFilterRebuild = true;

        DentistFilterOptions.Clear();
        var allDentists = new FilterOptionVm(null, treatmentId.HasValue ? "All Qualified Dentists" : "All Dentists");
        DentistFilterOptions.Add(allDentists);
        foreach (var d in eligible)
        {
            DentistFilterOptions.Add(new FilterOptionVm(d.Id, d.FullName));
        }

        SelectedDentistFilter = (prevDentistId.HasValue
            ? DentistFilterOptions.FirstOrDefault(o => o.Id == prevDentistId.Value)
            : null) ?? allDentists;

        _suppressFilterRebuild = false;
    }

    private void RebuildViews()
    {
        HasActiveFilters = SelectedDentistFilter?.Id != null
            || SelectedTreatmentFilter?.Id != null
            || !string.IsNullOrWhiteSpace(PatientFilterText);

        var localOffset = DateTimeOffset.Now.Offset;
        var (start, end) = AppointmentCalendarHelper.GetDateRange(AnchorDate, CalendarViewMode);

        DateOnly? tableStart = IsTableMode && ShowAllDatesInTable ? null : start;
        DateOnly? tableEnd = IsTableMode && ShowAllDatesInTable ? null : end;

        var filteredForTable = FilterWithActiveCriteria(tableStart, tableEnd, localOffset);
        RebuildTableRows(filteredForTable);

        if (CalendarViewMode == AppointmentCalendarViewMode.Day)
        {
            RebuildDayView();
            FilteredSummaryLabel = FormatCountLabel(
                FilterWithActiveCriteria(AnchorDate, AnchorDate, localOffset).Count);
        }
        else if (CalendarViewMode == AppointmentCalendarViewMode.Week)
        {
            RebuildWeekView();
            FilteredSummaryLabel = FormatCountLabel(
                FilterWithActiveCriteria(start, end, localOffset).Count);
        }
        else
        {
            RebuildMonthView();
            FilteredSummaryLabel = FormatCountLabel(
                FilterWithActiveCriteria(start, end, localOffset).Count);
        }

        if (IsTableMode)
        {
            FilteredSummaryLabel = FormatCountLabel(filteredForTable.Count);
        }
    }

    private void RebuildTableRows(IReadOnlyList<AppointmentQueueItemDto> items)
    {
        Guid? selectedId = SelectedAppointmentRow?.Source.Id;
        TableRows.Clear();
        foreach (var item in items)
        {
            TableRows.Add(new AppointmentRowItem(item));
        }

        if (selectedId.HasValue)
        {
            SelectedAppointmentRow = TableRows.FirstOrDefault(r => r.Source.Id == selectedId.Value);
        }
    }

    private void RebuildDayView()
    {
        var localOffset = DateTimeOffset.Now.Offset;
        var dayItems = FilterWithActiveCriteria(AnchorDate, AnchorDate, localOffset);

        var columnsSource = new List<(Guid? Id, string Name, string Specialty, string Room)>();
        if (SelectedDentistFilter?.Id is Guid selectedId)
        {
            var match = _dentists.FirstOrDefault(d => d.Id == selectedId);
            if (match != null)
            {
                columnsSource.Add((match.Id, match.FullName, match.Specialty, match.DefaultRoom));
            }
        }
        else
        {
            var eligibleDentists = AppointmentCalendarHelper.GetEligibleDentists(_dentists, SelectedTreatmentFilter?.Id);
            foreach (var d in eligibleDentists)
            {
                columnsSource.Add((d.Id, d.FullName, d.Specialty, d.DefaultRoom));
            }

            foreach (var appt in dayItems)
            {
                bool alreadyListed = columnsSource.Any(c =>
                    (appt.DentistId.HasValue && c.Id == appt.DentistId)
                    || string.Equals(c.Name, appt.DoctorName, StringComparison.OrdinalIgnoreCase));
                if (!alreadyListed && !string.IsNullOrWhiteSpace(appt.DoctorName))
                {
                    columnsSource.Add((appt.DentistId, appt.DoctorName, "Visiting Clinician", appt.RoomName));
                }
            }

            if (columnsSource.Count == 0)
            {
                columnsSource.Add((null, "Dr. Aina Rahman", "General & Restorative", "Suite 1"));
            }
        }

        DayDentistHeaders.Clear();
        foreach (var col in columnsSource)
        {
            int count = dayItems.Count(a => BelongsToDentistColumn(a, col.Id, col.Name));
            DayDentistHeaders.Add(new DayDentistHeaderVm(col.Id, col.Name, col.Specialty, col.Room, count));
        }

        var slots = AppointmentCalendarHelper.GetDayTimeSlots(DaySession);
        DayTimeRows.Clear();

        foreach (var slot in slots)
        {
            var cells = new List<DaySlotCellVm>(columnsSource.Count);
            foreach (var col in columnsSource)
            {
                var matches = dayItems
                    .Where(a => BelongsToDentistColumn(a, col.Id, col.Name)
                        && AppointmentCalendarHelper.DoesAppointmentMatchSlot(a.ScheduledAt, localOffset, slot, DaySession))
                    .ToList();

                List<CalendarAppointmentCardVm> visible;
                int overflow = 0;

                if (matches.Count <= 2)
                {
                    bool compact = matches.Count == 2;
                    visible = matches.Select(m => BuildDayCard(m, localOffset, compact)).ToList();
                }
                else
                {
                    visible = new List<CalendarAppointmentCardVm>
                    {
                        BuildDayCard(matches[0], localOffset, compact: true)
                    };
                    overflow = matches.Count - 1;
                }

                cells.Add(new DaySlotCellVm(
                    AnchorDate,
                    slot.Start,
                    col.Id,
                    col.Name,
                    col.Room,
                    visible,
                    overflow));
            }

            DayTimeRows.Add(new DaySlotRowVm(slot.Label, slot.Start, cells));
        }
    }

    private void RebuildWeekView()
    {
        var localOffset = DateTimeOffset.Now.Offset;
        var localToday = DateOnly.FromDateTime(DateTime.Now);
        var weekDates = AppointmentCalendarHelper.GetWeekDates(AnchorDate);
        var weekItems = FilterWithActiveCriteria(weekDates[0], weekDates[^1], localOffset);

        WeekDayColumns.Clear();
        foreach (var date in weekDates)
        {
            var dayMatches = weekItems
                .Where(a => DateOnly.FromDateTime(a.ScheduledAt.ToOffset(localOffset).DateTime) == date)
                .ToList();

            var visible = dayMatches
                .Take(_maxWeekCardsPerDay)
                .Select(a => BuildWeekCard(a, localOffset))
                .ToList();
            int overflow = Math.Max(0, dayMatches.Count - _maxWeekCardsPerDay);

            WeekDayColumns.Add(new WeekDayColumnVm(
                date,
                date == localToday,
                date.DayOfWeek == DayOfWeek.Sunday,
                dayMatches.Count,
                visible,
                overflow));
        }
    }

    private void RebuildMonthView()
    {
        var localOffset = DateTimeOffset.Now.Offset;
        var localToday = DateOnly.FromDateTime(DateTime.Now);
        var gridDates = AppointmentCalendarHelper.GetMonthGridDates(AnchorDate);
        MonthRowCount = gridDates.Count / 7;

        var gridItems = FilterWithActiveCriteria(gridDates[0], gridDates[^1], localOffset);

        MonthDayCells.Clear();
        foreach (var date in gridDates)
        {
            var dayMatches = gridItems
                .Where(a => DateOnly.FromDateTime(a.ScheduledAt.ToOffset(localOffset).DateTime) == date)
                .ToList();

            var visible = dayMatches
                .Take(_maxMonthCardsPerDay)
                .Select(a => BuildMonthCard(a, localOffset))
                .ToList();
            int overflow = Math.Max(0, dayMatches.Count - _maxMonthCardsPerDay);

            MonthDayCells.Add(new MonthDayCellVm(
                date,
                date.Month == AnchorDate.Month,
                date == localToday,
                date.DayOfWeek == DayOfWeek.Sunday,
                dayMatches.Count,
                visible,
                overflow));
        }
    }

    private IReadOnlyList<AppointmentQueueItemDto> FilterWithActiveCriteria(
        DateOnly? startDate,
        DateOnly? endDate,
        TimeSpan localOffset)
    {
        Guid? dentistId = SelectedDentistFilter?.Id;
        string? dentistName = dentistId.HasValue
            ? _dentists.FirstOrDefault(d => d.Id == dentistId.Value)?.FullName
            : null;

        Guid? treatmentId = SelectedTreatmentFilter?.Id;
        string? treatmentName = treatmentId.HasValue
            ? _treatments.FirstOrDefault(t => t.Id == treatmentId.Value)?.Name
            : null;

        return AppointmentCalendarHelper.FilterAppointments(
            _allAppointments,
            startDate,
            endDate,
            dentistId,
            treatmentId,
            PatientFilterText,
            localOffset,
            dentistName,
            treatmentName);
    }

    private static bool BelongsToDentistColumn(AppointmentQueueItemDto appt, Guid? dentistId, string dentistName)
    {
        if (dentistId.HasValue && appt.DentistId == dentistId.Value)
        {
            return true;
        }

        return string.Equals(appt.DoctorName.Trim(), dentistName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static CalendarAppointmentCardVm BuildDayCard(
        AppointmentQueueItemDto appt,
        TimeSpan localOffset,
        bool compact)
    {
        var local = appt.ScheduledAt.ToOffset(localOffset);
        var timeLabel = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        var patient = AppointmentCalendarHelper.FormatCompactPatientName(appt.PatientName, shortForm: false);
        var alertPrefix = appt.HasCriticalMedicalAlert ? "[!] " : string.Empty;

        if (compact)
        {
            var proc = AppointmentCalendarHelper.FormatCompactTreatment(appt.ProcedureSummary, 16);
            return new CalendarAppointmentCardVm(
                appt,
                timeLabel,
                $"{timeLabel} · {alertPrefix}{patient} · {proc}",
                string.Empty,
                BuildTooltip(appt, local));
        }

        var treatment = AppointmentCalendarHelper.FormatCompactTreatment(appt.ProcedureSummary, 22);
        var status = AppointmentCalendarHelper.FormatStatusLabel(appt.Status);
        return new CalendarAppointmentCardVm(
            appt,
            timeLabel,
            $"{timeLabel} ({appt.DurationMinutes}m) · {alertPrefix}{patient}",
            $"{treatment} · {status}",
            BuildTooltip(appt, local));
    }

    private static CalendarAppointmentCardVm BuildWeekCard(AppointmentQueueItemDto appt, TimeSpan localOffset)
    {
        var local = appt.ScheduledAt.ToOffset(localOffset);
        var timeLabel = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        var dentist = AppointmentCalendarHelper.FormatCompactDentistName(appt.DoctorName);
        var patient = AppointmentCalendarHelper.FormatCompactPatientName(appt.PatientName, shortForm: false);
        var alertPrefix = appt.HasCriticalMedicalAlert ? "[!] " : string.Empty;

        return new CalendarAppointmentCardVm(
            appt,
            timeLabel,
            $"{timeLabel} · {dentist}",
            $"{alertPrefix}{patient} ({appt.DurationMinutes}m)",
            BuildTooltip(appt, local));
    }

    private static CalendarAppointmentCardVm BuildMonthCard(AppointmentQueueItemDto appt, TimeSpan localOffset)
    {
        var local = appt.ScheduledAt.ToOffset(localOffset);
        var timeLabel = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        var patientShort = AppointmentCalendarHelper.FormatCompactPatientName(appt.PatientName, shortForm: true);
        var alertPrefix = appt.HasCriticalMedicalAlert ? "[!] " : string.Empty;

        return new CalendarAppointmentCardVm(
            appt,
            timeLabel,
            $"{timeLabel} {alertPrefix}{patientShort}",
            string.Empty,
            BuildTooltip(appt, local));
    }

    private static string BuildTooltip(AppointmentQueueItemDto appt, DateTimeOffset localStart)
    {
        var localEnd = localStart.AddMinutes(Math.Max(15, appt.DurationMinutes));
        var lines = new List<string>
        {
            $"{appt.PatientName} ({(string.IsNullOrWhiteSpace(appt.PatientIc) ? "No IC" : appt.PatientIc)})",
            $"{localStart:ddd dd MMM yyyy, HH:mm} – {localEnd:HH:mm} ({appt.DurationMinutes} min)",
            $"Dentist: {appt.DoctorName} · {appt.RoomName}",
            $"Treatment: {appt.ProcedureSummary}",
            $"Status: {AppointmentCalendarHelper.FormatStatusLabel(appt.Status)}"
        };

        if (appt.HasCriticalMedicalAlert)
        {
            lines.Add($"Medical Alert: {appt.AlertSummary}");
        }

        if (!string.IsNullOrWhiteSpace(appt.Notes))
        {
            lines.Add($"Notes: {appt.Notes}");
        }

        lines.Add("Click to view or edit appointment");
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatWeekRange(DateOnly anchorDate)
    {
        var (start, end) = AppointmentCalendarHelper.GetDateRange(anchorDate, AppointmentCalendarViewMode.Week);
        return start.Month == end.Month
            ? $"{start:dd} – {end:dd MMM yyyy}"
            : $"{start:dd MMM} – {end:dd MMM yyyy}";
    }

    private static string FormatCountLabel(int count) =>
        count == 1 ? "1 appointment" : $"{count} appointments";
}
