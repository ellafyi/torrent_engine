using CommunityToolkit.Maui;
using Downpour.App.ViewModels;
using Downpour.Engine;
using Downpour.Engine.Types;
using Microsoft.Extensions.Logging;
using TorrentEngine = Downpour.Engine.Engine;

namespace Downpour.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<IEngine>(_ =>
            TorrentEngine.createEngine(new EngineSettings(6881, true, 0, 0)));

        builder.Services.AddSingleton<MainViewModel>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
