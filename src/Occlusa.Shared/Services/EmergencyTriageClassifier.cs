using Occlusa.Shared.Models;

namespace Occlusa.Shared.Services;

/// <summary>
/// Result of triaging an inbound patient WhatsApp message against Occlusa safety and booking rules.
/// </summary>
public class TriageResult
{
    public TriageClassification Classification { get; init; }
    public bool IsEmergency => Classification == TriageClassification.EmergencyRedFlag;
    public bool IsComplexProcedureBlocked => Classification == TriageClassification.ComplexProcedureBlocked;
    public bool IsRoutineBookingEligible => Classification == TriageClassification.RoutineBookingEligible;
    public IReadOnlyList<string> MatchedKeywords { get; init; } = Array.Empty<string>();
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Deterministic triage classifier enforcing emergency escalation and surgical/complex procedure guardrails.
/// </summary>
public static class EmergencyTriageClassifier
{
    private static readonly string[] EmergencyKeywords =
    {
        "severe pain",
        "unbearable pain",
        "excruciating",
        "throbbing pain",
        "terrible pain",
        "can't sleep",
        "cannot sleep",
        "facial swelling",
        "face swollen",
        "swollen face",
        "cheek swollen",
        "swollen cheek",
        "jaw swollen",
        "eye swollen",
        "uncontrolled bleeding",
        "won't stop bleeding",
        "wont stop bleeding",
        "bleeding non-stop",
        "bleeding nonstop",
        "heavy bleeding",
        "knocked out",
        "knocked-out",
        "tooth fell out",
        "avulsed",
        "broken jaw",
        "facial trauma",
        "trauma",
        "difficulty breathing",
        "hard to breathe",
        "trouble breathing",
        "difficulty swallowing",
        "hard to swallow",
        "can't swallow",
        "cannot swallow",
        "sakit teruk",
        "sakit sangat",
        "tak tahan sakit",
        "muka bengkak",
        "bengkak muka",
        "pipi bengkak",
        "darah tak berhenti",
        "pendarahan teruk",
        "gigi tercabut",
        "susah bernafas",
        "susah menelan"
    };

    private static readonly string[] ComplexProcedureKeywords =
    {
        "implant",
        "wisdom tooth surgery",
        "wisdom tooth extraction",
        "impacted wisdom",
        "surgical extraction",
        "full-mouth rehab",
        "full mouth rehab",
        "full-mouth rehabilitation",
        "full mouth rehabilitation",
        "braces placement",
        "braces",
        "sedation",
        "general anesthesia",
        "general anaesthesia",
        "oral surgery",
        "bone graft",
        "sinus lift",
        "tanam gigi",
        "pendakap gigi"
    };

    private static readonly string[] BookingIntentKeywords =
    {
        "book",
        "booking",
        "appointment",
        "schedule",
        "slot",
        "reserve",
        "reschedule",
        "available",
        "can i come",
        "want to come",
        "temujanji",
        "nak datang"
    };

    private static readonly string[] RoutineProcedureKeywords =
    {
        "consultation",
        "checkup",
        "check-up",
        "check up",
        "scaling",
        "polishing",
        "cleaning",
        "simple filling",
        "filling",
        "cuci gigi",
        "tampal"
    };

    /// <summary>
    /// Classifies a WhatsApp message into EmergencyRedFlag, ComplexProcedureBlocked,
    /// RoutineBookingEligible, or RoutineInquiry.
    /// </summary>
    public static TriageResult Classify(string? messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return new TriageResult
            {
                Classification = TriageClassification.RoutineInquiry,
                Reason = "Empty or non-actionable inquiry."
            };
        }

        var normalized = messageText.Trim().ToLowerInvariant();

        // Priority 1: Emergency red flags always halt AI booking and trigger staff escalation.
        var emergencyMatches = FindMatches(normalized, EmergencyKeywords);
        if (emergencyMatches.Count > 0)
        {
            return new TriageResult
            {
                Classification = TriageClassification.EmergencyRedFlag,
                MatchedKeywords = emergencyMatches,
                Reason = $"Emergency red-flag symptom detected ({string.Join(", ", emergencyMatches)}). Immediate human staff takeover required."
            };
        }

        // Priority 2: Complex/surgical procedures cannot be self-booked by AI.
        var complexMatches = FindMatches(normalized, ComplexProcedureKeywords);
        if (complexMatches.Count > 0)
        {
            return new TriageResult
            {
                Classification = TriageClassification.ComplexProcedureBlocked,
                MatchedKeywords = complexMatches,
                Reason = $"Complex or surgical procedure ({string.Join(", ", complexMatches)}) requires human front-desk callback."
            };
        }

        // Priority 3: Routine booking requests.
        var bookingMatches = FindMatches(normalized, BookingIntentKeywords);
        var routineMatches = FindMatches(normalized, RoutineProcedureKeywords);
        if (bookingMatches.Count > 0)
        {
            return new TriageResult
            {
                Classification = TriageClassification.RoutineBookingEligible,
                MatchedKeywords = bookingMatches.Concat(routineMatches).Distinct().ToList(),
                Reason = "Routine appointment booking intent detected within AI receptionist scope."
            };
        }

        // Priority 4: General clinic hours, location, and routine pricing inquiries.
        return new TriageResult
        {
            Classification = TriageClassification.RoutineInquiry,
            MatchedKeywords = routineMatches,
            Reason = "General routine inquiry (hours, location, or standard pricing)."
        };
    }

    private static List<string> FindMatches(string normalizedText, string[] keywords)
    {
        var matches = new List<string>();
        foreach (var keyword in keywords)
        {
            if (normalizedText.Contains(keyword, StringComparison.Ordinal))
            {
                matches.Add(keyword);
            }
        }

        return matches;
    }
}
