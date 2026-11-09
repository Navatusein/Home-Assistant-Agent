using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeAssistant.Agent.Core.Platform;

namespace HomeAssistant.Agent.ViewModels.Settings;

// Controls the agent's background service from the desktop app. Stores
// nothing: it acts on the system directly, which is why it is not a settings
// section of its own but a group inside General.
public partial class WindowsServiceViewModel : ObservableObject
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    // The service manager may need a moment to settle after start/stop.
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(500);

    private readonly IBackgroundServiceControl _service;
    private readonly DispatcherTimer _refreshTimer;

    private bool _actionRunning;

    public WindowsServiceViewModel(IBackgroundServiceControl service)
    {
        _service = service;

        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += (_, _) => RefreshState();

        RefreshState();
    }

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _canInstall;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _canStart;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _canStop;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    private bool _canUninstall;

    // Reading the state enumerates every service on the machine, so only do it
    // while the section holding this group is actually on screen.
    public void StartPolling()
    {
        RefreshState();
        _refreshTimer.Start();
    }

    public void StopPolling() => _refreshTimer.Stop();

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private Task InstallAsync() => RunAsync(BackgroundServiceAction.Install);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync() => RunAsync(BackgroundServiceAction.Start);

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task StopAsync() => RunAsync(BackgroundServiceAction.Stop);

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private Task UninstallAsync() => RunAsync(BackgroundServiceAction.Uninstall);

    private void RefreshState()
    {
        if (_actionRunning)
            return;

        var state = _service.GetState();

        Status = state switch
        {
            BackgroundServiceState.Missing => "Service executable not found next to the app",
            BackgroundServiceState.NotInstalled => "Not installed",
            _ => $"Installed, {state.ToString().ToLowerInvariant()}",
        };

        var installed = state is not (BackgroundServiceState.Missing or BackgroundServiceState.NotInstalled);
        var running = state == BackgroundServiceState.Running;

        SetButtons(
            install: state == BackgroundServiceState.NotInstalled,
            start: installed && !running,
            stop: running,
            uninstall: installed);
    }

    private void SetButtons(bool install, bool start, bool stop, bool uninstall)
    {
        CanInstall = install;
        CanStart = start;
        CanStop = stop;
        CanUninstall = uninstall;
    }

    private async Task RunAsync(BackgroundServiceAction action)
    {
        var name = action.ToString().ToLowerInvariant();

        _actionRunning = true;
        SetButtons(install: false, start: false, stop: false, uninstall: false);
        Status = $"Running '{name}'…";

        string? failure = null;

        try
        {
            await _service.RunAsync(action);
        }
        catch (OperationCanceledException)
        {
            // The user declined the administrator prompt.
        }
        catch (Exception ex)
        {
            failure = ex is InvalidOperationException ? ex.Message : $"'{name}' failed — {ex.Message}";
        }

        await Task.Delay(SettleDelay);

        _actionRunning = false;
        RefreshState();

        // Shown until the next timer tick refreshes the status.
        if (failure != null)
            Status = failure;
    }
}
