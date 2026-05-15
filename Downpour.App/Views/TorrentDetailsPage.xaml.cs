using Downpour.App.ViewModels;

namespace Downpour.App.Views;

public partial class TorrentDetailsPage : ContentPage
{
    private readonly TorrentDetailsViewModel _vm;

    public TorrentDetailsPage(TorrentDetailsViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        InitializeComponent();
        WatchAndPop();
    }

    private async void WatchAndPop()
    {
        await _vm.WaitForClosedAsync();
        await Navigation.PopModalAsync(false);
    }

    protected override void OnDisappearing()
    {
        _vm.Cancel();
    }
}