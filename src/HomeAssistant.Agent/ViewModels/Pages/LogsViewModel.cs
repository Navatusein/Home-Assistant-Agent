using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using HomeAssistant.Agent.Core.Logging;
using HomeAssistant.Agent.Core.Platform;
using HomeAssistant.Agent.Core.Plugins;
using HomeAssistant.Agent.Core.Services;
using HomeAssistant.Agent.Logging;
using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Services;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace HomeAssistant.Agent.ViewModels.Pages;

public partial class LogsViewModel : ObservableObject, INavigationAware
{
    public const string ApplicationView = "Application";
    public const string ServiceView = "Service";

    private const string AllSources = "All sources";
    private const string AgentSource = "Agent";

    // The service writes to its file on its own schedule; re-reading the
    // tail this often keeps the view close to live without hammering the disk.
    private static readonly TimeSpan ServiceRefreshInterval = TimeSpan.FromSeconds(3);

    private readonly PluginManager _pluginManager;
    private readonly AgentService _agentService;
    private readonly IShell _shell;
    private readonly ILogger<LogsViewModel> _logger;
    private readonly DispatcherTimer _serviceTimer;

    private readonly List<LogEntry> _serviceEntries = [];

    // The app's earlier runs, read back from its log files; this run comes
    // from the live buffer.
    private readonly List<LogEntry> _appHistory = [];

    private LogEventLevel _minimumLevel = LogEventLevel.Verbose;
    private bool _isShown;

    public LogsViewModel(PluginManager pluginManager, AgentService agentService, IShell shell,
        ILogger<LogsViewModel> logger)
    {
        _pluginManager = pluginManager;
        _agentService = agentService;
        _shell = shell;
        _logger = logger;

        Sources = [AllSources, AgentSource, .. pluginManager.Plugins.Select(plugin => plugin.PluginCode)];

        _serviceTimer = new DispatcherTimer { Interval = ServiceRefreshInterval };
        _serviceTimer.Tick += (_, _) => LoadServiceLog();

        // New entries of the live buffer go straight onto the page when they
        // pass the filter, instead of re-filtering everything per line.
        ((INotifyCollectionChanged) LogBuffer.Instance.Entries).CollectionChanged += OnBufferChanged;
    }

    public IReadOnlyList<string> Views { get; } = [ApplicationView, ServiceView];

    public IReadOnlyList<string> Levels { get; } = LogLevels.Names;

    public IReadOnlyList<string> Sources { get; }

    // What the list shows: the selected log, filtered.
    public ObservableCollection<LogEntry> Entries { get; } = [];

    public bool IsServiceView => SelectedView == ServiceView;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsServiceView))]
    private string _selectedView = ApplicationView;

    [ObservableProperty]
    private string _selectedLevel = LogLevels.Trace;

    [ObservableProperty]
    private string _selectedSource = AllSources;

    [ObservableProperty]
    private string _searchText = string.Empty;

    // Off: only the current run (of the app, or of the service). On: the
    // earlier runs too, as far back as the recent log files go.
    [ObservableProperty]
    private bool _showPreviousRuns;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private LogEntry? _selectedEntry;

    public bool HasSelection => SelectedEntry != null;

    // Where the service view stands: which file, or why there is nothing.
    [ObservableProperty]
    private string _serviceStatus = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private NotificationType _statusSeverity = NotificationType.Information;

    [ObservableProperty]
    private bool _isStatusOpen;

    public void OnNavigatedTo()
    {
        _isShown = true;

        if (!IsServiceView && ShowPreviousRuns)
            LoadAppHistory();

        Rebuild();

        if (IsServiceView)
            StartServiceRefresh();
    }

    public void OnNavigatedFrom()
    {
        _isShown = false;
        _serviceTimer.Stop();

        // Nothing is on screen; the list is rebuilt on the next visit.
        SelectedEntry = null;
        Entries.Clear();
    }

    partial void OnSelectedViewChanged(string value)
    {
        SelectedEntry = null;

        if (IsServiceView && _isShown)
            StartServiceRefresh();
        else
            _serviceTimer.Stop();

        if (!IsServiceView && ShowPreviousRuns && _isShown)
            LoadAppHistory();

        Rebuild();
    }

    partial void OnShowPreviousRunsChanged(bool value)
    {
        if (value && !IsServiceView && _isShown)
            LoadAppHistory();

        if (!value)
            _appHistory.Clear();

        Rebuild();
    }

    partial void OnSelectedLevelChanged(string value)
    {
        _minimumLevel = LogLevels.TryParse(value, out var level) ? level : LogEventLevel.Verbose;
        Rebuild();
    }

    partial void OnSelectedSourceChanged(string value) => Rebuild();

    partial void OnSearchTextChanged(string value) => Rebuild();

    private void OnBufferChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_isShown || IsServiceView)
            return;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems != null:
                foreach (LogEntry entry in e.NewItems)
                {
                    if (Matches(entry))
                        Entries.Add(entry);
                }
                break;

            // The buffer drops its oldest entry once it is full.
            case NotifyCollectionChangedAction.Remove when e.OldItems != null:
                foreach (LogEntry entry in e.OldItems)
                {
                    if (Entries.Count > 0 && ReferenceEquals(Entries[0], entry))
                        Entries.RemoveAt(0);
                }
                break;

            default:
                Rebuild();
                break;
        }
    }

    private void Rebuild()
    {
        if (!_isShown)
            return;

        var selected = SelectedEntry;
        IEnumerable<LogEntry> source = IsServiceView
            ? ServiceEntriesToShow()
            : ShowPreviousRuns ? _appHistory.Concat(LogBuffer.Instance.Entries) : LogBuffer.Instance.Entries;

        Entries.Clear();

        foreach (var entry in source.Where(Matches))
            Entries.Add(entry);

        SelectedEntry = selected != null && Entries.Contains(selected) ? selected : null;
    }

    // The service's file holds its earlier runs too; the current one starts at
    // its last startup line.
    private IEnumerable<LogEntry> ServiceEntriesToShow()
    {
        if (ShowPreviousRuns)
            return _serviceEntries;

        var start = _serviceEntries.FindLastIndex(entry => entry.IsRunStart);
        return start < 0 ? _serviceEntries : _serviceEntries.Skip(start);
    }

    // What the app logged before the live buffer begins: the earlier runs, and
    // the start of this one if the buffer has dropped it already. Cut at the
    // buffer's first entry, to the millisecond the file keeps.
    private void LoadAppHistory()
    {
        _appHistory.Clear();

        try
        {
            var entries = LogFileReader.ReadRecent(AgentLogging.GetFilePrefix(PluginHostMode.Desktop), out _);

            if (entries == null)
                return;

            var first = LogBuffer.Instance.Entries.Count > 0 ? LogBuffer.Instance.Entries[0].Timestamp : DateTimeOffset.Now;
            var cut = first.AddTicks(-(first.Ticks % TimeSpan.TicksPerMillisecond));

            _appHistory.AddRange(entries.Where(entry => entry.Timestamp < cut));
        }
        catch (IOException ex)
        {
            ShowStatus($"Could not read the earlier logs: {ex.Message}", NotificationType.Warning);
        }
    }

    private bool Matches(LogEntry entry)
    {
        if (entry.Level < _minimumLevel)
            return false;

        var sourceMatches = SelectedSource switch
        {
            AllSources => true,
            AgentSource => entry.Plugin == null,
            _ => string.Equals(entry.Plugin, SelectedSource, StringComparison.OrdinalIgnoreCase)
        };

        if (!sourceMatches)
            return false;

        var search = SearchText.Trim();

        return search.Length == 0
            || entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.Source.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (entry.Exception?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void StartServiceRefresh()
    {
        LoadServiceLog();
        _serviceTimer.Start();
    }

    [RelayCommand]
    private void LoadServiceLog()
    {
        string? path = null;

        try
        {
            var entries = LogFileReader.ReadRecent(AgentLogging.GetFilePrefix(PluginHostMode.Service), out path);

            if (entries == null || path == null)
            {
                _serviceEntries.Clear();
                ServiceStatus = "The service has not written a log yet.";
                Rebuild();
                return;
            }

            // Nothing new — leave the list (and the selection) alone.
            if (entries.Count == _serviceEntries.Count
                && (entries.Count == 0 || entries[^1] == _serviceEntries[^1]))
            {
                return;
            }

            _serviceEntries.Clear();
            _serviceEntries.AddRange(entries);

            ServiceStatus = Path.GetFileName(path);
            Rebuild();
        }
        catch (IOException ex)
        {
            ServiceStatus = $"Could not read {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    // Copies what is on screen — the filter is usually exactly the excerpt
    // worth pasting into an issue.
    [RelayCommand]
    private async Task CopyVisibleAsync()
    {
        var lines = Entries.Select(entry => entry.FullText).ToArray();

        if (lines.Length == 0)
            return;

        if (await Dialogs.SetClipboardTextAsync(string.Join(Environment.NewLine, lines)))
            ShowStatus($"Copied {lines.Length} entries.", NotificationType.Success);
    }

    [RelayCommand]
    private async Task CollectDiagnosticsAsync()
    {
        var path = await Dialogs.SaveFileAsync("Save diagnostics", DiagnosticsBundle.DefaultFileName,
            "Zip archive", "*.zip", Environment.GetFolderPath(Environment.SpecialFolder.Desktop));

        if (path == null)
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);

            DiagnosticsBundle.Create(path, _pluginManager, _agentService);

            _logger.LogInformation("Diagnostics saved to {Path}", path);
            ShowStatus("Diagnostics saved. Secrets are masked; check the archive before sharing it.", NotificationType.Success);

            _shell.RevealFile(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect diagnostics");
            ShowStatus($"Failed to collect diagnostics: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        var folder = AgentLogging.LogsDirectory;

        if (!Directory.Exists(folder))
            return;

        _shell.OpenFolder(folder);
    }

    private void ShowStatus(string message, NotificationType severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
        IsStatusOpen = true;
    }
}
