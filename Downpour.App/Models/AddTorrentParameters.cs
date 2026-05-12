namespace Downpour.App.Models;

public record AddTorrentParameters(IReadOnlyList<string> TorrentFilePaths, string DownloadPath);
