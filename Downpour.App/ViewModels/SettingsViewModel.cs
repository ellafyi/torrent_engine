using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Downpour.Engine.Types;

namespace Downpour.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly TaskCompletionSource<EngineSettings?> _tcs = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string ListenPortText { get; set; } = "6881";

    [ObservableProperty]
    public partial bool SeedingEnabled { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string MaxDownloadKbpsText { get; set; } = "0";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string MaxUploadMbpsText { get; set; } = "0";

    public void Initialize(EngineSettings current)
    {
        ListenPortText      = current.ListenPort.ToString();
        SeedingEnabled      = current.SeedingEnabled;
        MaxDownloadKbpsText = current.MaxDownloadSpeedKbps.ToString();
        MaxUploadMbpsText   = current.MaxUploadSpeedMbps.ToString();
    }

    public Task<EngineSettings?> WaitForResultAsync() => _tcs.Task;
    public void Cancel() => _tcs.TrySetResult(null);

    private bool CanConfirm() =>
        int.TryParse(ListenPortText,      out var port) && port is >= 1 and <= 65535 &&
        int.TryParse(MaxDownloadKbpsText, out var dl)   && dl  >= 0 &&
        int.TryParse(MaxUploadMbpsText,   out var ul)   && ul  >= 0;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        var port = (ushort)int.Parse(ListenPortText);
        var dl   = int.Parse(MaxDownloadKbpsText);
        var ul   = int.Parse(MaxUploadMbpsText);
        _tcs.TrySetResult(new EngineSettings(port, SeedingEnabled, dl, ul));
    }

    [RelayCommand]
    private void CancelDialog() => _tcs.TrySetResult(null);
}
