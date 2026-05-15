namespace Downpour.App.Services;

public interface IFileSystemService
{
    Task<byte[]> ReadAllBytesAsync(string path);
}
