using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Occlusa.Desktop.Services;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.ViewModels;

/// <summary>
/// Display item for the catalog ComboBox inside InvoiceCheckoutModal.
/// </summary>
public sealed class CatalogPickerOptionVm
{
    public CatalogPickerOptionVm(BillingCatalogItemDto source)
    {
        Source = source;
        string prefix = source.Category switch
        {
            BillingCatalogCategory.OtcProduct => "[OTC · 0% Comm]",
            BillingCatalogCategory.Medication => "[Medication]",
            BillingCatalogCategory.Radiograph => "[X-Ray]",
            _ => "[Procedure]"
        };
        DisplayLabel = $"{prefix} {source.Name} — RM {source.UnitPrice:N2}";
    }

    public BillingCatalogItemDto Source { get; }
    public string DisplayLabel { get; }
}

/// <summary>
/// Clickable 1-click companion or note-detected suggestion chip inside InvoiceCheckoutModal.
/// </summary>
public sealed class SuggestedInvoiceChipVm
{
    public SuggestedInvoiceChipVm(InvoiceSuggestionItemDto source)
    {
        Source = source;
        Label = source.HasSafetyWarning
            ? $"+ {source.Name} · RM {source.UnitPrice:N2} [⚠]"
            : $"+ {source.Name} · RM {source.UnitPrice:N2}";
        ToolTipText = source.HasSafetyWarning
            ? $"{source.Name} (RM {source.UnitPrice:N2})\nSource: {source.SourceReason}\nCLINICAL WARNING: {source.SafetyWarning}"
            : $"{source.Name} (RM {source.UnitPrice:N2})\nSource: {source.SourceReason}";
    }

    public InvoiceSuggestionItemDto Source { get; }
    public string Label { get; }
    public string ToolTipText { get; }
    public bool HasWarning => Source.HasSafetyWarning;
}

/// <summary>
/// Unified ViewModel for InvoiceCheckoutModal supporting:
/// 1. Dentist Checkout Draft Initiation (auto-populated or blank per Doctor Settings + Smart Suggestion Chips),
/// 2. Standalone Front-Desk Counter / Walk-In OTC Retail Sales (toothbrush, toothpaste, mouthwash at 0% commission),
/// 3. Receptionist Line-Item Editing on Unpaid Drafts (strictly decoupled from Doctor's Visit Report),
/// 4. Corporate Panel, Split Payments, Shared Patient Credit Redemption, and Overpayment -> Credit / Cash Change.
/// </summary>
public partial class InvoiceCheckoutViewModel : ObservableObject
{
    private const int MaxSuggestedChips = 6;

    private readonly OcclusaApiClient _apiClient;
    private readonly string _branchId;
    private readonly string _activeStaffName;
    private readonly IReadOnlyList<PatientDto> _allPatients;
    private readonly IReadOnlyList<DentistDto> _dentists;
    private readonly BillingSettingsSnapshotDto _billingSettings;
    private readonly PatientVisitDto? _visit;
    private readonly AppointmentQueueItemDto? _appointment;
    private readonly Action<InvoiceDto>? _onInvoiceChanged;

    private bool _suppressDirty;
    private bool _linesModified;

    public event EventHandler? CloseRequested;

    [ObservableProperty]
    private InvoiceDto _invoice;

    [ObservableProperty]
    private bool _isStandaloneCreateMode;

    [ObservableProperty]
    private bool _isDentistCheckoutMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRegisteredPatientMode))]
    [NotifyPropertyChangedFor(nameof(CanUsePatientCredit))]
    [NotifyPropertyChangedFor(nameof(CanStoreOverpaymentCredit))]
    [NotifyPropertyChangedFor(nameof(AvailableCreditDisplay))]
    private bool _isWalkInRetailMode;

    [ObservableProperty]
    private string _walkInCustomerNameInput = "Walk-In Retail Customer";

    [ObservableProperty]
    private string _patientSearchInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUsePatientCredit))]
    [NotifyPropertyChangedFor(nameof(CanStoreOverpaymentCredit))]
    [NotifyPropertyChangedFor(nameof(AvailableCreditDisplay))]
    private PatientDto? _selectedPatient;

    [ObservableProperty]
    private string _selectedDoctorName = "Dr. Aina Rahman";

    [ObservableProperty]
    private CatalogPickerOptionVm? _selectedCatalogOption;

    [ObservableProperty]
    private InvoiceLineRowItem? _selectedLineRow;

    [ObservableProperty]
    private BillingCatalogCategory _lineCategoryInput = BillingCatalogCategory.Procedure;

    [ObservableProperty]
    private string _lineNameInput = string.Empty;

    [ObservableProperty]
    private string _lineToothInput = string.Empty;

    [ObservableProperty]
    private string _lineQtyInput = "1";

    [ObservableProperty]
    private string _lineUnitPriceInput = "0.00";

    [ObservableProperty]
    private string _discountInput = "0.00";

    [ObservableProperty]
    private string _selectedPanel = "None";

    [ObservableProperty]
    private string _panelCoverageInput = "0.00";

    [ObservableProperty]
    private string _creditRedeemInput = "0.00";

    [ObservableProperty]
    private string _cashInput = "0.00";

    [ObservableProperty]
    private string _cardInput = "0.00";

    [ObservableProperty]
    private string _qrInput = "0.00";

    [ObservableProperty]
    private OverpaymentHandling _selectedOverpaymentHandling = OverpaymentHandling.ReturnCashChange;

    [ObservableProperty]
    private string _referenceNoInput = string.Empty;

    [ObservableProperty]
    private string _voidReasonInput = string.Empty;

    [ObservableProperty]
    private decimal _projectedSubtotal;

    [ObservableProperty]
    private decimal _projectedPayable;

    [ObservableProperty]
    private decimal _projectedBalanceDue;

    [ObservableProperty]
    private decimal _projectedExcessAmount;

    [ObservableProperty]
    private decimal _projectedCommission;

    [ObservableProperty]
    private string _suggestionHeaderText = "Smart Suggestions (Dr's Notes & Settings)";

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isEditable = true;

    [ObservableProperty]
    private bool _canEditDraftLines = true;

    public InvoiceCheckoutViewModel(
        InvoiceDto? existingInvoice,
        OcclusaApiClient apiClient,
        Action<InvoiceDto>? onInvoiceChanged,
        string branchId = "HQ-KL",
        string activeStaffName = "Desktop Cashier",
        IReadOnlyList<PatientDto>? allPatients = null,
        IReadOnlyList<DentistDto>? dentists = null,
        BillingSettingsSnapshotDto? billingSettings = null,
        PatientDto? preselectedPatient = null,
        PatientVisitDto? visit = null,
        AppointmentQueueItemDto? appointment = null,
        bool startAsWalkInRetail = false,
        bool isDentistCheckout = false)
    {
        _apiClient = apiClient;
        _onInvoiceChanged = onInvoiceChanged;
        _branchId = string.IsNullOrWhiteSpace(branchId) ? (existingInvoice?.BranchId ?? "HQ-KL") : branchId;
        _activeStaffName = string.IsNullOrWhiteSpace(activeStaffName) ? "Desktop Cashier" : activeStaffName.Trim();
        _allPatients = allPatients ?? Array.Empty<PatientDto>();
        _dentists = dentists ?? Array.Empty<DentistDto>();
        _billingSettings = billingSettings ?? new BillingSettingsSnapshotDto();
        _visit = visit;
        _appointment = appointment;
        _isDentistCheckoutMode = isDentistCheckout;
        _isStandaloneCreateMode = existingInvoice is null && visit is null && appointment is null;

        LineRows = new ObservableCollection<InvoiceLineRowItem>();
        SuggestedChips = new ObservableCollection<SuggestedInvoiceChipVm>();
        SuggestedPatients = new ObservableCollection<PatientDto>();
        CatalogOptions = new ObservableCollection<CatalogPickerOptionVm>(
            _billingSettings.CatalogItems
                .Where(c => c.IsActive)
                .OrderBy(c => c.Category)
                .ThenBy(c => c.Name)
                .Select(c => new CatalogPickerOptionVm(c)));

        DoctorOptions = new ObservableCollection<string>(
            new[] { "Counter / Retail" }
                .Concat(_dentists.Select(d => d.FullName))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        if (DoctorOptions.Count == 1)
        {
            DoctorOptions.Add("Dr. Aina Rahman");
        }

        AvailableCategories = Enum.GetValues<BillingCatalogCategory>();
        var panelList = new List<string> { "None" };
        if (_billingSettings.CorporatePanels.Count > 0)
        {
            panelList.AddRange(_billingSettings.CorporatePanels
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }
        else
        {
            panelList.AddRange(["AIA Dental Panel", "PMCare", "MiCare", "Mednefits", "CompuMed", "Allianz Corporate"]);
        }
        AvailablePanels = new ObservableCollection<string>(panelList);

        _suppressDirty = true;

        if (existingInvoice is not null)
        {
            _invoice = existingInvoice;
            _isWalkInRetailMode = existingInvoice.IsWalkInRetail || existingInvoice.PatientId == Guid.Empty;
            _walkInCustomerNameInput = existingInvoice.PatientName;
            _selectedPatient = _isWalkInRetailMode
                ? null
                : (_allPatients.FirstOrDefault(p => p.Id == existingInvoice.PatientId) ?? preselectedPatient);
            _selectedDoctorName = string.IsNullOrWhiteSpace(existingInvoice.DoctorName)
                ? (_isWalkInRetailMode ? "Counter / Retail" : "Dr. Aina Rahman")
                : existingInvoice.DoctorName;
            if (!DoctorOptions.Contains(_selectedDoctorName))
            {
                DoctorOptions.Add(_selectedDoctorName);
            }

            foreach (var line in existingInvoice.Lines)
            {
                LineRows.Add(new InvoiceLineRowItem(CloneLine(line)));
            }

            DiscountInput = existingInvoice.DiscountAmount.ToString("0.00", CultureInfo.InvariantCulture);
            SelectedPanel = string.IsNullOrWhiteSpace(existingInvoice.PanelName) ? "None" : existingInvoice.PanelName;
            if (!AvailablePanels.Contains(SelectedPanel))
            {
                AvailablePanels.Add(SelectedPanel);
            }

            PanelCoverageInput = existingInvoice.PanelCoveredAmount.ToString("0.00", CultureInfo.InvariantCulture);
            IsEditable = !existingInvoice.IsVoided && (existingInvoice.BalanceDue > 0m || existingInvoice.Payments.Count == 0);
            CanEditDraftLines = !existingInvoice.IsVoided && existingInvoice.Payments.Count == 0;

            if (CanEditDraftLines && (_visit is not null || _appointment is not null))
            {
                PopulateSuggestionChipsOnly(forceScanNotes: false);
            }

            StatusMessage = CanEditDraftLines
                ? "Draft invoice open — you may edit/add/remove items or proceed to settlement."
                : "Settled / locked invoice — line items are immutable.";
        }
        else
        {
            _isWalkInRetailMode = startAsWalkInRetail;
            _selectedPatient = startAsWalkInRetail
                ? null
                : (preselectedPatient
                    ?? (visit is not null ? _allPatients.FirstOrDefault(p => p.Id == visit.PatientId) : null)
                    ?? (appointment is not null ? _allPatients.FirstOrDefault(p => p.Id == appointment.PatientId) : null));

            if (_selectedPatient is null && !startAsWalkInRetail && visit is not null)
            {
                _selectedPatient = new PatientDto
                {
                    Id = visit.PatientId,
                    FullName = visit.PatientName,
                    IcNumber = visit.PatientIc,
                    PrimaryBranchId = _branchId
                };
            }

            _selectedDoctorName = startAsWalkInRetail
                ? "Counter / Retail"
                : (visit?.DoctorName ?? appointment?.DoctorName ?? _dentists.FirstOrDefault()?.FullName ?? "Dr. Aina Rahman");
            if (!DoctorOptions.Contains(_selectedDoctorName))
            {
                DoctorOptions.Add(_selectedDoctorName);
            }

            _invoice = new InvoiceDto
            {
                Id = Guid.Empty,
                InvoiceNumber = "NEW DRAFT",
                BranchId = _branchId,
                IsWalkInRetail = _isWalkInRetailMode,
                PatientId = _isWalkInRetailMode ? Guid.Empty : (_selectedPatient?.Id ?? Guid.Empty),
                PatientName = _isWalkInRetailMode ? _walkInCustomerNameInput : (_selectedPatient?.FullName ?? "Select Patient"),
                DoctorName = _selectedDoctorName,
                AppointmentId = appointment?.Id ?? visit?.AppointmentId,
                VisitId = visit?.Id
            };

            IsEditable = true;
            CanEditDraftLines = true;

            if (_visit is not null || _appointment is not null)
            {
                ApplyInitialDoctorSuggestions();
            }
            else
            {
                StatusMessage = _isWalkInRetailMode
                    ? "Walk-In OTC Retail mode (0% doctor commission). Pick products below and settle payment."
                    : "Select a patient or switch to Walk-In Retail Customer, then add invoice items.";
            }
        }

        SelectedCatalogOption = CatalogOptions.FirstOrDefault(c =>
            _isWalkInRetailMode ? c.Source.Category == BillingCatalogCategory.OtcProduct : true)
            ?? CatalogOptions.FirstOrDefault();

        RefreshPatientSuggestions();
        _suppressDirty = false;
        RecalculatePreview();
    }

    public ObservableCollection<InvoiceLineRowItem> LineRows { get; }
    public ObservableCollection<SuggestedInvoiceChipVm> SuggestedChips { get; }
    public ObservableCollection<PatientDto> SuggestedPatients { get; }
    public ObservableCollection<CatalogPickerOptionVm> CatalogOptions { get; }
    public ObservableCollection<string> DoctorOptions { get; }
    public BillingCatalogCategory[] AvailableCategories { get; }
    public ObservableCollection<string> AvailablePanels { get; }

    public bool IsRegisteredPatientMode => !IsWalkInRetailMode;
    public bool HasSuggestions => SuggestedChips.Count > 0 && CanEditDraftLines;
    public bool HasVisitContext => _visit is not null || _appointment is not null;
    public bool HasSelectedLine => SelectedLineRow is not null && CanEditDraftLines;
    public bool CanVoidInvoice => Invoice.Id != Guid.Empty && !Invoice.IsVoided;
    public bool HasProjectedExcess => ProjectedExcessAmount > 0m;
    public bool IsReturnCashChangeSelected => SelectedOverpaymentHandling == OverpaymentHandling.ReturnCashChange;
    public bool IsStoreCreditSelected => SelectedOverpaymentHandling == OverpaymentHandling.StoreAsPatientCredit;

    public bool CanUsePatientCredit =>
        !IsWalkInRetailMode && SelectedPatient is { CreditBalance: > 0m } && IsEditable;

    public bool CanStoreOverpaymentCredit =>
        !IsWalkInRetailMode && SelectedPatient is not null && SelectedPatient.Id != Guid.Empty;

    public string AvailableCreditDisplay =>
        IsWalkInRetailMode || SelectedPatient is null
            ? "N/A (Retail)"
            : $"RM {SelectedPatient.CreditBalance:N2}";

    public string SaveDraftButtonText =>
        IsDentistCheckoutMode ? "Send Draft to Counter →" : "Save Draft Invoice";

    public string ModalTitle =>
        Invoice.Id == Guid.Empty
            ? (IsDentistCheckoutMode ? "Dentist Checkout — Draft Invoice" : "New Clinic / Retail Invoice")
            : $"Invoice Checkout — {Invoice.InvoiceNumber}";

    public string ModalSubtitle
    {
        get
        {
            string customer = IsWalkInRetailMode
                ? $"{WalkInCustomerNameInput} (Walk-In OTC)"
                : (SelectedPatient?.FullName ?? Invoice.PatientName);
            return $"{customer} · Clinician: {SelectedDoctorName} · Branch: {_branchId}";
        }
    }

    public string StatusBadgeText => Invoice.IsVoided
        ? "VOIDED"
        : Invoice.Id == Guid.Empty
            ? "UNSAVED DRAFT"
            : (Invoice.Payments.Count > 0 && Invoice.BalanceDue <= 0m)
                ? "SETTLED"
                : "DRAFT / DUE";

    partial void OnIsWalkInRetailModeChanged(bool value)
    {
        if (value)
        {
            SelectedDoctorName = "Counter / Retail";
            CreditRedeemInput = "0.00";
            if (SelectedOverpaymentHandling == OverpaymentHandling.StoreAsPatientCredit)
            {
                SelectedOverpaymentHandling = OverpaymentHandling.ReturnCashChange;
            }
        }
        else if (SelectedDoctorName == "Counter / Retail" && _dentists.Count > 0)
        {
            SelectedDoctorName = _dentists[0].FullName;
        }

        OnPropertyChanged(nameof(ModalSubtitle));
        MarkDirty();
        RecalculatePreview();
    }

    partial void OnWalkInCustomerNameInputChanged(string value)
    {
        OnPropertyChanged(nameof(ModalSubtitle));
        MarkDirty();
    }

    partial void OnPatientSearchInputChanged(string value) => RefreshPatientSuggestions();

    partial void OnSelectedPatientChanged(PatientDto? value)
    {
        OnPropertyChanged(nameof(ModalSubtitle));
        MarkDirty();
        RecalculatePreview();
    }

    partial void OnSelectedDoctorNameChanged(string value)
    {
        OnPropertyChanged(nameof(ModalSubtitle));
        MarkDirty();
        RecalculatePreview();
    }

    partial void OnSelectedCatalogOptionChanged(CatalogPickerOptionVm? value)
    {
        if (value is null)
        {
            return;
        }

        LineCategoryInput = value.Source.Category;
        LineNameInput = value.Source.Name;
        LineUnitPriceInput = value.Source.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(LineQtyInput) || LineQtyInput == "0")
        {
            LineQtyInput = "1";
        }
    }

    partial void OnSelectedLineRowChanged(InvoiceLineRowItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedLine));
        if (value is null)
        {
            return;
        }

        LineCategoryInput = value.Source.Category;
        LineNameInput = value.Source.ProcedureName;
        LineToothInput = value.Source.ToothNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        LineQtyInput = value.Source.Quantity.ToString(CultureInfo.InvariantCulture);
        LineUnitPriceInput = value.Source.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture);
    }

    partial void OnDiscountInputChanged(string value) { MarkDirty(); RecalculatePreview(); }
    partial void OnSelectedPanelChanged(string value) { MarkDirty(); RecalculatePreview(); }
    partial void OnPanelCoverageInputChanged(string value) { MarkDirty(); RecalculatePreview(); }
    partial void OnCreditRedeemInputChanged(string value) { MarkDirty(); RecalculatePreview(); }
    partial void OnCashInputChanged(string value) { MarkDirty(); RecalculatePreview(); }
    partial void OnCardInputChanged(string value) { MarkDirty(); RecalculatePreview(); }
    partial void OnQrInputChanged(string value) { MarkDirty(); RecalculatePreview(); }
    partial void OnVoidReasonInputChanged(string value) => MarkDirty();
    partial void OnSelectedOverpaymentHandlingChanged(OverpaymentHandling value)
    {
        OnPropertyChanged(nameof(IsReturnCashChangeSelected));
        OnPropertyChanged(nameof(IsStoreCreditSelected));
    }

    [RelayCommand]
    private void SetCustomerMode(string mode)
    {
        IsWalkInRetailMode = string.Equals(mode, "Retail", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void ChooseSuggestedPatient(PatientDto? patient)
    {
        if (patient is null)
        {
            return;
        }

        SelectedPatient = patient;
        IsWalkInRetailMode = false;
        StatusMessage = $"Selected patient: {patient.FullName} (Credit Wallet: RM {patient.CreditBalance:N2}).";
    }

    [RelayCommand]
    private void SetOverpaymentMode(string mode)
    {
        SelectedOverpaymentHandling = string.Equals(mode, "Credit", StringComparison.OrdinalIgnoreCase) && CanStoreOverpaymentCredit
            ? OverpaymentHandling.StoreAsPatientCredit
            : OverpaymentHandling.ReturnCashChange;
    }

    [RelayCommand]
    private void AddSuggestedChip(SuggestedInvoiceChipVm? chip)
    {
        if (chip is null || !CanEditDraftLines)
        {
            return;
        }

        if (chip.HasWarning)
        {
            var confirm = MessageBox.Show(
                $"Clinical Safety Check for {SelectedPatient?.FullName ?? "Patient"}:\n\n"
                + $"{chip.Source.SafetyWarning}\n\n"
                + $"Do you still want to add '{chip.Source.Name}' to this invoice?",
                "Medical Alert Warning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var existing = LineRows.FirstOrDefault(r =>
            r.Source.CatalogItemId == chip.Source.CatalogItemId
            && r.Source.ToothNumber == chip.Source.ToothNumber);

        if (existing is not null)
        {
            existing.Source.Quantity += 1;
            existing.Source.LineTotal = BillingCalculator.ComputeLineTotal(existing.Source.UnitPrice, existing.Source.Quantity);
            var idx = LineRows.IndexOf(existing);
            LineRows[idx] = new InvoiceLineRowItem(existing.Source);
        }
        else
        {
            var newLine = new InvoiceLineDto
            {
                Id = Guid.NewGuid(),
                CatalogItemId = chip.Source.CatalogItemId,
                ToothNumber = chip.Source.ToothNumber,
                ProcedureName = chip.Source.Name,
                Category = chip.Source.Category,
                IsCommissionable = chip.Source.Category != BillingCatalogCategory.OtcProduct && chip.Source.IsCommissionable,
                UnitPrice = chip.Source.UnitPrice,
                Quantity = 1,
                LineTotal = chip.Source.UnitPrice
            };
            LineRows.Add(new InvoiceLineRowItem(newLine));
        }

        SuggestedChips.Remove(chip);
        OnPropertyChanged(nameof(HasSuggestions));
        _linesModified = true;
        MarkDirty();
        RecalculatePreview();
        StatusMessage = $"Added '{chip.Source.Name}' to invoice.";
    }

    [RelayCommand]
    private void ScanVisitNotes()
    {
        if (!CanEditDraftLines)
        {
            return;
        }

        PopulateSuggestionChipsOnly(forceScanNotes: true);
        StatusMessage = SuggestedChips.Count > 0
            ? $"Scanned clinical notes & treatment rules — {SuggestedChips.Count} item suggestion(s) ready."
            : "No additional unbilled items detected in clinical notes.";
    }

    [RelayCommand]
    private void AddLineItem()
    {
        if (!CanEditDraftLines)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(LineNameInput))
        {
            StatusMessage = "Enter or select an invoice item name.";
            return;
        }

        if (!TryParseAmount(LineUnitPriceInput, out decimal unitPrice)
            || !int.TryParse(LineQtyInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out int qty)
            || qty <= 0)
        {
            StatusMessage = "Enter a valid non-negative RM price and quantity >= 1.";
            return;
        }

        int? toothNum = null;
        if (!string.IsNullOrWhiteSpace(LineToothInput))
        {
            var digits = new string(LineToothInput.Where(char.IsAsciiDigit).ToArray());
            if (int.TryParse(digits, out int parsedTooth) && OdontogramHelper.IsValidFdiTooth(parsedTooth))
            {
                toothNum = parsedTooth;
            }
            else
            {
                StatusMessage = "Optional Tooth # must be a valid FDI tooth (11-48 or 51-85), or leave blank.";
                return;
            }
        }

        var matchedCatalog = SelectedCatalogOption?.Source.Name.Equals(LineNameInput.Trim(), StringComparison.OrdinalIgnoreCase) == true
            ? SelectedCatalogOption.Source
            : null;

        var category = LineCategoryInput;
        bool isComm = !IsWalkInRetailMode
            && category != BillingCatalogCategory.OtcProduct
            && (matchedCatalog?.IsCommissionable ?? (category is BillingCatalogCategory.Procedure or BillingCatalogCategory.Radiograph));

        var dto = new InvoiceLineDto
        {
            Id = Guid.NewGuid(),
            CatalogItemId = matchedCatalog?.Id,
            ToothNumber = toothNum,
            ProcedureName = LineNameInput.Trim(),
            Category = category,
            IsCommissionable = isComm,
            UnitPrice = BillingCalculator.Round2(unitPrice),
            Quantity = qty,
            LineTotal = BillingCalculator.ComputeLineTotal(unitPrice, qty)
        };

        LineRows.Add(new InvoiceLineRowItem(dto));
        LineToothInput = string.Empty;
        _linesModified = true;
        MarkDirty();
        RecalculatePreview();
        StatusMessage = $"Added '{dto.ProcedureName}' (RM {dto.LineTotal:N2}).";
    }

    [RelayCommand]
    private void UpdateSelectedLine()
    {
        if (!CanEditDraftLines || SelectedLineRow is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(LineNameInput)
            || !TryParseAmount(LineUnitPriceInput, out decimal unitPrice)
            || !int.TryParse(LineQtyInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out int qty)
            || qty <= 0)
        {
            StatusMessage = "Enter a valid item name, RM unit price, and quantity >= 1.";
            return;
        }

        int? toothNum = null;
        if (!string.IsNullOrWhiteSpace(LineToothInput))
        {
            var digits = new string(LineToothInput.Where(char.IsAsciiDigit).ToArray());
            if (int.TryParse(digits, out int parsedTooth) && OdontogramHelper.IsValidFdiTooth(parsedTooth))
            {
                toothNum = parsedTooth;
            }
            else
            {
                StatusMessage = "Optional Tooth # must be a valid FDI tooth (11-48 or 51-85), or leave blank.";
                return;
            }
        }

        var updated = CloneLine(SelectedLineRow.Source);
        updated.ProcedureName = LineNameInput.Trim();
        updated.ToothNumber = toothNum;
        updated.Category = LineCategoryInput;
        if (updated.Category == BillingCatalogCategory.OtcProduct || IsWalkInRetailMode)
        {
            updated.IsCommissionable = false;
        }
        updated.UnitPrice = BillingCalculator.Round2(unitPrice);
        updated.Quantity = qty;
        updated.LineTotal = BillingCalculator.ComputeLineTotal(unitPrice, qty);

        int idx = LineRows.IndexOf(SelectedLineRow);
        if (idx >= 0)
        {
            var replacement = new InvoiceLineRowItem(updated);
            LineRows[idx] = replacement;
            SelectedLineRow = replacement;
        }

        _linesModified = true;
        MarkDirty();
        RecalculatePreview();
        StatusMessage = $"Updated invoice line '{updated.ProcedureName}' → RM {updated.LineTotal:N2} (Visit Report untouched).";
    }

    [RelayCommand]
    private void RemoveSelectedLine()
    {
        if (!CanEditDraftLines || SelectedLineRow is null)
        {
            return;
        }

        var removedName = SelectedLineRow.ProcedureName;
        LineRows.Remove(SelectedLineRow);
        SelectedLineRow = null;
        _linesModified = true;
        MarkDirty();
        RecalculatePreview();
        StatusMessage = $"Removed '{removedName}' from invoice.";
    }

    [RelayCommand]
    private void ClearAllLines()
    {
        if (!CanEditDraftLines || LineRows.Count == 0)
        {
            return;
        }

        LineRows.Clear();
        SelectedLineRow = null;
        _linesModified = true;
        MarkDirty();
        RecalculatePreview();
        StatusMessage = "Cleared invoice to blank — add items manually or click suggestion chips.";
    }

    [RelayCommand]
    private void ApplyMaxCredit()
    {
        if (!CanUsePatientCredit || SelectedPatient is null)
        {
            return;
        }

        RecalculatePreview();
        decimal unpaidPayable = Math.Max(0m, ProjectedPayable - Invoice.PaidAmount);
        decimal usable = Math.Min(SelectedPatient.CreditBalance, unpaidPayable);
        CreditRedeemInput = usable.ToString("0.00", CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private void FillRemainingInCash()
    {
        RecalculatePreview();
        TryParseAmount(CreditRedeemInput, out decimal credit);
        decimal remaining = Math.Max(0m, ProjectedPayable - Invoice.PaidAmount - credit);
        CashInput = remaining.ToString("0.00", CultureInfo.InvariantCulture);
        CardInput = "0.00";
        QrInput = "0.00";
    }

    [RelayCommand]
    private void FillRemainingInQr()
    {
        RecalculatePreview();
        TryParseAmount(CreditRedeemInput, out decimal credit);
        decimal remaining = Math.Max(0m, ProjectedPayable - Invoice.PaidAmount - credit);
        CashInput = "0.00";
        CardInput = "0.00";
        QrInput = remaining.ToString("0.00", CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    public async Task SaveDraftInvoiceAsync()
    {
        if (Invoice.IsVoided)
        {
            StatusMessage = "Cannot modify a voided invoice.";
            return;
        }

        if (LineRows.Count == 0)
        {
            StatusMessage = IsDentistCheckoutMode
                ? "Add at least one billable item, or click 'No-Charge Checkout' for a free visit."
                : "Add at least one item before saving the invoice.";
            return;
        }

        var saved = await PersistDraftInternalAsync(sendToCounterCheckout: IsDentistCheckoutMode);
        if (saved is null)
        {
            return;
        }

        HasUnsavedChanges = false;
        _linesModified = false;
        _onInvoiceChanged?.Invoke(saved);

        if (IsDentistCheckoutMode)
        {
            StatusMessage = $"Draft {saved.InvoiceNumber} sent to front-desk counter (Awaiting Checkout).";
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            StatusMessage = $"Saved draft invoice {saved.InvoiceNumber} (Payable: RM {saved.PatientPayableAmount:N2}).";
        }
    }

    [RelayCommand]
    private async Task CompleteNoChargeVisitAsync()
    {
        var apptId = _appointment?.Id ?? _visit?.AppointmentId;
        if (!apptId.HasValue)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            await _apiClient.UpdateQueueStatusAsync(apptId.Value, new UpdateQueueStatusRequest
            {
                NewStatus = QueueStatus.Completed,
                UpdatedBy = _activeStaffName
            });
            HasUnsavedChanges = false;
            _onInvoiceChanged?.Invoke(Invoice);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = $"No-charge checkout failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SettlePaymentAsync()
    {
        if (Invoice.IsVoided)
        {
            StatusMessage = "Cannot settle a voided invoice.";
            return;
        }

        if (LineRows.Count == 0)
        {
            StatusMessage = "Add at least one invoice line item before settling payment.";
            return;
        }

        if (!TryParseAmount(DiscountInput, out decimal discount)
            || !TryParseAmount(PanelCoverageInput, out decimal panelCoverage)
            || !TryParseAmount(CreditRedeemInput, out decimal creditRedeem)
            || !TryParseAmount(CashInput, out decimal cash)
            || !TryParseAmount(CardInput, out decimal card)
            || !TryParseAmount(QrInput, out decimal qr))
        {
            StatusMessage = "All RM amounts must be valid non-negative numbers.";
            return;
        }

        // Ensure draft invoice & any edited line items are persisted first
        if (Invoice.Id == Guid.Empty || (_linesModified && CanEditDraftLines))
        {
            var persisted = await PersistDraftInternalAsync(sendToCounterCheckout: false);
            if (persisted is null)
            {
                return;
            }
        }

        var splits = new List<PaymentSplitDto>();
        string? panelName = string.Equals(SelectedPanel, "None", StringComparison.OrdinalIgnoreCase)
            ? null
            : SelectedPanel;

        decimal additionalPanel = Math.Max(0m, panelCoverage - Invoice.PanelCoveredAmount);
        if (additionalPanel > 0m)
        {
            splits.Add(new PaymentSplitDto
            {
                Method = PaymentMethod.CorporatePanel,
                Amount = additionalPanel,
                PanelName = panelName ?? "Corporate Panel",
                ReferenceNo = string.IsNullOrWhiteSpace(ReferenceNoInput) ? null : ReferenceNoInput.Trim()
            });
        }

        if (creditRedeem > 0m)
        {
            splits.Add(new PaymentSplitDto
            {
                Method = PaymentMethod.PatientCredit,
                Amount = creditRedeem,
                ReferenceNo = "CREDIT-WALLET"
            });
        }

        if (cash > 0m)
        {
            splits.Add(new PaymentSplitDto { Method = PaymentMethod.Cash, Amount = cash });
        }

        if (card > 0m)
        {
            splits.Add(new PaymentSplitDto
            {
                Method = PaymentMethod.Card,
                Amount = card,
                ReferenceNo = string.IsNullOrWhiteSpace(ReferenceNoInput) ? null : ReferenceNoInput.Trim()
            });
        }

        if (qr > 0m)
        {
            splits.Add(new PaymentSplitDto
            {
                Method = PaymentMethod.QrTransfer,
                Amount = qr,
                ReferenceNo = string.IsNullOrWhiteSpace(ReferenceNoInput) ? null : ReferenceNoInput.Trim()
            });
        }

        RecalculatePreview();
        var overpaymentMode = ProjectedExcessAmount > 0m
            ? SelectedOverpaymentHandling
            : OverpaymentHandling.None;

        decimal excessBeforeSettle = ProjectedExcessAmount;

        try
        {
            var updated = await _apiClient.ApplyPaymentAsync(Invoice.Id, new ApplyPaymentRequest
            {
                DiscountAmount = discount,
                PanelName = panelName,
                PanelCoveredAmount = panelCoverage,
                Payments = splits,
                OverpaymentHandling = overpaymentMode,
                DoctorCommissionRate = BillingCalculator.DefaultDoctorCommissionRate,
                CollectedBy = _activeStaffName
            });

            if (SelectedPatient is not null)
            {
                if (creditRedeem > 0m)
                {
                    SelectedPatient.CreditBalance = BillingCalculator.Round2(Math.Max(0m, SelectedPatient.CreditBalance - creditRedeem));
                }
                if (overpaymentMode == OverpaymentHandling.StoreAsPatientCredit && excessBeforeSettle > 0m)
                {
                    SelectedPatient.CreditBalance = BillingCalculator.Round2(SelectedPatient.CreditBalance + excessBeforeSettle);
                }
                OnPropertyChanged(nameof(AvailableCreditDisplay));
                OnPropertyChanged(nameof(CanUsePatientCredit));
            }

            Invoice = updated;
            IsEditable = !updated.IsVoided && updated.BalanceDue > 0m;
            CanEditDraftLines = false;
            _linesModified = false;

            _suppressDirty = true;
            CreditRedeemInput = "0.00";
            CashInput = "0.00";
            CardInput = "0.00";
            QrInput = "0.00";
            _suppressDirty = false;

            HasUnsavedChanges = false;
            OnPropertyChanged(nameof(StatusBadgeText));
            OnPropertyChanged(nameof(ModalTitle));
            OnPropertyChanged(nameof(CanVoidInvoice));
            OnPropertyChanged(nameof(HasSuggestions));
            RecalculatePreview();
            _onInvoiceChanged?.Invoke(updated);

            if (updated.BalanceDue <= 0m)
            {
                string extra = overpaymentMode == OverpaymentHandling.StoreAsPatientCredit && excessBeforeSettle > 0m
                    ? $" · Stored +RM {excessBeforeSettle:N2} in Patient Credit"
                    : overpaymentMode == OverpaymentHandling.ReturnCashChange && excessBeforeSettle > 0m
                        ? $" · Return Cash Change: RM {excessBeforeSettle:N2}"
                        : string.Empty;
                StatusMessage = $"Settled {updated.InvoiceNumber} (Commission: RM {updated.DoctorCommissionAmount:N2}){extra}.";
            }
            else
            {
                StatusMessage = $"Partial payment posted. Remaining balance: RM {updated.BalanceDue:N2}.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Payment failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SoftVoidInvoiceAsync()
    {
        if (Invoice.Id == Guid.Empty || Invoice.IsVoided)
        {
            StatusMessage = "Only saved active invoices can be soft-voided.";
            return;
        }

        if (string.IsNullOrWhiteSpace(VoidReasonInput))
        {
            StatusMessage = "A void reason is mandatory for the immutable audit log.";
            return;
        }

        var confirm = MessageBox.Show(
            $"Soft-void invoice {Invoice.InvoiceNumber}?\nReason: {VoidReasonInput.Trim()}\n\nFinancial records are never deleted and any patient credit entries will be reversed in the Audit Ledger.",
            "Confirm Audited Soft-Void",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var updated = await _apiClient.VoidInvoiceAsync(Invoice.Id, new VoidInvoiceRequest
            {
                VoidReason = VoidReasonInput.Trim(),
                VoidedBy = _activeStaffName
            });

            Invoice = updated;
            IsEditable = false;
            CanEditDraftLines = false;
            HasUnsavedChanges = false;
            OnPropertyChanged(nameof(StatusBadgeText));
            OnPropertyChanged(nameof(CanVoidInvoice));
            RecalculatePreview();
            _onInvoiceChanged?.Invoke(updated);
            StatusMessage = $"Invoice {updated.InvoiceNumber} soft-voided and logged to Audit Trail.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Soft-void failed: {ex.Message}";
        }
    }

    private async Task<InvoiceDto?> PersistDraftInternalAsync(bool sendToCounterCheckout)
    {
        if (!IsWalkInRetailMode && (SelectedPatient is null || SelectedPatient.Id == Guid.Empty))
        {
            StatusMessage = "Select a registered patient or switch to Walk-In Retail Customer.";
            return null;
        }

        TryParseAmount(DiscountInput, out decimal discount);
        TryParseAmount(PanelCoverageInput, out decimal panelCov);
        string? panelName = string.Equals(SelectedPanel, "None", StringComparison.OrdinalIgnoreCase)
            ? null
            : SelectedPanel;

        try
        {
            var saved = await _apiClient.SaveInvoiceDraftAsync(new SaveInvoiceDraftRequest
            {
                InvoiceId = Invoice.Id == Guid.Empty ? null : Invoice.Id,
                BranchId = _branchId,
                IsWalkInRetail = IsWalkInRetailMode,
                PatientId = IsWalkInRetailMode ? null : SelectedPatient!.Id,
                PatientName = IsWalkInRetailMode
                    ? (string.IsNullOrWhiteSpace(WalkInCustomerNameInput) ? "Walk-In Retail Customer" : WalkInCustomerNameInput.Trim())
                    : SelectedPatient!.FullName,
                DoctorName = IsWalkInRetailMode ? "Counter / Retail" : SelectedDoctorName,
                AppointmentId = IsWalkInRetailMode ? null : (Invoice.AppointmentId ?? _appointment?.Id ?? _visit?.AppointmentId),
                VisitId = IsWalkInRetailMode ? null : (Invoice.VisitId ?? _visit?.Id),
                Lines = LineRows.Select(r => CloneLine(r.Source)).ToList(),
                DiscountAmount = discount,
                PanelName = panelName,
                PanelCoveredAmount = panelCov,
                SendToCounterCheckout = sendToCounterCheckout,
                UpdatedBy = _activeStaffName
            });

            Invoice = saved;
            OnPropertyChanged(nameof(ModalTitle));
            OnPropertyChanged(nameof(StatusBadgeText));
            OnPropertyChanged(nameof(CanVoidInvoice));
            return saved;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not save draft invoice: {ex.Message}";
            return null;
        }
    }

    private void ApplyInitialDoctorSuggestions()
    {
        var suggestionResult = InvoiceSuggestionEngine.BuildSuggestions(
            _visit,
            _appointment,
            SelectedPatient,
            _billingSettings,
            _visit?.DentistId ?? _appointment?.DentistId,
            SelectedDoctorName,
            forceScanNotes: false);

        LineRows.Clear();
        foreach (var line in suggestionResult.AutoPopulatedLines)
        {
            LineRows.Add(new InvoiceLineRowItem(line));
        }

        SuggestedChips.Clear();
        foreach (var chip in suggestionResult.SuggestedAddOns.Take(MaxSuggestedChips))
        {
            SuggestedChips.Add(new SuggestedInvoiceChipVm(chip));
        }

        string modeText = suggestionResult.AppliedMode switch
        {
            DoctorInvoiceInitMode.BlankWithSuggestions => "Blank + Suggestions",
            DoctorInvoiceInitMode.BlankManualOnly => "Blank Manual",
            _ => "Auto-Populate + Suggestions"
        };

        SuggestionHeaderText = $"Suggestions ({suggestionResult.PreferenceSourceLabel} · {modeText})";
        StatusMessage = suggestionResult.AppliedMode == DoctorInvoiceInitMode.BlankManualOnly
            ? $"Blank invoice opened ({suggestionResult.PreferenceSourceLabel}). Click 'Scan Notes' anytime for suggestions."
            : $"Prepared invoice using {suggestionResult.PreferenceSourceLabel} ({modeText}).";

        OnPropertyChanged(nameof(HasSuggestions));
    }

    private void PopulateSuggestionChipsOnly(bool forceScanNotes)
    {
        var suggestionResult = InvoiceSuggestionEngine.BuildSuggestions(
            _visit,
            _appointment,
            SelectedPatient,
            _billingSettings,
            _visit?.DentistId ?? _appointment?.DentistId,
            SelectedDoctorName,
            forceScanNotes: forceScanNotes);

        var existingCatalogIds = LineRows
            .Where(r => r.Source.CatalogItemId.HasValue)
            .Select(r => r.Source.CatalogItemId!.Value)
            .ToHashSet();

        SuggestedChips.Clear();
        foreach (var item in suggestionResult.SuggestedAddOns
            .Where(s => !existingCatalogIds.Contains(s.CatalogItemId))
            .Take(MaxSuggestedChips))
        {
            SuggestedChips.Add(new SuggestedInvoiceChipVm(item));
        }

        OnPropertyChanged(nameof(HasSuggestions));
    }

    private void RefreshPatientSuggestions()
    {
        SuggestedPatients.Clear();
        IEnumerable<PatientDto> query = _allPatients;
        if (!string.IsNullOrWhiteSpace(PatientSearchInput))
        {
            var term = PatientSearchInput.Trim();
            query = query.Where(p =>
                p.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || p.IcNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                || p.Phone.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var p in query.Take(3))
        {
            SuggestedPatients.Add(p);
        }
    }

    private void RecalculatePreview()
    {
        if (Invoice.IsVoided)
        {
            ProjectedSubtotal = Invoice.Subtotal;
            ProjectedPayable = 0m;
            ProjectedBalanceDue = 0m;
            ProjectedExcessAmount = 0m;
            ProjectedCommission = 0m;
            OnPropertyChanged(nameof(HasProjectedExcess));
            return;
        }

        TryParseAmount(DiscountInput, out decimal discount);
        TryParseAmount(PanelCoverageInput, out decimal panel);
        TryParseAmount(CreditRedeemInput, out decimal credit);
        TryParseAmount(CashInput, out decimal cash);
        TryParseAmount(CardInput, out decimal card);
        TryParseAmount(QrInput, out decimal qr);

        var previewInvoice = new InvoiceDto
        {
            IsWalkInRetail = IsWalkInRetailMode,
            DoctorName = IsWalkInRetailMode ? "Counter / Retail" : SelectedDoctorName,
            DiscountAmount = discount,
            PanelCoveredAmount = panel,
            Lines = LineRows.Select(r => CloneLine(r.Source)).ToList()
        };
        BillingCalculator.RecalculateInvoice(previewInvoice, BillingCalculator.DefaultDoctorCommissionRate);

        ProjectedSubtotal = previewInvoice.Subtotal;
        ProjectedPayable = previewInvoice.PatientPayableAmount;
        ProjectedCommission = previewInvoice.DoctorCommissionAmount;

        decimal remainingAfterPriorPayments = Math.Max(0m, ProjectedPayable - Invoice.PaidAmount);
        decimal newTendered = BillingCalculator.Round2(credit + cash + card + qr);
        ProjectedBalanceDue = BillingCalculator.Round2(Math.Max(0m, remainingAfterPriorPayments - newTendered));
        ProjectedExcessAmount = BillingCalculator.Round2(Math.Max(0m, newTendered - remainingAfterPriorPayments));

        if (ProjectedExcessAmount > 0m && !CanStoreOverpaymentCredit)
        {
            SelectedOverpaymentHandling = OverpaymentHandling.ReturnCashChange;
        }

        OnPropertyChanged(nameof(HasProjectedExcess));
    }

    private static InvoiceLineDto CloneLine(InvoiceLineDto l) => new()
    {
        Id = l.Id == Guid.Empty ? Guid.NewGuid() : l.Id,
        CatalogItemId = l.CatalogItemId,
        ToothNumber = l.ToothNumber,
        ProcedureName = l.ProcedureName,
        Category = l.Category,
        IsCommissionable = l.Category != BillingCatalogCategory.OtcProduct && l.IsCommissionable,
        UnitPrice = l.UnitPrice,
        Quantity = l.Quantity,
        LineTotal = l.LineTotal,
        SourceStage = l.SourceStage
    };

    private static bool TryParseAmount(string? text, out decimal amount)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            amount = 0m;
            return true;
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount >= 0m;
    }

    private void MarkDirty()
    {
        if (!_suppressDirty)
        {
            HasUnsavedChanges = true;
        }
    }
}
