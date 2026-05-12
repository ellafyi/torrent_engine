module Downpour.Engine.EngineAgent

open System
open System.IO
open System.Net
open System.Net.Sockets
open Downpour.Protocol
open Downpour.Torrent
open Downpour.Torrent.Types
open Downpour.Engine.Types
open Downpour.Engine.TorrentAgent
open Downpour.Storage

// Static data for the engine agent does not change after startup
type private EngineContext =
    { Repository: TorrentRepository
      Notify: EngineEvent -> unit }

type private EngineState =
    { mutable Torrents: Map<int, TorrentAgent>
      mutable LatestProgress: Map<int, TorrentProgress>
      mutable SavePaths: Map<int, string>
      mutable Settings: EngineSettings
      mutable Listener: TcpListener option
      mutable ListenerCts: Threading.CancellationTokenSource option
      OurPeerId: byte[] }

// helpers

let private generatePeerId () =
    let prefix = "-DW0001-"B
    let suffix = Array.zeroCreate 12
    Random.Shared.NextBytes(suffix)
    Array.append prefix suffix

let private toHex (bytes: byte[]) =
    bytes |> Array.map (sprintf "%02x") |> String.concat ""

let private totalSizeOf (meta: TorrentMetaInfo) =
    match meta.Info.FileLayout with
    | SingleFile(len, _) -> len
    | MultiFile files -> files |> List.sumBy _.Length

// Reads exactly count bytes from stream, respecting the cancellation token.
let private readExactly (stream: NetworkStream) (buf: byte[]) (count: int) (ct: Threading.CancellationToken) =
    async {
        let mutable total = 0

        while total < count do
            let! n =
                stream.ReadAsync(Memory<byte>(buf, total, count - total), ct).AsTask()
                |> Async.AwaitTask

            if n = 0 then
                failwith "connection closed"

            total <- total + n
    }

//  listener

let private startListener (inbox: MailboxProcessor<EngineCommand>) (port: uint16) =
    let listener = new TcpListener(IPAddress.Any, int port)
    listener.Start()
    let cts = new Threading.CancellationTokenSource()

    Async.Start(
        async {
            try
                while true do
                    let! client = listener.AcceptTcpClientAsync(cts.Token).AsTask() |> Async.AwaitTask
                    inbox.Post(IncomingConn client)
            with _ ->
                ()
        },
        cts.Token
    )

    listener, cts

let private stopListener (state: EngineState) =
    state.ListenerCts
    |> Option.iter (fun cts ->
        cts.Cancel()
        cts.Dispose())

    state.Listener |> Option.iter _.Stop()
    state.Listener <- None
    state.ListenerCts <- None

// initialization

// Loads all torrents from the DB and creates TorrentAgents for them.
// Non-paused torrents receive a Start command immediately.
let private initializeTorrents
    (ctx: EngineContext)
    (ourPeerId: byte[])
    (settings: EngineSettings)
    (inbox: MailboxProcessor<EngineCommand>)
    : Async<Map<int, TorrentAgent> * Map<int, string> * Map<int, TorrentProgress>> =
    async {
        let! stored = ctx.Repository.GetAllTorrentsAsync() |> Async.AwaitTask

        let mutable torrents = Map.empty
        let mutable savePaths = Map.empty
        let mutable initialProgress = Map.empty

        for t in stored do
            try
                let fileData = t.TorrentFileData |> Option.ofObj |> Option.get
                let meta = Parser.parse fileData

                let bitfield =
                    t.PieceBitfield
                    |> Option.ofObj
                    |> Option.defaultWith (fun () -> PieceStore.makeBitfield meta.Info.Pieces.Length)

                let notifyAgent ev = inbox.Post(TorrentEvent(t.Id, ev))

                let agent =
                    TorrentAgent.create t.Id meta t.SavePath bitfield ourPeerId settings ctx.Repository notifyAgent

                if t.Status <> Downpour.Storage.Models.TorrentStatus.Paused then
                    agent.Post Start

                let status =
                    match t.Status with
                    | Downpour.Storage.Models.TorrentStatus.Paused -> TorrentStatus.Paused
                    | Downpour.Storage.Models.TorrentStatus.Seeding -> TorrentStatus.Seeding
                    | Downpour.Storage.Models.TorrentStatus.Downloading -> TorrentStatus.Downloading
                    | Downpour.Storage.Models.TorrentStatus.Error -> TorrentStatus.Errored "Error"
                    | _ -> TorrentStatus.Paused

                let downloaded =
                    [ 0 .. meta.Info.Pieces.Length - 1 ]
                    |> List.filter (fun i -> PieceStore.getBit bitfield i)
                    |> List.sumBy (fun i -> int64 (PieceStore.actualPieceLength meta i))

                let progress = {
                    TorrentId = t.Id
                    Name = meta.Info.Name
                    TotalBytes = t.TotalSize
                    DownloadedBytes = downloaded
                    UploadedBytes = t.UploadedBytes
                    DownloadSpeedBps = 0L
                    UploadSpeedBps = 0L
                    PeerCount = 0
                    Status = status
                }

                torrents <- Map.add t.Id agent torrents
                savePaths <- Map.add t.Id t.SavePath savePaths
                initialProgress <- Map.add t.Id progress initialProgress
            with _ ->
                ()

        return torrents, savePaths, initialProgress
    }

// handlers

let private handleAddTorrent
    (ctx: EngineContext)
    (state: EngineState)
    (inbox: MailboxProcessor<EngineCommand>)
    (bytes: byte[])
    (savePath: string)
    (reply: AsyncReplyChannel<Result<int, string>>)
    =
    async {
        try
            let meta = Parser.parse bytes
            let (InfoHash infoHashBytes) = meta.InfoHash
            let infoHex = toHex infoHashBytes
            let! existing = ctx.Repository.GetByInfoHashAsync(infoHex) |> Async.AwaitTask

            match existing |> Option.ofObj with
            | Some _ -> reply.Reply(Error "already added")
            | None ->
                let fullSavePath = Path.Combine(savePath, meta.Info.Name)
                let torrent =
                    Downpour.Storage.Models.Torrent(
                        Name = meta.Info.Name,
                        InfoHash = infoHex,
                        TotalSize = totalSizeOf meta,
                        SavePath = fullSavePath,
                        Status = Downpour.Storage.Models.TorrentStatus.Downloading,
                        TorrentFileData = bytes
                    )

                do! ctx.Repository.AddTorrentAsync(torrent) |> Async.AwaitTask

                let bitfield = PieceStore.makeBitfield meta.Info.Pieces.Length

                let notifyAgent ev =
                    inbox.Post(TorrentEvent(torrent.Id, ev))

                let agent =
                    TorrentAgent.create
                        torrent.Id
                        meta
                        fullSavePath
                        bitfield
                        state.OurPeerId
                        state.Settings
                        ctx.Repository
                        notifyAgent

                agent.Post Start

                state.Torrents <- Map.add torrent.Id agent state.Torrents
                state.SavePaths <- Map.add torrent.Id fullSavePath state.SavePaths
                reply.Reply(Ok torrent.Id)
                ctx.Notify(EngineEvent.TorrentAdded torrent.Id)
        with ex ->
            reply.Reply(Error ex.Message)
    }

let private handleRemoveTorrent
    (ctx: EngineContext)
    (state: EngineState)
    (torrentId: int)
    (deleteFiles: bool)
    (reply: AsyncReplyChannel<unit>)
    =
    async {
        match Map.tryFind torrentId state.Torrents with
        | None -> reply.Reply(())
        | Some agent ->
            agent.Post Pause
            agent.Dispose()
            state.Torrents <- Map.remove torrentId state.Torrents
            state.LatestProgress <- Map.remove torrentId state.LatestProgress

            let savePath = Map.tryFind torrentId state.SavePaths
            state.SavePaths <- Map.remove torrentId state.SavePaths

            do! ctx.Repository.DeleteTorrentAsync(torrentId) |> Async.AwaitTask

            if deleteFiles then
                savePath
                |> Option.iter (fun path ->
                    if Directory.Exists(path) then
                        Directory.Delete(path, true))

            ctx.Notify(EngineEvent.TorrentRemoved torrentId)
            reply.Reply(())
    }

// Reads the inbound peer's handshake in a background async (5-second timeout),
// looks up the matching torrent, replies with our handshake, and routes the
// connection to the correct TorrentAgent.
let private handleIncomingConn (ctx: EngineContext) (state: EngineState) (client: TcpClient) =
    // immutable values, the background async must not read mutable state.
    let torrents = state.Torrents
    let ourPeerId = state.OurPeerId

    Async.Start(
        async {
            try
                use cts = new Threading.CancellationTokenSource(TimeSpan.FromSeconds 5.0)
                let stream = client.GetStream()
                let buf = Array.zeroCreate 68
                do! readExactly stream buf 68 cts.Token

                match Handshake.deserialize buf with
                | Error _ -> client.Dispose()
                | Ok hs ->
                    let infoHex = toHex hs.InfoHash
                    let! torrentOpt = ctx.Repository.GetByInfoHashAsync(infoHex) |> Async.AwaitTask

                    match torrentOpt |> Option.ofObj with
                    | None -> client.Dispose()
                    | Some torrent ->
                        match Map.tryFind torrent.Id torrents with
                        | None -> client.Dispose()
                        | Some agent ->
                            let reply = Handshake.serialize hs.InfoHash ourPeerId

                            do!
                                stream
                                    .WriteAsync(ReadOnlyMemory<byte>(reply), Threading.CancellationToken.None)
                                    .AsTask()
                                |> Async.AwaitTask

                            agent.Post(InboundPeer(client, hs.PeerId))
            with _ ->
                client.Dispose()
        }
    )

let private handleUpdateSettings
    (state: EngineState)
    (inbox: MailboxProcessor<EngineCommand>)
    (newSettings: EngineSettings)
    (reply: AsyncReplyChannel<unit>)
    =
    if newSettings.ListenPort <> state.Settings.ListenPort then
        stopListener state
        let l, cts = startListener inbox newSettings.ListenPort
        state.Listener <- Some l
        state.ListenerCts <- Some cts

    state.Settings <- newSettings

    for _, agent in Map.toSeq state.Torrents do
        agent.Post(SettingsUpdated newSettings)

    reply.Reply(())

let private handleClearDatabase
    (ctx: EngineContext)
    (state: EngineState)
    (reply: AsyncReplyChannel<unit>)
    =
    async {
        for torrentId, agent in Map.toSeq state.Torrents do
            agent.Post Pause
            agent.Dispose()
            ctx.Notify(EngineEvent.TorrentRemoved torrentId)

        state.Torrents <- Map.empty
        state.SavePaths <- Map.empty
        state.LatestProgress <- Map.empty

        do! ctx.Repository.ClearAllAsync() |> Async.AwaitTask
        reply.Reply(())
    }

let private handleStop (state: EngineState) (reply: AsyncReplyChannel<unit>) =
    for _, agent in Map.toSeq state.Torrents do
        agent.Post Pause
        agent.Dispose()

    state.Torrents <- Map.empty
    stopListener state
    reply.Reply(())

let start
    (repository: TorrentRepository)
    (settings: EngineSettings)
    (notify: EngineEvent -> unit)
    : Async<MailboxProcessor<EngineCommand>> =
    async {
        let ctx =
            { Repository = repository
              Notify = notify }

        let agent =
            MailboxProcessor<EngineCommand>.Start(fun inbox ->
                async {
                    let ourPeerId = generatePeerId ()
                    let! torrents, savePaths, initialProgress = initializeTorrents ctx ourPeerId settings inbox

                    let state =
                        { Torrents = torrents
                          LatestProgress = initialProgress
                          SavePaths = savePaths
                          Settings = settings
                          Listener = Option.None
                          ListenerCts = Option.None
                          OurPeerId = ourPeerId }

                    let l, cts = startListener inbox settings.ListenPort
                    state.Listener <- Some l
                    state.ListenerCts <- Some cts

                    let! struct (gsDl, gsUl) = ctx.Repository.GetGlobalStatsAsync() |> Async.AwaitTask
                    ctx.Notify(EngineEvent.GlobalStatsUpdate(gsDl, gsUl))

                    let rec loop () =
                        async {
                            let! msg = inbox.Receive()

                            match msg with

                            // torrent management

                            | AddTorrent(bytes, savePath, reply) ->
                                do! handleAddTorrent ctx state inbox bytes savePath reply

                            | RemoveTorrent(torrentId, deleteFiles, reply) ->
                                do! handleRemoveTorrent ctx state torrentId deleteFiles reply

                            | PauseTorrent(torrentId, reply) ->
                                state.Torrents |> Map.tryFind torrentId |> Option.iter (fun a -> a.Post Pause)
                                reply.Reply(())

                            | ResumeTorrent(torrentId, reply) ->
                                state.Torrents |> Map.tryFind torrentId |> Option.iter (fun a -> a.Post Resume)
                                reply.Reply(())

                            // inbound TCP

                            | IncomingConn client -> handleIncomingConn ctx state client

                            // event forwarding

                            | TorrentEvent(_, EngineEvent.Progress p) ->
                                state.LatestProgress <- Map.add p.TorrentId p state.LatestProgress
                                ctx.Notify(EngineEvent.Progress p)

                            | TorrentEvent(_, ev) -> ctx.Notify ev

                            // settings

                            | UpdateSettings(newSettings, reply) -> handleUpdateSettings state inbox newSettings reply

                            // query / shutdown

                            | GetAllProgress reply -> reply.Reply(state.LatestProgress |> Map.toList |> List.map snd)

                            | ClearDatabase reply -> do! handleClearDatabase ctx state reply

                            | Stop reply -> handleStop state reply

                            return! loop ()
                        }

                    return! loop ()
                })

        return agent
    }
