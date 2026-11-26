using System.Windows;
using Occlusa.Desktop.ViewModels;
using Occlusa.Shared.Models;

namespace Occlusa.Desktop.Views;

/// <summary>
/// True modal window for viewing, booking, rescheduling, and cancelling appointments.
/// </summary>
public partial class AppointmentDetailModal : Window
{
    private readonly AppointmentDetailViewModel _viewModel;
    private readonly Action<PatientDto>? _openPatientAction;
    private readonly Action<PatientDto>? _openOdontogramAction;
    private readonly Action<AppointmentQueueItemDto, Action<AppointmentQueueItemDto>>? _openReminderAction;

    public AppointmentDetailModal(
        AppointmentDetailViewModel viewModel,
        Action<PatientDto>? openPatientAction = null,
        Action<PatientDto>? openOdontogramAction = null,
        Action<AppointmentQueueItemDto, Action<AppointmentQueueItemDto>>? openReminderAction = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _openPatientAction = openPatientAction;
        _openOdontogramAction = openOdontogramAction;
        _openReminderAction = openReminderAction;
        DataContext = _viewModel;

        _viewModel.RequestCloseApproved += () => DialogShell.ForceCloseWithoutPrompt();
    }

    private void OpenReminderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ExistingAppointment is { } appt)
        {
            _openReminderAction?.Invoke(appt, updated => _viewModel.ApplyReminderUpdated(updated));
        }
    }

    private void OpenPatientChartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentPatientForNavigation is { } patient)
        {
            _openPatientAction?.Invoke(patient);
        }
    }

    private void OpenOdontogramButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentPatientForNavigation is { } patient)
        {
            _openOdontogramAction?.Invoke(patient);
        }
    }
}

