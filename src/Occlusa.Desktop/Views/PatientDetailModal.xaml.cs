using System.Windows;
using Occlusa.Desktop.ViewModels;
using Occlusa.Shared.Models;

namespace Occlusa.Desktop.Views;

/// <summary>
/// True modal window for standalone patient registration, full demographic/safety editing,
/// and 0..N chronological Patient Visit Records navigation.
/// </summary>
public partial class PatientDetailModal : Window
{
    private readonly PatientDetailViewModel _viewModel;
    private readonly Action<PatientDto, PatientVisitDto> _openVisitAction;

    public PatientDetailModal(
        PatientDetailViewModel viewModel,
        Action<PatientDto, PatientVisitDto> openVisitAction)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _openVisitAction = openVisitAction;
        DataContext = _viewModel;

        Loaded += async (_, _) => await _viewModel.LoadVisitsAsync();
    }

    private void VisitsPagedGrid_RowDoubleClicked(object? sender, object? rowObj)
    {
        if (rowObj is PatientVisitRowItem row)
        {
            OpenVisitAndRefresh(row.Source);
        }
    }

    private void OpenSelectedVisitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedVisitRow is { } row)
        {
            OpenVisitAndRefresh(row.Source);
        }
    }

    private async void CreateNewVisitButton_Click(object sender, RoutedEventArgs e)
    {
        var created = await _viewModel.CreateNewVisitRecordAsync();
        if (created is not null)
        {
            OpenVisitAndRefresh(created);
        }
    }

    private void OpenVisitAndRefresh(PatientVisitDto visit)
    {
        _openVisitAction(_viewModel.Patient, visit);
        _ = _viewModel.LoadVisitsAsync();
    }
}
