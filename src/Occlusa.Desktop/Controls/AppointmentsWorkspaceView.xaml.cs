using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Occlusa.Desktop.ViewModels;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.Controls;

/// <summary>
/// Appointments workspace canvas housing the proportional 15-minute Timeline Calendar
/// (Day / Week), Month View, and 4-column UniformPagedGrid Table with cross-date drag-and-drop.
/// </summary>
public partial class AppointmentsWorkspaceView : UserControl
{
    private const string AppointmentDragFormat = "Occlusa.Appointment";

    private static readonly Brush ValidGhostBackground = CreateFrozenBrush("#CCFBF1");
    private static readonly Brush ValidGhostBorder = CreateFrozenBrush("#0F766E");
    private static readonly Brush ValidGhostText = CreateFrozenBrush("#0F3D39");

    private static readonly Brush BlockedGhostBackground = CreateFrozenBrush("#FEE2E2");
    private static readonly Brush BlockedGhostBorder = CreateFrozenBrush("#DC2626");
    private static readonly Brush BlockedGhostText = CreateFrozenBrush("#991B1B");

    private static readonly Brush DropTargetHighlightBorder = CreateFrozenBrush("#0F766E");
    private static readonly Brush DropTargetHighlightBackground = CreateFrozenBrush("#E6F4F1");

    private Window? _hostWindow;
    private Point _dragStartPoint;
    private double _dragGrabOffsetY;
    private AppointmentQueueItemDto? _pendingDragAppt;
    private AppointmentQueueItemDto? _activeDragAppt;
    private Border? _highlightedPillBorder;
    private Border? _highlightedMonthBorder;

    public AppointmentsWorkspaceView()
    {
        InitializeComponent();
        Loaded += AppointmentsWorkspaceView_Loaded;
        Unloaded += AppointmentsWorkspaceView_Unloaded;
        SizeChanged += AppointmentsWorkspaceView_SizeChanged;
    }

    private void AppointmentsWorkspaceView_Loaded(object sender, RoutedEventArgs e)
    {
        AttachHostWindow();
        UpdateResponsiveCapacity();
    }

    private void AppointmentsWorkspaceView_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachHostWindow();
    }

    private void AppointmentsWorkspaceView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveCapacity();
    }

    private void HostWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveCapacity();
    }

    private void AttachHostWindow()
    {
        var window = Window.GetWindow(this);
        if (ReferenceEquals(window, _hostWindow))
        {
            return;
        }

        DetachHostWindow();
        _hostWindow = window;
        if (_hostWindow != null)
        {
            _hostWindow.SizeChanged += HostWindow_SizeChanged;
        }
    }

    private void DetachHostWindow()
    {
        if (_hostWindow != null)
        {
            _hostWindow.SizeChanged -= HostWindow_SizeChanged;
            _hostWindow = null;
        }
    }

    private void UpdateResponsiveCapacity()
    {
        if (DataContext is not AppointmentsViewModel vm)
        {
            return;
        }

        double referenceHeight = _hostWindow is { ActualHeight: > 0 }
            ? _hostWindow.ActualHeight
            : SystemParameters.PrimaryScreenHeight;

        vm.UpdateViewportCapacity(referenceHeight);
    }

    private void AppointmentsPagedGrid_RowDoubleClicked(object? sender, object? rowObj)
    {
        if (rowObj is AppointmentRowItem row && DataContext is AppointmentsViewModel vm)
        {
            vm.OpenAppointmentCommand.Execute(row.Source);
        }
    }

    private void AppointmentCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe)
        {
            return;
        }

        _dragStartPoint = e.GetPosition(null);
        _dragGrabOffsetY = e.GetPosition(fe).Y;
        _pendingDragAppt = fe.DataContext switch
        {
            TimelineAppointmentBlockVm block => block.Source,
            CalendarAppointmentCardVm card => card.Source,
            _ => null
        };
    }

    private void AppointmentCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pendingDragAppt == null || sender is not FrameworkElement fe)
        {
            return;
        }

        var currentPos = e.GetPosition(null);
        double minDx = Math.Max(6.0, SystemParameters.MinimumHorizontalDragDistance);
        double minDy = Math.Max(6.0, SystemParameters.MinimumVerticalDragDistance);

        if (Math.Abs(currentPos.X - _dragStartPoint.X) < minDx
            && Math.Abs(currentPos.Y - _dragStartPoint.Y) < minDy)
        {
            return;
        }

        var appt = _pendingDragAppt;
        _pendingDragAppt = null;
        _activeDragAppt = appt;

        try
        {
            var data = new DataObject(AppointmentDragFormat, appt);
            DragDrop.DoDragDrop(fe, data, DragDropEffects.Move);
        }
        finally
        {
            _activeDragAppt = null;
            _dragGrabOffsetY = 0;
            ClearPillHighlight();
            ClearMonthHighlight();
        }
    }

    private void AppointmentCard_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _pendingDragAppt = null;
    }

    private void TimelineColumn_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Grid columnGrid
            || DataContext is not AppointmentsViewModel vm
            || _activeDragAppt != null
            || columnGrid.ActualHeight <= 0)
        {
            return;
        }

        if (IsPointerOverInteractiveElement(e.OriginalSource as DependencyObject))
        {
            HideHoverIndicator(columnGrid);
            return;
        }

        int totalRows = ResolveTotalRows(columnGrid.Tag, vm);
        double rowHeight = columnGrid.ActualHeight / totalRows;
        var pos = e.GetPosition(columnGrid);
        int row = Math.Clamp((int)Math.Floor(pos.Y / rowHeight), 0, totalRows - 1);
        var time = vm.SnapRowToTime(row);

        ShowHoverIndicator(columnGrid, row, rowHeight, $"+ {time:HH:mm}");
    }

    private void TimelineColumn_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Grid columnGrid)
        {
            HideHoverIndicator(columnGrid);
        }
    }

    private void TimelineColumn_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Grid columnGrid
            || DataContext is not AppointmentsViewModel vm
            || _activeDragAppt != null
            || columnGrid.ActualHeight <= 0)
        {
            return;
        }

        if (IsPointerOverInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        int totalRows = ResolveTotalRows(columnGrid.Tag, vm);
        double rowHeight = columnGrid.ActualHeight / totalRows;
        var pos = e.GetPosition(columnGrid);
        int row = Math.Clamp((int)Math.Floor(pos.Y / rowHeight), 0, totalRows - 1);
        var startTime = vm.SnapRowToTime(row);

        if (columnGrid.Tag is DayDentistTimelineColumnVm dayCol)
        {
            vm.QuickBookAtSlot(dayCol.Date, startTime, dayCol.DentistId, dayCol.FullName, dayCol.DefaultRoom);
        }
        else if (columnGrid.Tag is WeekTimelineColumnVm weekCol)
        {
            vm.QuickBookAtSlot(weekCol.Date, startTime, dentistId: null, doctorName: null, defaultRoom: null);
        }
    }

    private void TimelineColumn_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Grid columnGrid
            || DataContext is not AppointmentsViewModel vm
            || columnGrid.ActualHeight <= 0)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var appt = GetDraggedAppointment(e);
        if (appt == null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        HideHoverIndicator(columnGrid);

        int totalRows = ResolveTotalRows(columnGrid.Tag, vm);
        double rowHeight = columnGrid.ActualHeight / totalRows;
        var pos = e.GetPosition(columnGrid);
        double effectiveTopY = Math.Max(0, pos.Y - _dragGrabOffsetY);
        int startRow = Math.Clamp((int)Math.Round(effectiveTopY / rowHeight), 0, totalRows - 1);
        int rowSpan = vm.ComputeAppointmentRowSpan(appt);
        var startTime = vm.SnapRowToTime(startRow);
        var endTime = startTime.AddMinutes(Math.Max(15, appt.DurationMinutes));

        if (columnGrid.Tag is DayDentistTimelineColumnVm dayCol)
        {
            if (!vm.CanDropAppointmentOnDentist(appt, dayCol.DentistId, dayCol.FullName))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                ShowDropGhost(columnGrid, startRow, rowSpan, rowHeight, "Same dentist only", isAllowed: false);
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            ShowDropGhost(
                columnGrid,
                startRow,
                rowSpan,
                rowHeight,
                $"{startTime:HH:mm} – {endTime:HH:mm}",
                isAllowed: true);
        }
        else if (columnGrid.Tag is WeekTimelineColumnVm weekCol)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            ShowDropGhost(
                columnGrid,
                startRow,
                rowSpan,
                rowHeight,
                $"{weekCol.DayNameLabel} {weekCol.DateLabel} · {startTime:HH:mm}–{endTime:HH:mm}",
                isAllowed: true);
        }
        else
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void TimelineColumn_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Grid columnGrid)
        {
            HideDropGhost(columnGrid);
        }
    }

    private void TimelineColumn_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Grid columnGrid)
        {
            return;
        }

        HideDropGhost(columnGrid);

        if (DataContext is not AppointmentsViewModel vm || columnGrid.ActualHeight <= 0)
        {
            return;
        }

        var appt = GetDraggedAppointment(e);
        if (appt == null)
        {
            return;
        }

        int totalRows = ResolveTotalRows(columnGrid.Tag, vm);
        double rowHeight = columnGrid.ActualHeight / totalRows;
        var pos = e.GetPosition(columnGrid);
        double effectiveTopY = Math.Max(0, pos.Y - _dragGrabOffsetY);
        int startRow = Math.Clamp((int)Math.Round(effectiveTopY / rowHeight), 0, totalRows - 1);
        var startTime = vm.SnapRowToTime(startRow);

        if (columnGrid.Tag is DayDentistTimelineColumnVm dayCol)
        {
            if (!vm.CanDropAppointmentOnDentist(appt, dayCol.DentistId, dayCol.FullName))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            vm.RequestReschedule(appt, dayCol.Date, startTime);
            e.Handled = true;
        }
        else if (columnGrid.Tag is WeekTimelineColumnVm weekCol)
        {
            vm.RequestReschedule(appt, weekCol.Date, startTime);
            e.Handled = true;
        }
    }

    private void QuickDatePill_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Border border || border.Tag is not DayQuickDatePillVm pill)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var appt = GetDraggedAppointment(e);
        if (appt == null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var localDate = DateOnly.FromDateTime(appt.ScheduledAt.ToOffset(DateTimeOffset.Now.Offset).DateTime);
        if (pill.Date == localDate)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        HighlightPill(border);
    }

    private void QuickDatePill_DragLeave(object sender, DragEventArgs e)
    {
        ClearPillHighlight();
    }

    private void QuickDatePill_Drop(object sender, DragEventArgs e)
    {
        ClearPillHighlight();

        if (sender is not Border border
            || border.Tag is not DayQuickDatePillVm pill
            || DataContext is not AppointmentsViewModel vm)
        {
            return;
        }

        var appt = GetDraggedAppointment(e);
        if (appt == null)
        {
            return;
        }

        var local = appt.ScheduledAt.ToOffset(DateTimeOffset.Now.Offset);
        var localTime = TimeOnly.FromDateTime(local.DateTime);
        vm.RequestReschedule(appt, pill.Date, localTime);
        e.Handled = true;
    }

    private void MonthCell_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Border border || border.Tag is not MonthDayCellVm cell)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var appt = GetDraggedAppointment(e);
        if (appt == null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var localDate = DateOnly.FromDateTime(appt.ScheduledAt.ToOffset(DateTimeOffset.Now.Offset).DateTime);
        if (cell.Date == localDate)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        HighlightMonthCell(border);
    }

    private void MonthCell_DragLeave(object sender, DragEventArgs e)
    {
        ClearMonthHighlight();
    }

    private void MonthCell_Drop(object sender, DragEventArgs e)
    {
        ClearMonthHighlight();

        if (sender is not Border border
            || border.Tag is not MonthDayCellVm cell
            || DataContext is not AppointmentsViewModel vm)
        {
            return;
        }

        var appt = GetDraggedAppointment(e);
        if (appt == null)
        {
            return;
        }

        var local = appt.ScheduledAt.ToOffset(DateTimeOffset.Now.Offset);
        var localTime = TimeOnly.FromDateTime(local.DateTime);
        vm.RequestReschedule(appt, cell.Date, localTime);
        e.Handled = true;
    }

    private static AppointmentQueueItemDto? GetDraggedAppointment(DragEventArgs e) =>
        e.Data.GetDataPresent(AppointmentDragFormat)
            ? e.Data.GetData(AppointmentDragFormat) as AppointmentQueueItemDto
            : null;

    private static int ResolveTotalRows(object? tag, AppointmentsViewModel vm) => tag switch
    {
        DayDentistTimelineColumnVm dayCol when dayCol.TotalRows > 0 => dayCol.TotalRows,
        WeekTimelineColumnVm weekCol when weekCol.TotalRows > 0 => weekCol.TotalRows,
        _ => Math.Max(1, vm.TimelineTotalRows)
    };

    private static bool IsPointerOverInteractiveElement(DependencyObject? source)
    {
        var current = source;
        while (current != null)
        {
            if (current is Button btn && btn.DataContext is
                TimelineAppointmentBlockVm or
                CalendarAppointmentCardVm or
                DayDentistTimelineColumnVm or
                WeekTimelineColumnVm)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static void ShowHoverIndicator(Grid columnGrid, int row, double rowHeight, string label)
    {
        var indicator = FindChildByName<Border>(columnGrid, "HoverSlotIndicator");
        var text = FindChildByName<TextBlock>(columnGrid, "HoverSlotText");
        if (indicator == null || text == null)
        {
            return;
        }

        text.Text = label;
        indicator.Width = Math.Max(0, columnGrid.ActualWidth - 8);
        indicator.Height = Math.Max(14, rowHeight - 2);
        Canvas.SetLeft(indicator, 4);
        Canvas.SetTop(indicator, row * rowHeight + 1);
        indicator.Visibility = Visibility.Visible;
    }

    private static void HideHoverIndicator(Grid columnGrid)
    {
        var indicator = FindChildByName<Border>(columnGrid, "HoverSlotIndicator");
        if (indicator != null)
        {
            indicator.Visibility = Visibility.Collapsed;
        }
    }

    private static void ShowDropGhost(
        Grid columnGrid,
        int startRow,
        int rowSpan,
        double rowHeight,
        string label,
        bool isAllowed)
    {
        var ghost = FindChildByName<Border>(columnGrid, "DropGhostIndicator");
        var text = FindChildByName<TextBlock>(columnGrid, "DropGhostText");
        if (ghost == null || text == null)
        {
            return;
        }

        ghost.Background = isAllowed ? ValidGhostBackground : BlockedGhostBackground;
        ghost.BorderBrush = isAllowed ? ValidGhostBorder : BlockedGhostBorder;
        text.Foreground = isAllowed ? ValidGhostText : BlockedGhostText;
        text.Text = label;

        double top = startRow * rowHeight + 1.5;
        double available = Math.Max(16, columnGrid.ActualHeight - top);
        double height = Math.Max(16, Math.Min(available, rowSpan * rowHeight - 3));

        ghost.Width = Math.Max(0, columnGrid.ActualWidth - 8);
        ghost.Height = height;
        Canvas.SetLeft(ghost, 4);
        Canvas.SetTop(ghost, top);
        ghost.Visibility = Visibility.Visible;
    }

    private static void HideDropGhost(Grid columnGrid)
    {
        var ghost = FindChildByName<Border>(columnGrid, "DropGhostIndicator");
        if (ghost != null)
        {
            ghost.Visibility = Visibility.Collapsed;
        }
    }

    private void HighlightPill(Border border)
    {
        if (ReferenceEquals(_highlightedPillBorder, border))
        {
            return;
        }

        ClearPillHighlight();
        _highlightedPillBorder = border;
        border.Background = DropTargetHighlightBackground;
        border.BorderBrush = DropTargetHighlightBorder;
    }

    private void ClearPillHighlight()
    {
        if (_highlightedPillBorder != null)
        {
            _highlightedPillBorder.ClearValue(Border.BackgroundProperty);
            _highlightedPillBorder.ClearValue(Border.BorderBrushProperty);
            _highlightedPillBorder = null;
        }
    }

    private void HighlightMonthCell(Border border)
    {
        if (ReferenceEquals(_highlightedMonthBorder, border))
        {
            return;
        }

        ClearMonthHighlight();
        _highlightedMonthBorder = border;
        border.BorderBrush = DropTargetHighlightBorder;
        border.BorderThickness = new Thickness(1.5);
    }

    private void ClearMonthHighlight()
    {
        if (_highlightedMonthBorder != null)
        {
            _highlightedMonthBorder.ClearValue(Border.BorderBrushProperty);
            _highlightedMonthBorder.ClearValue(Border.BorderThicknessProperty);
            _highlightedMonthBorder = null;
        }
    }

    private static T? FindChildByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && string.Equals(match.Name, name, StringComparison.Ordinal))
            {
                return match;
            }

            if (child is ItemsControl)
            {
                continue;
            }

            var nested = FindChildByName<T>(child, name);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static Brush CreateFrozenBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
