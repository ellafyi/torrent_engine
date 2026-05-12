using CommunityToolkit.Maui.Storage;

namespace Downpour.App.Services;

public class FilePickerService : IFilePickerService
{
    public async Task<string?> PickTorrentFileAsync()
    {
        var result = await FilePicker.PickAsync(new PickOptions
        {
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, [".torrent"] }
            }),
            PickerTitle = "Select a .torrent file"
        });
        return result?.FullPath;
    }

    public async Task<string?> PickFolderAsync()
    {
        var result = await FolderPicker.Default.PickAsync();
        return result.IsSuccessful ? result.Folder.Path : null;
    }
}
