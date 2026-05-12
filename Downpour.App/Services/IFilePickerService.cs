namespace Downpour.App.Services;

public interface IFilePickerService
{
    Task<IReadOnlyList<string>> PickTorrentFilesAsync();
    Task<string?> PickFolderAsync();
}
