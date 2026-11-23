using Occlusa.Shared.Services;

namespace Occlusa.Tests;

public class MyKadParserTests
{
    [Theory]
    [InlineData("920415-14-5678", 2026, 1992, 4, 15, "Female", "14", "W.P. Kuala Lumpur")]
    [InlineData("851103071235", 2026, 1985, 11, 3, "Male", "07", "Pulau Pinang")]
    [InlineData("040229-10-4422", 2026, 2004, 2, 29, "Female", "10", "Selangor")]
    [InlineData("250110-01-0011", 2026, 2025, 1, 10, "Male", "01", "Johor")]
    [InlineData("260101-16-0002", 2026, 2026, 1, 1, "Female", "16", "W.P. Putrajaya")]
    [InlineData("270831-12-0033", 2026, 1927, 8, 31, "Male", "12", "Sabah")]
    public void ParseNric_ValidMalaysianIc_ExtractsDobGenderAndBirthPlace(
        string rawIc,
        int refYear,
        int expectedYear,
        int expectedMonth,
        int expectedDay,
        string expectedGender,
        string expectedStateCode,
        string expectedPlace)
    {
        var result = MyKadParser.ParseNric(rawIc, referenceYear: refYear);

        Assert.True(result.IsValid);
        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), result.DateOfBirth);
        Assert.Equal(expectedGender, result.Gender);
        Assert.Equal(expectedStateCode, result.StateCode);
        Assert.Equal(expectedPlace, result.BirthPlace);
        Assert.Matches(@"^\d{6}-\d{2}-\d{4}$", result.NormalizedIc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("920415-14-567")]       // 11 digits
    [InlineData("920415-14-56789")]     // 13 digits
    [InlineData("92041A-14-5678")]      // Non-digit
    [InlineData("920230-14-5678")]      // Feb 30 invalid calendar date
    [InlineData("030229-10-1234")]      // 2003 is not leap year
    [InlineData("920415-00-5678")]      // Invalid state code 00
    public void ParseNric_InvalidInputs_ReturnsInvalid(string? invalidIc)
    {
        var result = MyKadParser.ParseNric(invalidIc, referenceYear: 2026);

        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public void ParseNric_ForeignBirthCode_MapsToForeignBornAbroad()
    {
        var result = MyKadParser.ParseNric("900512-71-0024", referenceYear: 2026);
        Assert.True(result.IsValid);
        Assert.Equal("Foreign / Born Abroad", result.BirthPlace);
    }

    [Fact]
    public void ParseJpnFields_FormatsCleanMyKadResult()
    {
        var dto = MyKadParser.ParseJpnFields(
            rawName: "  Nurul   Aisyah  binti Zulkifli ",
            rawIc: "920415145678",
            rawAddressLine1: "No. 12 Jalan Bangsar",
            rawAddressLine2: "Bangsar Utama",
            postcode: "59000",
            city: "Kuala Lumpur",
            state: "W.P. Kuala Lumpur",
            isSimulated: true,
            referenceYear: 2026);

        Assert.True(dto.Success);
        Assert.Equal("NURUL AISYAH BINTI ZULKIFLI", dto.FullName);
        Assert.Equal("920415-14-5678", dto.IcNumber);
        Assert.Equal(new DateOnly(1992, 4, 15), dto.DateOfBirth);
        Assert.Equal("Female", dto.Gender);
        Assert.Equal("No. 12 Jalan Bangsar, Bangsar Utama, 59000 Kuala Lumpur, W.P. Kuala Lumpur", dto.Address);
        Assert.Equal("WARGANEGARA", dto.Citizenship);
        Assert.True(dto.IsSimulated);
    }

    [Fact]
    public void ParseDelimitedApduPayload_SupportsPipeAndKeyValuePayloads()
    {
        var pipeDto = MyKadParser.ParseDelimitedApduPayload(
            "Tan Wei Ming|880922105511|24 Jalan SS2/67, 47300 Petaling Jaya|WARGANEGARA",
            referenceYear: 2026);

        Assert.True(pipeDto.Success);
        Assert.Equal("TAN WEI MING", pipeDto.FullName);
        Assert.Equal("880922-10-5511", pipeDto.IcNumber);
        Assert.Equal("Male", pipeDto.Gender);
        Assert.False(pipeDto.IsSimulated);

        var kvDto = MyKadParser.ParseDelimitedApduPayload(
            "NAME=Kavitha a/p Subramaniam;IC=950708-08-3342;ADDR=88 Jalan Ipoh, KL;CITIZENSHIP=WARGANEGARA",
            referenceYear: 2026);

        Assert.True(kvDto.Success);
        Assert.Equal("KAVITHA A/P SUBRAMANIAM", kvDto.FullName);
        Assert.Equal("950708-08-3342", kvDto.IcNumber);
        Assert.Equal("Female", kvDto.Gender);
    }
}
