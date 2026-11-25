using Occlusa.Shared.Models;

namespace Occlusa.Shared.Services;

/// <summary>
/// Pure domain helper for 52-tooth FDI odontograms (Permanent 11-48 and Primary/Deciduous 51-85),
/// 5-surface (M, O, D, B, L) normalization, baseline carry-forward across patient visits, and
/// zero-ellipsis visit summary formatting.
/// </summary>
public static class OdontogramHelper
{
    public static readonly int[] UpperRightPermanent = [18, 17, 16, 15, 14, 13, 12, 11];
    public static readonly int[] UpperLeftPermanent = [21, 22, 23, 24, 25, 26, 27, 28];
    public static readonly int[] LowerRightPermanent = [48, 47, 46, 45, 44, 43, 42, 41];
    public static readonly int[] LowerLeftPermanent = [31, 32, 33, 34, 35, 36, 37, 38];

    public static readonly int[] UpperRightPrimary = [55, 54, 53, 52, 51];
    public static readonly int[] UpperLeftPrimary = [61, 62, 63, 64, 65];
    public static readonly int[] LowerRightPrimary = [85, 84, 83, 82, 81];
    public static readonly int[] LowerLeftPrimary = [71, 72, 73, 74, 75];

    public static readonly int[] PermanentTeeth =
    [
        .. UpperRightPermanent,
        .. UpperLeftPermanent,
        .. LowerRightPermanent,
        .. LowerLeftPermanent
    ];

    public static readonly int[] PrimaryTeeth =
    [
        .. UpperRightPrimary,
        .. UpperLeftPrimary,
        .. LowerRightPrimary,
        .. LowerLeftPrimary
    ];

    public static readonly int[] AllFdiTeeth =
    [
        .. PermanentTeeth,
        .. PrimaryTeeth
    ];

    private static readonly HashSet<int> ValidToothSet = new(AllFdiTeeth);
    private static readonly HashSet<int> PrimaryToothSet = new(PrimaryTeeth);
    private static readonly char[] CanonicalSurfaceOrder = ['M', 'O', 'D', 'B', 'L'];

    public static bool IsValidFdiTooth(int toothNumber) => ValidToothSet.Contains(toothNumber);

    public static bool IsPrimaryTooth(int toothNumber) => PrimaryToothSet.Contains(toothNumber);

    public static bool IsUpperTooth(int toothNumber)
    {
        int quadrant = toothNumber / 10;
        return quadrant is 1 or 2 or 5 or 6;
    }

    public static bool IsMirroredLeftQuadrant(int toothNumber)
    {
        int quadrant = toothNumber / 10;
        return quadrant is 2 or 3 or 6 or 7;
    }

    /// <summary>
    /// Normalizes tooth surface letters into canonical MODBL order (mapping I->O, F->B, P->L).
    /// </summary>
    public static string NormalizeSurfaces(string? rawSurfaces)
    {
        if (string.IsNullOrWhiteSpace(rawSurfaces))
        {
            return string.Empty;
        }

        var present = new HashSet<char>();
        foreach (char ch in rawSurfaces)
        {
            char upper = char.ToUpperInvariant(ch);
            char mapped = upper switch
            {
                'M' => 'M',
                'O' or 'I' => 'O',
                'D' => 'D',
                'B' or 'F' or 'V' => 'B',
                'L' or 'P' => 'L',
                _ => '\0'
            };

            if (mapped != '\0')
            {
                present.Add(mapped);
            }
        }

        if (present.Count == 0)
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[CanonicalSurfaceOrder.Length];
        int len = 0;
        foreach (char s in CanonicalSurfaceOrder)
        {
            if (present.Contains(s))
            {
                buffer[len++] = s;
            }
        }

        return new string(buffer[..len]);
    }

    /// <summary>
    /// Builds a complete 52-tooth blank FDI odontogram (all Healthy) for a specific visit.
    /// </summary>
    public static List<OdontogramToothDto> BuildBlankOdontogram(
        Guid patientId,
        Guid visitId,
        DateTimeOffset timestamp)
    {
        var list = new List<OdontogramToothDto>(AllFdiTeeth.Length);
        foreach (int toothNum in AllFdiTeeth)
        {
            list.Add(new OdontogramToothDto
            {
                Id = Guid.Empty,
                VisitId = visitId,
                PatientId = patientId,
                ToothNumber = toothNum,
                Condition = ToothCondition.Healthy,
                Surfaces = string.Empty,
                ProcedureName = string.Empty,
                Fee = 0m,
                Stage = TreatmentStage.Proposed,
                DoctorName = string.Empty,
                UpdatedAt = timestamp
            });
        }

        return list;
    }

    /// <summary>
    /// Builds a complete 52-tooth FDI odontogram for a new visit by carrying forward baseline
    /// conditions and pending treatment items from the patient's prior visit without re-billing
    /// already-completed procedures.
    /// </summary>
    public static List<OdontogramToothDto> BuildCarryForwardOdontogram(
        Guid patientId,
        Guid newVisitId,
        IEnumerable<OdontogramToothDto>? priorVisitChart,
        DateTimeOffset timestamp)
    {
        var priorByTooth = new Dictionary<int, OdontogramToothDto>();
        if (priorVisitChart is not null)
        {
            foreach (var entry in priorVisitChart)
            {
                if (IsValidFdiTooth(entry.ToothNumber))
                {
                    priorByTooth[entry.ToothNumber] = entry;
                }
            }
        }

        var result = new List<OdontogramToothDto>(AllFdiTeeth.Length);
        foreach (int toothNum in AllFdiTeeth)
        {
            if (!priorByTooth.TryGetValue(toothNum, out var prior)
                || (prior.Condition == ToothCondition.Healthy && string.IsNullOrWhiteSpace(prior.ProcedureName)))
            {
                result.Add(new OdontogramToothDto
                {
                    Id = Guid.Empty,
                    VisitId = newVisitId,
                    PatientId = patientId,
                    ToothNumber = toothNum,
                    Condition = ToothCondition.Healthy,
                    Surfaces = string.Empty,
                    ProcedureName = string.Empty,
                    Fee = 0m,
                    Stage = TreatmentStage.Proposed,
                    DoctorName = string.Empty,
                    UpdatedAt = timestamp
                });
                continue;
            }

            if (prior.Stage == TreatmentStage.Completed)
            {
                var nextCondition = ResolvePostCompletionCondition(prior);
                var nextSurfaces = nextCondition is ToothCondition.Missing or ToothCondition.Implant or ToothCondition.Healthy
                    ? string.Empty
                    : NormalizeSurfaces(prior.Surfaces);

                result.Add(new OdontogramToothDto
                {
                    Id = Guid.NewGuid(),
                    VisitId = newVisitId,
                    PatientId = patientId,
                    ToothNumber = toothNum,
                    Condition = nextCondition,
                    Surfaces = nextSurfaces,
                    ProcedureName = string.Empty,
                    Fee = 0m,
                    Stage = TreatmentStage.Proposed,
                    DoctorName = prior.DoctorName ?? string.Empty,
                    UpdatedAt = timestamp
                });
            }
            else if (string.IsNullOrWhiteSpace(prior.ProcedureName))
            {
                // Baseline existing condition (e.g. Missing or external restoration)
                result.Add(new OdontogramToothDto
                {
                    Id = Guid.NewGuid(),
                    VisitId = newVisitId,
                    PatientId = patientId,
                    ToothNumber = toothNum,
                    Condition = prior.Condition,
                    Surfaces = NormalizeSurfaces(prior.Surfaces),
                    ProcedureName = string.Empty,
                    Fee = 0m,
                    Stage = TreatmentStage.Proposed,
                    DoctorName = prior.DoctorName ?? string.Empty,
                    UpdatedAt = timestamp
                });
            }
            else
            {
                // Unfinished Proposed / Accepted treatment carries forward to complete in subsequent visit
                result.Add(new OdontogramToothDto
                {
                    Id = Guid.NewGuid(),
                    VisitId = newVisitId,
                    PatientId = patientId,
                    ToothNumber = toothNum,
                    Condition = prior.Condition,
                    Surfaces = NormalizeSurfaces(prior.Surfaces),
                    ProcedureName = prior.ProcedureName.Trim(),
                    Fee = prior.Fee,
                    Stage = prior.Stage,
                    DoctorName = prior.DoctorName ?? string.Empty,
                    UpdatedAt = timestamp
                });
            }
        }

        return result;
    }

    private static ToothCondition ResolvePostCompletionCondition(OdontogramToothDto completedTooth)
    {
        if (completedTooth.Condition == ToothCondition.ExtractionNeeded)
        {
            return ToothCondition.Missing;
        }

        if (completedTooth.Condition == ToothCondition.Caries && !string.IsNullOrWhiteSpace(completedTooth.ProcedureName))
        {
            var proc = completedTooth.ProcedureName;
            if (proc.Contains("Extract", StringComparison.OrdinalIgnoreCase))
            {
                return ToothCondition.Missing;
            }
            if (proc.Contains("Crown", StringComparison.OrdinalIgnoreCase))
            {
                return ToothCondition.Crown;
            }
            if (proc.Contains("Root Canal", StringComparison.OrdinalIgnoreCase) || proc.Contains("RCT", StringComparison.OrdinalIgnoreCase))
            {
                return ToothCondition.RootCanal;
            }
            if (proc.Contains("Implant", StringComparison.OrdinalIgnoreCase))
            {
                return ToothCondition.Implant;
            }

            return ToothCondition.Filling;
        }

        return completedTooth.Condition;
    }

    /// <summary>
    /// Formats a concise single-line visit summary on word boundaries without ellipsis ("...").
    /// </summary>
    public static string FormatCompactVisitSummary(PatientVisitDto visit, int maxLength = 42)
    {
        string raw = !string.IsNullOrWhiteSpace(visit.Assessment)
            ? visit.Assessment
            : !string.IsNullOrWhiteSpace(visit.Subjective)
                ? visit.Subjective
                : !string.IsNullOrWhiteSpace(visit.Plan)
                    ? visit.Plan
                    : "Clinical chart recorded";

        return AppointmentCalendarHelper.FormatCompactReceptionNote(raw, maxLength);
    }
}

