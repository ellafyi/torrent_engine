using Downpour.App.ViewModels;

namespace Downpour.App.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _vm;

    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        _ = WatchAndPop();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Cancel();
    }

    private async Task WatchAndPop()
    {
        await _vm.WaitForResultAsync();
        try { await Navigation.PopModalAsync(false); }
        catch { }
    }
}
