using System.Windows;
using Occlusa.Desktop.Services;
using Occlusa.Desktop.ViewModels;
using Occlusa.Desktop.Views;
using Occlusa.Shared.Models;

namespace Occlusa.Desktop;

/// <summary>
/// Main 6-workspace single-screen clinical window opening True Modal Popups via ShowDialog().
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
        _viewModel.WalkInIntakeRequested += (_, patient) =>
            OpenWalkInModal(patient);

        _viewModel.BillingSettings.OpenCatalogItemModalRequested += (_, item) =>
            OpenBillingCatalogItemModal(item);
        _viewModel.BillingSettings.OpenTreatmentRuleModalRequested += (_, args) =>
            OpenTreatmentSuggestionRuleModal(args.ProcedureItem, args.DentistId, args.DoctorName);

        Closed += (_, _) => _viewModel.StopTimers();
    }

    private async void QueuePagedGrid_RowDoubleClicked(object? sender, object? rowObj)
    {
        if (rowObj is not QueueRowItem row)
        {
            return;
        }

        if (row.Source.Status == QueueStatus.AwaitingCheckout)
        {
            await OpenCheckoutForQueueItemAsync(row.Source);
        }
        else if (row.Source.Status == QueueStatus.InChair || row.Source.VisitId.HasValue)
        {
            await OpenVisitForQueueItemAsync(row.Source, this);
        }
        else
        {
            OpenAppointmentModal(row.Source, prefill: null);
        }
    }

    private void PatientsPagedGrid_RowDoubleClicked(object? sender, object? rowObj)
    {
        if (rowObj is PatientRowItem row)
        {
            OpenPatientModal(row.Source);
        }
    }

    private async void InvoicesPagedGrid_RowDoubleClicked(object? sender, object? rowObj)
    {
        if (rowObj is InvoiceRowItem row)
        {
            await OpenExistingInvoiceModalAsync(row.Source);
        }
    }

    private void WhatsAppPagedGrid_RowDoubleClicked(object? sender, object? rowObj)
    {
        if (rowObj is WhatsAppThreadRowItem row)
        {
            OpenWhatsAppModal(row.Source);
        }
    }

    private void BillingCatalogPagedGrid_RowDoubleClicked(object? sender, object? rowObj)
    {
        if (rowObj is BillingCatalogRowItem row)
        {
            OpenBillingCatalogItemModal(row.Source);
        }
    }

    private void DoctorRulesPagedGrid_RowDoubleClicked(object? sender, object? rowObj)
    {
        if (rowObj is DoctorTreatmentRuleRowItem row && _viewModel.BillingSettings.SelectedDoctorScope is { } scope)
        {
            OpenTreatmentSuggestionRuleModal(row.ProcedureItem, scope.DentistId, scope.DisplayName);
        }
    }

    private async void CheckoutSelectedQueuePatientButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedQueueRow is { } row)
        {
            await OpenCheckoutForQueueItemAsync(row.Source);
        }
    }

    private void OpenSelectedQueueVisitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedQueueRow is { } row)
        {
            OpenAppointmentModal(row.Source, prefill: null);
        }
    }

    private void NewWalkInButton_Click(object sender, RoutedEventArgs e)
    {
        OpenWalkInModal(preselectedPatient: null);
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

    private async void OpenSelectedQueueOdontogramButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedQueueRow is { } row)
        {
            await OpenVisitForQueueItemAsync(row.Source, this);
        }
        else if (_viewModel.SelectedPatient != null)
        {
            await OpenLatestOrCreateVisitForPatientAsync(_viewModel.SelectedPatient, this);
        }
    }

    private void RegisterStandalonePatientButton_Click(object sender, RoutedEventArgs e)
    {
        OpenPatientModal(patient: null);
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

    private void NewRetailInvoiceButton_Click(object sender, RoutedEventArgs e)
    {
        OpenInvoiceBuilderModal(
            existingInvoice: null,
            preselectedPatient: null,
            visit: null,
            appointment: null,
            startAsWalkInRetail: true,
            isDentistCheckout: false);
    }

    private void NewPatientInvoiceButton_Click(object sender, RoutedEventArgs e)
    {
        OpenInvoiceBuilderModal(
            existingInvoice: null,
            preselectedPatient: _viewModel.SelectedPatient,
            visit: null,
            appointment: null,
            startAsWalkInRetail: false,
            isDentistCheckout: false);
    }

    private async void OpenSelectedInvoiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedInvoiceRow is { } row)
        {
            await OpenExistingInvoiceModalAsync(row.Source);
        }
    }

    private async void SaveDoctorModeButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.BillingSettings.SaveCurrentDoctorModeAsync(_viewModel.SelectedStaffName);
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

    private void OpenWalkInModal(PatientDto? preselectedPatient)
    {
        OpenAppointmentModal(
            existing: null,
            prefill: null,
            startInNewPatientMode: false,
            isWalkInMode: true,
            preselectedPatient: preselectedPatient);
    }

    private void OpenAppointmentModal(
        AppointmentQueueItemDto? existing,
        QuickBookSlotArgs? prefill,
        bool startInNewPatientMode = false,
        bool isWalkInMode = false,
        PatientDto? preselectedPatient = null)
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
            startInNewPatientMode,
            isWalkInMode,
            preselectedPatient,
            _viewModel.SelectedStaffName);

        modal = new AppointmentDetailModal(
            vm,
            patient => OpenPatientModal(patient),
            async patient =>
            {
                if (existing is not null)
                {
                    await OpenVisitForQueueItemAsync(existing, modal);
                }
                else
                {
                    await OpenLatestOrCreateVisitForPatientAsync(patient, modal);
                }
            })
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

    private void OpenPatientModal(PatientDto? patient)
    {
        PatientDetailModal? modal = null;
        var defaultDoc = _viewModel.Dentists.FirstOrDefault()?.FullName ?? "Dr. Aina Rahman";
        var vm = new PatientDetailViewModel(
            patient,
            _viewModel.SelectedBranchId,
            _viewModel.SelectedStaffName,
            defaultDoc,
            _apiClient,
            updated =>
            {
                _viewModel.MergePatient(updated);
                _ = _viewModel.RefreshSnapshotAsync();
            });

        modal = new PatientDetailModal(
            vm,
            (pat, visit) => OpenVisitRecordModal(pat, visit, modal))
        {
            Owner = this
        };
        modal.ShowDialog();
    }

    private async Task OpenVisitForQueueItemAsync(AppointmentQueueItemDto queueItem, Window? owner)
    {
        try
        {
            var visit = await _apiClient.EnsureVisitForAppointmentAsync(queueItem.Id, _viewModel.SelectedStaffName);
            var patient = _viewModel.FindPatient(queueItem.PatientId) ?? new PatientDto
            {
                Id = queueItem.PatientId,
                FullName = queueItem.PatientName,
                IcNumber = queueItem.PatientIc,
                PrimaryBranchId = queueItem.BranchId
            };

            _ = _viewModel.RefreshSnapshotAsync();
            OpenVisitRecordModal(patient, visit, owner);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open clinical visit record: {ex.Message}",
                "Visit Record Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task OpenLatestOrCreateVisitForPatientAsync(PatientDto patient, Window? owner)
    {
        try
        {
            var visits = await _apiClient.GetPatientVisitsAsync(patient.Id);
            var target = visits.OrderByDescending(v => v.VisitDate).ThenByDescending(v => v.VisitNumber).FirstOrDefault();
            if (target is null)
            {
                target = await _apiClient.CreatePatientVisitAsync(patient.Id, new CreatePatientVisitRequest
                {
                    BranchId = _viewModel.SelectedBranchId,
                    DoctorName = _viewModel.Dentists.FirstOrDefault()?.FullName ?? "Dr. Aina Rahman",
                    InitMode = OdontogramInitMode.CarryForward,
                    CreatedBy = _viewModel.SelectedStaffName
                });
                _ = _viewModel.RefreshSnapshotAsync();
            }

            OpenVisitRecordModal(patient, target, owner);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open visit chart: {ex.Message}",
                "Visit Chart Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenVisitRecordModal(PatientDto patient, PatientVisitDto visit, Window? owner)
    {
        PatientVisitDto? checkoutTarget = null;
        var vm = new VisitRecordViewModel(
            patient,
            visit,
            _viewModel.Dentists,
            _viewModel.SoapMacros,
            _apiClient,
            _viewModel.SelectedStaffName,
            VisitWorkspaceTab.Odontogram,
            savedVisit => { _ = _viewModel.RefreshSnapshotAsync(); },
            savedForCheckout => checkoutTarget = savedForCheckout,
            _viewModel.BillingSettings.CurrentSettings.CatalogItems);

        var modal = new VisitRecordModal(vm)
        {
            Owner = owner ?? this
        };
        modal.ShowDialog();
        _ = _viewModel.RefreshSnapshotAsync();

        if (checkoutTarget is not null)
        {
            var linkedAppt = checkoutTarget.AppointmentId.HasValue
                ? _viewModel.AllQueueItems.FirstOrDefault(q => q.Id == checkoutTarget.AppointmentId.Value)
                : null;
            var existingInv = _viewModel.FindActiveInvoiceForVisitOrAppointment(checkoutTarget.Id, checkoutTarget.AppointmentId);
            OpenInvoiceBuilderModal(
                existingInvoice: existingInv,
                preselectedPatient: _viewModel.FindPatient(patient.Id) ?? patient,
                visit: checkoutTarget,
                appointment: linkedAppt,
                startAsWalkInRetail: false,
                isDentistCheckout: linkedAppt?.Status == QueueStatus.InChair || existingInv is null,
                owner: owner ?? this);
        }
    }

    private async Task OpenCheckoutForQueueItemAsync(AppointmentQueueItemDto queueItem)
    {
        try
        {
            PatientVisitDto? visit = null;
            if (queueItem.VisitId.HasValue)
            {
                visit = await _apiClient.GetVisitAsync(queueItem.VisitId.Value);
            }

            visit ??= await _apiClient.EnsureVisitForAppointmentAsync(queueItem.Id, _viewModel.SelectedStaffName);
            var patient = _viewModel.FindPatient(queueItem.PatientId) ?? new PatientDto
            {
                Id = queueItem.PatientId,
                FullName = queueItem.PatientName,
                IcNumber = queueItem.PatientIc,
                PrimaryBranchId = queueItem.BranchId
            };

            var existingInvoice = _viewModel.FindActiveInvoiceForVisitOrAppointment(visit.Id, queueItem.Id);
            bool dentistCheckout = queueItem.Status == QueueStatus.InChair && existingInvoice is null;

            OpenInvoiceBuilderModal(
                existingInvoice: existingInvoice,
                preselectedPatient: patient,
                visit: visit,
                appointment: queueItem,
                startAsWalkInRetail: false,
                isDentistCheckout: dentistCheckout);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not initiate checkout invoice: {ex.Message}",
                "Checkout Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task OpenExistingInvoiceModalAsync(InvoiceDto invoice)
    {
        PatientVisitDto? visit = null;
        if (invoice.VisitId.HasValue && invoice.Payments.Count == 0 && !invoice.IsVoided)
        {
            try
            {
                visit = await _apiClient.GetVisitAsync(invoice.VisitId.Value);
            }
            catch
            {
                // Proceed even if clinical visit cannot be prefetched.
            }
        }

        var linkedAppt = invoice.AppointmentId.HasValue
            ? _viewModel.AllQueueItems.FirstOrDefault(a => a.Id == invoice.AppointmentId.Value)
            : null;
        var patient = !invoice.IsWalkInRetail && invoice.PatientId != Guid.Empty
            ? _viewModel.FindPatient(invoice.PatientId)
            : null;

        OpenInvoiceBuilderModal(
            existingInvoice: invoice,
            preselectedPatient: patient,
            visit: visit,
            appointment: linkedAppt,
            startAsWalkInRetail: invoice.IsWalkInRetail,
            isDentistCheckout: false);
    }

    private void OpenInvoiceBuilderModal(
        InvoiceDto? existingInvoice,
        PatientDto? preselectedPatient,
        PatientVisitDto? visit,
        AppointmentQueueItemDto? appointment,
        bool startAsWalkInRetail,
        bool isDentistCheckout,
        Window? owner = null)
    {
        var vm = new InvoiceCheckoutViewModel(
            existingInvoice,
            _apiClient,
            inv => { _ = _viewModel.RefreshSnapshotAsync(); },
            _viewModel.SelectedBranchId,
            _viewModel.SelectedStaffName,
            _viewModel.AllPatients,
            _viewModel.Dentists,
            _viewModel.BillingSettings.CurrentSettings,
            preselectedPatient,
            visit,
            appointment,
            startAsWalkInRetail,
            isDentistCheckout);

        var modal = new InvoiceCheckoutModal(vm)
        {
            Owner = owner ?? this
        };
        modal.ShowDialog();
        _ = _viewModel.RefreshSnapshotAsync();
    }

    private void OpenBillingCatalogItemModal(BillingCatalogItemDto? existing)
    {
        var vm = new BillingCatalogItemViewModel(
            existing,
            _apiClient,
            _viewModel.SelectedStaffName,
            saved =>
            {
                _viewModel.BillingSettings.MergeSavedCatalogItem(saved);
                _ = _viewModel.RefreshSnapshotAsync();
            });

        var modal = new BillingCatalogItemModal(vm)
        {
            Owner = this
        };
        modal.ShowDialog();
    }

    private void OpenTreatmentSuggestionRuleModal(BillingCatalogItemDto procedureItem, Guid? dentistId, string doctorName)
    {
        var editable = _viewModel.BillingSettings.ResolveEditablePreference(dentistId, doctorName);
        var clinicDefault = _viewModel.BillingSettings.GetClinicDefaultPreference();

        var vm = new TreatmentSuggestionRuleViewModel(
            procedureItem,
            dentistId,
            doctorName,
            editable,
            clinicDefault,
            _viewModel.BillingSettings.CurrentSettings.CatalogItems,
            _apiClient,
            _viewModel.SelectedStaffName,
            saved => _viewModel.BillingSettings.MergeSavedPreference(saved));

        var modal = new TreatmentSuggestionRuleModal(vm)
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
            () => _ = _viewModel.RefreshSnapshotAsync(),
            _viewModel.SelectedStaffName);

        var modal = new WhatsAppConsoleModal(vm)
        {
            Owner = this
        };
        modal.ShowDialog();
    }
}
