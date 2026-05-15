using CommunityToolkit.Maui;
using Downpour.App.Services;
using Downpour.App.ViewModels;
using Downpour.Engine;
using MauiIcons.Fluent;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;
using SkiaSharp.Views.Maui.Controls.Hosting;
using TorrentEngine = Downpour.Engine.Engine;

#if WINDOWS
using Microsoft.Maui.LifecycleEvents;
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
                wndLifeCycleBuilder.OnWindowCreated(window => { window.SystemBackdrop = new MicaBackdrop(); });
            });
        });
#endif

        builder.Services.AddSingleton<SettingsService>();
        builder.Services.AddSingleton<IEngine>(sp =>
            TorrentEngine.createEngine(SettingsService.Load()));

        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<IFilePickerService, FilePickerService>();
        builder.Services.AddSingleton<IFileSystemService, FileSystemService>();
        builder.Services.AddSingleton<ISpeedHistoryService, SpeedHistoryService>();
        builder.Services.AddSingleton<INavigationService, NavigationService>();

        builder.Services.AddSingleton<MainViewModel>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}