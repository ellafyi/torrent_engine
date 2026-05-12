module Downpour.Engine.TorrentAgent

open System
open System.Net.Sockets
open Downpour.Protocol
open Downpour.Tracker
open Downpour.Torrent.Types
open Downpour.Engine.Types
open Downpour.Engine.PeerAgent
open Downpour.Storage

let private blockSize = 16384
let private pipelineDepth = 5
let private blockTimeoutSecs = TimeSpan.FromSeconds 30.0

// internal types

type private ActivePeer =
    { Agent: PeerAgent
      mutable Bitfield: byte[]
      mutable State: PeerState
      mutable Pending: int }

type private AgentState =
    { mutable Status: TorrentStatus
      mutable Peers: Map<string, ActivePeer>
      mutable PieceStates: Map<int, PieceState>
      mutable Bitfield: byte[]
      mutable TrackerId: string option
      mutable NextAnnounce: DateTime
      mutable AnnounceInterval: int
      mutable AnnounceTiers: string list list
      mutable DownloadedThisTick: int64
      mutable RequestedThisTick: int64
      mutable UploadedThisTick: int64
      mutable SessionDownloaded: int64
      mutable SessionUploaded: int64
      mutable TotalDownloaded: int64
      mutable TotalUploaded: int64
      mutable LastDownloadSpeedBps: int64
      mutable LastUploadSpeedBps: int64
      mutable Settings: EngineSettings
      mutable TickCts: Threading.CancellationTokenSource option
      mutable TickCount: int }

// Static data for a torrent agent, does not change after creation.
type private TorrentContext =
    { TorrentId: int
      Meta: TorrentMetaInfo
      SavePath: string
      OurPeerId: byte[]
      Repository: TorrentRepository
      Notify: EngineEvent -> unit
      TotalSize: int64 }

type TorrentAgent =
    { TorrentId: int
      Post: TorrentCommand -> unit
      Dispose: unit -> unit }

// helpers

#if DEBUG
let private loggingOn = Environment.GetEnvironmentVariable("DOWNPOUR_LOG") <> null

let private dbg (id: int) (msg: string) =
    if loggingOn then
        eprintfn "[%s] [T%d] %s" (DateTime.Now.ToString("HH:mm:ss.fff")) id msg
#else
let private dbg (_: int) (_: string) = ()
#endif

let private toHex (bytes: byte[]) =
    bytes |> Array.map (sprintf "%02x") |> String.concat ""

let private totalSizeOf (meta: TorrentMetaInfo) =
    match meta.Info.FileLayout with
    | SingleFile(len, _) -> len
    | MultiFile files -> files |> List.sumBy _.Length

let private toStorageStatus =
    function
    | TorrentStatus.Checking -> Downpour.Storage.Models.TorrentStatus.Downloading
    | TorrentStatus.Downloading -> Downpour.Storage.Models.TorrentStatus.Downloading
    | TorrentStatus.Seeding -> Downpour.Storage.Models.TorrentStatus.Seeding
    | TorrentStatus.Paused -> Downpour.Storage.Models.TorrentStatus.Paused
    | TorrentStatus.Errored _ -> Downpour.Storage.Models.TorrentStatus.Error

let private initPieceStates (meta: TorrentMetaInfo) (bitfield: byte[]) : Map<int, PieceState> =
    meta.Info.Pieces
    |> List.mapi (fun i (Sha1Hash hash) ->
        let pieceLen = PieceStore.actualPieceLength meta i
        let blockCount = (pieceLen + blockSize - 1) / blockSize

        i,
        { Index = i
          ExpectedHash = hash
          Length = pieceLen
          BlockCount = blockCount
          Blocks = Array.create blockCount Missing
          Data = [||] })
    |> List.filter (fun (i, _) -> not (PieceStore.getBit bitfield i))
    |> Map.ofList

let private buildAnnounceRequest
    (meta: TorrentMetaInfo)
    (ourPeerId: byte[])
    (state: AgentState)
    (event: AnnounceEvent)
    =
    let (InfoHash infoHash) = meta.InfoHash

    { InfoHash = infoHash
      PeerId = ourPeerId
      Port = state.Settings.ListenPort
      Uploaded = state.TotalUploaded
      Downloaded = state.TotalDownloaded
      Left = totalSizeOf meta - state.TotalDownloaded
      Event = event
      TrackerId = state.TrackerId }

let private announceWithFallback
    (tiers: string list list)
    (req: AnnounceRequest)
    : Async<Result<AnnounceResponse, TrackerError>> =
    async {
        let mutable result: Result<AnnounceResponse, TrackerError> =
            Error(ParseError "no trackers configured")

        let mutable stop = false

        for tier in tiers do
            if not stop then
                let shuffled = tier |> List.sortBy (fun _ -> Random.Shared.Next())

                for url in shuffled do
                    if not stop then
                        let! r = Tracker.announce url req

                        match r with
                        | Ok _ ->
                            result <- r
                            stop <- true
                        | Error e -> result <- Error e

        return result
    }

let private startTicks (inbox: MailboxProcessor<TorrentCommand>) (cts: Threading.CancellationTokenSource) =
    async {
        while not cts.IsCancellationRequested do
            do! Async.Sleep 1000

            if not cts.IsCancellationRequested then
                inbox.Post ProgressTick
                inbox.Post TrackerTick
    }
    |> fun a -> Async.Start(a, cts.Token)

let private findNextBlock (peer: ActivePeer) (state: AgentState) =
    state.PieceStates
    |> Map.toSeq
    |> Seq.tryPick (fun (pieceIdx, ps) ->
        if not (PieceStore.getBit peer.Bitfield pieceIdx) then
            Option.None
        else
            ps.Blocks
            |> Array.tryFindIndex (fun b -> b = Missing)
            |> Option.map (fun blockIdx ->
                let blockOffset = blockIdx * blockSize
                let blockLen = min blockSize (ps.Length - blockOffset)
                pieceIdx, blockIdx, blockOffset, blockLen))

let private dispatchRequests (peerId: string) (state: AgentState) =
    match Map.tryFind peerId state.Peers with
    | Option.None -> ()
    | Some peer when peer.State.PeerChoking -> ()
    | Some peer ->
        let mutable stop = false

        while not stop do
            if peer.Pending >= pipelineDepth then
                stop <- true
            else
                match findNextBlock peer state with
                | Option.None -> stop <- true
                | Some(pieceIdx, blockIdx, blockOffset, blockLen) ->
                    let dlLimit = state.Settings.MaxDownloadSpeedKbps
                    let dlBudget = int64 dlLimit * 1024L
                    if dlLimit > 0 && state.RequestedThisTick + int64 blockLen > dlBudget then
                        stop <- true
                    else
                        let ps = state.PieceStates[pieceIdx]
                        ps.Blocks[blockIdx] <- InFlight(peer.Agent.PeerId, DateTime.UtcNow)
                        peer.Agent.Post(SendMsg(Request(pieceIdx, blockOffset, blockLen)))
                        peer.Pending <- peer.Pending + 1
                        state.RequestedThisTick <- state.RequestedThisTick + int64 blockLen

// mailbox helpers

let private fireAnnounce
    (ctx: TorrentContext)
    (state: AgentState)
    (inbox: MailboxProcessor<TorrentCommand>)
    (event: AnnounceEvent)
    =
    let req = buildAnnounceRequest ctx.Meta ctx.OurPeerId state event

    Async.Start(
        async {
            let! result = announceWithFallback state.AnnounceTiers req
            inbox.Post(TrackerResult result)
        }
    )

let private registerPeer (ctx: TorrentContext) (state: AgentState) (peer: PeerAgent) =
    let id = toHex peer.PeerId

    if not (Map.containsKey id state.Peers) then
        let activePeer =
            { Agent = peer
              Bitfield = PieceStore.makeBitfield ctx.Meta.Info.Pieces.Length
              State = PeerState.initial
              Pending = 0 }

        state.Peers <- Map.add id activePeer state.Peers

        if PieceStore.countSet state.Bitfield > 0 then
            peer.Post(SendMsg(Bitfield state.Bitfield))

let private resetInFlight (state: AgentState) (peerId: byte[]) =
    for _, ps in Map.toSeq state.PieceStates do
        for i in 0 .. ps.Blocks.Length - 1 do
            match ps.Blocks[i] with
            | InFlight(pid, _) when pid = peerId -> ps.Blocks[i] <- Missing
            | _ -> ()

let private handleCompletion (ctx: TorrentContext) (state: AgentState) (inbox: MailboxProcessor<TorrentCommand>) =
    async {
        state.Status <- TorrentStatus.Seeding
        ctx.Notify(EngineEvent.StatusChanged(ctx.TorrentId, TorrentStatus.Seeding))

        do!
            ctx.Repository.UpdateStatusAsync(ctx.TorrentId, toStorageStatus TorrentStatus.Seeding)
            |> Async.AwaitTask

        fireAnnounce ctx state inbox Completed

        if not state.Settings.SeedingEnabled then
            inbox.Post Pause
    }

let private makeProgress (ctx: TorrentContext) (state: AgentState) =
    { TorrentId = ctx.TorrentId
      Name = ctx.Meta.Info.Name
      TotalBytes = ctx.TotalSize
      DownloadedBytes = state.TotalDownloaded
      UploadedBytes = state.TotalUploaded
      DownloadSpeedBps = state.LastDownloadSpeedBps
      UploadSpeedBps = state.LastUploadSpeedBps
      PeerCount = Map.count state.Peers
      Status = state.Status }

// Writes the block to disk, marks it received, and spawns async SHA-1
// verification once all blocks in the piece are in. Posts PieceVerified back.
let private handleBlockReceived
    (ctx: TorrentContext)
    (state: AgentState)
    (inbox: MailboxProcessor<TorrentCommand>)
    (peerId: byte[])
    (pieceIdx: int)
    (offset: int)
    (data: byte[])
    =
    async {
        let id = toHex peerId

        match Map.tryFind id state.Peers with
        | Option.None -> ()
        | Some peer ->
            match Map.tryFind pieceIdx state.PieceStates with
            | Option.None -> () // already verified, ignore duplicate
            | Some ps ->
                if ps.Data.Length = 0 then
                    ps.Data <- Array.zeroCreate ps.Length

                Array.blit data 0 ps.Data offset data.Length

                do!
                    PieceStore.writeBlock
                        ctx.Meta.Info.FileLayout
                        ctx.SavePath
                        ctx.Meta.Info.PieceLength
                        pieceIdx
                        offset
                        data

                let blockIdx = offset / blockSize
                ps.Blocks[blockIdx] <- Received
                peer.Pending <- max 0 (peer.Pending - 1)
                state.DownloadedThisTick <- state.DownloadedThisTick + int64 data.Length
                state.SessionDownloaded  <- state.SessionDownloaded  + int64 data.Length
                dbg ctx.TorrentId $"Block piece=%d{pieceIdx} offset=%d{offset} len=%d{data.Length}"

                if ps.Blocks |> Array.forall (fun b -> b = Received) then
                    let pieceLen = ps.Length
                    let expected = Sha1Hash ps.ExpectedHash

                    Async.Start(
                        async {
                            let! ok =
                                PieceStore.verifyPiece
                                    ctx.Meta.Info.FileLayout
                                    ctx.SavePath
                                    ctx.Meta.Info.PieceLength
                                    pieceIdx
                                    pieceLen
                                    expected

                            inbox.Post(PieceVerified(pieceIdx, ok))
                        }
                    )
                else
                    dispatchRequests id state
    }

// Updates per-second speed counters, emits a Progress event,
// and resets any timed-out in-flight blocks.
let private handleProgressTick (ctx: TorrentContext) (state: AgentState) (inbox: MailboxProcessor<TorrentCommand>) =
    state.TickCount <- state.TickCount + 1
    state.LastDownloadSpeedBps <- state.DownloadedThisTick
    state.LastUploadSpeedBps <- state.UploadedThisTick
    state.DownloadedThisTick <- 0L
    state.RequestedThisTick <- 0L
    state.UploadedThisTick <- 0L

    ctx.Notify(EngineEvent.Progress(makeProgress ctx state))

    if state.TickCount % 30 = 0 then
        inbox.Post PersistStats

    // Block timeout is 30s; checking every 10s is sufficient and avoids scanning all blocks per-second
    if state.TickCount % 10 = 0 then
        for _, ps in Map.toSeq state.PieceStates do
            for i in 0 .. ps.Blocks.Length - 1 do
                match ps.Blocks[i] with
                | InFlight(_, since) when DateTime.UtcNow - since > blockTimeoutSecs -> ps.Blocks[i] <- Missing
                | _ -> ()


let create
    (torrentId: int)
    (meta: TorrentMetaInfo)
    (savePath: string)
    (initialBitfield: byte[])
    (ourPeerId: byte[])
    (settings: EngineSettings)
    (repository: TorrentRepository)
    (notify: EngineEvent -> unit)
    : TorrentAgent =

    let ctx =
        { TorrentId = torrentId
          Meta = meta
          SavePath = savePath
          OurPeerId = ourPeerId
          Repository = repository
          Notify = notify
          TotalSize = totalSizeOf meta }

    let initialState =
        { Status = TorrentStatus.Paused
          Peers = Map.empty
          PieceStates = Map.empty
          Bitfield = initialBitfield
          TrackerId = Option.None
          NextAnnounce = DateTime.MinValue
          AnnounceInterval = 1800
          AnnounceTiers = defaultArg meta.AnnounceList [ [ meta.Announce ] ]
          DownloadedThisTick = 0L
          RequestedThisTick = 0L
          UploadedThisTick = 0L
          SessionDownloaded = 0L
          SessionUploaded = 0L
          TotalDownloaded =
            [ 0 .. meta.Info.Pieces.Length - 1 ]
            |> List.filter (fun i -> PieceStore.getBit initialBitfield i)
            |> List.sumBy (fun i -> int64 (PieceStore.actualPieceLength meta i))
          TotalUploaded = 0L
          LastDownloadSpeedBps = 0L
          LastUploadSpeedBps = 0L
          Settings = settings
          TickCts = Option.None
          TickCount = 0 }

    let agent =
        MailboxProcessor<TorrentCommand>.Start(fun inbox ->
            let state = initialState
            let fire = fireAnnounce ctx state inbox
            let register = registerPeer ctx state
            let reset = resetInFlight state

            let rec loop () =
                async {
                    let! msg = inbox.Receive()

                    match msg with

                    // lifecycle

                    | Start ->
                        let cts = new Threading.CancellationTokenSource()
                        state.TickCts <- Some cts
                        ctx.Notify(EngineEvent.StatusChanged(ctx.TorrentId, TorrentStatus.Checking))
                        PieceStore.prepareFiles ctx.Meta.Info.FileLayout ctx.SavePath
                        let! corrected = PieceStore.verifyExistingPieces ctx.Meta ctx.SavePath state.Bitfield
                        do! ctx.Repository.UpdateBitfieldAsync(ctx.TorrentId, corrected) |> Async.AwaitTask
                        state.Bitfield <- corrected
                        state.PieceStates <- initPieceStates ctx.Meta corrected

                        let complete = PieceStore.isComplete corrected ctx.Meta.Info.Pieces.Length

                        dbg
                            ctx.TorrentId
                            $"Start: %d{PieceStore.countSet corrected}/%d{ctx.Meta.Info.Pieces.Length} pieces verified, complete=%b{complete}"

                        if complete then
                            state.Status <- TorrentStatus.Seeding
                            ctx.Notify(EngineEvent.StatusChanged(ctx.TorrentId, TorrentStatus.Seeding))
                            fire Completed
                        else
                            state.Status <- TorrentStatus.Downloading
                            ctx.Notify(EngineEvent.StatusChanged(ctx.TorrentId, TorrentStatus.Downloading))
                            fire Started

                        startTicks inbox cts

                    | Resume ->
                        let cts = new Threading.CancellationTokenSource()
                        state.TickCts <- Some cts
                        PieceStore.prepareFiles ctx.Meta.Info.FileLayout ctx.SavePath
                        state.PieceStates <- initPieceStates ctx.Meta state.Bitfield

                        if PieceStore.isComplete state.Bitfield ctx.Meta.Info.Pieces.Length then
                            state.Status <- TorrentStatus.Seeding
                            ctx.Notify(EngineEvent.StatusChanged(ctx.TorrentId, TorrentStatus.Seeding))
                            fire Completed
                        else
                            state.Status <- TorrentStatus.Downloading
                            ctx.Notify(EngineEvent.StatusChanged(ctx.TorrentId, TorrentStatus.Downloading))
                            fire Started

                        startTicks inbox cts

                    | Pause ->
                        state.TickCts
                        |> Option.iter (fun cts ->
                            cts.Cancel()
                            cts.Dispose())

                        state.TickCts <- Option.None
                        fire Stopped

                        for _, peer in Map.toSeq state.Peers do
                            peer.Agent.Post Disconnect
                            peer.Agent.Dispose()

                        state.Peers <- Map.empty
                        state.Status <- TorrentStatus.Paused
                        ctx.Notify(EngineEvent.StatusChanged(ctx.TorrentId, TorrentStatus.Paused))

                        do!
                            ctx.Repository.UpdateStatusAsync(ctx.TorrentId, toStorageStatus TorrentStatus.Paused)
                            |> Async.AwaitTask

                    // tracker

                    | TrackerTick ->
                        if DateTime.UtcNow >= state.NextAnnounce then
                            state.NextAnnounce <- DateTime.UtcNow.AddSeconds(float state.AnnounceInterval)
                            fire AnnounceEvent.None

                    | TrackerResult(Ok response) ->
                        dbg
                            ctx.TorrentId
                            $"Tracker OK: %d{response.Peers.Length} peers, interval=%d{response.Interval}s"

                        state.TrackerId <- response.TrackerId
                        state.NextAnnounce <- DateTime.UtcNow.AddSeconds(float response.Interval)
                        state.AnnounceInterval <- response.Interval

                        response.Peers
                        |> List.truncate 50
                        |> List.iter (fun p -> inbox.Post(ConnectToPeer p))

                    | TrackerResult(Error e) ->
                        dbg ctx.TorrentId $"Tracker error: %A{e} — retry in 30s"
                        state.NextAnnounce <- DateTime.UtcNow.AddSeconds 30.0

                    // peer connections

                    | ConnectToPeer peerInfo ->
                        let (InfoHash infoHashBytes) = ctx.Meta.InfoHash
                        dbg ctx.TorrentId $"Connecting to %s{peerInfo.IP.ToString()}:%d{peerInfo.Port}"

                        Async.Start(
                            async {
                                try
                                    let client = new TcpClient()
                                    do! client.ConnectAsync(peerInfo.IP, int peerInfo.Port) |> Async.AwaitTask
                                    let peerIdRef = ref [||]

                                    let peerNotify ev =
                                        inbox.Post(FromPeer(peerIdRef.Value, ev))

                                    let! result = create client infoHashBytes ctx.OurPeerId peerNotify

                                    match result with
                                    | Ok(peer, startRead) ->
                                        peerIdRef.Value <- peer.PeerId

                                        dbg
                                            ctx.TorrentId
                                            $"Handshake OK with %s{toHex peer.PeerId |> fun s -> s[..11]}"

                                        inbox.Post(
                                            PeerReady(fun () ->
                                                register peer
                                                startRead ())
                                        )
                                    | Error msg ->
                                        dbg ctx.TorrentId $"Handshake failed: %s{msg}"
                                        client.Dispose()
                                with ex ->
                                    dbg ctx.TorrentId $"Connect failed: %s{ex.Message}"
                            }
                        )

                    | PeerReady fn ->
                        fn ()
                        dbg ctx.TorrentId $"Peer registered, total peers: %d{Map.count state.Peers}"

                    | InboundPeer(client, peerId) ->
                        let peerNotify ev = inbox.Post(FromPeer(peerId, ev))
                        let peer, startRead = createInbound client peerId peerNotify
                        register peer
                        startRead ()

                    // peer events

                    | FromPeer(peerId, PeerBitfieldReceived bits) ->
                        let id = toHex peerId

                        match Map.tryFind id state.Peers with
                        | Option.None -> dbg ctx.TorrentId $"Bitfield from unregistered peer %s{id[..7]} — dropped"
                        | Some peer ->
                            peer.Bitfield <- bits
                            let piecesAvailable = PieceStore.countSet bits

                            let hasNeeded =
                                state.PieceStates |> Map.exists (fun i _ -> PieceStore.getBit bits i)

                            dbg
                                ctx.TorrentId
                                $"Peer %s{id[..7]} bitfield: %d{piecesAvailable} pieces, hasNeeded=%b{hasNeeded}, choking=%b{peer.State.PeerChoking}"

                            if hasNeeded then
                                if not peer.State.AmInterested then
                                    peer.State <- { peer.State with AmInterested = true }
                                    peer.Agent.Post(SendMsg Interested)

                                if not peer.State.PeerChoking then
                                    dispatchRequests id state

                    | FromPeer(peerId, PeerHasPiece idx) ->
                        let id = toHex peerId

                        match Map.tryFind id state.Peers with
                        | Option.None -> ()
                        | Some peer ->
                            PieceStore.setBit peer.Bitfield idx

                            if Map.containsKey idx state.PieceStates then
                                if not peer.State.AmInterested then
                                    peer.State <- { peer.State with AmInterested = true }
                                    peer.Agent.Post(SendMsg Interested)

                                if not peer.State.PeerChoking then
                                    dispatchRequests id state

                    | FromPeer(peerId, PeerUnchoked) ->
                        let id = toHex peerId

                        match Map.tryFind id state.Peers with
                        | Option.None -> dbg ctx.TorrentId $"Unchoke from unregistered peer %s{id[..7]} — dropped"
                        | Some peer ->
                            dbg ctx.TorrentId $"Peer %s{id[..7]} unchoked us"
                            peer.State <- { peer.State with PeerChoking = false }
                            dispatchRequests id state

                    | FromPeer(peerId, PeerChoked) ->
                        let id = toHex peerId

                        match Map.tryFind id state.Peers with
                        | Option.None -> ()
                        | Some peer ->
                            peer.State <- { peer.State with PeerChoking = true }
                            reset peerId
                            peer.Pending <- 0

                    | FromPeer(_, PeerInterestedChanged _) -> ()

                    // piece transfer

                    | FromPeer(peerId, BlockReceived(pieceIdx, offset, data)) ->
                        do! handleBlockReceived ctx state inbox peerId pieceIdx offset data

                    | FromPeer(peerId, InboundRequest(pieceIdx, offset, len)) ->
                        if state.Settings.SeedingEnabled && PieceStore.getBit state.Bitfield pieceIdx then
                            let id = toHex peerId

                            match Map.tryFind id state.Peers with
                            | Option.None -> ()
                            | Some peer ->
                                let ulLimit = state.Settings.MaxUploadSpeedMbps
                                let ulBudget = int64 ulLimit * 1024L * 1024L
                                let underLimit = ulLimit = 0 || state.UploadedThisTick + int64 len <= ulBudget

                                if underLimit then
                                    Async.Start(
                                        async {
                                            let! data =
                                                PieceStore.readBlock
                                                    ctx.Meta.Info.FileLayout
                                                    ctx.SavePath
                                                    ctx.Meta.Info.PieceLength
                                                    pieceIdx
                                                    offset
                                                    len

                                            peer.Agent.Post(SendMsg(Piece(pieceIdx, offset, data)))
                                        }
                                    )

                                    state.UploadedThisTick  <- state.UploadedThisTick  + int64 len
                                    state.SessionUploaded   <- state.SessionUploaded   + int64 len

                    | FromPeer(peerId, Disconnected _) ->
                        let id = toHex peerId
                        state.Peers |> Map.tryFind id |> Option.iter (fun p -> p.Agent.Dispose())
                        state.Peers <- Map.remove id state.Peers
                        reset peerId

                        if Map.isEmpty state.Peers && state.Status = TorrentStatus.Downloading then
                            state.NextAnnounce <- DateTime.UtcNow

                    // verification and tick

                    | PieceVerified(pieceIdx, true) ->
                        dbg
                            ctx.TorrentId
                            $"Piece %d{pieceIdx} verified OK (%d{Map.count state.PieceStates - 1} remaining)"

                        state.PieceStates <- Map.remove pieceIdx state.PieceStates
                        PieceStore.setBit state.Bitfield pieceIdx

                        do!
                            ctx.Repository.UpdateBitfieldAsync(ctx.TorrentId, state.Bitfield)
                            |> Async.AwaitTask

                        state.TotalDownloaded <-
                            state.TotalDownloaded + int64 (PieceStore.actualPieceLength ctx.Meta pieceIdx)

                        for _, peer in Map.toSeq state.Peers do
                            peer.Agent.Post(SendMsg(Have pieceIdx))

                        if PieceStore.isComplete state.Bitfield ctx.Meta.Info.Pieces.Length then
                            do! handleCompletion ctx state inbox
                        else
                            for id in state.Peers |> Map.toSeq |> Seq.map fst do
                                dispatchRequests id state

                    | PieceVerified(pieceIdx, false) ->
                        dbg ctx.TorrentId $"Piece %d{pieceIdx} hash FAILED — resetting blocks"

                        match Map.tryFind pieceIdx state.PieceStates with
                        | Option.None -> ()
                        | Some ps -> Array.fill ps.Blocks 0 ps.Blocks.Length Missing

                    | SettingsUpdated newSettings -> state.Settings <- newSettings

                    | GetProgress reply -> reply.Reply(makeProgress ctx state)

                    | ProgressTick -> handleProgressTick ctx state inbox

                    | PersistStats ->
                        if state.SessionDownloaded > 0L || state.SessionUploaded > 0L then
                            let dl = state.SessionDownloaded
                            let ul = state.SessionUploaded
                            state.SessionDownloaded <- 0L
                            state.SessionUploaded   <- 0L
                            let! struct (totalDl, totalUl) =
                                ctx.Repository.IncrementTransferStatsAsync(ctx.TorrentId, ul, dl)
                                |> Async.AwaitTask
                            ctx.Notify(EngineEvent.GlobalStatsUpdate(totalDl, totalUl))

                    return! loop ()
                }

            loop ())

    { TorrentId = torrentId
      Post = agent.Post
      Dispose = fun () -> (agent :> IDisposable).Dispose() }
