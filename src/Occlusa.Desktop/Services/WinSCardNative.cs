using System.Runtime.InteropServices;

namespace Occlusa.Desktop.Services;

/// <summary>
/// Native Windows PC/SC smartcard P/Invoke declarations against winscard.dll
/// for communicating with MyKad readers without third-party driver dependencies.
/// </summary>
internal static class WinSCardNative
{
    public const int SCARD_S_SUCCESS = 0;
    public const uint SCARD_SCOPE_USER = 0;
    public const uint SCARD_SCOPE_SYSTEM = 2;

    public const uint SCARD_SHARE_SHARED = 2;
    public const uint SCARD_PROTOCOL_T0 = 0x0001;
    public const uint SCARD_PROTOCOL_T1 = 0x0002;
    public const uint SCARD_PROTOCOL_ANY = SCARD_PROTOCOL_T0 | SCARD_PROTOCOL_T1;

    public const uint SCARD_LEAVE_CARD = 0;
    public const uint SCARD_RESET_CARD = 1;

    public const uint SCARD_STATE_UNAWARE = 0x0000;
    public const uint SCARD_STATE_CHANGED = 0x0002;
    public const uint SCARD_STATE_EMPTY = 0x0010;
    public const uint SCARD_STATE_PRESENT = 0x0020;

    public const int SCARD_E_NO_READERS_AVAILABLE = unchecked((int)0x8010002E);
    public const int SCARD_E_NO_SERVICE = unchecked((int)0x8010001D);
    public const int SCARD_E_NO_SMARTCARD = unchecked((int)0x8010000C);
    public const int SCARD_E_TIMEOUT = unchecked((int)0x8010000A);
    public const int SCARD_W_REMOVED_CARD = unchecked((int)0x80100069);

    [StructLayout(LayoutKind.Sequential)]
    public struct SCARD_IO_REQUEST
    {
        public uint dwProtocol;
        public uint cbPciLength;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SCARD_READERSTATE
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string szReader;
        public nint pvUserData;
        public uint dwCurrentState;
        public uint dwEventState;
        public uint cbAtr;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)]
        public byte[] rgbAtr;
    }

    [DllImport("winscard.dll")]
    public static extern int SCardEstablishContext(
        uint dwScope,
        nint pvReserved1,
        nint pvReserved2,
        out nint phContext);

    [DllImport("winscard.dll")]
    public static extern int SCardReleaseContext(nint hContext);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode, EntryPoint = "SCardListReadersW")]
    public static extern int SCardListReaders(
        nint hContext,
        string? mszGroups,
        char[]? mszReaders,
        ref uint pcchReaders);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode, EntryPoint = "SCardConnectW")]
    public static extern int SCardConnect(
        nint hContext,
        string szReader,
        uint dwShareMode,
        uint dwPreferredProtocols,
        out nint phCard,
        out uint pdwActiveProtocol);

    [DllImport("winscard.dll")]
    public static extern int SCardDisconnect(
        nint hCard,
        uint dwDisposition);

    [DllImport("winscard.dll")]
    public static extern int SCardTransmit(
        nint hCard,
        ref SCARD_IO_REQUEST pioSendPci,
        byte[] pbSendBuffer,
        uint cbSendLength,
        nint pioRecvPci,
        byte[] pbRecvBuffer,
        ref uint pcbRecvLength);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode, EntryPoint = "SCardGetStatusChangeW")]
    public static extern int SCardGetStatusChange(
        nint hContext,
        uint dwTimeout,
        [In, Out] SCARD_READERSTATE[] rgReaderStates,
        uint cReaders);

    /// <summary>
    /// Creates an IO request header matching the active PC/SC card protocol (T=0 or T=1).
    /// </summary>
    public static SCARD_IO_REQUEST CreateIoRequest(uint activeProtocol) => new()
    {
        dwProtocol = activeProtocol,
        cbPciLength = (uint)Marshal.SizeOf<SCARD_IO_REQUEST>()
    };

    /// <summary>
    /// Parses a Windows multi-string buffer (null-delimited, double-null terminated) into reader names.
    /// </summary>
    public static IReadOnlyList<string> ParseMultiString(char[] buffer, int charCount)
    {
        var results = new List<string>();
        int start = 0;
        int limit = Math.Min(buffer.Length, charCount);
        for (int i = 0; i < limit; i++)
        {
            if (buffer[i] == '\0')
            {
                if (i > start)
                {
                    results.Add(new string(buffer, start, i - start));
                }
                else
                {
                    break;
                }
                start = i + 1;
            }
        }
        return results;
    }
}
