using Occlusa.Shared.Models;

namespace Occlusa.Shared.Services;

/// <summary>
/// Deterministic, offline-ready engine that inspects a patient's Visit Record (completed FDI odontogram
/// procedures + clinical SOAP narrative) and Appointment alongside the single unified Master Catalogue
/// and per-doctor Settings (with Clinic Default inheritance) to seed or suggest invoice items.
/// </summary>
public static class InvoiceSuggestionEngine
{
    /// <summary>
    /// Evaluates clinical context and doctor preferences against the single Master Catalogue to produce
    /// auto-populated draft invoice lines and 1-click companion item suggestions.
    /// </summary>
    public static VisitInvoiceSuggestionResultDto BuildSuggestions(
        PatientVisitDto? visit,
        AppointmentQueueItemDto? appointment,
        PatientDto? patient,
        BillingSettingsSnapshotDto settings,
        Guid? dentistId = null,
        string? doctorName = null,
        bool forceScanNotes = false)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var activeCatalog = settings.CatalogItems
            .Where(c => c.IsActive)
            .ToList();
        var catalogById = activeCatalog.ToDictionary(c => c.Id);

        var effectiveDentistId = dentistId ?? visit?.DentistId ?? appointment?.DentistId;
        var effectiveDoctorName = !string.IsNullOrWhiteSpace(doctorName)
            ? doctorName.Trim()
            : (!string.IsNullOrWhiteSpace(visit?.DoctorName) ? visit!.DoctorName.Trim() : (appointment?.DoctorName?.Trim() ?? string.Empty));

        var clinicDefaultPref = settings.DoctorPreferences.FirstOrDefault(p => !p.DentistId.HasValue)
            ?? new DoctorInvoicePreferenceDto
            {
                DentistId = null,
                DoctorName = "Clinic Default (All Doctors)",
                InitMode = DoctorInvoiceInitMode.AutoPopulateWithSuggestions,
                EnableNoteKeywordEngine = true
            };

        var doctorPref = ResolveDoctorPreference(settings.DoctorPreferences, effectiveDentistId, effectiveDoctorName);
        var activePref = doctorPref ?? clinicDefaultPref;
        var mode = activePref.InitMode;
        bool scanNotes = forceScanNotes || (mode != DoctorInvoiceInitMode.BlankManualOnly && activePref.EnableNoteKeywordEngine);

        var primaryCandidates = new List<(InvoiceLineDto Line, BillingCatalogItemDto? MatchedCatalog, string SourceReason)>();
        var detectedPrimaryCatalogIds = new HashSet<Guid>();

        // 1. Completed procedures on the Visit Odontogram (one-way read, priced strictly from Master Catalogue)
        if (visit is not null)
        {
            foreach (var tooth in visit.Odontogram
                .Where(t => t.Stage == TreatmentStage.Completed && !string.IsNullOrWhiteSpace(t.ProcedureName))
                .OrderBy(t => t.ToothNumber))
            {
                var matchedItem = MatchCatalogItemForProcedure(activeCatalog, tooth.ProcedureName, tooth.Condition);
                var unitPrice = matchedItem?.UnitPrice ?? 0m;
                var surfaces = OdontogramHelper.NormalizeSurfaces(tooth.Surfaces);
                var baseLabel = !string.IsNullOrWhiteSpace(tooth.ProcedureName)
                    ? tooth.ProcedureName.Trim()
                    : (matchedItem?.Name ?? tooth.Condition.ToString());

                string displayLabel = baseLabel.Contains($"#{tooth.ToothNumber}", StringComparison.OrdinalIgnoreCase)
                    ? baseLabel
                    : string.IsNullOrEmpty(surfaces)
                        ? $"Tooth #{tooth.ToothNumber} - {baseLabel}"
                        : $"Tooth #{tooth.ToothNumber} ({surfaces}) - {baseLabel}";

                var category = matchedItem?.Category ?? BillingCatalogCategory.Procedure;
                var isComm = category != BillingCatalogCategory.OtcProduct && (matchedItem?.IsCommissionable ?? true);

                if (matchedItem is not null)
                {
                    detectedPrimaryCatalogIds.Add(matchedItem.Id);
                }

                primaryCandidates.Add((
                    new InvoiceLineDto
                    {
                        Id = Guid.NewGuid(),
                        CatalogItemId = matchedItem?.Id,
                        ToothNumber = tooth.ToothNumber,
                        ProcedureName = displayLabel,
                        Category = category,
                        IsCommissionable = isComm,
                        UnitPrice = BillingCalculator.Round2(unitPrice),
                        Quantity = 1,
                        LineTotal = BillingCalculator.Round2(unitPrice),
                        SourceStage = TreatmentStage.Completed
                    },
                    matchedItem,
                    $"Completed on FDI #{tooth.ToothNumber}"));
            }
        }

        // 2. Scan Doctor's Clinical SOAP Notes against Master Catalogue NoteKeywords
        var noteDetectedItems = new List<(BillingCatalogItemDto Item, string MatchedKeyword)>();
        if (scanNotes && visit is not null)
        {
            string combinedNotes = string.Join(
                " \n ",
                new[]
                {
                    visit.Plan,
                    visit.Assessment,
                    visit.Objective,
                    visit.Subjective,
                    visit.AnesthesiaUsed,
                    visit.PostOpInstructions
                }.Where(s => !string.IsNullOrWhiteSpace(s)));

            if (!string.IsNullOrWhiteSpace(combinedNotes))
            {
                foreach (var item in activeCatalog)
                {
                    if (TryMatchKeyword(combinedNotes, item.NoteKeywords, out var matchedKw))
                    {
                        noteDetectedItems.Add((item, matchedKw));
                        if (item.Category == BillingCatalogCategory.Procedure)
                        {
                            detectedPrimaryCatalogIds.Add(item.Id);
                        }
                    }
                }
            }
        }

        // Promote SOAP-detected primary Procedures when Odontogram had 0 completed procedures
        if (primaryCandidates.Count == 0 && noteDetectedItems.Count > 0)
        {
            foreach (var (procItem, kw) in noteDetectedItems.Where(x => x.Item.Category == BillingCatalogCategory.Procedure))
            {
                primaryCandidates.Add((
                    BuildLineFromCatalogItem(procItem),
                    procItem,
                    $"Dr's Note ('{kw}')"));
            }
        }

        // 3. Fallback to Appointment's booked Master Catalogue item if neither Odontogram nor SOAP notes yielded a procedure
        if (primaryCandidates.Count == 0 && appointment is not null)
        {
            BillingCatalogItemDto? apptCatalogMatch = null;
            if (appointment.TreatmentId.HasValue && catalogById.TryGetValue(appointment.TreatmentId.Value, out var byDirectId))
            {
                apptCatalogMatch = byDirectId;
            }
            else
            {
                apptCatalogMatch = MatchCatalogItemForProcedure(activeCatalog, appointment.ProcedureSummary, ToothCondition.Healthy);
            }

            if (apptCatalogMatch is not null)
            {
                detectedPrimaryCatalogIds.Add(apptCatalogMatch.Id);
                primaryCandidates.Add((
                    BuildLineFromCatalogItem(apptCatalogMatch),
                    apptCatalogMatch,
                    "Booked Appointment"));
            }
        }
        else if (appointment?.TreatmentId is { } bookedId && detectedPrimaryCatalogIds.Count == 0)
        {
            detectedPrimaryCatalogIds.Add(bookedId);
        }

        var result = new VisitInvoiceSuggestionResultDto
        {
            AppliedMode = mode,
            PreferenceSourceLabel = doctorPref is not null ? doctorPref.DoctorName : clinicDefaultPref.DoctorName
        };

        var addedCatalogIds = new HashSet<Guid>();
        var suggestionMap = new Dictionary<Guid, InvoiceSuggestionItemDto>();

        if (mode == DoctorInvoiceInitMode.AutoPopulateWithSuggestions && !forceScanNotes)
        {
            foreach (var candidate in primaryCandidates)
            {
                result.AutoPopulatedLines.Add(candidate.Line);
                if (candidate.MatchedCatalog is not null)
                {
                    addedCatalogIds.Add(candidate.MatchedCatalog.Id);
                }
            }
        }
        else if (mode == DoctorInvoiceInitMode.BlankWithSuggestions || forceScanNotes)
        {
            foreach (var candidate in primaryCandidates)
            {
                if (candidate.MatchedCatalog is { } cat && !suggestionMap.ContainsKey(cat.Id))
                {
                    suggestionMap[cat.Id] = CreateSuggestionDto(cat, patient?.MedicalAlerts, candidate.Line.ToothNumber, candidate.SourceReason);
                }
            }
        }

        if (mode != DoctorInvoiceInitMode.BlankManualOnly || forceScanNotes)
        {
            // Companion rules keyed directly by Master Catalogue Procedure Id
            foreach (var primaryId in detectedPrimaryCatalogIds)
            {
                var (rule, _) = ResolveRuleWithSource(primaryId, doctorPref, clinicDefaultPref);
                if (rule is null)
                {
                    continue;
                }

                string ruleReason = string.IsNullOrWhiteSpace(rule.TreatmentName)
                    ? "Treatment Add-On"
                    : $"For {AppointmentCalendarHelper.FormatCompactTreatment(rule.TreatmentName, 22)}";

                foreach (var suggestedId in rule.SuggestedCatalogItemIds)
                {
                    if (addedCatalogIds.Contains(suggestedId)
                        || suggestionMap.ContainsKey(suggestedId)
                        || !catalogById.TryGetValue(suggestedId, out var catItem))
                    {
                        continue;
                    }

                    suggestionMap[suggestedId] = CreateSuggestionDto(catItem, patient?.MedicalAlerts, null, ruleReason);
                }
            }

            // Note-detected companion items (medications, X-rays, OTC, additional procedures)
            foreach (var (item, kw) in noteDetectedItems)
            {
                if (addedCatalogIds.Contains(item.Id) || suggestionMap.ContainsKey(item.Id))
                {
                    continue;
                }

                suggestionMap[item.Id] = CreateSuggestionDto(item, patient?.MedicalAlerts, null, $"Dr's Note ('{kw}')");
            }
        }

        result.SuggestedAddOns = suggestionMap.Values.ToList();
        return result;
    }

    public static (TreatmentSuggestionRuleDto? Rule, bool IsDoctorOverride) ResolveRuleWithSource(
        Guid catalogProcedureId,
        DoctorInvoicePreferenceDto? doctorPref,
        DoctorInvoicePreferenceDto? clinicDefaultPref)
    {
        var overrideRule = doctorPref?.TreatmentRules.FirstOrDefault(r => r.TreatmentId == catalogProcedureId);
        if (overrideRule is not null)
        {
            return (overrideRule, true);
        }

        var defaultRule = clinicDefaultPref?.TreatmentRules.FirstOrDefault(r => r.TreatmentId == catalogProcedureId);
        return (defaultRule, false);
    }

    public static string EvaluateSafetyWarning(BillingCatalogItemDto item, MedicalAlertDto? alerts)
    {
        if (alerts is null || string.IsNullOrWhiteSpace(item.SafetyAlertTags))
        {
            return string.Empty;
        }

        var tags = SplitTokens(item.SafetyAlertTags);
        var warnings = new List<string>();

        foreach (var tag in tags)
        {
            if (tag.Equals("Pregnancy", StringComparison.OrdinalIgnoreCase) && alerts.IsPregnant)
            {
                warnings.Add("Pregnancy");
            }
            else if (tag.Equals("Anticoagulants", StringComparison.OrdinalIgnoreCase) && alerts.OnAnticoagulants)
            {
                warnings.Add("Anticoagulants");
            }
            else if (!string.IsNullOrWhiteSpace(alerts.Allergies)
                     && (alerts.Allergies.Contains(tag, StringComparison.OrdinalIgnoreCase)
                         || (tag.Equals("NSAID", StringComparison.OrdinalIgnoreCase)
                             && (alerts.Allergies.Contains("Ibuprofen", StringComparison.OrdinalIgnoreCase)
                                 || alerts.Allergies.Contains("Aspirin", StringComparison.OrdinalIgnoreCase)
                                 || alerts.Allergies.Contains("Mefenamic", StringComparison.OrdinalIgnoreCase)
                                 || alerts.Allergies.Contains("Ponstan", StringComparison.OrdinalIgnoreCase)))))
            {
                warnings.Add($"Allergy ({alerts.Allergies.Trim()})");
            }
        }

        return warnings.Count == 0 ? string.Empty : $"Alert: {string.Join(", ", warnings.Distinct())}";
    }

    private static DoctorInvoicePreferenceDto? ResolveDoctorPreference(
        IEnumerable<DoctorInvoicePreferenceDto> prefs,
        Guid? dentistId,
        string doctorName)
    {
        if (dentistId.HasValue)
        {
            var byId = prefs.FirstOrDefault(p => p.DentistId == dentistId.Value);
            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(doctorName))
        {
            return prefs.FirstOrDefault(p =>
                p.DentistId.HasValue
                && string.Equals(p.DoctorName.Trim(), doctorName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static BillingCatalogItemDto? MatchCatalogItemForProcedure(
        IReadOnlyList<BillingCatalogItemDto> catalog,
        string? procedureText,
        ToothCondition condition)
    {
        if (!string.IsNullOrWhiteSpace(procedureText))
        {
            var trimmed = procedureText.Trim();
            var exact = catalog.FirstOrDefault(c => string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }

            var contains = catalog
                .Where(c => c.Category == BillingCatalogCategory.Procedure)
                .FirstOrDefault(c =>
                    trimmed.Contains(c.Name, StringComparison.OrdinalIgnoreCase)
                    || c.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                    || TryMatchKeyword(trimmed, c.NoteKeywords, out _));
            if (contains is not null)
            {
                return contains;
            }
        }

        return condition switch
        {
            ToothCondition.Filling => catalog.FirstOrDefault(c => c.DefaultToothCondition == ToothCondition.Filling && c.ShowInOdontogramPresets),
            ToothCondition.ExtractionNeeded => catalog.FirstOrDefault(c => c.DefaultToothCondition == ToothCondition.ExtractionNeeded && c.ShowInOdontogramPresets),
            ToothCondition.Crown => catalog.FirstOrDefault(c => c.DefaultToothCondition == ToothCondition.Crown && c.ShowInOdontogramPresets),
            ToothCondition.RootCanal => catalog.FirstOrDefault(c => c.DefaultToothCondition == ToothCondition.RootCanal && c.ShowInOdontogramPresets),
            ToothCondition.Implant => catalog.FirstOrDefault(c => c.DefaultToothCondition == ToothCondition.Implant && c.ShowInOdontogramPresets),
            _ => null
        };
    }

    private static InvoiceLineDto BuildLineFromCatalogItem(BillingCatalogItemDto item, int? toothNumber = null)
    {
        bool isComm = item.Category != BillingCatalogCategory.OtcProduct && item.IsCommissionable;
        decimal price = BillingCalculator.Round2(item.UnitPrice);
        return new InvoiceLineDto
        {
            Id = Guid.NewGuid(),
            CatalogItemId = item.Id,
            ToothNumber = toothNumber,
            ProcedureName = toothNumber.HasValue ? $"Tooth #{toothNumber.Value} - {item.Name}" : item.Name,
            Category = item.Category,
            IsCommissionable = isComm,
            UnitPrice = price,
            Quantity = 1,
            LineTotal = price
        };
    }

    private static InvoiceSuggestionItemDto CreateSuggestionDto(
        BillingCatalogItemDto item,
        MedicalAlertDto? alerts,
        int? toothNumber,
        string sourceReason)
    {
        bool isComm = item.Category != BillingCatalogCategory.OtcProduct && item.IsCommissionable;
        return new InvoiceSuggestionItemDto
        {
            CatalogItemId = item.Id,
            Name = toothNumber.HasValue ? $"Tooth #{toothNumber.Value} - {item.Name}" : item.Name,
            Category = item.Category,
            UnitPrice = BillingCalculator.Round2(item.UnitPrice),
            IsCommissionable = isComm,
            ToothNumber = toothNumber,
            SourceReason = sourceReason,
            SafetyWarning = EvaluateSafetyWarning(item, alerts)
        };
    }

    public static bool TryMatchKeyword(string text, string? rawKeywords, out string matchedKeyword)
    {
        matchedKeyword = string.Empty;
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(rawKeywords))
        {
            return false;
        }

        foreach (var keyword in SplitTokens(rawKeywords))
        {
            if (ContainsWholePhrase(text, keyword))
            {
                matchedKeyword = keyword;
                return true;
            }
        }

        return false;
    }

    private static bool ContainsWholePhrase(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(needle))
        {
            return false;
        }

        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            bool startOk = index == 0 || !char.IsLetterOrDigit(haystack[index - 1]);
            int after = index + needle.Length;
            bool endOk = after >= haystack.Length || !char.IsLetterOrDigit(haystack[after]);
            if (startOk && endOk)
            {
                return true;
            }

            index += 1;
        }

        return false;
    }

    private static IEnumerable<string> SplitTokens(string raw) =>
        raw.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0);
}
