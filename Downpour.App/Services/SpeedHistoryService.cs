namespace Downpour.App.Services;

public class SpeedHistoryService : ISpeedHistoryService
{
    private const int HistorySize = 120;
    private readonly Queue<(long down, long up)> _globalQueue = new();
    private readonly Dictionary<int, Queue<(long down, long up)>> _torrentQueues = new();

    public IReadOnlyList<long> GlobalDownloadHistory { get; private set; } = [];
    public IReadOnlyList<long> GlobalUploadHistory { get; private set; } = [];
    public event Action? HistoryUpdated;

    public void RecordSamples(IReadOnlyDictionary<int, (long down, long up)> currentSpeeds)
    {
        var totalDown = currentSpeeds.Values.Sum(s => s.down);
        var totalUp = currentSpeeds.Values.Sum(s => s.up);
        Enqueue(_globalQueue, (totalDown, totalUp));
        GlobalDownloadHistory = _globalQueue.Select(s => s.down).ToList();
        GlobalUploadHistory = _globalQueue.Select(s => s.up).ToList();

        foreach (var (id, speeds) in currentSpeeds)
        {
            if (!_torrentQueues.TryGetValue(id, out var q))
                _torrentQueues[id] = q = new Queue<(long, long)>();
            Enqueue(q, speeds);
        }

        HistoryUpdated?.Invoke();
    }

    public void RemoveTorrent(int torrentId)
    {
        _torrentQueues.Remove(torrentId);
    }

    public IReadOnlyList<long> GetTorrentDownloadHistory(int torrentId)
    {
        return _torrentQueues.TryGetValue(torrentId, out var q) ? q.Select(s => s.down).ToList() : [];
    }

    public IReadOnlyList<long> GetTorrentUploadHistory(int torrentId)
    {
        return _torrentQueues.TryGetValue(torrentId, out var q) ? q.Select(s => s.up).ToList() : [];
    }

    private static void Enqueue(Queue<(long, long)> q, (long, long) sample)
    {
        q.Enqueue(sample);
        while (q.Count > HistorySize) q.Dequeue();
    }
}