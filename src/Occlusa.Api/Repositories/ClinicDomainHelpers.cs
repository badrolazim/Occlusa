using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Api.Repositories;

/// <summary>
/// Shared domain guards and FDI tooth catalog used by both In-Memory and Supabase PostgreSQL repositories.
/// </summary>
public static class ClinicDomainHelpers
{
    public static readonly int[] AllFdiTeeth =
    {
        18, 17, 16, 15, 14, 13, 12, 11,
        21, 22, 23, 24, 25, 26, 27, 28,
        48, 47, 46, 45, 44, 43, 42, 41,
        31, 32, 33, 34, 35, 36, 37, 38
    };

    private static readonly HashSet<int> ValidFdiToothSet = new(AllFdiTeeth);

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

    public static bool IsValidFdiTooth(int toothNumber) => ValidFdiToothSet.Contains(toothNumber);

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

        return baseName.Contains($"#{request.ToothNumber}", StringComparison.OrdinalIgnoreCase)
            || baseName.Contains($"Tooth {request.ToothNumber}", StringComparison.OrdinalIgnoreCase)
            ? baseName
            : $"Tooth #{request.ToothNumber} - {baseName}";
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
        ConsentSignedAt = p.ConsentSignedAt
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
        AlertSummary = a.AlertSummary
    };

    public static OdontogramToothDto Clone(OdontogramToothDto t) => new()
    {
        Id = t.Id,
        PatientId = t.PatientId,
        ToothNumber = t.ToothNumber,
        Condition = t.Condition,
        ProcedureName = t.ProcedureName,
        Fee = t.Fee,
        Stage = t.Stage,
        DoctorName = t.DoctorName,
        UpdatedAt = t.UpdatedAt
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
        PatientName = inv.PatientName,
        DoctorName = inv.DoctorName,
        AppointmentId = inv.AppointmentId,
        Lines = inv.Lines.Select(l => new InvoiceLineDto
        {
            Id = l.Id,
            ToothNumber = l.ToothNumber,
            ProcedureName = l.ProcedureName,
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
