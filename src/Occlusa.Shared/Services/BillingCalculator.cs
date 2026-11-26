using Occlusa.Shared.Models;

namespace Occlusa.Shared.Services;

/// <summary>
/// Outcome of validating and applying split payments (including PatientCredit, cash change, or credit deposit) to an invoice.
/// </summary>
public class PaymentValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public InvoiceDto Invoice { get; init; } = new();
    public decimal CashChangeReturned { get; init; }
    public decimal CreditDeposited { get; init; }
    public decimal CreditRedeemed { get; init; }
}

/// <summary>
/// Deterministic pure C# billing, OTC commission exclusion, patient credit wallet, split-payment, and EOD reconciliation calculator.
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
    /// Recomputes subtotal, clamped discount, panel coverage, payable balance, paid amount, and doctor commission
    /// (excluding Over-The-Counter retail items and non-commissionable lines).
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
            if (line.Category == BillingCatalogCategory.OtcProduct)
            {
                line.IsCommissionable = false;
            }

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

        invoice.CreditedOverpaymentAmount = invoice.IsVoided
            ? 0m
            : Round2(Math.Max(0m, invoice.PaidAmount - invoice.PatientPayableAmount));

        invoice.DoctorCommissionAmount = ComputeCommissionAmount(invoice, subtotal, netAfterDiscount, doctorCommissionRate);
    }

    private static decimal ComputeCommissionAmount(
        InvoiceDto invoice,
        decimal subtotal,
        decimal netAfterDiscount,
        decimal doctorCommissionRate)
    {
        if (invoice.IsVoided
            || invoice.IsWalkInRetail
            || subtotal <= 0m
            || netAfterDiscount <= 0m
            || string.Equals(invoice.DoctorName?.Trim(), "Counter / Retail", StringComparison.OrdinalIgnoreCase))
        {
            return 0m;
        }

        var commissionableSubtotal = Round2(invoice.Lines
            .Where(l => l.IsCommissionable && l.Category != BillingCatalogCategory.OtcProduct)
            .Sum(l => l.LineTotal));

        if (commissionableSubtotal <= 0m)
        {
            return 0m;
        }

        if (commissionableSubtotal >= subtotal)
        {
            return Round2(netAfterDiscount * doctorCommissionRate);
        }

        // Pro-rate invoice-level discount across commissionable vs non-commissionable lines.
        var commissionableShareOfNet = netAfterDiscount * (commissionableSubtotal / subtotal);
        return Round2(Math.Max(0m, commissionableShareOfNet) * doctorCommissionRate);
    }

    /// <summary>
    /// Validates discount, corporate panel deduction, patient credit redemption, split-payment lines,
    /// and explicit overpayment handling (ReturnCashChange vs StoreAsPatientCredit).
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

            if (line.Category == BillingCatalogCategory.OtcProduct)
            {
                line.IsCommissionable = false;
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

        var creditRedeemed = Round2(request.Payments
            .Where(p => p.Method == PaymentMethod.PatientCredit)
            .Sum(p => p.Amount));

        if (creditRedeemed > 0m)
        {
            if (invoice.IsWalkInRetail || invoice.PatientId == Guid.Empty)
            {
                return Fail(invoice, "Patient credit can only be used for a registered patient.");
            }

            if (request.AvailablePatientCredit.HasValue && creditRedeemed > Round2(request.AvailablePatientCredit.Value))
            {
                return Fail(invoice, $"Redeemed credit (RM {creditRedeemed:N2}) exceeds patient's available credit (RM {request.AvailablePatientCredit.Value:N2}).");
            }
        }

        var existingPatientPaid = Round2(invoice.Payments
            .Where(p => p.Method != PaymentMethod.CorporatePanel)
            .Sum(p => p.Amount));

        var newPatientPaid = Round2(request.Payments
            .Where(p => p.Method != PaymentMethod.CorporatePanel)
            .Sum(p => p.Amount));

        var remainingPayableBeforeNew = Round2(Math.Max(0m, patientPayable - existingPatientPaid));
        if (creditRedeemed > remainingPayableBeforeNew)
        {
            return Fail(invoice, "Applied patient credit cannot exceed the remaining invoice balance due.");
        }

        var totalAfterNew = Round2(existingPatientPaid + newPatientPaid);
        var excessAmount = Round2(Math.Max(0m, totalAfterNew - patientPayable));
        decimal cashChangeReturned = 0m;
        decimal creditDeposited = 0m;

        var normalizedSplits = request.Payments
            .Select(s => new PaymentSplitDto
            {
                Method = s.Method,
                Amount = Round2(s.Amount),
                ReferenceNo = s.ReferenceNo,
                PanelName = s.PanelName
            })
            .ToList();

        if (excessAmount > 0m)
        {
            if (request.OverpaymentHandling == OverpaymentHandling.None)
            {
                return Fail(invoice, "Total split payments exceed the patient payable amount. Choose Return Cash Change or Store as Patient Credit.");
            }

            if (request.OverpaymentHandling == OverpaymentHandling.ReturnCashChange)
            {
                var cashOffered = Round2(normalizedSplits.Where(s => s.Method == PaymentMethod.Cash).Sum(s => s.Amount));
                if (cashOffered < excessAmount)
                {
                    return Fail(invoice, "Cash change cannot exceed the cash amount tendered.");
                }

                cashChangeReturned = excessAmount;
                decimal toDeduct = excessAmount;
                for (int i = normalizedSplits.Count - 1; i >= 0 && toDeduct > 0m; i--)
                {
                    if (normalizedSplits[i].Method != PaymentMethod.Cash)
                    {
                        continue;
                    }

                    decimal reduce = Math.Min(normalizedSplits[i].Amount, toDeduct);
                    normalizedSplits[i].Amount = Round2(normalizedSplits[i].Amount - reduce);
                    toDeduct = Round2(toDeduct - reduce);
                }

                normalizedSplits.RemoveAll(s => s.Method == PaymentMethod.Cash && s.Amount <= 0m);
            }
            else if (request.OverpaymentHandling == OverpaymentHandling.StoreAsPatientCredit)
            {
                if (invoice.IsWalkInRetail || invoice.PatientId == Guid.Empty)
                {
                    return Fail(invoice, "Cannot store overpayment credit for an unregistered walk-in retail customer.");
                }

                creditDeposited = excessAmount;
            }
        }

        if (normalizedSplits.Count == 0 && request.Payments.Count == 0 && effectivePanelAmount == 0m && patientPayable > 0m)
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
            invoice.PanelName = normalizedSplits
                .Where(p => p.Method == PaymentMethod.CorporatePanel && !string.IsNullOrWhiteSpace(p.PanelName))
                .Select(p => p.PanelName!.Trim())
                .FirstOrDefault();
        }

        foreach (var split in normalizedSplits)
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
            Invoice = invoice,
            CashChangeReturned = cashChangeReturned,
            CreditDeposited = creditDeposited,
            CreditRedeemed = creditRedeemed
        };
    }

    /// <summary>
    /// Aggregates branch invoices for a given date into an end-of-day sales reconciliation report,
    /// excluding voided invoices and accounting accurately for patient credit deposits &amp; redemptions.
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
        var creditRedeemed = Round2(allActivePayments.Where(p => p.Method == PaymentMethod.PatientCredit).Sum(p => p.Amount));
        var creditDeposited = Round2(activeInvoices.Sum(i => i.CreditedOverpaymentAmount));

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

            var expectedCreditExcess = Round2(Math.Max(0m, inv.PaidAmount - inv.PatientPayableAmount));
            var panelMatches = invPanelSplits == 0m || invPanelSplits == Round2(inv.PanelCoveredAmount);
            return invPatientPayments == Round2(inv.PaidAmount)
                && Round2(inv.CreditedOverpaymentAmount) == expectedCreditExcess
                && panelMatches;
        }) && Round2(netCollected + creditRedeemed) == Round2(activeInvoices.Sum(i => i.PaidAmount));

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
            TotalCreditRedeemed = creditRedeemed,
            TotalCreditDeposited = creditDeposited,
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
