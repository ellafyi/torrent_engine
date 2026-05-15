using CommunityToolkit.Maui.Storage;

namespace Downpour.App.Services;

public class FilePickerService : IFilePickerService
{
    public async Task<IReadOnlyList<string>> PickTorrentFilesAsync()
    {
        var results = await FilePicker.PickMultipleAsync(new PickOptions
        {
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, [".torrent"] }
            }),
            PickerTitle = "Select .torrent files"
        });
        return results?.Select(r => r?.FullPath).OfType<string>().ToList() ?? [];
    }

    public async Task<string?> PickFolderAsync()
    {
        var result = await FolderPicker.Default.PickAsync();
        return result.IsSuccessful ? result.Folder.Path : null;
    }
}