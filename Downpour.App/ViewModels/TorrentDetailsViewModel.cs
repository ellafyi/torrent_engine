using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Downpour.App.ViewModels;

public partial class TorrentDetailsViewModel : ObservableObject, IDisposable
{
    private readonly int _torrentId;
    private readonly MainViewModel _main;
    private readonly TaskCompletionSource _closeTcs = new();

    public TorrentItemViewModel Item { get; }

    private IReadOnlyList<long> _downloadHistory = [];
    public IReadOnlyList<long> DownloadHistory { get => _downloadHistory; private set => SetProperty(ref _downloadHistory, value); }

    private IReadOnlyList<long> _uploadHistory = [];
    public IReadOnlyList<long> UploadHistory { get => _uploadHistory; private set => SetProperty(ref _uploadHistory, value); }

    public TorrentDetailsViewModel(TorrentItemViewModel item, MainViewModel main)
    {
        Item = item;
        _torrentId = item.TorrentId;
        _main = main;
        _main.TorrentSpeedHistoryUpdated += OnHistoryUpdated;
        Refresh();
    }

    private void OnHistoryUpdated()
    {
        DownloadHistory = _main.GetTorrentDownloadHistory(_torrentId);
        UploadHistory   = _main.GetTorrentUploadHistory(_torrentId);
    }

    private void Refresh()
    {
        DownloadHistory = _main.GetTorrentDownloadHistory(_torrentId);
        UploadHistory   = _main.GetTorrentUploadHistory(_torrentId);
    }

    public Task WaitForClosedAsync() => _closeTcs.Task;

    [RelayCommand]
    private void Close() => _closeTcs.TrySetResult();

    public void Cancel() => _closeTcs.TrySetResult();

    public void Dispose()
    {
        _main.TorrentSpeedHistoryUpdated -= OnHistoryUpdated;
    }
}
