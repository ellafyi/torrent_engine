namespace Downpour.Storage.Models;

public class Torrent
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string InfoHash { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public long UploadedBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public string SavePath { get; set; } = string.Empty;
    public TorrentStatus Status { get; set; } = TorrentStatus.Paused;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public byte[]? TorrentFileData { get; set; }
    public byte[]? PieceBitfield { get; set; }
}