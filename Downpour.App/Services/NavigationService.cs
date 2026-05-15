using Downpour.App.Models;
using Downpour.App.ViewModels;
using Downpour.App.Views;
using Downpour.Engine.Types;

namespace Downpour.App.Services;

public class NavigationService(IFilePickerService filePicker, ISpeedHistoryService speedHistory)
    : INavigationService
{
    public async Task<AddTorrentParameters?> ShowAddTorrentAsync()
    {
        var vm = new AddTorrentViewModel(filePicker);
        var page = new AddTorrentPage(vm);
        await Shell.Current.Navigation.PushModalAsync(page, false);
        return await vm.WaitForResultAsync();
    }

    public async Task<EngineSettings?> ShowSettingsAsync(EngineSettings current)
    {
        var vm = new SettingsViewModel();
        vm.Initialize(current);
        var page = new SettingsPage(vm);
        await Shell.Current.Navigation.PushModalAsync(page, false);
        return await vm.WaitForResultAsync();
    }

    public async Task ShowDetailsAsync(TorrentItemViewModel item)
    {
        var vm = new TorrentDetailsViewModel(item, speedHistory);
        var page = new TorrentDetailsPage(vm);
        await Shell.Current.Navigation.PushModalAsync(page, false);
        await vm.WaitForClosedAsync();
    }
}