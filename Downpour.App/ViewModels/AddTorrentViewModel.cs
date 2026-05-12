using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Downpour.App.Models;
using Downpour.App.Services;

namespace Downpour.App.ViewModels;

public partial class AddTorrentViewModel : ObservableObject
{
    private readonly IFilePickerService _filePicker;
    private TaskCompletionSource<AddTorrentParameters?> _tcs = new();

    public AddTorrentViewModel(IFilePickerService filePicker)
    {
        _filePicker = filePicker;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string? TorrentFilePath { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string? DownloadPath { get; set; }

    public void Reset()
    {
        TorrentFilePath = null;
        DownloadPath = null;
        _tcs = new TaskCompletionSource<AddTorrentParameters?>();
    }

    public Task<AddTorrentParameters?> WaitForResultAsync() => _tcs.Task;

    public void Cancel() => _tcs.TrySetResult(null);

    [RelayCommand]
    private async Task BrowseTorrentFile()
    {
        TorrentFilePath = await _filePicker.PickTorrentFileAsync();
    }

    [RelayCommand]
    private async Task BrowseDownloadFolder()
    {
        DownloadPath = await _filePicker.PickFolderAsync();
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => _tcs.TrySetResult(new AddTorrentParameters(TorrentFilePath!, DownloadPath!));

    private bool CanConfirm() =>
        !string.IsNullOrEmpty(TorrentFilePath) && !string.IsNullOrEmpty(DownloadPath);

    [RelayCommand]
    private void CancelDialog() => Cancel();
}
