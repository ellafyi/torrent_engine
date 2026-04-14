using Downpour.Storage.Models;
using Microsoft.EntityFrameworkCore;

namespace Downpour.Storage;

public class DownpourDbContext : DbContext
{
    public DbSet<Torrent> Torrents { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite("Data Source=downpour.db;Cache=Shared");
    }
}