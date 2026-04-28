namespace Downpour.Engine.Types

type EngineSettings =
    { ListenPort: uint16
      SeedingEnabled: bool
      MaxDownloadSpeedKbps: int
      MaxUploadSpeedMbps: int } // 0 is unlimited
