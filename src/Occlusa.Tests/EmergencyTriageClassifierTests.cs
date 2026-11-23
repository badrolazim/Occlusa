using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Tests;

public class EmergencyTriageClassifierTests
{
    [Theory]
    [InlineData("Help! I have severe pain and facial swelling since this morning.")]
    [InlineData("My tooth extraction site has uncontrolled bleeding, won't stop bleeding!")]
    [InlineData("My son fell and had a knocked-out tooth at school!")]
    [InlineData("Swollen face and difficulty swallowing right now.")]
    [InlineData("Doktor, muka bengkak dan darah tak berhenti sejak tadi!")]
    public void Classify_EmergencyRedFlag_HaltsBookingAndTriggersEscalation(string message)
    {
        var result = EmergencyTriageClassifier.Classify(message);

        Assert.Equal(TriageClassification.EmergencyRedFlag, result.Classification);
        Assert.True(result.IsEmergency);
        Assert.False(result.IsRoutineBookingEligible);
        Assert.NotEmpty(result.MatchedKeywords);
    }

    [Fact]
    public void Classify_EmergencyOverridesBookingAndComplexKeywords()
    {
        var result = EmergencyTriageClassifier.Classify(
            "Can I book a dental implant or wisdom tooth surgery tomorrow? I have severe pain and facial swelling.");

        Assert.Equal(TriageClassification.EmergencyRedFlag, result.Classification);
        Assert.True(result.IsEmergency);
    }

    [Theory]
    [InlineData("Hi, I want to book dental implant surgery next week.")]
    [InlineData("Can I schedule a wisdom tooth surgery appointment at KL Sentral?")]
    [InlineData("Looking to book full-mouth rehab consultation.")]
    [InlineData("I need braces placement for my daughter.")]
    [InlineData("Do you have slots for IV sedation extraction?")]
    public void Classify_ComplexProcedures_AreBlockedFromAutoBooking(string message)
    {
        var result = EmergencyTriageClassifier.Classify(message);

        Assert.Equal(TriageClassification.ComplexProcedureBlocked, result.Classification);
        Assert.True(result.IsComplexProcedureBlocked);
        Assert.False(result.IsRoutineBookingEligible);
        Assert.False(result.IsEmergency);
    }

    [Theory]
    [InlineData("Hi, can I book a scaling and polishing slot tomorrow at 2pm?")]
    [InlineData("I would like to schedule an appointment for a routine dental checkup.")]
    [InlineData("Is there an available slot for a simple filling on Saturday morning?")]
    [InlineData("Nak buat temujanji cuci gigi di PJ SS2 esok boleh?")]
    public void Classify_RoutineBookingRequests_AreEligibleForAiPendingBooking(string message)
    {
        var result = EmergencyTriageClassifier.Classify(message);

        Assert.Equal(TriageClassification.RoutineBookingEligible, result.Classification);
        Assert.True(result.IsRoutineBookingEligible);
        Assert.False(result.IsEmergency);
        Assert.False(result.IsComplexProcedureBlocked);
    }

    [Theory]
    [InlineData("What are your clinic opening hours and location in KL?")]
    [InlineData("How much is scaling and polishing?")]
    [InlineData("")]
    [InlineData(null)]
    public void Classify_GeneralQuestionsOrEmpty_ReturnRoutineInquiry(string? message)
    {
        var result = EmergencyTriageClassifier.Classify(message);

        Assert.Equal(TriageClassification.RoutineInquiry, result.Classification);
        Assert.False(result.IsEmergency);
        Assert.False(result.IsComplexProcedureBlocked);
        Assert.False(result.IsRoutineBookingEligible);
    }
}
