using Downpour.Engine;
using Downpour.Engine.Types;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: Downpour.Console <torrent-file> <save-path>");
    return 1;
}

var torrentFile = args[0];
var savePath = args[1];

if (!File.Exists(torrentFile))
{
    Console.Error.WriteLine($"File not found: {torrentFile}");
    return 1;
}

var settings = new EngineSettings((ushort)6881, seedingEnabled: false, maxDownloadSpeedKbps: 0, maxUploadSpeedMbps: 0);
using var engine = Engine.createEngine(settings);

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await engine.StartAsync();

int torrentId;
try
{
    var bytes = await File.ReadAllBytesAsync(torrentFile);
    torrentId = await engine.AddTorrentAsync(bytes, savePath);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to add torrent: {ex.Message}");
    await engine.StopAsync();
    return 1;
}

var done = new TaskCompletionSource();
cts.Token.Register(() => done.TrySetResult());

engine.Events.Subscribe(new Observer(ev =>
{
    switch (ev)
    {
        case EngineEvent.Progress { Item: var p } when p.TorrentId == torrentId:
            PrintProgress(p);
            break;
        case EngineEvent.StatusChanged sc when sc.torrentId == torrentId && sc.Item2.IsSeeding:
            Console.WriteLine("\nComplete.");
            done.TrySetResult();
            break;
        case EngineEvent.Error err when err.torrentId == torrentId:
            Console.Error.WriteLine($"\nError: {err.message}");
            done.TrySetResult();
            break;
    }
}));

await done.Task;
await engine.StopAsync();
return 0;

static void PrintProgress(TorrentProgress p)
{
    var pct = p.TotalBytes > 0 ? 100.0 * p.DownloadedBytes / p.TotalBytes : 0.0;
    string status;
    if      (p.Status.IsChecking)                  status = "Checking";
    else if (p.Status.IsDownloading)               status = $"{pct:F1}%";
    else if (p.Status.IsSeeding)                   status = "Seeding";
    else if (p.Status.IsPaused)                    status = "Paused";
    else if (p.Status is TorrentStatus.Errored e)  status = $"Error: {e.message}";
    else                                           status = "?";
    Console.Write($"\r{p.Name}: {status} | ↓ {Speed(p.DownloadSpeedBps)} ↑ {Speed(p.UploadSpeedBps)} | {p.PeerCount} peers   ");
}

static string Speed(long bps) => bps switch
{
    >= 1_000_000 => $"{bps / 1_000_000.0:F1} MB/s",
    >= 1_000     => $"{bps / 1_000.0:F1} KB/s",
    _            => $"{bps} B/s"
};

class Observer(Action<EngineEvent> onNext) : IObserver<EngineEvent>
{
    public void OnNext(EngineEvent value) => onNext(value);
    public void OnError(Exception error) { }
    public void OnCompleted() { }
}
