using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
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

public sealed record AppointmentRescheduleArgs(
    AppointmentQueueItemDto Appointment,
    DateOnly TargetDate,
    TimeOnly TargetStartTime);

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

public sealed class TimelineHourMarkVm
{
    public TimelineHourMarkVm(string label, int startRow, int rowSpan, bool isFullHour)
    {
        Label = label;
        StartRow = startRow;
        RowSpan = rowSpan;
        IsFullHour = isFullHour;
    }

    public string Label { get; }
    public int StartRow { get; }
    public int RowSpan { get; }
    public int LaneIndex => 0;
    public int LaneSpan => 1;
    public int LaneCount => 1;
    public bool IsFullHour { get; }
}

public sealed class DayQuickDatePillVm
{
    public DayQuickDatePillVm(
        DateOnly date,
        string dayLabel,
        string dateNumLabel,
        bool isSelected,
        bool isToday,
        int appointmentCount)
    {
        Date = date;
        DayLabel = dayLabel;
        DateNumLabel = dateNumLabel;
        IsSelected = isSelected;
        IsToday = isToday;
        AppointmentCount = appointmentCount;
    }

    public DateOnly Date { get; }
    public string DayLabel { get; }
    public string DateNumLabel { get; }
    public bool IsSelected { get; }
    public bool IsToday { get; }
    public int AppointmentCount { get; }
    public bool HasCount => AppointmentCount > 0;
    public string CountText => AppointmentCount == 0 ? string.Empty : AppointmentCount.ToString(CultureInfo.InvariantCulture);
    public string PillTitle => $"{DayLabel} {DateNumLabel}";
}

public sealed class TimelineAppointmentBlockVm
{
    public TimelineAppointmentBlockVm(
        AppointmentQueueItemDto source,
        int startRow,
        int rowSpan,
        int laneIndex,
        int laneSpan,
        int laneCount,
        string timeRangeLabel,
        string patientDisplay,
        string secondaryDisplay,
        string tertiaryDisplay,
        string statusBadgeText,
        string toolTipText,
        bool showHeaderRow,
        bool showSecondaryLine,
        bool showTertiaryLine,
        bool showStatusBadge,
        Brush cardBackground,
        Brush cardBorderBrush,
        Brush accentBrush,
        Brush primaryTextBrush,
        Brush secondaryTextBrush,
        Brush badgeBackground,
        Brush badgeForeground)
    {
        Source = source;
        StartRow = startRow;
        RowSpan = rowSpan;
        LaneIndex = laneIndex;
        LaneSpan = laneSpan;
        LaneCount = laneCount;
        TimeRangeLabel = timeRangeLabel;
        PatientDisplay = patientDisplay;
        SecondaryDisplay = secondaryDisplay;
        TertiaryDisplay = tertiaryDisplay;
        StatusBadgeText = statusBadgeText;
        ToolTipText = toolTipText;
        ShowHeaderRow = showHeaderRow;
        ShowSecondaryLine = showSecondaryLine;
        ShowTertiaryLine = showTertiaryLine;
        ShowStatusBadge = showStatusBadge;
        CardBackground = cardBackground;
        CardBorderBrush = cardBorderBrush;
        AccentBrush = accentBrush;
        PrimaryTextBrush = primaryTextBrush;
        SecondaryTextBrush = secondaryTextBrush;
        BadgeBackground = badgeBackground;
        BadgeForeground = badgeForeground;
    }

    public AppointmentQueueItemDto Source { get; }
    public int StartRow { get; }
    public int RowSpan { get; }
    public int LaneIndex { get; }
    public int LaneSpan { get; }
    public int LaneCount { get; }

    public string TimeRangeLabel { get; }
    public string PatientDisplay { get; }
    public string SecondaryDisplay { get; }
    public string TertiaryDisplay { get; }
    public string StatusBadgeText { get; }
    public string ToolTipText { get; }

    public bool IsUltraCompact => RowSpan <= 1;
    public bool ShowHeaderRow { get; }
    public bool ShowSecondaryLine { get; }
    public bool ShowTertiaryLine { get; }
    public bool ShowStatusBadge { get; }
    public bool HasCriticalAlert => Source.HasCriticalMedicalAlert;
    public bool IsCancelled => Source.Status == QueueStatus.Cancelled;
    public bool IsWalkIn => Source.VisitType == VisitType.WalkIn;

    public Brush CardBackground { get; }
    public Brush CardBorderBrush { get; }
    public Brush AccentBrush { get; }
    public Brush PrimaryTextBrush { get; }
    public Brush SecondaryTextBrush { get; }
    public Brush BadgeBackground { get; }
    public Brush BadgeForeground { get; }
}

public sealed class DayDentistTimelineColumnVm
{
    public DayDentistTimelineColumnVm(
        DateOnly date,
        Guid? dentistId,
        string fullName,
        string specialty,
        string defaultRoom,
        int dayCount,
        Brush accentBrush,
        int totalRows,
        int hourOffsetRows,
        bool showCurrentTimeLine,
        double currentTimeRowFraction,
        IReadOnlyList<TimelineAppointmentBlockVm> blocks,
        int overflowCount)
    {
        Date = date;
        DentistId = dentistId;
        FullName = fullName;
        Subtitle = string.IsNullOrWhiteSpace(specialty)
            ? defaultRoom
            : $"{specialty} · {defaultRoom}";
        DefaultRoom = defaultRoom;
        CountLabel = dayCount == 1 ? "1 appt" : $"{dayCount} appts";
        AccentBrush = accentBrush;
        TotalRows = totalRows;
        HourOffsetRows = hourOffsetRows;
        ShowCurrentTimeLine = showCurrentTimeLine;
        CurrentTimeRowFraction = currentTimeRowFraction;
        Blocks = blocks;
        OverflowCount = overflowCount;
    }

    public DateOnly Date { get; }
    public Guid? DentistId { get; }
    public string FullName { get; }
    public string Subtitle { get; }
    public string DefaultRoom { get; }
    public string CountLabel { get; }
    public Brush AccentBrush { get; }
    public int TotalRows { get; }
    public int HourOffsetRows { get; }
    public bool ShowCurrentTimeLine { get; }
    public double CurrentTimeRowFraction { get; }
    public IReadOnlyList<TimelineAppointmentBlockVm> Blocks { get; }
    public int OverflowCount { get; }
    public bool HasOverflow => OverflowCount > 0;
    public string OverflowLabel => $"+{OverflowCount} more";
}

public sealed class WeekTimelineColumnVm
{
    public WeekTimelineColumnVm(
        DateOnly date,
        bool isToday,
        bool isSunday,
        int totalCount,
        int totalRows,
        int hourOffsetRows,
        bool showCurrentTimeLine,
        double currentTimeRowFraction,
        IReadOnlyList<TimelineAppointmentBlockVm> blocks,
        int overflowCount)
    {
        Date = date;
        DayNameLabel = date.ToString("ddd", CultureInfo.InvariantCulture);
        DateLabel = date.ToString("dd MMM", CultureInfo.InvariantCulture);
        IsToday = isToday;
        IsSunday = isSunday;
        TotalCount = totalCount;
        CountLabel = totalCount == 0 ? string.Empty : totalCount.ToString(CultureInfo.InvariantCulture);
        TotalRows = totalRows;
        HourOffsetRows = hourOffsetRows;
        ShowCurrentTimeLine = showCurrentTimeLine;
        CurrentTimeRowFraction = currentTimeRowFraction;
        Blocks = blocks;
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
    public int TotalRows { get; }
    public int HourOffsetRows { get; }
    public bool ShowCurrentTimeLine { get; }
    public double CurrentTimeRowFraction { get; }
    public IReadOnlyList<TimelineAppointmentBlockVm> Blocks { get; }
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
/// Workspace ViewModel managing the Appointments proportional 15-minute Timeline Calendar
/// (Day / Week / Month) and paginated 4-column Appointment Table with drag-and-drop date rescheduling.
/// </summary>
public partial class AppointmentsViewModel : ObservableObject
{
    private const double ResolutionHeightThreshold = 1120.0;

    private static readonly CardPalette[] DentistPalettes =
    [
        CreatePalette("#E8F7F5", "#B7E4DD", "#0F766E", "#0F3D39", "#2C6B65", "#FFFFFF", "#0F766E"),
        CreatePalette("#EAF4FE", "#BCD9FA", "#0369A1", "#0C3554", "#2D5D80", "#FFFFFF", "#0369A1"),
        CreatePalette("#F2EEFE", "#D3C6FA", "#6D28D9", "#36196B", "#5B428A", "#FFFFFF", "#6D28D9"),
        CreatePalette("#ECFDF3", "#B5ECCD", "#15803D", "#124024", "#316B46", "#FFFFFF", "#15803D")
    ];

    private static readonly CardPalette AiPendingPalette =
        CreatePalette("#FFFBEB", "#FDE68A", "#D97706", "#78350F", "#92400E", "#FEF3C7", "#B45309");

    private static readonly CardPalette WalkInPalette =
        CreatePalette("#FFF7ED", "#FDBA74", "#EA580C", "#7C2D12", "#9A3412", "#FFEDD5", "#C2410C");

    private static readonly CardPalette CancelledPalette =
        CreatePalette("#F8FAFC", "#E2E8F0", "#94A3B8", "#64748B", "#94A3B8", "#F1F5F9", "#64748B");

    private List<AppointmentQueueItemDto> _allAppointments = new();
    private List<DentistDto> _dentists = new();
    private List<TreatmentDto> _treatments = new();
    private bool _suppressFilterRebuild;
    private int _maxMonthCardsPerDay = 2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCalendarMode))]
    [NotifyPropertyChangedFor(nameof(IsTableMode))]
    [NotifyPropertyChangedFor(nameof(IsDayCalendarView))]
    [NotifyPropertyChangedFor(nameof(IsWeekCalendarView))]
    [NotifyPropertyChangedFor(nameof(IsMonthCalendarView))]
    [NotifyPropertyChangedFor(nameof(IsTimelineCalendarView))]
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
    [NotifyPropertyChangedFor(nameof(IsTimelineCalendarView))]
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
    private int _timelineTotalRows = 36;

    [ObservableProperty]
    private TimeOnly _timelineWindowStart = new(9, 0);

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
        TimelineHourMarks = new ObservableCollection<TimelineHourMarkVm>();
        DayQuickDates = new ObservableCollection<DayQuickDatePillVm>();
        DayDentistColumns = new ObservableCollection<DayDentistTimelineColumnVm>();
        WeekTimelineColumns = new ObservableCollection<WeekTimelineColumnVm>();
        MonthDayCells = new ObservableCollection<MonthDayCellVm>();
    }

    public event EventHandler<AppointmentQueueItemDto>? AppointmentOpenRequested;
    public event EventHandler<QuickBookSlotArgs>? QuickBookRequested;
    public event EventHandler<AppointmentQueueItemDto>? AppointmentSelectedForBanner;
    public event EventHandler<AppointmentRescheduleArgs>? AppointmentRescheduleRequested;

    public ObservableCollection<FilterOptionVm> DentistFilterOptions { get; }
    public ObservableCollection<FilterOptionVm> TreatmentFilterOptions { get; }
    public ObservableCollection<AppointmentRowItem> TableRows { get; }
    public ObservableCollection<TimelineHourMarkVm> TimelineHourMarks { get; }
    public ObservableCollection<DayQuickDatePillVm> DayQuickDates { get; }
    public ObservableCollection<DayDentistTimelineColumnVm> DayDentistColumns { get; }
    public ObservableCollection<WeekTimelineColumnVm> WeekTimelineColumns { get; }
    public ObservableCollection<MonthDayCellVm> MonthDayCells { get; }

    public int TimelineSlotMinutes => 15;

    public bool IsCalendarMode => DisplayMode == AppointmentDisplayMode.Calendar;
    public bool IsTableMode => DisplayMode == AppointmentDisplayMode.Table;

    public bool IsDayCalendarView => IsCalendarMode && CalendarViewMode == AppointmentCalendarViewMode.Day;
    public bool IsWeekCalendarView => IsCalendarMode && CalendarViewMode == AppointmentCalendarViewMode.Week;
    public bool IsMonthCalendarView => IsCalendarMode && CalendarViewMode == AppointmentCalendarViewMode.Month;
    public bool IsTimelineCalendarView => IsDayCalendarView || IsWeekCalendarView;

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
        int newMonthCap = hostHeight > ResolutionHeightThreshold ? 3 : 2;
        if (newMonthCap == _maxMonthCardsPerDay)
        {
            return;
        }

        _maxMonthCardsPerDay = newMonthCap;
        RebuildViews();
    }

    public void QuickBookAtSlot(
        DateOnly date,
        TimeOnly startTime,
        Guid? dentistId,
        string? doctorName,
        string? defaultRoom)
    {
        if (!dentistId.HasValue && string.IsNullOrWhiteSpace(doctorName))
        {
            var eligible = AppointmentCalendarHelper.GetEligibleDentists(_dentists, SelectedTreatmentFilter?.Id);
            var fallback = SelectedDentistFilter?.Id is Guid did
                ? eligible.FirstOrDefault(d => d.Id == did) ?? _dentists.FirstOrDefault(d => d.Id == did)
                : eligible.FirstOrDefault() ?? _dentists.FirstOrDefault();
            dentistId = fallback?.Id;
            doctorName = fallback?.FullName;
            defaultRoom = fallback?.DefaultRoom;
        }

        QuickBookRequested?.Invoke(this, new QuickBookSlotArgs(
            date,
            startTime,
            dentistId,
            doctorName,
            defaultRoom,
            SelectedTreatmentFilter?.Id));
    }

    public bool CanDropAppointmentOnDentist(
        AppointmentQueueItemDto appointment,
        Guid? targetDentistId,
        string? targetDentistName) =>
        AppointmentCalendarHelper.IsSameDentist(appointment, targetDentistId, targetDentistName);

    public void RequestReschedule(
        AppointmentQueueItemDto appointment,
        DateOnly targetDate,
        TimeOnly targetStartTime)
    {
        var local = appointment.ScheduledAt.ToOffset(DateTimeOffset.Now.Offset);
        var currentDate = DateOnly.FromDateTime(local.DateTime);
        var currentTime = TimeOnly.FromDateTime(local.DateTime);

        if (currentDate == targetDate && currentTime == targetStartTime)
        {
            return;
        }

        AppointmentRescheduleRequested?.Invoke(
            this,
            new AppointmentRescheduleArgs(appointment, targetDate, targetStartTime));
    }

    public int ComputeAppointmentRowSpan(AppointmentQueueItemDto appointment)
    {
        int duration = Math.Max(TimelineSlotMinutes, appointment.DurationMinutes);
        return Math.Max(1, (int)Math.Ceiling(duration / (double)TimelineSlotMinutes));
    }

    public TimeOnly SnapRowToTime(int rowIndex)
    {
        var window = AppointmentCalendarHelper.GetTimelineWindow(DaySession, TimelineSlotMinutes);
        return AppointmentCalendarHelper.SnapRowToStartTime(window, rowIndex);
    }

    partial void OnDisplayModeChanged(AppointmentDisplayMode value) => RebuildViews();
    partial void OnCalendarViewModeChanged(AppointmentCalendarViewMode value) => RebuildViews();
    partial void OnShowAllDatesInTableChanged(bool value) => RebuildViews();
    partial void OnDaySessionChanged(DaySlotSession value) => RebuildViews();
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
    private void ShowDentistDayOverflowInTable(DayDentistTimelineColumnVm? column)
    {
        if (column == null)
        {
            return;
        }

        _suppressFilterRebuild = true;
        AnchorDate = column.Date;
        CalendarViewMode = AppointmentCalendarViewMode.Day;
        ShowAllDatesInTable = false;
        if (column.DentistId.HasValue)
        {
            var match = DentistFilterOptions.FirstOrDefault(o => o.Id == column.DentistId);
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
    private void QuickBookFromDate(DateOnly date)
    {
        var eligible = AppointmentCalendarHelper.GetEligibleDentists(_dentists, SelectedTreatmentFilter?.Id);
        var dentist = SelectedDentistFilter?.Id is Guid did
            ? eligible.FirstOrDefault(d => d.Id == did) ?? _dentists.FirstOrDefault(d => d.Id == did)
            : eligible.FirstOrDefault() ?? _dentists.FirstOrDefault();

        QuickBookRequested?.Invoke(this, new QuickBookSlotArgs(
            date,
            TimelineWindowStart,
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

    private void RebuildTimelineHeaderMarks(TimelineWindow window)
    {
        TimelineTotalRows = window.TotalRows;
        TimelineWindowStart = window.Start;
        TimelineHourMarks.Clear();

        int rowsPerHour = 60 / window.SlotMinutes;
        int row = 0;
        while (row < window.TotalRows)
        {
            var time = window.Start.AddMinutes(row * window.SlotMinutes);
            bool isFullHour = time.Minute == 0;
            int nextBoundary = isFullHour
                ? Math.Min(window.TotalRows, row + rowsPerHour)
                : Math.Min(window.TotalRows, row + ((60 - time.Minute) / window.SlotMinutes));
            int span = Math.Max(1, nextBoundary - row);

            TimelineHourMarks.Add(new TimelineHourMarkVm(
                time.ToString("HH:mm", CultureInfo.InvariantCulture),
                row,
                span,
                isFullHour));

            row = nextBoundary;
        }
    }

    private void RebuildDayView()
    {
        var localNow = DateTimeOffset.Now;
        var localOffset = localNow.Offset;
        var localToday = DateOnly.FromDateTime(localNow.DateTime);
        var window = AppointmentCalendarHelper.GetTimelineWindow(DaySession, TimelineSlotMinutes);
        RebuildTimelineHeaderMarks(window);

        var weekDates = AppointmentCalendarHelper.GetWeekDates(AnchorDate);
        var weekItems = FilterWithActiveCriteria(weekDates[0], weekDates[^1], localOffset);
        DayQuickDates.Clear();
        foreach (var d in weekDates)
        {
            int count = weekItems.Count(a => DateOnly.FromDateTime(a.ScheduledAt.ToOffset(localOffset).DateTime) == d);
            DayQuickDates.Add(new DayQuickDatePillVm(
                d,
                d.ToString("ddd", CultureInfo.InvariantCulture),
                d.ToString("dd MMM", CultureInfo.InvariantCulture),
                d == AnchorDate,
                d == localToday,
                count));
        }

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

        int hourOffsetRows = window.Start.Minute == 0 ? 0 : (60 - window.Start.Minute) / window.SlotMinutes;
        var (showNow, nowFraction) = ComputeCurrentTimeIndicator(AnchorDate, window, localNow);

        DayDentistColumns.Clear();
        foreach (var col in columnsSource)
        {
            var colMatches = dayItems
                .Where(a => BelongsToDentistColumn(a, col.Id, col.Name))
                .ToList();

            var layout = AppointmentCalendarHelper.ComputeTimelineLayout(
                colMatches,
                DaySession,
                localOffset,
                slotMinutes: TimelineSlotMinutes,
                maxVisibleLanes: 4);

            var blocks = layout.VisibleBlocks
                .Select(b => BuildTimelineBlock(b, localOffset, isWeekView: false))
                .ToList();

            var palette = DentistPalettes[GetDentistPaletteIndex(col.Id, col.Name)];
            DayDentistColumns.Add(new DayDentistTimelineColumnVm(
                AnchorDate,
                col.Id,
                col.Name,
                col.Specialty,
                col.Room,
                colMatches.Count,
                palette.Accent,
                window.TotalRows,
                hourOffsetRows,
                showNow,
                nowFraction,
                blocks,
                layout.OverflowAppointments.Count));
        }
    }

    private void RebuildWeekView()
    {
        var localNow = DateTimeOffset.Now;
        var localOffset = localNow.Offset;
        var localToday = DateOnly.FromDateTime(localNow.DateTime);
        var window = AppointmentCalendarHelper.GetTimelineWindow(DaySession, TimelineSlotMinutes);
        RebuildTimelineHeaderMarks(window);

        int hourOffsetRows = window.Start.Minute == 0 ? 0 : (60 - window.Start.Minute) / window.SlotMinutes;
        var weekDates = AppointmentCalendarHelper.GetWeekDates(AnchorDate);
        var weekItems = FilterWithActiveCriteria(weekDates[0], weekDates[^1], localOffset);

        WeekTimelineColumns.Clear();
        foreach (var date in weekDates)
        {
            var dayMatches = weekItems
                .Where(a => DateOnly.FromDateTime(a.ScheduledAt.ToOffset(localOffset).DateTime) == date)
                .ToList();

            var layout = AppointmentCalendarHelper.ComputeTimelineLayout(
                dayMatches,
                DaySession,
                localOffset,
                slotMinutes: TimelineSlotMinutes,
                maxVisibleLanes: 3);

            var blocks = layout.VisibleBlocks
                .Select(b => BuildTimelineBlock(b, localOffset, isWeekView: true))
                .ToList();

            var (showNow, nowFraction) = ComputeCurrentTimeIndicator(date, window, localNow);

            WeekTimelineColumns.Add(new WeekTimelineColumnVm(
                date,
                date == localToday,
                date.DayOfWeek == DayOfWeek.Sunday,
                dayMatches.Count,
                window.TotalRows,
                hourOffsetRows,
                showNow,
                nowFraction,
                blocks,
                layout.OverflowAppointments.Count));
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

    private TimelineAppointmentBlockVm BuildTimelineBlock(
        TimelineBlockLayout layout,
        TimeSpan localOffset,
        bool isWeekView)
    {
        var appt = layout.Appointment;
        var localStart = appt.ScheduledAt.ToOffset(localOffset);
        var duration = Math.Max(15, appt.DurationMinutes);
        var localEnd = localStart.AddMinutes(duration);

        var startStr = localStart.ToString("HH:mm", CultureInfo.InvariantCulture);
        var endStr = localEnd.ToString("HH:mm", CultureInfo.InvariantCulture);
        var durLabel = duration >= 60 && duration % 60 == 0
            ? $"{duration / 60}h"
            : $"{duration}m";

        var shortName = AppointmentCalendarHelper.FormatCompactPatientName(appt.PatientName, shortForm: true);
        var fullCompactName = AppointmentCalendarHelper.FormatCompactPatientName(appt.PatientName, shortForm: false);
        var alertPrefix = appt.HasCriticalMedicalAlert ? "[!] " : string.Empty;
        var isWalkIn = appt.VisitType == VisitType.WalkIn;
        var walkInTag = isWalkIn ? "W · " : string.Empty;
        var statusLabel = isWalkIn
            ? "Walk-In"
            : AppointmentCalendarHelper.FormatStatusLabel(appt.Status);

        var palette = ResolvePalette(appt);
        bool isNarrowSplit = layout.LaneCount >= 3 || (isWeekView && layout.LaneCount >= 2);

        string timeRangeLabel;
        string patientDisplay;
        string secondaryDisplay;
        string tertiaryDisplay;
        bool showHeaderRow;
        bool showSecondaryLine;
        bool showTertiaryLine;
        bool showStatusBadge;

        if (layout.RowSpan <= 1)
        {
            timeRangeLabel = string.Empty;
            patientDisplay = isNarrowSplit
                ? $"{startStr} {walkInTag}{shortName}"
                : $"{startStr} · {walkInTag}{alertPrefix}{shortName}";
            secondaryDisplay = string.Empty;
            tertiaryDisplay = string.Empty;
            showHeaderRow = false;
            showSecondaryLine = false;
            showTertiaryLine = false;
            showStatusBadge = false;
        }
        else if (isNarrowSplit)
        {
            timeRangeLabel = $"{startStr}–{endStr}";
            patientDisplay = $"{walkInTag}{alertPrefix}{shortName}";
            showHeaderRow = layout.RowSpan >= 3;
            secondaryDisplay = layout.RowSpan == 2
                ? $"{startStr}–{endStr}"
                : AppointmentCalendarHelper.FormatCompactTreatment(appt.ProcedureSummary, 16);
            tertiaryDisplay = layout.RowSpan >= 5
                ? (isWeekView
                    ? AppointmentCalendarHelper.FormatCompactDentistName(appt.DoctorName)
                    : appt.RoomName)
                : string.Empty;
            showSecondaryLine = !string.IsNullOrEmpty(secondaryDisplay);
            showTertiaryLine = !string.IsNullOrEmpty(tertiaryDisplay);
            showStatusBadge = false;
        }
        else
        {
            var displayName = layout.LaneCount >= 2 ? shortName : fullCompactName;

            if (layout.RowSpan == 2)
            {
                showHeaderRow = false;
                timeRangeLabel = $"{startStr}–{endStr}";
                patientDisplay = $"{walkInTag}{alertPrefix}{displayName}";
                secondaryDisplay = isWeekView && layout.LaneCount == 1
                    ? $"{startStr}–{endStr} · {AppointmentCalendarHelper.FormatCompactDentistName(appt.DoctorName)}"
                    : $"{startStr}–{endStr} · {AppointmentCalendarHelper.FormatCompactTreatment(appt.ProcedureSummary, 18)}";
                tertiaryDisplay = string.Empty;
                showSecondaryLine = true;
                showTertiaryLine = false;
                showStatusBadge = false;
            }
            else if (layout.RowSpan == 3)
            {
                showHeaderRow = true;
                timeRangeLabel = $"{startStr} – {endStr}";
                patientDisplay = $"{alertPrefix}{displayName}";
                secondaryDisplay = isWeekView
                    ? $"{AppointmentCalendarHelper.FormatCompactDentistName(appt.DoctorName)} · {AppointmentCalendarHelper.FormatCompactTreatment(appt.ProcedureSummary, 16)}"
                    : AppointmentCalendarHelper.FormatCompactTreatment(appt.ProcedureSummary, 22);
                tertiaryDisplay = string.Empty;
                showSecondaryLine = true;
                showTertiaryLine = false;
                showStatusBadge = layout.LaneCount == 1
                    && (isWalkIn
                        || appt.Status == QueueStatus.InChair
                        || appt.Status == QueueStatus.AiBookedPending
                        || appt.Status == QueueStatus.Cancelled);
            }
            else
            {
                showHeaderRow = true;
                timeRangeLabel = $"{startStr} – {endStr} · {durLabel}";
                patientDisplay = $"{alertPrefix}{displayName}";
                secondaryDisplay = AppointmentCalendarHelper.FormatCompactTreatment(
                    appt.ProcedureSummary,
                    layout.LaneCount >= 2 ? 20 : 28);

                var dentistShort = AppointmentCalendarHelper.FormatCompactDentistName(appt.DoctorName);
                var roomPart = string.IsNullOrWhiteSpace(appt.RoomName) ? string.Empty : $" · {appt.RoomName}";
                var baseMeta = isWeekView
                    ? $"{dentistShort}{roomPart}"
                    : (string.IsNullOrWhiteSpace(appt.RoomName)
                        ? $"{dentistShort} · {statusLabel}"
                        : $"{appt.RoomName} · {statusLabel}");

                if (layout.RowSpan >= 6 && !string.IsNullOrWhiteSpace(appt.Notes))
                {
                    baseMeta = $"{baseMeta} · {AppointmentCalendarHelper.FormatCompactTreatment(appt.Notes, 24)}";
                }

                tertiaryDisplay = baseMeta;
                showSecondaryLine = true;
                showTertiaryLine = true;
                showStatusBadge = layout.LaneCount == 1;
            }
        }

        return new TimelineAppointmentBlockVm(
            appt,
            layout.StartRow,
            layout.RowSpan,
            layout.LaneIndex,
            layout.LaneSpan,
            layout.LaneCount,
            timeRangeLabel,
            patientDisplay,
            secondaryDisplay,
            tertiaryDisplay,
            statusLabel,
            BuildTooltip(appt, localStart),
            showHeaderRow,
            showSecondaryLine,
            showTertiaryLine,
            showStatusBadge,
            palette.Background,
            palette.Border,
            palette.Accent,
            palette.PrimaryText,
            palette.SecondaryText,
            palette.BadgeBg,
            palette.BadgeFg);
    }

    private CardPalette ResolvePalette(AppointmentQueueItemDto appt)
    {
        if (appt.Status == QueueStatus.Cancelled)
        {
            return CancelledPalette;
        }

        if (appt.Status == QueueStatus.AiBookedPending)
        {
            return AiPendingPalette;
        }

        if (appt.VisitType == VisitType.WalkIn)
        {
            return WalkInPalette;
        }

        return DentistPalettes[GetDentistPaletteIndex(appt.DentistId, appt.DoctorName)];
    }

    private int GetDentistPaletteIndex(Guid? dentistId, string? doctorName)
    {
        if (dentistId.HasValue)
        {
            int idx = _dentists.FindIndex(d => d.Id == dentistId.Value);
            if (idx >= 0)
            {
                return idx % DentistPalettes.Length;
            }
        }

        if (!string.IsNullOrWhiteSpace(doctorName))
        {
            var trimmed = doctorName.Trim();
            int idx = _dentists.FindIndex(d =>
                string.Equals(d.FullName.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                return idx % DentistPalettes.Length;
            }

            return Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(trimmed)) % DentistPalettes.Length;
        }

        return 0;
    }

    private static (bool Show, double RowFraction) ComputeCurrentTimeIndicator(
        DateOnly columnDate,
        TimelineWindow window,
        DateTimeOffset localNow)
    {
        if (columnDate != DateOnly.FromDateTime(localNow.DateTime))
        {
            return (false, -1.0);
        }

        double nowMinFromStart = (localNow.Hour * 60.0 + localNow.Minute) - (window.Start.Hour * 60.0 + window.Start.Minute);
        double fraction = nowMinFromStart / window.SlotMinutes;
        if (fraction < 0 || fraction > window.TotalRows)
        {
            return (false, -1.0);
        }

        return (true, fraction);
    }

    private static CalendarAppointmentCardVm BuildMonthCard(AppointmentQueueItemDto appt, TimeSpan localOffset)
    {
        var local = appt.ScheduledAt.ToOffset(localOffset);
        var timeLabel = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        var patientShort = AppointmentCalendarHelper.FormatCompactPatientName(appt.PatientName, shortForm: true);
        var alertPrefix = appt.HasCriticalMedicalAlert ? "[!] " : string.Empty;
        var walkInTag = appt.VisitType == VisitType.WalkIn ? "W · " : string.Empty;

        return new CalendarAppointmentCardVm(
            appt,
            timeLabel,
            $"{timeLabel} {walkInTag}{alertPrefix}{patientShort}",
            string.Empty,
            BuildTooltip(appt, local));
    }

    private static string BuildTooltip(AppointmentQueueItemDto appt, DateTimeOffset localStart)
    {
        var localEnd = localStart.AddMinutes(Math.Max(15, appt.DurationMinutes));
        var visitLabel = appt.VisitType switch
        {
            VisitType.WalkIn => "Walk-In (No Appointment)",
            VisitType.AiBooked => "WhatsApp AI Booking",
            _ => "Scheduled Appointment"
        };
        var lines = new List<string>
        {
            $"{appt.PatientName} ({(string.IsNullOrWhiteSpace(appt.PatientIc) ? "No IC" : appt.PatientIc)})",
            $"Visit Type: {visitLabel}",
            $"{localStart:ddd dd MMM yyyy, HH:mm} – {localEnd:HH:mm} ({appt.DurationMinutes} min)",
            $"Dentist: {appt.DoctorName} · {appt.RoomName}",
            $"Treatment: {appt.ProcedureSummary}",
            $"Status: {AppointmentCalendarHelper.FormatStatusLabel(appt.Status)}"
        };

        if (appt.ArrivedAt.HasValue)
        {
            lines.Add($"Arrived: {appt.ArrivedAt.Value.ToLocalTime():HH:mm}");
        }

        if (!string.IsNullOrWhiteSpace(appt.CreatedBy))
        {
            lines.Add($"Added By: {appt.CreatedBy}");
        }

        if (!string.IsNullOrWhiteSpace(appt.LastUpdatedBy))
        {
            lines.Add($"Last Updated By: {appt.LastUpdatedBy}");
        }

        if (appt.HasCriticalMedicalAlert)
        {
            lines.Add($"Medical Alert: {appt.AlertSummary}");
        }

        if (!string.IsNullOrWhiteSpace(appt.Notes))
        {
            lines.Add($"Notes: {appt.Notes}");
        }

        lines.Add("Click to edit · Drag to another date to reschedule (same dentist)");
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

    private static Brush FreezeBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private static CardPalette CreatePalette(
        string backgroundHex,
        string borderHex,
        string accentHex,
        string primaryTextHex,
        string secondaryTextHex,
        string badgeBgHex,
        string badgeFgHex) =>
        new(
            FreezeBrush(backgroundHex),
            FreezeBrush(borderHex),
            FreezeBrush(accentHex),
            FreezeBrush(primaryTextHex),
            FreezeBrush(secondaryTextHex),
            FreezeBrush(badgeBgHex),
            FreezeBrush(badgeFgHex));

    private sealed record CardPalette(
        Brush Background,
        Brush Border,
        Brush Accent,
        Brush PrimaryText,
        Brush SecondaryText,
        Brush BadgeBg,
        Brush BadgeFg);
}
