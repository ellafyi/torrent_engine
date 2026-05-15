using Downpour.Storage.Models;
using Microsoft.EntityFrameworkCore;

namespace Downpour.Storage;

public class TorrentRepository(DownpourDbContext context)
{
    // DbContext is not thread-safe; multiple TorrentAgents call this concurrently,
    // so all async operations are serialized through this semaphore.
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task AddTorrentAsync(Torrent torrent)
    {
        await _lock.WaitAsync();
        try
        {
            context.Torrents.Add(torrent);
            await context.SaveChangesAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<(long TotalDownloaded, long TotalUploaded)> IncrementTransferStatsAsync(
        int torrentId, long uploaded, long downloaded)
    {
        await _lock.WaitAsync();
        try
        {
            var torrent = await context.Torrents.FindAsync(torrentId);
            if (torrent != null)
            {
                torrent.UploadedBytes += uploaded;
                torrent.DownloadedBytes += downloaded;
            }

            var gs = await context.GlobalStats.FindAsync(1);
            if (gs == null)
            {
                gs = new GlobalStats { Id = 1, TotalDownloaded = downloaded, TotalUploaded = uploaded };
                context.GlobalStats.Add(gs);
            }
            else
            {
                gs.TotalDownloaded += downloaded;
                gs.TotalUploaded += uploaded;
            }

            await context.SaveChangesAsync();
            return (gs.TotalDownloaded, gs.TotalUploaded);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<(long TotalDownloaded, long TotalUploaded)> GetGlobalStatsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var gs = await context.GlobalStats.FindAsync(1);
            return gs == null ? (0L, 0L) : (gs.TotalDownloaded, gs.TotalUploaded);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IEnumerable<Torrent>> GetAllTorrentsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return await context.Torrents.ToListAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<(long TotalUploaded, long TotalDownloaded)> GetGlobalStateAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var totalUploaded = await context.Torrents.SumAsync(t => t.UploadedBytes);
            var totalDownloaded = await context.Torrents.SumAsync(t => t.DownloadedBytes);
            return (totalUploaded, totalDownloaded);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateBitfieldAsync(int torrentId, byte[] bitfield)
    {
        await _lock.WaitAsync();
        try
        {
            await context.Torrents
                .Where(t => t.Id == torrentId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.PieceBitfield, bitfield));
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateStatusAsync(int torrentId, TorrentStatus status)
    {
        await _lock.WaitAsync();
        try
        {
            await context.Torrents
                .Where(t => t.Id == torrentId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, status));
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Torrent?> GetByInfoHashAsync(string infoHashHex)
    {
        await _lock.WaitAsync();
        try
        {
            return await context.Torrents.FirstOrDefaultAsync(t => t.InfoHash == infoHashHex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteTorrentAsync(int torrentId)
    {
        await _lock.WaitAsync();
        try
        {
            await context.Torrents
                .Where(t => t.Id == torrentId)
                .ExecuteDeleteAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await context.Torrents.ExecuteDeleteAsync();
        }
        finally
        {
            _lock.Release();
        }
    }
}