using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Occlusa.Desktop.Services;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.ViewModels;

/// <summary>
/// Interactive hourly availability tile shown in Step 2 of AppointmentDetailModal.
/// </summary>
public sealed class DentistSlotAvailabilityVm
{
    public DentistSlotAvailabilityVm(
        string label,
        string startHour,
        string startMinute,
        int bookedCount,
        string statusLabel,
        string summary,
        bool isSelected)
    {
        Label = label;
        StartHour = startHour;
        StartMinute = startMinute;
        BookedCount = bookedCount;
        IsAvailable = bookedCount == 0;
        StatusLabel = statusLabel;
        Summary = summary;
        IsSelected = isSelected;
    }

    public string Label { get; }
    public string StartHour { get; }
    public string StartMinute { get; }
    public int BookedCount { get; }
    public bool IsAvailable { get; }
    public string StatusLabel { get; }
    public string Summary { get; }
    public bool IsSelected { get; }
}

/// <summary>
/// ViewModel for AppointmentDetailModal implementing the 4-step clinic counter booking flow:
/// 1. Select Treatment -> filter qualified Dentists
/// 2. Pick Date & inspect Dentist hourly availability
/// 3. Search Patient by IC / Name / Phone (auto-suggest)
/// 4. Book existing patient or prompt to register new patient inline.
/// </summary>
public partial class AppointmentDetailViewModel : ObservableObject
{
    private const int MaxSuggestedPatients = 4;

    private readonly string _branchId;
    private readonly IReadOnlyList<PatientDto> _allPatients;
    private readonly IReadOnlyList<DentistDto> _dentists;
    private readonly IReadOnlyList<TreatmentDto> _treatments;
    private readonly IReadOnlyList<AppointmentQueueItemDto> _branchAppointments;
    private readonly OcclusaApiClient _apiClient;
    private readonly AppointmentQueueItemDto? _existing;
    private readonly Action<AppointmentQueueItemDto>? _onSaved;
    private readonly Action<PatientDto>? _onPatientRegistered;
    private bool _suppressDirty;
    private DateOnly? _parsedNewPatientDob;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    [NotifyPropertyChangedFor(nameof(ModalTitle))]
    private bool _isRegisterNewPatientMode;

    [ObservableProperty]
    private string _newPatientFullName = string.Empty;

    [ObservableProperty]
    private string _newPatientIcNumber = string.Empty;

    [ObservableProperty]
    private string _newPatientPhone = string.Empty;

    [ObservableProperty]
    private string _newPatientGender = "Female";

    [ObservableProperty]
    private string _newPatientAllergies = string.Empty;

    [ObservableProperty]
    private bool _newPatientOnAnticoagulants;

    [ObservableProperty]
    private bool _newPatientHasDiabetes;

    [ObservableProperty]
    private bool _newPatientIsPregnant;

    [ObservableProperty]
    private string _nricParseHint = "Enter 12-digit NRIC (auto-fills DOB & gender) or Passport #";

    [ObservableProperty]
    private string _patientSearchText = string.Empty;

    [ObservableProperty]
    private PatientDto? _selectedPatient;

    [ObservableProperty]
    private DentistDto? _selectedDentist;

    [ObservableProperty]
    private TreatmentDto? _selectedTreatment;

    [ObservableProperty]
    private string _customProcedureText = string.Empty;

    [ObservableProperty]
    private string _roomName = "Suite 1";

    [ObservableProperty]
    private DateTime? _selectedDate = DateTime.Today;

    [ObservableProperty]
    private string _selectedHour = "09";

    [ObservableProperty]
    private string _selectedMinute = "00";

    [ObservableProperty]
    private int _durationMinutes = 30;

    [ObservableProperty]
    private QueueStatus _status = QueueStatus.Scheduled;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string _treatmentFeeSummary = "Standard Fee: RM 50.00";

    [ObservableProperty]
    private string _dentistQualificationHint = string.Empty;

    [ObservableProperty]
    private string _availabilitySummary = string.Empty;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string _statusMessage = "Step 1: Choose treatment & dentist · Step 2: Check date availability · Step 3: Enter patient IC, name, or phone.";

    [ObservableProperty]
    private bool _isStatusError;

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

    public AppointmentDetailViewModel(
        string branchId,
        IReadOnlyList<PatientDto> allPatients,
        IReadOnlyList<DentistDto> branchDentists,
        IReadOnlyList<TreatmentDto> treatments,
        IReadOnlyList<AppointmentQueueItemDto> branchAppointments,
        OcclusaApiClient apiClient,
        AppointmentQueueItemDto? existingAppointment = null,
        QuickBookSlotArgs? prefill = null,
        Action<AppointmentQueueItemDto>? onSaved = null,
        Action<PatientDto>? onPatientRegistered = null,
        bool startInNewPatientMode = false)
    {
        _branchId = branchId;
        _allPatients = allPatients;
        _dentists = branchDentists;
        _treatments = treatments;
        _branchAppointments = branchAppointments;
        _apiClient = apiClient;
        _existing = existingAppointment;
        _onSaved = onSaved;
        _onPatientRegistered = onPatientRegistered;

        SuggestedPatients = new ObservableCollection<PatientDto>();
        EligibleDentists = new ObservableCollection<DentistDto>();
        Treatments = new ObservableCollection<TreatmentDto>(treatments);
        DentistAvailabilitySlots = new ObservableCollection<DentistSlotAvailabilityVm>();

        var roomSeed = branchId == "PJ-SS2"
            ? new[] { "PJ Room 1", "PJ Room 2", "Suite 1", "Suite 2" }
            : new[] { "Suite 1", "Suite 2", "Suite 3", "PJ Room 1" };
        AvailableRooms = new ObservableCollection<string>(roomSeed);
        AvailableHours = new ObservableCollection<string> { "09", "10", "11", "12", "13", "14", "15", "16", "17" };
        AvailableMinutes = new ObservableCollection<string> { "00", "15", "30", "45" };
        AvailableDurations = new ObservableCollection<int> { 15, 30, 45, 60, 90, 120 };
        AvailableStatuses = Enum.GetValues<QueueStatus>();
        AvailableGenders = ["Female", "Male", "Unspecified"];

        _suppressDirty = true;

        if (existingAppointment != null)
        {
            InitializeFromExisting(existingAppointment);
        }
        else
        {
            _isRegisterNewPatientMode = startInNewPatientMode;
            InitializeForNew(prefill);
        }

        RefreshPatientSuggestions();
        RefreshAvailabilitySlots();
        SyncBannerFromCurrentMode();
        UpdateTreatmentSummary();
        NotifyPatientLookupStateChanged();

        _suppressDirty = false;
        HasUnsavedChanges = false;
    }

    public event Action? RequestCloseApproved;

    public ObservableCollection<PatientDto> SuggestedPatients { get; }
    public ObservableCollection<DentistDto> EligibleDentists { get; }
    public ObservableCollection<TreatmentDto> Treatments { get; }
    public ObservableCollection<DentistSlotAvailabilityVm> DentistAvailabilitySlots { get; }
    public ObservableCollection<string> AvailableRooms { get; }
    public ObservableCollection<string> AvailableHours { get; }
    public ObservableCollection<string> AvailableMinutes { get; }
    public ObservableCollection<int> AvailableDurations { get; }
    public QueueStatus[] AvailableStatuses { get; }
    public string[] AvailableGenders { get; }

    public bool IsEditMode => _existing != null;
    public bool CanShowQuickRegisterButton => !IsEditMode && !IsRegisterNewPatientMode && SelectedPatient == null;
    public bool CanShowBackToSearchButton => !IsEditMode && IsRegisterNewPatientMode;
    public bool ShowConfirmedPatientCard => !IsRegisterNewPatientMode && SelectedPatient != null;
    public bool ShowPatientLookupPanel => !IsRegisterNewPatientMode && SelectedPatient == null;
    public bool HasPatientSuggestions => ShowPatientLookupPanel && SuggestedPatients.Count > 0;
    public bool ShowNoPatientMatchPrompt => ShowPatientLookupPanel && !string.IsNullOrWhiteSpace(PatientSearchText) && SuggestedPatients.Count == 0;
    public bool ShowLookupHint => ShowPatientLookupPanel && string.IsNullOrWhiteSpace(PatientSearchText);
    public string NoPatientMatchText => $"No existing patient found for \"{(PatientSearchText ?? string.Empty).Trim()}\".";

    public bool ShowPatientSafetyBanner =>
        IsEditMode
        || SelectedPatient != null
        || (IsRegisterNewPatientMode && (
            !string.IsNullOrWhiteSpace(NewPatientFullName)
            || !string.IsNullOrWhiteSpace(NewPatientIcNumber)
            || !string.IsNullOrWhiteSpace(NewPatientAllergies)
            || NewPatientOnAnticoagulants
            || NewPatientHasDiabetes
            || NewPatientIsPregnant));

    public PatientDto? CurrentPatientForNavigation => SelectedPatient;
    public bool CanApproveAiBooking => IsEditMode && Status == QueueStatus.AiBookedPending;
    public bool CanCancelAppointment => IsEditMode && Status != QueueStatus.Cancelled && Status != QueueStatus.Completed;

    public string ModalTitle => IsEditMode
        ? $"Appointment Details — {_existing!.PatientName}"
        : IsRegisterNewPatientMode
            ? $"Register Patient & Book ({_branchId})"
            : $"Book New Appointment ({_branchId})";

    public string ModalSubtitle => IsEditMode
        ? $"{_existing!.BranchId} · Scheduled {_existing.ScheduledAt.ToLocalTime():ddd dd MMM yyyy, HH:mm}"
        : "1. Treatment & Qualified Dentist  ·  2. Date & Availability  ·  3. Patient IC / Name / Phone";

    public string BadgeLabel => IsEditMode ? Status.ToString() : "New Booking";

    public string SaveButtonText => IsEditMode
        ? "Save Changes"
        : IsRegisterNewPatientMode
            ? "Register & Book"
            : "Book Appointment";

    partial void OnIsRegisterNewPatientModeChanged(bool value) => NotifyPatientLookupStateChanged();

    partial void OnPatientSearchTextChanged(string value) => RefreshPatientSuggestions();

    partial void OnSelectedPatientChanged(PatientDto? value)
    {
        SyncBannerFromCurrentMode();
        NotifyPatientLookupStateChanged();
        MarkDirty();
    }

    partial void OnNewPatientFullNameChanged(string value)
    {
        SyncBannerFromCurrentMode();
        MarkDirty();
    }

    partial void OnNewPatientIcNumberChanged(string value)
    {
        var parsed = MyKadParser.ParseNric(value);
        if (parsed.IsValid)
        {
            _parsedNewPatientDob = parsed.DateOfBirth;
            if (!string.IsNullOrWhiteSpace(parsed.Gender))
            {
                _suppressDirty = true;
                NewPatientGender = parsed.Gender;
                _suppressDirty = false;
            }

            bool exists = _allPatients.Any(p =>
                string.Equals(p.IcNumber, parsed.NormalizedIc, StringComparison.OrdinalIgnoreCase));
            NricParseHint = exists
                ? $"NRIC valid ({parsed.Gender}, DOB {parsed.DateOfBirth:dd MMM yyyy}) · Matches existing patient"
                : $"NRIC valid · DOB {parsed.DateOfBirth:dd MMM yyyy} · {parsed.Gender} ({parsed.BirthPlace})";
        }
        else
        {
            _parsedNewPatientDob = null;
            NricParseHint = "Enter 12-digit NRIC (auto-fills DOB & gender) or Passport #";
        }

        SyncBannerFromCurrentMode();
        MarkDirty();
    }

    partial void OnNewPatientPhoneChanged(string value) => MarkDirty();
    partial void OnNewPatientGenderChanged(string value) => MarkDirty();

    partial void OnNewPatientAllergiesChanged(string value)
    {
        SyncBannerFromCurrentMode();
        MarkDirty();
    }

    partial void OnNewPatientOnAnticoagulantsChanged(bool value)
    {
        SyncBannerFromCurrentMode();
        MarkDirty();
    }

    partial void OnNewPatientHasDiabetesChanged(bool value)
    {
        SyncBannerFromCurrentMode();
        MarkDirty();
    }

    partial void OnNewPatientIsPregnantChanged(bool value)
    {
        SyncBannerFromCurrentMode();
        MarkDirty();
    }

    partial void OnSelectedDentistChanged(DentistDto? value)
    {
        if (value != null && !_suppressDirty)
        {
            if (!IsEditMode || string.IsNullOrWhiteSpace(RoomName))
            {
                EnsureRoomOption(value.DefaultRoom);
                RoomName = value.DefaultRoom;
            }
        }

        if (!_suppressDirty)
        {
            RefreshAvailabilitySlots();
        }
        MarkDirty();
    }

    partial void OnSelectedTreatmentChanged(TreatmentDto? value)
    {
        if (value != null && !_suppressDirty)
        {
            DurationMinutes = AvailableDurations.Contains(value.DefaultDurationMinutes)
                ? value.DefaultDurationMinutes
                : 30;
            CustomProcedureText = value.Name;
        }

        if (!_suppressDirty)
        {
            RefreshEligibleDentists(preserveSelection: true);
            RefreshAvailabilitySlots();
        }

        UpdateTreatmentSummary();
        MarkDirty();
    }

    partial void OnCustomProcedureTextChanged(string value) => MarkDirty();
    partial void OnRoomNameChanged(string value) => MarkDirty();

    partial void OnSelectedDateChanged(DateTime? value)
    {
        if (!_suppressDirty)
        {
            RefreshAvailabilitySlots();
        }
        MarkDirty();
    }

    partial void OnSelectedHourChanged(string value)
    {
        if (!_suppressDirty)
        {
            RefreshAvailabilitySlots();
        }
        MarkDirty();
    }

    partial void OnSelectedMinuteChanged(string value)
    {
        if (!_suppressDirty)
        {
            RefreshAvailabilitySlots();
        }
        MarkDirty();
    }

    partial void OnDurationMinutesChanged(int value) => MarkDirty();

    partial void OnStatusChanged(QueueStatus value)
    {
        OnPropertyChanged(nameof(CanApproveAiBooking));
        OnPropertyChanged(nameof(CanCancelAppointment));
        MarkDirty();
    }

    partial void OnNotesChanged(string value) => MarkDirty();

    [RelayCommand]
    private void SetDateOffset(string offsetDaysStr)
    {
        if (int.TryParse(offsetDaysStr, out int days))
        {
            SelectedDate = DateTime.Today.AddDays(days);
        }
    }

    [RelayCommand]
    private void SelectAvailabilitySlot(DentistSlotAvailabilityVm? slot)
    {
        if (slot == null)
        {
            return;
        }

        _suppressDirty = true;
        SelectedHour = slot.StartHour;
        SelectedMinute = slot.StartMinute;
        _suppressDirty = false;
        MarkDirty();

        RefreshAvailabilitySlots();
        IsStatusError = false;
        StatusMessage = slot.IsAvailable
            ? $"Selected open slot {slot.Label} with {SelectedDentist?.FullName ?? "dentist"}. Enter patient IC, name, or phone."
            : $"Selected {slot.Label} ({slot.Summary}) — unrestricted booking allowed.";
    }

    [RelayCommand]
    private void SelectSuggestedPatient(PatientDto? patient)
    {
        if (patient == null)
        {
            return;
        }

        _suppressDirty = true;
        SelectedPatient = patient;
        PatientSearchText = string.Empty;
        _suppressDirty = false;

        RefreshPatientSuggestions();
        SyncBannerFromCurrentMode();
        NotifyPatientLookupStateChanged();
        MarkDirty();

        IsStatusError = false;
        StatusMessage = $"Patient selected: {patient.FullName} ({patient.IcNumber}). Ready to book.";
    }

    [RelayCommand]
    private void ChangeSelectedPatient()
    {
        _suppressDirty = true;
        SelectedPatient = null;
        IsRegisterNewPatientMode = false;
        PatientSearchText = string.Empty;
        _suppressDirty = false;

        RefreshPatientSuggestions();
        SyncBannerFromCurrentMode();
        NotifyPatientLookupStateChanged();
        IsStatusError = false;
        StatusMessage = "Type patient IC number, full name, or phone to search.";
    }

    [RelayCommand]
    private void StartRegisterNewPatient()
    {
        if (IsEditMode)
        {
            return;
        }

        var query = (PatientSearchText ?? string.Empty).Trim();
        _suppressDirty = true;
        IsRegisterNewPatientMode = true;

        if (!string.IsNullOrEmpty(query))
        {
            var digits = new string(query.Where(char.IsAsciiDigit).ToArray());
            var parsedNric = MyKadParser.ParseNric(query);
            if (parsedNric.IsValid || (digits.Length >= 6 && !query.StartsWith('+') && !digits.StartsWith("01")))
            {
                NewPatientIcNumber = parsedNric.IsValid ? parsedNric.NormalizedIc : query;
                if (parsedNric.IsValid)
                {
                    _parsedNewPatientDob = parsedNric.DateOfBirth;
                    if (!string.IsNullOrWhiteSpace(parsedNric.Gender))
                    {
                        NewPatientGender = parsedNric.Gender;
                    }
                    NricParseHint = $"NRIC valid · DOB {parsedNric.DateOfBirth:dd MMM yyyy} · {parsedNric.Gender} ({parsedNric.BirthPlace})";
                }
            }
            else if (query.StartsWith('+') || (digits.Length >= 7 && digits.StartsWith("01")))
            {
                NewPatientPhone = query;
            }
            else
            {
                NewPatientFullName = query;
            }
        }

        _suppressDirty = false;
        SyncBannerFromCurrentMode();
        NotifyPatientLookupStateChanged();
        IsStatusError = false;
        StatusMessage = "Fill in the new patient's details and click Register & Book.";
    }

    [RelayCommand]
    private void BackToPatientSearch()
    {
        if (IsEditMode)
        {
            return;
        }

        _suppressDirty = true;
        IsRegisterNewPatientMode = false;
        _suppressDirty = false;

        RefreshPatientSuggestions();
        SyncBannerFromCurrentMode();
        NotifyPatientLookupStateChanged();
    }

    [RelayCommand]
    public async Task SaveAppointmentAsync()
    {
        IsStatusError = false;

        if (!int.TryParse(SelectedHour, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hour))
        {
            hour = 9;
        }
        if (!int.TryParse(SelectedMinute, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minute))
        {
            minute = 0;
        }

        var date = SelectedDate ?? DateTime.Today;
        var localDateTime = new DateTime(date.Year, date.Month, date.Day, hour, minute, 0, DateTimeKind.Local);
        var scheduledAt = new DateTimeOffset(localDateTime);

        string doctorName = SelectedDentist?.FullName ?? _existing?.DoctorName ?? "Dr. Aina Rahman";
        string procedure = !string.IsNullOrWhiteSpace(CustomProcedureText)
            ? CustomProcedureText.Trim()
            : (SelectedTreatment?.Name ?? "Consultation");
        string room = string.IsNullOrWhiteSpace(RoomName)
            ? (SelectedDentist?.DefaultRoom ?? "Suite 1")
            : RoomName.Trim();

        try
        {
            AppointmentQueueItemDto saved;
            if (!IsEditMode && IsRegisterNewPatientMode)
            {
                if (string.IsNullOrWhiteSpace(NewPatientFullName))
                {
                    IsStatusError = true;
                    StatusMessage = "Enter the new patient's full name before booking.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(NewPatientIcNumber))
                {
                    IsStatusError = true;
                    StatusMessage = "Enter the new patient's IC / Passport # before booking.";
                    return;
                }

                var result = await _apiClient.RegisterPatientAndBookAsync(new RegisterPatientAndAppointmentRequest
                {
                    Patient = new UpsertPatientRequest
                    {
                        FullName = NewPatientFullName.Trim(),
                        IcNumber = NewPatientIcNumber.Trim(),
                        DateOfBirth = _parsedNewPatientDob ?? new DateOnly(1990, 1, 1),
                        Gender = NewPatientGender,
                        Phone = NewPatientPhone?.Trim() ?? string.Empty,
                        PrimaryBranchId = _branchId,
                        MedicalAlerts = new MedicalAlertDto
                        {
                            Allergies = NewPatientAllergies?.Trim() ?? string.Empty,
                            OnAnticoagulants = NewPatientOnAnticoagulants,
                            HasDiabetes = NewPatientHasDiabetes,
                            IsPregnant = NewPatientIsPregnant
                        }
                    },
                    Appointment = new CreateAppointmentRequest
                    {
                        BranchId = _branchId,
                        DentistId = SelectedDentist?.Id,
                        DoctorName = doctorName,
                        TreatmentId = SelectedTreatment?.Id,
                        RoomName = room,
                        ScheduledAt = scheduledAt,
                        DurationMinutes = DurationMinutes,
                        ProcedureSummary = procedure,
                        Notes = Notes?.Trim() ?? string.Empty,
                        Status = Status
                    }
                });

                _onPatientRegistered?.Invoke(result.Patient);
                saved = result.Appointment;
                StatusMessage = $"Registered {result.Patient.FullName} & booked with {saved.DoctorName} at {saved.ScheduledAt.ToLocalTime():dd MMM HH:mm}.";
            }
            else if (IsEditMode)
            {
                if (SelectedPatient == null)
                {
                    IsStatusError = true;
                    StatusMessage = "Please select a patient before saving.";
                    return;
                }

                saved = await _apiClient.UpdateAppointmentAsync(_existing!.Id, new UpdateAppointmentRequest
                {
                    PatientId = SelectedPatient.Id,
                    DentistId = SelectedDentist?.Id,
                    DoctorName = doctorName,
                    TreatmentId = SelectedTreatment?.Id,
                    ProcedureSummary = procedure,
                    RoomName = room,
                    ScheduledAt = scheduledAt,
                    DurationMinutes = DurationMinutes,
                    Status = Status,
                    Notes = Notes?.Trim() ?? string.Empty,
                    UpdatedBy = "Desktop Reception"
                });
                StatusMessage = $"Updated appointment for {saved.PatientName}.";
            }
            else
            {
                if (SelectedPatient == null)
                {
                    IsStatusError = true;
                    StatusMessage = "Search and select a patient (or click + Register New Patient) before booking.";
                    return;
                }

                saved = await _apiClient.CreateAppointmentAsync(new CreateAppointmentRequest
                {
                    BranchId = _branchId,
                    PatientId = SelectedPatient.Id,
                    DentistId = SelectedDentist?.Id,
                    DoctorName = doctorName,
                    TreatmentId = SelectedTreatment?.Id,
                    RoomName = room,
                    ScheduledAt = scheduledAt,
                    DurationMinutes = DurationMinutes,
                    ProcedureSummary = procedure,
                    Notes = Notes?.Trim() ?? string.Empty,
                    Status = Status
                });
                StatusMessage = $"Booked {saved.PatientName} with {saved.DoctorName} at {saved.ScheduledAt.ToLocalTime():dd MMM HH:mm}.";
            }

            HasUnsavedChanges = false;
            _onSaved?.Invoke(saved);
            RequestCloseApproved?.Invoke();
        }
        catch (HttpRequestException ex)
        {
            IsStatusError = true;
            StatusMessage = $"Cannot reach Occlusa API ({_apiClient.BaseUrl}): {ex.Message}";
        }
        catch (Exception ex)
        {
            IsStatusError = true;
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApproveAiBookingAsync()
    {
        if (!IsEditMode || Status != QueueStatus.AiBookedPending)
        {
            return;
        }

        IsStatusError = false;
        try
        {
            var approved = await _apiClient.ApproveAiBookingAsync(_existing!.Id);
            _suppressDirty = true;
            Status = approved.Status;
            _suppressDirty = false;
            HasUnsavedChanges = false;
            StatusMessage = $"AI-Booked appointment approved for {approved.PatientName}.";
            _onSaved?.Invoke(approved);
        }
        catch (HttpRequestException ex)
        {
            IsStatusError = true;
            StatusMessage = $"Cannot reach Occlusa API ({_apiClient.BaseUrl}): {ex.Message}";
        }
        catch (Exception ex)
        {
            IsStatusError = true;
            StatusMessage = $"Approval failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CancelAppointmentAsync()
    {
        if (!IsEditMode || !CanCancelAppointment)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"Mark {_existing!.PatientName}'s appointment on {_existing.ScheduledAt.ToLocalTime():dd MMM yyyy HH:mm} as Cancelled?",
            "Confirm Cancellation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        IsStatusError = false;
        try
        {
            var updated = await _apiClient.UpdateAppointmentAsync(_existing.Id, new UpdateAppointmentRequest
            {
                PatientId = SelectedPatient?.Id ?? _existing.PatientId,
                DentistId = SelectedDentist?.Id ?? _existing.DentistId,
                DoctorName = SelectedDentist?.FullName ?? _existing.DoctorName,
                TreatmentId = SelectedTreatment?.Id ?? _existing.TreatmentId,
                ProcedureSummary = !string.IsNullOrWhiteSpace(CustomProcedureText) ? CustomProcedureText.Trim() : _existing.ProcedureSummary,
                RoomName = RoomName,
                ScheduledAt = _existing.ScheduledAt,
                DurationMinutes = DurationMinutes,
                Status = QueueStatus.Cancelled,
                Notes = Notes?.Trim() ?? string.Empty,
                UpdatedBy = "Desktop Reception"
            });

            HasUnsavedChanges = false;
            StatusMessage = $"Appointment cancelled for {updated.PatientName}.";
            _onSaved?.Invoke(updated);
            RequestCloseApproved?.Invoke();
        }
        catch (HttpRequestException ex)
        {
            IsStatusError = true;
            StatusMessage = $"Cannot reach Occlusa API ({_apiClient.BaseUrl}): {ex.Message}";
        }
        catch (Exception ex)
        {
            IsStatusError = true;
            StatusMessage = $"Cancellation failed: {ex.Message}";
        }
    }

    private void InitializeFromExisting(AppointmentQueueItemDto appt)
    {
        SelectedPatient = _allPatients.FirstOrDefault(p => p.Id == appt.PatientId)
            ?? new PatientDto
            {
                Id = appt.PatientId,
                FullName = appt.PatientName,
                IcNumber = appt.PatientIc,
                PrimaryBranchId = appt.BranchId
            };

        SelectedTreatment = (appt.TreatmentId.HasValue
            ? _treatments.FirstOrDefault(t => t.Id == appt.TreatmentId.Value)
            : null)
            ?? _treatments.FirstOrDefault(t => appt.ProcedureSummary.Contains(t.Name, StringComparison.OrdinalIgnoreCase))
            ?? _treatments.FirstOrDefault();

        var initialDentist = (appt.DentistId.HasValue
            ? _dentists.FirstOrDefault(d => d.Id == appt.DentistId.Value)
            : null)
            ?? _dentists.FirstOrDefault(d => string.Equals(d.FullName, appt.DoctorName, StringComparison.OrdinalIgnoreCase))
            ?? _dentists.FirstOrDefault();

        SelectedDentist = initialDentist;
        RefreshEligibleDentists(preserveSelection: true);

        CustomProcedureText = appt.ProcedureSummary;
        EnsureRoomOption(appt.RoomName);
        RoomName = string.IsNullOrWhiteSpace(appt.RoomName) ? (SelectedDentist?.DefaultRoom ?? "Suite 1") : appt.RoomName;

        var local = appt.ScheduledAt.ToLocalTime();
        SelectedDate = local.Date;
        SelectedHour = Math.Clamp(local.Hour, 9, 17).ToString("00", CultureInfo.InvariantCulture);
        int snappedMinute = (local.Minute / 15) * 15;
        SelectedMinute = snappedMinute.ToString("00", CultureInfo.InvariantCulture);

        if (!AvailableDurations.Contains(appt.DurationMinutes) && appt.DurationMinutes > 0)
        {
            AvailableDurations.Add(appt.DurationMinutes);
        }
        DurationMinutes = appt.DurationMinutes > 0 ? appt.DurationMinutes : 30;

        Status = appt.Status;
        Notes = appt.Notes;
        StatusMessage = $"Viewing {appt.PatientName} ({appt.Status}).";
    }

    private void InitializeForNew(QuickBookSlotArgs? prefill)
    {
        SelectedPatient = null;

        SelectedTreatment = (prefill?.TreatmentId is Guid tid
            ? _treatments.FirstOrDefault(t => t.Id == tid)
            : null) ?? _treatments.FirstOrDefault();

        CustomProcedureText = SelectedTreatment?.Name ?? "Dental Consultation & Checkup";
        DurationMinutes = SelectedTreatment?.DefaultDurationMinutes ?? 30;

        RefreshEligibleDentists(preserveSelection: false);

        if (prefill?.DentistId is Guid did)
        {
            var match = EligibleDentists.FirstOrDefault(d => d.Id == did);
            if (match != null)
            {
                SelectedDentist = match;
            }
        }
        else if (!string.IsNullOrWhiteSpace(prefill?.DoctorName))
        {
            var match = EligibleDentists.FirstOrDefault(d =>
                string.Equals(d.FullName, prefill.DoctorName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                SelectedDentist = match;
            }
        }

        var room = !string.IsNullOrWhiteSpace(prefill?.DefaultRoom)
            ? prefill.DefaultRoom
            : (SelectedDentist?.DefaultRoom ?? (_branchId == "PJ-SS2" ? "PJ Room 1" : "Suite 1"));
        EnsureRoomOption(room);
        RoomName = room;

        if (prefill != null)
        {
            SelectedDate = prefill.Date.ToDateTime(TimeOnly.MinValue);
            SelectedHour = Math.Clamp(prefill.StartTime.Hour, 9, 17).ToString("00", CultureInfo.InvariantCulture);
            int snappedMinute = (prefill.StartTime.Minute / 15) * 15;
            SelectedMinute = snappedMinute.ToString("00", CultureInfo.InvariantCulture);
        }
        else
        {
            SelectedDate = DateTime.Today;
            SelectedHour = "09";
            SelectedMinute = "00";
        }

        Status = QueueStatus.Scheduled;
        Notes = string.Empty;
        StatusMessage = IsRegisterNewPatientMode
            ? "1. Choose treatment & dentist · 2. Pick date/slot · 3. Enter new patient details."
            : "1. Choose treatment & dentist · 2. Pick open slot · 3. Search patient IC, name, or phone.";
    }

    private void RefreshEligibleDentists(bool preserveSelection)
    {
        var eligible = AppointmentCalendarHelper.GetEligibleDentists(_dentists, SelectedTreatment?.Id).ToList();
        if (IsEditMode && SelectedDentist != null && eligible.All(d => d.Id != SelectedDentist.Id))
        {
            eligible.Add(SelectedDentist);
        }

        var previous = preserveSelection ? SelectedDentist : null;
        bool prevSuppress = _suppressDirty;
        _suppressDirty = true;
        try
        {
            EligibleDentists.Clear();
            foreach (var d in eligible)
            {
                EligibleDentists.Add(d);
            }

            SelectedDentist = (previous != null
                ? EligibleDentists.FirstOrDefault(d => d.Id == previous.Id)
                : null) ?? EligibleDentists.FirstOrDefault();

            if (SelectedDentist != null && (!IsEditMode || string.IsNullOrWhiteSpace(RoomName)))
            {
                EnsureRoomOption(SelectedDentist.DefaultRoom);
                RoomName = SelectedDentist.DefaultRoom;
            }
        }
        finally
        {
            _suppressDirty = prevSuppress;
        }

        DentistQualificationHint = SelectedTreatment == null
            ? $"{EligibleDentists.Count} active dentist(s)"
            : EligibleDentists.Count == 1
                ? $"1 qualified dentist: {EligibleDentists[0].FullName}"
                : $"{EligibleDentists.Count} qualified dentists for {SelectedTreatment.Name}";
    }

    private void RefreshAvailabilitySlots()
    {
        var date = DateOnly.FromDateTime(SelectedDate ?? DateTime.Today);
        var localOffset = DateTimeOffset.Now.Offset;
        var slots = AppointmentCalendarHelper.GetDentistDaySlotAvailability(
            _branchAppointments,
            SelectedDentist?.Id,
            SelectedDentist?.FullName ?? _existing?.DoctorName,
            date,
            localOffset);

        int selectedHourInt = int.TryParse(SelectedHour, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)
            ? h
            : 9;

        DentistAvailabilitySlots.Clear();
        int openCount = 0;
        int bookedCount = 0;

        foreach (var slot in slots)
        {
            if (slot.IsAvailable)
            {
                openCount++;
            }
            else
            {
                bookedCount++;
            }

            bool isSelected = slot.Start.Hour == selectedHourInt;
            DentistAvailabilitySlots.Add(new DentistSlotAvailabilityVm(
                slot.Label,
                slot.Start.Hour.ToString("00", CultureInfo.InvariantCulture),
                slot.Start.Minute.ToString("00", CultureInfo.InvariantCulture),
                slot.BookedCount,
                slot.StatusLabel,
                slot.Summary,
                isSelected));
        }

        var docLabel = SelectedDentist?.FullName ?? "Dentist";
        AvailabilitySummary = $"{docLabel} · {date:ddd dd MMM}: {openCount} open, {bookedCount} booked";
    }

    private void RefreshPatientSuggestions()
    {
        SuggestedPatients.Clear();
        var query = PatientSearchText?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(query))
        {
            foreach (var p in _allPatients.Where(p => AppointmentCalendarHelper.MatchesPatientQuery(p, query)).Take(MaxSuggestedPatients))
            {
                SuggestedPatients.Add(p);
            }
        }

        NotifyPatientLookupStateChanged();
    }

    private void NotifyPatientLookupStateChanged()
    {
        OnPropertyChanged(nameof(CanShowQuickRegisterButton));
        OnPropertyChanged(nameof(CanShowBackToSearchButton));
        OnPropertyChanged(nameof(ShowConfirmedPatientCard));
        OnPropertyChanged(nameof(ShowPatientLookupPanel));
        OnPropertyChanged(nameof(HasPatientSuggestions));
        OnPropertyChanged(nameof(ShowNoPatientMatchPrompt));
        OnPropertyChanged(nameof(ShowLookupHint));
        OnPropertyChanged(nameof(NoPatientMatchText));
        OnPropertyChanged(nameof(ShowPatientSafetyBanner));
    }

    private void EnsureRoomOption(string? room)
    {
        if (!string.IsNullOrWhiteSpace(room) && !AvailableRooms.Contains(room))
        {
            AvailableRooms.Add(room);
        }
    }

    private void UpdateTreatmentSummary()
    {
        if (SelectedTreatment == null)
        {
            TreatmentFeeSummary = "Custom procedure";
            return;
        }

        TreatmentFeeSummary = $"Standard Fee: RM {SelectedTreatment.DefaultFee:N2} · {SelectedTreatment.Category} ({SelectedTreatment.DefaultDurationMinutes}m default)";
    }

    private void SyncBannerFromCurrentMode()
    {
        if (!IsEditMode && IsRegisterNewPatientMode)
        {
            BannerPatientName = string.IsNullOrWhiteSpace(NewPatientFullName)
                ? "New Patient"
                : NewPatientFullName.Trim();
            BannerIcNumber = NewPatientIcNumber?.Trim() ?? string.Empty;
            BannerIsMyKadVerified = false;
            BannerAllergies = NewPatientAllergies?.Trim() ?? string.Empty;
            BannerOnAnticoagulants = NewPatientOnAnticoagulants;
            BannerHasDiabetes = NewPatientHasDiabetes;
            BannerIsPregnant = NewPatientIsPregnant;
            BannerAlertNotes = string.Empty;
            OnPropertyChanged(nameof(ShowPatientSafetyBanner));
            return;
        }

        if (SelectedPatient == null)
        {
            BannerPatientName = string.Empty;
            BannerIcNumber = string.Empty;
            BannerIsMyKadVerified = false;
            BannerAllergies = string.Empty;
            BannerOnAnticoagulants = false;
            BannerHasDiabetes = false;
            BannerIsPregnant = false;
            BannerAlertNotes = string.Empty;
            OnPropertyChanged(nameof(ShowPatientSafetyBanner));
            return;
        }

        BannerPatientName = SelectedPatient.FullName;
        BannerIcNumber = SelectedPatient.IcNumber;
        BannerIsMyKadVerified = SelectedPatient.IsMyKadVerified;
        BannerAllergies = SelectedPatient.MedicalAlerts.Allergies;
        BannerOnAnticoagulants = SelectedPatient.MedicalAlerts.OnAnticoagulants;
        BannerHasDiabetes = SelectedPatient.MedicalAlerts.HasDiabetes;
        BannerIsPregnant = SelectedPatient.MedicalAlerts.IsPregnant;
        BannerAlertNotes = SelectedPatient.MedicalAlerts.Notes;
        OnPropertyChanged(nameof(ShowPatientSafetyBanner));
    }

    private void MarkDirty()
    {
        if (!_suppressDirty)
        {
            HasUnsavedChanges = true;
        }
    }
}
