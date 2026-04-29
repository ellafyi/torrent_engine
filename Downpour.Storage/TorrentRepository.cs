using Downpour.Storage.Models;
using Microsoft.EntityFrameworkCore;

namespace Downpour.Storage;

public class TorrentRepository(DownpourDbContext context)
{
    public async Task AddTorrentAsync(Torrent torrent)
    {
        context.Torrents.Add(torrent);
        await context.SaveChangesAsync();
    }

    public async Task IncrementTransferStatsAsync(int torrentId, long uploaded, long downloaded)
    {
        var torrent = await context.Torrents.FindAsync(torrentId);
        if (torrent == null) return;
        torrent.UploadedBytes += uploaded;
        torrent.DownloadedBytes += downloaded;
        await context.SaveChangesAsync();
    }

    public async Task<IEnumerable<Torrent>> GetAllTorrentsAsync()
    {
        return await context.Torrents.ToListAsync();
    }

    public async Task<(long TotalUploaded, long TotalDownloaded)> GetGlobalStateAsync()
    {
        var totalUploaded = await context.Torrents.SumAsync(t => t.UploadedBytes);
        var totalDownloaded = await context.Torrents.SumAsync(t => t.DownloadedBytes);
        return (totalUploaded, totalDownloaded);
    }

    public async Task UpdateBitfieldAsync(int torrentId, byte[] bitfield)
    {
        await context.Torrents
            .Where(t => t.Id == torrentId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.PieceBitfield, bitfield));
    }

    public async Task UpdateStatusAsync(int torrentId, TorrentStatus status)
    {
        await context.Torrents
            .Where(t => t.Id == torrentId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, status));
    }

    public async Task<Torrent?> GetByInfoHashAsync(string infoHashHex)
    {
        return await context.Torrents.FirstOrDefaultAsync(t => t.InfoHash == infoHashHex);
    }
}