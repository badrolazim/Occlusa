using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Occlusa.Desktop.Services;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.ViewModels;

/// <summary>
/// ViewModel for PatientDetailModal supporting standalone new-patient registration (0 visits),
/// full editable patient demographics &amp; clinical safety flags, PDPA consent stamping, and
/// a 4-column paged Visit Records history (0..N visits) opening VisitRecordModal.
/// </summary>
public partial class PatientDetailViewModel : ObservableObject
{
    private readonly OcclusaApiClient _apiClient;
    private readonly string _activeBranchId;
    private readonly string _activeStaffName;
    private readonly string _defaultDoctorName;
    private readonly Action<PatientDto> _onPatientUpdated;
    private bool _suppressDirty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfileMode))]
    [NotifyPropertyChangedFor(nameof(ModalTitle))]
    [NotifyPropertyChangedFor(nameof(ModalSubtitle))]
    [NotifyPropertyChangedFor(nameof(MyKadBadgeLabel))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    [NotifyPropertyChangedFor(nameof(CanSignConsent))]
    private bool _isCreateMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModalTitle))]
    [NotifyPropertyChangedFor(nameof(ModalSubtitle))]
    [NotifyPropertyChangedFor(nameof(MyKadBadgeLabel))]
    private PatientDto _patient;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModalTitle))]
    private string _fullName = string.Empty;

    [ObservableProperty]
    private string _icNumber = string.Empty;

    [ObservableProperty]
    private DateTime? _dateOfBirthInput = new DateTime(1990, 1, 1);

    [ObservableProperty]
    private string _gender = "Female";

    [ObservableProperty]
    private string _primaryBranchId = "HQ-KL";

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _allergies = string.Empty;

    [ObservableProperty]
    private bool _onAnticoagulants;

    [ObservableProperty]
    private bool _hasDiabetes;

    [ObservableProperty]
    private bool _isPregnant;

    [ObservableProperty]
    private string _alertNotes = string.Empty;

    [ObservableProperty]
    private string _nricHintText = "12-digit Malaysian NRIC auto-fills Date of Birth & Gender";

    [ObservableProperty]
    private string _consentStatusText = "Consent Not Signed";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedVisit))]
    private PatientVisitRowItem? _selectedVisitRow;

    [ObservableProperty]
    private string _visitsSubtitle = "0 clinical visit records";

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public PatientDetailViewModel(
        PatientDto? existingPatient,
        string activeBranchId,
        string activeStaffName,
        string defaultDoctorName,
        OcclusaApiClient apiClient,
        Action<PatientDto> onPatientUpdated)
    {
        _activeBranchId = string.IsNullOrWhiteSpace(activeBranchId) ? "HQ-KL" : activeBranchId;
        _activeStaffName = string.IsNullOrWhiteSpace(activeStaffName) ? "Desktop Reception" : activeStaffName.Trim();
        _defaultDoctorName = string.IsNullOrWhiteSpace(defaultDoctorName) ? "Dr. Aina Rahman" : defaultDoctorName.Trim();
        _apiClient = apiClient;
        _onPatientUpdated = onPatientUpdated;

        AvailableGenders = ["Female", "Male", "Unspecified"];
        AvailableBranches = ["HQ-KL", "PJ-SS2"];
        VisitRows = new ObservableCollection<PatientVisitRowItem>();

        _isCreateMode = existingPatient is null;
        _patient = existingPatient ?? new PatientDto
        {
            PrimaryBranchId = _activeBranchId,
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = "Female"
        };

        _suppressDirty = true;
        FullName = _patient.FullName;
        IcNumber = _patient.IcNumber;
        DateOfBirthInput = _patient.DateOfBirth == default
            ? new DateTime(1990, 1, 1)
            : _patient.DateOfBirth.ToDateTime(TimeOnly.MinValue);
        Gender = string.IsNullOrWhiteSpace(_patient.Gender) ? "Female" : _patient.Gender;
        PrimaryBranchId = string.IsNullOrWhiteSpace(_patient.PrimaryBranchId) ? _activeBranchId : _patient.PrimaryBranchId;
        Phone = _patient.Phone;
        Address = _patient.Address;
        Allergies = _patient.MedicalAlerts.Allergies;
        OnAnticoagulants = _patient.MedicalAlerts.OnAnticoagulants;
        HasDiabetes = _patient.MedicalAlerts.HasDiabetes;
        IsPregnant = _patient.MedicalAlerts.IsPregnant;
        AlertNotes = _patient.MedicalAlerts.Notes;
        UpdateConsentText();
        _suppressDirty = false;
    }

    public string[] AvailableGenders { get; }
    public string[] AvailableBranches { get; }
    public ObservableCollection<PatientVisitRowItem> VisitRows { get; }

    public bool IsProfileMode => !IsCreateMode;
    public bool CanSignConsent => !IsCreateMode;
    public bool HasSelectedVisit => SelectedVisitRow is not null;

    public string ModalTitle => IsCreateMode
        ? (string.IsNullOrWhiteSpace(FullName) ? "Register New Patient" : $"Register Patient — {FullName}")
        : $"Patient Profile & Visit Registry — {Patient.FullName}";

    public string ModalSubtitle => IsCreateMode
        ? "Standalone registration · New patients start with 0 visit records until clinically seen"
        : $"IC: {Patient.IcNumber} · DOB: {Patient.DateOfBirth:dd MMM yyyy} ({Patient.Gender}) · Primary Branch: {Patient.PrimaryBranchId}";

    public string MyKadBadgeLabel => IsCreateMode
        ? "New Registration"
        : (Patient.IsMyKadVerified ? "MyKad Verified" : "Manual Registration");

    public string SaveButtonText => IsCreateMode ? "Register Patient" : "Save Patient Profile";

    partial void OnFullNameChanged(string value) => MarkDirty();

    partial void OnIcNumberChanged(string value)
    {
        var parsed = MyKadParser.ParseNric(value);
        if (parsed.IsValid)
        {
            _suppressDirty = true;
            if (parsed.DateOfBirth.HasValue)
            {
                DateOfBirthInput = parsed.DateOfBirth.Value.ToDateTime(TimeOnly.MinValue);
            }
            if (!string.IsNullOrWhiteSpace(parsed.Gender))
            {
                Gender = parsed.Gender;
            }
            _suppressDirty = false;
            NricHintText = $"Valid Malaysian NRIC · DOB {parsed.DateOfBirth:dd MMM yyyy} · {parsed.Gender} ({parsed.BirthPlace})";
        }
        else
        {
            NricHintText = "12-digit Malaysian NRIC auto-fills Date of Birth & Gender (or enter Passport #)";
        }

        MarkDirty();
    }

    partial void OnDateOfBirthInputChanged(DateTime? value) => MarkDirty();
    partial void OnGenderChanged(string value) => MarkDirty();
    partial void OnPrimaryBranchIdChanged(string value) => MarkDirty();
    partial void OnPhoneChanged(string value) => MarkDirty();
    partial void OnAddressChanged(string value) => MarkDirty();
    partial void OnAllergiesChanged(string value) => MarkDirty();
    partial void OnOnAnticoagulantsChanged(bool value) => MarkDirty();
    partial void OnHasDiabetesChanged(bool value) => MarkDirty();
    partial void OnIsPregnantChanged(bool value) => MarkDirty();
    partial void OnAlertNotesChanged(string value) => MarkDirty();

    public async Task LoadVisitsAsync()
    {
        if (IsCreateMode)
        {
            VisitRows.Clear();
            VisitsSubtitle = "Save patient profile first to create clinical visit records";
            return;
        }

        try
        {
            var visits = await _apiClient.GetPatientVisitsAsync(Patient.Id);
            Guid? prevSelected = SelectedVisitRow?.Source.Id;

            VisitRows.Clear();
            foreach (var v in visits.OrderByDescending(x => x.VisitDate).ThenByDescending(x => x.VisitNumber))
            {
                VisitRows.Add(new PatientVisitRowItem(v));
            }

            SelectedVisitRow = prevSelected.HasValue
                ? VisitRows.FirstOrDefault(r => r.Source.Id == prevSelected.Value) ?? VisitRows.FirstOrDefault()
                : VisitRows.FirstOrDefault();

            Patient.VisitCount = VisitRows.Count;
            Patient.LastVisitAt = VisitRows.FirstOrDefault()?.Source.VisitDate;
            VisitsSubtitle = VisitRows.Count == 0
                ? "0 clinical visits recorded — click + New Visit Record when patient attends"
                : $"{VisitRows.Count} clinical visit record{(VisitRows.Count == 1 ? string.Empty : "s")} · Double-click any visit for Odontogram & SOAP";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load visit history: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SignConsentAsync()
    {
        if (IsCreateMode)
        {
            return;
        }

        try
        {
            var updated = await _apiClient.SignConsentAsync(Patient.Id, new SignConsentRequest
            {
                SignedAt = DateTimeOffset.UtcNow,
                SignedBy = Patient.FullName
            }, _activeBranchId);

            Patient = updated;
            UpdateConsentText();
            _onPatientUpdated(updated);
            StatusMessage = "Digital PDPA & clinical consent stamped.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Consent signing failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SaveAllChangesAsync()
    {
        await SavePatientProfileInternalAsync();
    }

    public async Task<bool> SavePatientProfileInternalAsync()
    {
        if (string.IsNullOrWhiteSpace(FullName))
        {
            StatusMessage = "Enter the patient's Full Name before saving.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(IcNumber))
        {
            StatusMessage = "Enter the patient's IC Number or Passport # before saving.";
            return false;
        }

        try
        {
            var dob = DateOfBirthInput.HasValue
                ? DateOnly.FromDateTime(DateOfBirthInput.Value)
                : new DateOnly(1990, 1, 1);

            var alertDto = BuildAlertDto();
            var saved = await _apiClient.UpsertPatientAsync(new UpsertPatientRequest
            {
                Id = IsCreateMode ? null : Patient.Id,
                FullName = FullName.Trim(),
                IcNumber = IcNumber.Trim(),
                DateOfBirth = dob,
                Gender = Gender,
                Phone = Phone.Trim(),
                Address = Address.Trim(),
                PrimaryBranchId = PrimaryBranchId,
                IsMyKadVerified = Patient.IsMyKadVerified,
                ConsentSignedAt = Patient.ConsentSignedAt,
                MedicalAlerts = alertDto
            });

            saved = await _apiClient.UpdateMedicalAlertsAsync(saved.Id, alertDto, _activeBranchId);

            bool wasCreate = IsCreateMode;
            Patient = saved;
            IsCreateMode = false;
            HasUnsavedChanges = false;
            UpdateConsentText();
            _onPatientUpdated(saved);

            if (wasCreate)
            {
                await LoadVisitsAsync();
                StatusMessage = $"Registered {saved.FullName} (0 visits). You can close or click + New Visit Record.";
            }
            else
            {
                StatusMessage = $"Saved patient profile & safety alerts at {DateTime.Now:HH:mm:ss}.";
            }

            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
            return false;
        }
    }

    public async Task<PatientVisitDto?> CreateNewVisitRecordAsync()
    {
        if (IsCreateMode || HasUnsavedChanges)
        {
            bool saved = await SavePatientProfileInternalAsync();
            if (!saved)
            {
                return null;
            }
        }

        try
        {
            var created = await _apiClient.CreatePatientVisitAsync(Patient.Id, new CreatePatientVisitRequest
            {
                BranchId = _activeBranchId,
                DoctorName = _defaultDoctorName,
                VisitDate = DateTimeOffset.UtcNow,
                InitMode = OdontogramInitMode.CarryForward,
                CreatedBy = _activeStaffName
            });

            await LoadVisitsAsync();
            _onPatientUpdated(Patient);
            StatusMessage = $"Created Visit #{created.VisitNumber} (with carried-forward odontogram baseline).";
            return created;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not create visit record: {ex.Message}";
            return null;
        }
    }

    private MedicalAlertDto BuildAlertDto() => new()
    {
        Allergies = Allergies.Trim(),
        OnAnticoagulants = OnAnticoagulants,
        HasDiabetes = HasDiabetes,
        IsPregnant = IsPregnant,
        Notes = AlertNotes.Trim()
    };

    private void UpdateConsentText()
    {
        ConsentStatusText = Patient.ConsentSignedAt.HasValue
            ? $"PDPA Signed ({Patient.ConsentSignedAt.Value.ToLocalTime():dd MMM yyyy HH:mm})"
            : "PDPA Consent Not Yet Signed";
    }

    private void MarkDirty()
    {
        if (_suppressDirty) return;
        HasUnsavedChanges = true;
    }
}
