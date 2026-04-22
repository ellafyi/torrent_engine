using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Downpour.Storage;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDownpourStorage(this IServiceCollection services, string dbPath)
    {
        services.AddDbContext<DownpourDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath};Cache=Shared"));
        services.AddScoped<TorrentRepository>();
        return services;
    }
}
