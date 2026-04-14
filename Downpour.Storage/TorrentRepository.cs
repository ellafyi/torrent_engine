using Downpour.Storage.Models;

namespace Downpour.Storage;

public class TorrentRepository
{
    private readonly DownpourDbContext _context;

    public TorrentRepository(DownpourDbContext context)
    {
        _context = context;
        _context.Database.EnsureCreated();
    }

    public void AddTorrent(Torrent torrent)
    {
        _context.Torrents.Add(torrent);
        _context.SaveChanges();
    }

    public void UpdateTransferStats(int torrentId, long uploaded, long downloaded)
    {
        var torrent = _context.Torrents.Find(torrentId);
        if (torrent == null) return;
        torrent.UploadedBytes += uploaded;
        torrent.DownloadedBytes += downloaded;
        _context.SaveChanges();
    }

    public IEnumerable<Torrent> GetAllTorrents()
    {
        return _context.Torrents.ToList();
    }

    public (long TotalUploaded, long TotalDownloaded) GetGlobalState()
    {
        var totalUploaded = _context.Torrents.Sum(t => t.UploadedBytes);
        var totalDownloaded = _context.Torrents.Sum(t => t.DownloadedBytes);

        return (totalDownloaded, totalDownloaded);
    }
}