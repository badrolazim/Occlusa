using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Occlusa.Desktop.Services;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.ViewModels;

/// <summary>
///Selectable row for TreatmentSuggestionRuleModal's 4-column UniformPagedGrid.
/// </summary>
public sealed partial class SelectableCompanionItemRowVm : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDisplay))]
    private bool _isIncluded;

    public SelectableCompanionItemRowVm(BillingCatalogItemDto item, bool isIncluded)
    {
        Item = item;
        _isIncluded = isIncluded;
        Name = item.Name;
        CategoryDisplay = item.Category switch
        {
            BillingCatalogCategory.OtcProduct => "OTC (0% Comm)",
            BillingCatalogCategory.Medication => "Medication",
            BillingCatalogCategory.Radiograph => "Radiograph",
            _ => "Procedure"
        };
        PriceDisplay = $"RM {item.UnitPrice:N2}";
    }

    public BillingCatalogItemDto Item { get; }
    public string Name { get; }
    public string CategoryDisplay { get; }
    public string PriceDisplay { get; }
    public string SelectedDisplay => IsIncluded ? "✓ Suggested" : "—";
}

/// <summary>
/// ViewModel for TreatmentSuggestionRuleModal allowing clinic admins or dentists to configure
/// which companion invoice items are suggested for a specific Treatment (globally or per-doctor).
/// </summary>
public partial class TreatmentSuggestionRuleViewModel : ObservableObject
{
    private readonly BillingCatalogItemDto _primaryProcedure;
    private readonly Guid? _dentistId;
    private readonly string _doctorName;
    private readonly DoctorInvoicePreferenceDto _editablePreference;
    private readonly DoctorInvoicePreferenceDto _clinicDefaultPreference;
    private readonly OcclusaApiClient _apiClient;
    private readonly string _activeStaffName;
    private readonly Action<DoctorInvoicePreferenceDto>? _onSaved;

    public event EventHandler? CloseRequested;

    [ObservableProperty]
    private SelectableCompanionItemRowVm? _selectedRow;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string _statusMessage = "Double-click any row (or click Toggle Selected) to include/exclude it from invoice suggestions.";

    public TreatmentSuggestionRuleViewModel(
        BillingCatalogItemDto primaryProcedure,
        Guid? dentistId,
        string doctorName,
        DoctorInvoicePreferenceDto editablePreference,
        DoctorInvoicePreferenceDto clinicDefaultPreference,
        IReadOnlyList<BillingCatalogItemDto> catalogItems,
        OcclusaApiClient apiClient,
        string activeStaffName = "Clinic Admin",
        Action<DoctorInvoicePreferenceDto>? onSaved = null)
    {
        _primaryProcedure = primaryProcedure;
        _dentistId = dentistId;
        _doctorName = doctorName;
        _editablePreference = editablePreference;
        _clinicDefaultPreference = clinicDefaultPreference;
        _apiClient = apiClient;
        _activeStaffName = activeStaffName;
        _onSaved = onSaved;

        var (effectiveRule, _) = dentistId.HasValue
            ? InvoiceSuggestionEngine.ResolveRuleWithSource(primaryProcedure.Id, editablePreference, clinicDefaultPreference)
            : (clinicDefaultPreference.TreatmentRules.FirstOrDefault(r => r.TreatmentId == primaryProcedure.Id), false);

        var activeIds = effectiveRule?.SuggestedCatalogItemIds.ToHashSet() ?? new HashSet<Guid>();

        CandidateRows = new ObservableCollection<SelectableCompanionItemRowVm>(
            catalogItems
                .Where(c => c.IsActive && c.Id != primaryProcedure.Id)
                .OrderByDescending(c => activeIds.Contains(c.Id))
                .ThenBy(c => c.Category)
                .ThenBy(c => c.Name)
                .Select(c => new SelectableCompanionItemRowVm(c, activeIds.Contains(c.Id))));
    }

    public ObservableCollection<SelectableCompanionItemRowVm> CandidateRows { get; }

    public bool IsDoctorSpecific => _dentistId.HasValue;
    public string ModalTitle => $"Invoice Add-On Suggestions — {_primaryProcedure.Name}";
    public string ModalSubtitle => $"Scope: {_doctorName} · Subcategory: {_primaryProcedure.ClinicalSubcategory} · RM {_primaryProcedure.UnitPrice:N2}";
    public string BadgeText => IsDoctorSpecific ? "DOCTOR SCOPE" : "CLINIC DEFAULT";

    [RelayCommand]
    public void ToggleRow(SelectableCompanionItemRowVm? row)
    {
        var target = row ?? SelectedRow;
        if (target is null)
        {
            return;
        }

        target.IsIncluded = !target.IsIncluded;
        int idx = CandidateRows.IndexOf(target);
        if (idx >= 0)
        {
            // Trigger UniformPagedGrid row rebind cleanly
            CandidateRows[idx] = new SelectableCompanionItemRowVm(target.Item, target.IsIncluded);
            SelectedRow = CandidateRows[idx];
        }

        HasUnsavedChanges = true;
        int total = CandidateRows.Count(r => r.IsIncluded);
        StatusMessage = $"{total} companion item(s) selected for {_primaryProcedure.Name}.";
    }

    [RelayCommand]
    private void ClearAllSelections()
    {
        for (int i = 0; i < CandidateRows.Count; i++)
        {
            if (CandidateRows[i].IsIncluded)
            {
                CandidateRows[i] = new SelectableCompanionItemRowVm(CandidateRows[i].Item, false);
            }
        }

        HasUnsavedChanges = true;
        StatusMessage = $"Cleared all companion suggestions for {_primaryProcedure.Name} (will suggest 0 add-ons).";
    }

    [RelayCommand]
    public async Task ResetToClinicDefaultAsync()
    {
        if (!_dentistId.HasValue)
        {
            return;
        }

        var updatedRules = _editablePreference.TreatmentRules
            .Where(r => r.TreatmentId != _primaryProcedure.Id)
            .ToList();

        await PersistRulesAsync(updatedRules, $"Reverted {_primaryProcedure.Name} to inherit Clinic Default suggestions.");
    }

    [RelayCommand]
    public async Task SaveRuleAsync()
    {
        var selectedIds = CandidateRows
            .Where(r => r.IsIncluded)
            .Select(r => r.Item.Id)
            .ToList();

        var updatedRules = _editablePreference.TreatmentRules
            .Where(r => r.TreatmentId != _primaryProcedure.Id)
            .ToList();

        updatedRules.Add(new TreatmentSuggestionRuleDto
        {
            TreatmentId = _primaryProcedure.Id,
            TreatmentName = _primaryProcedure.Name,
            SuggestedCatalogItemIds = selectedIds
        });

        await PersistRulesAsync(updatedRules, $"Saved {selectedIds.Count} suggested item(s) for {_primaryProcedure.Name}.");
    }

    private async Task PersistRulesAsync(List<TreatmentSuggestionRuleDto> rules, string successMsg)
    {
        try
        {
            var saved = await _apiClient.SaveDoctorInvoicePreferenceAsync(new SaveDoctorInvoicePreferenceRequest
            {
                DentistId = _dentistId,
                DoctorName = _doctorName,
                InitMode = _editablePreference.InitMode,
                EnableNoteKeywordEngine = _editablePreference.EnableNoteKeywordEngine,
                TreatmentRules = rules,
                UpdatedBy = _activeStaffName
            });

            HasUnsavedChanges = false;
            StatusMessage = successMsg;
            _onSaved?.Invoke(saved);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }
}

