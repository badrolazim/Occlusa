using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Occlusa.Desktop.Services;
using Occlusa.Shared.Models;

namespace Occlusa.Desktop.ViewModels;

/// <summary>
/// ViewModel for BillingCatalogItemModal managing the single unified Master Catalogue item
/// (price, OTC commission guard, Appointment booking toggle/duration, Odontogram preset toggle/condition,
/// SOAP note keywords, and medical alert safety tags).
/// </summary>
public partial class BillingCatalogItemViewModel : ObservableObject
{
    private readonly OcclusaApiClient _apiClient;
    private readonly Guid? _existingId;
    private readonly string _activeStaffName;
    private readonly Action<BillingCatalogItemDto>? _onSaved;
    private bool _suppressDirty;

    public event EventHandler? CloseRequested;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleCommission))]
    [NotifyPropertyChangedFor(nameof(CanBeBookable))]
    [NotifyPropertyChangedFor(nameof(CanShowInOdontogram))]
    [NotifyPropertyChangedFor(nameof(CommissionHintText))]
    [NotifyPropertyChangedFor(nameof(BadgeText))]
    private BillingCatalogCategory _category = BillingCatalogCategory.Procedure;

    [ObservableProperty]
    private string _clinicalSubcategory = "General";

    [ObservableProperty]
    private string _unitPriceInput = "50.00";

    [ObservableProperty]
    private bool _isCommissionable = true;

    [ObservableProperty]
    private bool _isBookableAppointment;

    [ObservableProperty]
    private int _defaultDurationMinutes = 30;

    [ObservableProperty]
    private bool _showInOdontogramPresets;

    [ObservableProperty]
    private ToothCondition _defaultToothCondition = ToothCondition.Healthy;

    [ObservableProperty]
    private string _noteKeywords = string.Empty;

    [ObservableProperty]
    private string _safetyAlertTags = string.Empty;

    [ObservableProperty]
    private bool _isActive = true;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string _statusMessage = "Changes sync across Appointments, Odontogram Quick Presets, Invoices, and WhatsApp AI quotes.";

    public BillingCatalogItemViewModel(
        BillingCatalogItemDto? existing,
        OcclusaApiClient apiClient,
        string activeStaffName = "Clinic Admin",
        Action<BillingCatalogItemDto>? onSaved = null)
    {
        _existingId = existing?.Id;
        _apiClient = apiClient;
        _activeStaffName = activeStaffName;
        _onSaved = onSaved;

        Categories = Enum.GetValues<BillingCatalogCategory>();
        ToothConditions = Enum.GetValues<ToothCondition>();
        DurationOptions = [15, 30, 45, 60, 90, 120];
        SubcategoryOptions = new ObservableCollection<string>
        {
            "Diagnostic",
            "Preventive",
            "Restorative",
            "Oral Surgery",
            "Endodontics",
            "Prosthodontics",
            "Radiology",
            "Pharmacy",
            "Oral Care Retail",
            "General"
        };

        _suppressDirty = true;
        if (existing is not null)
        {
            Name = existing.Name;
            Category = existing.Category;
            ClinicalSubcategory = string.IsNullOrWhiteSpace(existing.ClinicalSubcategory)
                ? DefaultSubcategoryFor(existing.Category)
                : existing.ClinicalSubcategory;
            if (!SubcategoryOptions.Contains(ClinicalSubcategory))
            {
                SubcategoryOptions.Add(ClinicalSubcategory);
            }

            UnitPriceInput = existing.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture);
            IsCommissionable = existing.Category != BillingCatalogCategory.OtcProduct && existing.IsCommissionable;
            IsBookableAppointment = CanBeBookable && existing.IsBookableAppointment;
            DefaultDurationMinutes = DurationOptions.Contains(existing.DefaultDurationMinutes)
                ? existing.DefaultDurationMinutes
                : 30;
            ShowInOdontogramPresets = CanShowInOdontogram && existing.ShowInOdontogramPresets;
            DefaultToothCondition = existing.DefaultToothCondition;
            NoteKeywords = existing.NoteKeywords;
            SafetyAlertTags = existing.SafetyAlertTags;
            IsActive = existing.IsActive;
        }
        else
        {
            ClinicalSubcategory = "General";
            IsBookableAppointment = true;
            ShowInOdontogramPresets = false;
        }
        _suppressDirty = false;
    }

    public BillingCatalogCategory[] Categories { get; }
    public ToothCondition[] ToothConditions { get; }
    public int[] DurationOptions { get; }
    public ObservableCollection<string> SubcategoryOptions { get; }

    public string ModalTitle => _existingId.HasValue ? "Edit Master Catalogue Item" : "New Master Catalogue Item";
    public string ModalSubtitle => "Single Source of Truth · Appointments · Odontogram Presets · Billing & Note Keywords";
    public string BadgeText => Category == BillingCatalogCategory.OtcProduct ? "OTC · 0% COMM" : Category.ToString().ToUpperInvariant();
    public bool CanToggleCommission => Category != BillingCatalogCategory.OtcProduct;
    public bool CanBeBookable => Category is BillingCatalogCategory.Procedure or BillingCatalogCategory.Radiograph;
    public bool CanShowInOdontogram => Category == BillingCatalogCategory.Procedure;
    public string CommissionHintText => Category == BillingCatalogCategory.OtcProduct
        ? "Over-The-Counter (OTC) products strictly enforce 0% doctor commission."
        : "Enable if doctors earn commission (40% default) when this item is billed.";

    partial void OnNameChanged(string value) => MarkDirty();

    partial void OnCategoryChanged(BillingCatalogCategory value)
    {
        if (value == BillingCatalogCategory.OtcProduct)
        {
            IsCommissionable = false;
            IsBookableAppointment = false;
            ShowInOdontogramPresets = false;
        }
        else if (value == BillingCatalogCategory.Medication)
        {
            IsBookableAppointment = false;
            ShowInOdontogramPresets = false;
        }
        else if (value == BillingCatalogCategory.Radiograph)
        {
            ShowInOdontogramPresets = false;
        }
        else if (value == BillingCatalogCategory.Procedure && !_existingId.HasValue)
        {
            IsCommissionable = true;
        }

        if (!_suppressDirty && !_existingId.HasValue)
        {
            ClinicalSubcategory = DefaultSubcategoryFor(value);
        }

        MarkDirty();
    }

    partial void OnClinicalSubcategoryChanged(string value) => MarkDirty();
    partial void OnUnitPriceInputChanged(string value) => MarkDirty();
    partial void OnIsCommissionableChanged(bool value) => MarkDirty();
    partial void OnIsBookableAppointmentChanged(bool value) => MarkDirty();
    partial void OnDefaultDurationMinutesChanged(int value) => MarkDirty();
    partial void OnShowInOdontogramPresetsChanged(bool value) => MarkDirty();
    partial void OnDefaultToothConditionChanged(ToothCondition value) => MarkDirty();
    partial void OnNoteKeywordsChanged(string value) => MarkDirty();
    partial void OnSafetyAlertTagsChanged(string value) => MarkDirty();
    partial void OnIsActiveChanged(bool value) => MarkDirty();

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            StatusMessage = "Master Catalogue item name is required.";
            return;
        }

        if (!decimal.TryParse(UnitPriceInput, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal price) || price < 0m)
        {
            StatusMessage = "Enter a valid non-negative RM unit price.";
            return;
        }

        try
        {
            var saved = await _apiClient.UpsertBillingCatalogItemAsync(new UpsertBillingCatalogItemRequest
            {
                Id = _existingId,
                Name = Name.Trim(),
                Category = Category,
                ClinicalSubcategory = string.IsNullOrWhiteSpace(ClinicalSubcategory)
                    ? DefaultSubcategoryFor(Category)
                    : ClinicalSubcategory.Trim(),
                UnitPrice = price,
                IsCommissionable = Category != BillingCatalogCategory.OtcProduct && IsCommissionable,
                IsBookableAppointment = CanBeBookable && IsBookableAppointment,
                DefaultDurationMinutes = DefaultDurationMinutes,
                ShowInOdontogramPresets = CanShowInOdontogram && ShowInOdontogramPresets,
                DefaultToothCondition = DefaultToothCondition,
                NoteKeywords = NoteKeywords.Trim(),
                SafetyAlertTags = SafetyAlertTags.Trim(),
                IsActive = IsActive,
                UpdatedBy = _activeStaffName
            });

            HasUnsavedChanges = false;
            _onSaved?.Invoke(saved);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    private static string DefaultSubcategoryFor(BillingCatalogCategory category) => category switch
    {
        BillingCatalogCategory.Radiograph => "Radiology",
        BillingCatalogCategory.Medication => "Pharmacy",
        BillingCatalogCategory.OtcProduct => "Oral Care Retail",
        _ => "General"
    };

    private void MarkDirty()
    {
        if (!_suppressDirty)
        {
            HasUnsavedChanges = true;
        }
    }
}
