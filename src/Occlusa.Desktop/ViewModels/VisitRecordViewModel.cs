using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Occlusa.Desktop.Services;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.ViewModels;

public enum VisitWorkspaceTab
{
    Odontogram,
    SoapNotes
}

/// <summary>
/// Interactive cell ViewModel representing one tooth (Permanent 11-48 or Primary 51-85)
/// on a specific Patient Visit's anatomical odontogram.
/// </summary>
public partial class VisitToothCellViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConditionCode))]
    [NotifyPropertyChangedFor(nameof(StageCode))]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    private ToothCondition _condition = ToothCondition.Healthy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    private string _surfaces = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StageCode))]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    private TreatmentStage _stage = TreatmentStage.Proposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StageCode))]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    private string _procedureName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    private decimal _fee;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    private string _doctorName = "Dr. Aina Rahman";

    [ObservableProperty]
    private bool _isSelected;

    public VisitToothCellViewModel(int toothNumber)
    {
        ToothNumber = toothNumber;
    }

    public int ToothNumber { get; }

    public string ConditionCode => Condition switch
    {
        ToothCondition.Caries => "CAR",
        ToothCondition.Filling => "FIL",
        ToothCondition.Crown => "CRN",
        ToothCondition.RootCanal => "RCT",
        ToothCondition.ExtractionNeeded => "EXT",
        ToothCondition.Missing => "MIS",
        ToothCondition.Implant => "IMP",
        _ => "OK"
    };

    public string StageCode
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ProcedureName))
            {
                return Condition == ToothCondition.Healthy ? "-" : "BASE";
            }

            return Stage switch
            {
                TreatmentStage.Completed => "DONE",
                TreatmentStage.Accepted => "ACPT",
                _ => "PROP"
            };
        }
    }

    public string ToolTipText
    {
        get
        {
            var surf = string.IsNullOrWhiteSpace(Surfaces) ? string.Empty : $" ({Surfaces})";
            if (Condition == ToothCondition.Healthy && string.IsNullOrWhiteSpace(ProcedureName))
            {
                return $"FDI #{ToothNumber} · Healthy";
            }

            if (string.IsNullOrWhiteSpace(ProcedureName))
            {
                return $"FDI #{ToothNumber} · {Condition}{surf} (Baseline / Existing)";
            }

            return $"FDI #{ToothNumber} · {Condition}{surf}\nProcedure: {ProcedureName}\nStage: {Stage} · RM {Fee:N2}\nClinician: {DoctorName}";
        }
    }
}

/// <summary>
/// ViewModel for VisitRecordModal combining a Patient Visit's Kreloses-style Anatomical + 5-Surface
/// Odontogram (Permanent 11-48 &amp; Primary 51-85) and Clinical SOAP Notes with unified unsaved-changes guard.
/// </summary>
public partial class VisitRecordViewModel : ObservableObject
{
    private static readonly Brush SurfaceEmptyBrush = FreezeBrush("#FFFFFF");
    private static readonly Brush CariesBrush = FreezeBrush("#EF4444");
    private static readonly Brush FillingBrush = FreezeBrush("#0EA5E9");
    private static readonly Brush CrownBrush = FreezeBrush("#8B5CF6");
    private static readonly Brush RctBrush = FreezeBrush("#F97316");
    private static readonly Brush ImplantBrush = FreezeBrush("#14B8A6");

    private readonly OcclusaApiClient _apiClient;
    private readonly string _activeStaffName;
    private readonly Action<PatientVisitDto>? _onVisitSaved;
    private readonly Action? _onBillingUpdated;
    private readonly Dictionary<int, VisitToothCellViewModel> _toothLookup = new();

    private bool _suppressDirty;
    private bool _visitNotesDirty;
    private bool _toothEditDirty;

    [ObservableProperty]
    private PatientDto _patient;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModalTitle))]
    [NotifyPropertyChangedFor(nameof(ModalSubtitle))]
    [NotifyPropertyChangedFor(nameof(BadgeText))]
    private PatientVisitDto _visit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOdontogramTab))]
    [NotifyPropertyChangedFor(nameof(IsSoapTab))]
    [NotifyPropertyChangedFor(nameof(SwitchTabButtonText))]
    private VisitWorkspaceTab _activeTab;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPermanentDentition))]
    [NotifyPropertyChangedFor(nameof(IsPrimaryDentition))]
    private DentitionMode _dentitionMode = DentitionMode.Permanent;

    [ObservableProperty]
    private string _permanentTabLabel = "Permanent (11–48 · 32)";

    [ObservableProperty]
    private string _primaryTabLabel = "Primary / Child (51–85 · 20)";

    [ObservableProperty]
    private string _visitSummaryLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SurfaceTopLabel))]
    [NotifyPropertyChangedFor(nameof(SurfaceBottomLabel))]
    [NotifyPropertyChangedFor(nameof(SurfaceLeftLabel))]
    [NotifyPropertyChangedFor(nameof(SurfaceRightLabel))]
    [NotifyPropertyChangedFor(nameof(SelectedToothHeader))]
    private int _selectedToothNumber = 16;

    [ObservableProperty]
    private ToothCondition _selectedCondition = ToothCondition.Healthy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSurfacesDisplay))]
    [NotifyPropertyChangedFor(nameof(SurfaceTopFill))]
    [NotifyPropertyChangedFor(nameof(SurfaceBottomFill))]
    [NotifyPropertyChangedFor(nameof(SurfaceLeftFill))]
    [NotifyPropertyChangedFor(nameof(SurfaceRightFill))]
    [NotifyPropertyChangedFor(nameof(SurfaceCenterFill))]
    private string _selectedSurfaces = string.Empty;

    [ObservableProperty]
    private string _procedureName = string.Empty;

    [ObservableProperty]
    private string _feeInput = "200.00";

    [ObservableProperty]
    private TreatmentStage _selectedStage = TreatmentStage.Proposed;

    [ObservableProperty]
    private string _doctorName = "Dr. Aina Rahman";

    [ObservableProperty]
    private DateTime? _visitDateInput;

    [ObservableProperty]
    private string _subjective = string.Empty;

    [ObservableProperty]
    private string _objective = string.Empty;

    [ObservableProperty]
    private string _assessment = string.Empty;

    [ObservableProperty]
    private string _plan = string.Empty;

    [ObservableProperty]
    private string _anesthesiaUsed = string.Empty;

    [ObservableProperty]
    private string _toothShade = string.Empty;

    [ObservableProperty]
    private string _postOpInstructions = string.Empty;

    [ObservableProperty]
    private SoapMacroDto? _selectedMacro;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string _statusMessage = "Click any tooth silhouette or occlusal surface (M, O, D, B, L) to chart.";

    public VisitRecordViewModel(
        PatientDto patient,
        PatientVisitDto visit,
        IReadOnlyList<DentistDto> dentists,
        IReadOnlyList<SoapMacroDto> soapMacros,
        OcclusaApiClient apiClient,
        string activeStaffName = "Dr. Aina Rahman",
        VisitWorkspaceTab initialTab = VisitWorkspaceTab.Odontogram,
        Action<PatientVisitDto>? onVisitSaved = null,
        Action? onBillingUpdated = null)
    {
        _patient = patient;
        _visit = visit;
        _apiClient = apiClient;
        _activeStaffName = string.IsNullOrWhiteSpace(activeStaffName) ? "Desktop Clinician" : activeStaffName.Trim();
        _activeTab = initialTab;
        _onVisitSaved = onVisitSaved;
        _onBillingUpdated = onBillingUpdated;

        UpperRightPermanent = BuildQuad(OdontogramHelper.UpperRightPermanent);
        UpperLeftPermanent = BuildQuad(OdontogramHelper.UpperLeftPermanent);
        LowerRightPermanent = BuildQuad(OdontogramHelper.LowerRightPermanent);
        LowerLeftPermanent = BuildQuad(OdontogramHelper.LowerLeftPermanent);

        UpperRightPrimary = BuildQuad(OdontogramHelper.UpperRightPrimary);
        UpperLeftPrimary = BuildQuad(OdontogramHelper.UpperLeftPrimary);
        LowerRightPrimary = BuildQuad(OdontogramHelper.LowerRightPrimary);
        LowerLeftPrimary = BuildQuad(OdontogramHelper.LowerLeftPrimary);

        Conditions = Enum.GetValues<ToothCondition>();
        DentistNames = new ObservableCollection<string>(
            dentists.Select(d => d.FullName)
                .Append(visit.DoctorName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        if (DentistNames.Count == 0)
        {
            DentistNames.Add("Dr. Aina Rahman");
        }

        SoapMacros = new ObservableCollection<SoapMacroDto>(soapMacros);
        SelectedMacro = SoapMacros.FirstOrDefault();

        ProcedurePresets = new ObservableCollection<string>
        {
            "Posterior Composite Filling",
            "Routine Scaling & Polishing",
            "Root Canal Therapy (Stage 1)",
            "Porcelain / Zirconia Crown",
            "Simple Extraction",
            "Surgical Extraction",
            "Dental Implant"
        };

        HydrateFromVisit(visit);
        SelectTooth(16);
    }

    public ObservableCollection<VisitToothCellViewModel> UpperRightPermanent { get; }
    public ObservableCollection<VisitToothCellViewModel> UpperLeftPermanent { get; }
    public ObservableCollection<VisitToothCellViewModel> LowerRightPermanent { get; }
    public ObservableCollection<VisitToothCellViewModel> LowerLeftPermanent { get; }

    public ObservableCollection<VisitToothCellViewModel> UpperRightPrimary { get; }
    public ObservableCollection<VisitToothCellViewModel> UpperLeftPrimary { get; }
    public ObservableCollection<VisitToothCellViewModel> LowerRightPrimary { get; }
    public ObservableCollection<VisitToothCellViewModel> LowerLeftPrimary { get; }

    public ToothCondition[] Conditions { get; }
    public ObservableCollection<string> DentistNames { get; }
    public ObservableCollection<SoapMacroDto> SoapMacros { get; }
    public ObservableCollection<string> ProcedurePresets { get; }

    public bool IsOdontogramTab => ActiveTab == VisitWorkspaceTab.Odontogram;
    public bool IsSoapTab => ActiveTab == VisitWorkspaceTab.SoapNotes;
    public bool IsPermanentDentition => DentitionMode == DentitionMode.Permanent;
    public bool IsPrimaryDentition => DentitionMode == DentitionMode.Primary;

    public string ModalTitle => $"Clinical Visit #{Visit.VisitNumber} — {Patient.FullName}";
    public string ModalSubtitle =>
        $"{Visit.BranchId} · Visit Date: {Visit.VisitDate.ToLocalTime():dd MMM yyyy HH:mm} · Clinician: {Visit.DoctorName}";
    public string BadgeText => $"Visit #{Visit.VisitNumber} · FDI Chart";
    public string SwitchTabButtonText => IsOdontogramTab ? "Switch to SOAP Notes" : "Switch to Odontogram";

    public string SelectedToothHeader =>
        OdontogramHelper.IsPrimaryTooth(SelectedToothNumber)
            ? $"Selected Primary Tooth: FDI #{SelectedToothNumber}"
            : $"Selected Permanent Tooth: FDI #{SelectedToothNumber}";

    public string SelectedSurfacesDisplay =>
        string.IsNullOrWhiteSpace(SelectedSurfaces) ? "Whole / Unspecified" : SelectedSurfaces;

    public string SurfaceTopLabel => OdontogramHelper.IsUpperTooth(SelectedToothNumber) ? "B" : "L";
    public string SurfaceBottomLabel => OdontogramHelper.IsUpperTooth(SelectedToothNumber) ? "L" : "B";
    public string SurfaceLeftLabel => OdontogramHelper.IsMirroredLeftQuadrant(SelectedToothNumber) ? "M" : "D";
    public string SurfaceRightLabel => OdontogramHelper.IsMirroredLeftQuadrant(SelectedToothNumber) ? "D" : "M";

    public Brush SurfaceTopFill => GetInspectorSurfaceBrush(SurfaceTopLabel[0]);
    public Brush SurfaceBottomFill => GetInspectorSurfaceBrush(SurfaceBottomLabel[0]);
    public Brush SurfaceLeftFill => GetInspectorSurfaceBrush(SurfaceLeftLabel[0]);
    public Brush SurfaceRightFill => GetInspectorSurfaceBrush(SurfaceRightLabel[0]);
    public Brush SurfaceCenterFill => GetInspectorSurfaceBrush('O');

    partial void OnSelectedConditionChanged(ToothCondition value)
    {
        NotifySurfaceBrushes();
        MarkToothDirty();
    }

    partial void OnSelectedSurfacesChanged(string value) => MarkToothDirty();
    partial void OnProcedureNameChanged(string value) => MarkToothDirty();
    partial void OnFeeInputChanged(string value) => MarkToothDirty();
    partial void OnSelectedStageChanged(TreatmentStage value) => MarkToothDirty();

    partial void OnDoctorNameChanged(string value) => MarkVisitDirty();
    partial void OnVisitDateInputChanged(DateTime? value) => MarkVisitDirty();
    partial void OnSubjectiveChanged(string value) => MarkVisitDirty();
    partial void OnObjectiveChanged(string value) => MarkVisitDirty();
    partial void OnAssessmentChanged(string value) => MarkVisitDirty();
    partial void OnPlanChanged(string value) => MarkVisitDirty();
    partial void OnAnesthesiaUsedChanged(string value) => MarkVisitDirty();
    partial void OnToothShadeChanged(string value) => MarkVisitDirty();
    partial void OnPostOpInstructionsChanged(string value) => MarkVisitDirty();

    public async Task RefreshVisitFromServerAsync()
    {
        try
        {
            var fresh = await _apiClient.GetVisitAsync(Visit.Id);
            if (fresh is not null)
            {
                Visit = fresh;
                HydrateFromVisit(fresh);
                SelectTooth(SelectedToothNumber);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not refresh visit record: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SelectTab(string tabName)
    {
        ActiveTab = string.Equals(tabName, "SoapNotes", StringComparison.OrdinalIgnoreCase)
            ? VisitWorkspaceTab.SoapNotes
            : VisitWorkspaceTab.Odontogram;
    }

    [RelayCommand]
    private void ToggleTab()
    {
        ActiveTab = IsOdontogramTab ? VisitWorkspaceTab.SoapNotes : VisitWorkspaceTab.Odontogram;
    }

    [RelayCommand]
    private void SetDentitionMode(string modeName)
    {
        var target = string.Equals(modeName, "Primary", StringComparison.OrdinalIgnoreCase)
            ? DentitionMode.Primary
            : DentitionMode.Permanent;

        if (DentitionMode == target)
        {
            return;
        }

        DentitionMode = target;
        if (target == DentitionMode.Primary && !OdontogramHelper.IsPrimaryTooth(SelectedToothNumber))
        {
            SelectTooth(55);
        }
        else if (target == DentitionMode.Permanent && OdontogramHelper.IsPrimaryTooth(SelectedToothNumber))
        {
            SelectTooth(16);
        }
    }

    [RelayCommand]
    private void SelectTooth(int toothNumber)
    {
        if (!_toothLookup.TryGetValue(toothNumber, out var selected))
        {
            return;
        }

        foreach (var cell in _toothLookup.Values)
        {
            cell.IsSelected = cell.ToothNumber == toothNumber;
        }

        _suppressDirty = true;
        SelectedToothNumber = toothNumber;
        SelectedCondition = selected.Condition;
        SelectedSurfaces = selected.Surfaces;
        ProcedureName = selected.ProcedureName;
        FeeInput = (selected.Fee > 0m ? selected.Fee : 200m).ToString("0.00", CultureInfo.InvariantCulture);
        SelectedStage = selected.Stage;
        _suppressDirty = false;

        _toothEditDirty = false;
        SyncUnsavedFlag();
        NotifySurfaceBrushes();
    }

    [RelayCommand]
    private void TogglePositionSurface(string position)
    {
        char targetChar = position switch
        {
            "Top" => SurfaceTopLabel[0],
            "Bottom" => SurfaceBottomLabel[0],
            "Left" => SurfaceLeftLabel[0],
            "Right" => SurfaceRightLabel[0],
            _ => 'O'
        };

        var current = OdontogramHelper.NormalizeSurfaces(SelectedSurfaces);
        string updated = current.Contains(targetChar)
            ? new string(current.Where(c => c != targetChar).ToArray())
            : OdontogramHelper.NormalizeSurfaces(current + targetChar);

        if (SelectedCondition == ToothCondition.Healthy && !string.IsNullOrEmpty(updated))
        {
            SelectedCondition = ToothCondition.Filling;
        }

        SelectedSurfaces = updated;
        NotifySurfaceBrushes();
    }

    [RelayCommand]
    private void ClearSurfaces()
    {
        SelectedSurfaces = string.Empty;
        NotifySurfaceBrushes();
    }

    [RelayCommand]
    private void ApplyProcedurePreset(string preset)
    {
        ProcedureName = preset;
        (SelectedCondition, FeeInput) = preset switch
        {
            "Routine Scaling & Polishing" => (SelectedCondition == ToothCondition.Healthy ? ToothCondition.Healthy : SelectedCondition, "150.00"),
            "Posterior Composite Filling" => (ToothCondition.Filling, "200.00"),
            "Simple Extraction" => (ToothCondition.ExtractionNeeded, "180.00"),
            "Surgical Extraction" => (ToothCondition.ExtractionNeeded, "450.00"),
            "Root Canal Therapy (Stage 1)" => (ToothCondition.RootCanal, "450.00"),
            "Porcelain / Zirconia Crown" => (ToothCondition.Crown, "950.00"),
            "Dental Implant" => (ToothCondition.Implant, "3500.00"),
            _ => (SelectedCondition, FeeInput)
        };
    }

    [RelayCommand]
    private async Task RecordBaselineToothAsync()
    {
        try
        {
            var saved = await _apiClient.UpdateVisitOdontogramToothAsync(Visit.Id, new UpdateOdontogramRequest
            {
                ToothNumber = SelectedToothNumber,
                Condition = SelectedCondition,
                Surfaces = SelectedCondition is ToothCondition.Healthy or ToothCondition.Missing
                    ? string.Empty
                    : SelectedSurfaces,
                ProcedureName = string.Empty,
                Fee = 0m,
                Stage = TreatmentStage.Proposed,
                DoctorName = EffectiveDoctorName,
                BranchId = Visit.BranchId,
                UpdatedBy = _activeStaffName
            });

            ApplySavedToothToUi(saved);
            StatusMessage = saved.Condition == ToothCondition.Healthy
                ? $"Cleared Tooth #{saved.ToothNumber} to Healthy."
                : $"Recorded baseline existing {saved.Condition}{(string.IsNullOrEmpty(saved.Surfaces) ? string.Empty : $" ({saved.Surfaces})")} on Tooth #{saved.ToothNumber} (RM 0.00).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Baseline save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StageAndSaveToothAsync(string stageName)
    {
        if (!Enum.TryParse<TreatmentStage>(stageName, out var targetStage))
        {
            return;
        }

        SelectedStage = targetStage;
        await SaveSelectedToothInternalAsync();
    }

    [RelayCommand]
    private async Task ResetOdontogramAsync(string modeName)
    {
        var mode = string.Equals(modeName, "CarryForward", StringComparison.OrdinalIgnoreCase)
            ? OdontogramInitMode.CarryForward
            : OdontogramInitMode.Blank;

        string prompt = mode == OdontogramInitMode.CarryForward
            ? $"Re-synchronize Visit #{Visit.VisitNumber}'s odontogram from the patient's previous visit baseline?"
            : $"Reset all 52 teeth on Visit #{Visit.VisitNumber}'s odontogram to Healthy (Blank)?\n(Prior visit odontograms will NOT be affected.)";

        var confirm = MessageBox.Show(
            prompt,
            "Confirm Visit Odontogram Reset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var updatedVisit = await _apiClient.ResetVisitOdontogramAsync(Visit.Id, new ResetVisitOdontogramRequest
            {
                Mode = mode,
                UpdatedBy = _activeStaffName
            });

            Visit = updatedVisit;
            HydrateTeethFromVisit(updatedVisit);
            SelectTooth(SelectedToothNumber);
            _onVisitSaved?.Invoke(updatedVisit);

            StatusMessage = mode == OdontogramInitMode.CarryForward
                ? $"Re-synced Visit #{updatedVisit.VisitNumber} odontogram from prior visit ({updatedVisit.ChartedTeethCount} charted teeth)."
                : $"Reset Visit #{updatedVisit.VisitNumber} odontogram to blank Healthy state.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Odontogram reset failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ApplySelectedMacro()
    {
        if (SelectedMacro is null)
        {
            return;
        }

        Subjective = SelectedMacro.SubjectiveTemplate;
        Objective = SelectedMacro.ObjectiveTemplate;
        Assessment = SelectedMacro.AssessmentTemplate;
        Plan = SelectedMacro.PlanTemplate;
        StatusMessage = $"Applied Smart SOAP Macro: {SelectedMacro.Title}";
    }

    [RelayCommand]
    private void ApplyAnesthesiaMacro(string value) => AnesthesiaUsed = value;

    [RelayCommand]
    private void ApplyShadeMacro(string value) => ToothShade = value;

    [RelayCommand]
    private void ApplyPostOpMacro(string value) => PostOpInstructions = value;

    [RelayCommand]
    public async Task SaveAllVisitChangesAsync()
    {
        try
        {
            if (_toothEditDirty)
            {
                bool ok = await SaveSelectedToothInternalAsync();
                if (!ok)
                {
                    return;
                }
            }

            if (_visitNotesDirty)
            {
                var targetDate = VisitDateInput.HasValue
                    ? new DateTimeOffset(
                        VisitDateInput.Value.Year,
                        VisitDateInput.Value.Month,
                        VisitDateInput.Value.Day,
                        Visit.VisitDate.ToLocalTime().Hour,
                        Visit.VisitDate.ToLocalTime().Minute,
                        0,
                        DateTimeOffset.Now.Offset)
                    : Visit.VisitDate;

                var updated = await _apiClient.UpdatePatientVisitAsync(Visit.Id, new UpdatePatientVisitRequest
                {
                    BranchId = Visit.BranchId,
                    DentistId = Visit.DentistId,
                    DoctorName = EffectiveDoctorName,
                    VisitDate = targetDate,
                    Subjective = Subjective.Trim(),
                    Objective = Objective.Trim(),
                    Assessment = Assessment.Trim(),
                    Plan = Plan.Trim(),
                    AnesthesiaUsed = AnesthesiaUsed.Trim(),
                    ToothShade = ToothShade.Trim(),
                    PostOpInstructions = PostOpInstructions.Trim(),
                    UpdatedBy = _activeStaffName
                });

                Visit = updated;
                _visitNotesDirty = false;
                SyncUnsavedFlag();
                RefreshSummaryLabels();
                _onVisitSaved?.Invoke(updated);
            }

            StatusMessage = $"Saved Visit #{Visit.VisitNumber} clinical record at {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    private async Task<bool> SaveSelectedToothInternalAsync()
    {
        if (!decimal.TryParse(FeeInput, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal fee) || fee < 0m)
        {
            StatusMessage = "Enter a valid non-negative RM fee.";
            return false;
        }

        string effectiveProcedure = string.IsNullOrWhiteSpace(ProcedureName)
            ? (SelectedCondition == ToothCondition.Healthy ? string.Empty : $"{SelectedCondition} Treatment")
            : ProcedureName.Trim();

        var saved = await _apiClient.UpdateVisitOdontogramToothAsync(Visit.Id, new UpdateOdontogramRequest
        {
            ToothNumber = SelectedToothNumber,
            Condition = SelectedCondition,
            Surfaces = SelectedSurfaces,
            ProcedureName = effectiveProcedure,
            Fee = SelectedCondition == ToothCondition.Healthy && string.IsNullOrWhiteSpace(ProcedureName) ? 0m : fee,
            Stage = SelectedStage,
            DoctorName = EffectiveDoctorName,
            BranchId = Visit.BranchId,
            UpdatedBy = _activeStaffName
        });

        ApplySavedToothToUi(saved);

        if (saved.Stage == TreatmentStage.Completed && !string.IsNullOrWhiteSpace(saved.ProcedureName))
        {
            StatusMessage = $"Tooth #{saved.ToothNumber}{(string.IsNullOrEmpty(saved.Surfaces) ? string.Empty : $" ({saved.Surfaces})")} COMPLETED — synced RM {saved.Fee:N2} to Visit Billing.";
            _onBillingUpdated?.Invoke();
        }
        else
        {
            StatusMessage = $"Saved Tooth #{saved.ToothNumber} ({saved.Condition}, {saved.Stage}) on Visit #{Visit.VisitNumber}.";
        }

        return true;
    }

    private void ApplySavedToothToUi(OdontogramToothDto saved)
    {
        if (_toothLookup.TryGetValue(saved.ToothNumber, out var cell))
        {
            cell.Condition = saved.Condition;
            cell.Surfaces = saved.Surfaces;
            cell.ProcedureName = saved.ProcedureName;
            cell.Fee = saved.Fee;
            cell.Stage = saved.Stage;
            cell.DoctorName = saved.DoctorName;
        }

        var idx = Visit.Odontogram.FindIndex(t => t.ToothNumber == saved.ToothNumber);
        if (idx >= 0) Visit.Odontogram[idx] = saved;
        else Visit.Odontogram.Add(saved);

        _suppressDirty = true;
        ProcedureName = saved.ProcedureName;
        SelectedSurfaces = saved.Surfaces;
        if (saved.Fee > 0m)
        {
            FeeInput = saved.Fee.ToString("0.00", CultureInfo.InvariantCulture);
        }
        _suppressDirty = false;

        _toothEditDirty = false;
        SyncUnsavedFlag();
        RefreshSummaryLabels();
        NotifySurfaceBrushes();
        _onVisitSaved?.Invoke(Visit);
    }

    private void HydrateFromVisit(PatientVisitDto visit)
    {
        _suppressDirty = true;
        DoctorName = string.IsNullOrWhiteSpace(visit.DoctorName) ? "Dr. Aina Rahman" : visit.DoctorName;
        VisitDateInput = visit.VisitDate.ToLocalTime().Date;
        Subjective = visit.Subjective;
        Objective = visit.Objective;
        Assessment = visit.Assessment;
        Plan = visit.Plan;
        AnesthesiaUsed = visit.AnesthesiaUsed;
        ToothShade = visit.ToothShade;
        PostOpInstructions = visit.PostOpInstructions;
        _suppressDirty = false;

        HydrateTeethFromVisit(visit);
        _visitNotesDirty = false;
        _toothEditDirty = false;
        HasUnsavedChanges = false;
    }

    private void HydrateTeethFromVisit(PatientVisitDto visit)
    {
        foreach (var cell in _toothLookup.Values)
        {
            cell.Condition = ToothCondition.Healthy;
            cell.Surfaces = string.Empty;
            cell.ProcedureName = string.Empty;
            cell.Fee = 0m;
            cell.Stage = TreatmentStage.Proposed;
            cell.DoctorName = visit.DoctorName;
        }

        foreach (var entry in visit.Odontogram)
        {
            if (_toothLookup.TryGetValue(entry.ToothNumber, out var cell))
            {
                cell.Condition = entry.Condition;
                cell.Surfaces = entry.Surfaces;
                cell.ProcedureName = entry.ProcedureName;
                cell.Fee = entry.Fee;
                cell.Stage = entry.Stage;
                cell.DoctorName = string.IsNullOrWhiteSpace(entry.DoctorName) ? visit.DoctorName : entry.DoctorName;
            }
        }

        RefreshSummaryLabels();
    }

    private void RefreshSummaryLabels()
    {
        int permCharted = OdontogramHelper.PermanentTeeth.Count(n =>
            _toothLookup.TryGetValue(n, out var c) && (c.Condition != ToothCondition.Healthy || !string.IsNullOrWhiteSpace(c.ProcedureName)));
        int primCharted = OdontogramHelper.PrimaryTeeth.Count(n =>
            _toothLookup.TryGetValue(n, out var c) && (c.Condition != ToothCondition.Healthy || !string.IsNullOrWhiteSpace(c.ProcedureName)));

        PermanentTabLabel = permCharted > 0
            ? $"Permanent (11–48) · {permCharted} Charted"
            : "Permanent (11–48 · 32)";
        PrimaryTabLabel = primCharted > 0
            ? $"Primary / Child (51–85) · {primCharted} Charted"
            : "Primary / Child (51–85 · 20)";

        VisitSummaryLabel =
            $"Visit #{Visit.VisitNumber} · Charted: {permCharted + primCharted} teeth · Completed: {Visit.CompletedProceduresCount} (RM {Visit.CompletedTotalFee:N2})";
    }

    private ObservableCollection<VisitToothCellViewModel> BuildQuad(int[] numbers)
    {
        var list = new ObservableCollection<VisitToothCellViewModel>();
        foreach (int num in numbers)
        {
            var cell = new VisitToothCellViewModel(num);
            _toothLookup[num] = cell;
            list.Add(cell);
        }

        return list;
    }

    private Brush GetInspectorSurfaceBrush(char targetSurface)
    {
        var norm = OdontogramHelper.NormalizeSurfaces(SelectedSurfaces);
        bool active = SelectedCondition == ToothCondition.Crown
            || (!string.IsNullOrEmpty(norm) && norm.Contains(targetSurface));

        if (!active)
        {
            return SurfaceEmptyBrush;
        }

        return SelectedCondition switch
        {
            ToothCondition.Caries or ToothCondition.ExtractionNeeded => CariesBrush,
            ToothCondition.Crown => CrownBrush,
            ToothCondition.RootCanal => RctBrush,
            ToothCondition.Implant => ImplantBrush,
            _ => FillingBrush
        };
    }

    private void NotifySurfaceBrushes()
    {
        OnPropertyChanged(nameof(SurfaceTopFill));
        OnPropertyChanged(nameof(SurfaceBottomFill));
        OnPropertyChanged(nameof(SurfaceLeftFill));
        OnPropertyChanged(nameof(SurfaceRightFill));
        OnPropertyChanged(nameof(SurfaceCenterFill));
    }

    private string EffectiveDoctorName =>
        string.IsNullOrWhiteSpace(DoctorName) ? "Dr. Aina Rahman" : DoctorName.Trim();

    private void MarkVisitDirty()
    {
        if (_suppressDirty) return;
        _visitNotesDirty = true;
        SyncUnsavedFlag();
    }

    private void MarkToothDirty()
    {
        if (_suppressDirty) return;
        _toothEditDirty = true;
        SyncUnsavedFlag();
    }

    private void SyncUnsavedFlag() =>
        HasUnsavedChanges = _visitNotesDirty || _toothEditDirty;

    private static Brush FreezeBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}

