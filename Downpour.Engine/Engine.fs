namespace Downpour.Engine

open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Downpour.Engine.Types
open Downpour.Engine.EngineAgent
open Downpour.Storage

type IEngine =
    inherit System.IDisposable
    abstract StartAsync: unit -> Task
    abstract StopAsync: unit -> Task
    abstract AddTorrentAsync: torrentBytes: byte[] * savePath: string -> Task<int>
    abstract RemoveTorrentAsync: torrentId: int * deleteFiles: bool -> Task
    abstract PauseTorrentAsync: torrentId: int -> Task
    abstract ResumeTorrentAsync: torrentId: int -> Task
    abstract UpdateSettingsAsync: EngineSettings -> Task
    abstract ClearDatabaseAsync: unit -> Task
    abstract GetTorrents: unit -> TorrentProgress list
    abstract Events: System.IObservable<EngineEvent>

type Engine(initialSettings: EngineSettings) =
    let opts =
        let dir = System.IO.Path.Combine(
                      System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                      "Downpour")
        System.IO.Directory.CreateDirectory(dir) |> ignore
        let dbPath = System.IO.Path.Combine(dir, "downpour.db")
        DbContextOptionsBuilder<DownpourDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options

    let context = new DownpourDbContext(opts)
    let () = context.Database.EnsureCreated() |> ignore
    let repository = new TorrentRepository(context)

    let subject = new System.Reactive.Subjects.Subject<EngineEvent>()
    let mutable agentOpt: MailboxProcessor<EngineCommand> option = Option.None

    let agent () =
        match agentOpt with
        | Some a -> a
        | None -> invalidOp "Engine not started"
    let postAndAwait f = (agent ()).PostAndAsyncReply f |> Async.StartAsTask

    interface IEngine with
        member _.StartAsync() = task {
            let! a = start repository initialSettings (fun ev -> subject.OnNext ev)
            agentOpt <- Some a
        }

        member _.StopAsync() = task {
            match agentOpt with
            | Some a ->
                do! a.PostAndAsyncReply Stop |> Async.StartAsTask
                agentOpt <- Option.None
            | Option.None -> ()
        }

        member _.AddTorrentAsync(bytes, savePath) = task {
            let! result = postAndAwait (fun ch -> AddTorrent(bytes, savePath, ch))
            return! match result with
                    | Ok id -> Task.FromResult id
                    | Error msg -> Task.FromException<int>(exn msg)
        }

        member _.RemoveTorrentAsync(torrentId, deleteFiles) =
            postAndAwait (fun ch -> RemoveTorrent(torrentId, deleteFiles, ch))

        member _.PauseTorrentAsync(torrentId) =
            postAndAwait (fun ch -> PauseTorrent(torrentId, ch))

        member _.ResumeTorrentAsync(torrentId) =
            postAndAwait (fun ch -> ResumeTorrent(torrentId, ch))

        member _.UpdateSettingsAsync(settings) =
            postAndAwait (fun ch -> UpdateSettings(settings, ch))

        member _.ClearDatabaseAsync() =
            postAndAwait (fun ch -> ClearDatabase ch)

        member _.Events = subject :> System.IObservable<EngineEvent>

        member _.GetTorrents() = (agent ()).PostAndReply GetAllProgress

        member _.Dispose() = context.Dispose()

    static member createEngine(settings: EngineSettings) : IEngine =
        new Engine(settings) :> IEngine
