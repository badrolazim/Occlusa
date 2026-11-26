using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Api.Repositories;

/// <summary>
/// Shared domain guards and FDI tooth catalog used by both In-Memory and Supabase PostgreSQL repositories.
/// </summary>
public static class ClinicDomainHelpers
{
    public static readonly int[] AllFdiTeeth = OdontogramHelper.AllFdiTeeth;

    public static bool IsValidFdiTooth(int toothNumber) => OdontogramHelper.IsValidFdiTooth(toothNumber);

    private static readonly Dictionary<QueueStatus, HashSet<QueueStatus>> AllowedTransitions = new()
    {
        [QueueStatus.AiBookedPending] = new()
        {
            QueueStatus.Scheduled,
            QueueStatus.Waiting,
            QueueStatus.Cancelled
        },
        [QueueStatus.Scheduled] = new()
        {
            QueueStatus.Waiting,
            QueueStatus.InChair,
            QueueStatus.Cancelled
        },
        [QueueStatus.Waiting] = new()
        {
            QueueStatus.InChair,
            QueueStatus.Scheduled,
            QueueStatus.Cancelled
        },
        [QueueStatus.InChair] = new()
        {
            QueueStatus.AwaitingCheckout,
            QueueStatus.Waiting,
            QueueStatus.Completed,
            QueueStatus.Cancelled
        },
        [QueueStatus.AwaitingCheckout] = new()
        {
            QueueStatus.Completed,
            QueueStatus.InChair,
            QueueStatus.Cancelled
        },
        [QueueStatus.Completed] = new(),
        [QueueStatus.Cancelled] = new()
    };

    public static bool IsTransitionAllowed(QueueStatus current, QueueStatus target)
    {
        var normalizedCurrent = AppointmentCalendarHelper.NormalizeQueueStatus(current);
        var normalizedTarget = AppointmentCalendarHelper.NormalizeQueueStatus(target);

        if (normalizedCurrent == normalizedTarget)
        {
            return true;
        }

        return AllowedTransitions.TryGetValue(normalizedCurrent, out var allowed) && allowed.Contains(normalizedTarget);
    }

    /// <summary>
    /// Updates status milestone timestamps, staff attribution fields, and activity trail uniformly.
    /// </summary>
    public static void ApplyStatusMilestone(
        AppointmentQueueItemDto item,
        QueueStatus oldStatus,
        QueueStatus newStatus,
        string actor,
        DateTimeOffset now,
        string? roomDetails = null)
    {
        var normalizedOld = AppointmentCalendarHelper.NormalizeQueueStatus(oldStatus);
        var normalizedNew = AppointmentCalendarHelper.NormalizeQueueStatus(newStatus);
        var staff = string.IsNullOrWhiteSpace(actor) ? "Desktop Staff" : actor.Trim();

        item.Status = normalizedNew;
        item.LastUpdatedBy = staff;
        item.LastUpdatedAt = now;

        if (normalizedOld == normalizedNew)
        {
            item.ActivityLog.Add(new VisitActivityEntryDto
            {
                Timestamp = now,
                Action = "Queue Updated",
                StaffName = staff,
                Details = string.IsNullOrWhiteSpace(roomDetails) ? item.RoomName : roomDetails.Trim()
            });
            return;
        }

        switch (normalizedNew)
        {
            case QueueStatus.Waiting:
                if (normalizedOld == QueueStatus.InChair)
                {
                    item.ArrivedAt ??= now;
                    item.ActivityLog.Add(new VisitActivityEntryDto
                    {
                        Timestamp = now,
                        Action = "Returned to Waiting",
                        StaffName = staff,
                        Details = item.RoomName
                    });
                }
                else
                {
                    item.ArrivedAt ??= now;
                    item.CheckedInBy = staff;
                    item.ActivityLog.Add(new VisitActivityEntryDto
                    {
                        Timestamp = now,
                        Action = "Checked In (Waiting)",
                        StaffName = staff,
                        Details = $"{item.DoctorName} · {item.RoomName}"
                    });
                }
                break;

            case QueueStatus.Scheduled:
                if (normalizedOld == QueueStatus.Waiting)
                {
                    item.ArrivedAt = null;
                    item.CheckedInBy = string.Empty;
                    item.ActivityLog.Add(new VisitActivityEntryDto
                    {
                        Timestamp = now,
                        Action = "Check-In Reverted",
                        StaffName = staff,
                        Details = "Returned to Scheduled"
                    });
                }
                else if (normalizedOld == QueueStatus.AiBookedPending)
                {
                    item.ActivityLog.Add(new VisitActivityEntryDto
                    {
                        Timestamp = now,
                        Action = "AI Booking Approved",
                        StaffName = staff,
                        Details = $"{item.DoctorName} · {item.RoomName}"
                    });
                }
                break;

            case QueueStatus.InChair:
                item.ArrivedAt ??= now;
                if (string.IsNullOrWhiteSpace(item.CheckedInBy))
                {
                    item.CheckedInBy = staff;
                }
                item.SeatedAt = now;
                item.SeatedBy = staff;
                item.ActivityLog.Add(new VisitActivityEntryDto
                {
                    Timestamp = now,
                    Action = "Called to Chair",
                    StaffName = staff,
                    Details = !string.IsNullOrWhiteSpace(roomDetails) ? roomDetails.Trim() : item.RoomName
                });
                break;

            case QueueStatus.AwaitingCheckout:
                item.CheckoutAt = now;
                item.CheckoutBy = staff;
                item.ActivityLog.Add(new VisitActivityEntryDto
                {
                    Timestamp = now,
                    Action = "Sent to Checkout",
                    StaffName = staff,
                    Details = item.RoomName
                });
                break;

            case QueueStatus.Completed:
                item.CompletedAt = now;
                item.CompletedBy = staff;
                item.ActivityLog.Add(new VisitActivityEntryDto
                {
                    Timestamp = now,
                    Action = "Completed",
                    StaffName = staff,
                    Details = item.ProcedureSummary
                });
                break;

            case QueueStatus.Cancelled:
                item.ActivityLog.Add(new VisitActivityEntryDto
                {
                    Timestamp = now,
                    Action = "Cancelled",
                    StaffName = staff,
                    Details = item.ProcedureSummary
                });
                break;
        }
    }

    public static string NormalizeBranch(string? branchId, string defaultBranch = "HQ-KL") =>
        string.IsNullOrWhiteSpace(branchId) ? defaultBranch : branchId.Trim().ToUpperInvariant();

    public static string BuildCompletedToothProcedureLabel(UpdateOdontogramRequest request)
    {
        var baseName = string.IsNullOrWhiteSpace(request.ProcedureName)
            ? request.Condition.ToString()
            : request.ProcedureName.Trim();

        if (baseName.Contains($"#{request.ToothNumber}", StringComparison.OrdinalIgnoreCase)
            || baseName.Contains($"Tooth {request.ToothNumber}", StringComparison.OrdinalIgnoreCase))
        {
            return baseName;
        }

        var surfaces = OdontogramHelper.NormalizeSurfaces(request.Surfaces);
        return string.IsNullOrEmpty(surfaces)
            ? $"Tooth #{request.ToothNumber} - {baseName}"
            : $"Tooth #{request.ToothNumber} ({surfaces}) - {baseName}";
    }

    public static PatientDto Clone(PatientDto p) => new()
    {
        Id = p.Id,
        FullName = p.FullName,
        IcNumber = p.IcNumber,
        DateOfBirth = p.DateOfBirth,
        Gender = p.Gender,
        Phone = p.Phone,
        Address = p.Address,
        PrimaryBranchId = p.PrimaryBranchId,
        MedicalAlerts = new MedicalAlertDto
        {
            Allergies = p.MedicalAlerts.Allergies,
            OnAnticoagulants = p.MedicalAlerts.OnAnticoagulants,
            HasDiabetes = p.MedicalAlerts.HasDiabetes,
            IsPregnant = p.MedicalAlerts.IsPregnant,
            Notes = p.MedicalAlerts.Notes
        },
        IsMyKadVerified = p.IsMyKadVerified,
        ConsentSignedAt = p.ConsentSignedAt,
        CreditBalance = p.CreditBalance,
        VisitCount = p.VisitCount,
        LastVisitAt = p.LastVisitAt
    };

    public static DentistDto Clone(DentistDto d) => new()
    {
        Id = d.Id,
        FullName = d.FullName,
        Specialty = d.Specialty,
        DefaultRoom = d.DefaultRoom,
        BranchIds = d.BranchIds.ToList(),
        SupportedTreatmentIds = d.SupportedTreatmentIds.ToList(),
        IsActive = d.IsActive
    };

    public static TreatmentDto Clone(TreatmentDto t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Category = t.Category,
        DefaultDurationMinutes = t.DefaultDurationMinutes,
        DefaultFee = t.DefaultFee,
        IsActive = t.IsActive
    };

    public static AppointmentQueueItemDto Clone(AppointmentQueueItemDto a) => new()
    {
        Id = a.Id,
        BranchId = a.BranchId,
        PatientId = a.PatientId,
        PatientName = a.PatientName,
        PatientIc = a.PatientIc,
        DentistId = a.DentistId,
        DoctorName = a.DoctorName,
        TreatmentId = a.TreatmentId,
        RoomName = a.RoomName,
        ScheduledAt = a.ScheduledAt,
        DurationMinutes = a.DurationMinutes,
        ProcedureSummary = a.ProcedureSummary,
        Notes = a.Notes,
        Status = a.Status,
        VisitType = a.VisitType,
        ArrivedAt = a.ArrivedAt,
        SeatedAt = a.SeatedAt,
        CheckoutAt = a.CheckoutAt,
        CompletedAt = a.CompletedAt,
        CreatedBy = a.CreatedBy,
        CheckedInBy = a.CheckedInBy,
        SeatedBy = a.SeatedBy,
        CheckoutBy = a.CheckoutBy,
        CompletedBy = a.CompletedBy,
        LastUpdatedBy = a.LastUpdatedBy,
        LastUpdatedAt = a.LastUpdatedAt,
        ActivityLog = a.ActivityLog.Select(e => new VisitActivityEntryDto
        {
            Timestamp = e.Timestamp,
            Action = e.Action,
            StaffName = e.StaffName,
            Details = e.Details
        }).ToList(),
        HasCriticalMedicalAlert = a.HasCriticalMedicalAlert,
        AlertSummary = a.AlertSummary,
        VisitId = a.VisitId
    };

    public static OdontogramToothDto Clone(OdontogramToothDto t) => new()
    {
        Id = t.Id,
        VisitId = t.VisitId,
        PatientId = t.PatientId,
        ToothNumber = t.ToothNumber,
        Condition = t.Condition,
        Surfaces = t.Surfaces,
        ProcedureName = t.ProcedureName,
        Fee = t.Fee,
        Stage = t.Stage,
        DoctorName = t.DoctorName,
        UpdatedAt = t.UpdatedAt
    };

    public static PatientVisitDto Clone(PatientVisitDto v) => new()
    {
        Id = v.Id,
        PatientId = v.PatientId,
        PatientName = v.PatientName,
        PatientIc = v.PatientIc,
        AppointmentId = v.AppointmentId,
        InvoiceId = v.InvoiceId,
        BranchId = v.BranchId,
        DentistId = v.DentistId,
        DoctorName = v.DoctorName,
        VisitNumber = v.VisitNumber,
        VisitDate = v.VisitDate,
        Subjective = v.Subjective,
        Objective = v.Objective,
        Assessment = v.Assessment,
        Plan = v.Plan,
        AnesthesiaUsed = v.AnesthesiaUsed,
        ToothShade = v.ToothShade,
        PostOpInstructions = v.PostOpInstructions,
        CreatedBy = v.CreatedBy,
        LastUpdatedBy = v.LastUpdatedBy,
        CreatedAt = v.CreatedAt,
        UpdatedAt = v.UpdatedAt,
        Odontogram = v.Odontogram.Select(Clone).ToList()
    };

    public static SoapNoteDto Clone(SoapNoteDto s) => new()
    {
        Id = s.Id,
        PatientId = s.PatientId,
        BranchId = s.BranchId,
        DoctorName = s.DoctorName,
        VisitDate = s.VisitDate,
        Subjective = s.Subjective,
        Objective = s.Objective,
        Assessment = s.Assessment,
        Plan = s.Plan,
        AnesthesiaUsed = s.AnesthesiaUsed,
        ToothShade = s.ToothShade,
        PostOpInstructions = s.PostOpInstructions
    };

    public static InvoiceDto Clone(InvoiceDto inv) => new()
    {
        Id = inv.Id,
        InvoiceNumber = inv.InvoiceNumber,
        BranchId = inv.BranchId,
        PatientId = inv.PatientId,
        IsWalkInRetail = inv.IsWalkInRetail,
        PatientName = inv.PatientName,
        DoctorName = inv.DoctorName,
        AppointmentId = inv.AppointmentId,
        VisitId = inv.VisitId,
        Lines = inv.Lines.Select(l => new InvoiceLineDto
        {
            Id = l.Id,
            CatalogItemId = l.CatalogItemId,
            ToothNumber = l.ToothNumber,
            ProcedureName = l.ProcedureName,
            Category = l.Category,
            IsCommissionable = l.IsCommissionable,
            UnitPrice = l.UnitPrice,
            Quantity = l.Quantity,
            LineTotal = l.LineTotal,
            SourceStage = l.SourceStage
        }).ToList(),
        Subtotal = inv.Subtotal,
        DiscountAmount = inv.DiscountAmount,
        PanelCoveredAmount = inv.PanelCoveredAmount,
        PanelName = inv.PanelName,
        PatientPayableAmount = inv.PatientPayableAmount,
        PaidAmount = inv.PaidAmount,
        CreditedOverpaymentAmount = inv.CreditedOverpaymentAmount,
        DoctorCommissionAmount = inv.DoctorCommissionAmount,
        Payments = inv.Payments.Select(p => new PaymentSplitDto
        {
            Method = p.Method,
            Amount = p.Amount,
            ReferenceNo = p.ReferenceNo,
            PanelName = p.PanelName
        }).ToList(),
        IsVoided = inv.IsVoided,
        VoidReason = inv.VoidReason,
        CreatedAt = inv.CreatedAt
    };

    public static BillingCatalogItemDto Clone(BillingCatalogItemDto c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Category = c.Category,
        ClinicalSubcategory = c.ClinicalSubcategory,
        UnitPrice = c.UnitPrice,
        IsCommissionable = c.Category != BillingCatalogCategory.OtcProduct && c.IsCommissionable,
        IsBookableAppointment = c.Category is BillingCatalogCategory.Procedure or BillingCatalogCategory.Radiograph && c.IsBookableAppointment,
        DefaultDurationMinutes = Math.Clamp(c.DefaultDurationMinutes > 0 ? c.DefaultDurationMinutes : 30, 15, 480),
        ShowInOdontogramPresets = c.ShowInOdontogramPresets,
        DefaultToothCondition = c.DefaultToothCondition,
        NoteKeywords = c.NoteKeywords,
        SafetyAlertTags = c.SafetyAlertTags,
        IsActive = c.IsActive
    };

    public static TreatmentSuggestionRuleDto Clone(TreatmentSuggestionRuleDto r) => new()
    {
        TreatmentId = r.TreatmentId,
        TreatmentName = r.TreatmentName,
        SuggestedCatalogItemIds = r.SuggestedCatalogItemIds.ToList()
    };

    public static DoctorInvoicePreferenceDto Clone(DoctorInvoicePreferenceDto p) => new()
    {
        DentistId = p.DentistId,
        DoctorName = p.DoctorName,
        InitMode = p.InitMode,
        EnableNoteKeywordEngine = p.EnableNoteKeywordEngine,
        TreatmentRules = p.TreatmentRules.Select(Clone).ToList()
    };

    public static BillingSettingsSnapshotDto Clone(BillingSettingsSnapshotDto s) => new()
    {
        CatalogItems = s.CatalogItems.Select(Clone).ToList(),
        DoctorPreferences = s.DoctorPreferences.Select(Clone).ToList(),
        CorporatePanels = s.CorporatePanels.ToList()
    };

    public static PatientCreditEntryDto Clone(PatientCreditEntryDto e) => new()
    {
        Id = e.Id,
        PatientId = e.PatientId,
        BranchId = e.BranchId,
        InvoiceId = e.InvoiceId,
        InvoiceNumber = e.InvoiceNumber,
        AmountDelta = e.AmountDelta,
        BalanceAfter = e.BalanceAfter,
        EntryType = e.EntryType,
        Notes = e.Notes,
        RecordedBy = e.RecordedBy,
        CreatedAt = e.CreatedAt
    };

    /// <summary>
    /// Creates the single unified Master Catalogue powering Appointment Booking, Odontogram Presets,
    /// Doctor Suggestion Rules, Invoice Checkout, Walk-In OTC Retail Sales, and WhatsApp AI quotes.
    /// </summary>
    public static BillingSettingsSnapshotDto CreateDefaultBillingSettings(IReadOnlyList<DentistDto> dentists)
    {
        // Canonical Bookable Clinical Treatments (60000000-... IDs shared directly with Appointments & Dentists)
        var cConsult = new BillingCatalogItemDto
        {
            Id = Guid.Parse("60000000-0000-0000-0000-000000000001"),
            Name = "Dental Consultation & Checkup",
            Category = BillingCatalogCategory.Procedure,
            ClinicalSubcategory = "Diagnostic",
            UnitPrice = 50.00m,
            IsCommissionable = true,
            IsBookableAppointment = true,
            DefaultDurationMinutes = 30,
            ShowInOdontogramPresets = false,
            DefaultToothCondition = ToothCondition.Healthy,
            NoteKeywords = "consultation, checkup, routine exam, dental assessment"
        };
        var cScaling = new BillingCatalogItemDto
        {
            Id = Guid.Parse("60000000-0000-0000-0000-000000000002"),
            Name = "Routine Scaling & Polishing",
            Category = BillingCatalogCategory.Procedure,
            ClinicalSubcategory = "Preventive",
            UnitPrice = 150.00m,
            IsCommissionable = true,
            IsBookableAppointment = true,
            DefaultDurationMinutes = 30,
            ShowInOdontogramPresets = true,
            DefaultToothCondition = ToothCondition.Healthy,
            NoteKeywords = "scaling, polishing, ultrasonic scaling, s&p, prophy"
        };
        var cFilling = new BillingCatalogItemDto
        {
            Id = Guid.Parse("60000000-0000-0000-0000-000000000003"),
            Name = "Posterior Composite Filling",
            Category = BillingCatalogCategory.Procedure,
            ClinicalSubcategory = "Restorative",
            UnitPrice = 200.00m,
            IsCommissionable = true,
            IsBookableAppointment = true,
            DefaultDurationMinutes = 45,
            ShowInOdontogramPresets = true,
            DefaultToothCondition = ToothCondition.Filling,
            NoteKeywords = "composite, filling, restoration, tampal, gic"
        };
        var cSimpleExo = new BillingCatalogItemDto
        {
            Id = Guid.Parse("60000000-0000-0000-0000-000000000004"),
            Name = "Simple Tooth Extraction",
            Category = BillingCatalogCategory.Procedure,
            ClinicalSubcategory = "Oral Surgery",
            UnitPrice = 180.00m,
            IsCommissionable = true,
            IsBookableAppointment = true,
            DefaultDurationMinutes = 30,
            ShowInOdontogramPresets = true,
            DefaultToothCondition = ToothCondition.ExtractionNeeded,
            NoteKeywords = "extraction, forceps, exo, cabut, elevation"
        };
        var cRct = new BillingCatalogItemDto
        {
            Id = Guid.Parse("60000000-0000-0000-0000-000000000005"),
            Name = "Root Canal Therapy (Stage 1)",
            Category = BillingCatalogCategory.Procedure,
            ClinicalSubcategory = "Endodontics",
            UnitPrice = 450.00m,
            IsCommissionable = true,
            IsBookableAppointment = true,
            DefaultDurationMinutes = 60,
            ShowInOdontogramPresets = true,
            DefaultToothCondition = ToothCondition.RootCanal,
            NoteKeywords = "root canal, rct, pulpitis, pulpectomy, chemo-mechanical"
        };
        var cCrown = new BillingCatalogItemDto
        {
            Id = Guid.Parse("60000000-0000-0000-0000-000000000006"),
            Name = "Crown Preparation & Cementation",
            Category = BillingCatalogCategory.Procedure,
            ClinicalSubcategory = "Prosthodontics",
            UnitPrice = 950.00m,
            IsCommissionable = true,
            IsBookableAppointment = true,
            DefaultDurationMinutes = 60,
            ShowInOdontogramPresets = true,
            DefaultToothCondition = ToothCondition.Crown,
            NoteKeywords = "crown, zirconia, pfm, cementation"
        };
        var cImplant = new BillingCatalogItemDto
        {
            Id = Guid.Parse("60000000-0000-0000-0000-000000000007"),
            Name = "Implant Crown Review",
            Category = BillingCatalogCategory.Procedure,
            ClinicalSubcategory = "Prosthodontics",
            UnitPrice = 350.00m,
            IsCommissionable = true,
            IsBookableAppointment = true,
            DefaultDurationMinutes = 30,
            ShowInOdontogramPresets = true,
            DefaultToothCondition = ToothCondition.Implant,
            NoteKeywords = "implant, abutment, peri-implant"
        };

        // Additional Chairside Procedures (Non-Default Appointment Slots unless toggled Bookable in Settings)
        var cSurgExo = new BillingCatalogItemDto
        {
            Id = Guid.Parse("80000000-0000-0000-0000-000000000005"),
            Name = "Surgical Extraction (MOS)",
            Category = BillingCatalogCategory.Procedure,
            ClinicalSubcategory = "Oral Surgery",
            UnitPrice = 450.00m,
            IsCommissionable = true,
            IsBookableAppointment = false,
            DefaultDurationMinutes = 60,
            ShowInOdontogramPresets = true,
            DefaultToothCondition = ToothCondition.ExtractionNeeded,
            NoteKeywords = "surgical extraction, mos, wisdom tooth, mucoperiosteal flap, odontectomy"
        };
        var cFluoride = new BillingCatalogItemDto
        {
            Id = Guid.Parse("80000000-0000-0000-0000-000000000009"),
            Name = "Topical Fluoride Varnish",
            Category = BillingCatalogCategory.Procedure,
            ClinicalSubcategory = "Preventive",
            UnitPrice = 45.00m,
            IsCommissionable = true,
            IsBookableAppointment = false,
            DefaultDurationMinutes = 15,
            ShowInOdontogramPresets = false,
            NoteKeywords = "fluoride, varnish, desensitizing gel"
        };

        // Radiographs
        var rIopa = new BillingCatalogItemDto
        {
            Id = Guid.Parse("80000000-0000-0000-0000-000000000010"),
            Name = "Periapical X-Ray (IOPA)",
            Category = BillingCatalogCategory.Radiograph,
            ClinicalSubcategory = "Radiology",
            UnitPrice = 40.00m,
            IsCommissionable = true,
            NoteKeywords = "iopa, periapical, pa xray, pre-op pa, radiograph",
            SafetyAlertTags = "Pregnancy"
        };
        var rOpg = new BillingCatalogItemDto
        {
            Id = Guid.Parse("80000000-0000-0000-0000-000000000011"),
            Name = "OPG Panoramic X-Ray",
            Category = BillingCatalogCategory.Radiograph,
            ClinicalSubcategory = "Radiology",
            UnitPrice = 90.00m,
            IsCommissionable = true,
            NoteKeywords = "opg, panoramic, full mouth xray",
            SafetyAlertTags = "Pregnancy"
        };

        // Clinic Medications
        var mPcm = new BillingCatalogItemDto
        {
            Id = Guid.Parse("80000000-0000-0000-0000-000000000012"),
            Name = "Paracetamol 500mg (10 Tabs)",
            Category = BillingCatalogCategory.Medication,
            ClinicalSubcategory = "Pharmacy",
            UnitPrice = 10.00m,
            IsCommissionable = false,
            NoteKeywords = "paracetamol, pcm, panadol, analgesia"
        };
        var mIbuprofen = new BillingCatalogItemDto
        {
            Id = Guid.Parse("80000000-0000-0000-0000-000000000013"),
            Name = "Ibuprofen 400mg Painkiller (10 Tabs)",
            Category = BillingCatalogCategory.Medication,
            ClinicalSubcategory = "Pharmacy",
            UnitPrice = 15.00m,
            IsCommissionable = false,
            NoteKeywords = "ibuprofen, brufen, nsaid, ponstan, mefenamic",
            SafetyAlertTags = "Ibuprofen,NSAID,Pregnancy,Anticoagulants"
        };
        var mAmox = new BillingCatalogItemDto
        {
            Id = Guid.Parse("80000000-0000-0000-0000-000000000014"),
            Name = "Amoxicillin 500mg Antibiotic (15 Caps)",
            Category = BillingCatalogCategory.Medication,
            ClinicalSubcategory = "Pharmacy",
            UnitPrice = 18.00m,
            IsCommissionable = false,
            NoteKeywords = "amoxicillin, amox, augmentin, antibiotic",
            SafetyAlertTags = "Penicillin,Amoxicillin"
        };

        // Over-The-Counter (OTC) Retail Products — strictly 0% Doctor Commission
        var otcBrush = new BillingCatalogItemDto
        {
            Id = Guid.Parse("80000000-0000-0000-0000-000000000015"),
            Name = "Ultra-Soft Bristle Toothbrush",
            Category = BillingCatalogCategory.OtcProduct,
            ClinicalSubcategory = "Oral Care Retail",
            UnitPrice = 12.00m,
            IsCommissionable = false,
            NoteKeywords = "toothbrush, soft brush, berus gigi"
        };
        var otcPaste = new BillingCatalogItemDto
        {
            Id = Guid.Parse("80000000-0000-0000-0000-000000000016"),
            Name = "Fluoride Desensitizing Toothpaste 100g",
            Category = BillingCatalogCategory.OtcProduct,
            ClinicalSubcategory = "Oral Care Retail",
            UnitPrice = 18.00m,
            IsCommissionable = false,
            NoteKeywords = "toothpaste, sensodyne, ubat gigi"
        };
        var otcWash = new BillingCatalogItemDto
        {
            Id = Guid.Parse("80000000-0000-0000-0000-000000000017"),
            Name = "Chlorhexidine 0.12% Mouthwash 250ml",
            Category = BillingCatalogCategory.OtcProduct,
            ClinicalSubcategory = "Oral Care Retail",
            UnitPrice = 25.00m,
            IsCommissionable = false,
            NoteKeywords = "mouthwash, chlorhexidine, oradex, antiseptic rinse"
        };
        var otcFloss = new BillingCatalogItemDto
        {
            Id = Guid.Parse("80000000-0000-0000-0000-000000000018"),
            Name = "Interdental Brush & Superfloss Kit",
            Category = BillingCatalogCategory.OtcProduct,
            ClinicalSubcategory = "Oral Care Retail",
            UnitPrice = 16.00m,
            IsCommissionable = false,
            NoteKeywords = "superfloss, interdental, floss"
        };

        static TreatmentSuggestionRuleDto Rule(BillingCatalogItemDto primary, params BillingCatalogItemDto[] companions) =>
            new()
            {
                TreatmentId = primary.Id,
                TreatmentName = primary.Name,
                SuggestedCatalogItemIds = companions.Select(c => c.Id).ToList()
            };

        var defaultRules = new List<TreatmentSuggestionRuleDto>
        {
            Rule(cConsult, rIopa, rOpg),
            Rule(cScaling, cFluoride, otcBrush, otcPaste),
            Rule(cFilling, rIopa, otcPaste),
            Rule(cSimpleExo, mPcm, mIbuprofen, mAmox, rIopa, otcWash),
            Rule(cSurgExo, mPcm, mIbuprofen, mAmox, rOpg, otcWash),
            Rule(cRct, rIopa, mPcm, mIbuprofen, mAmox),
            Rule(cCrown, rIopa, otcFloss),
            Rule(cImplant, rIopa, otcFloss, otcWash)
        };

        var prefs = new List<DoctorInvoicePreferenceDto>
        {
            new()
            {
                DentistId = null,
                DoctorName = "Clinic Default (All Doctors)",
                InitMode = DoctorInvoiceInitMode.AutoPopulateWithSuggestions,
                EnableNoteKeywordEngine = true,
                TreatmentRules = defaultRules
            }
        };

        foreach (var doc in dentists)
        {
            var docMode = doc.FullName.Contains("Priya", StringComparison.OrdinalIgnoreCase)
                ? DoctorInvoiceInitMode.BlankWithSuggestions
                : DoctorInvoiceInitMode.AutoPopulateWithSuggestions;

            prefs.Add(new DoctorInvoicePreferenceDto
            {
                DentistId = doc.Id,
                DoctorName = doc.FullName,
                InitMode = docMode,
                EnableNoteKeywordEngine = true,
                TreatmentRules = new List<TreatmentSuggestionRuleDto>()
            });
        }

        return new BillingSettingsSnapshotDto
        {
            CatalogItems =
            [
                cConsult, cScaling, cFilling, cSimpleExo, cSurgExo, cRct, cCrown, cImplant, cFluoride,
                rIopa, rOpg,
                mPcm, mIbuprofen, mAmox,
                otcBrush, otcPaste, otcWash, otcFloss
            ],
            DoctorPreferences = prefs
        };
    }

    public static WhatsAppThreadDto Clone(WhatsAppThreadDto th) => new()
    {
        ThreadId = th.ThreadId,
        PatientPhone = th.PatientPhone,
        PatientName = th.PatientName,
        IsEmergencyEscalated = th.IsEmergencyEscalated,
        EmergencyReason = th.EmergencyReason,
        LastUpdatedAt = th.LastUpdatedAt,
        Messages = th.Messages.Select(m => new WhatsAppMessageDto
        {
            Id = m.Id,
            Sender = m.Sender,
            Text = m.Text,
            Timestamp = m.Timestamp,
            CreatedBookingId = m.CreatedBookingId
        }).ToList()
    };
}
