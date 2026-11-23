using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Occlusa.Desktop.Services;
using Occlusa.Shared.Models;

namespace Occlusa.Desktop.ViewModels;

/// <summary>
/// ViewModel for PatientDetailModal supporting demographics, MyKad badge, clinical safety alerts,
/// digital consent stamping, 1-per-page SOAP history viewer, and Smart SOAP macro authoring.
/// </summary>
public partial class PatientDetailViewModel : ObservableObject
{
    private readonly OcclusaApiClient _apiClient;
    private readonly string _activeBranchId;
    private readonly Action<PatientDto> _onPatientUpdated;
    private readonly List<SoapNoteDto> _soapNotes = new();
    private bool _suppressDirty;
    private bool _patientFieldsDirty;
    private bool _soapDraftDirty;

    [ObservableProperty]
    private PatientDto _patient;

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
    private string _consentStatusText = "Consent Not Yet Signed";

    [ObservableProperty]
    private bool _isConsentSigned;

    [ObservableProperty]
    private SoapNoteDto? _currentSoapNote;

    [ObservableProperty]
    private int _currentSoapPage = 1;

    [ObservableProperty]
    private int _totalSoapPages = 1;

    [ObservableProperty]
    private string _soapPaginationSummary = "No SOAP notes recorded";

    [ObservableProperty]
    private bool _canGoPrevSoap;

    [ObservableProperty]
    private bool _canGoNextSoap;

    [ObservableProperty]
    private bool _hasSoapNotes;

    [ObservableProperty]
    private SoapMacroDto? _selectedMacro;

    [ObservableProperty]
    private string _newDoctorName = "Dr. Aina Rahman";

    [ObservableProperty]
    private string _newSubjective = string.Empty;

    [ObservableProperty]
    private string _newObjective = string.Empty;

    [ObservableProperty]
    private string _newAssessment = string.Empty;

    [ObservableProperty]
    private string _newPlan = string.Empty;

    [ObservableProperty]
    private string _newAnesthesia = string.Empty;

    [ObservableProperty]
    private string _newToothShade = string.Empty;

    [ObservableProperty]
    private string _newPostOp = string.Empty;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public PatientDetailViewModel(
        PatientDto patient,
        string activeBranchId,
        IReadOnlyList<SoapMacroDto> soapMacros,
        OcclusaApiClient apiClient,
        Action<PatientDto> onPatientUpdated)
    {
        _patient = patient;
        _activeBranchId = activeBranchId;
        _apiClient = apiClient;
        _onPatientUpdated = onPatientUpdated;

        SoapMacros = new ObservableCollection<SoapMacroDto>(soapMacros);
        SelectedMacro = SoapMacros.FirstOrDefault();

        _suppressDirty = true;
        Phone = patient.Phone;
        Address = patient.Address;
        Allergies = patient.MedicalAlerts.Allergies;
        OnAnticoagulants = patient.MedicalAlerts.OnAnticoagulants;
        HasDiabetes = patient.MedicalAlerts.HasDiabetes;
        IsPregnant = patient.MedicalAlerts.IsPregnant;
        AlertNotes = patient.MedicalAlerts.Notes;
        UpdateConsentText();
        _suppressDirty = false;
    }

    public ObservableCollection<SoapMacroDto> SoapMacros { get; }

    public string ModalTitle => $"Patient Record — {Patient.FullName}";
    public string ModalSubtitle => $"DOB: {Patient.DateOfBirth:dd MMM yyyy} ({Patient.Gender}) · Branch: {Patient.PrimaryBranchId}";
    public string MyKadBadgeLabel => Patient.IsMyKadVerified ? "MyKad Verified" : "Manual Registration";

    public async Task LoadSoapHistoryAsync()
    {
        try
        {
            var notes = await _apiClient.GetSoapNotesAsync(Patient.Id);
            _soapNotes.Clear();
            _soapNotes.AddRange(notes.OrderByDescending(n => n.VisitDate));
            CurrentSoapPage = 1;
            RefreshSoapPager();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load SOAP history: {ex.Message}";
            RefreshSoapPager();
        }
    }

    partial void OnPhoneChanged(string value) => MarkPatientDirty();
    partial void OnAddressChanged(string value) => MarkPatientDirty();
    partial void OnAllergiesChanged(string value) => MarkPatientDirty();
    partial void OnOnAnticoagulantsChanged(bool value) => MarkPatientDirty();
    partial void OnHasDiabetesChanged(bool value) => MarkPatientDirty();
    partial void OnIsPregnantChanged(bool value) => MarkPatientDirty();
    partial void OnAlertNotesChanged(string value) => MarkPatientDirty();

    partial void OnNewSubjectiveChanged(string value) => MarkSoapDirty();
    partial void OnNewObjectiveChanged(string value) => MarkSoapDirty();
    partial void OnNewAssessmentChanged(string value) => MarkSoapDirty();
    partial void OnNewPlanChanged(string value) => MarkSoapDirty();
    partial void OnNewAnesthesiaChanged(string value) => MarkSoapDirty();
    partial void OnNewToothShadeChanged(string value) => MarkSoapDirty();
    partial void OnNewPostOpChanged(string value) => MarkSoapDirty();

    [RelayCommand]
    private void PrevSoapPage()
    {
        if (CurrentSoapPage > 1)
        {
            CurrentSoapPage--;
            RefreshSoapPager();
        }
    }

    [RelayCommand]
    private void NextSoapPage()
    {
        if (CurrentSoapPage < TotalSoapPages)
        {
            CurrentSoapPage++;
            RefreshSoapPager();
        }
    }

    [RelayCommand]
    private void ApplySelectedMacro()
    {
        if (SelectedMacro == null)
        {
            return;
        }

        NewSubjective = SelectedMacro.SubjectiveTemplate;
        NewObjective = SelectedMacro.ObjectiveTemplate;
        NewAssessment = SelectedMacro.AssessmentTemplate;
        NewPlan = SelectedMacro.PlanTemplate;
        StatusMessage = $"Applied Smart SOAP Macro: {SelectedMacro.Title}";
    }

    [RelayCommand]
    private void ApplyAnesthesiaMacro(string value)
    {
        NewAnesthesia = value;
    }

    [RelayCommand]
    private void ApplyShadeMacro(string value)
    {
        NewToothShade = value;
    }

    [RelayCommand]
    private void ApplyPostOpMacro(string value)
    {
        NewPostOp = value;
    }

    [RelayCommand]
    private async Task SignConsentAsync()
    {
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
        try
        {
            if (_patientFieldsDirty)
            {
                var updatedPatient = await _apiClient.UpsertPatientAsync(new UpsertPatientRequest
                {
                    Id = Patient.Id,
                    FullName = Patient.FullName,
                    IcNumber = Patient.IcNumber,
                    DateOfBirth = Patient.DateOfBirth,
                    Gender = Patient.Gender,
                    Phone = Phone.Trim(),
                    Address = Address.Trim(),
                    PrimaryBranchId = Patient.PrimaryBranchId,
                    IsMyKadVerified = Patient.IsMyKadVerified,
                    ConsentSignedAt = Patient.ConsentSignedAt,
                    MedicalAlerts = BuildAlertDto()
                });

                updatedPatient = await _apiClient.UpdateMedicalAlertsAsync(updatedPatient.Id, BuildAlertDto(), _activeBranchId);
                Patient = updatedPatient;
                _onPatientUpdated(updatedPatient);
                _patientFieldsDirty = false;
            }

            bool hasNewSoapContent = !string.IsNullOrWhiteSpace(NewSubjective)
                || !string.IsNullOrWhiteSpace(NewObjective)
                || !string.IsNullOrWhiteSpace(NewAssessment)
                || !string.IsNullOrWhiteSpace(NewPlan);

            if (_soapDraftDirty && hasNewSoapContent)
            {
                var createdNote = await _apiClient.CreateSoapNoteAsync(Patient.Id, new CreateSoapNoteRequest
                {
                    BranchId = _activeBranchId,
                    DoctorName = string.IsNullOrWhiteSpace(NewDoctorName) ? "Dr. Aina Rahman" : NewDoctorName.Trim(),
                    VisitDate = DateTimeOffset.UtcNow,
                    Subjective = NewSubjective.Trim(),
                    Objective = NewObjective.Trim(),
                    Assessment = NewAssessment.Trim(),
                    Plan = NewPlan.Trim(),
                    AnesthesiaUsed = NewAnesthesia.Trim(),
                    ToothShade = NewToothShade.Trim(),
                    PostOpInstructions = NewPostOp.Trim()
                });

                _soapNotes.Insert(0, createdNote);
                CurrentSoapPage = 1;
                RefreshSoapPager();
                ClearSoapEditor();
            }

            _soapDraftDirty = false;
            HasUnsavedChanges = false;
            StatusMessage = $"Saved patient chart & clinical notes at {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
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

    private void ClearSoapEditor()
    {
        _suppressDirty = true;
        NewSubjective = string.Empty;
        NewObjective = string.Empty;
        NewAssessment = string.Empty;
        NewPlan = string.Empty;
        NewAnesthesia = string.Empty;
        NewToothShade = string.Empty;
        NewPostOp = string.Empty;
        _suppressDirty = false;
    }

    private void RefreshSoapPager()
    {
        HasSoapNotes = _soapNotes.Count > 0;
        TotalSoapPages = Math.Max(1, _soapNotes.Count);
        CurrentSoapPage = Math.Clamp(CurrentSoapPage, 1, TotalSoapPages);
        CurrentSoapNote = _soapNotes.Count > 0 ? _soapNotes[CurrentSoapPage - 1] : null;
        CanGoPrevSoap = CurrentSoapPage > 1;
        CanGoNextSoap = CurrentSoapPage < TotalSoapPages;
        SoapPaginationSummary = _soapNotes.Count == 0
            ? "No SOAP notes recorded"
            : $"Note {CurrentSoapPage} of {_soapNotes.Count}";
    }

    private void UpdateConsentText()
    {
        IsConsentSigned = Patient.ConsentSignedAt.HasValue;
        ConsentStatusText = Patient.ConsentSignedAt.HasValue
            ? $"Digitally Signed ({Patient.ConsentSignedAt.Value.ToLocalTime():dd MMM yyyy HH:mm})"
            : "Consent Not Signed";
    }

    private void MarkPatientDirty()
    {
        if (_suppressDirty) return;
        _patientFieldsDirty = true;
        HasUnsavedChanges = true;
    }

    private void MarkSoapDirty()
    {
        if (_suppressDirty) return;
        _soapDraftDirty = true;
        HasUnsavedChanges = true;
    }
}
