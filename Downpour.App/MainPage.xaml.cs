using Downpour.App.ViewModels;

namespace Downpour.App;

public partial class MainPage : ContentPage
{
    private bool _initialized;
    private bool _suppressDeselect;
    private MainViewModel? _viewModel;

    public MainPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized) return;
        _initialized = true;

        _viewModel = IPlatformApplication.Current!.Services.GetRequiredService<MainViewModel>();
        BindingContext = _viewModel;

        Window?.Destroying += async (_, _) => await _viewModel.ShutdownAsync();

        try
        {
            await Task.Run(_viewModel.InitializeAsync);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Startup Error", $"Engine failed to start: {ex.Message}", "OK");
        }
    }

    private void OnTorrentSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _suppressDeselect = true;
    }

    private void OnBackgroundTapped(object? sender, TappedEventArgs e)
    {
        if (_suppressDeselect)
        {
            _suppressDeselect = false;
            return;
        }

        if (_viewModel != null) _viewModel.SelectedTorrent = null;
    }
}