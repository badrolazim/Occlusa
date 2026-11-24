using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Occlusa.Api;
using Occlusa.Shared.Models;

namespace Occlusa.Desktop.Services;

/// <summary>
/// Automatically probes an external Occlusa.Api or hosts Occlusa.Api in-process on Kestrel
/// when Occlusa.Desktop targets a loopback URL, with ephemeral port fallback if the default port is busy.
/// </summary>
public sealed class LocalApiHostService : IDisposable
{
    private static readonly JsonSerializerOptions HealthJsonOptions = CreateHealthJsonOptions();

    private readonly string _requestedBaseUrl;
    private readonly Uri? _requestedUri;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _probeClient;
    private WebApplication? _embeddedApp;
    private bool _disposed;

    public LocalApiHostService(string baseUrl)
    {
        _requestedBaseUrl = baseUrl.TrimEnd('/');
        ActiveBaseUrl = _requestedBaseUrl;
        Uri.TryCreate(_requestedBaseUrl, UriKind.Absolute, out _requestedUri);

        _probeClient = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false
        })
        {
            Timeout = TimeSpan.FromMilliseconds(1200)
        };
    }

    public string ActiveBaseUrl { get; private set; }
    public string LastStatusMessage { get; private set; } = string.Empty;
    public bool IsHostingInProcess => _embeddedApp is not null;

    public async Task<bool> EnsureRunningAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            return false;
        }

        if (_requestedUri is null || !_requestedUri.IsLoopback)
        {
            return true;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_disposed)
            {
                return false;
            }

            if (await IsHealthyOcclusaApiAsync(ActiveBaseUrl, ct))
            {
                LastStatusMessage = IsHostingInProcess
                    ? $"Embedded Occlusa API running on {ActiveBaseUrl}."
                    : $"Connected to running Occlusa API on {ActiveBaseUrl}.";
                return true;
            }

            if (!string.Equals(ActiveBaseUrl, _requestedBaseUrl, StringComparison.OrdinalIgnoreCase)
                && await IsHealthyOcclusaApiAsync(_requestedBaseUrl, ct))
            {
                await StopAndDisposeEmbeddedAsync();
                ActiveBaseUrl = _requestedBaseUrl;
                LastStatusMessage = $"Connected to running Occlusa API on {ActiveBaseUrl}.";
                return true;
            }

            await StopAndDisposeEmbeddedAsync();

            return await StartEmbeddedHostAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> StartEmbeddedHostAsync(CancellationToken ct)
    {
        var contentRoot = ResolveApiContentRoot();
        var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(envName))
        {
            envName = "Development";
        }

        var primaryAttempt = await TryStartAtUrlAsync(_requestedBaseUrl, contentRoot, envName, ct);
        if (primaryAttempt.Success)
        {
            _embeddedApp = primaryAttempt.App;
            ActiveBaseUrl = primaryAttempt.BoundUrl!;
            LastStatusMessage = $"Embedded Occlusa API running on {ActiveBaseUrl}.";
            return true;
        }

        if (ct.IsCancellationRequested)
        {
            LastStatusMessage = "Starting embedded Occlusa API was cancelled.";
            return false;
        }

        // If the configured port (e.g. 5188) is occupied by another process, fall back to a free OS loopback port.
        if (!string.Equals(_requestedBaseUrl, "http://127.0.0.1:0", StringComparison.OrdinalIgnoreCase))
        {
            var fallbackAttempt = await TryStartAtUrlAsync("http://127.0.0.1:0", contentRoot, envName, ct);
            if (fallbackAttempt.Success)
            {
                _embeddedApp = fallbackAttempt.App;
                ActiveBaseUrl = fallbackAttempt.BoundUrl!;
                LastStatusMessage = $"Embedded Occlusa API running on {ActiveBaseUrl} (fallback port).";
                return true;
            }

            LastStatusMessage = $"Failed to start embedded Occlusa API on {_requestedBaseUrl} ({primaryAttempt.Error}) or fallback port ({fallbackAttempt.Error}).";
            return false;
        }

        LastStatusMessage = $"Failed to start embedded Occlusa API: {primaryAttempt.Error}";
        return false;
    }

    private async Task<(bool Success, WebApplication? App, string? BoundUrl, string? Error)> TryStartAtUrlAsync(
        string targetUrl,
        string contentRoot,
        string envName,
        CancellationToken ct)
    {
        WebApplication? app = null;
        try
        {
            app = OcclusaApiApplicationFactory.BuildApplication(
                contentRootPath: contentRoot,
                urls: targetUrl,
                environmentName: envName);

            await app.StartAsync(ct);

            var boundUrl = app.Urls.FirstOrDefault()?.TrimEnd('/') ?? targetUrl.TrimEnd('/');
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (await IsHealthyOcclusaApiAsync(boundUrl, ct))
                {
                    return (true, app, boundUrl, null);
                }

                await Task.Delay(100, ct);
            }

            await SafeStopAndDisposeAsync(app);
            return (false, null, null, $"API on {boundUrl} did not pass health verification within 5s.");
        }
        catch (Exception ex)
        {
            if (app is not null)
            {
                await SafeStopAndDisposeAsync(app);
            }

            return (false, null, null, ex.Message);
        }
    }

    private async Task<bool> IsHealthyOcclusaApiAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            using var resp = await _probeClient.GetAsync($"{baseUrl.TrimEnd('/')}/api/health", ct);
            if (!resp.IsSuccessStatusCode)
            {
                return false;
            }

            var health = await resp.Content.ReadFromJsonAsync<HealthStatusDto>(HealthJsonOptions, ct);
            return health is not null
                   && string.Equals(health.Status, "Healthy", StringComparison.OrdinalIgnoreCase)
                   && !string.IsNullOrWhiteSpace(health.DatabaseMode);
        }
        catch
        {
            return false;
        }
    }

    private async Task StopAndDisposeEmbeddedAsync()
    {
        if (_embeddedApp is not null)
        {
            var app = _embeddedApp;
            _embeddedApp = null;
            await SafeStopAndDisposeAsync(app);
        }
    }

    private static async Task SafeStopAndDisposeAsync(WebApplication app)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await app.StopAsync(cts.Token);
        }
        catch
        {
            // Ignore shutdown stop errors.
        }

        try
        {
            await app.DisposeAsync();
        }
        catch
        {
            // Ignore disposal errors.
        }
    }

    private static string ResolveApiContentRoot()
    {
        var roots = new[] { AppContext.BaseDirectory, Environment.CurrentDirectory };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var dir = new DirectoryInfo(root);
            for (int depth = 0; depth < 7 && dir != null; depth++, dir = dir.Parent)
            {
                var srcSettings = Path.Combine(dir.FullName, "src", "Occlusa.Api", "appsettings.json");
                if (File.Exists(srcSettings))
                {
                    return Path.GetDirectoryName(srcSettings)!;
                }

                var directSettings = Path.Combine(dir.FullName, "Occlusa.Api", "appsettings.json");
                if (File.Exists(directSettings))
                {
                    return Path.GetDirectoryName(directSettings)!;
                }
            }
        }

        return AppContext.BaseDirectory;
    }

    private static JsonSerializerOptions CreateHealthJsonOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        opts.Converters.Add(new JsonStringEnumConverter());
        return opts;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var app = _embeddedApp;
        _embeddedApp = null;

        if (app is not null)
        {
            try
            {
                // Run on thread pool to avoid blocking the WPF DispatcherSynchronizationContext.
                Task.Run(async () =>
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await app.StopAsync(cts.Token);
                    await app.DisposeAsync();
                }).Wait(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // Ignore errors during shutdown cleanup.
            }
        }

        _probeClient.Dispose();
        _gate.Dispose();
    }
}
