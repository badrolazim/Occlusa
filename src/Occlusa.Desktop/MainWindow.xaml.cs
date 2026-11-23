using System.Windows;
using Occlusa.Desktop.Services;
using Occlusa.Desktop.ViewModels;
using Occlusa.Desktop.Views;
using Occlusa.Shared.Models;

namespace Occlusa.Desktop;

/// <summary>
/// Main 4-workspace single-screen clinical window opening True Modal Popups via ShowDialog().
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly OcclusaApiClient _apiClient;

    public MainWindow(MainViewModel viewModel, OcclusaApiClient apiClient, MyKadPcscReaderService myKadService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _apiClient = apiClient;
        DataContext = _viewModel;

        _viewModel.Appointments.AppointmentOpenRequested += (_, appt) =>
            OpenAppointmentModal(appt, prefill: null);
        _viewModel.Appointments.QuickBookRequested += (_, prefill) =>
            OpenAppointmentModal(existing: null, prefill: prefill);
    }

    private void QueuePagedGrid_RowDoubleClicked(object? sender, object? rowObj)
    {
        if (rowObj is not QueueRowItem row)
        {
            return;
        }

        if (row.Source.Status == QueueStatus.InChair)
        {
            OpenOdontogramForPatientId(row.Source.PatientId, row.Source.PatientName, row.Source.PatientIc);
        }
        else
        {
            OpenPatientModalById(row.Source.PatientId, row.Source.PatientName, row.Source.PatientIc);
        }
    }

    private void PatientsPagedGrid_RowDoubleClicked(object? sender, object? rowObj)
    {
        if (rowObj is PatientRowItem row)
        {
            OpenPatientModal(row.Source);
        }
    }

    private void InvoicesPagedGrid_RowDoubleClicked(object? sender, object? rowObj)
    {
        if (rowObj is InvoiceRowItem row)
        {
            OpenInvoiceModal(row.Source);
        }
    }

    private void WhatsAppPagedGrid_RowDoubleClicked(object? sender, object? rowObj)
    {
        if (rowObj is WhatsAppThreadRowItem row)
        {
            OpenWhatsAppModal(row.Source);
        }
    }

    private void OpenSelectedQueuePatientButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedQueueRow is { } row)
        {
            OpenPatientModalById(row.Source.PatientId, row.Source.PatientName, row.Source.PatientIc);
        }
        else if (_viewModel.SelectedPatient != null)
        {
            OpenPatientModal(_viewModel.SelectedPatient);
        }
    }

    private void OpenSelectedQueueOdontogramButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedQueueRow is { } row)
        {
            OpenOdontogramForPatientId(row.Source.PatientId, row.Source.PatientName, row.Source.PatientIc);
        }
        else if (_viewModel.SelectedPatient != null)
        {
            OpenOdontogramModal(_viewModel.SelectedPatient, this);
        }
    }

    private void OpenSelectedPatientButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPatientRow is { } row)
        {
            OpenPatientModal(row.Source);
        }
        else if (_viewModel.SelectedPatient != null)
        {
            OpenPatientModal(_viewModel.SelectedPatient);
        }
    }

    private void OpenSelectedInvoiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedInvoiceRow is { } row)
        {
            OpenInvoiceModal(row.Source);
        }
    }

    private void OpenDailyReconciliationButton_Click(object sender, RoutedEventArgs e)
    {
        var vm = new DailyReconciliationViewModel(
            _viewModel.SelectedBranchId,
            _viewModel.LatestReconciliation,
            _apiClient);

        var modal = new DailyReconciliationModal(vm)
        {
            Owner = this
        };
        modal.ShowDialog();
        _ = _viewModel.RefreshSnapshotAsync();
    }

    private void OpenWhatsAppConsoleButton_Click(object sender, RoutedEventArgs e)
    {
        OpenWhatsAppModal(_viewModel.SelectedWhatsAppRow?.Source);
    }

    private void NewAppointmentButton_Click(object sender, RoutedEventArgs e)
    {
        OpenAppointmentModal(existing: null, prefill: null);
    }

    private void RegisterPatientAndBookButton_Click(object sender, RoutedEventArgs e)
    {
        OpenAppointmentModal(existing: null, prefill: null, startInNewPatientMode: true);
    }

    private void OpenAppointmentModal(
        AppointmentQueueItemDto? existing,
        QuickBookSlotArgs? prefill,
        bool startInNewPatientMode = false)
    {
        AppointmentDetailModal? modal = null;
        var vm = new AppointmentDetailViewModel(
            _viewModel.SelectedBranchId,
            _viewModel.AllPatients,
            _viewModel.Dentists,
            _viewModel.Treatments,
            _viewModel.AllQueueItems,
            _apiClient,
            existing,
            prefill,
            saved =>
            {
                _viewModel.MergeAppointment(saved);
                _ = _viewModel.RefreshSnapshotAsync();
            },
            registeredPatient =>
            {
                _viewModel.MergePatient(registeredPatient);
                _viewModel.SelectedPatient = registeredPatient;
            },
            startInNewPatientMode);

        modal = new AppointmentDetailModal(
            vm,
            patient => OpenPatientModal(patient),
            patient => OpenOdontogramModal(patient, modal))
        {
            Owner = this
        };
        modal.ShowDialog();
    }

    private void OpenPatientModalById(Guid patientId, string fallbackName, string fallbackIc)
    {
        var patient = _viewModel.FindPatient(patientId) ?? new PatientDto
        {
            Id = patientId,
            FullName = fallbackName,
            IcNumber = fallbackIc,
            PrimaryBranchId = _viewModel.SelectedBranchId
        };
        OpenPatientModal(patient);
    }

    private void OpenOdontogramForPatientId(Guid patientId, string fallbackName, string fallbackIc)
    {
        var patient = _viewModel.FindPatient(patientId) ?? new PatientDto
        {
            Id = patientId,
            FullName = fallbackName,
            IcNumber = fallbackIc,
            PrimaryBranchId = _viewModel.SelectedBranchId
        };
        OpenOdontogramModal(patient, this);
    }

    private void OpenPatientModal(PatientDto patient)
    {
        PatientDetailModal? modal = null;
        var vm = new PatientDetailViewModel(
            patient,
            _viewModel.SelectedBranchId,
            _viewModel.SoapMacros,
            _apiClient,
            updated =>
            {
                _viewModel.MergePatient(updated);
                _ = _viewModel.RefreshSnapshotAsync();
            });

        modal = new PatientDetailModal(vm, () => OpenOdontogramModal(vm.Patient, modal))
        {
            Owner = this
        };
        modal.ShowDialog();
    }

    private void OpenOdontogramModal(PatientDto patient, Window? owner)
    {
        var vm = new OdontogramViewModel(
            patient,
            _viewModel.SelectedBranchId,
            _apiClient,
            () => _ = _viewModel.RefreshSnapshotAsync());

        var modal = new OdontogramModal(vm)
        {
            Owner = owner ?? this
        };
        modal.ShowDialog();
    }

    private void OpenInvoiceModal(InvoiceDto invoice)
    {
        var vm = new InvoiceCheckoutViewModel(
            invoice,
            _apiClient,
            _inv => _ = _viewModel.RefreshSnapshotAsync());

        var modal = new InvoiceCheckoutModal(vm)
        {
            Owner = this
        };
        modal.ShowDialog();
    }

    private void OpenWhatsAppModal(WhatsAppThreadDto? thread)
    {
        var vm = new WhatsAppConsoleViewModel(
            thread,
            _viewModel.SelectedBranchId,
            _viewModel.AllQueueItems,
            _apiClient,
            () => _ = _viewModel.RefreshSnapshotAsync());

        var modal = new WhatsAppConsoleModal(vm)
        {
            Owner = this
        };
        modal.ShowDialog();
    }
}
