using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace Occlusa.Desktop.Services;

/// <summary>
/// Automatically probes and starts the local Occlusa.Api process when Occlusa.Desktop
/// is launched against a loopback URL without a separate API terminal running.
/// </summary>
public sealed class LocalApiHostService : IDisposable
{
    private readonly string _baseUrl;
    private readonly Uri? _baseUri;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _probeClient;
    private Process? _managedProcess;
    private bool _disposed;

    public LocalApiHostService(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        Uri.TryCreate(_baseUrl, UriKind.Absolute, out _baseUri);

        _probeClient = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false
        })
        {
            Timeout = TimeSpan.FromMilliseconds(1200)
        };
    }

    public string LastStatusMessage { get; private set; } = string.Empty;

    public async Task<bool> EnsureRunningAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            return false;
        }

        if (_baseUri is null || !_baseUri.IsLoopback)
        {
            return true;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (await IsHealthyAsync(ct))
            {
                LastStatusMessage = "Connected to running Occlusa API.";
                return true;
            }

            if (_managedProcess is null || _managedProcess.HasExited)
            {
                if (!TryStartApiProcess(out var process, out var launchError))
                {
                    LastStatusMessage = launchError;
                    return false;
                }

                _managedProcess?.Dispose();
                _managedProcess = process;
            }

            var deadline = DateTime.UtcNow.AddSeconds(12);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (await IsHealthyAsync(ct))
                {
                    LastStatusMessage = $"Local Occlusa API started on {_baseUrl}.";
                    return true;
                }

                if (_managedProcess is { HasExited: true })
                {
                    LastStatusMessage = $"Local Occlusa API exited prematurely (exit code {_managedProcess.ExitCode}).";
                    return false;
                }

                await Task.Delay(250, ct);
            }

            LastStatusMessage = $"Timed out waiting for Occlusa API on {_baseUrl}.";
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _probeClient.GetAsync($"{_baseUrl}/api/health", ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private bool TryStartApiProcess(out Process? process, out string error)
    {
        process = null;
        error = string.Empty;

        var exePath = FindCandidateFile(
            "Occlusa.Api.exe",
            Path.Combine("src", "Occlusa.Api", "bin", "Debug", "net8.0", "Occlusa.Api.exe"),
            Path.Combine("src", "Occlusa.Api", "bin", "Release", "net8.0", "Occlusa.Api.exe"),
            Path.Combine("Occlusa.Api", "bin", "Debug", "net8.0", "Occlusa.Api.exe"),
            Path.Combine("Occlusa.Api", "bin", "Release", "net8.0", "Occlusa.Api.exe"));

        ProcessStartInfo startInfo;
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
        }
        else
        {
            var csprojPath = FindCandidateFile(
                Path.Combine("src", "Occlusa.Api", "Occlusa.Api.csproj"),
                Path.Combine("Occlusa.Api", "Occlusa.Api.csproj"));

            if (string.IsNullOrWhiteSpace(csprojPath))
            {
                error = "Could not locate Occlusa.Api.exe or Occlusa.Api.csproj to start the local API.";
                return false;
            }

            startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{csprojPath}\" --no-launch-profile",
                WorkingDirectory = Path.GetDirectoryName(csprojPath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
        }

        startInfo.Environment["ASPNETCORE_URLS"] = _baseUrl;
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                error = "Failed to launch local Occlusa API process.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to launch local Occlusa API: {ex.Message}";
            return false;
        }
    }

    private static string? FindCandidateFile(params string[] relativePaths)
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
                foreach (var rel in relativePaths)
                {
                    var candidate = Path.Combine(dir.FullName, rel);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (_managedProcess is { HasExited: false })
            {
                _managedProcess.Kill(entireProcessTree: true);
                _managedProcess.WaitForExit(2000);
            }
        }
        catch
        {
            // Ignore errors during shutdown cleanup.
        }
        finally
        {
            _managedProcess?.Dispose();
            _managedProcess = null;
            _probeClient.Dispose();
            _gate.Dispose();
        }
    }
}

