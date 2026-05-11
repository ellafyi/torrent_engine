using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Downpour.Engine;
using Downpour.Engine.Types;
using Downpour.App.Views;

namespace Downpour.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IEngine _engine;
    private IDisposable? _subscription;

    [ObservableProperty]
    public partial ObservableCollection<TorrentItemViewModel> Torrents { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial TorrentItemViewModel? SelectedTorrent { get; set; }

    public bool HasSelection => SelectedTorrent != null;

    public MainViewModel(IEngine engine)
    {
        _engine = engine;
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
                    break;

                case EngineEvent.TorrentRemoved removed:
                    var toRemove = Torrents.FirstOrDefault(t => t.TorrentId == removed.torrentId);
                    if (toRemove != null)
                    {
                        if (SelectedTorrent == toRemove) SelectedTorrent = null;
                        Torrents.Remove(toRemove);
                    }
                    break;

                case EngineEvent.Error err:
                    var errItem = Torrents.FirstOrDefault(t => t.TorrentId == err.torrentId);
                    if (errItem != null) errItem.StatusLabel = "Error: " + err.message;
                    break;
            }
        });
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
