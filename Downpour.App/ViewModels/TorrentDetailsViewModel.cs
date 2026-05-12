using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Downpour.App.Services;

namespace Downpour.App.ViewModels;

public partial class TorrentDetailsViewModel : ObservableObject
{
    private readonly int _torrentId;
    private readonly ISpeedHistoryService _speedHistory;
    private readonly TaskCompletionSource _closeTcs = new();

    public TorrentItemViewModel Item { get; }

    private IReadOnlyList<long> _downloadHistory = [];
    public IReadOnlyList<long> DownloadHistory { get => _downloadHistory; private set => SetProperty(ref _downloadHistory, value); }

    private IReadOnlyList<long> _uploadHistory = [];
    public IReadOnlyList<long> UploadHistory { get => _uploadHistory; private set => SetProperty(ref _uploadHistory, value); }

    public TorrentDetailsViewModel(TorrentItemViewModel item, ISpeedHistoryService speedHistory)
    {
        Item = item;
        _torrentId = item.TorrentId;
        _speedHistory = speedHistory;
        _speedHistory.HistoryUpdated += OnHistoryUpdated;
        Refresh();
    }

    private void OnHistoryUpdated()
    {
        DownloadHistory = _speedHistory.GetTorrentDownloadHistory(_torrentId);
        UploadHistory   = _speedHistory.GetTorrentUploadHistory(_torrentId);
    }

    private void Refresh()
    {
        DownloadHistory = _speedHistory.GetTorrentDownloadHistory(_torrentId);
        UploadHistory   = _speedHistory.GetTorrentUploadHistory(_torrentId);
    }

    public Task WaitForClosedAsync() => _closeTcs.Task;

    [RelayCommand]
    private void Close() => Cancel();

    public void Cancel()
    {
        _speedHistory.HistoryUpdated -= OnHistoryUpdated;
        _closeTcs.TrySetResult();
    }
}
