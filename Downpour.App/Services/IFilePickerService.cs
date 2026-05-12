namespace Downpour.App.Services;

public interface IFilePickerService
{
    Task<string?> PickTorrentFileAsync();
    Task<string?> PickFolderAsync();
}
