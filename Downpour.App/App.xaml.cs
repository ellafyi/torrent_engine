using Downpour.App.ViewModels;

namespace Downpour.App;

public partial class App : Application
{
    private readonly MainViewModel _vm;

    public App(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());
        window.TitleBar = new TitleBarView { BindingContext = _vm };
        return window;
    }
}