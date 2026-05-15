namespace Downpour.App.Services;

public interface ISpeedHistoryService
{
    IReadOnlyList<long> GlobalDownloadHistory { get; }
    IReadOnlyList<long> GlobalUploadHistory { get; }
    event Action? HistoryUpdated;
    void RecordSamples(IReadOnlyDictionary<int, (long down, long up)> currentSpeeds);
    void RemoveTorrent(int torrentId);
    IReadOnlyList<long> GetTorrentDownloadHistory(int torrentId);
    IReadOnlyList<long> GetTorrentUploadHistory(int torrentId);
}