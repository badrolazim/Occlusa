using System.Windows;
using System.Windows.Controls;
using Occlusa.Desktop.ViewModels;

namespace Occlusa.Desktop.Controls;

/// <summary>
/// Appointments workspace canvas housing both the Calendar (Day / Week / Month)
/// and 4-column UniformPagedGrid Table with zero scrollbars and responsive capacity.
/// </summary>
public partial class AppointmentsWorkspaceView : UserControl
{
    private Window? _hostWindow;

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
}

