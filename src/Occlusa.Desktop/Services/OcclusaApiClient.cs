using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using Occlusa.Shared.Models;

namespace Occlusa.Desktop.Services;

/// <summary>
/// Typed HTTP + SignalR client for Occlusa.Api with transparent local host recovery
/// and 5-second DispatcherTimer polling fallback whenever SignalR is disconnected.
/// </summary>
public sealed class OcclusaApiClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly Func<CancellationToken, Task<string?>>? _ensureApiAvailableAsync;
    private readonly object _httpSync = new();
    private readonly List<HttpClient> _retiredClients = new();
    private readonly DispatcherTimer _fallbackPollTimer;
    private HttpClient _http;
    private string _baseUrl;
    private HubConnection? _hubConnection;
    private string _hubBaseUrl = string.Empty;
    private string _activeBranchId = "HQ-KL";
    private bool _isPollInFlight;
    private bool _disposed;

    public event EventHandler<AppointmentQueueItemDto>? QueueUpdated;
    public event EventHandler<InvoiceDto>? InvoiceUpdated;
    public event EventHandler<WhatsAppThreadDto>? WhatsAppUpdated;
    public event EventHandler<WhatsAppThreadDto>? EmergencyEscalated;
    public event EventHandler<ClinicDashboardSnapshotDto>? FallbackSnapshotPolled;
    public event EventHandler<bool>? ConnectionModeChanged;

    public OcclusaApiClient(
        string baseUrl = "http://localhost:5188",
        Func<CancellationToken, Task<string?>>? ensureApiAvailableAsync = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _ensureApiAvailableAsync = ensureApiAvailableAsync;
        var baseUri = new Uri(_baseUrl);
        _http = CreateHttpClient(baseUri);

        _fallbackPollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _fallbackPollTimer.Tick += FallbackPollTimer_Tick;
    }

    public string BaseUrl
    {
        get
        {
            lock (_httpSync)
            {
                return _baseUrl;
            }
        }
    }

    public bool IsSignalRConnected => _hubConnection?.State == HubConnectionState.Connected;

    public string ConnectionModeLabel => IsSignalRConnected ? "Live (SignalR)" : "Fallback Poll (5s)";

    public string ActiveBranchId
    {
        get => _activeBranchId;
        private set => _activeBranchId = value;
    }

    public void UpdateBaseUrl(string newBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(newBaseUrl))
        {
            return;
        }

        var normalized = newBaseUrl.TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var newUri))
        {
            return;
        }

        lock (_httpSync)
        {
            if (_disposed || string.Equals(_baseUrl, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _retiredClients.Add(_http);
            _http = CreateHttpClient(newUri);
            _baseUrl = normalized;
        }
    }

    /// <summary>
    /// Starts SignalR hub connection and enables 5-second fallback polling until connected.
    /// </summary>
    public async Task StartRealtimeAsync(string branchId, CancellationToken ct = default)
    {
        _activeBranchId = branchId;

        var currentBaseUrl = BaseUrl;
        if (_hubConnection != null
            && !string.Equals(_hubBaseUrl, currentBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _hubConnection.DisposeAsync();
            }
            catch
            {
                // Ignore disposal errors when rebinding to a recovered base URL.
            }

            _hubConnection = null;
        }

        if (_hubConnection == null)
        {
            _hubBaseUrl = currentBaseUrl;
            _hubConnection = new HubConnectionBuilder()
                .WithUrl($"{currentBaseUrl}/hubs/queue", options =>
                {
                    if (Uri.TryCreate(currentBaseUrl, UriKind.Absolute, out var hubUri) && hubUri.IsLoopback)
                    {
                        options.HttpMessageHandlerFactory = handler =>
                        {
                            if (handler is HttpClientHandler clientHandler)
                            {
                                clientHandler.UseProxy = false;
                            }
                            else if (handler is SocketsHttpHandler socketsHandler)
                            {
                                socketsHandler.UseProxy = false;
                            }

                            return handler;
                        };
                    }
                })
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

        if (_hubConnection?.State == HubConnectionState.Connected
            && string.Equals(_hubBaseUrl, BaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            OnConnectionStateChanged(true);
            return;
        }

        _isPollInFlight = true;
        try
        {
            var snapshot = await GetDashboardSnapshotAsync(_activeBranchId);
            if (snapshot != null)
            {
                FallbackSnapshotPolled?.Invoke(this, snapshot);
            }

            if (_hubConnection == null
                || !string.Equals(_hubBaseUrl, BaseUrl, StringComparison.OrdinalIgnoreCase)
                || _hubConnection.State == HubConnectionState.Disconnected)
            {
                await StartRealtimeAsync(_activeBranchId);
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

    public Task<HealthStatusDto?> GetHealthAsync(CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(
            (http, token) => http.GetFromJsonAsync<HealthStatusDto>("/api/health", JsonOptions, token),
            ct);

    public Task<ClinicDashboardSnapshotDto?> GetDashboardSnapshotAsync(string branchId, CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(
            (http, token) => http.GetFromJsonAsync<ClinicDashboardSnapshotDto>(
                $"/api/dashboard/snapshot?branchId={Uri.EscapeDataString(branchId)}",
                JsonOptions,
                token),
            ct);

    public Task<List<PatientDto>> GetPatientsAsync(CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(
            async (http, token) => await http.GetFromJsonAsync<List<PatientDto>>("/api/patients", JsonOptions, token) ?? new(),
            ct);

    public Task<PatientDto> UpsertPatientAsync(UpsertPatientRequest request, CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(async (http, token) =>
        {
            var resp = await http.PostAsJsonAsync("/api/patients", request, JsonOptions, token);
            return await ReadRequiredAsync<PatientDto>(resp, token);
        }, ct);

    public Task<PatientDto> UpsertPatientFromMyKadAsync(UpsertPatientRequest request, CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(async (http, token) =>
        {
            var resp = await http.PostAsJsonAsync("/api/patients/mykad-upsert", request, JsonOptions, token);
            return await ReadRequiredAsync<PatientDto>(resp, token);
        }, ct);

    public Task<PatientDto> UpdateMedicalAlertsAsync(
        Guid patientId,
        MedicalAlertDto alerts,
        string branchId = "HQ-KL",
        CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(async (http, token) =>
        {
            var resp = await http.PutAsJsonAsync(
                $"/api/patients/{patientId}/medical-alerts?branchId={Uri.EscapeDataString(branchId)}",
                alerts,
                JsonOptions,
                token);
            return await ReadRequiredAsync<PatientDto>(resp, token);
        }, ct);

    public Task<PatientDto> SignConsentAsync(
        Guid patientId,
        SignConsentRequest request,
        string branchId = "HQ-KL",
        CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(async (http, token) =>
        {
            var resp = await http.PostAsJsonAsync(
                $"/api/patients/{patientId}/consent?branchId={Uri.EscapeDataString(branchId)}",
                request,
                JsonOptions,
                token);
            return await ReadRequiredAsync<PatientDto>(resp, token);
        }, ct);

    public Task<List<DentistDto>> GetDentistsAsync(string? branchId = null, CancellationToken ct = default)
    {
        var url = string.IsNullOrWhiteSpace(branchId)
            ? "/api/dentists"
            : $"/api/dentists?branchId={Uri.EscapeDataString(branchId)}";
        return ExecuteWithRecoveryAsync(
            async (http, token) => await http.GetFromJsonAsync<List<DentistDto>>(url, JsonOptions, token) ?? new(),
            ct);
    }

    public Task<List<TreatmentDto>> GetTreatmentsAsync(CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(
            async (http, token) => await http.GetFromJsonAsync<List<TreatmentDto>>("/api/treatments", JsonOptions, token) ?? new(),
            ct);

    public Task<List<AppointmentQueueItemDto>> GetAppointmentsAsync(string branchId, CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(
            async (http, token) => await http.GetFromJsonAsync<List<AppointmentQueueItemDto>>(
                $"/api/appointments?branchId={Uri.EscapeDataString(branchId)}",
                JsonOptions,
                token) ?? new(),
            ct);

    public Task<AppointmentQueueItemDto> CreateAppointmentAsync(CreateAppointmentRequest request, CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(async (http, token) =>
        {
            var resp = await http.PostAsJsonAsync("/api/appointments", request, JsonOptions, token);
            return await ReadRequiredAsync<AppointmentQueueItemDto>(resp, token);
        }, ct);

    public Task<RegisterPatientAndAppointmentResponse> RegisterPatientAndBookAsync(
        RegisterPatientAndAppointmentRequest request,
        CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(async (http, token) =>
        {
            var resp = await http.PostAsJsonAsync("/api/appointments/register-and-book", request, JsonOptions, token);
            return await ReadRequiredAsync<RegisterPatientAndAppointmentResponse>(resp, token);
        }, ct);

    public Task<AppointmentQueueItemDto> UpdateAppointmentAsync(
        Guid appointmentId,
        UpdateAppointmentRequest request,
        CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(async (http, token) =>
        {
            var resp = await http.PutAsJsonAsync($"/api/appointments/{appointmentId}", request, JsonOptions, token);
            return await ReadRequiredAsync<AppointmentQueueItemDto>(resp, token);
        }, ct);

    public Task<AppointmentQueueItemDto> UpdateQueueStatusAsync(
        Guid appointmentId,
        UpdateQueueStatusRequest request,
        CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(async (http, token) =>
        {
            var resp = await http.PatchAsJsonAsync($"/api/queue/{appointmentId}/status", request, JsonOptions, token);
            return await ReadRequiredAsync<AppointmentQueueItemDto>(resp, token);
        }, ct);

    public Task<List<OdontogramToothDto>> GetOdontogramAsync(Guid patientId, CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(
            async (http, token) => await http.GetFromJsonAsync<List<OdontogramToothDto>>(
                $"/api/patients/{patientId}/odontogram",
                JsonOptions,
                token) ?? new(),
            ct);

    public Task<OdontogramToothDto> UpdateOdontogramToothAsync(
        Guid patientId,
        UpdateOdontogramRequest request,
        CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(async (http, token) =>
        {
            var resp = await http.PutAsJsonAsync($"/api/patients/{patientId}/odontogram", request, JsonOptions, token);
            return await ReadRequiredAsync<OdontogramToothDto>(resp, token);
        }, ct);

    public Task<List<SoapNoteDto>> GetSoapNotesAsync(Guid patientId, CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(
            async (http, token) => await http.GetFromJsonAsync<List<SoapNoteDto>>(
                $"/api/patients/{patientId}/soap-notes",
                JsonOptions,
                token) ?? new(),
            ct);

    public Task<SoapNoteDto> CreateSoapNoteAsync(
        Guid patientId,
        CreateSoapNoteRequest request,
        CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(async (http, token) =>
        {
            var resp = await http.PostAsJsonAsync($"/api/patients/{patientId}/soap-notes", request, JsonOptions, token);
            return await ReadRequiredAsync<SoapNoteDto>(resp, token);
        }, ct);

    public Task<List<SoapMacroDto>> GetSoapMacrosAsync(CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(
            async (http, token) => await http.GetFromJsonAsync<List<SoapMacroDto>>("/api/soap-macros", JsonOptions, token) ?? new(),
            ct);

    public Task<List<InvoiceDto>> GetInvoicesAsync(string branchId, CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(
            async (http, token) => await http.GetFromJsonAsync<List<InvoiceDto>>(
                $"/api/invoices?branchId={Uri.EscapeDataString(branchId)}",
                JsonOptions,
                token) ?? new(),
            ct);

    public Task<InvoiceDto?> GetInvoiceAsync(Guid invoiceId, CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(
            (http, token) => http.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{invoiceId}", JsonOptions, token),
            ct);

    public Task<InvoiceDto> ApplyPaymentAsync(
        Guid invoiceId,
        ApplyPaymentRequest request,
        CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(async (http, token) =>
        {
            var resp = await http.PostAsJsonAsync($"/api/invoices/{invoiceId}/pay", request, JsonOptions, token);
            return await ReadRequiredAsync<InvoiceDto>(resp, token);
        }, ct);

    public Task<InvoiceDto> VoidInvoiceAsync(
        Guid invoiceId,
        VoidInvoiceRequest request,
        CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(async (http, token) =>
        {
            var resp = await http.PostAsJsonAsync($"/api/invoices/{invoiceId}/void", request, JsonOptions, token);
            return await ReadRequiredAsync<InvoiceDto>(resp, token);
        }, ct);

    public Task<DailySalesReconciliationDto> ReconcileDailySalesAsync(
        ReconcileDailySalesRequest request,
        CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(async (http, token) =>
        {
            var resp = await http.PostAsJsonAsync("/api/daily-sales/reconcile", request, JsonOptions, token);
            return await ReadRequiredAsync<DailySalesReconciliationDto>(resp, token);
        }, ct);

    public Task<List<AuditLogDto>> GetAuditLogsAsync(string branchId, CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(
            async (http, token) => await http.GetFromJsonAsync<List<AuditLogDto>>(
                $"/api/audit-logs?branchId={Uri.EscapeDataString(branchId)}",
                JsonOptions,
                token) ?? new(),
            ct);

    public Task<BackupVerificationDto> VerifyBackupAsync(
        string branchId,
        string verifiedBy,
        CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(async (http, token) =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/backups/verify");
            if (!string.IsNullOrWhiteSpace(verifiedBy))
            {
                req.Headers.TryAddWithoutValidation("X-Occlusa-User", verifiedBy);
            }

            var resp = await http.SendAsync(req, token);
            return await ReadRequiredAsync<BackupVerificationDto>(resp, token);
        }, ct);

    public Task<List<WhatsAppThreadDto>> GetWhatsAppThreadsAsync(CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(
            async (http, token) => await http.GetFromJsonAsync<List<WhatsAppThreadDto>>("/api/whatsapp/threads", JsonOptions, token) ?? new(),
            ct);

    public Task<WhatsAppThreadDto> SimulateWhatsAppAsync(
        WhatsAppSimulateRequest request,
        CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(async (http, token) =>
        {
            var resp = await http.PostAsJsonAsync("/api/whatsapp/simulate", request, JsonOptions, token);
            return await ReadRequiredAsync<WhatsAppThreadDto>(resp, token);
        }, ct);

    public Task<WhatsAppThreadDto> SendWhatsAppTakeoverReplyAsync(
        string threadId,
        WhatsAppTakeoverReplyRequest request,
        CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(async (http, token) =>
        {
            var resp = await http.PostAsJsonAsync(
                $"/api/whatsapp/threads/{Uri.EscapeDataString(threadId)}/takeover-reply",
                request,
                JsonOptions,
                token);
            return await ReadRequiredAsync<WhatsAppThreadDto>(resp, token);
        }, ct);

    public Task<AppointmentQueueItemDto> ApproveAiBookingAsync(
        Guid appointmentId,
        CancellationToken ct = default) =>
        ExecuteWithRecoveryAsync(async (http, token) =>
        {
            var resp = await http.PostAsync($"/api/whatsapp/bookings/{appointmentId}/approve", null, token);
            return await ReadRequiredAsync<AppointmentQueueItemDto>(resp, token);
        }, ct);

    private async Task<T> ExecuteWithRecoveryAsync<T>(
        Func<HttpClient, CancellationToken, Task<T>> operation,
        CancellationToken ct)
    {
        HttpClient client;
        lock (_httpSync)
        {
            client = _http;
        }

        try
        {
            return await operation(client, ct);
        }
        catch (HttpRequestException) when (_ensureApiAvailableAsync is not null && !_disposed && !ct.IsCancellationRequested)
        {
            var recoveredUrl = await _ensureApiAvailableAsync(ct);
            if (string.IsNullOrWhiteSpace(recoveredUrl))
            {
                throw;
            }

            UpdateBaseUrl(recoveredUrl);

            HttpClient retryClient;
            lock (_httpSync)
            {
                retryClient = _http;
            }

            return await operation(retryClient, ct);
        }
    }

    private static HttpClient CreateHttpClient(Uri baseUri)
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = !baseUri.IsLoopback
        })
        {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Occlusa-User", "desktop-clinician");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Occlusa-Allowed-Branches", "HQ-KL,PJ-SS2");
        return client;
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

        List<HttpClient> clientsToDispose;
        lock (_httpSync)
        {
            clientsToDispose = new List<HttpClient>(_retiredClients) { _http };
            _retiredClients.Clear();
        }

        foreach (var client in clientsToDispose)
        {
            client.Dispose();
        }
    }
}
