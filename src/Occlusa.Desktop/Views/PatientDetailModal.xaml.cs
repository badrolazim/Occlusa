using System.Windows;
using Occlusa.Desktop.ViewModels;

namespace Occlusa.Desktop.Views;

/// <summary>
/// True modal window for viewing/editing patient clinical chart, alerts, consent, and SOAP notes.
/// </summary>
public partial class PatientDetailModal : Window
{
    private readonly PatientDetailViewModel _viewModel;
    private readonly Action _openOdontogramAction;

    public PatientDetailModal(PatientDetailViewModel viewModel, Action openOdontogramAction)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _openOdontogramAction = openOdontogramAction;
        DataContext = _viewModel;

        Loaded += async (_, _) => await _viewModel.LoadSoapHistoryAsync();
    }

    private void OpenOdontogramButton_Click(object sender, RoutedEventArgs e)
    {
        _openOdontogramAction();
    }
}
