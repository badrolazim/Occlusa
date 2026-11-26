using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Occlusa.Api;
using Occlusa.Api.Options;
using Occlusa.Api.Repositories;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Tests;

public class DecoupledBillingAndSuggestionsTests
{
    private static InMemoryOcclusaRepository CreateRepository() =>
        new(Options.Create(new ClinicOptions()), new HttpContextAccessor());

    [Fact]
    public void BillingCalculator_ExcludesOtcProductsFromDoctorCommission_AndHandlesMixedDiscounts()
    {
        // 1. Standalone Walk-In OTC Retail purchase (toothbrush + toothpaste) -> RM 0.00 commission
        var retailInvoice = new InvoiceDto
        {
            InvoiceNumber = "INV-KL-RETAIL-01",
            BranchId = "HQ-KL",
            IsWalkInRetail = true,
            PatientId = Guid.Empty,
            PatientName = "Walk-In Retail Customer",
            DoctorName = "Counter / Retail",
            Lines =
            [
                new InvoiceLineDto
                {
                    ProcedureName = "Ultra-Soft Bristle Toothbrush",
                    Category = BillingCatalogCategory.OtcProduct,
                    IsCommissionable = true, // Calculator must force false for OtcProduct
                    UnitPrice = 12.00m,
                    Quantity = 2
                },
                new InvoiceLineDto
                {
                    ProcedureName = "Fluoride Desensitizing Toothpaste 100g",
                    Category = BillingCatalogCategory.OtcProduct,
                    UnitPrice = 18.00m,
                    Quantity = 1
                }
            ]
        };

        BillingCalculator.RecalculateInvoice(retailInvoice, 0.40m);
        Assert.Equal(42.00m, retailInvoice.Subtotal);
        Assert.Equal(42.00m, retailInvoice.PatientPayableAmount);
        Assert.Equal(0.00m, retailInvoice.DoctorCommissionAmount);
        Assert.All(retailInvoice.Lines, l => Assert.False(l.IsCommissionable));

        // 2. Mixed invoice: RM 180 Simple Extraction (Commissionable) + RM 20 OTC Mouthwash (Non-Commissionable)
        var mixedInvoice = new InvoiceDto
        {
            InvoiceNumber = "INV-KL-MIXED-01",
            BranchId = "HQ-KL",
            PatientId = Guid.NewGuid(),
            PatientName = "Ahmad Faizal",
            DoctorName = "Dr. Aina Rahman",
            DiscountAmount = 20.00m, // 10% discount across RM 200 subtotal -> commissionable share = 180 * 0.9 = 162 -> 40% = 64.80
            Lines =
            [
                new InvoiceLineDto
                {
                    ProcedureName = "Simple Tooth Extraction",
                    Category = BillingCatalogCategory.Procedure,
                    IsCommissionable = true,
                    UnitPrice = 180.00m,
                    Quantity = 1
                },
                new InvoiceLineDto
                {
                    ProcedureName = "Chlorhexidine Mouthwash",
                    Category = BillingCatalogCategory.OtcProduct,
                    UnitPrice = 20.00m,
                    Quantity = 1
                }
            ]
        };

        BillingCalculator.RecalculateInvoice(mixedInvoice, 0.40m);
        Assert.Equal(200.00m, mixedInvoice.Subtotal);
        Assert.Equal(180.00m, mixedInvoice.PatientPayableAmount);
        Assert.Equal(64.80m, mixedInvoice.DoctorCommissionAmount);
    }

    [Fact]
    public async Task PatientCreditWallet_SupportsOverpaymentDeposit_CrossBranchRedemption_AutoComplete_AndEodBalance()
    {
        var repo = CreateRepository();
        var patient = await repo.UpsertPatientAsync(new UpsertPatientRequest
        {
            FullName = "Syafiq bin Razali",
            IcNumber = "910514-10-5319",
            DateOfBirth = new DateOnly(1991, 5, 14),
            Gender = "Male",
            Phone = "+6012-7008899",
            PrimaryBranchId = "HQ-KL"
        });
        Assert.Equal(0m, patient.CreditBalance);

        // 1. Visit 1 at HQ-KL: RM 150 Scaling, patient pays RM 200 QR and stores RM 50 into Patient Credit
        var (draft1, _, _) = await repo.SaveInvoiceDraftAsync(new SaveInvoiceDraftRequest
        {
            BranchId = "HQ-KL",
            PatientId = patient.Id,
            PatientName = patient.FullName,
            DoctorName = "Dr. Aina Rahman",
            Lines =
            [
                new InvoiceLineDto
                {
                    ProcedureName = "Routine Scaling & Polishing",
                    Category = BillingCatalogCategory.Procedure,
                    IsCommissionable = true,
                    UnitPrice = 150.00m,
                    Quantity = 1
                }
            ]
        });
        Assert.NotNull(draft1);

        var (paid1, _, pay1Err) = await repo.ApplyInvoicePaymentAsync(draft1!.Id, new ApplyPaymentRequest
        {
            OverpaymentHandling = OverpaymentHandling.StoreAsPatientCredit,
            Payments = [new PaymentSplitDto { Method = PaymentMethod.QrTransfer, Amount = 200.00m, ReferenceNo = "DN-OVERPAY" }],
            CollectedBy = "Siti (Reception)"
        });
        Assert.Null(pay1Err);
        Assert.Equal(150.00m, paid1!.PatientPayableAmount);
        Assert.Equal(200.00m, paid1.PaidAmount);
        Assert.Equal(50.00m, paid1.CreditedOverpaymentAmount);
        Assert.Equal(0.00m, paid1.BalanceDue);

        var afterDeposit = await repo.GetPatientByIdAsync(patient.Id);
        Assert.Equal(50.00m, afterDeposit!.CreditBalance);

        // 2. Visit 2 at PJ-SS2 (different branch!): RM 80 bill redeemed using RM 50 shared Credit + RM 100 Cash (RM 70 Cash Change!)
        var dentistsPj = await repo.GetDentistsAsync("PJ-SS2");
        var apptPj = await repo.CreateAppointmentAsync(new CreateAppointmentRequest
        {
            BranchId = "PJ-SS2",
            PatientId = patient.Id,
            DentistId = dentistsPj[0].Id,
            DoctorName = dentistsPj[0].FullName,
            ScheduledAt = DateTimeOffset.UtcNow,
            ProcedureSummary = "Consultation & Periapical X-Ray",
            Status = QueueStatus.InChair,
            VisitType = VisitType.WalkIn,
            CreatedBy = "Aisyah (Reception)"
        });

        var (draft2, queuedCheckout, draft2Err) = await repo.SaveInvoiceDraftAsync(new SaveInvoiceDraftRequest
        {
            BranchId = "PJ-SS2",
            PatientId = patient.Id,
            PatientName = patient.FullName,
            DoctorName = dentistsPj[0].FullName,
            AppointmentId = apptPj.Id,
            VisitId = apptPj.VisitId,
            SendToCounterCheckout = true,
            Lines =
            [
                new InvoiceLineDto
                {
                    ProcedureName = "Consultation & Periapical X-Ray",
                    Category = BillingCatalogCategory.Procedure,
                    IsCommissionable = true,
                    UnitPrice = 80.00m,
                    Quantity = 1
                }
            ],
            UpdatedBy = dentistsPj[0].FullName
        });
        Assert.Null(draft2Err);
        Assert.NotNull(queuedCheckout);
        Assert.Equal(QueueStatus.AwaitingCheckout, queuedCheckout!.Status);

        var (paid2, completedQueue, pay2Err) = await repo.ApplyInvoicePaymentAsync(draft2!.Id, new ApplyPaymentRequest
        {
            OverpaymentHandling = OverpaymentHandling.ReturnCashChange,
            Payments =
            [
                new PaymentSplitDto { Method = PaymentMethod.PatientCredit, Amount = 50.00m },
                new PaymentSplitDto { Method = PaymentMethod.Cash, Amount = 100.00m } // RM 30 needed -> RM 70 cash change returned
            ],
            CollectedBy = "Aisyah (Reception)"
        });
        Assert.Null(pay2Err);
        Assert.Equal(80.00m, paid2!.PaidAmount); // 50 Credit + 30 Net Cash kept in drawer
        Assert.Equal(0.00m, paid2.BalanceDue);
        Assert.NotNull(completedQueue);
        Assert.Equal(QueueStatus.Completed, completedQueue!.Status);

        var afterRedeem = await repo.GetPatientByIdAsync(patient.Id);
        Assert.Equal(0.00m, afterRedeem!.CreditBalance);

        var ledger = await repo.GetPatientCreditLedgerAsync(patient.Id);
        Assert.Equal(2, ledger.Count);
        Assert.Contains(ledger, e => e.EntryType == "CREDIT_DEPOSIT" && e.AmountDelta == 50.00m);
        Assert.Contains(ledger, e => e.EntryType == "CREDIT_REDEEMED" && e.AmountDelta == -50.00m);

        // 3. Verify End-of-Day Reconciliation stays 100% balanced across both branches
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var eodKl = await repo.ReconcileDailySalesAsync(new ReconcileDailySalesRequest { BranchId = "HQ-KL", Date = today, ReconciledBy = "Manager" });
        var eodPj = await repo.ReconcileDailySalesAsync(new ReconcileDailySalesRequest { BranchId = "PJ-SS2", Date = today, ReconciledBy = "Manager" });

        Assert.True(eodKl.IsBalanced);
        Assert.Equal(50.00m, eodKl.TotalCreditDeposited);
        Assert.True(eodPj.IsBalanced);
        Assert.Equal(50.00m, eodPj.TotalCreditRedeemed);
    }

    [Fact]
    public async Task InvoiceSuggestionEngine_RespectsDoctorModes_SoapKeywords_CustomOverrides_AndSafetyAlerts()
    {
        var repo = CreateRepository();
        var settings = await repo.GetBillingSettingsAsync();
        var treatments = await repo.GetTreatmentsAsync();
        var dentists = await repo.GetDentistsAsync("HQ-KL");

        var drAina = dentists.Single(d => d.FullName == "Dr. Aina Rahman");
        var drPriya = dentists.Single(d => d.FullName == "Dr. Priya Nair");
        var drMarcus = dentists.Single(d => d.FullName == "Dr. Marcus Tan");

        var patientWithAlerts = new PatientDto
        {
            Id = Guid.NewGuid(),
            FullName = "Kavitha Subramaniam",
            MedicalAlerts = new MedicalAlertDto
            {
                Allergies = "Penicillin",
                IsPregnant = true
            }
        };

        // Case 1: Extraction written in Doctor's SOAP notes -> Auto-populates Extraction + suggests Painkillers/Antibiotic with Medical Alert warnings!
        var extractionVisit = new PatientVisitDto
        {
            Id = Guid.NewGuid(),
            PatientId = patientWithAlerts.Id,
            DentistId = drAina.Id,
            DoctorName = drAina.FullName,
            Plan = "Forceps exo #46 under LA, gauze hemostasis achieved. Pre-op IOPA reviewed.",
            Odontogram = OdontogramHelper.BuildBlankOdontogram(patientWithAlerts.Id, Guid.NewGuid(), DateTimeOffset.UtcNow)
        };

        var ainaResult = InvoiceSuggestionEngine.BuildSuggestions(
            extractionVisit,
            appointment: null,
            patientWithAlerts,
            settings,
            drAina.Id,
            drAina.FullName);

        Assert.Equal(DoctorInvoiceInitMode.AutoPopulateWithSuggestions, ainaResult.AppliedMode);
        Assert.Single(ainaResult.AutoPopulatedLines);
        Assert.Contains("Simple Tooth Extraction", ainaResult.AutoPopulatedLines[0].ProcedureName);

        // Suggested companions must include Paracetamol (safe), Ibuprofen (warned for Pregnancy), Amoxicillin (warned for Penicillin), and Periapical X-Ray
        var pcm = ainaResult.SuggestedAddOns.Single(s => s.Name.Contains("Paracetamol", StringComparison.OrdinalIgnoreCase));
        Assert.False(pcm.HasSafetyWarning);

        var ibu = ainaResult.SuggestedAddOns.Single(s => s.Name.Contains("Ibuprofen", StringComparison.OrdinalIgnoreCase));
        Assert.True(ibu.HasSafetyWarning);
        Assert.Contains("Pregnancy", ibu.SafetyWarning);

        var amox = ainaResult.SuggestedAddOns.Single(s => s.Name.Contains("Amoxicillin", StringComparison.OrdinalIgnoreCase));
        Assert.True(amox.HasSafetyWarning);
        Assert.Contains("Penicillin", amox.SafetyWarning);

        // Case 2: Dr. Priya defaults to BlankWithSuggestions -> 0 auto-populated lines, primary + companions offered as chips
        var priyaResult = InvoiceSuggestionEngine.BuildSuggestions(
            extractionVisit,
            appointment: null,
            patientWithAlerts,
            settings,
            drPriya.Id,
            drPriya.FullName);

        Assert.Equal(DoctorInvoiceInitMode.BlankWithSuggestions, priyaResult.AppliedMode);
        Assert.Empty(priyaResult.AutoPopulatedLines);
        Assert.Contains(priyaResult.SuggestedAddOns, s => s.Name.Contains("Simple Tooth Extraction", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(priyaResult.SuggestedAddOns, s => s.Name.Contains("Paracetamol", StringComparison.OrdinalIgnoreCase));

        // Case 3: Configure Dr. Marcus as BlankManualOnly + custom Extraction rule via Settings API
        var simpleExoTreatment = treatments.Single(t => t.Name.Contains("Extraction", StringComparison.OrdinalIgnoreCase));
        var mouthwashItem = settings.CatalogItems.Single(c => c.Name.Contains("Mouthwash", StringComparison.OrdinalIgnoreCase));

        var (savedPref, prefErr) = await repo.SaveDoctorInvoicePreferenceAsync(new SaveDoctorInvoicePreferenceRequest
        {
            DentistId = drMarcus.Id,
            DoctorName = drMarcus.FullName,
            InitMode = DoctorInvoiceInitMode.BlankManualOnly,
            EnableNoteKeywordEngine = false,
            TreatmentRules =
            [
                new TreatmentSuggestionRuleDto
                {
                    TreatmentId = simpleExoTreatment.Id,
                    TreatmentName = simpleExoTreatment.Name,
                    SuggestedCatalogItemIds = [mouthwashItem.Id]
                }
            ]
        });
        Assert.Null(prefErr);
        Assert.NotNull(savedPref);

        var updatedSettings = await repo.GetBillingSettingsAsync();
        var marcusBlank = InvoiceSuggestionEngine.BuildSuggestions(
            extractionVisit,
            appointment: null,
            patientWithAlerts,
            updatedSettings,
            drMarcus.Id,
            drMarcus.FullName,
            forceScanNotes: false);

        Assert.Equal(DoctorInvoiceInitMode.BlankManualOnly, marcusBlank.AppliedMode);
        Assert.Empty(marcusBlank.AutoPopulatedLines);
        Assert.Empty(marcusBlank.SuggestedAddOns);

        // Clicking 'Scan Dr Notes' on demand in BlankManualOnly mode surfaces Dr. Marcus's custom Mouthwash override!
        var marcusScanned = InvoiceSuggestionEngine.BuildSuggestions(
            extractionVisit,
            appointment: null,
            patientWithAlerts,
            updatedSettings,
            drMarcus.Id,
            drMarcus.FullName,
            forceScanNotes: true);

        Assert.Empty(marcusScanned.AutoPopulatedLines);
        Assert.Contains(marcusScanned.SuggestedAddOns, s => s.CatalogItemId == mouthwashItem.Id);
        Assert.DoesNotContain(marcusScanned.SuggestedAddOns, s => s.Name.Contains("Amoxicillin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SingleMasterCatalogue_DrivesTreatments_OdontogramPresets_Suggestions_AndWhatsAppAiQuotes()
    {
        await using var app = OcclusaApiApplicationFactory.BuildApplication(
            urls: "http://127.0.0.1:0",
            environmentName: "Development");
        await app.StartAsync();

        try
        {
            var baseAddress = app.Urls.First();
            using var http = new HttpClient { BaseAddress = new Uri(baseAddress) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Occlusa-User", "Clinic Admin");
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Occlusa-Allowed-Branches", "HQ-KL,PJ-SS2");

            var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };
            jsonOpts.Converters.Add(new JsonStringEnumConverter());

            var initialSettings = await http.GetFromJsonAsync<BillingSettingsSnapshotDto>("/api/billing-settings", jsonOpts);
            Assert.NotNull(initialSettings);

            // 1. Editing one price in Master Catalogue updates Appointments/Treatments, Suggestions, and WhatsApp AI simultaneously
            var scaling = initialSettings!.CatalogItems.Single(c => c.Name == "Routine Scaling & Polishing");
            var updateResp = await http.PostAsJsonAsync("/api/billing-settings/catalog", new UpsertBillingCatalogItemRequest
            {
                Id = scaling.Id,
                Name = scaling.Name,
                Category = scaling.Category,
                ClinicalSubcategory = scaling.ClinicalSubcategory,
                UnitPrice = 175.00m,
                IsCommissionable = scaling.IsCommissionable,
                IsBookableAppointment = true,
                DefaultDurationMinutes = 45,
                ShowInOdontogramPresets = true,
                DefaultToothCondition = scaling.DefaultToothCondition,
                NoteKeywords = scaling.NoteKeywords,
                SafetyAlertTags = scaling.SafetyAlertTags,
                IsActive = true
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

            var treatments = await http.GetFromJsonAsync<List<TreatmentDto>>("/api/treatments", jsonOpts);
            Assert.NotNull(treatments);
            var scalingTreatment = treatments!.Single(t => t.Id == scaling.Id);
            Assert.Equal(175.00m, scalingTreatment.DefaultFee);
            Assert.Equal(45, scalingTreatment.DefaultDurationMinutes);

            var refreshedSettings = await http.GetFromJsonAsync<BillingSettingsSnapshotDto>("/api/billing-settings", jsonOpts);
            Assert.NotNull(refreshedSettings);
            var visit = new PatientVisitDto
            {
                DoctorName = "Dr. Aina Rahman",
                Plan = "Full mouth ultrasonic scaling done."
            };
            var suggestions = InvoiceSuggestionEngine.BuildSuggestions(visit, null, null, refreshedSettings!);
            Assert.Single(suggestions.AutoPopulatedLines);
            Assert.Equal(175.00m, suggestions.AutoPopulatedLines[0].UnitPrice);

            var waResp = await http.PostAsJsonAsync("/api/whatsapp/simulate", new WhatsAppSimulateRequest
            {
                PatientPhone = "+6012-9001122",
                PatientName = "Test Price Caller",
                MessageText = "Hi, what are your routine fees and clinic hours?",
                BranchId = "HQ-KL"
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.OK, waResp.StatusCode);
            var thread = await waResp.Content.ReadFromJsonAsync<WhatsAppThreadDto>(jsonOpts);
            Assert.NotNull(thread);
            Assert.Contains("RM175", thread!.Messages.Last().Text, StringComparison.Ordinal);

            // 2. Adding a new Bookable + Odontogram Preset item in Master Catalogue immediately registers it for booking & active dentists
            var addResp = await http.PostAsJsonAsync("/api/billing-settings/catalog", new UpsertBillingCatalogItemRequest
            {
                Name = "Porcelain Veneer",
                Category = BillingCatalogCategory.Procedure,
                ClinicalSubcategory = "Prosthodontics",
                UnitPrice = 1200.00m,
                IsCommissionable = true,
                IsBookableAppointment = true,
                DefaultDurationMinutes = 60,
                ShowInOdontogramPresets = true,
                DefaultToothCondition = ToothCondition.Crown,
                NoteKeywords = "veneer, porcelain veneer",
                IsActive = true
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.OK, addResp.StatusCode);
            var newVeneer = await addResp.Content.ReadFromJsonAsync<BillingCatalogItemDto>(jsonOpts);
            Assert.NotNull(newVeneer);

            var snapshot = await http.GetFromJsonAsync<ClinicDashboardSnapshotDto>("/api/dashboard/snapshot?branchId=HQ-KL", jsonOpts);
            Assert.NotNull(snapshot);
            Assert.Contains(snapshot!.Treatments, t => t.Id == newVeneer!.Id && t.DefaultFee == 1200.00m && t.DefaultDurationMinutes == 60);
            Assert.All(snapshot.Dentists, d => Assert.Contains(newVeneer!.Id, d.SupportedTreatmentIds));

            // 3. SoapMacroDto remains purely clinical (no DefaultFee property)
            Assert.Null(typeof(SoapMacroDto).GetProperty("DefaultFee"));
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task HttpPipeline_WalkInOtcSale_BillingSettings_AndOverpaymentCredit_WorkEndToEnd()
    {
        await using var app = OcclusaApiApplicationFactory.BuildApplication(
            urls: "http://127.0.0.1:0",
            environmentName: "Development");
        await app.StartAsync();

        try
        {
            var baseAddress = app.Urls.First();
            using var http = new HttpClient { BaseAddress = new Uri(baseAddress) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Occlusa-User", "Siti (Reception)");
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-Occlusa-Allowed-Branches", "HQ-KL,PJ-SS2");

            var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };
            jsonOpts.Converters.Add(new JsonStringEnumConverter());

            // 1. Add a custom OTC item in Billing Settings
            var catResp = await http.PostAsJsonAsync("/api/billing-settings/catalog", new UpsertBillingCatalogItemRequest
            {
                Name = "Orthodontic Wax Pack",
                Category = BillingCatalogCategory.OtcProduct,
                UnitPrice = 8.50m,
                IsCommissionable = true, // API must enforce false for OtcProduct
                NoteKeywords = "ortho wax, relief wax"
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.OK, catResp.StatusCode);
            var createdItem = await catResp.Content.ReadFromJsonAsync<BillingCatalogItemDto>(jsonOpts);
            Assert.NotNull(createdItem);
            Assert.False(createdItem!.IsCommissionable);

            // 2. Walk-In customer buys toothbrush + ortho wax at front counter (no patient profile needed)
            var draftResp = await http.PostAsJsonAsync("/api/invoices/draft", new SaveInvoiceDraftRequest
            {
                BranchId = "HQ-KL",
                IsWalkInRetail = true,
                PatientName = "Walk-In Toothbrush Buyer",
                Lines =
                [
                    new InvoiceLineDto
                    {
                        CatalogItemId = createdItem.Id,
                        ProcedureName = createdItem.Name,
                        Category = BillingCatalogCategory.OtcProduct,
                        UnitPrice = 8.50m,
                        Quantity = 2
                    }
                ]
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.OK, draftResp.StatusCode);
            var retailDraft = await draftResp.Content.ReadFromJsonAsync<InvoiceDto>(jsonOpts);
            Assert.True(retailDraft!.IsWalkInRetail);
            Assert.Equal(17.00m, retailDraft.PatientPayableAmount);
            Assert.Equal(0.00m, retailDraft.DoctorCommissionAmount);

            // Settle RM 17.00 bill with RM 20.00 Cash note (Return Cash Change RM 3.00)
            var payResp = await http.PostAsJsonAsync($"/api/invoices/{retailDraft.Id}/pay", new ApplyPaymentRequest
            {
                OverpaymentHandling = OverpaymentHandling.ReturnCashChange,
                Payments = [new PaymentSplitDto { Method = PaymentMethod.Cash, Amount = 20.00m }]
            }, jsonOpts);
            Assert.Equal(HttpStatusCode.OK, payResp.StatusCode);
            var settledRetail = await payResp.Content.ReadFromJsonAsync<InvoiceDto>(jsonOpts);
            Assert.Equal(17.00m, settledRetail!.PaidAmount);
            Assert.Equal(0.00m, settledRetail.BalanceDue);
            Assert.Equal(0.00m, settledRetail.DoctorCommissionAmount);
        }
        finally
        {
            await app.StopAsync();
        }
    }
}

