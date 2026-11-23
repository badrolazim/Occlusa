using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using Occlusa.Shared.Models;

namespace Occlusa.Desktop.Services;

/// <summary>
/// Typed HTTP + SignalR client for Occlusa.Api with mandatory 5-second DispatcherTimer
/// polling fallback whenever the SignalR hub connection is disconnected or reconnecting.
/// </summary>
public sealed class OcclusaApiClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly DispatcherTimer _fallbackPollTimer;
    private HubConnection? _hubConnection;
    private string _activeBranchId = "HQ-KL";
    private bool _isPollInFlight;
    private bool _disposed;

    public event EventHandler<AppointmentQueueItemDto>? QueueUpdated;
    public event EventHandler<InvoiceDto>? InvoiceUpdated;
    public event EventHandler<WhatsAppThreadDto>? WhatsAppUpdated;
    public event EventHandler<WhatsAppThreadDto>? EmergencyEscalated;
    public event EventHandler<ClinicDashboardSnapshotDto>? FallbackSnapshotPolled;
    public event EventHandler<bool>? ConnectionModeChanged;

    public OcclusaApiClient(string baseUrl = "http://localhost:5188")
    {
        _baseUrl = baseUrl.TrimEnd('/');
        var baseUri = new Uri(_baseUrl);
        _http = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = !baseUri.IsLoopback
        })
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(15)
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Occlusa-User", "desktop-clinician");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Occlusa-Allowed-Branches", "HQ-KL,PJ-SS2");

        _fallbackPollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _fallbackPollTimer.Tick += FallbackPollTimer_Tick;
    }

    public bool IsSignalRConnected => _hubConnection?.State == HubConnectionState.Connected;

    public string ConnectionModeLabel => IsSignalRConnected ? "Live (SignalR)" : "Fallback Poll (5s)";

    public string ActiveBranchId
    {
        get => _activeBranchId;
        private set => _activeBranchId = value;
    }

    /// <summary>
    /// Starts SignalR hub connection and enables 5-second fallback polling until connected.
    /// </summary>
    public async Task StartRealtimeAsync(string branchId, CancellationToken ct = default)
    {
        _activeBranchId = branchId;

        if (_hubConnection == null)
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl($"{_baseUrl}/hubs/queue")
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<AppointmentQueueItemDto>("QueueUpdated", item =>
                QueueUpdated?.Invoke(this, item));

            _hubConnection.On<InvoiceDto>("InvoiceUpdated", invoice =>
                InvoiceUpdated?.Invoke(this, invoice));

            _hubConnection.On<WhatsAppThreadDto>("WhatsAppUpdated", thread =>
                WhatsAppUpdated?.Invoke(this, thread));

            _hubConnection.On<WhatsAppThreadDto>("EmergencyEscalated", thread =>
                EmergencyEscalated?.Invoke(this, thread));

            _hubConnection.Reconnecting += _ =>
            {
                OnConnectionStateChanged(false);
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += async _ =>
            {
                await SafeJoinBranchAsync(_activeBranchId);
                OnConnectionStateChanged(true);
            };

            _hubConnection.Closed += _ =>
            {
                OnConnectionStateChanged(false);
                return Task.CompletedTask;
            };
        }

        try
        {
            if (_hubConnection.State == HubConnectionState.Disconnected)
            {
                await _hubConnection.StartAsync(ct);
            }

            await SafeJoinBranchAsync(branchId);
            OnConnectionStateChanged(true);
        }
        catch
        {
            // Mandatory 5-second polling fallback engages immediately when SignalR is unavailable.
            OnConnectionStateChanged(false);
        }
    }

    public async Task SwitchBranchAsync(string newBranchId, CancellationToken ct = default)
    {
        string previousBranch = _activeBranchId;
        _activeBranchId = newBranchId;

        if (_hubConnection?.State == HubConnectionState.Connected)
        {
            try
            {
                if (!string.Equals(previousBranch, newBranchId, StringComparison.OrdinalIgnoreCase))
                {
                    await _hubConnection.InvokeAsync("LeaveBranch", previousBranch, ct);
                    await _hubConnection.InvokeAsync("JoinBranch", newBranchId, ct);
                }
            }
            catch
            {
                // Ignore hub group errors; HTTP snapshot refresh still brings fresh branch data.
            }
        }
        else if (_hubConnection?.State == HubConnectionState.Disconnected)
        {
            await StartRealtimeAsync(newBranchId, ct);
        }
    }

    private async Task SafeJoinBranchAsync(string branchId)
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            await _hubConnection.InvokeAsync("JoinBranch", branchId);
        }
        catch
        {
            // Global hub broadcasts still work even if group subscription fails.
        }
    }

    private void OnConnectionStateChanged(bool isConnected)
    {
        if (isConnected)
        {
            _fallbackPollTimer.Stop();
        }
        else if (!_fallbackPollTimer.IsEnabled && !_disposed)
        {
            _fallbackPollTimer.Start();
        }

        ConnectionModeChanged?.Invoke(this, isConnected);
    }

    private async void FallbackPollTimer_Tick(object? sender, EventArgs e)
    {
        if (_disposed || _isPollInFlight)
        {
            return;
        }

        if (_hubConnection?.State == HubConnectionState.Connected)
        {
            OnConnectionStateChanged(true);
            return;
        }

        _isPollInFlight = true;
        try
        {
            // Periodically attempt to re-establish SignalR if it closed completely.
            if (_hubConnection is { State: HubConnectionState.Disconnected })
            {
                try
                {
                    await _hubConnection.StartAsync();
                    await SafeJoinBranchAsync(_activeBranchId);
                    OnConnectionStateChanged(true);
                }
                catch
                {
                    // Remain in 5s fallback polling mode until API/Hub is reachable.
                }
            }

            var snapshot = await GetDashboardSnapshotAsync(_activeBranchId);
            if (snapshot != null)
            {
                FallbackSnapshotPolled?.Invoke(this, snapshot);
            }
        }
        catch
        {
            // Keep 5s polling resilient if API is temporarily restarting.
        }
        finally
        {
            _isPollInFlight = false;
        }
    }

    public async Task<HealthStatusDto?> GetHealthAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<HealthStatusDto>("/api/health", JsonOptions, ct);

    public async Task<ClinicDashboardSnapshotDto?> GetDashboardSnapshotAsync(string branchId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<ClinicDashboardSnapshotDto>(
            $"/api/dashboard/snapshot?branchId={Uri.EscapeDataString(branchId)}",
            JsonOptions,
            ct);

    public async Task<List<PatientDto>> GetPatientsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<PatientDto>>("/api/patients", JsonOptions, ct) ?? new();

    public async Task<PatientDto> UpsertPatientAsync(UpsertPatientRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/patients", request, JsonOptions, ct);
        return await ReadRequiredAsync<PatientDto>(resp, ct);
    }

    public async Task<PatientDto> UpsertPatientFromMyKadAsync(UpsertPatientRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/patients/mykad-upsert", request, JsonOptions, ct);
        return await ReadRequiredAsync<PatientDto>(resp, ct);
    }

    public async Task<PatientDto> UpdateMedicalAlertsAsync(
        Guid patientId,
        MedicalAlertDto alerts,
        string branchId = "HQ-KL",
        CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync(
            $"/api/patients/{patientId}/medical-alerts?branchId={Uri.EscapeDataString(branchId)}",
            alerts,
            JsonOptions,
            ct);
        return await ReadRequiredAsync<PatientDto>(resp, ct);
    }

    public async Task<PatientDto> SignConsentAsync(
        Guid patientId,
        SignConsentRequest request,
        string branchId = "HQ-KL",
        CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync(
            $"/api/patients/{patientId}/consent?branchId={Uri.EscapeDataString(branchId)}",
            request,
            JsonOptions,
            ct);
        return await ReadRequiredAsync<PatientDto>(resp, ct);
    }

    public async Task<List<DentistDto>> GetDentistsAsync(string? branchId = null, CancellationToken ct = default)
    {
        var url = string.IsNullOrWhiteSpace(branchId)
            ? "/api/dentists"
            : $"/api/dentists?branchId={Uri.EscapeDataString(branchId)}";
        return await _http.GetFromJsonAsync<List<DentistDto>>(url, JsonOptions, ct) ?? new();
    }

    public async Task<List<TreatmentDto>> GetTreatmentsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<TreatmentDto>>("/api/treatments", JsonOptions, ct) ?? new();

    public async Task<List<AppointmentQueueItemDto>> GetAppointmentsAsync(string branchId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<AppointmentQueueItemDto>>(
            $"/api/appointments?branchId={Uri.EscapeDataString(branchId)}",
            JsonOptions,
            ct) ?? new();

    public async Task<AppointmentQueueItemDto> CreateAppointmentAsync(CreateAppointmentRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/appointments", request, JsonOptions, ct);
        return await ReadRequiredAsync<AppointmentQueueItemDto>(resp, ct);
    }

    public async Task<RegisterPatientAndAppointmentResponse> RegisterPatientAndBookAsync(
        RegisterPatientAndAppointmentRequest request,
        CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/appointments/register-and-book", request, JsonOptions, ct);
        return await ReadRequiredAsync<RegisterPatientAndAppointmentResponse>(resp, ct);
    }

    public async Task<AppointmentQueueItemDto> UpdateAppointmentAsync(
        Guid appointmentId,
        UpdateAppointmentRequest request,
        CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/appointments/{appointmentId}", request, JsonOptions, ct);
        return await ReadRequiredAsync<AppointmentQueueItemDto>(resp, ct);
    }

    public async Task<AppointmentQueueItemDto> UpdateQueueStatusAsync(
        Guid appointmentId,
        UpdateQueueStatusRequest request,
        CancellationToken ct = default)
    {
        var resp = await _http.PatchAsJsonAsync($"/api/queue/{appointmentId}/status", request, JsonOptions, ct);
        return await ReadRequiredAsync<AppointmentQueueItemDto>(resp, ct);
    }

    public async Task<List<OdontogramToothDto>> GetOdontogramAsync(Guid patientId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<OdontogramToothDto>>(
            $"/api/patients/{patientId}/odontogram",
            JsonOptions,
            ct) ?? new();

    public async Task<OdontogramToothDto> UpdateOdontogramToothAsync(
        Guid patientId,
        UpdateOdontogramRequest request,
        CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"/api/patients/{patientId}/odontogram", request, JsonOptions, ct);
        return await ReadRequiredAsync<OdontogramToothDto>(resp, ct);
    }

    public async Task<List<SoapNoteDto>> GetSoapNotesAsync(Guid patientId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<SoapNoteDto>>(
            $"/api/patients/{patientId}/soap-notes",
            JsonOptions,
            ct) ?? new();

    public async Task<SoapNoteDto> CreateSoapNoteAsync(
        Guid patientId,
        CreateSoapNoteRequest request,
        CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/patients/{patientId}/soap-notes", request, JsonOptions, ct);
        return await ReadRequiredAsync<SoapNoteDto>(resp, ct);
    }

    public async Task<List<SoapMacroDto>> GetSoapMacrosAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<SoapMacroDto>>("/api/soap-macros", JsonOptions, ct) ?? new();

    public async Task<List<InvoiceDto>> GetInvoicesAsync(string branchId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<InvoiceDto>>(
            $"/api/invoices?branchId={Uri.EscapeDataString(branchId)}",
            JsonOptions,
            ct) ?? new();

    public async Task<InvoiceDto?> GetInvoiceAsync(Guid invoiceId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoiceId}", JsonOptions, ct);

    public async Task<InvoiceDto> ApplyPaymentAsync(
        Guid invoiceId,
        ApplyPaymentRequest request,
        CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/invoices/{invoiceId}/pay", request, JsonOptions, ct);
        return await ReadRequiredAsync<InvoiceDto>(resp, ct);
    }

    public async Task<InvoiceDto> VoidInvoiceAsync(
        Guid invoiceId,
        VoidInvoiceRequest request,
        CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/invoices/{invoiceId}/void", request, JsonOptions, ct);
        return await ReadRequiredAsync<InvoiceDto>(resp, ct);
    }

    public async Task<DailySalesReconciliationDto> ReconcileDailySalesAsync(
        ReconcileDailySalesRequest request,
        CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/daily-sales/reconcile", request, JsonOptions, ct);
        return await ReadRequiredAsync<DailySalesReconciliationDto>(resp, ct);
    }

    public async Task<List<AuditLogDto>> GetAuditLogsAsync(string branchId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<AuditLogDto>>(
            $"/api/audit-logs?branchId={Uri.EscapeDataString(branchId)}",
            JsonOptions,
            ct) ?? new();

    public async Task<BackupVerificationDto> VerifyBackupAsync(
        string branchId,
        string verifiedBy,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/backups/verify");
        if (!string.IsNullOrWhiteSpace(verifiedBy))
        {
            req.Headers.TryAddWithoutValidation("X-Occlusa-User", verifiedBy);
        }
        var resp = await _http.SendAsync(req, ct);
        return await ReadRequiredAsync<BackupVerificationDto>(resp, ct);
    }

    public async Task<List<WhatsAppThreadDto>> GetWhatsAppThreadsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<List<WhatsAppThreadDto>>("/api/whatsapp/threads", JsonOptions, ct) ?? new();

    public async Task<WhatsAppThreadDto> SimulateWhatsAppAsync(
        WhatsAppSimulateRequest request,
        CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/whatsapp/simulate", request, JsonOptions, ct);
        return await ReadRequiredAsync<WhatsAppThreadDto>(resp, ct);
    }

    public async Task<WhatsAppThreadDto> SendWhatsAppTakeoverReplyAsync(
        string threadId,
        WhatsAppTakeoverReplyRequest request,
        CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync(
            $"/api/whatsapp/threads/{Uri.EscapeDataString(threadId)}/takeover-reply",
            request,
            JsonOptions,
            ct);
        return await ReadRequiredAsync<WhatsAppThreadDto>(resp, ct);
    }

    public async Task<AppointmentQueueItemDto> ApproveAiBookingAsync(
        Guid appointmentId,
        CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/api/whatsapp/bookings/{appointmentId}/approve", null, ct);
        return await ReadRequiredAsync<AppointmentQueueItemDto>(resp, ct);
    }

    private static async Task<T> ReadRequiredAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(FormatApiError(response, body));
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("API returned an empty JSON response.");
    }

    private static string FormatApiError(HttpResponseMessage response, string body)
    {
        if (!string.IsNullOrWhiteSpace(body) && body.TrimStart().StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("errors", out var errorsEl)
                    && errorsEl.ValueKind == JsonValueKind.Object)
                {
                    var messages = new List<string>();
                    foreach (var prop in errorsEl.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in prop.Value.EnumerateArray())
                            {
                                var msg = item.GetString();
                                if (!string.IsNullOrWhiteSpace(msg))
                                {
                                    messages.Add(msg);
                                }
                            }
                        }
                    }

                    if (messages.Count > 0)
                    {
                        return string.Join(" ", messages);
                    }
                }

                if (doc.RootElement.TryGetProperty("detail", out var detailEl)
                    && detailEl.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(detailEl.GetString()))
                {
                    return detailEl.GetString()!;
                }

                if (doc.RootElement.TryGetProperty("title", out var titleEl)
                    && titleEl.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(titleEl.GetString()))
                {
                    return titleEl.GetString()!;
                }
            }
            catch (JsonException)
            {
                // Fall back to raw response body below.
            }
        }

        return string.IsNullOrWhiteSpace(body)
            ? $"API returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})."
            : body.Trim();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        opts.Converters.Add(new JsonStringEnumConverter());
        return opts;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _fallbackPollTimer.Stop();

        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }

        _http.Dispose();
    }
}
