using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Occlusa.Desktop.Services;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.ViewModels;

/// <summary>
/// Scope option for Doctor Invoice Preferences (Clinic Default vs Individual Dentist).
/// </summary>
public sealed record DoctorScopeOptionVm(Guid? DentistId, string DisplayName);

/// <summary>
/// Display option for DoctorInvoiceInitMode ComboBox.
/// </summary>
public sealed record InvoiceInitModeOptionVm(DoctorInvoiceInitMode Mode, string Label);

/// <summary>
/// ViewModel for the Billing &amp; Doctor Suggestion Settings workspace (6th sidebar tab),
/// managing:
/// 1. Master Billing Catalog (Procedures, X-Rays, Medications, OTC Products, Prices, Note Keywords),
/// 2. Per-Doctor Invoice Checkout Mode &amp; Treatment -> Companion Item Suggestion Rules.
/// </summary>
public partial class BillingSettingsViewModel : ObservableObject
{
    private readonly OcclusaApiClient _apiClient;
    private List<DentistDto> _dentists = new();
    private BillingSettingsSnapshotDto _snapshot = new();
    private bool _suppressPrefDirty;

    public event EventHandler<BillingCatalogItemDto?>? OpenCatalogItemModalRequested;
    public event EventHandler<(BillingCatalogItemDto ProcedureItem, Guid? DentistId, string DoctorName)>? OpenTreatmentRuleModalRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCatalogSubTab))]
    [NotifyPropertyChangedFor(nameof(IsDoctorRulesSubTab))]
    private int _subTabIndex;

    [ObservableProperty]
    private string _selectedCategoryFilter = "All Items";

    [ObservableProperty]
    private string _catalogSearchText = string.Empty;

    [ObservableProperty]
    private BillingCatalogRowItem? _selectedCatalogRow;

    [ObservableProperty]
    private DoctorScopeOptionVm? _selectedDoctorScope;

    [ObservableProperty]
    private InvoiceInitModeOptionVm? _selectedInitModeOption;

    [ObservableProperty]
    private bool _enableNoteKeywordEngine = true;

    [ObservableProperty]
    private DoctorTreatmentRuleRowItem? _selectedRuleRow;

    [ObservableProperty]
    private string _statusMessage = "Single Master Catalogue powering Appointments, Odontogram Presets, Billing, WhatsApp AI & Doctor Suggestions.";

    public BillingSettingsViewModel(OcclusaApiClient apiClient)
    {
        _apiClient = apiClient;
        CatalogRows = new ObservableCollection<BillingCatalogRowItem>();
        RuleRows = new ObservableCollection<DoctorTreatmentRuleRowItem>();
        DoctorScopes = new ObservableCollection<DoctorScopeOptionVm>
        {
            new(null, "Clinic Default (All Doctors)")
        };

        CategoryFilterOptions =
        [
            "All Items",
            "Bookable Appointments",
            "Procedures",
            "Radiographs (X-Ray)",
            "Medications",
            "OTC / Retail (0% Comm)"
        ];

        InitModeOptions =
        [
            new(DoctorInvoiceInitMode.AutoPopulateWithSuggestions, "Auto-Populate + Suggestions"),
            new(DoctorInvoiceInitMode.BlankWithSuggestions, "Blank Invoice + Suggestions"),
            new(DoctorInvoiceInitMode.BlankManualOnly, "Blank Invoice (Manual Only)")
        ];

        _selectedDoctorScope = DoctorScopes[0];
        _selectedInitModeOption = InitModeOptions[0];
    }

    public ObservableCollection<BillingCatalogRowItem> CatalogRows { get; }
    public ObservableCollection<DoctorTreatmentRuleRowItem> RuleRows { get; }
    public ObservableCollection<DoctorScopeOptionVm> DoctorScopes { get; }
    public string[] CategoryFilterOptions { get; }
    public InvoiceInitModeOptionVm[] InitModeOptions { get; }

    public bool IsCatalogSubTab => SubTabIndex == 0;
    public bool IsDoctorRulesSubTab => SubTabIndex == 1;
    public int CatalogCount => _snapshot.CatalogItems.Count;
    public BillingSettingsSnapshotDto CurrentSettings => _snapshot;

    partial void OnSelectedCategoryFilterChanged(string value) => RebuildCatalogRows();
    partial void OnCatalogSearchTextChanged(string value) => RebuildCatalogRows();

    partial void OnSelectedDoctorScopeChanged(DoctorScopeOptionVm? value)
    {
        HydrateSelectedDoctorScope();
        RebuildRuleRows();
    }

    public void ApplySnapshot(
        BillingSettingsSnapshotDto settings,
        IReadOnlyList<DentistDto> dentists)
    {
        _snapshot = settings ?? new BillingSettingsSnapshotDto();
        _dentists = dentists.ToList();

        var prevDentistId = SelectedDoctorScope?.DentistId;
        DoctorScopes.Clear();
        DoctorScopes.Add(new DoctorScopeOptionVm(null, "Clinic Default (All Doctors)"));
        foreach (var doc in _dentists)
        {
            DoctorScopes.Add(new DoctorScopeOptionVm(doc.Id, doc.FullName));
        }

        SelectedDoctorScope = DoctorScopes.FirstOrDefault(s => s.DentistId == prevDentistId) ?? DoctorScopes[0];
        HydrateSelectedDoctorScope();
        RebuildCatalogRows();
        RebuildRuleRows();
        OnPropertyChanged(nameof(CatalogCount));
    }

    [RelayCommand]
    private void SetSubTab(string indexText)
    {
        SubTabIndex = indexText == "1" ? 1 : 0;
    }

    [RelayCommand]
    private void RequestNewCatalogItem() =>
        OpenCatalogItemModalRequested?.Invoke(this, null);

    [RelayCommand]
    private void RequestEditSelectedCatalogItem()
    {
        if (SelectedCatalogRow is not null)
        {
            OpenCatalogItemModalRequested?.Invoke(this, SelectedCatalogRow.Source);
        }
    }

    [RelayCommand]
    private void RequestEditSelectedRule()
    {
        if (SelectedRuleRow is null || SelectedDoctorScope is null)
        {
            return;
        }

        OpenTreatmentRuleModalRequested?.Invoke(
            this,
            (SelectedRuleRow.ProcedureItem, SelectedDoctorScope.DentistId, SelectedDoctorScope.DisplayName));
    }

    [RelayCommand]
    public async Task SaveCurrentDoctorModeAsync(string actorName = "Clinic Admin")
    {
        if (_suppressPrefDirty || SelectedDoctorScope is null || SelectedInitModeOption is null)
        {
            return;
        }

        var existingPref = ResolveEditablePreference(SelectedDoctorScope.DentistId, SelectedDoctorScope.DisplayName);
        try
        {
            var saved = await _apiClient.SaveDoctorInvoicePreferenceAsync(new SaveDoctorInvoicePreferenceRequest
            {
                DentistId = SelectedDoctorScope.DentistId,
                DoctorName = SelectedDoctorScope.DisplayName,
                InitMode = SelectedInitModeOption.Mode,
                EnableNoteKeywordEngine = EnableNoteKeywordEngine,
                TreatmentRules = existingPref.TreatmentRules,
                UpdatedBy = actorName
            });

            MergeSavedPreference(saved);
            RebuildRuleRows();
            StatusMessage = $"Saved invoice preferences for {saved.DoctorName} ({SelectedInitModeOption.Label}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save doctor preference: {ex.Message}";
        }
    }

    public void MergeSavedCatalogItem(BillingCatalogItemDto item)
    {
        int idx = _snapshot.CatalogItems.FindIndex(c => c.Id == item.Id);
        if (idx >= 0) _snapshot.CatalogItems[idx] = item;
        else _snapshot.CatalogItems.Add(item);

        RebuildCatalogRows();
        RebuildRuleRows();
        OnPropertyChanged(nameof(CatalogCount));
        StatusMessage = $"Saved catalog item '{item.Name}' (RM {item.UnitPrice:N2}).";
    }

    public void MergeSavedPreference(DoctorInvoicePreferenceDto pref)
    {
        int idx = _snapshot.DoctorPreferences.FindIndex(p => p.DentistId == pref.DentistId);
        if (idx >= 0) _snapshot.DoctorPreferences[idx] = pref;
        else _snapshot.DoctorPreferences.Add(pref);

        HydrateSelectedDoctorScope();
        RebuildRuleRows();
    }

    public DoctorInvoicePreferenceDto ResolveEditablePreference(Guid? dentistId, string doctorName)
    {
        var found = _snapshot.DoctorPreferences.FirstOrDefault(p => p.DentistId == dentistId);
        if (found is not null)
        {
            return found;
        }

        var clinicDef = _snapshot.DoctorPreferences.FirstOrDefault(p => !p.DentistId.HasValue);
        return new DoctorInvoicePreferenceDto
        {
            DentistId = dentistId,
            DoctorName = doctorName,
            InitMode = clinicDef?.InitMode ?? DoctorInvoiceInitMode.AutoPopulateWithSuggestions,
            EnableNoteKeywordEngine = clinicDef?.EnableNoteKeywordEngine ?? true,
            TreatmentRules = new List<TreatmentSuggestionRuleDto>()
        };
    }

    public DoctorInvoicePreferenceDto GetClinicDefaultPreference() =>
        _snapshot.DoctorPreferences.FirstOrDefault(p => !p.DentistId.HasValue)
        ?? new DoctorInvoicePreferenceDto { DentistId = null, DoctorName = "Clinic Default (All Doctors)" };

    private void HydrateSelectedDoctorScope()
    {
        if (SelectedDoctorScope is null)
        {
            return;
        }

        _suppressPrefDirty = true;
        var pref = ResolveEditablePreference(SelectedDoctorScope.DentistId, SelectedDoctorScope.DisplayName);
        SelectedInitModeOption = InitModeOptions.FirstOrDefault(o => o.Mode == pref.InitMode) ?? InitModeOptions[0];
        EnableNoteKeywordEngine = pref.EnableNoteKeywordEngine;
        _suppressPrefDirty = false;
    }

    private void RebuildCatalogRows()
    {
        CatalogRows.Clear();
        IEnumerable<BillingCatalogItemDto> query = _snapshot.CatalogItems;

        query = SelectedCategoryFilter switch
        {
            "Bookable Appointments" => query.Where(c => c.IsBookableAppointment),
            "Procedures" => query.Where(c => c.Category == BillingCatalogCategory.Procedure),
            "Radiographs (X-Ray)" => query.Where(c => c.Category == BillingCatalogCategory.Radiograph),
            "Medications" => query.Where(c => c.Category == BillingCatalogCategory.Medication),
            "OTC / Retail (0% Comm)" => query.Where(c => c.Category == BillingCatalogCategory.OtcProduct),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(CatalogSearchText))
        {
            var term = CatalogSearchText.Trim();
            query = query.Where(c =>
                c.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || c.ClinicalSubcategory.Contains(term, StringComparison.OrdinalIgnoreCase)
                || c.NoteKeywords.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in query.OrderBy(c => c.Category).ThenBy(c => c.Name))
        {
            CatalogRows.Add(new BillingCatalogRowItem(item));
        }
    }

    private void RebuildRuleRows()
    {
        RuleRows.Clear();
        if (SelectedDoctorScope is null)
        {
            return;
        }

        var clinicDefault = GetClinicDefaultPreference();
        var doctorPref = SelectedDoctorScope.DentistId.HasValue
            ? _snapshot.DoctorPreferences.FirstOrDefault(p => p.DentistId == SelectedDoctorScope.DentistId.Value)
            : null;
        var catalogById = _snapshot.CatalogItems.ToDictionary(c => c.Id);

        foreach (var procedure in _snapshot.CatalogItems
            .Where(c => c.IsActive && c.Category == BillingCatalogCategory.Procedure)
            .OrderBy(c => c.ClinicalSubcategory)
            .ThenBy(c => c.Name))
        {
            var (rule, isOverride) = SelectedDoctorScope.DentistId.HasValue
                ? InvoiceSuggestionEngine.ResolveRuleWithSource(procedure.Id, doctorPref, clinicDefault)
                : (clinicDefault.TreatmentRules.FirstOrDefault(r => r.TreatmentId == procedure.Id), false);

            var items = new List<BillingCatalogItemDto>();
            if (rule is not null)
            {
                foreach (var cid in rule.SuggestedCatalogItemIds)
                {
                    if (catalogById.TryGetValue(cid, out var cItem))
                    {
                        items.Add(cItem);
                    }
                }
            }

            RuleRows.Add(new DoctorTreatmentRuleRowItem(
                procedure,
                items,
                SelectedDoctorScope.DentistId.HasValue,
                isOverride));
        }
    }
}

