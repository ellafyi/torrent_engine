namespace Downpour.App.Services;

public class FileSystemService : IFileSystemService
{
    public Task<byte[]> ReadAllBytesAsync(string path)
    {
        return File.ReadAllBytesAsync(path);
    }
}
