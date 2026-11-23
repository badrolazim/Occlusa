using System.Runtime.InteropServices;
using Occlusa.Shared.Models;
using Occlusa.Shared.Services;

namespace Occlusa.Desktop.Services;

/// <summary>
/// Native Windows PC/SC smartcard reader service for Malaysian MyKad verification
/// with background card-insertion monitoring and built-in Malaysian simulation profiles.
/// </summary>
public sealed class MyKadPcscReaderService : IDisposable
{
    private readonly object _syncLock = new();
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private string? _lastSeenCardReader;
    private bool _wasCardPresent;
    private bool _disposed;

    public event EventHandler<MyKadReadResultDto>? MyKadDetected;
    public event EventHandler<string>? ReaderStatusChanged;

    public bool IsReaderConnected { get; private set; }
    public string ActiveReaderName { get; private set; } = "No USB Smartcard Reader";
    public string StatusMessage { get; private set; } = "PC/SC Idle (Simulation Mode Available)";

    public IReadOnlyList<MyKadRawRecord> SimulatedProfiles => MyKadApduProtocol.SimulatedProfiles;

    /// <summary>
    /// Starts background polling for USB PC/SC reader attachment and MyKad insertion.
    /// </summary>
    public void StartMonitoring()
    {
        lock (_syncLock)
        {
            if (_disposed || _monitorTask is { IsCompleted: false })
            {
                return;
            }

            _monitorCts = new CancellationTokenSource();
            var ct = _monitorCts.Token;
            _monitorTask = Task.Run(() => MonitorLoopAsync(ct), ct);
        }
    }

    /// <summary>
    /// Stops background PC/SC smartcard monitoring cleanly.
    /// </summary>
    public void StopMonitoring()
    {
        CancellationTokenSource? cts;
        lock (_syncLock)
        {
            cts = _monitorCts;
            _monitorCts = null;
        }

        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    /// <summary>
    /// Attempts a live PC/SC read from the first connected smartcard reader.
    /// Returns a graceful non-throwing status DTO if no reader or card is present.
    /// </summary>
    public Task<MyKadReadResultDto> ReadPhysicalCardAsync(CancellationToken ct = default)
    {
        return Task.Run(() => ReadPhysicalCardInternal(), ct);
    }

    /// <summary>
    /// Generates a verified simulated MyKad read via <see cref="MyKadParser"/> for
    /// 1-click zero-typo registration when no physical reader is attached.
    /// </summary>
    public MyKadReadResultDto SimulateInsert(int profileIndex = 0, bool raiseDetectedEvent = false)
    {
        var profiles = MyKadApduProtocol.SimulatedProfiles;
        int safeIndex = Math.Clamp(profileIndex, 0, profiles.Count - 1);
        var profile = profiles[safeIndex];

        var parsed = MyKadParser.ParseJpnFields(
            rawName: profile.FullName,
            rawIc: profile.IcNumber,
            rawAddressLine1: profile.Address,
            citizenship: profile.Citizenship,
            readerName: profile.ReaderName,
            isSimulated: true);

        UpdateStatus(true, profile.ReaderName, $"Simulated MyKad verified: {parsed.FullName}");

        if (raiseDetectedEvent)
        {
            MyKadDetected?.Invoke(this, parsed);
        }

        return parsed;
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                PollReaderStateOnce();
            }
            catch (DllNotFoundException)
            {
                UpdateStatus(false, "PC/SC Unavailable", "winscard.dll unavailable (Simulation Mode Ready)");
                return;
            }
            catch (Exception ex)
            {
                UpdateStatus(false, "PC/SC Monitor Error", $"PC/SC standby ({ex.Message})");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1500), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void PollReaderStateOnce()
    {
        int ctxRc = WinSCardNative.SCardEstablishContext(
            WinSCardNative.SCARD_SCOPE_USER,
            nint.Zero,
            nint.Zero,
            out nint hContext);

        if (ctxRc != WinSCardNative.SCARD_S_SUCCESS)
        {
            _wasCardPresent = false;
            _lastSeenCardReader = null;
            UpdateStatus(false, "No USB Smartcard Reader", "Smartcard service idle (Simulation Mode Ready)");
            return;
        }

        try
        {
            var readers = ListReaders(hContext);
            if (readers.Count == 0)
            {
                _wasCardPresent = false;
                _lastSeenCardReader = null;
                UpdateStatus(false, "No USB Smartcard Reader", "No physical reader attached (Simulation Mode Ready)");
                return;
            }

            string primaryReader = readers[0];
            var states = new WinSCardNative.SCARD_READERSTATE[]
            {
                new()
                {
                    szReader = primaryReader,
                    dwCurrentState = WinSCardNative.SCARD_STATE_UNAWARE,
                    rgbAtr = new byte[36]
                }
            };

            int statusRc = WinSCardNative.SCardGetStatusChange(hContext, 50, states, (uint)states.Length);
            if (statusRc != WinSCardNative.SCARD_S_SUCCESS && statusRc != WinSCardNative.SCARD_E_TIMEOUT)
            {
                _wasCardPresent = false;
                UpdateStatus(true, primaryReader, $"Reader ready: {primaryReader}");
                return;
            }

            bool isPresent = (states[0].dwEventState & WinSCardNative.SCARD_STATE_PRESENT) != 0;
            if (isPresent)
            {
                if (!_wasCardPresent || !string.Equals(_lastSeenCardReader, primaryReader, StringComparison.Ordinal))
                {
                    _wasCardPresent = true;
                    _lastSeenCardReader = primaryReader;
                    UpdateStatus(true, primaryReader, $"MyKad inserted on {primaryReader}");

                    var result = ReadFromConnectedReader(hContext, primaryReader);
                    if (result.Success)
                    {
                        MyKadDetected?.Invoke(this, result);
                    }
                }
            }
            else
            {
                _wasCardPresent = false;
                _lastSeenCardReader = null;
                UpdateStatus(true, primaryReader, $"Reader ready: {primaryReader} (Insert MyKad)");
            }
        }
        finally
        {
            WinSCardNative.SCardReleaseContext(hContext);
        }
    }

    private MyKadReadResultDto ReadPhysicalCardInternal()
    {
        int ctxRc;
        nint hContext = nint.Zero;
        try
        {
            ctxRc = WinSCardNative.SCardEstablishContext(
                WinSCardNative.SCARD_SCOPE_USER,
                nint.Zero,
                nint.Zero,
                out hContext);
        }
        catch (DllNotFoundException)
        {
            return FailureResult("PC/SC Unavailable", "Windows Smart Card subsystem (winscard.dll) not available. Use Simulate MyKad.");
        }

        if (ctxRc != WinSCardNative.SCARD_S_SUCCESS)
        {
            return FailureResult(
                "No Smartcard Service",
                $"Windows Smart Card service not running (0x{ctxRc:X8}). Attach a PC/SC reader or use Simulate MyKad.");
        }

        try
        {
            var readers = ListReaders(hContext);
            if (readers.Count == 0)
            {
                UpdateStatus(false, "No USB Smartcard Reader", "No physical reader attached (Simulation Mode Ready)");
                return FailureResult(
                    "No USB Smartcard Reader",
                    "No physical PC/SC smartcard reader detected. Attach a CCID reader or click Simulate MyKad.");
            }

            return ReadFromConnectedReader(hContext, readers[0]);
        }
        finally
        {
            WinSCardNative.SCardReleaseContext(hContext);
        }
    }

    private MyKadReadResultDto ReadFromConnectedReader(nint hContext, string readerName)
    {
        int connectRc = WinSCardNative.SCardConnect(
            hContext,
            readerName,
            WinSCardNative.SCARD_SHARE_SHARED,
            WinSCardNative.SCARD_PROTOCOL_ANY,
            out nint hCard,
            out uint activeProtocol);

        if (connectRc != WinSCardNative.SCARD_S_SUCCESS)
        {
            string reason = connectRc == WinSCardNative.SCARD_E_NO_SMARTCARD
                ? $"No MyKad inserted in '{readerName}'. Please insert card or click Simulate MyKad."
                : $"Could not connect to card in '{readerName}' (0x{connectRc:X8}).";
            return FailureResult(readerName, reason);
        }

        try
        {
            // 1. Select Malaysian JPN Application AID
            var selectResp = TransmitApdu(hCard, activeProtocol, MyKadApduProtocol.SelectJpnApplicationApdu);
            if (!selectResp.Success)
            {
                return FailureResult(readerName, $"Inserted smartcard in '{readerName}' did not accept JPN MyKad AID.");
            }

            // 2. Read JPN-1-1 Identity fields (Name, IC, Gender, DOB, Citizenship)
            string rawName = MyKadApduProtocol.DecodeAsciiTrimmed(
                ReadJpnSegment(hCard, activeProtocol, 0x01, 0x00, offset: 0x0003, length: 40));
            string rawIc = MyKadApduProtocol.DecodeAsciiTrimmed(
                ReadJpnSegment(hCard, activeProtocol, 0x01, 0x00, offset: 0x0111, length: 13));
            string rawCitizenship = MyKadApduProtocol.DecodeAsciiTrimmed(
                ReadJpnSegment(hCard, activeProtocol, 0x01, 0x00, offset: 0x0138, length: 18));

            // 3. Read JPN-1-4 Address fields
            string addr1 = MyKadApduProtocol.DecodeAsciiTrimmed(
                ReadJpnSegment(hCard, activeProtocol, 0x04, 0x00, offset: 0x0003, length: 30));
            string addr2 = MyKadApduProtocol.DecodeAsciiTrimmed(
                ReadJpnSegment(hCard, activeProtocol, 0x04, 0x00, offset: 0x0021, length: 30));
            string addr3 = MyKadApduProtocol.DecodeAsciiTrimmed(
                ReadJpnSegment(hCard, activeProtocol, 0x04, 0x00, offset: 0x003F, length: 30));
            string postcode = MyKadApduProtocol.DecodeBcdPostcode(
                ReadJpnSegment(hCard, activeProtocol, 0x04, 0x00, offset: 0x005D, length: 3));
            string city = MyKadApduProtocol.DecodeAsciiTrimmed(
                ReadJpnSegment(hCard, activeProtocol, 0x04, 0x00, offset: 0x0060, length: 25));
            string state = MyKadApduProtocol.DecodeAsciiTrimmed(
                ReadJpnSegment(hCard, activeProtocol, 0x04, 0x00, offset: 0x0079, length: 30));

            if (string.IsNullOrWhiteSpace(rawIc))
            {
                return FailureResult(readerName, $"Unable to extract NRIC from card in '{readerName}'. Ensure card chip contacts are clean.");
            }

            return MyKadParser.ParseJpnFields(
                rawName: rawName,
                rawIc: rawIc,
                rawAddressLine1: addr1,
                rawAddressLine2: addr2,
                rawAddressLine3: addr3,
                postcode: postcode,
                city: city,
                state: state,
                citizenship: string.IsNullOrWhiteSpace(rawCitizenship) ? "WARGANEGARA" : rawCitizenship,
                readerName: readerName,
                isSimulated: false);
        }
        finally
        {
            WinSCardNative.SCardDisconnect(hCard, WinSCardNative.SCARD_LEAVE_CARD);
        }
    }

    private static byte[] ReadJpnSegment(nint hCard, uint activeProtocol, byte fileLo, byte fileHi, int offset, int length)
    {
        var setLen = TransmitApdu(hCard, activeProtocol, MyKadApduProtocol.BuildSetLengthApdu(length));
        if (!setLen.Success) return Array.Empty<byte>();

        var selectInfo = TransmitApdu(hCard, activeProtocol, MyKadApduProtocol.BuildSelectInfoApdu(fileLo, fileHi, offset, length));
        if (!selectInfo.Success) return Array.Empty<byte>();

        var readInfo = TransmitApdu(hCard, activeProtocol, MyKadApduProtocol.BuildReadInfoApdu(length));
        if (!readInfo.Success) return Array.Empty<byte>();

        return readInfo.Payload;
    }

    private static (bool Success, byte[] Payload) TransmitApdu(nint hCard, uint activeProtocol, byte[] apdu)
    {
        var ioReq = WinSCardNative.CreateIoRequest(activeProtocol);
        var recvBuffer = new byte[512];
        uint recvLength = (uint)recvBuffer.Length;

        int rc = WinSCardNative.SCardTransmit(
            hCard,
            ref ioReq,
            apdu,
            (uint)apdu.Length,
            nint.Zero,
            recvBuffer,
            ref recvLength);

        if (rc != WinSCardNative.SCARD_S_SUCCESS || recvLength < 2)
        {
            return (false, Array.Empty<byte>());
        }

        byte sw1 = recvBuffer[recvLength - 2];
        byte sw2 = recvBuffer[recvLength - 1];

        if (sw1 == 0x61)
        {
            byte[] getRespApdu = MyKadApduProtocol.BuildGetResponseApdu(sw2);
            recvLength = (uint)recvBuffer.Length;
            rc = WinSCardNative.SCardTransmit(
                hCard,
                ref ioReq,
                getRespApdu,
                (uint)getRespApdu.Length,
                nint.Zero,
                recvBuffer,
                ref recvLength);

            if (rc != WinSCardNative.SCARD_S_SUCCESS || recvLength < 2)
            {
                return (false, Array.Empty<byte>());
            }

            sw1 = recvBuffer[recvLength - 2];
            sw2 = recvBuffer[recvLength - 1];
        }

        if (sw1 != 0x90 || sw2 != 0x00)
        {
            return (false, Array.Empty<byte>());
        }

        int payloadLen = (int)recvLength - 2;
        var payload = new byte[payloadLen];
        Array.Copy(recvBuffer, 0, payload, 0, payloadLen);
        return (true, payload);
    }

    private static IReadOnlyList<string> ListReaders(nint hContext)
    {
        uint charCount = 0;
        int rc = WinSCardNative.SCardListReaders(hContext, null, null, ref charCount);
        if (rc != WinSCardNative.SCARD_S_SUCCESS || charCount == 0)
        {
            return Array.Empty<string>();
        }

        var buffer = new char[charCount];
        rc = WinSCardNative.SCardListReaders(hContext, null, buffer, ref charCount);
        if (rc != WinSCardNative.SCARD_S_SUCCESS)
        {
            return Array.Empty<string>();
        }

        return WinSCardNative.ParseMultiString(buffer, (int)charCount);
    }

    private void UpdateStatus(bool readerConnected, string readerName, string message)
    {
        bool changed = IsReaderConnected != readerConnected
            || !string.Equals(ActiveReaderName, readerName, StringComparison.Ordinal)
            || !string.Equals(StatusMessage, message, StringComparison.Ordinal);

        IsReaderConnected = readerConnected;
        ActiveReaderName = readerName;
        StatusMessage = message;

        if (changed)
        {
            ReaderStatusChanged?.Invoke(this, message);
        }
    }

    private static MyKadReadResultDto FailureResult(string readerName, string message) =>
        new()
        {
            Success = false,
            ReaderName = readerName,
            IsSimulated = false,
            Message = message
        };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopMonitoring();
    }
}
