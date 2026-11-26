using System.Windows;
using System.Windows.Input;
using Occlusa.Desktop.ViewModels;

namespace Occlusa.Desktop.Views;

/// <summary>
/// True modal dialog hosting a single Patient Visit's Kreloses-style Anatomical + 5-Surface
/// Odontogram (Permanent 11-48 &amp; Primary 51-85) and Clinical SOAP Notes (100% free of monetary fields).
/// </summary>
public partial class VisitRecordModal : Window
{
    private readonly VisitRecordViewModel _viewModel;

    public VisitRecordModal(VisitRecordViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.CheckoutInvoiceRequested += (_, _) => DialogShell.ForceCloseWithoutPrompt();
        Loaded += async (_, _) => await _viewModel.RefreshVisitFromServerAsync();
    }

    private void SurfaceTop_MouseDown(object sender, MouseButtonEventArgs e) =>
        _viewModel.TogglePositionSurfaceCommand.Execute("Top");

    private void SurfaceBottom_MouseDown(object sender, MouseButtonEventArgs e) =>
        _viewModel.TogglePositionSurfaceCommand.Execute("Bottom");

    private void SurfaceLeft_MouseDown(object sender, MouseButtonEventArgs e) =>
        _viewModel.TogglePositionSurfaceCommand.Execute("Left");

    private void SurfaceRight_MouseDown(object sender, MouseButtonEventArgs e) =>
        _viewModel.TogglePositionSurfaceCommand.Execute("Right");

    private void SurfaceCenter_MouseDown(object sender, MouseButtonEventArgs e) =>
        _viewModel.TogglePositionSurfaceCommand.Execute("Center");
}
