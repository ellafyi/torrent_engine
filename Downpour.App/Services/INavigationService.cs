using Downpour.App.Models;
using Downpour.App.ViewModels;
using Downpour.Engine.Types;

namespace Downpour.App.Services;

public interface INavigationService
{
    Task<AddTorrentParameters?> ShowAddTorrentAsync();
    Task<EngineSettings?> ShowSettingsAsync(EngineSettings current);
    Task ShowDetailsAsync(TorrentItemViewModel item);
}
