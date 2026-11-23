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
/// ViewModel for InvoiceCheckoutModal managing Corporate Panel coverage, split-payment settlement
/// (Panel + Cash + Card + QR/Transfer), Doctor Commission calculation, and audited Soft-Voiding.
/// </summary>
public partial class InvoiceCheckoutViewModel : ObservableObject
{
    private readonly OcclusaApiClient _apiClient;
    private readonly Action<InvoiceDto> _onInvoiceChanged;
    private bool _suppressDirty;

    [ObservableProperty]
    private InvoiceDto _invoice;

    [ObservableProperty]
    private string _discountInput = "0.00";

    [ObservableProperty]
    private string _selectedPanel = "None";

    [ObservableProperty]
    private string _panelCoverageInput = "0.00";

    [ObservableProperty]
    private string _cashInput = "0.00";

    [ObservableProperty]
    private string _cardInput = "0.00";

    [ObservableProperty]
    private string _qrInput = "0.00";

    [ObservableProperty]
    private string _referenceNoInput = string.Empty;

    [ObservableProperty]
    private string _voidReasonInput = string.Empty;

    [ObservableProperty]
    private decimal _projectedPayable;

    [ObservableProperty]
    private decimal _projectedBalanceDue;

    [ObservableProperty]
    private decimal _projectedCommission;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isEditable = true;

    public InvoiceCheckoutViewModel(
        InvoiceDto invoice,
        OcclusaApiClient apiClient,
        Action<InvoiceDto> onInvoiceChanged)
    {
        _invoice = invoice;
        _apiClient = apiClient;
        _onInvoiceChanged = onInvoiceChanged;

        LineRows = new ObservableCollection<InvoiceLineRowItem>(
            invoice.Lines.Select(l => new InvoiceLineRowItem(l)));

        AvailablePanels = new ObservableCollection<string>
        {
            "None",
            "AIA Dental Panel",
            "PMCare",
            "MiCare",
            "Mednefits",
            "CompuMed",
            "Allianz Corporate"
        };

        _suppressDirty = true;
        DiscountInput = invoice.DiscountAmount.ToString("0.00", CultureInfo.InvariantCulture);
        SelectedPanel = string.IsNullOrWhiteSpace(invoice.PanelName) ? "None" : invoice.PanelName;
        if (!AvailablePanels.Contains(SelectedPanel))
        {
            AvailablePanels.Add(SelectedPanel);
        }
        PanelCoverageInput = invoice.PanelCoveredAmount.ToString("0.00", CultureInfo.InvariantCulture);
        IsEditable = !invoice.IsVoided && invoice.BalanceDue > 0m;
        _suppressDirty = false;

        RecalculatePreview();
    }

    public ObservableCollection<InvoiceLineRowItem> LineRows { get; }
    public ObservableCollection<string> AvailablePanels { get; }

    public string ModalTitle => $"Invoice Checkout — {Invoice.InvoiceNumber}";
    public string ModalSubtitle => $"{Invoice.PatientName} · Doctor: {Invoice.DoctorName} · Branch: {Invoice.BranchId}";
    public string StatusBadgeText => Invoice.IsVoided
        ? "VOIDED"
        : Invoice.BalanceDue <= 0m
            ? "SETTLED"
            : "PAYMENT DUE";

    partial void OnDiscountInputChanged(string value) { MarkDirty(); RecalculatePreview(); }
    partial void OnSelectedPanelChanged(string value) { MarkDirty(); RecalculatePreview(); }
    partial void OnPanelCoverageInputChanged(string value) { MarkDirty(); RecalculatePreview(); }
    partial void OnCashInputChanged(string value) { MarkDirty(); RecalculatePreview(); }
    partial void OnCardInputChanged(string value) { MarkDirty(); RecalculatePreview(); }
    partial void OnQrInputChanged(string value) { MarkDirty(); RecalculatePreview(); }
    partial void OnVoidReasonInputChanged(string value) => MarkDirty();

    [RelayCommand]
    private void FillRemainingInCash()
    {
        RecalculatePreview();
        decimal remaining = Math.Max(0m, ProjectedPayable - Invoice.PaidAmount);
        CashInput = remaining.ToString("0.00", CultureInfo.InvariantCulture);
        CardInput = "0.00";
        QrInput = "0.00";
    }

    [RelayCommand]
    private void FillRemainingInQr()
    {
        RecalculatePreview();
        decimal remaining = Math.Max(0m, ProjectedPayable - Invoice.PaidAmount);
        CashInput = "0.00";
        CardInput = "0.00";
        QrInput = remaining.ToString("0.00", CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    public async Task SettlePaymentAsync()
    {
        if (Invoice.IsVoided)
        {
            StatusMessage = "Cannot settle a voided invoice.";
            return;
        }

        if (!TryParseAmount(DiscountInput, out decimal discount)
            || !TryParseAmount(PanelCoverageInput, out decimal panelCoverage)
            || !TryParseAmount(CashInput, out decimal cash)
            || !TryParseAmount(CardInput, out decimal card)
            || !TryParseAmount(QrInput, out decimal qr))
        {
            StatusMessage = "All RM amounts must be valid non-negative numbers.";
            return;
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

        try
        {
            var updated = await _apiClient.ApplyPaymentAsync(Invoice.Id, new ApplyPaymentRequest
            {
                DiscountAmount = discount,
                PanelName = panelName,
                PanelCoveredAmount = panelCoverage,
                Payments = splits,
                DoctorCommissionRate = BillingCalculator.DefaultDoctorCommissionRate,
                CollectedBy = "Desktop Cashier"
            });

            Invoice = updated;
            IsEditable = !updated.IsVoided && updated.BalanceDue > 0m;
            _suppressDirty = true;
            CashInput = "0.00";
            CardInput = "0.00";
            QrInput = "0.00";
            _suppressDirty = false;

            HasUnsavedChanges = false;
            OnPropertyChanged(nameof(StatusBadgeText));
            RecalculatePreview();
            _onInvoiceChanged(updated);

            StatusMessage = updated.BalanceDue <= 0m
                ? $"Invoice {updated.InvoiceNumber} fully settled (Commission: RM {updated.DoctorCommissionAmount:N2})."
                : $"Partial payment posted. Remaining balance: RM {updated.BalanceDue:N2}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Payment failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SoftVoidInvoiceAsync()
    {
        if (Invoice.IsVoided)
        {
            StatusMessage = "Invoice is already voided.";
            return;
        }

        if (string.IsNullOrWhiteSpace(VoidReasonInput))
        {
            StatusMessage = "A void reason is mandatory for the immutable audit log.";
            return;
        }

        var confirm = MessageBox.Show(
            $"Soft-void invoice {Invoice.InvoiceNumber}?\nReason: {VoidReasonInput.Trim()}\n\nFinancial records are never deleted and will be written to Audit Logs.",
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
                VoidedBy = "Desktop Cashier"
            });

            Invoice = updated;
            IsEditable = false;
            HasUnsavedChanges = false;
            OnPropertyChanged(nameof(StatusBadgeText));
            RecalculatePreview();
            _onInvoiceChanged(updated);
            StatusMessage = $"Invoice {updated.InvoiceNumber} soft-voided and logged to Audit Trail.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Soft-void failed: {ex.Message}";
        }
    }

    private void RecalculatePreview()
    {
        if (Invoice.IsVoided)
        {
            ProjectedPayable = 0m;
            ProjectedBalanceDue = 0m;
            ProjectedCommission = 0m;
            return;
        }

        TryParseAmount(DiscountInput, out decimal discount);
        TryParseAmount(PanelCoverageInput, out decimal panel);
        TryParseAmount(CashInput, out decimal cash);
        TryParseAmount(CardInput, out decimal card);
        TryParseAmount(QrInput, out decimal qr);

        decimal clampedDiscount = Math.Clamp(discount, 0m, Invoice.Subtotal);
        decimal netAfterDiscount = Math.Max(0m, Invoice.Subtotal - clampedDiscount);
        decimal clampedPanel = Math.Clamp(panel, 0m, netAfterDiscount);
        decimal payable = Math.Max(0m, netAfterDiscount - clampedPanel);

        ProjectedPayable = BillingCalculator.Round2(payable);
        ProjectedBalanceDue = BillingCalculator.Round2(Math.Max(0m, payable - Invoice.PaidAmount - cash - card - qr));
        ProjectedCommission = BillingCalculator.Round2(netAfterDiscount * BillingCalculator.DefaultDoctorCommissionRate);
    }

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
