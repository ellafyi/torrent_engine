using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Downpour.App.Services;
using Downpour.Engine;
using Downpour.Engine.Types;
using Timer = System.Timers.Timer;

namespace Downpour.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly Dictionary<int, (long down, long up)> _currentSpeeds = new();
    private readonly IEngine _engine;
    private readonly ConcurrentDictionary<int, TorrentProgress> _pendingProgress = new();
    private readonly SettingsService _settingsService;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialog;
    private readonly ISpeedHistoryService _speedHistory;
    private Timer? _flushTimer;
    private IDisposable? _subscription;

    public MainViewModel(IEngine engine, SettingsService settingsService,
        INavigationService navigation, IDialogService dialog, ISpeedHistoryService speedHistory)
    {
        _engine = engine;
        _settingsService = settingsService;
        _navigation = navigation;
        _dialog = dialog;
        _speedHistory = speedHistory;
        AllTimeStats = "↓ 0 B  ↑ 0 B";
        RefreshThrottleDisplay();
    }

    [ObservableProperty] public partial ObservableCollection<TorrentItemViewModel> Torrents { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial TorrentItemViewModel? SelectedTorrent { get; set; }

    public bool HasSelection => SelectedTorrent != null;

    [ObservableProperty] public partial string GlobalDownloadSpeed { get; set; } = "↓ 0 B/s";

    [ObservableProperty] public partial string GlobalUploadSpeed { get; set; } = "↑ 0 B/s";

    private string _allTimeStats = "↓ 0 B  ↑ 0 B";
    public string AllTimeStats { get => _allTimeStats; private set => SetProperty(ref _allTimeStats, value); }

    private IReadOnlyList<long> _globalDownloadHistory = [];
    public IReadOnlyList<long> GlobalDownloadHistory { get => _globalDownloadHistory; private set => SetProperty(ref _globalDownloadHistory, value); }

    private IReadOnlyList<long> _globalUploadHistory = [];
    public IReadOnlyList<long> GlobalUploadHistory { get => _globalUploadHistory; private set => SetProperty(ref _globalUploadHistory, value); }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThrottleLimits))]
    public partial string ThrottleLimitText { get; set; } = "";

    public bool HasThrottleLimits => ThrottleLimitText.Length > 0;

    private void RefreshThrottleDisplay()
    {
        var s = _settingsService.Load();
        var parts = new List<string>();
        if (s.MaxDownloadSpeedKbps > 0) parts.Add($"↓ {s.MaxDownloadSpeedKbps} KB/s");
        if (s.MaxUploadSpeedMbps > 0) parts.Add($"↑ {s.MaxUploadSpeedMbps} MB/s");
        ThrottleLimitText = string.Join("  ", parts);
    }

    public async Task InitializeAsync()
    {
        _subscription = _engine.Events.Subscribe(new EventObserver(OnEngineEvent));
        await _engine.StartAsync();

        _flushTimer = new Timer(1000);
        _flushTimer.Elapsed += (_, _) => FlushProgressToUI();
        _flushTimer.AutoReset = true;
        _flushTimer.Start();

        var initial = _engine.GetTorrents();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var p in initial)
            {
                var vm = new TorrentItemViewModel();
                vm.Update(p);
                Torrents.Add(vm);
            }
        });
    }

    private void OnEngineEvent(EngineEvent ev)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            switch (ev)
            {
                case EngineEvent.Progress { Item: var p }:
                    _pendingProgress[p.TorrentId] = p;
                    break;

                case EngineEvent.StatusChanged sc:
                    _pendingProgress.TryRemove(sc.torrentId, out _);
                    var scItem = Torrents.FirstOrDefault(t => t.TorrentId == sc.torrentId);
                    if (scItem != null) scItem.UpdateStatus(sc.Item2);
                    if (sc.Item2.IsPaused || sc.Item2 is TorrentStatus.Errored)
                    {
                        _currentSpeeds.Remove(sc.torrentId);
                        if (scItem != null)
                        {
                            scItem.DownloadSpeed = "↓ 0 B/s";
                            scItem.UploadSpeed = "↑ 0 B/s";
                        }
                        UpdateGlobalSpeedStrings();
                    }
                    break;

                case EngineEvent.TorrentRemoved removed:
                    _pendingProgress.TryRemove(removed.torrentId, out _);
                    var toRemove = Torrents.FirstOrDefault(t => t.TorrentId == removed.torrentId);
                    if (toRemove != null)
                    {
                        if (SelectedTorrent == toRemove) SelectedTorrent = null;
                        Torrents.Remove(toRemove);
                    }
                    _currentSpeeds.Remove(removed.torrentId);
                    _speedHistory.RemoveTorrent(removed.torrentId);
                    UpdateGlobalSpeedStrings();
                    break;

                case EngineEvent.GlobalStatsUpdate gs:
                    AllTimeStats = $"↓ {FormatBytes(gs.downloaded)}  ↑ {FormatBytes(gs.uploaded)}";
                    break;

                case EngineEvent.Error err:
                    var errItem = Torrents.FirstOrDefault(t => t.TorrentId == err.torrentId);
                    if (errItem != null) errItem.StatusLabel = "Error: " + err.message;
                    break;
            }
        });
    }

    private void FlushProgressToUI()
    {
        var snapshot = _pendingProgress.IsEmpty ? [] : _pendingProgress.ToArray();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var (_, p) in snapshot)
            {
                var item = Torrents.FirstOrDefault(t => t.TorrentId == p.TorrentId);
                if (item == null)
                {
                    item = new TorrentItemViewModel();
                    Torrents.Add(item);
                }
                item.Update(p);
                _currentSpeeds[p.TorrentId] = (p.DownloadSpeedBps, p.UploadSpeedBps);
            }

            if (snapshot.Length > 0) UpdateGlobalSpeedStrings();

            _speedHistory.RecordSamples(_currentSpeeds);
            GlobalDownloadHistory = _speedHistory.GlobalDownloadHistory;
            GlobalUploadHistory   = _speedHistory.GlobalUploadHistory;
        });
    }

    private void UpdateGlobalSpeedStrings()
    {
        var totalDown = _currentSpeeds.Values.Sum(s => s.down);
        var totalUp = _currentSpeeds.Values.Sum(s => s.up);
        GlobalDownloadSpeed = $"↓ {FormatSpeed(totalDown)}";
        GlobalUploadSpeed = $"↑ {FormatSpeed(totalUp)}";
    }

    private static string FormatSpeed(long bps)
    {
        return bps switch
        {
            >= 1_000_000 => $"{bps / 1_000_000.0:F1} MB/s",
            >= 1_000 => $"{bps / 1_000.0:F1} KB/s",
            _ => $"{bps} B/s"
        };
    }

    internal static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1_024 => $"{bytes / 1_024.0:F1} KB",
            _ => $"{bytes} B"
        };
    }

    partial void OnSelectedTorrentChanged(TorrentItemViewModel? oldValue, TorrentItemViewModel? newValue)
    {
        if (oldValue != null) oldValue.PropertyChanged -= OnSelectedItemPropertyChanged;
        if (newValue != null) newValue.PropertyChanged += OnSelectedItemPropertyChanged;
    }

    private void OnSelectedItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TorrentItemViewModel.CanPause) or nameof(TorrentItemViewModel.CanResume))
            OnPropertyChanged(nameof(SelectedTorrent));
    }

    [RelayCommand]
    private async Task AddTorrent()
    {
        var result = await _navigation.ShowAddTorrentAsync();
        if (result == null) return;

        foreach (var path in result.TorrentFilePaths)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(path);
                var torrentId = await _engine.AddTorrentAsync(bytes, result.DownloadPath);

                if (!Torrents.Any(t => t.TorrentId == torrentId))
                {
                    Torrents.Add(new TorrentItemViewModel
                    {
                        TorrentId = torrentId,
                        Name = Path.GetFileNameWithoutExtension(path),
                        StatusLabel = "Starting..."
                    });
                }
            }
            catch (Exception ex)
            {
                await _dialog.ShowErrorAsync("Error",
                    $"Failed to add '{Path.GetFileName(path)}': {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private async Task PauseTorrent()
    {
        if (SelectedTorrent == null || !SelectedTorrent.CanPause) return;
        await _engine.PauseTorrentAsync(SelectedTorrent.TorrentId);
    }

    [RelayCommand]
    private async Task ResumeTorrent()
    {
        if (SelectedTorrent == null || !SelectedTorrent.CanResume) return;
        await _engine.ResumeTorrentAsync(SelectedTorrent.TorrentId);
    }

    [RelayCommand]
    private async Task RemoveTorrent()
    {
        if (SelectedTorrent == null) return;
        await _engine.RemoveTorrentAsync(SelectedTorrent.TorrentId, false);
    }

    [RelayCommand]
    private async Task RemoveAndDelete()
    {
        if (SelectedTorrent == null) return;
        await _engine.RemoveTorrentAsync(SelectedTorrent.TorrentId, true);
    }

    [RelayCommand]
    private async Task OpenSettings()
    {
        var result = await _navigation.ShowSettingsAsync(_settingsService.Load());
        if (result == null) return;
        _settingsService.Save(result);
        await _engine.UpdateSettingsAsync(result);
        RefreshThrottleDisplay();
    }

    [RelayCommand]
    private async Task ClearDatabase()
    {
        var ok = await _dialog.ConfirmAsync(
            "Clear Database",
            "Are you sure you want to delete all torrents from the database? This action cannot be undone.");
        if (ok) await _engine.ClearDatabaseAsync();
    }

    [RelayCommand]
    private async Task OpenDetails()
    {
        if (SelectedTorrent == null) return;
        await _navigation.ShowDetailsAsync(SelectedTorrent);
    }

    public async Task ShutdownAsync()
    {
        _flushTimer?.Stop();
        _flushTimer?.Dispose();
        _subscription?.Dispose();
        await _engine.StopAsync();
    }

    private sealed class EventObserver(Action<EngineEvent> onNext) : IObserver<EngineEvent>
    {
        public void OnNext(EngineEvent value) => onNext(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}
