using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Occlusa.Desktop.ViewModels;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.Controls;

/// <summary>
/// Zero-scrollbar 3-Column Live Queue Kanban Board (Waiting, In Chair, Ready for Payment)
/// with dynamic per-column pagination and a collapsible bottom dock bar for Scheduled and Completed visits.
/// </summary>
public partial class LiveQueueKanbanView : UserControl
{
    private const double SimpleCardEstimatedHeight = 156.0;
    private const double DetailedCardEstimatedHeight = 198.0;
    private const double CardSpacing = 8.0;
    private const double TallResolutionThreshold = 1120.0;
    private const double CompactResolutionThreshold = 860.0;
    private const double DrawerRowHeight = 36.0;
    private const double DrawerGridChromeHeight = 82.0;

    private Window? _hostWindow;
    private MainViewModel? _subscribedVm;
    private double _lastColumnBodyHeight;

    public event EventHandler<QueueRowItem>? CardNextStageRequested;
    public event EventHandler<QueueRowItem>? CardDoubleClicked;
    public event EventHandler<QueueRowItem>? DrawerRowDoubleClicked;
    public event EventHandler<QueueRowItem>? ScheduledCheckInRequested;
    public event EventHandler<QueueRowItem>? ScheduledApproveAiRequested;
    public event EventHandler<QueueRowItem>? ScheduledRemindRequested;
    public event EventHandler<QueueRowItem>? CompletedOpenInvoiceRequested;

    public LiveQueueKanbanView()
    {
        InitializeComponent();
        Loaded += LiveQueueKanbanView_Loaded;
        Unloaded += LiveQueueKanbanView_Unloaded;
        DataContextChanged += LiveQueueKanbanView_DataContextChanged;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                UpdateDrawerResponsiveSizing();
                ApplyColumnCardCapacity(_lastColumnBodyHeight);
            }
        };
    }

    private void LiveQueueKanbanView_Loaded(object sender, RoutedEventArgs e)
    {
        AttachHostWindow();
        SubscribeViewModel(DataContext as MainViewModel);
        UpdateDrawerResponsiveSizing();
        ApplyColumnCardCapacity(_lastColumnBodyHeight);
    }

    private void LiveQueueKanbanView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_hostWindow is not null)
        {
            _hostWindow.SizeChanged -= HostWindow_SizeChanged;
            _hostWindow = null;
        }

        SubscribeViewModel(null);
    }

    private void LiveQueueKanbanView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SubscribeViewModel(e.NewValue as MainViewModel);
        ApplyColumnCardCapacity(_lastColumnBodyHeight);
    }

    private void AttachHostWindow()
    {
        var window = Window.GetWindow(this);
        if (ReferenceEquals(window, _hostWindow))
        {
            return;
        }

        if (_hostWindow is not null)
        {
            _hostWindow.SizeChanged -= HostWindow_SizeChanged;
        }

        _hostWindow = window;
        if (_hostWindow is not null)
        {
            _hostWindow.SizeChanged += HostWindow_SizeChanged;
        }
    }

    private void HostWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateDrawerResponsiveSizing();
    }

    private void SubscribeViewModel(MainViewModel? vm)
    {
        if (ReferenceEquals(_subscribedVm, vm))
        {
            return;
        }

        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _subscribedVm = vm;
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsDetailedQueueView)
            or nameof(MainViewModel.IsSimpleQueueView)
            or nameof(MainViewModel.ActiveQueueDrawer))
        {
            ApplyColumnCardCapacity(_lastColumnBodyHeight);
        }
    }

    private void ColumnBodyHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Height > 0)
        {
            _lastColumnBodyHeight = e.NewSize.Height;
            ApplyColumnCardCapacity(_lastColumnBodyHeight);
        }
    }

    private void ApplyColumnCardCapacity(double availableHeight)
    {
        if (_subscribedVm is null || availableHeight <= 0)
        {
            return;
        }

        double cardHeight = _subscribedVm.IsDetailedQueueView
            ? DetailedCardEstimatedHeight
            : SimpleCardEstimatedHeight;

        int perPage = AppointmentCalendarHelper.CalculateKanbanCardsPerPage(
            availableHeight,
            cardHeight,
            CardSpacing);

        _subscribedVm.WaitingColumn.SetCardsPerPage(perPage);
        _subscribedVm.InChairColumn.SetCardsPerPage(perPage);
        _subscribedVm.ReadyForPaymentColumn.SetCardsPerPage(perPage);
    }

    private void UpdateDrawerResponsiveSizing()
    {
        double windowHeight = _hostWindow?.ActualHeight ?? ActualHeight;
        if (windowHeight <= 0)
        {
            return;
        }

        int rows = windowHeight > TallResolutionThreshold
            ? 6
            : (windowHeight < CompactResolutionThreshold ? 3 : 4);

        double gridHeight = DrawerGridChromeHeight + (rows * DrawerRowHeight);
        ScheduledDrawerGrid.PageSize = rows;
        CompletedDrawerGrid.PageSize = rows;
        ScheduledDrawerGridRow.Height = new GridLength(gridHeight);
        CompletedDrawerGridRow.Height = new GridLength(gridHeight);
    }

    private void KanbanCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (HasAncestor<ButtonBase>(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (sender is FrameworkElement element && element.DataContext is QueueRowItem row)
        {
            _subscribedVm?.SelectQueueCardCommand.Execute(row);
            if (e.ClickCount == 2)
            {
                e.Handled = true;
                CardDoubleClicked?.Invoke(this, row);
            }
        }
    }

    private void CardNextStageButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement element && element.DataContext is QueueRowItem row)
        {
            _subscribedVm?.SelectQueueCardCommand.Execute(row);
            CardNextStageRequested?.Invoke(this, row);
        }
    }

    private void ScheduledCheckInButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement element && element.DataContext is QueueRowItem row)
        {
            _subscribedVm?.SelectQueueCardCommand.Execute(row);
            ScheduledCheckInRequested?.Invoke(this, row);
        }
    }

    private void ScheduledApproveAiButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement element && element.DataContext is QueueRowItem row)
        {
            _subscribedVm?.SelectQueueCardCommand.Execute(row);
            ScheduledApproveAiRequested?.Invoke(this, row);
        }
    }

    private void ScheduledRemindSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_subscribedVm?.SelectedQueueRow is { } row)
        {
            ScheduledRemindRequested?.Invoke(this, row);
        }
    }

    private void CompletedOpenInvoiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_subscribedVm?.SelectedQueueRow is { } row)
        {
            CompletedOpenInvoiceRequested?.Invoke(this, row);
        }
    }

    private void DrawerGrid_RowDoubleClicked(object? sender, object? rowObj)
    {
        if (rowObj is QueueRowItem row)
        {
            DrawerRowDoubleClicked?.Invoke(this, row);
        }
    }

    private static bool HasAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T)
            {
                return true;
            }

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }
}

