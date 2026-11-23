using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Tests;

public class BillingCalculatorTests
{
    private static InvoiceDto CreateSampleInvoice(
        string branchId = "HQ-KL",
        DateTimeOffset? createdAt = null)
    {
        return new InvoiceDto
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = "INV-KL-2026-0001",
            BranchId = branchId,
            PatientId = Guid.NewGuid(),
            PatientName = "NURUL AISYAH BINTI ZULKIFLI",
            DoctorName = "Dr. Aina Rahman",
            CreatedAt = createdAt ?? new DateTimeOffset(2026, 11, 23, 10, 30, 0, TimeSpan.FromHours(8)),
            Lines =
            [
                new InvoiceLineDto { ToothNumber = null, ProcedureName = "Scaling & Polishing", UnitPrice = 150.00m, Quantity = 1 },
                new InvoiceLineDto { ToothNumber = 36, ProcedureName = "Composite Filling (MOD)", UnitPrice = 220.00m, Quantity = 1 }
            ]
        };
    }

    [Fact]
    public void ComputeLineTotal_RoundsAwayFromZero_AndRejectsNegatives()
    {
        Assert.Equal(300.00m, BillingCalculator.ComputeLineTotal(150.00m, 2));
        Assert.Equal(10.01m, BillingCalculator.ComputeLineTotal(3.335m, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => BillingCalculator.ComputeLineTotal(-1m, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BillingCalculator.ComputeLineTotal(10m, -1));
    }

    [Fact]
    public void RecalculateInvoice_ClampsDiscountWithinSubtotal_AndComputesCommission()
    {
        var invoice = CreateSampleInvoice(); // Subtotal = 370.00
        invoice.DiscountAmount = 500.00m;   // Exceeds subtotal, should clamp to 370.00

        BillingCalculator.RecalculateInvoice(invoice);

        Assert.Equal(370.00m, invoice.Subtotal);
        Assert.Equal(370.00m, invoice.DiscountAmount);
        Assert.Equal(0.00m, invoice.PatientPayableAmount);
        Assert.Equal(0.00m, invoice.DoctorCommissionAmount);

        invoice.DiscountAmount = -25.00m; // Negative clamped to 0.00
        BillingCalculator.RecalculateInvoice(invoice, doctorCommissionRate: 0.40m);

        Assert.Equal(0.00m, invoice.DiscountAmount);
        Assert.Equal(370.00m, invoice.PatientPayableAmount);
        Assert.Equal(148.00m, invoice.DoctorCommissionAmount);
    }

    [Fact]
    public void ValidateAndApplyPayments_SupportsPanelDeductionAndSplitPayments()
    {
        var invoice = CreateSampleInvoice(); // Subtotal = 370.00
        var request = new ApplyPaymentRequest
        {
            DiscountAmount = 20.00m, // Net = 350.00
            PanelName = "AIA Dental Panel",
            DoctorCommissionRate = 0.40m,
            Payments =
            [
                new PaymentSplitDto { Method = PaymentMethod.CorporatePanel, Amount = 150.00m, PanelName = "AIA Dental Panel", ReferenceNo = "AIA-8821" },
                new PaymentSplitDto { Method = PaymentMethod.Cash, Amount = 100.00m },
                new PaymentSplitDto { Method = PaymentMethod.QrTransfer, Amount = 100.00m, ReferenceNo = "DUITNOW-991" }
            ]
        };

        var result = BillingCalculator.ValidateAndApplyPayments(invoice, request);

        Assert.True(result.IsValid);
        Assert.Equal(370.00m, invoice.Subtotal);
        Assert.Equal(20.00m, invoice.DiscountAmount);
        Assert.Equal(150.00m, invoice.PanelCoveredAmount);
        Assert.Equal("AIA Dental Panel", invoice.PanelName);
        Assert.Equal(200.00m, invoice.PatientPayableAmount);
        Assert.Equal(200.00m, invoice.PaidAmount);
        Assert.Equal(0.00m, invoice.BalanceDue);
        Assert.Equal(140.00m, invoice.DoctorCommissionAmount); // 40% of 350.00 net
    }

    [Fact]
    public void ValidateAndApplyPayments_SupportsSequentialPartialPayments()
    {
        var invoice = CreateSampleInvoice(); // Subtotal = 370.00

        var first = BillingCalculator.ValidateAndApplyPayments(invoice, new ApplyPaymentRequest
        {
            Payments = [new PaymentSplitDto { Method = PaymentMethod.Card, Amount = 200.00m }]
        });
        Assert.True(first.IsValid);
        Assert.Equal(170.00m, invoice.BalanceDue);

        var second = BillingCalculator.ValidateAndApplyPayments(invoice, new ApplyPaymentRequest
        {
            Payments = [new PaymentSplitDto { Method = PaymentMethod.QrTransfer, Amount = 170.00m }]
        });
        Assert.True(second.IsValid);
        Assert.Equal(370.00m, invoice.PaidAmount);
        Assert.Equal(0.00m, invoice.BalanceDue);
    }

    [Theory]
    [InlineData(400.00, 0.00, "exceed")]
    [InlineData(-10.00, 0.00, "greater than zero")]
    [InlineData(0.00, 0.00, "greater than zero")]
    public void ValidateAndApplyPayments_RejectsOverpaymentAndNonPositiveSplits(
        decimal splitAmount,
        decimal discount,
        string expectedErrorFragment)
    {
        var invoice = CreateSampleInvoice(); // Subtotal = 370.00
        var result = BillingCalculator.ValidateAndApplyPayments(invoice, new ApplyPaymentRequest
        {
            DiscountAmount = discount,
            Payments = [new PaymentSplitDto { Method = PaymentMethod.Cash, Amount = splitAmount }]
        });

        Assert.False(result.IsValid);
        Assert.Contains(expectedErrorFragment, result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateAndApplyPayments_RejectsNegativeDiscountOrVoidedInvoice()
    {
        var invoice = CreateSampleInvoice();

        var negDiscount = BillingCalculator.ValidateAndApplyPayments(invoice, new ApplyPaymentRequest
        {
            DiscountAmount = -5m,
            Payments = [new PaymentSplitDto { Method = PaymentMethod.Cash, Amount = 50m }]
        });
        Assert.False(negDiscount.IsValid);

        invoice.IsVoided = true;
        invoice.VoidReason = "Duplicate entry";
        var voidedResult = BillingCalculator.ValidateAndApplyPayments(invoice, new ApplyPaymentRequest
        {
            Payments = [new PaymentSplitDto { Method = PaymentMethod.Cash, Amount = 50m }]
        });
        Assert.False(voidedResult.IsValid);
        Assert.Contains("voided", voidedResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateAndApplyPayments_RejectsPanelExceedingNetTotal()
    {
        var invoice = CreateSampleInvoice(); // Subtotal = 370.00
        var result = BillingCalculator.ValidateAndApplyPayments(invoice, new ApplyPaymentRequest
        {
            DiscountAmount = 50.00m, // Net = 320.00
            Payments =
            [
                new PaymentSplitDto { Method = PaymentMethod.CorporatePanel, Amount = 350.00m, PanelName = "PMCare" }
            ]
        });

        Assert.False(result.IsValid);
        Assert.Contains("panel", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateAndApplyPayments_AllowsFullPanelCoverageWithNoPatientSplits()
    {
        var invoice = CreateSampleInvoice(); // 370.00
        var result = BillingCalculator.ValidateAndApplyPayments(invoice, new ApplyPaymentRequest
        {
            PanelName = "PMCare",
            PanelCoveredAmount = 370.00m,
            Payments = []
        });

        Assert.True(result.IsValid);
        Assert.Equal(370.00m, invoice.PanelCoveredAmount);
        Assert.Equal(0.00m, invoice.PatientPayableAmount);
        Assert.Equal(0.00m, invoice.BalanceDue);
    }

    [Fact]
    public void CalculateDailySalesReconciliation_ExcludesVoidedAndIsolatesByBranchAndDate()
    {
        var targetDate = new DateOnly(2026, 11, 23);
        var myTime = new DateTimeOffset(2026, 11, 23, 14, 0, 0, TimeSpan.FromHours(8));

        var inv1 = CreateSampleInvoice("HQ-KL", myTime); // 370
        BillingCalculator.ValidateAndApplyPayments(inv1, new ApplyPaymentRequest
        {
            DiscountAmount = 20m, // net 350
            Payments =
            [
                new PaymentSplitDto { Method = PaymentMethod.CorporatePanel, Amount = 150m, PanelName = "AIA Dental Panel" },
                new PaymentSplitDto { Method = PaymentMethod.Cash, Amount = 120m },
                new PaymentSplitDto { Method = PaymentMethod.QrTransfer, Amount = 80m }
            ]
        });

        var inv2 = CreateSampleInvoice("HQ-KL", myTime); // 370 -> Card 370
        BillingCalculator.ValidateAndApplyPayments(inv2, new ApplyPaymentRequest
        {
            Payments = [new PaymentSplitDto { Method = PaymentMethod.Card, Amount = 370m }]
        });

        var invVoided = CreateSampleInvoice("HQ-KL", myTime); // Voided: must be excluded from revenue!
        BillingCalculator.ValidateAndApplyPayments(invVoided, new ApplyPaymentRequest
        {
            Payments = [new PaymentSplitDto { Method = PaymentMethod.Cash, Amount = 370m }]
        });
        invVoided.IsVoided = true;
        invVoided.VoidReason = "Entered under wrong patient";
        BillingCalculator.RecalculateInvoice(invVoided);

        var otherBranch = CreateSampleInvoice("PJ-SS2", myTime);
        BillingCalculator.ValidateAndApplyPayments(otherBranch, new ApplyPaymentRequest
        {
            Payments = [new PaymentSplitDto { Method = PaymentMethod.Cash, Amount = 370m }]
        });

        var otherDate = CreateSampleInvoice("HQ-KL", myTime.AddDays(-1));
        BillingCalculator.ValidateAndApplyPayments(otherDate, new ApplyPaymentRequest
        {
            Payments = [new PaymentSplitDto { Method = PaymentMethod.Cash, Amount = 370m }]
        });

        var report = BillingCalculator.CalculateDailySalesReconciliation(
            "HQ-KL",
            targetDate,
            [inv1, inv2, invVoided, otherBranch, otherDate],
            "Admin Siti");

        Assert.Equal("HQ-KL", report.BranchId);
        Assert.Equal(targetDate, report.Date);
        Assert.Equal(2, report.TotalInvoicesCount);
        Assert.Equal(1, report.VoidedInvoicesCount);
        Assert.Equal(740.00m, report.GrossRevenue);
        Assert.Equal(20.00m, report.TotalDiscounts);
        Assert.Equal(150.00m, report.TotalPanelClaims);
        Assert.Equal(120.00m, report.TotalCashCollected);
        Assert.Equal(370.00m, report.TotalCardCollected);
        Assert.Equal(80.00m, report.TotalQrCollected);
        Assert.Equal(570.00m, report.NetCollected);
        Assert.Equal(288.00m, report.TotalDoctorCommissions); // 140 + 148
        Assert.True(report.IsBalanced);
        Assert.Equal("Admin Siti", report.ReconciledBy);
    }

    [Fact]
    public void CalculateDailySalesReconciliation_DetectsImbalance()
    {
        var myTime = new DateTimeOffset(2026, 11, 23, 14, 0, 0, TimeSpan.FromHours(8));
        var inv = CreateSampleInvoice("HQ-KL", myTime);
        BillingCalculator.RecalculateInvoice(inv);
        inv.PaidAmount = 99.00m; // Tampered paid amount with no matching PaymentSplitDto

        var report = BillingCalculator.CalculateDailySalesReconciliation(
            "HQ-KL",
            new DateOnly(2026, 11, 23),
            [inv]);

        Assert.False(report.IsBalanced);
    }
}
