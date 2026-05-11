using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Downpour.App.Services;
using Downpour.App.Views;
using Downpour.Engine;
using Downpour.Engine.Types;

namespace Downpour.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IEngine _engine;
    private readonly SettingsService _settingsService;
    private IDisposable? _subscription;

    [ObservableProperty]
    public partial ObservableCollection<TorrentItemViewModel> Torrents { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial TorrentItemViewModel? SelectedTorrent { get; set; }

    public bool HasSelection => SelectedTorrent != null;

    [ObservableProperty]
    public partial string GlobalDownloadSpeed { get; set; } = "↓ 0 B/s";

    [ObservableProperty]
    public partial string GlobalUploadSpeed { get; set; } = "↑ 0 B/s";

    private readonly Dictionary<int, (long down, long up)> _currentSpeeds = new();

    public MainViewModel(IEngine engine, SettingsService settingsService)
    {
        _engine = engine;
        _settingsService = settingsService;
    }

    public async Task InitializeAsync()
    {
        _subscription = _engine.Events.Subscribe(new EventObserver(OnEngineEvent));
        await _engine.StartAsync();

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
                    var item = Torrents.FirstOrDefault(t => t.TorrentId == p.TorrentId);
                    if (item == null)
                    {
                        item = new TorrentItemViewModel();
                        Torrents.Add(item);
                    }
                    item.Update(p);
                    
                    _currentSpeeds[p.TorrentId] = (p.DownloadSpeedBps, p.UploadSpeedBps);
                    UpdateGlobalSpeedStrings();
                    break;

                case EngineEvent.StatusChanged sc:
                    var scItem = Torrents.FirstOrDefault(t => t.TorrentId == sc.torrentId);
                    if (scItem != null)
                    {
                        scItem.UpdateStatus(sc.Item2);
                    }
                    break;

                case EngineEvent.TorrentRemoved removed:
                    var toRemove = Torrents.FirstOrDefault(t => t.TorrentId == removed.torrentId);
                    if (toRemove != null)
                    {
                        if (SelectedTorrent == toRemove) SelectedTorrent = null;
                        Torrents.Remove(toRemove);
                    }
                    _currentSpeeds.Remove(removed.torrentId);
                    UpdateGlobalSpeedStrings();
                    break;

                case EngineEvent.Error err:
                    var errItem = Torrents.FirstOrDefault(t => t.TorrentId == err.torrentId);
                    if (errItem != null) errItem.StatusLabel = "Error: " + err.message;
                    break;
            }
        });
    }

    private void UpdateGlobalSpeedStrings()
    {
        long totalDown = _currentSpeeds.Values.Sum(s => s.down);
        long totalUp = _currentSpeeds.Values.Sum(s => s.up);
        GlobalDownloadSpeed = $"↓ {FormatSpeed(totalDown)}";
        GlobalUploadSpeed = $"↑ {FormatSpeed(totalUp)}";
    }

    private static string FormatSpeed(long bps) => bps switch
    {
        >= 1_000_000 => $"{bps / 1_000_000.0:F1} MB/s",
        >= 1_000 => $"{bps / 1_000.0:F1} KB/s",
        _ => $"{bps} B/s"
    };

    partial void OnSelectedTorrentChanged(TorrentItemViewModel? oldValue, TorrentItemViewModel? newValue)
    {
        if (oldValue != null) oldValue.PropertyChanged -= OnSelectedItemPropertyChanged;
        if (newValue != null) newValue.PropertyChanged += OnSelectedItemPropertyChanged;
    }

    private void OnSelectedItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TorrentItemViewModel.CanPause) or nameof(TorrentItemViewModel.CanResume))
            OnPropertyChanged(nameof(SelectedTorrent));
    }

    [RelayCommand]
    private async Task AddTorrent()
    {
        var vm = new AddTorrentViewModel();
        var page = new AddTorrentPage(vm);

        await Shell.Current.Navigation.PushModalAsync(page, false);

        var result = await vm.WaitForResultAsync();
        if (result == null) return;

        try
        {
            var bytes = await File.ReadAllBytesAsync(result.Value.TorrentFilePath);
            var torrentId = await _engine.AddTorrentAsync(bytes, result.Value.DownloadPath);

            if (!Torrents.Any(t => t.TorrentId == torrentId))
            {
                var torrentVm = new TorrentItemViewModel
                {
                    TorrentId = torrentId,
                    Name = Path.GetFileNameWithoutExtension(result.Value.TorrentFilePath),
                    StatusLabel = "Starting..."
                };
                Torrents.Add(torrentVm);
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Error", $"Failed to add torrent: {ex.Message}", "OK");
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
        var vm = new SettingsViewModel();
        vm.Initialize(_settingsService.Load());
        var page = new SettingsPage(vm);
        await Shell.Current.Navigation.PushModalAsync(page, false);
        var result = await vm.WaitForResultAsync();
        if (result == null) return;
        _settingsService.Save(result);
        await _engine.UpdateSettingsAsync(result);
    }

    [RelayCommand]
    private async Task ClearDatabase()
    {
        bool confirm = await Shell.Current.DisplayAlertAsync(
            "Clear Database",
            "Are you sure you want to delete all torrents from the database? This action cannot be undone.",
            "Yes", "No");

        if (confirm)
        {
            await _engine.ClearDatabaseAsync();
        }
    }

    public async Task ShutdownAsync()
    {
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
