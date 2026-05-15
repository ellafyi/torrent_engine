using Downpour.Engine.Types;

namespace Downpour.App.Services;

public class SettingsService
{
    private const string KeyPort = "engine.port";
    private const string KeySeeding = "engine.seeding";
    private const string KeyDownKbps = "engine.downKbps";
    private const string KeyUpMbps = "engine.upMbps";

    public static EngineSettings Load()
    {
        return new EngineSettings(
            (ushort)Preferences.Default.Get(KeyPort, 6881),
            Preferences.Default.Get(KeySeeding, true),
            Preferences.Default.Get(KeyDownKbps, 0),
            Preferences.Default.Get(KeyUpMbps, 0));
    }

    public static void Save(EngineSettings s)
    {
        Preferences.Default.Set(KeyPort, (int)s.ListenPort);
        Preferences.Default.Set(KeySeeding, s.SeedingEnabled);
        Preferences.Default.Set(KeyDownKbps, s.MaxDownloadSpeedKbps);
        Preferences.Default.Set(KeyUpMbps, s.MaxUploadSpeedMbps);
    }
}