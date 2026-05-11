using Downpour.App.ViewModels;

namespace Downpour.App;

public partial class MainPage : ContentPage
{
    private MainViewModel? _viewModel;
    private bool _initialized;

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

        Window.Destroying += async (_, _) => await _viewModel.ShutdownAsync();

        try
        {
            await Task.Run(_viewModel.InitializeAsync);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Startup Error", $"Engine failed to start: {ex.Message}", "OK");
        }
    }
}
