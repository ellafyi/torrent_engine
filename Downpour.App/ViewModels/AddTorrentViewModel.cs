using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Downpour.App.ViewModels;

public partial class AddTorrentViewModel : ObservableObject
{
    private TaskCompletionSource<(string TorrentFilePath, string DownloadPath)?> _tcs = new();

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
        _tcs = new TaskCompletionSource<(string, string)?>();
    }

    public Task<(string TorrentFilePath, string DownloadPath)?> WaitForResultAsync() => _tcs.Task;

    public void Cancel() => _tcs.TrySetResult(null);

    [RelayCommand]
    private async Task BrowseTorrentFile()
    {
        var result = await FilePicker.PickAsync(new PickOptions
        {
            PickerTitle = "Select .torrent file",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, [".torrent"] }
            })
        });

        if (result != null)
            TorrentFilePath = result.FullPath;
    }

    [RelayCommand]
    private async Task BrowseDownloadFolder()
    {
        var result = await FolderPicker.Default.PickAsync(CancellationToken.None);
        if (result.IsSuccessful)
            DownloadPath = result.Folder.Path;
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => _tcs.TrySetResult((TorrentFilePath!, DownloadPath!));

    private bool CanConfirm() =>
        !string.IsNullOrEmpty(TorrentFilePath) && !string.IsNullOrEmpty(DownloadPath);

    [RelayCommand]
    private void CancelDialog() => Cancel();
}
