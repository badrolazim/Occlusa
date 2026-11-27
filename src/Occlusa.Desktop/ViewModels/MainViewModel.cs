using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Occlusa.Desktop.Services;
using Occlusa.Desktop.Themes;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.ViewModels;

/// <summary>
/// Which hidden bottom drawer is currently expanded beneath the 3-Column Live Queue Kanban Board.
/// </summary>
public enum LiveQueueDrawerMode
{
    None,
    Scheduled,
    Completed
}

/// <summary>
/// Primary workspace ViewModel coordinating the 6 single-screen tabs, branch switching,
/// live SignalR + 5s polling fallback, PC/SC MyKad registration, and 3-Column Live Queue Kanban Board.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    public const int WorkspaceQueue = 0;
    public const int WorkspaceAppointments = 1;
    public const int WorkspacePatients = 2;
    public const int WorkspaceBilling = 3;
    public const int WorkspaceWhatsApp = 4;
    public const int WorkspaceSettings = 5;

    private readonly OcclusaApiClient _apiClient;
    private readonly MyKadPcscReaderService _myKadService;
    private readonly Func<Task<bool>>? _ensureLocalApiAsync;
    private readonly Func<string?>? _getLocalApiUrl;
    private readonly Func<string>? _getLocalApiStatus;

    private List<PatientDto> _allPatients = new();
    private List<DentistDto> _dentists = new();
    private List<TreatmentDto> _treatments = new();
    private List<AppointmentQueueItemDto> _allQueue = new();
    private List<InvoiceDto> _allInvoices = new();
    private List<WhatsAppThreadDto> _allThreads = new();
    private List<SoapMacroDto> _soapMacros = new();
    private DailySalesReconciliationDto? _latestReconciliation;
    private DispatcherTimer? _queueElapsedTimer;
    private Dictionary<string, UserWorkspacePreferencesDto> _userPreferencesByKey = new(StringComparer.OrdinalIgnoreCase);
    private UserWorkspacePreferencesDto _activeUserPreferences = AppointmentCalendarHelper.BuildDefaultUserPreferences("Siti (Reception)");
    private bool _suppressQueueFilterRebuild;

    [ObservableProperty]
    private string _selectedBranchId = "HQ-KL";

    [ObservableProperty]
    private string _selectedStaffName = "Siti (Reception)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQueueWorkspace))]
    [NotifyPropertyChangedFor(nameof(IsAppointmentsWorkspace))]
    [NotifyPropertyChangedFor(nameof(IsPatientsWorkspace))]
    [NotifyPropertyChangedFor(nameof(IsBillingWorkspace))]
    [NotifyPropertyChangedFor(nameof(IsWhatsAppWorkspace))]
    [NotifyPropertyChangedFor(nameof(IsSettingsWorkspace))]
    [NotifyPropertyChangedFor(nameof(IsSimpleQueueToolbarVisible))]
    [NotifyPropertyChangedFor(nameof(IsDetailedQueueToolbarVisible))]
    [NotifyPropertyChangedFor(nameof(IsQueueSimpleGridVisible))]
    [NotifyPropertyChangedFor(nameof(IsQueueDetailedGridVisible))]
    [NotifyPropertyChangedFor(nameof(ShowCompactMyKadButton))]
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
    [NotifyPropertyChangedFor(nameof(IsMyQueueScope))]
    [NotifyPropertyChangedFor(nameof(IsAllClinicScope))]
    [NotifyPropertyChangedFor(nameof(QueueGridTitle))]
    [NotifyPropertyChangedFor(nameof(MyQueueToggleLabel))]
    [NotifyPropertyChangedFor(nameof(MyQueueToggleToolTip))]
    [NotifyPropertyChangedFor(nameof(CanUseDoctorDropdownFilter))]
    [NotifyPropertyChangedFor(nameof(ShowDoctorFilterDropdown))]
    [NotifyPropertyChangedFor(nameof(ActiveWorkspaceSubtitle))]
    private LiveQueueScopeMode _activeQueueScope = LiveQueueScopeMode.AllClinic;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScheduledDrawerOpen))]
    [NotifyPropertyChangedFor(nameof(IsCompletedDrawerOpen))]
    [NotifyPropertyChangedFor(nameof(IsAnyQueueDrawerOpen))]
    [NotifyPropertyChangedFor(nameof(ScheduledDrawerChevron))]
    [NotifyPropertyChangedFor(nameof(CompletedDrawerChevron))]
    private LiveQueueDrawerMode _activeQueueDrawer = LiveQueueDrawerMode.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScheduledAllFilterChecked))]
    [NotifyPropertyChangedFor(nameof(IsScheduledUnremindedFilterChecked))]
    private bool _showUnremindedOnlyInScheduledDrawer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduledDrawerToggleLabel))]
    private int _scheduledQueueCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompletedDrawerToggleLabel))]
    private int _completedQueueCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCancelledQueueItems))]
    [NotifyPropertyChangedFor(nameof(CancelledBadgeLabel))]
    private int _cancelledQueueCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnremindedFilterLabel))]
    [NotifyPropertyChangedFor(nameof(HasUnremindedScheduled))]
    private int _unremindedScheduledCount;

    [ObservableProperty]
    private int _activeBoardQueueCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActiveOnlyChecked))]
    [NotifyPropertyChangedFor(nameof(IsShowDoneChecked))]
    private bool _showFinishedQueueRows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDoneToggleLabel))]
    private int _finishedQueueCount;

    [ObservableProperty]
    private FilterOptionVm? _selectedQueueDoctorFilter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusChipAll))]
    [NotifyPropertyChangedFor(nameof(IsStatusChipWaiting))]
    [NotifyPropertyChangedFor(nameof(IsStatusChipInChair))]
    [NotifyPropertyChangedFor(nameof(IsStatusChipCheckout))]
    [NotifyPropertyChangedFor(nameof(IsStatusChipUnreminded))]
    private string _selectedQueueStatusChip = "All";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedQueueRow))]
    [NotifyPropertyChangedFor(nameof(CanApproveSelectedAi))]
    [NotifyPropertyChangedFor(nameof(CanRemindSelected))]
    [NotifyPropertyChangedFor(nameof(HasSmartNextAction))]
    [NotifyPropertyChangedFor(nameof(SmartNextActionLabel))]
    [NotifyPropertyChangedFor(nameof(CanCheckInSelected))]
    [NotifyPropertyChangedFor(nameof(CanCallToChairSelected))]
    [NotifyPropertyChangedFor(nameof(CanSendToCheckoutSelected))]
    [NotifyPropertyChangedFor(nameof(CanCompleteSelected))]
    [NotifyPropertyChangedFor(nameof(CanCancelSelectedQueue))]
    private QueueRowItem? _selectedQueueRow;

    [ObservableProperty]
    private string _queuePulseSummary = "Double-click any row to open";

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
        Func<Task<bool>>? ensureLocalApiAsync = null,
        Func<string?>? getLocalApiUrl = null,
        Func<string>? getLocalApiStatus = null)
    {
        _apiClient = apiClient;
        _myKadService = myKadService;
        _ensureLocalApiAsync = ensureLocalApiAsync;
        _getLocalApiUrl = getLocalApiUrl;
        _getLocalApiStatus = getLocalApiStatus;

        AvailableBranches = new ObservableCollection<string> { "HQ-KL", "PJ-SS2" };
        StaffMembers = new ObservableCollection<string>
        {
            "Siti (Reception)",
            "Aisyah (Reception)",
            "Nurse Farah (Assistant)",
            "Nurse Mei Yan (Assistant)",
            "Dr. Aina Rahman",
            "Dr. Marcus Tan",
            "Dr. Priya Nair"
        };
        _apiClient.SetActiveUser(SelectedStaffName);
        SimulatedProfileNames = new ObservableCollection<string>(
            _myKadService.SimulatedProfiles.Select(p => $"{p.FullName} ({p.IcNumber})"));

        QueueRows = new ObservableCollection<QueueRowItem>();
        ScheduledQueueRows = new ObservableCollection<QueueRowItem>();
        CompletedQueueRows = new ObservableCollection<QueueRowItem>();
        WaitingColumn = new LiveQueueKanbanColumnVm(
            QueueStatus.Waiting,
            "Waiting",
            "No patients currently waiting.",
            CalmThemePalette.StatusWarningBrush,
            CalmThemePalette.StatusWarningBgBrush,
            CalmThemePalette.StatusWarningBrush);
        InChairColumn = new LiveQueueKanbanColumnVm(
            QueueStatus.InChair,
            "In Chair",
            "No patients currently in chair.",
            CalmThemePalette.BrandPrimaryBrush,
            CalmThemePalette.BrandTintBrush,
            CalmThemePalette.BrandHoverBrush);
        ReadyForPaymentColumn = new LiveQueueKanbanColumnVm(
            QueueStatus.AwaitingCheckout,
            "Ready for Payment",
            "No patients ready for payment.",
            CalmThemePalette.StatusInfoBrush,
            CalmThemePalette.StatusInfoBgBrush,
            CalmThemePalette.StatusInfoBrush);

        PatientRows = new ObservableCollection<PatientRowItem>();
        InvoiceRows = new ObservableCollection<InvoiceRowItem>();
        WhatsAppRows = new ObservableCollection<WhatsAppThreadRowItem>();
        QueueDoctorFilterOptions = new ObservableCollection<FilterOptionVm>
        {
            new(null, "All Dentists")
        };
        _selectedQueueDoctorFilter = QueueDoctorFilterOptions[0];

        _userPreferencesByKey = LocalUserPreferencesStore.LoadAll();
        ApplyPreferencesForActiveStaff(rebuildRows: false);

        Appointments = new AppointmentsViewModel();
        BillingSettings = new BillingSettingsViewModel(_apiClient);
        Appointments.AppointmentSelectedForBanner += (_, appt) =>
            SelectPatientById(appt.PatientId, appt.PatientName, appt.PatientIc);
        Appointments.AppointmentRescheduleRequested += (_, args) =>
            _ = RescheduleAppointmentAsync(args);

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

    public event EventHandler<PatientDto>? WalkInIntakeRequested;

    public ObservableCollection<string> AvailableBranches { get; }
    public ObservableCollection<string> StaffMembers { get; }
    public ObservableCollection<string> SimulatedProfileNames { get; }
    public ObservableCollection<QueueRowItem> QueueRows { get; }
    public ObservableCollection<QueueRowItem> ScheduledQueueRows { get; }
    public ObservableCollection<QueueRowItem> CompletedQueueRows { get; }
    public LiveQueueKanbanColumnVm WaitingColumn { get; }
    public LiveQueueKanbanColumnVm InChairColumn { get; }
    public LiveQueueKanbanColumnVm ReadyForPaymentColumn { get; }
    public ObservableCollection<PatientRowItem> PatientRows { get; }
    public ObservableCollection<InvoiceRowItem> InvoiceRows { get; }
    public ObservableCollection<WhatsAppThreadRowItem> WhatsAppRows { get; }
    public ObservableCollection<FilterOptionVm> QueueDoctorFilterOptions { get; }

    public AppointmentsViewModel Appointments { get; }
    public BillingSettingsViewModel BillingSettings { get; }
    public UserWorkspacePreferencesDto ActiveUserPreferences => _activeUserPreferences;
    public IReadOnlyList<PatientDto> AllPatients => _allPatients;
    public IReadOnlyList<DentistDto> Dentists => _dentists;
    public IReadOnlyList<TreatmentDto> Treatments => _treatments;
    public IReadOnlyList<SoapMacroDto> SoapMacros => _soapMacros;
    public IReadOnlyList<AppointmentQueueItemDto> AllQueueItems => _allQueue;
    public IReadOnlyList<InvoiceDto> AllInvoices => _allInvoices;
    public DailySalesReconciliationDto? LatestReconciliation => _latestReconciliation;

    public bool IsCurrentStaffDentist => AppointmentCalendarHelper.IsDentistStaff(SelectedStaffName, _dentists);
    public bool IsSimpleQueueView => _activeUserPreferences.QueueViewMode == LiveQueueViewMode.Simple;
    public bool IsDetailedQueueView => _activeUserPreferences.QueueViewMode == LiveQueueViewMode.Detailed;
    public bool IsSimpleQueueToolbarVisible => IsQueueWorkspace && IsSimpleQueueView;
    public bool IsDetailedQueueToolbarVisible => IsQueueWorkspace && IsDetailedQueueView;
    public bool IsQueueSimpleGridVisible => IsQueueWorkspace && IsSimpleQueueView;
    public bool IsQueueDetailedGridVisible => IsQueueWorkspace && IsDetailedQueueView;
    public string QueueViewBadgeText => IsSimpleQueueView ? "View: Simple ⚙" : "View: Detailed ⚙";
    public string QueueGridTitle => IsMyQueueScope ? $"My Patient Queue ({SelectedStaffName})" : "Today's Live Queue";
    public bool ShowDentistScopeToggle => IsCurrentStaffDentist;
    public bool IsMyQueueScope => ActiveQueueScope == LiveQueueScopeMode.MyQueue && IsCurrentStaffDentist;
    public bool IsAllClinicScope => !IsMyQueueScope;
    public string MyQueueToggleLabel
    {
        get
        {
            if (!IsCurrentStaffDentist)
            {
                return "My Queue (Dentist)";
            }

            var shortDoc = AppointmentCalendarHelper.FormatCompactDentistName(SelectedStaffName);
            return IsMyQueueScope ? $"✓ My Queue · {shortDoc}" : $"My Queue · {shortDoc}";
        }
    }

    public string MyQueueToggleToolTip => IsCurrentStaffDentist
        ? (IsMyQueueScope
            ? $"Showing only {SelectedStaffName}'s queue across all 3 columns and drawers. Click to view All Clinic."
            : $"Click to filter all 3 columns and drawers strictly to {SelectedStaffName}'s queue.")
        : "Select a Dentist under Active Staff in the left sidebar to filter to your personal queue.";

    public bool CanUseDoctorDropdownFilter => !IsMyQueueScope;
    public bool ShowDoctorFilterDropdown => CanUseDoctorDropdownFilter;

    public bool IsScheduledDrawerOpen => ActiveQueueDrawer == LiveQueueDrawerMode.Scheduled;
    public bool IsCompletedDrawerOpen => ActiveQueueDrawer == LiveQueueDrawerMode.Completed;
    public bool IsAnyQueueDrawerOpen => ActiveQueueDrawer != LiveQueueDrawerMode.None;
    public string ScheduledDrawerChevron => IsScheduledDrawerOpen ? "▼" : "▲";
    public string CompletedDrawerChevron => IsCompletedDrawerOpen ? "▼" : "▲";
    public string ScheduledDrawerToggleLabel => $"Scheduled ({ScheduledQueueCount})";
    public string CompletedDrawerToggleLabel => $"Completed ({CompletedQueueCount})";
    public bool HasCancelledQueueItems => CancelledQueueCount > 0;
    public string CancelledBadgeLabel => $"{CancelledQueueCount} Cancelled";
    public bool IsScheduledAllFilterChecked => !ShowUnremindedOnlyInScheduledDrawer;
    public bool IsScheduledUnremindedFilterChecked => ShowUnremindedOnlyInScheduledDrawer;
    public bool HasUnremindedScheduled => UnremindedScheduledCount > 0;
    public string UnremindedFilterLabel => UnremindedScheduledCount > 0
        ? $"Unreminded ({UnremindedScheduledCount})"
        : "Unreminded";

    public bool IsActiveOnlyChecked => !ShowFinishedQueueRows;
    public bool IsShowDoneChecked => ShowFinishedQueueRows;
    public string ShowDoneToggleLabel => FinishedQueueCount > 0 ? $"Completed ({FinishedQueueCount})" : "Completed";
    public bool IsStatusChipAll => string.Equals(SelectedQueueStatusChip, "All", StringComparison.OrdinalIgnoreCase);
    public bool IsStatusChipWaiting => string.Equals(SelectedQueueStatusChip, "Waiting", StringComparison.OrdinalIgnoreCase);
    public bool IsStatusChipInChair => string.Equals(SelectedQueueStatusChip, "InChair", StringComparison.OrdinalIgnoreCase);
    public bool IsStatusChipCheckout => string.Equals(SelectedQueueStatusChip, "Checkout", StringComparison.OrdinalIgnoreCase);
    public bool IsStatusChipUnreminded => string.Equals(SelectedQueueStatusChip, "Unreminded", StringComparison.OrdinalIgnoreCase);
    public bool ShowFullMyKadBar => _activeUserPreferences.ShowMyKadQuickBar;
    public bool ShowCompactMyKadButton => !ShowFullMyKadBar && !IsCurrentStaffDentist && (IsQueueWorkspace || IsPatientsWorkspace);

    public bool HasSelectedQueueRow => SelectedQueueRow != null;
    public bool CanApproveSelectedAi => SelectedQueueRow?.Source.Status == QueueStatus.AiBookedPending;
    public bool CanRemindSelected => SelectedQueueRow != null && AppointmentCalendarHelper.IsUpcomingRemindableAppointment(SelectedQueueRow.Source);
    public string SmartNextActionLabel => SelectedQueueRow is null
        ? string.Empty
        : AppointmentCalendarHelper.NormalizeQueueStatus(SelectedQueueRow.Source.Status) switch
        {
            QueueStatus.AiBookedPending => "Approve AI Booking",
            QueueStatus.Scheduled => "Waiting",
            QueueStatus.Waiting => "In Chair",
            QueueStatus.InChair => "Ready for Payment",
            QueueStatus.AwaitingCheckout => "Completed",
            _ => string.Empty
        };
    public bool HasSmartNextAction => !string.IsNullOrEmpty(SmartNextActionLabel);
    public bool CanCheckInSelected => SelectedQueueRow?.Source.Status is QueueStatus.Scheduled or QueueStatus.AiBookedPending;
    public bool CanCallToChairSelected => SelectedQueueRow?.Source.Status is QueueStatus.Waiting or QueueStatus.Arrived or QueueStatus.Scheduled;
    public bool CanSendToCheckoutSelected => SelectedQueueRow?.Source.Status is QueueStatus.InChair or QueueStatus.AwaitingCheckout;
    public bool CanCompleteSelected => SelectedQueueRow?.Source.Status is QueueStatus.InChair or QueueStatus.AwaitingCheckout;
    public bool CanCancelSelectedQueue => SelectedQueueRow != null && !AppointmentCalendarHelper.IsFinishedQueueStatus(SelectedQueueRow.Source.Status);

    public bool IsQueueWorkspace => ActiveWorkspaceIndex == WorkspaceQueue;
    public bool IsAppointmentsWorkspace => ActiveWorkspaceIndex == WorkspaceAppointments;
    public bool IsPatientsWorkspace => ActiveWorkspaceIndex == WorkspacePatients;
    public bool IsBillingWorkspace => ActiveWorkspaceIndex == WorkspaceBilling;
    public bool IsWhatsAppWorkspace => ActiveWorkspaceIndex == WorkspaceWhatsApp;
    public bool IsSettingsWorkspace => ActiveWorkspaceIndex == WorkspaceSettings;

    public string ActiveWorkspaceTitle => ActiveWorkspaceIndex switch
    {
        WorkspaceQueue => "Live Queue",
        WorkspaceAppointments => "Appointments & Clinic Calendar",
        WorkspacePatients => "Malaysian Patients Registry",
        WorkspaceBilling => "Reception Billing, OTC & Credit Wallet",
        WorkspaceSettings => "Master Catalogue & Doctor Settings",
        _ => "WhatsApp AI Receptionist"
    };

    public string ActiveWorkspaceSubtitle => ActiveWorkspaceIndex switch
    {
        WorkspaceQueue => IsMyQueueScope
            ? $"Filtered to {SelectedStaffName} · Waiting → In Chair → Ready for Payment · Click My Queue again for All Clinic"
            : IsSimpleQueueView
                ? "Active clinic flow (Waiting → In Chair → Ready for Payment) · Scheduled & Completed tucked below"
                : "Detailed Kanban board — live wait & chair timers, arrival telemetry, reception notes & stage actions",
        WorkspaceAppointments => "Multi-day dentist schedule, treatments & table directory",
        WorkspacePatients => "MyKad IC verification, clinical safety alerts & pure clinical visit notes",
        WorkspaceBilling => "Draft invoices, walk-in OTC sales, shared patient credit & split-payment checkout",
        WorkspaceSettings => "Single source of truth for Appointments, Odontogram Presets, Prices, OTC 0% Comm & Note Keywords",
        _ => "Automated triage monitoring, simulator & emergency takeover"
    };

    public async Task InitializeAsync()
    {
        _myKadService.StartMonitoring();
        MyKadReaderStatus = _myKadService.StatusMessage;

        if (_ensureLocalApiAsync != null)
        {
            StatusMessage = "Connecting to local Occlusa API...";
            bool ready = await _ensureLocalApiAsync();
            if (ready)
            {
                var activeUrl = _getLocalApiUrl?.Invoke();
                if (!string.IsNullOrWhiteSpace(activeUrl))
                {
                    _apiClient.UpdateBaseUrl(activeUrl);
                }
            }
            else
            {
                var detail = _getLocalApiStatus?.Invoke();
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    StatusMessage = detail;
                }
            }
        }

        await RefreshSnapshotAsync();
        await _apiClient.StartRealtimeAsync(SelectedBranchId);

        if (_queueElapsedTimer == null)
        {
            _queueElapsedTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _queueElapsedTimer.Tick += (_, _) => RefreshQueueElapsedTimers();
            _queueElapsedTimer.Start();
        }
    }

    public void StopTimers()
    {
        _queueElapsedTimer?.Stop();
    }

    partial void OnSelectedBranchIdChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _ = SwitchBranchInternalAsync(value);
        }
    }

    partial void OnSelectedStaffNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _apiClient.SetActiveUser(value);
            ApplyPreferencesForActiveStaff(rebuildRows: true);
            StatusMessage = $"Switched to {value} ({_activeUserPreferences.QueueViewMode} View).";
        }
    }

    partial void OnActiveQueueScopeChanged(LiveQueueScopeMode value)
    {
        if (!_suppressQueueFilterRebuild)
        {
            RebuildQueueRows();
        }
    }

    partial void OnShowUnremindedOnlyInScheduledDrawerChanged(bool value)
    {
        if (!_suppressQueueFilterRebuild)
        {
            RebuildQueueRows();
        }
    }

    partial void OnShowFinishedQueueRowsChanged(bool value)
    {
        if (!_suppressQueueFilterRebuild)
        {
            RebuildQueueRows();
        }
    }

    partial void OnSelectedQueueDoctorFilterChanged(FilterOptionVm? value)
    {
        if (!_suppressQueueFilterRebuild)
        {
            RebuildQueueRows();
        }
    }

    partial void OnSelectedQueueStatusChipChanged(string value)
    {
        if (!_suppressQueueFilterRebuild)
        {
            RebuildQueueRows();
        }
    }

    partial void OnPatientSearchTextChanged(string value)
    {
        RebuildPatientRows();
    }

    partial void OnSelectedPatientChanged(PatientDto? value)
    {
        // Patient selection is retained for cross-tab actions (Check-In, Patient Invoice);
        // clinical MedicalAlertsBanner is pinned inside patient/clinical/appointment modals.
    }

    partial void OnSelectedQueueRowChanged(QueueRowItem? value)
    {
        var selectedId = value?.Source.Id;
        WaitingColumn.SyncSelection(selectedId);
        InChairColumn.SyncSelection(selectedId);
        ReadyForPaymentColumn.SyncSelection(selectedId);

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
            if (value.Source.IsWalkInRetail || value.Source.PatientId == Guid.Empty)
            {
                SelectedPatient = null;
            }
            else
            {
                SelectPatientById(value.Source.PatientId, value.Source.PatientName, string.Empty);
            }
        }
    }

    [RelayCommand]
    private void SelectWorkspace(string indexStr)
    {
        if (int.TryParse(indexStr, out int index))
        {
            ActiveWorkspaceIndex = Math.Clamp(index, WorkspaceQueue, WorkspaceSettings);
        }
    }

    [RelayCommand]
    private void ClearPatientSearch()
    {
        PatientSearchText = string.Empty;
    }

    [RelayCommand]
    private void SetQueueScope(string scopeName)
    {
        if (Enum.TryParse<LiveQueueScopeMode>(scopeName, out var parsed))
        {
            ActiveQueueScope = parsed == LiveQueueScopeMode.MyQueue && !IsCurrentStaffDentist
                ? LiveQueueScopeMode.AllClinic
                : parsed;
        }
    }

    [RelayCommand]
    private void ToggleMyQueueScope()
    {
        if (!IsCurrentStaffDentist)
        {
            return;
        }

        ActiveQueueScope = IsMyQueueScope
            ? LiveQueueScopeMode.AllClinic
            : LiveQueueScopeMode.MyQueue;

        StatusMessage = IsMyQueueScope
            ? $"Live Queue filtered to {SelectedStaffName}."
            : "Live Queue showing All Clinic.";
    }

    [RelayCommand]
    private void ToggleQueueDrawer(string modeName)
    {
        if (!Enum.TryParse<LiveQueueDrawerMode>(modeName, out var parsed) || parsed == LiveQueueDrawerMode.None)
        {
            ActiveQueueDrawer = LiveQueueDrawerMode.None;
            return;
        }

        ActiveQueueDrawer = ActiveQueueDrawer == parsed
            ? LiveQueueDrawerMode.None
            : parsed;
    }

    [RelayCommand]
    private void CollapseQueueDrawer()
    {
        ActiveQueueDrawer = LiveQueueDrawerMode.None;
    }

    [RelayCommand]
    private void SelectQueueWorkspaceAndScheduledDrawer()
    {
        ActiveWorkspaceIndex = WorkspaceQueue;
        ActiveQueueDrawer = LiveQueueDrawerMode.Scheduled;
    }

    [RelayCommand]
    private void SetScheduledUnremindedFilter(string flagStr)
    {
        if (bool.TryParse(flagStr, out bool flag))
        {
            ShowUnremindedOnlyInScheduledDrawer = flag;
        }
    }

    [RelayCommand]
    private void SelectQueueCard(QueueRowItem? row)
    {
        SelectedQueueRow = row;
    }

    [RelayCommand]
    private void SetShowFinishedQueueRows(string flagStr)
    {
        if (bool.TryParse(flagStr, out bool flag))
        {
            ShowFinishedQueueRows = flag;
        }
    }

    [RelayCommand]
    private void ToggleShowFinishedQueueRows()
    {
        ShowFinishedQueueRows = !ShowFinishedQueueRows;
    }

    [RelayCommand]
    private void SetQueueStatusChip(string chip)
    {
        if (!string.IsNullOrWhiteSpace(chip))
        {
            SelectedQueueStatusChip = chip.Trim();
        }
    }

    public Task SimulateMyKadByIndexAsync(int profileIndex)
    {
        SelectedSimulatedProfileIndex = profileIndex;
        return SimulateMyKadAsync();
    }

    public void ApplySavedUserPreferences(UserWorkspacePreferencesDto saved)
    {
        var key = AppointmentCalendarHelper.NormalizeStaffKey(
            string.IsNullOrWhiteSpace(saved.StaffKey) ? saved.StaffDisplayName : saved.StaffKey);
        saved.StaffKey = key;
        _userPreferencesByKey[key] = saved;
        LocalUserPreferencesStore.SaveAll(_userPreferencesByKey.Values);

        if (string.Equals(key, AppointmentCalendarHelper.NormalizeStaffKey(SelectedStaffName), StringComparison.OrdinalIgnoreCase))
        {
            ApplyPreferencesForActiveStaff(rebuildRows: true);
            StatusMessage = $"Updated view settings for {SelectedStaffName} ({saved.QueueViewMode} View).";
        }
    }

    private void ApplyPreferencesForActiveStaff(bool rebuildRows)
    {
        var key = AppointmentCalendarHelper.NormalizeStaffKey(SelectedStaffName);
        if (!_userPreferencesByKey.TryGetValue(key, out var resolved))
        {
            resolved = AppointmentCalendarHelper.BuildDefaultUserPreferences(SelectedStaffName, _dentists);
        }

        _activeUserPreferences = resolved;
        bool isDentist = AppointmentCalendarHelper.IsDentistStaff(SelectedStaffName, _dentists);

        _suppressQueueFilterRebuild = true;
        ActiveQueueScope = resolved.DefaultQueueScope == LiveQueueScopeMode.MyQueue && isDentist
            ? LiveQueueScopeMode.MyQueue
            : LiveQueueScopeMode.AllClinic;
        ShowFinishedQueueRows = !resolved.HideFinishedQueueRows;
        SelectedQueueStatusChip = "All";
        SelectedQueueDoctorFilter = QueueDoctorFilterOptions.FirstOrDefault();
        _suppressQueueFilterRebuild = false;

        NotifyUserPreferenceStates();
        if (rebuildRows)
        {
            RebuildQueueRows();
        }
    }

    private void NotifyUserPreferenceStates()
    {
        OnPropertyChanged(nameof(ActiveUserPreferences));
        OnPropertyChanged(nameof(IsCurrentStaffDentist));
        OnPropertyChanged(nameof(IsSimpleQueueView));
        OnPropertyChanged(nameof(IsDetailedQueueView));
        OnPropertyChanged(nameof(IsSimpleQueueToolbarVisible));
        OnPropertyChanged(nameof(IsDetailedQueueToolbarVisible));
        OnPropertyChanged(nameof(IsQueueSimpleGridVisible));
        OnPropertyChanged(nameof(IsQueueDetailedGridVisible));
        OnPropertyChanged(nameof(QueueViewBadgeText));
        OnPropertyChanged(nameof(QueueGridTitle));
        OnPropertyChanged(nameof(ShowDentistScopeToggle));
        OnPropertyChanged(nameof(IsMyQueueScope));
        OnPropertyChanged(nameof(IsAllClinicScope));
        OnPropertyChanged(nameof(MyQueueToggleLabel));
        OnPropertyChanged(nameof(MyQueueToggleToolTip));
        OnPropertyChanged(nameof(CanUseDoctorDropdownFilter));
        OnPropertyChanged(nameof(ShowDoctorFilterDropdown));
        OnPropertyChanged(nameof(ShowFullMyKadBar));
        OnPropertyChanged(nameof(ShowCompactMyKadButton));
        OnPropertyChanged(nameof(ActiveWorkspaceSubtitle));
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
        catch (HttpRequestException ex) when (_ensureLocalApiAsync != null)
        {
            StatusMessage = "Local Occlusa API offline — starting embedded service...";
            if (await _ensureLocalApiAsync())
            {
                var activeUrl = _getLocalApiUrl?.Invoke();
                if (!string.IsNullOrWhiteSpace(activeUrl))
                {
                    _apiClient.UpdateBaseUrl(activeUrl);
                }

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

            var hostDetail = _getLocalApiStatus?.Invoke();
            var reason = !string.IsNullOrWhiteSpace(hostDetail) ? hostDetail : ex.Message;
            DatabaseModeLabel = "API Offline";
            StatusMessage = $"API unreachable ({_apiClient.BaseUrl}): {reason} — 5s fallback retry active";
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

    public async Task<AppointmentQueueItemDto?> TransitionQueueItemAsync(QueueRowItem row, QueueStatus targetStatus)
    {
        if (row == null || row.IsTransitioning)
        {
            return null;
        }

        var newStatus = AppointmentCalendarHelper.NormalizeQueueStatus(targetStatus);
        row.IsTransitioning = true;
        try
        {
            var updated = await _apiClient.UpdateQueueStatusAsync(
                row.Source.Id,
                new UpdateQueueStatusRequest
                {
                    NewStatus = newStatus,
                    RoomName = row.Source.RoomName,
                    UpdatedBy = SelectedStaffName
                });

            UpsertQueueItem(updated);
            SelectedQueueRow = QueueRows.FirstOrDefault(r => r.Source.Id == updated.Id);
            StatusMessage = $"{updated.PatientName} → {AppointmentCalendarHelper.FormatStatusLabel(updated.Status)} (by {SelectedStaffName}).";
            return updated;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Status update failed: {ex.Message}";
            return null;
        }
        finally
        {
            row.IsTransitioning = false;
        }
    }

    [RelayCommand]
    private async Task AdvanceQueueStatusAsync(string targetStatusName)
    {
        if (SelectedQueueRow == null || !Enum.TryParse<QueueStatus>(targetStatusName, out var parsedStatus))
        {
            return;
        }

        await TransitionQueueItemAsync(SelectedQueueRow, parsedStatus);
    }

    [RelayCommand]
    private async Task CancelSelectedQueueItemAsync()
    {
        if (SelectedQueueRow == null || AppointmentCalendarHelper.IsFinishedQueueStatus(SelectedQueueRow.Source.Status))
        {
            return;
        }

        var target = SelectedQueueRow.Source;
        var confirm = MessageBox.Show(
            $"Cancel {target.PatientName}'s visit ({target.ProcedureSummary})?\nThis will move the row to the bottom as Cancelled.",
            "Confirm Cancel Visit",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var updated = await _apiClient.UpdateQueueStatusAsync(
                target.Id,
                new UpdateQueueStatusRequest
                {
                    NewStatus = QueueStatus.Cancelled,
                    RoomName = target.RoomName,
                    UpdatedBy = SelectedStaffName
                });

            UpsertQueueItem(updated);
            SelectedQueueRow = QueueRows.FirstOrDefault(r => r.Source.Id == updated.Id);
            StatusMessage = $"Cancelled {updated.PatientName} (by {SelectedStaffName}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cancel failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApproveSelectedAiBookingAsync()
    {
        await ApproveAiBookingRowAsync(SelectedQueueRow);
    }

    [RelayCommand]
    public async Task ApproveAiBookingRowAsync(QueueRowItem? row)
    {
        var target = row ?? SelectedQueueRow;
        if (target == null || target.Source.Status != QueueStatus.AiBookedPending || target.IsTransitioning)
        {
            return;
        }

        target.IsTransitioning = true;
        try
        {
            var approved = await _apiClient.ApproveAiBookingAsync(target.Source.Id, SelectedStaffName);
            UpsertQueueItem(approved);
            SelectedQueueRow = QueueRows.FirstOrDefault(r => r.Source.Id == approved.Id);
            StatusMessage = $"Approved AI booking for {approved.PatientName} → Scheduled (by {SelectedStaffName}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"AI booking approval failed: {ex.Message}";
        }
        finally
        {
            target.IsTransitioning = false;
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

        var existingToday = QueueRows.FirstOrDefault(r =>
            r.Source.PatientId == SelectedPatient.Id
            && r.Source.Status is QueueStatus.Scheduled or QueueStatus.AiBookedPending);

        if (existingToday != null)
        {
            try
            {
                var updated = await _apiClient.UpdateQueueStatusAsync(
                    existingToday.Source.Id,
                    new UpdateQueueStatusRequest
                    {
                        NewStatus = QueueStatus.Waiting,
                        RoomName = existingToday.Source.RoomName,
                        UpdatedBy = SelectedStaffName
                    });

                UpsertQueueItem(updated);
                ActiveWorkspaceIndex = WorkspaceQueue;
                SelectedQueueRow = QueueRows.FirstOrDefault(r => r.Source.Id == updated.Id);
                StatusMessage = $"Checked in {SelectedPatient.FullName} to Waiting (by {SelectedStaffName}).";
                return;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Check-in failed: {ex.Message}";
                return;
            }
        }

        WalkInIntakeRequested?.Invoke(this, SelectedPatient);
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

            var activeVisitRow = QueueRows.FirstOrDefault(r =>
                r.Source.PatientId == saved.Id
                && !AppointmentCalendarHelper.IsFinishedQueueStatus(r.Source.Status));

            if (activeVisitRow != null)
            {
                ActiveWorkspaceIndex = WorkspaceQueue;
                SelectedQueueRow = activeVisitRow;

                if (activeVisitRow.Source.Status is QueueStatus.Scheduled or QueueStatus.AiBookedPending)
                {
                    var checkInPrompt = MessageBox.Show(
                        $"MyKad verified for {saved.FullName}.\n\n"
                        + $"Found booking today at {activeVisitRow.Source.ScheduledAt.ToLocalTime():HH:mm} "
                        + $"with {activeVisitRow.Source.DoctorName} ({activeVisitRow.Source.RoomName}).\n\n"
                        + "Check in to Waiting now?",
                        "Smart MyKad Check-In",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (checkInPrompt == MessageBoxResult.Yes)
                    {
                        var updated = await _apiClient.UpdateQueueStatusAsync(
                            activeVisitRow.Source.Id,
                            new UpdateQueueStatusRequest
                            {
                                NewStatus = QueueStatus.Waiting,
                                RoomName = activeVisitRow.Source.RoomName,
                                UpdatedBy = SelectedStaffName
                            });
                        UpsertQueueItem(updated);
                        SelectedQueueRow = QueueRows.FirstOrDefault(r => r.Source.Id == updated.Id);
                        StatusMessage = $"Checked in {saved.FullName} to Waiting via MyKad (by {SelectedStaffName}).";
                        return;
                    }

                    ActiveQueueDrawer = LiveQueueDrawerMode.Scheduled;
                }

                StatusMessage = $"MyKad verified: {saved.FullName} ({activeVisitRow.StatusDisplay})";
                return;
            }

            if (IsQueueWorkspace)
            {
                var walkInPrompt = MessageBox.Show(
                    $"MyKad verified for {saved.FullName} ({saved.IcNumber}).\n\n"
                    + "No appointment found for today. Register as Walk-In now?",
                    "MyKad Walk-In Check-In",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (walkInPrompt == MessageBoxResult.Yes)
                {
                    WalkInIntakeRequested?.Invoke(this, saved);
                    return;
                }
            }
            else
            {
                ActiveWorkspaceIndex = WorkspacePatients;
            }

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
        }
    }

    public void MergeAppointment(AppointmentQueueItemDto item) =>
        UpsertQueueItem(item);

    public void MergeWhatsAppThread(WhatsAppThreadDto thread) =>
        UpsertWhatsAppThread(thread, thread.IsEmergencyEscalated);

    public async Task RescheduleAppointmentAsync(AppointmentRescheduleArgs args)
    {
        try
        {
            var request = AppointmentCalendarHelper.BuildDragRescheduleRequest(
                args.Appointment,
                args.TargetDate,
                args.TargetStartTime,
                DateTimeOffset.Now.Offset,
                SelectedStaffName);

            var updated = await _apiClient.UpdateAppointmentAsync(args.Appointment.Id, request);
            UpsertQueueItem(updated);
            StatusMessage = $"Rescheduled {updated.PatientName} ({updated.DoctorName}) to {updated.ScheduledAt.ToLocalTime():ddd dd MMM, HH:mm}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Reschedule failed: {ex.Message}";
        }
    }

    public PatientDto? FindPatient(Guid patientId) =>
        _allPatients.FirstOrDefault(p => p.Id == patientId);

    public InvoiceDto? FindActiveInvoiceForVisitOrAppointment(Guid? visitId, Guid? appointmentId)
    {
        return _allInvoices
            .Where(i => !i.IsVoided && (
                (visitId.HasValue && i.VisitId == visitId.Value)
                || (appointmentId.HasValue && i.AppointmentId == appointmentId.Value)))
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefault();
    }

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

        if (snapshot.UserPreferences is { Count: > 0 })
        {
            foreach (var pref in snapshot.UserPreferences)
            {
                var key = AppointmentCalendarHelper.NormalizeStaffKey(
                    string.IsNullOrWhiteSpace(pref.StaffKey) ? pref.StaffDisplayName : pref.StaffKey);
                pref.StaffKey = key;
                _userPreferencesByKey[key] = pref;
            }

            LocalUserPreferencesStore.SaveAll(_userPreferencesByKey.Values);
        }

        RebuildQueueDoctorFilterOptions();
        SyncActivePreferencesFromCacheWithoutOverridingTransientToggle();

        RebuildQueueRows();
        RebuildAppointmentsWorkspace();
        RebuildPatientRows();
        RebuildInvoiceRows();
        RebuildWhatsAppRows();
        BillingSettings.ApplySnapshot(snapshot.BillingSettings, _dentists);
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

    private void RebuildQueueDoctorFilterOptions()
    {
        var previousId = SelectedQueueDoctorFilter?.Id;
        _suppressQueueFilterRebuild = true;
        QueueDoctorFilterOptions.Clear();
        QueueDoctorFilterOptions.Add(new FilterOptionVm(null, "All Dentists"));
        foreach (var d in _dentists)
        {
            QueueDoctorFilterOptions.Add(new FilterOptionVm(d.Id, d.FullName));
        }

        SelectedQueueDoctorFilter = QueueDoctorFilterOptions.FirstOrDefault(o => o.Id == previousId)
            ?? QueueDoctorFilterOptions[0];
        _suppressQueueFilterRebuild = false;
    }

    private void SyncActivePreferencesFromCacheWithoutOverridingTransientToggle()
    {
        var key = AppointmentCalendarHelper.NormalizeStaffKey(SelectedStaffName);
        if (_userPreferencesByKey.TryGetValue(key, out var cached))
        {
            bool viewModeChanged = cached.QueueViewMode != _activeUserPreferences.QueueViewMode
                || cached.ShowWaitTimers != _activeUserPreferences.ShowWaitTimers
                || cached.ShowInlineReceptionNotes != _activeUserPreferences.ShowInlineReceptionNotes
                || cached.ShowMyKadQuickBar != _activeUserPreferences.ShowMyKadQuickBar;

            _activeUserPreferences = cached;
            if (viewModeChanged)
            {
                NotifyUserPreferenceStates();
            }
        }
        else
        {
            _activeUserPreferences = AppointmentCalendarHelper.BuildDefaultUserPreferences(SelectedStaffName, _dentists);
            NotifyUserPreferenceStates();
        }
    }

    private void RebuildQueueRows()
    {
        Guid? selectedId = SelectedQueueRow?.Source.Id;
        var localToday = DateOnly.FromDateTime(DateTime.Now);
        var localOffset = DateTimeOffset.Now.Offset;

        var todayItems = _allQueue
            .Where(a => AppointmentCalendarHelper.IsTodayForLiveQueue(a, localToday, localOffset))
            .ToList();

        var scope = IsMyQueueScope ? LiveQueueScopeMode.MyQueue : LiveQueueScopeMode.AllClinic;
        Guid? doctorFilterId = !IsMyQueueScope ? SelectedQueueDoctorFilter?.Id : null;
        string? doctorFilterName = !IsMyQueueScope && SelectedQueueDoctorFilter?.Id != null
            ? SelectedQueueDoctorFilter.Label
            : null;

        // Partition scoped today items into the 3 active Kanban columns + 2 collapsible drawers
        var allScopedBuckets = AppointmentCalendarHelper.PartitionLiveQueueKanban(
            todayItems,
            scope,
            SelectedStaffName,
            _dentists,
            doctorFilterId,
            doctorFilterName,
            scheduledUnremindedOnly: false);

        var scheduledDrawerBuckets = ShowUnremindedOnlyInScheduledDrawer
            ? AppointmentCalendarHelper.PartitionLiveQueueKanban(
                todayItems,
                scope,
                SelectedStaffName,
                _dentists,
                doctorFilterId,
                doctorFilterName,
                scheduledUnremindedOnly: true)
            : allScopedBuckets;

        bool showDoctorInSubline = !IsMyQueueScope;
        bool showWaitTimers = _activeUserPreferences.ShowWaitTimers;
        bool showInlineNotes = _activeUserPreferences.ShowInlineReceptionNotes;
        bool isDetailedCard = IsDetailedQueueView;

        QueueRowItem CreateRow(AppointmentQueueItemDto dto) =>
            new(dto, showDoctorInSubline, showWaitTimers, showInlineNotes, isDetailedCard);

        var waitingRows = allScopedBuckets.Waiting.Select(CreateRow).ToList();
        var inChairRows = allScopedBuckets.InChair.Select(CreateRow).ToList();
        var readyForPaymentRows = allScopedBuckets.ReadyForPayment.Select(CreateRow).ToList();
        var allScheduledRows = allScopedBuckets.Scheduled.Select(CreateRow).ToList();
        var drawerScheduledRows = ShowUnremindedOnlyInScheduledDrawer
            ? scheduledDrawerBuckets.Scheduled.Select(CreateRow).ToList()
            : allScheduledRows;
        var completedRows = allScopedBuckets.Completed.Select(CreateRow).ToList();

        WaitingColumn.SetItems(waitingRows, selectedId);
        InChairColumn.SetItems(inChairRows, selectedId);
        ReadyForPaymentColumn.SetItems(readyForPaymentRows, selectedId);

        ScheduledQueueRows.Clear();
        foreach (var row in drawerScheduledRows)
        {
            ScheduledQueueRows.Add(row);
        }

        CompletedQueueRows.Clear();
        foreach (var row in completedRows)
        {
            CompletedQueueRows.Add(row);
        }

        // Keep unified QueueRows synchronized so MyKad Smart Check-In, Patient Check-In, and selection lookup work across all 5 stages
        QueueRows.Clear();
        foreach (var row in readyForPaymentRows) QueueRows.Add(row);
        foreach (var row in inChairRows) QueueRows.Add(row);
        foreach (var row in waitingRows) QueueRows.Add(row);
        foreach (var row in allScheduledRows) QueueRows.Add(row);
        foreach (var row in completedRows) QueueRows.Add(row);

        ScheduledQueueCount = allScopedBuckets.Scheduled.Count;
        CompletedQueueCount = allScopedBuckets.Completed.Count(a => a.Status == QueueStatus.Completed);
        CancelledQueueCount = allScopedBuckets.Completed.Count(a => a.Status == QueueStatus.Cancelled);
        FinishedQueueCount = allScopedBuckets.Completed.Count;
        UnremindedScheduledCount = allScopedBuckets.Scheduled.Count(a =>
            AppointmentCalendarHelper.IsUpcomingRemindableAppointment(a)
            && a.ReminderStatus is ReminderStatus.NotReminded or ReminderStatus.NoReply);
        ActiveBoardQueueCount = waitingRows.Count + inChairRows.Count + readyForPaymentRows.Count;

        if (selectedId.HasValue)
        {
            SelectedQueueRow = QueueRows.FirstOrDefault(r => r.Source.Id == selectedId.Value);
        }

        QueueCount = ActiveBoardQueueCount + ScheduledQueueCount;
        UpdateQueuePulseSummary(
            waitingRows.Count,
            inChairRows.Count,
            readyForPaymentRows.Count,
            ScheduledQueueCount,
            UnremindedScheduledCount,
            CompletedQueueCount);
        NotifyQueueCommandStates();
    }

    private void RefreshQueueElapsedTimers()
    {
        if (!_activeUserPreferences.ShowWaitTimers)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        WaitingColumn.RefreshElapsed(now);
        InChairColumn.RefreshElapsed(now);
        ReadyForPaymentColumn.RefreshElapsed(now);
        foreach (var row in QueueRows)
        {
            if (!row.IsFinished)
            {
                row.RefreshElapsed(now);
            }
        }
    }

    private void UpdateQueuePulseSummary(
        int waiting,
        int inChair,
        int readyForPayment,
        int scheduled,
        int unreminded,
        int completed)
    {
        QueuePulseSummary = IsSimpleQueueView
            ? $"{waiting} Waiting · {inChair} In Chair · {readyForPayment} Ready for Payment"
            : $"{waiting} Waiting · {inChair} In Chair · {readyForPayment} Ready for Payment · {scheduled} Scheduled ({unreminded} unreminded) · {completed} Completed";
    }

    private void NotifyQueueCommandStates()
    {
        OnPropertyChanged(nameof(HasSelectedQueueRow));
        OnPropertyChanged(nameof(CanApproveSelectedAi));
        OnPropertyChanged(nameof(CanRemindSelected));
        OnPropertyChanged(nameof(HasSmartNextAction));
        OnPropertyChanged(nameof(SmartNextActionLabel));
        OnPropertyChanged(nameof(CanCheckInSelected));
        OnPropertyChanged(nameof(CanCallToChairSelected));
        OnPropertyChanged(nameof(CanSendToCheckoutSelected));
        OnPropertyChanged(nameof(CanCompleteSelected));
        OnPropertyChanged(nameof(CanCancelSelectedQueue));
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
