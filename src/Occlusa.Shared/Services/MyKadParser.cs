using System.Globalization;
using Occlusa.Shared.Models;

namespace Occlusa.Shared.Services;

/// <summary>
/// Parsed Malaysian NRIC components.
/// </summary>
public class ParsedNricResult
{
    public bool IsValid { get; init; }
    public string NormalizedIc { get; init; } = string.Empty;
    public string RawDigits { get; init; } = string.Empty;
    public DateOnly? DateOfBirth { get; init; }
    public string Gender { get; init; } = string.Empty;
    public string StateCode { get; init; } = string.Empty;
    public string BirthPlace { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
}

/// <summary>
/// Malaysian NRIC (YYMMDD-PB-###G) validator, century/gender/state extractor, and MyKad JPN payload parser.
/// </summary>
public static class MyKadParser
{
    private static readonly Dictionary<string, string> StateBirthCodes = new(StringComparer.Ordinal)
    {
        ["01"] = "Johor", ["21"] = "Johor", ["22"] = "Johor", ["23"] = "Johor", ["24"] = "Johor",
        ["02"] = "Kedah", ["25"] = "Kedah", ["26"] = "Kedah", ["27"] = "Kedah",
        ["03"] = "Kelantan", ["28"] = "Kelantan", ["29"] = "Kelantan",
        ["04"] = "Melaka", ["30"] = "Melaka",
        ["05"] = "Negeri Sembilan", ["31"] = "Negeri Sembilan", ["59"] = "Negeri Sembilan",
        ["06"] = "Pahang", ["32"] = "Pahang", ["33"] = "Pahang",
        ["07"] = "Pulau Pinang", ["34"] = "Pulau Pinang", ["35"] = "Pulau Pinang",
        ["08"] = "Perak", ["36"] = "Perak", ["37"] = "Perak", ["38"] = "Perak", ["39"] = "Perak",
        ["09"] = "Perlis", ["40"] = "Perlis",
        ["10"] = "Selangor", ["41"] = "Selangor", ["42"] = "Selangor", ["43"] = "Selangor", ["44"] = "Selangor",
        ["11"] = "Terengganu", ["45"] = "Terengganu", ["46"] = "Terengganu",
        ["12"] = "Sabah", ["47"] = "Sabah", ["48"] = "Sabah", ["49"] = "Sabah",
        ["13"] = "Sarawak", ["50"] = "Sarawak", ["51"] = "Sarawak", ["52"] = "Sarawak", ["53"] = "Sarawak",
        ["14"] = "W.P. Kuala Lumpur", ["54"] = "W.P. Kuala Lumpur", ["55"] = "W.P. Kuala Lumpur", ["56"] = "W.P. Kuala Lumpur", ["57"] = "W.P. Kuala Lumpur",
        ["15"] = "W.P. Labuan", ["58"] = "W.P. Labuan",
        ["16"] = "W.P. Putrajaya",
        ["82"] = "Unknown State"
    };

    /// <summary>
    /// Extracts digits and validates YYMMDD-PB-###G NRIC format, inferring DOB century and gender.
    /// </summary>
    public static ParsedNricResult ParseNric(string? input, int? referenceYear = null)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Invalid("IC number is required.");
        }

        var cleaned = input.Trim().Replace("-", string.Empty).Replace(" ", string.Empty);
        if (cleaned.Length != 12 || !cleaned.All(char.IsAsciiDigit))
        {
            return Invalid("Malaysian NRIC must contain exactly 12 digits (YYMMDD-PB-###G).");
        }

        var yy = int.Parse(cleaned[..2], CultureInfo.InvariantCulture);
        var mm = int.Parse(cleaned[2..4], CultureInfo.InvariantCulture);
        var dd = int.Parse(cleaned[4..6], CultureInfo.InvariantCulture);
        var pb = cleaned[6..8];

        if (pb == "00")
        {
            return Invalid($"Invalid NRIC state birth code '{pb}'.");
        }

        var refYear = referenceYear ?? DateTime.UtcNow.Year;
        var currentTwoDigitYear = refYear % 100;
        var fullYear = yy <= currentTwoDigitYear ? 2000 + yy : 1900 + yy;

        DateOnly dob;
        try
        {
            dob = new DateOnly(fullYear, mm, dd);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Invalid($"Invalid birth date '{cleaned[..6]}' in NRIC.");
        }

        var lastDigit = cleaned[11] - '0';
        var gender = lastDigit % 2 == 1 ? "Male" : "Female";

        var birthPlace = LookupBirthPlace(pb);
        var normalized = $"{cleaned[..6]}-{pb}-{cleaned[8..12]}";

        return new ParsedNricResult
        {
            IsValid = true,
            NormalizedIc = normalized,
            RawDigits = cleaned,
            DateOfBirth = dob,
            Gender = gender,
            StateCode = pb,
            BirthPlace = birthPlace
        };
    }

    /// <summary>
    /// Looks up the Malaysian state or foreign birth indicator from the 2-digit JPN PB code.
    /// </summary>
    public static string LookupBirthPlace(string pbCode)
    {
        if (StateBirthCodes.TryGetValue(pbCode, out var state))
        {
            return state;
        }

        if (int.TryParse(pbCode, NumberStyles.None, CultureInfo.InvariantCulture, out var code)
            && code is >= 60 and <= 99)
        {
            return "Foreign / Born Abroad";
        }

        return "Unknown";
    }

    /// <summary>
    /// Builds a clean <see cref="MyKadReadResultDto"/> from raw simulated or PC/SC APDU JPN fields.
    /// </summary>
    public static MyKadReadResultDto ParseJpnFields(
        string rawName,
        string rawIc,
        string rawAddressLine1,
        string rawAddressLine2 = "",
        string rawAddressLine3 = "",
        string postcode = "",
        string city = "",
        string state = "",
        string citizenship = "WARGANEGARA",
        string readerName = "Simulated PC/SC MyKad Reader",
        bool isSimulated = true,
        int? referenceYear = null)
    {
        var parsedIc = ParseNric(rawIc, referenceYear);
        var cleanName = NormalizeWhitespace(rawName).ToUpperInvariant();

        var addressParts = new[]
        {
            rawAddressLine1,
            rawAddressLine2,
            rawAddressLine3,
            $"{postcode} {city}".Trim(),
            state
        }
        .Select(NormalizeWhitespace)
        .Where(p => !string.IsNullOrWhiteSpace(p));

        var formattedAddress = string.Join(", ", addressParts);
        var cleanCitizenship = string.IsNullOrWhiteSpace(citizenship)
            ? "WARGANEGARA"
            : NormalizeWhitespace(citizenship).ToUpperInvariant();

        if (!parsedIc.IsValid)
        {
            return new MyKadReadResultDto
            {
                Success = false,
                ReaderName = readerName,
                FullName = cleanName,
                IcNumber = rawIc?.Trim() ?? string.Empty,
                Address = formattedAddress,
                Citizenship = cleanCitizenship,
                IsSimulated = isSimulated,
                Message = parsedIc.ErrorMessage
            };
        }

        return new MyKadReadResultDto
        {
            Success = true,
            ReaderName = readerName,
            FullName = cleanName,
            IcNumber = parsedIc.NormalizedIc,
            DateOfBirth = parsedIc.DateOfBirth,
            Gender = parsedIc.Gender,
            Address = formattedAddress,
            Citizenship = cleanCitizenship,
            IsSimulated = isSimulated,
            Message = isSimulated
                ? $"Simulated MyKad verified ({parsedIc.BirthPlace})."
                : $"MyKad PC/SC read verified ({parsedIc.BirthPlace})."
        };
    }

    /// <summary>
    /// Parses a pipe-delimited (<c>FullName|IcNumber|Address|Citizenship</c>) or key-value JPN APDU string.
    /// </summary>
    public static MyKadReadResultDto ParseDelimitedApduPayload(
        string payload,
        string readerName = "ACS ACR38U MyKad Reader",
        bool isSimulated = false,
        int? referenceYear = null)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new MyKadReadResultDto
            {
                Success = false,
                ReaderName = readerName,
                IsSimulated = isSimulated,
                Message = "Empty MyKad payload."
            };
        }

        if (payload.Contains('|'))
        {
            var parts = payload.Split('|');
            var name = parts.Length > 0 ? parts[0] : string.Empty;
            var ic = parts.Length > 1 ? parts[1] : string.Empty;
            var addr = parts.Length > 2 ? parts[2] : string.Empty;
            var cit = parts.Length > 3 ? parts[3] : "WARGANEGARA";
            return ParseJpnFields(name, ic, addr, citizenship: cit, readerName: readerName, isSimulated: isSimulated, referenceYear: referenceYear);
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entries = payload.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries)
        {
            var idx = entry.IndexOf('=');
            if (idx > 0)
            {
                var key = entry[..idx].Trim();
                var value = entry[(idx + 1)..].Trim();
                map[key] = value;
            }
        }

        map.TryGetValue("NAME", out var kvName);
        map.TryGetValue("IC", out var kvIc);
        map.TryGetValue("ADDR", out var kvAddr);
        map.TryGetValue("CITIZENSHIP", out var kvCit);

        return ParseJpnFields(
            kvName ?? string.Empty,
            kvIc ?? string.Empty,
            kvAddr ?? string.Empty,
            citizenship: kvCit ?? "WARGANEGARA",
            readerName: readerName,
            isSimulated: isSimulated,
            referenceYear: referenceYear);
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static ParsedNricResult Invalid(string message) =>
        new()
        {
            IsValid = false,
            ErrorMessage = message
        };
}
