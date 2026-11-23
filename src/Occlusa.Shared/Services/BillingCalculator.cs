using Occlusa.Shared.Models;

namespace Occlusa.Shared.Services;

/// <summary>
/// Outcome of validating and applying split payments to an invoice.
/// </summary>
public class PaymentValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public InvoiceDto Invoice { get; init; } = new();
}

/// <summary>
/// Deterministic pure C# billing, split-payment, commission, and EOD reconciliation calculator.
/// </summary>
public static class BillingCalculator
{
    public const decimal DefaultDoctorCommissionRate = 0.40m;

    public static decimal Round2(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Computes an invoice line total using MYR 2-decimal precision.
    /// </summary>
    public static decimal ComputeLineTotal(decimal unitPrice, int quantity)
    {
        if (unitPrice < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
        }

        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity cannot be negative.");
        }

        return Round2(unitPrice * quantity);
    }

    /// <summary>
    /// Recomputes subtotal, clamped discount, panel coverage, payable balance, paid amount, and doctor commission.
    /// </summary>
    public static void RecalculateInvoice(
        InvoiceDto invoice,
        decimal doctorCommissionRate = DefaultDoctorCommissionRate)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        if (doctorCommissionRate < 0m || doctorCommissionRate > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(doctorCommissionRate),
                "Commission rate must be between 0.0 and 1.0.");
        }

        foreach (var line in invoice.Lines)
        {
            var safePrice = Math.Max(0m, line.UnitPrice);
            var safeQty = Math.Max(0, line.Quantity);
            line.UnitPrice = Round2(safePrice);
            line.Quantity = safeQty;
            line.LineTotal = ComputeLineTotal(line.UnitPrice, line.Quantity);
        }

        var subtotal = Round2(invoice.Lines.Sum(l => l.LineTotal));
        invoice.Subtotal = subtotal;

        // Clamp discount strictly within [0, Subtotal].
        invoice.DiscountAmount = Round2(Math.Clamp(invoice.DiscountAmount, 0m, subtotal));

        var netAfterDiscount = Round2(subtotal - invoice.DiscountAmount);

        var panelSplits = invoice.Payments
            .Where(p => p.Method == PaymentMethod.CorporatePanel)
            .ToList();

        if (panelSplits.Count > 0)
        {
            var splitPanelTotal = Round2(panelSplits.Sum(p => Math.Max(0m, p.Amount)));
            invoice.PanelCoveredAmount = Round2(Math.Clamp(splitPanelTotal, 0m, netAfterDiscount));

            if (string.IsNullOrWhiteSpace(invoice.PanelName))
            {
                invoice.PanelName = panelSplits
                    .Select(p => p.PanelName)
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            }
        }
        else
        {
            invoice.PanelCoveredAmount = Round2(Math.Clamp(invoice.PanelCoveredAmount, 0m, netAfterDiscount));
        }

        invoice.PatientPayableAmount = Round2(Math.Max(0m, netAfterDiscount - invoice.PanelCoveredAmount));

        invoice.PaidAmount = Round2(invoice.Payments
            .Where(p => p.Method != PaymentMethod.CorporatePanel)
            .Sum(p => Math.Max(0m, p.Amount)));

        invoice.DoctorCommissionAmount = invoice.IsVoided
            ? 0m
            : Round2(netAfterDiscount * doctorCommissionRate);
    }

    /// <summary>
    /// Validates discount, corporate panel deduction, and split-payment lines without allowing overpayment or negative amounts.
    /// </summary>
    public static PaymentValidationResult ValidateAndApplyPayments(
        InvoiceDto invoice,
        ApplyPaymentRequest request)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(request);

        if (invoice.IsVoided)
        {
            return Fail(invoice, "Cannot apply payment to a voided invoice.");
        }

        if (request.DoctorCommissionRate < 0m || request.DoctorCommissionRate > 1m)
        {
            return Fail(invoice, "Doctor commission rate must be between 0% and 100%.");
        }

        if (request.DiscountAmount < 0m)
        {
            return Fail(invoice, "Discount amount cannot be negative.");
        }

        if (request.PanelCoveredAmount < 0m)
        {
            return Fail(invoice, "Corporate panel covered amount cannot be negative.");
        }

        if (request.Payments.Any(p => p.Amount <= 0m))
        {
            return Fail(invoice, "Every payment split amount must be greater than zero.");
        }

        foreach (var line in invoice.Lines)
        {
            if (line.UnitPrice < 0m || line.Quantity < 0)
            {
                return Fail(invoice, "Invoice lines cannot contain negative prices or quantities.");
            }

            line.LineTotal = ComputeLineTotal(line.UnitPrice, line.Quantity);
        }

        var subtotal = Round2(invoice.Lines.Sum(l => l.LineTotal));
        var clampedDiscount = Round2(Math.Clamp(request.DiscountAmount, 0m, subtotal));
        var netAfterDiscount = Round2(subtotal - clampedDiscount);

        var existingPanelSplits = Round2(invoice.Payments
            .Where(p => p.Method == PaymentMethod.CorporatePanel)
            .Sum(p => p.Amount));

        var newPanelSplits = Round2(request.Payments
            .Where(p => p.Method == PaymentMethod.CorporatePanel)
            .Sum(p => p.Amount));

        var effectivePanelAmount = newPanelSplits > 0m || existingPanelSplits > 0m
            ? Round2(existingPanelSplits + newPanelSplits)
            : Round2(request.PanelCoveredAmount > 0m ? request.PanelCoveredAmount : invoice.PanelCoveredAmount);

        if (effectivePanelAmount > netAfterDiscount)
        {
            return Fail(invoice, "Corporate panel claim cannot exceed net invoice total after discount.");
        }

        var patientPayable = Round2(netAfterDiscount - effectivePanelAmount);

        var existingPatientPaid = Round2(invoice.Payments
            .Where(p => p.Method != PaymentMethod.CorporatePanel)
            .Sum(p => p.Amount));

        var newPatientPaid = Round2(request.Payments
            .Where(p => p.Method != PaymentMethod.CorporatePanel)
            .Sum(p => p.Amount));

        if (Round2(existingPatientPaid + newPatientPaid) > patientPayable)
        {
            return Fail(invoice, "Total split payments exceed the patient payable amount.");
        }

        if (request.Payments.Count == 0 && effectivePanelAmount == 0m && patientPayable > 0m)
        {
            return Fail(invoice, "At least one payment split is required when balance is due.");
        }

        invoice.DiscountAmount = clampedDiscount;
        invoice.PanelCoveredAmount = effectivePanelAmount;

        if (!string.IsNullOrWhiteSpace(request.PanelName))
        {
            invoice.PanelName = request.PanelName.Trim();
        }
        else if (string.IsNullOrWhiteSpace(invoice.PanelName))
        {
            invoice.PanelName = request.Payments
                .Where(p => p.Method == PaymentMethod.CorporatePanel && !string.IsNullOrWhiteSpace(p.PanelName))
                .Select(p => p.PanelName!.Trim())
                .FirstOrDefault();
        }

        foreach (var split in request.Payments)
        {
            invoice.Payments.Add(new PaymentSplitDto
            {
                Method = split.Method,
                Amount = Round2(split.Amount),
                ReferenceNo = split.ReferenceNo,
                PanelName = split.PanelName ?? (split.Method == PaymentMethod.CorporatePanel ? invoice.PanelName : null)
            });
        }

        RecalculateInvoice(invoice, request.DoctorCommissionRate);

        return new PaymentValidationResult
        {
            IsValid = true,
            Invoice = invoice
        };
    }

    /// <summary>
    /// Aggregates branch invoices for a given date into an end-of-day sales reconciliation report,
    /// excluding voided invoices from revenue totals.
    /// </summary>
    public static DailySalesReconciliationDto CalculateDailySalesReconciliation(
        string branchId,
        DateOnly date,
        IEnumerable<InvoiceDto> invoices,
        string reconciledBy = "System")
    {
        ArgumentNullException.ThrowIfNull(invoices);

        var branchInvoices = invoices
            .Where(i => string.Equals(i.BranchId, branchId, StringComparison.OrdinalIgnoreCase)
                     && DateOnly.FromDateTime(i.CreatedAt.DateTime) == date)
            .ToList();

        var activeInvoices = branchInvoices.Where(i => !i.IsVoided).ToList();
        var voidedCount = branchInvoices.Count(i => i.IsVoided);

        var grossRevenue = Round2(activeInvoices.Sum(i => i.Subtotal));
        var totalDiscounts = Round2(activeInvoices.Sum(i => i.DiscountAmount));
        var totalPanelClaims = Round2(activeInvoices.Sum(i => i.PanelCoveredAmount));

        var allActivePayments = activeInvoices.SelectMany(i => i.Payments).ToList();
        var cash = Round2(allActivePayments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount));
        var card = Round2(allActivePayments.Where(p => p.Method == PaymentMethod.Card).Sum(p => p.Amount));
        var qr = Round2(allActivePayments.Where(p => p.Method == PaymentMethod.QrTransfer).Sum(p => p.Amount));

        var netCollected = Round2(cash + card + qr);
        var totalCommissions = Round2(activeInvoices.Sum(i => i.DoctorCommissionAmount));

        var allBalanced = activeInvoices.All(inv =>
        {
            if (inv.Payments.Any(p => p.Amount < 0m))
            {
                return false;
            }

            var invPatientPayments = Round2(inv.Payments
                .Where(p => p.Method != PaymentMethod.CorporatePanel)
                .Sum(p => p.Amount));

            var invPanelSplits = Round2(inv.Payments
                .Where(p => p.Method == PaymentMethod.CorporatePanel)
                .Sum(p => p.Amount));

            var panelMatches = invPanelSplits == 0m || invPanelSplits == Round2(inv.PanelCoveredAmount);
            return invPatientPayments == Round2(inv.PaidAmount)
                && inv.PaidAmount <= inv.PatientPayableAmount
                && panelMatches;
        }) && netCollected == Round2(activeInvoices.Sum(i => i.PaidAmount));

        return new DailySalesReconciliationDto
        {
            BranchId = branchId,
            Date = date,
            TotalInvoicesCount = activeInvoices.Count,
            VoidedInvoicesCount = voidedCount,
            GrossRevenue = grossRevenue,
            TotalDiscounts = totalDiscounts,
            TotalPanelClaims = totalPanelClaims,
            TotalCashCollected = cash,
            TotalCardCollected = card,
            TotalQrCollected = qr,
            TotalDoctorCommissions = totalCommissions,
            NetCollected = netCollected,
            IsBalanced = allBalanced,
            ReconciledBy = string.IsNullOrWhiteSpace(reconciledBy) ? "System" : reconciledBy.Trim(),
            ReconciledAt = DateTimeOffset.UtcNow
        };
    }

    private static PaymentValidationResult Fail(InvoiceDto invoice, string message) =>
        new()
        {
            IsValid = false,
            ErrorMessage = message,
            Invoice = invoice
        };
}
