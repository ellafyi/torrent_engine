using Downpour.App.ViewModels;

namespace Downpour.App.Views;

public partial class AddTorrentPage : ContentPage
{
    private readonly AddTorrentViewModel _vm;

    public AddTorrentPage(AddTorrentViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        _ = WatchAndPop();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Ensure the TCS completes if the page is dismissed without a button press
        _vm.Cancel();
    }

    private async Task WatchAndPop()
    {
        await _vm.WaitForResultAsync();
        try
        {
            await Navigation.PopModalAsync(false);
        }
        catch
        {
        }
    }
}