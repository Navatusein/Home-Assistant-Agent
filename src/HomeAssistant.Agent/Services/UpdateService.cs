using System.IO;
using HomeAssistant.Agent.Core;
using HomeAssistant.Agent.Core.Platform;
using HomeAssistant.Agent.Core.Plugins;
using HomeAssistant.Agent.Core.Services;
using HomeAssistant.Agent.Core.Updates;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Services;

// Keeps the agent and its plugins current from the catalogs (index.json): the
// official one, which also lists the agent, and the ones the user added.
//
// The agent updates through its installer, run silently with one UAC prompt;
// the installer closes this app, replaces everything and starts it again.
// Plugins change through the service executable's elevated "plugins" step
// (see PluginStep): this side downloads and verifies the files, starts that
// step and exits, since the plugin dlls stay locked while the app runs.
public sealed class UpdateService : IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly AgentService _agentService;
    private readonly PluginManager _pluginManager;
    private readonly TrayIconService _trayIcon;
    private readonly IElevatedLauncher _elevation;
    private readonly ILogger<UpdateService> _logger;
    private readonly CatalogClient _catalog = new();
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();

    public UpdateService(AgentService agentService, PluginManager pluginManager, TrayIconService trayIcon,
        IElevatedLauncher elevation, ILogger<UpdateService> logger)
    {
        _agentService = agentService;
        _pluginManager = pluginManager;
        _trayIcon = trayIcon;
        _elevation = elevation;
        _logger = logger;

        // Left by the elevated plugin step before it restarted this app.
        LastPluginReport = PluginOperations.TakeReport();
    }

    public UpdateCheckResult Result { get; private set; } = UpdateCheckResult.Empty;

    public PluginReport? LastPluginReport { get; }

    public bool IsChecking { get; private set; }

    // A portable copy (a development build) has no installer to run, and the
    // installer is a Windows one.
    public bool CanUpdateAgent => !AgentPaths.IsPortable && OperatingSystem.IsWindows();

    // Raised after every check, on a background thread.
    public event Action? Changed;

    public static string InstalledVersion => AgentInfo.Version.ToString(3);

    // The periodic check, when the user has it on. Started once the agent runs.
    public void Start()
    {
        _ = RunAsync(_stopping.Token);
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        await _checkLock.WaitAsync(cancellationToken);

        try
        {
            IsChecking = true;
            Changed?.Invoke();

            var sources = CatalogClient.GetSources(_agentService.General.PluginSources);

            // The agent's own index alongside the plugin catalogs: it is where
            // the agent's releases are listed, and only that is read from it.
            var agentFetch = _catalog.FetchAsync(new CatalogSource(CatalogClient.AgentIndexUrl, IsOfficial: true), cancellationToken);
            var fetched = await Task.WhenAll(sources.Select(source => _catalog.FetchAsync(source, cancellationToken)));
            var agentIndex = await agentFetch;

            foreach (var failed in fetched.Append(agentIndex).Where(result => result.Error != null))
                _logger.LogWarning("Catalog {Catalog} could not be read: {Error}", failed.Source.Url, failed.Error);

            var installed = _pluginManager.Plugins.ToDictionary(
                plugin => plugin.PluginCode,
                plugin => Version.TryParse(plugin.Info.Version, out var version) ? version : null,
                StringComparer.OrdinalIgnoreCase);

            Result = UpdateCheck.Evaluate(AgentInfo.Version, PluginManager.ApiVersion, installed,
                new PluginLock(PluginLock.DefaultPath).Read(), fetched, agentIndex);

            var pluginUpdates = Result.Plugins.Count(offer => offer.State == PluginOfferState.UpdateAvailable);

            _logger.LogInformation("Update check: agent {Agent}, {Count} plugin update(s), {Available} plugin(s) available",
                Result.AgentUpdate?.Version ?? "up to date", pluginUpdates,
                Result.Plugins.Count(offer => offer.State == PluginOfferState.Available));

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                _trayIcon.ShowUpdate(CanUpdateAgent ? Result.AgentUpdate?.Version : null));

            return Result;
        }
        finally
        {
            IsChecking = false;
            _checkLock.Release();
            Changed?.Invoke();
        }
    }

    // Downloads the installer, checks it against the catalog and runs it. The
    // installer then closes this app; returns once it has been started.
    public async Task InstallAgentUpdateAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!CanUpdateAgent)
            throw new InvalidOperationException("A portable copy is updated by replacing its folder.");

        var release = Result.AgentUpdate ?? throw new InvalidOperationException("No update is available.");
        var installer = Path.Combine(Path.GetTempPath(), $"HomeAssistantAgent-Setup-{release.Version}.exe");

        _logger.LogInformation("Downloading agent {Version} from {Url}", release.Version, release.Url);

        await _catalog.DownloadAsync(release.Url, release.Sha256, installer, progress, cancellationToken);

        _logger.LogInformation("Starting the installer of {Version}", release.Version);

        StartElevated(installer, ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART"]);
    }

    // Downloads and verifies the new plugin files, starts the elevated step
    // and exits the app, which the step starts again when it is done.
    public async Task ChangePluginsAsync(IReadOnlyList<PluginOffer> installs, IReadOnlyList<PluginRemoval> removals,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var folder = Directory.CreateTempSubdirectory("ha-agent-plugins-").FullName;
        var requests = new List<PluginInstall>();

        for (var i = 0; i < installs.Count; i++)
        {
            var plugin = installs[i].Plugin;
            var file = Path.Combine(folder, plugin.FileName);
            var step = i;

            _logger.LogInformation("Downloading plugin {PluginCode} {Version} from {Url}", plugin.Code, plugin.Version, plugin.Url);

            await _catalog.DownloadAsync(plugin.Url, plugin.Sha256, file,
                progress == null ? null : new Progress<double>(p => progress.Report((step + p) / installs.Count)),
                cancellationToken);

            requests.Add(new PluginInstall(file, plugin.Sha256, plugin.Code, plugin.Version, installs[i].Source.Url, plugin.FileName));
        }

        if (!OperatingSystem.IsWindows())
        {
            await ChangePluginsInPlaceAsync(requests, removals, cancellationToken);
            return;
        }

        var request = new PluginRequest(requests, removals, Environment.ProcessId, Relaunch: "app");
        var arguments = PluginOperations.ToArguments(request);

        _logger.LogInformation("Applying plugin changes: {Arguments}", string.Join(' ', arguments.Skip(1)));

        StartElevated(BackgroundServiceInfo.ExecutablePath, arguments);

        // The dlls are locked while this app runs; the step waits for it.
        Avalonia.Threading.Dispatcher.UIThread.Post(_trayIcon.RequestExit);
    }

    // macOS: files in use can be replaced, so the step runs while the app
    // waits for it — the password prompt is part of it, and a declined one
    // comes back here as OperationCanceledException with the app still
    // running. The app then restarts to load the plugins; the replacement
    // shows the step's report.
    private async Task ChangePluginsInPlaceAsync(IReadOnlyList<PluginInstall> installs,
        IReadOnlyList<PluginRemoval> removals, CancellationToken cancellationToken)
    {
        var arguments = PluginOperations.ToArguments(new PluginRequest(installs, removals, WaitForProcess: null, Relaunch: null));

        _logger.LogInformation("Applying plugin changes: {Arguments}", string.Join(' ', arguments.Skip(1)));

        var exitCode = await _elevation.RunAsync(BackgroundServiceInfo.ExecutablePath, arguments, cancellationToken);

        if (exitCode != 0)
        {
            var report = PluginOperations.TakeReport();
            throw new InvalidOperationException(report?.Message ?? $"The plugin step failed (exit code {exitCode}).");
        }

        AgentRestart.Start();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(FirstCheckDelay, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                if (_agentService.General.CheckForUpdates)
                {
                    try
                    {
                        await CheckAsync(cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Update check failed");
                    }
                }

                await Task.Delay(CheckInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    // Throws OperationCanceledException when the user declines the prompt.
    private void StartElevated(string file, IReadOnlyList<string> arguments) =>
        _elevation.Start(file, arguments)?.Dispose();
}
