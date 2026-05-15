using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Downpour.App.Services;

namespace Downpour.App.ViewModels;

public partial class TorrentDetailsViewModel : ObservableObject
{
    private readonly TaskCompletionSource _closeTcs = new();
    private readonly ISpeedHistoryService _speedHistory;
    private readonly int _torrentId;

    public TorrentDetailsViewModel(TorrentItemViewModel item, ISpeedHistoryService speedHistory)
    {
        Item = item;
        _torrentId = item.TorrentId;
        _speedHistory = speedHistory;
        _speedHistory.HistoryUpdated += OnHistoryUpdated;
        Refresh();
    }

    public TorrentItemViewModel Item { get; }

    [ObservableProperty] public partial IReadOnlyList<long> DownloadHistory { get; private set; } = [];

    public IReadOnlyList<long> UploadHistory
    {
        get;
        private set => SetProperty(ref field, value);
    } = [];

    private void OnHistoryUpdated()
    {
        DownloadHistory = _speedHistory.GetTorrentDownloadHistory(_torrentId);
        UploadHistory = _speedHistory.GetTorrentUploadHistory(_torrentId);
    }

    private void Refresh()
    {
        DownloadHistory = _speedHistory.GetTorrentDownloadHistory(_torrentId);
        UploadHistory = _speedHistory.GetTorrentUploadHistory(_torrentId);
    }

    public Task WaitForClosedAsync()
    {
        return _closeTcs.Task;
    }

    [RelayCommand]
    private void Close()
    {
        Cancel();
    }

    public void Cancel()
    {
        _speedHistory.HistoryUpdated -= OnHistoryUpdated;
        _closeTcs.TrySetResult();
    }
}