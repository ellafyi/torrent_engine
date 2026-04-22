namespace Downpour.Storage.Models;

public class Torrent
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string InfoHash { get; set; } = string.Empty;
    public long UploadedBytes { get; set; }
    public long DownloadedBytes { get; set; }

    public byte[]? TorrentFileData { get; set; }
}