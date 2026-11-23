using System.Windows;
using Occlusa.Desktop.Services;
using Occlusa.Desktop.ViewModels;

namespace Occlusa.Desktop;

/// <summary>
/// Application composition root initializing shared API, SignalR, PC/SC MyKad reader,
/// and the MainViewModel workspace.
/// </summary>
public partial class App : Application
{
    private LocalApiHostService? _localApiHost;
    private OcclusaApiClient? _apiClient;
    private MyKadPcscReaderService? _myKadService;
    private MainViewModel? _mainViewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string baseUrl = Environment.GetEnvironmentVariable("OCCLUSA_API_URL")
            ?? "http://localhost:5188";

        _localApiHost = new LocalApiHostService(baseUrl);
        _apiClient = new OcclusaApiClient(baseUrl);
        _myKadService = new MyKadPcscReaderService();
        _mainViewModel = new MainViewModel(_apiClient, _myKadService, () => _localApiHost.EnsureRunningAsync());

        var mainWindow = new MainWindow(_mainViewModel, _apiClient, _myKadService);
        MainWindow = mainWindow;
        mainWindow.Show();

        _ = _mainViewModel.InitializeAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _myKadService?.Dispose();
        if (_apiClient != null)
        {
            await _apiClient.DisposeAsync();
        }
        _localApiHost?.Dispose();
        base.OnExit(e);
    }
}
