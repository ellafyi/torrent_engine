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
        TorrentFilePaths = [];
    }

    private IReadOnlyList<string> TorrentFilePaths
    {
        get;
        set
        {
            if (!SetProperty(ref field, value)) return;
            ConfirmCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(TorrentFileNames));
            OnPropertyChanged(nameof(ConfirmButtonText));
        }
    }

    public string? DownloadPath
    {
        get;
        set
        {
            if (SetProperty(ref field, value)) ConfirmCommand.NotifyCanExecuteChanged();
        }
    }

    public IReadOnlyList<string> TorrentFileNames =>
        TorrentFilePaths.Select(Path.GetFileName).ToList()!;

    public string ConfirmButtonText => TorrentFilePaths.Count > 1
        ? $"Add ({TorrentFilePaths.Count})"
        : "Add";

    public void Reset()
    {
        TorrentFilePaths = [];
        DownloadPath = null;
        _tcs = new TaskCompletionSource<AddTorrentParameters?>();
    }

    public Task<AddTorrentParameters?> WaitForResultAsync()
    {
        return _tcs.Task;
    }

    public void Cancel()
    {
        _tcs.TrySetResult(null);
    }

    [RelayCommand]
    private async Task BrowseTorrentFiles()
    {
        var paths = await _filePicker.PickTorrentFilesAsync();
        if (paths.Count > 0)
            TorrentFilePaths = paths;
    }

    [RelayCommand]
    private async Task BrowseDownloadFolder()
    {
        DownloadPath = await _filePicker.PickFolderAsync();
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        _tcs.TrySetResult(new AddTorrentParameters(TorrentFilePaths, DownloadPath!));
    }

    private bool CanConfirm()
    {
        return TorrentFilePaths.Count > 0 && !string.IsNullOrEmpty(DownloadPath);
    }

    [RelayCommand]
    private void CancelDialog()
    {
        Cancel();
    }
}