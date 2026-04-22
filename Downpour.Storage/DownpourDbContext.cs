using Downpour.Storage.Models;
using Microsoft.EntityFrameworkCore;

namespace Downpour.Storage;

public class DownpourDbContext(DbContextOptions<DownpourDbContext> options) : DbContext(options)
{
    public DbSet<Torrent> Torrents { get; set; } = null!;
}