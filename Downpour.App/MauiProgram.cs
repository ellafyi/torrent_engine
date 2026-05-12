using CommunityToolkit.Maui;
using SkiaSharp.Views.Maui.Controls.Hosting;
using Downpour.App.Services;
using Downpour.App.ViewModels;
using Downpour.Engine;
using Downpour.Engine.Types;
using Microsoft.Extensions.Logging;
using TorrentEngine = Downpour.Engine.Engine;
using MauiIcons.Fluent;

#if WINDOWS
using Microsoft.Maui.LifecycleEvents;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;
#endif

namespace Downpour.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseSkiaSharp()
            .UseFluentMauiIcons()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if WINDOWS
        builder.ConfigureLifecycleEvents(events =>
        {
            events.AddWindows(wndLifeCycleBuilder =>
            {
                wndLifeCycleBuilder.OnWindowCreated(window =>
                {
                    window.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
                });
            });
        });
#endif

        builder.Services.AddSingleton<SettingsService>();
        builder.Services.AddSingleton<IEngine>(sp =>
            TorrentEngine.createEngine(sp.GetRequiredService<SettingsService>().Load()));

        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<IFilePickerService, FilePickerService>();
        builder.Services.AddSingleton<ISpeedHistoryService, SpeedHistoryService>();
        builder.Services.AddSingleton<INavigationService, NavigationService>();

        builder.Services.AddSingleton<MainViewModel>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
