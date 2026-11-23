using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Occlusa.Desktop.Services;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.ViewModels;

/// <summary>
/// Primary workspace ViewModel coordinating the 5 single-screen tabs, branch switching,
/// live SignalR + 5s polling fallback, PC/SC MyKad registration, and pinned MedicalAlertsBanner.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    public const int WorkspaceQueue = 0;
    public const int WorkspaceAppointments = 1;
    public const int WorkspacePatients = 2;
    public const int WorkspaceBilling = 3;
    public const int WorkspaceWhatsApp = 4;

    private readonly OcclusaApiClient _apiClient;
    private readonly MyKadPcscReaderService _myKadService;
    private readonly Func<Task<bool>>? _ensureLocalApiAsync;

    private List<PatientDto> _allPatients = new();
    private List<DentistDto> _dentists = new();
    private List<TreatmentDto> _treatments = new();
    private List<AppointmentQueueItemDto> _allQueue = new();
    private List<InvoiceDto> _allInvoices = new();
    private List<WhatsAppThreadDto> _allThreads = new();
    private List<SoapMacroDto> _soapMacros = new();
    private DailySalesReconciliationDto? _latestReconciliation;

    [ObservableProperty]
    private string _selectedBranchId = "HQ-KL";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQueueWorkspace))]
    [NotifyPropertyChangedFor(nameof(IsAppointmentsWorkspace))]
    [NotifyPropertyChangedFor(nameof(IsPatientsWorkspace))]
    [NotifyPropertyChangedFor(nameof(IsBillingWorkspace))]
    [NotifyPropertyChangedFor(nameof(IsWhatsAppWorkspace))]
    [NotifyPropertyChangedFor(nameof(ActiveWorkspaceTitle))]
    [NotifyPropertyChangedFor(nameof(ActiveWorkspaceSubtitle))]
    private int _activeWorkspaceIndex;

    [ObservableProperty]
    private int _queueCount;

    [ObservableProperty]
    private int _appointmentCount;

    [ObservableProperty]
    private int _patientCount;

    [ObservableProperty]
    private int _invoiceCount;

    [ObservableProperty]
    private int _whatsAppCount;

    [ObservableProperty]
    private PatientDto? _selectedPatient;

    [ObservableProperty]
    private string _bannerPatientName = string.Empty;

    [ObservableProperty]
    private string _bannerIcNumber = string.Empty;

    [ObservableProperty]
    private bool _bannerIsMyKadVerified;

    [ObservableProperty]
    private string _bannerAllergies = string.Empty;

    [ObservableProperty]
    private bool _bannerOnAnticoagulants;

    [ObservableProperty]
    private bool _bannerHasDiabetes;

    [ObservableProperty]
    private bool _bannerIsPregnant;

    [ObservableProperty]
    private string _bannerAlertNotes = string.Empty;

    [ObservableProperty]
    private QueueRowItem? _selectedQueueRow;

    [ObservableProperty]
    private PatientRowItem? _selectedPatientRow;

    [ObservableProperty]
    private InvoiceRowItem? _selectedInvoiceRow;

    [ObservableProperty]
    private WhatsAppThreadRowItem? _selectedWhatsAppRow;

    [ObservableProperty]
    private string _patientSearchText = string.Empty;

    [ObservableProperty]
    private int _selectedSimulatedProfileIndex;

    [ObservableProperty]
    private bool _isSignalRConnected;

    [ObservableProperty]
    private string _connectionModeLabel = "Fallback Poll (5s)";

    [ObservableProperty]
    private string _databaseModeLabel = "Connecting...";

    [ObservableProperty]
    private string _myKadReaderStatus = "Checking PC/SC reader...";

    [ObservableProperty]
    private string _statusMessage = "Initializing Occlusa workspace...";

    [ObservableProperty]
    private int _emergencyAlertCount;

    [ObservableProperty]
    private int _aiBookedPendingCount;

    [ObservableProperty]
    private bool _hasEmergencyAlerts;

    [ObservableProperty]
    private bool _hasAiBookedPending;

    public MainViewModel(
        OcclusaApiClient apiClient,
        MyKadPcscReaderService myKadService,
        Func<Task<bool>>? ensureLocalApiAsync = null)
    {
        _apiClient = apiClient;
        _myKadService = myKadService;
        _ensureLocalApiAsync = ensureLocalApiAsync;

        AvailableBranches = new ObservableCollection<string> { "HQ-KL", "PJ-SS2" };
        SimulatedProfileNames = new ObservableCollection<string>(
            _myKadService.SimulatedProfiles.Select(p => $"{p.FullName} ({p.IcNumber})"));

        QueueRows = new ObservableCollection<QueueRowItem>();
        PatientRows = new ObservableCollection<PatientRowItem>();
        InvoiceRows = new ObservableCollection<InvoiceRowItem>();
        WhatsAppRows = new ObservableCollection<WhatsAppThreadRowItem>();

        Appointments = new AppointmentsViewModel();
        Appointments.AppointmentSelectedForBanner += (_, appt) =>
            SelectPatientById(appt.PatientId, appt.PatientName, appt.PatientIc);

        _apiClient.ConnectionModeChanged += (_, connected) => RunOnUi(() =>
        {
            IsSignalRConnected = connected;
            ConnectionModeLabel = connected ? "Live (SignalR)" : "Fallback Poll (5s)";
        });

        _apiClient.FallbackSnapshotPolled += (_, snapshot) => RunOnUi(() =>
        {
            ApplySnapshot(snapshot);
            StatusMessage = $"Synced via 5s fallback poll at {DateTime.Now:HH:mm:ss}";
        });

        _apiClient.QueueUpdated += (_, item) => RunOnUi(() => UpsertQueueItem(item));
        _apiClient.InvoiceUpdated += (_, invoice) => RunOnUi(() => UpsertInvoiceItem(invoice));
        _apiClient.WhatsAppUpdated += (_, thread) => RunOnUi(() => UpsertWhatsAppThread(thread, isEmergency: false));
        _apiClient.EmergencyEscalated += (_, thread) => RunOnUi(() => UpsertWhatsAppThread(thread, isEmergency: true));

        _myKadService.ReaderStatusChanged += (_, msg) => RunOnUi(() => MyKadReaderStatus = msg);
        _myKadService.MyKadDetected += (_, result) => RunOnUi(() => _ = HandleMyKadReadResultAsync(result));
    }

    public ObservableCollection<string> AvailableBranches { get; }
    public ObservableCollection<string> SimulatedProfileNames { get; }
    public ObservableCollection<QueueRowItem> QueueRows { get; }
    public ObservableCollection<PatientRowItem> PatientRows { get; }
    public ObservableCollection<InvoiceRowItem> InvoiceRows { get; }
    public ObservableCollection<WhatsAppThreadRowItem> WhatsAppRows { get; }

    public AppointmentsViewModel Appointments { get; }
    public IReadOnlyList<PatientDto> AllPatients => _allPatients;
    public IReadOnlyList<DentistDto> Dentists => _dentists;
    public IReadOnlyList<TreatmentDto> Treatments => _treatments;
    public IReadOnlyList<SoapMacroDto> SoapMacros => _soapMacros;
    public IReadOnlyList<AppointmentQueueItemDto> AllQueueItems => _allQueue;
    public DailySalesReconciliationDto? LatestReconciliation => _latestReconciliation;

    public bool IsQueueWorkspace => ActiveWorkspaceIndex == WorkspaceQueue;
    public bool IsAppointmentsWorkspace => ActiveWorkspaceIndex == WorkspaceAppointments;
    public bool IsPatientsWorkspace => ActiveWorkspaceIndex == WorkspacePatients;
    public bool IsBillingWorkspace => ActiveWorkspaceIndex == WorkspaceBilling;
    public bool IsWhatsAppWorkspace => ActiveWorkspaceIndex == WorkspaceWhatsApp;

    public string ActiveWorkspaceTitle => ActiveWorkspaceIndex switch
    {
        WorkspaceQueue => "Live Queue & Chair Schedule",
        WorkspaceAppointments => "Appointments & Clinic Calendar",
        WorkspacePatients => "Malaysian Patients Registry",
        WorkspaceBilling => "Reception Billing & Invoices",
        _ => "WhatsApp AI Receptionist"
    };

    public string ActiveWorkspaceSubtitle => ActiveWorkspaceIndex switch
    {
        WorkspaceQueue => "Real-time patient arrival, chair allocation & AI booking approvals",
        WorkspaceAppointments => "Multi-day dentist schedule, treatments & table directory",
        WorkspacePatients => "MyKad IC verification, clinical safety alerts & SOAP notes",
        WorkspaceBilling => "Split-payment checkout, corporate panels & audited soft-voids",
        _ => "Automated triage monitoring, simulator & emergency takeover"
    };

    public async Task InitializeAsync()
    {
        _myKadService.StartMonitoring();
        MyKadReaderStatus = _myKadService.StatusMessage;

        if (_ensureLocalApiAsync != null)
        {
            StatusMessage = "Connecting to local Occlusa API...";
            await _ensureLocalApiAsync();
        }

        await RefreshSnapshotAsync();
        await _apiClient.StartRealtimeAsync(SelectedBranchId);
    }

    partial void OnSelectedBranchIdChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _ = SwitchBranchInternalAsync(value);
        }
    }

    partial void OnPatientSearchTextChanged(string value)
    {
        RebuildPatientRows();
    }

    partial void OnSelectedPatientChanged(PatientDto? value)
    {
        UpdateMedicalBanner(value);
    }

    partial void OnSelectedQueueRowChanged(QueueRowItem? value)
    {
        if (value != null)
        {
            SelectPatientById(value.Source.PatientId, value.Source.PatientName, value.Source.PatientIc);
        }
    }

    partial void OnSelectedPatientRowChanged(PatientRowItem? value)
    {
        if (value != null)
        {
            SelectedPatient = value.Source;
        }
    }

    partial void OnSelectedInvoiceRowChanged(InvoiceRowItem? value)
    {
        if (value != null)
        {
            SelectPatientById(value.Source.PatientId, value.Source.PatientName, string.Empty);
        }
    }

    [RelayCommand]
    private void SelectWorkspace(string indexStr)
    {
        if (int.TryParse(indexStr, out int index))
        {
            ActiveWorkspaceIndex = Math.Clamp(index, WorkspaceQueue, WorkspaceWhatsApp);
        }
    }

    [RelayCommand]
    private void ClearPatientSearch()
    {
        PatientSearchText = string.Empty;
    }

    [RelayCommand]
    public async Task RefreshSnapshotAsync()
    {
        try
        {
            var snapshot = await _apiClient.GetDashboardSnapshotAsync(SelectedBranchId);
            if (snapshot != null)
            {
                ApplySnapshot(snapshot);
                StatusMessage = $"Connected ({snapshot.DatabaseMode}) · Synced {DateTime.Now:HH:mm:ss}";
            }
        }
        catch (HttpRequestException) when (_ensureLocalApiAsync != null)
        {
            StatusMessage = "Local Occlusa API offline — starting background service...";
            if (await _ensureLocalApiAsync())
            {
                try
                {
                    var snapshot = await _apiClient.GetDashboardSnapshotAsync(SelectedBranchId);
                    if (snapshot != null)
                    {
                        ApplySnapshot(snapshot);
                        StatusMessage = $"Connected ({snapshot.DatabaseMode}) · Synced {DateTime.Now:HH:mm:ss}";
                        _ = _apiClient.StartRealtimeAsync(SelectedBranchId);
                        return;
                    }
                }
                catch
                {
                    // Fall through to standard offline message below.
                }
            }

            DatabaseModeLabel = "API Offline";
            StatusMessage = "API unreachable (http://localhost:5188) — 5s fallback retry active";
        }
        catch (Exception ex)
        {
            DatabaseModeLabel = "API Offline";
            StatusMessage = $"API unreachable ({ex.Message}) — 5s fallback retry active";
        }
    }

    [RelayCommand]
    private async Task ReadPhysicalMyKadAsync()
    {
        StatusMessage = "Reading physical PC/SC smartcard reader...";
        var result = await _myKadService.ReadPhysicalCardAsync();
        await HandleMyKadReadResultAsync(result);
    }

    [RelayCommand]
    private async Task SimulateMyKadAsync()
    {
        var result = _myKadService.SimulateInsert(SelectedSimulatedProfileIndex, raiseDetectedEvent: false);
        await HandleMyKadReadResultAsync(result);
    }

    [RelayCommand]
    private async Task AdvanceQueueStatusAsync(string targetStatusName)
    {
        if (SelectedQueueRow == null || !Enum.TryParse<QueueStatus>(targetStatusName, out var newStatus))
        {
            return;
        }

        try
        {
            var updated = await _apiClient.UpdateQueueStatusAsync(
                SelectedQueueRow.Source.Id,
                new UpdateQueueStatusRequest
                {
                    NewStatus = newStatus,
                    RoomName = SelectedQueueRow.Source.RoomName,
                    UpdatedBy = "Desktop Reception"
                });

            UpsertQueueItem(updated);
            StatusMessage = $"Updated {updated.PatientName} to {updated.Status}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Status update failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApproveSelectedAiBookingAsync()
    {
        if (SelectedQueueRow == null || SelectedQueueRow.Source.Status != QueueStatus.AiBookedPending)
        {
            return;
        }

        try
        {
            var approved = await _apiClient.ApproveAiBookingAsync(SelectedQueueRow.Source.Id);
            UpsertQueueItem(approved);
            StatusMessage = $"Approved AI booking for {approved.PatientName}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"AI booking approval failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CheckInSelectedPatientAsync()
    {
        if (SelectedPatient == null)
        {
            StatusMessage = "Select or scan a patient first before creating a queue check-in.";
            return;
        }

        try
        {
            var created = await _apiClient.CreateAppointmentAsync(new CreateAppointmentRequest
            {
                BranchId = SelectedBranchId,
                PatientId = SelectedPatient.Id,
                DoctorName = SelectedBranchId == "PJ-SS2" ? "Dr. Marcus Tan" : "Dr. Aina Rahman",
                RoomName = SelectedBranchId == "PJ-SS2" ? "Room 2" : "Room 1",
                ScheduledAt = DateTimeOffset.Now,
                ProcedureSummary = "Walk-In Consultation",
                Status = QueueStatus.Arrived
            });

            UpsertQueueItem(created);
            ActiveWorkspaceIndex = WorkspaceQueue;
            StatusMessage = $"Checked in {SelectedPatient.FullName} to Live Queue ({SelectedBranchId}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Check-in failed: {ex.Message}";
        }
    }

    public async Task HandleMyKadReadResultAsync(MyKadReadResultDto result)
    {
        if (!result.Success)
        {
            StatusMessage = result.Message;
            return;
        }

        try
        {
            var existing = _allPatients.FirstOrDefault(p =>
                string.Equals(p.IcNumber, result.IcNumber, StringComparison.OrdinalIgnoreCase));

            var saved = await _apiClient.UpsertPatientFromMyKadAsync(new UpsertPatientRequest
            {
                Id = existing?.Id,
                FullName = result.FullName,
                IcNumber = result.IcNumber,
                DateOfBirth = result.DateOfBirth ?? new DateOnly(1990, 1, 1),
                Gender = result.Gender,
                Phone = existing?.Phone ?? "+6012-0000000",
                Address = result.Address,
                PrimaryBranchId = existing?.PrimaryBranchId ?? SelectedBranchId,
                MedicalAlerts = existing?.MedicalAlerts ?? new MedicalAlertDto(),
                IsMyKadVerified = true,
                ConsentSignedAt = existing?.ConsentSignedAt
            });

            MergePatient(saved);
            SelectedPatient = saved;
            SelectedPatientRow = PatientRows.FirstOrDefault(r => r.Source.Id == saved.Id);
            ActiveWorkspaceIndex = WorkspacePatients;
            StatusMessage = $"MyKad Verified ({(result.IsSimulated ? "Simulated" : result.ReaderName)}): {saved.FullName} [{saved.IcNumber}]";
        }
        catch (Exception ex)
        {
            // Graceful local preview even if API is currently offline
            var fallbackPatient = new PatientDto
            {
                FullName = result.FullName,
                IcNumber = result.IcNumber,
                DateOfBirth = result.DateOfBirth ?? new DateOnly(1990, 1, 1),
                Gender = result.Gender,
                Address = result.Address,
                PrimaryBranchId = SelectedBranchId,
                IsMyKadVerified = true
            };
            MergePatient(fallbackPatient);
            SelectedPatient = fallbackPatient;
            StatusMessage = $"MyKad read locally ({result.IcNumber}), API sync pending: {ex.Message}";
        }
    }

    public void MergePatient(PatientDto patient)
    {
        int idx = _allPatients.FindIndex(p => p.Id == patient.Id);
        if (idx >= 0)
        {
            _allPatients[idx] = patient;
        }
        else
        {
            _allPatients.Insert(0, patient);
        }

        RebuildPatientRows();

        if (SelectedPatient?.Id == patient.Id)
        {
            SelectedPatient = patient;
            UpdateMedicalBanner(patient);
        }
    }

    public void MergeAppointment(AppointmentQueueItemDto item) =>
        UpsertQueueItem(item);

    public PatientDto? FindPatient(Guid patientId) =>
        _allPatients.FirstOrDefault(p => p.Id == patientId);

    private async Task SwitchBranchInternalAsync(string branchId)
    {
        await _apiClient.SwitchBranchAsync(branchId);
        await RefreshSnapshotAsync();
    }

    private void ApplySnapshot(ClinicDashboardSnapshotDto snapshot)
    {
        DatabaseModeLabel = snapshot.DatabaseMode;
        _allPatients = snapshot.Patients.ToList();
        _dentists = snapshot.Dentists.ToList();
        _treatments = snapshot.Treatments.ToList();
        _allQueue = snapshot.Queue.ToList();
        _allInvoices = snapshot.Invoices.ToList();
        _allThreads = snapshot.WhatsAppThreads.ToList();
        _soapMacros = snapshot.SoapMacros.ToList();
        _latestReconciliation = snapshot.DailySummary;

        RebuildQueueRows();
        RebuildAppointmentsWorkspace();
        RebuildPatientRows();
        RebuildInvoiceRows();
        RebuildWhatsAppRows();
        UpdateTopBadges();

        if (SelectedPatient != null)
        {
            var refreshed = _allPatients.FirstOrDefault(p => p.Id == SelectedPatient.Id);
            if (refreshed != null)
            {
                SelectedPatient = refreshed;
            }
        }
    }

    private void UpsertQueueItem(AppointmentQueueItemDto item)
    {
        if (!string.Equals(item.BranchId, SelectedBranchId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        int idx = _allQueue.FindIndex(q => q.Id == item.Id);
        if (idx >= 0) _allQueue[idx] = item;
        else _allQueue.Add(item);

        RebuildQueueRows();
        RebuildAppointmentsWorkspace();
        UpdateTopBadges();
    }

    private void UpsertInvoiceItem(InvoiceDto invoice)
    {
        if (!string.Equals(invoice.BranchId, SelectedBranchId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        int idx = _allInvoices.FindIndex(i => i.Id == invoice.Id);
        if (idx >= 0) _allInvoices[idx] = invoice;
        else _allInvoices.Insert(0, invoice);

        RebuildInvoiceRows();
    }

    private void UpsertWhatsAppThread(WhatsAppThreadDto thread, bool isEmergency)
    {
        int idx = _allThreads.FindIndex(t => string.Equals(t.ThreadId, thread.ThreadId, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) _allThreads[idx] = thread;
        else _allThreads.Insert(0, thread);

        RebuildWhatsAppRows();
        UpdateTopBadges();

        if (isEmergency || thread.IsEmergencyEscalated)
        {
            StatusMessage = $"EMERGENCY ESCALATION: {thread.PatientName} ({thread.PatientPhone}) — {thread.EmergencyReason}";
        }
    }

    private void RebuildQueueRows()
    {
        Guid? selectedId = SelectedQueueRow?.Source.Id;
        var localToday = DateOnly.FromDateTime(DateTime.Now);
        var localOffset = DateTimeOffset.Now.Offset;

        QueueRows.Clear();
        foreach (var item in _allQueue
            .Where(a => AppointmentCalendarHelper.IsTodayForLiveQueue(a, localToday, localOffset))
            .OrderBy(q => q.ScheduledAt))
        {
            QueueRows.Add(new QueueRowItem(item));
        }

        if (selectedId.HasValue)
        {
            SelectedQueueRow = QueueRows.FirstOrDefault(r => r.Source.Id == selectedId.Value);
        }

        QueueCount = QueueRows.Count;
    }

    private void RebuildAppointmentsWorkspace()
    {
        Appointments.UpdateData(_allQueue, _dentists, _treatments);
        AppointmentCount = _allQueue.Count;
    }

    private void RebuildPatientRows()
    {
        Guid? selectedId = SelectedPatientRow?.Source.Id;
        string filter = (PatientSearchText ?? string.Empty).Trim();

        PatientRows.Clear();
        IEnumerable<PatientDto> query = _allPatients;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(p =>
                p.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || p.IcNumber.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || p.Phone.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var patient in query.OrderBy(p => p.FullName))
        {
            PatientRows.Add(new PatientRowItem(patient));
        }

        if (selectedId.HasValue)
        {
            SelectedPatientRow = PatientRows.FirstOrDefault(r => r.Source.Id == selectedId.Value);
        }

        PatientCount = PatientRows.Count;
    }

    private void RebuildInvoiceRows()
    {
        Guid? selectedId = SelectedInvoiceRow?.Source.Id;
        InvoiceRows.Clear();
        foreach (var inv in _allInvoices.OrderByDescending(i => i.CreatedAt))
        {
            InvoiceRows.Add(new InvoiceRowItem(inv));
        }

        if (selectedId.HasValue)
        {
            SelectedInvoiceRow = InvoiceRows.FirstOrDefault(r => r.Source.Id == selectedId.Value);
        }

        InvoiceCount = InvoiceRows.Count;
    }

    private void RebuildWhatsAppRows()
    {
        string? selectedId = SelectedWhatsAppRow?.Source.ThreadId;
        WhatsAppRows.Clear();
        foreach (var thread in _allThreads.OrderByDescending(t => t.IsEmergencyEscalated).ThenByDescending(t => t.LastUpdatedAt))
        {
            WhatsAppRows.Add(new WhatsAppThreadRowItem(thread));
        }

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            SelectedWhatsAppRow = WhatsAppRows.FirstOrDefault(r => r.Source.ThreadId == selectedId);
        }

        WhatsAppCount = WhatsAppRows.Count;
    }

    private void UpdateTopBadges()
    {
        EmergencyAlertCount = _allThreads.Count(t => t.IsEmergencyEscalated);
        AiBookedPendingCount = _allQueue.Count(q => q.Status == QueueStatus.AiBookedPending);
        HasEmergencyAlerts = EmergencyAlertCount > 0;
        HasAiBookedPending = AiBookedPendingCount > 0;
    }

    private void SelectPatientById(Guid patientId, string fallbackName, string fallbackIc)
    {
        var match = _allPatients.FirstOrDefault(p => p.Id == patientId);
        if (match != null)
        {
            SelectedPatient = match;
            return;
        }

        if (!string.IsNullOrWhiteSpace(fallbackName))
        {
            SelectedPatient = new PatientDto
            {
                Id = patientId,
                FullName = fallbackName,
                IcNumber = fallbackIc,
                PrimaryBranchId = SelectedBranchId
            };
        }
    }

    private void UpdateMedicalBanner(PatientDto? patient)
    {
        if (patient == null)
        {
            BannerPatientName = string.Empty;
            BannerIcNumber = string.Empty;
            BannerIsMyKadVerified = false;
            BannerAllergies = string.Empty;
            BannerOnAnticoagulants = false;
            BannerHasDiabetes = false;
            BannerIsPregnant = false;
            BannerAlertNotes = string.Empty;
            return;
        }

        BannerPatientName = patient.FullName;
        BannerIcNumber = patient.IcNumber;
        BannerIsMyKadVerified = patient.IsMyKadVerified;
        BannerAllergies = patient.MedicalAlerts.Allergies;
        BannerOnAnticoagulants = patient.MedicalAlerts.OnAnticoagulants;
        BannerHasDiabetes = patient.MedicalAlerts.HasDiabetes;
        BannerIsPregnant = patient.MedicalAlerts.IsPregnant;
        BannerAlertNotes = patient.MedicalAlerts.Notes;
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}
