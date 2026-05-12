namespace Downpour.Storage.Models;

public class GlobalStats
{
    public int Id { get; set; } = 1; // singleton row
    public long TotalDownloaded { get; set; }
    public long TotalUploaded { get; set; }
}
