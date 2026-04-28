namespace Downpour.Engine.IEngine

open System.Threading.Tasks
open Downpour.Engine.Types

type Engine =
    abstract StartAsync: unit -> Task
    abstract StopAsync: unit -> Task

    abstract AddTorrentAsync: torrentBytes: byte[] * savePath: string -> Task<int>
    abstract RemoveTorrentAsync: torrentId: int * deleteFiles: bool -> Task
    abstract PauseTorrentAsync: torrentId: int -> Task
    abstract ResumeTorrentAsync: torrentId: int -> Task

    abstract UpdateSettingsAsync: EngineSettings -> Task

    abstract GetTorrents: unit -> TorrentProgress list

    abstract Events: System.IObservable<EngineEvent>
