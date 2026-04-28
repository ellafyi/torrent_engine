namespace Downpour.Engine.Types

[<RequireQualifiedAccess>]
type TorrentStatus =
    | Checking
    | Downloading
    | Seeding
    | Paused
    | Errored of message: string


type TorrentProgress =
    { TorrentId: int
      Name: string
      TotalBytes: int64
      DownloadedBytes: int64
      UploadedBytes: int64
      DownloadSpeedBps: int64
      UploadSpeedBps: int64
      PeerCount: int
      Status: TorrentStatus }

[<RequireQualifiedAccess>]
type EngineEvent =
    | TorrentAdded of torrentId: int
    | TorrentRemoved of torrentId: int
    | Progress of TorrentProgress
    | StatusChanged of torrentId: int * TorrentStatus
    | Error of torrentId: int * message: string
