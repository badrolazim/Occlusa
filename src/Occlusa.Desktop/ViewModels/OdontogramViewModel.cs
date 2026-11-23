using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Occlusa.Desktop.Services;
using Occlusa.Shared.Models;

namespace Occlusa.Desktop.ViewModels;

/// <summary>
/// Interactive cell representing one tooth on the FDI 32-tooth chart.
/// </summary>
public partial class ToothCellViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConditionCode))]
    private ToothCondition _condition = ToothCondition.Healthy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StageCode))]
    private TreatmentStage _stage = TreatmentStage.Proposed;

    [ObservableProperty]
    private string _procedureName = string.Empty;

    [ObservableProperty]
    private decimal _fee;

    [ObservableProperty]
    private string _doctorName = "Dr. Aina Rahman";

    [ObservableProperty]
    private bool _isSelected;

    public ToothCellViewModel(int toothNumber)
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

    public string StageCode => string.IsNullOrWhiteSpace(ProcedureName)
        ? "-"
        : Stage switch
        {
            TreatmentStage.Completed => "DONE",
            TreatmentStage.Accepted => "ACPT",
            _ => "PROP"
        };
}

/// <summary>
/// ViewModel for OdontogramModal supporting the 32-tooth FDI grid and 3-stage
/// treatment workflow (Proposed -> Accepted -> Completed -> Auto Billing Push).
/// </summary>
public partial class OdontogramViewModel : ObservableObject
{
    private static readonly int[] UpperRightNumbers = [18, 17, 16, 15, 14, 13, 12, 11];
    private static readonly int[] UpperLeftNumbers = [21, 22, 23, 24, 25, 26, 27, 28];
    private static readonly int[] LowerRightNumbers = [48, 47, 46, 45, 44, 43, 42, 41];
    private static readonly int[] LowerLeftNumbers = [31, 32, 33, 34, 35, 36, 37, 38];

    private readonly OcclusaApiClient _apiClient;
    private readonly string _activeBranchId;
    private readonly Action? _onBillingUpdated;
    private readonly Dictionary<int, ToothCellViewModel> _toothLookup = new();
    private bool _suppressDirty;

    [ObservableProperty]
    private PatientDto _patient;

    [ObservableProperty]
    private int _selectedToothNumber = 16;

    [ObservableProperty]
    private ToothCondition _selectedCondition = ToothCondition.Healthy;

    [ObservableProperty]
    private string _procedureName = string.Empty;

    [ObservableProperty]
    private string _feeInput = "180.00";

    [ObservableProperty]
    private TreatmentStage _selectedStage = TreatmentStage.Proposed;

    [ObservableProperty]
    private string _doctorName = "Dr. Aina Rahman";

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string _statusMessage = "Select any FDI tooth (11-48) to chart condition and treatment stage.";

    public OdontogramViewModel(
        PatientDto patient,
        string activeBranchId,
        OcclusaApiClient apiClient,
        Action? onBillingUpdated = null)
    {
        _patient = patient;
        _activeBranchId = activeBranchId;
        _apiClient = apiClient;
        _onBillingUpdated = onBillingUpdated;

        UpperRightTeeth = BuildQuad(UpperRightNumbers);
        UpperLeftTeeth = BuildQuad(UpperLeftNumbers);
        LowerRightTeeth = BuildQuad(LowerRightNumbers);
        LowerLeftTeeth = BuildQuad(LowerLeftNumbers);

        Conditions = Enum.GetValues<ToothCondition>();
        ProcedurePresets = new ObservableCollection<string>
        {
            "Composite Filling",
            "Scaling & Polishing",
            "Root Canal Therapy",
            "Porcelain Crown",
            "Simple Extraction",
            "Surgical Extraction",
            "Dental Implant"
        };

        SelectTooth(16);
    }

    public ObservableCollection<ToothCellViewModel> UpperRightTeeth { get; }
    public ObservableCollection<ToothCellViewModel> UpperLeftTeeth { get; }
    public ObservableCollection<ToothCellViewModel> LowerRightTeeth { get; }
    public ObservableCollection<ToothCellViewModel> LowerLeftTeeth { get; }
    public ToothCondition[] Conditions { get; }
    public ObservableCollection<string> ProcedurePresets { get; }

    public string ModalTitle => $"32-Tooth FDI Odontogram — {Patient.FullName}";
    public string ModalSubtitle => "Proposed → Accepted → Completed (Completed pushes item directly to Reception Billing)";

    public async Task LoadOdontogramAsync()
    {
        try
        {
            var entries = await _apiClient.GetOdontogramAsync(Patient.Id);
            foreach (var entry in entries)
            {
                if (_toothLookup.TryGetValue(entry.ToothNumber, out var cell))
                {
                    cell.Condition = entry.Condition;
                    cell.ProcedureName = entry.ProcedureName;
                    cell.Fee = entry.Fee;
                    cell.Stage = entry.Stage;
                    cell.DoctorName = string.IsNullOrWhiteSpace(entry.DoctorName) ? "Dr. Aina Rahman" : entry.DoctorName;
                }
            }

            SelectTooth(SelectedToothNumber);
            StatusMessage = $"Loaded odontogram ({entries.Count} charted teeth).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load odontogram: {ex.Message}";
        }
    }

    partial void OnSelectedConditionChanged(ToothCondition value) => MarkDirty();
    partial void OnProcedureNameChanged(string value) => MarkDirty();
    partial void OnFeeInputChanged(string value) => MarkDirty();
    partial void OnSelectedStageChanged(TreatmentStage value) => MarkDirty();
    partial void OnDoctorNameChanged(string value) => MarkDirty();

    [RelayCommand]
    private void SelectTooth(int toothNumber)
    {
        foreach (var cell in _toothLookup.Values)
        {
            cell.IsSelected = cell.ToothNumber == toothNumber;
        }

        SelectedToothNumber = toothNumber;
        if (_toothLookup.TryGetValue(toothNumber, out var selected))
        {
            _suppressDirty = true;
            SelectedCondition = selected.Condition;
            ProcedureName = selected.ProcedureName;
            FeeInput = (selected.Fee > 0 ? selected.Fee : 180m).ToString("0.00", CultureInfo.InvariantCulture);
            SelectedStage = selected.Stage;
            DoctorName = string.IsNullOrWhiteSpace(selected.DoctorName) ? "Dr. Aina Rahman" : selected.DoctorName;
            _suppressDirty = false;
            HasUnsavedChanges = false;
        }
    }

    [RelayCommand]
    private void ApplyProcedurePreset(string preset)
    {
        ProcedureName = preset;
        FeeInput = preset switch
        {
            "Scaling & Polishing" => "150.00",
            "Composite Filling" => "180.00",
            "Simple Extraction" => "180.00",
            "Surgical Extraction" => "450.00",
            "Root Canal Therapy" => "850.00",
            "Porcelain Crown" => "1200.00",
            "Dental Implant" => "4500.00",
            _ => FeeInput
        };
    }

    [RelayCommand]
    private async Task StageAndSaveAsync(string stageName)
    {
        if (!Enum.TryParse<TreatmentStage>(stageName, out var targetStage))
        {
            return;
        }

        SelectedStage = targetStage;
        await SaveCurrentToothAsync();
    }

    [RelayCommand]
    public async Task SaveCurrentToothAsync()
    {
        if (!decimal.TryParse(FeeInput, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal fee) || fee < 0m)
        {
            StatusMessage = "Enter a valid non-negative RM fee.";
            return;
        }

        string effectiveProcedure = string.IsNullOrWhiteSpace(ProcedureName)
            ? $"{SelectedCondition} Assessment"
            : ProcedureName.Trim();

        try
        {
            var saved = await _apiClient.UpdateOdontogramToothAsync(Patient.Id, new UpdateOdontogramRequest
            {
                ToothNumber = SelectedToothNumber,
                Condition = SelectedCondition,
                ProcedureName = effectiveProcedure,
                Fee = fee,
                Stage = SelectedStage,
                DoctorName = string.IsNullOrWhiteSpace(DoctorName) ? "Dr. Aina Rahman" : DoctorName.Trim(),
                BranchId = _activeBranchId
            });

            if (_toothLookup.TryGetValue(SelectedToothNumber, out var cell))
            {
                cell.Condition = saved.Condition;
                cell.ProcedureName = saved.ProcedureName;
                cell.Fee = saved.Fee;
                cell.Stage = saved.Stage;
                cell.DoctorName = saved.DoctorName;
            }

            HasUnsavedChanges = false;

            if (saved.Stage == TreatmentStage.Completed)
            {
                StatusMessage = $"Tooth #{saved.ToothNumber} marked COMPLETED — pushed '{saved.ProcedureName}' (RM {saved.Fee:N2}) to Reception Billing!";
                _onBillingUpdated?.Invoke();
            }
            else
            {
                StatusMessage = $"Saved Tooth #{saved.ToothNumber} ({saved.Condition}, {saved.Stage}).";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Odontogram save failed: {ex.Message}";
        }
    }

    private ObservableCollection<ToothCellViewModel> BuildQuad(int[] numbers)
    {
        var list = new ObservableCollection<ToothCellViewModel>();
        foreach (int num in numbers)
        {
            var cell = new ToothCellViewModel(num);
            _toothLookup[num] = cell;
            list.Add(cell);
        }
        return list;
    }

    private void MarkDirty()
    {
        if (!_suppressDirty)
        {
            HasUnsavedChanges = true;
        }
    }
}
