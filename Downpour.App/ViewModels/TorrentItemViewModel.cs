using CommunityToolkit.Mvvm.ComponentModel;
using Downpour.Engine.Types;

namespace Downpour.App.ViewModels;

public partial class TorrentItemViewModel : ObservableObject
{
    [ObservableProperty] public partial int TorrentId { get; set; }
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusLabel { get; set; } = string.Empty;
    [ObservableProperty] public partial Color StatusTextColor { get; set; } = Color.FromArgb("#FF2196F3");
    [ObservableProperty] public partial Color CardBackgroundColor { get; set; } = Color.FromArgb("#142196F3");
    [ObservableProperty] public partial double ProgressFraction { get; set; }
    [ObservableProperty] public partial string ProgressPercent { get; set; } = "0%";
    [ObservableProperty] public partial string SizeText { get; set; } = string.Empty;
    [ObservableProperty] public partial string DownloadSpeed { get; set; } = string.Empty;
    [ObservableProperty] public partial string UploadSpeed { get; set; } = string.Empty;
    [ObservableProperty] public partial string PeerCountText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool CanPause { get; set; }
    [ObservableProperty] public partial bool CanResume { get; set; }

    public void UpdateStatus(TorrentStatus status)
    {
        StatusLabel = GetStatusLabel(status);
        CanPause = status.IsDownloading || status.IsSeeding || status.IsChecking;
        CanResume = status.IsPaused;
        (CardBackgroundColor, StatusTextColor) = GetStatusColors(status);
    }

    public void Update(TorrentProgress p)
    {
        TorrentId = p.TorrentId;
        Name = p.Name;
        UpdateStatus(p.Status);
        ProgressFraction = p.TotalBytes > 0
            ? Math.Clamp((double)p.DownloadedBytes / p.TotalBytes, 0.0, 1.0)
            : 0.0;
        ProgressPercent = $"{ProgressFraction * 100:F0}%";
        SizeText = $"{FormatBytes(p.DownloadedBytes)} / {FormatBytes(p.TotalBytes)} ({ProgressFraction:P0})";
        DownloadSpeed = $"↓ {FormatSpeed(p.DownloadSpeedBps)}";
        UploadSpeed = $"↑ {FormatSpeed(p.UploadSpeedBps)}";
        PeerCountText = $"{p.PeerCount}p";
    }

    private static string GetStatusLabel(TorrentStatus status)
    {
        if (status.IsChecking) return "Checking";
        if (status.IsDownloading) return "Downloading";
        if (status.IsSeeding) return "Seeding";
        if (status.IsPaused) return "Paused";
        if (status is TorrentStatus.Errored err) return $"Error: {err.message}";
        return "Unknown";
    }

    private static (Color background, Color text) GetStatusColors(TorrentStatus status)
    {
        if (status.IsDownloading) return (Color.FromArgb("#142196F3"), Color.FromArgb("#FF2196F3"));
        if (status.IsSeeding)     return (Color.FromArgb("#144CAF50"), Color.FromArgb("#FF4CAF50"));
        if (status.IsPaused)      return (Color.FromArgb("#14FF9800"), Color.FromArgb("#FFFF9800"));
        return                          (Color.FromArgb("#14F44336"), Color.FromArgb("#FFF44336"));
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_000_000_000 => $"{bytes / 1_000_000_000.0:F1} GB",
        >= 1_000_000 => $"{bytes / 1_000_000.0:F1} MB",
        >= 1_000 => $"{bytes / 1_000.0:F1} KB",
        _ => $"{bytes} B"
    };

    private static string FormatSpeed(long bps) => bps switch
    {
        >= 1_000_000 => $"{bps / 1_000_000.0:F1} MB/s",
        >= 1_000 => $"{bps / 1_000.0:F1} KB/s",
        _ => $"{bps} B/s"
    };
}
