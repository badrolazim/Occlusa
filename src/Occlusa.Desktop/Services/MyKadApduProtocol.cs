using System.Text;

namespace Occlusa.Desktop.Services;

/// <summary>
/// Raw fields extracted from a Malaysian MyKad JPN application or built-in simulation profile.
/// </summary>
public sealed record MyKadRawRecord(
    string ReaderName,
    string FullName,
    string IcNumber,
    string DateOfBirthRaw,
    string GenderRaw,
    string Address,
    string Citizenship,
    bool IsSimulated);

/// <summary>
/// JPN MyKad APDU constants, field offset parsing, and built-in Malaysian test profiles
/// for zero-typo patient registration when a physical PC/SC reader is unavailable.
/// </summary>
internal static class MyKadApduProtocol
{
    /// <summary>
    /// Standard JPN MyKad application SELECT APDU: 00 A4 04 00 0A A0 00 00 00 74 4A 50 4E 00 10.
    /// </summary>
    public static readonly byte[] SelectJpnApplicationApdu =
    [
        0x00, 0xA4, 0x04, 0x00, 0x0A,
        0xA0, 0x00, 0x00, 0x00, 0x74, 0x4A, 0x50, 0x4E, 0x00, 0x10
    ];

    public static byte[] BuildSetLengthApdu(int length)
    {
        byte lo = (byte)(length & 0xFF);
        byte hi = (byte)((length >> 8) & 0xFF);
        return [0xC8, 0x32, 0x00, 0x00, 0x05, 0x08, 0x00, 0x00, lo, hi];
    }

    public static byte[] BuildSelectInfoApdu(byte fileIdLo, byte fileIdHi, int offset, int length)
    {
        byte offLo = (byte)(offset & 0xFF);
        byte offHi = (byte)((offset >> 8) & 0xFF);
        byte lenLo = (byte)(length & 0xFF);
        byte lenHi = (byte)((length >> 8) & 0xFF);
        return [0xCC, 0x00, 0x00, 0x00, 0x08, fileIdLo, fileIdHi, offLo, offHi, lenLo, lenHi, 0x00, 0x00];
    }

    public static byte[] BuildReadInfoApdu(int length) =>
        [0xCC, 0x06, 0x00, 0x00, (byte)(length & 0xFF)];

    public static byte[] BuildGetResponseApdu(int length) =>
        [0x00, 0xC0, 0x00, 0x00, (byte)(length & 0xFF)];

    public static bool IsSuccessStatus(byte[] response, int length)
    {
        if (length < 2) return false;
        byte sw1 = response[length - 2];
        byte sw2 = response[length - 1];
        return (sw1 == 0x90 && sw2 == 0x00) || sw1 == 0x61;
    }

    public static string DecodeAsciiTrimmed(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;
        string raw = Encoding.ASCII.GetString(bytes);
        return string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    public static string DecodeBcdDate(byte[] bytes)
    {
        if (bytes.Length < 4) return string.Empty;
        var sb = new StringBuilder(8);
        for (int i = 0; i < 4; i++)
        {
            sb.Append((bytes[i] >> 4) & 0x0F);
            sb.Append(bytes[i] & 0x0F);
        }
        string yyyymmdd = sb.ToString();
        if (yyyymmdd.Length == 8)
        {
            return $"{yyyymmdd[..4]}-{yyyymmdd[4..6]}-{yyyymmdd[6..8]}";
        }
        return yyyymmdd;
    }

    public static string DecodeBcdPostcode(byte[] bytes)
    {
        if (bytes.Length < 3) return string.Empty;
        var sb = new StringBuilder(6);
        for (int i = 0; i < 3; i++)
        {
            sb.Append((bytes[i] >> 4) & 0x0F);
            sb.Append(bytes[i] & 0x0F);
        }
        return sb.ToString()[..Math.Min(5, sb.Length)];
    }

    /// <summary>
    /// Built-in realistic Malaysian MyKad simulation profiles for clinics testing
    /// zero-typo IC registration without a physical USB smartcard reader attached.
    /// </summary>
    public static IReadOnlyList<MyKadRawRecord> SimulatedProfiles { get; } =
    [
        new MyKadRawRecord(
            ReaderName: "Simulated PC/SC Reader (ACS ACR38U-CCID)",
            FullName: "NURUL AISYAH BINTI ZULKIFLI",
            IcNumber: "920814-14-5682",
            DateOfBirthRaw: "1992-08-14",
            GenderRaw: "P",
            Address: "NO 18 JALAN BANGSAR UTAMA 3, 59000 KUALA LUMPUR, WILAYAH PERSEKUTUAN",
            Citizenship: "WARGANEGARA",
            IsSimulated: true),
        new MyKadRawRecord(
            ReaderName: "Simulated PC/SC Reader (ACS ACR38U-CCID)",
            FullName: "TAN WEI MING",
            IcNumber: "850321-10-5149",
            DateOfBirthRaw: "1985-03-21",
            GenderRaw: "L",
            Address: "24 JALAN SS 2/66, 47300 PETALING JAYA, SELANGOR",
            Citizenship: "WARGANEGARA",
            IsSimulated: true),
        new MyKadRawRecord(
            ReaderName: "Simulated PC/SC Reader (ACS ACR38U-CCID)",
            FullName: "KAVITHA A/P SUBRAMANIAM",
            IcNumber: "961105-08-6234",
            DateOfBirthRaw: "1996-11-05",
            GenderRaw: "P",
            Address: "12 LENGKOK MERDEKA, 30010 IPOH, PERAK",
            Citizenship: "WARGANEGARA",
            IsSimulated: true)
    ];
}
