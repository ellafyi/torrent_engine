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
      Bitfield: byte[]
      State: PeerState
      Pending: int }

type private AgentState =
    { Status: TorrentStatus
      Peers: Map<string, ActivePeer>
      PieceStates: Map<int, PieceState>
      Bitfield: byte[]
      TrackerId: string option
      NextAnnounce: DateTime
      AnnounceInterval: int
      AnnounceTiers: string list list
      DownloadedThisTick: int64
      RequestedThisTick: int64
      UploadedThisTick: int64
      SessionDownloaded: int64
      SessionUploaded: int64
      TotalDownloaded: int64
      TotalUploaded: int64
      LastDownloadSpeedBps: int64
      LastUploadSpeedBps: int64
      Settings: EngineSettings
      TickCts: Threading.CancellationTokenSource option
      TickCount: int }

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
    let rec tryUrls urls =
        async {
            match urls with
            | [] -> return Error(ParseError "no trackers in tier")
            | url :: rest ->
                let! r = Tracker.announce url req

                match r with
                | Ok _ -> return r
                | Error _ -> return! tryUrls rest
        }

    let rec tryTiers tiers =
        async {
            match tiers with
            | [] -> return Error(ParseError "no trackers configured")
            | tier :: rest ->
                let shuffled = tier |> List.sortBy (fun _ -> Random.Shared.Next())
                let! r = tryUrls shuffled

                match r with
                | Ok _ -> return r
                | Error _ -> return! tryTiers rest
        }

    tryTiers tiers

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

let rec private dispatchRequestsRecursive (peerId: string) (state: AgentState) : AgentState =
    match Map.tryFind peerId state.Peers with
    | Option.None -> state
    | Some peer when peer.State.PeerChoking || peer.Pending >= pipelineDepth -> state
    | Some peer ->
        match findNextBlock peer state with
        | Option.None -> state
        | Some(pieceIdx, blockIdx, blockOffset, blockLen) ->

            let dlLimit = state.Settings.MaxDownloadSpeedKbps
            let dlBudget = int64 dlLimit * 1024L

            if dlLimit > 0 && state.RequestedThisTick + int64 blockLen > dlBudget then
                state
            else
                let ps = state.PieceStates[pieceIdx]
                ps.Blocks[blockIdx] <- InFlight(peer.Agent.PeerId, DateTime.UtcNow)
                peer.Agent.Post(SendMsg(Request(pieceIdx, blockOffset, blockLen)))

                let updatedPeer = { peer with Pending = peer.Pending + 1 }

                let updatedState =
                    { state with
                        Peers = Map.add peerId updatedPeer state.Peers
                        RequestedThisTick = state.RequestedThisTick + int64 blockLen }

                dispatchRequestsRecursive peerId updatedState

let private dispatchRequests (peerId: string) (state: AgentState) = dispatchRequestsRecursive peerId state

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

        let newState = { state with Peers = Map.add id activePeer state.Peers }

        if PieceStore.countSet state.Bitfield > 0 then
            peer.Post(SendMsg(Bitfield state.Bitfield))

        newState
    else
        state

let private resetInFlight (state: AgentState) (peerId: byte[]) : unit =
    for _, ps in Map.toSeq state.PieceStates do
        for i in 0 .. ps.Blocks.Length - 1 do
            match ps.Blocks[i] with
            | InFlight(pid, _) when pid = peerId -> ps.Blocks[i] <- Missing
            | _ -> ()

let private handleCompletion (ctx: TorrentContext) (state: AgentState) (inbox: MailboxProcessor<TorrentCommand>) =
    async {
        ctx.Notify(EngineEvent.StatusChanged(ctx.TorrentId, TorrentStatus.Seeding))

        do!
            ctx.Repository.UpdateStatusAsync(ctx.TorrentId, toStorageStatus TorrentStatus.Seeding)
            |> Async.AwaitTask

        let updatedState = { state with Status = TorrentStatus.Seeding }
        fireAnnounce ctx updatedState inbox Completed

        if not state.Settings.SeedingEnabled then
            inbox.Post Pause

        return updatedState
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
        | Option.None -> return state
        | Some peer ->
            match Map.tryFind pieceIdx state.PieceStates with
            | Option.None -> return state // already verified, ignore duplicate
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

                let updatedPeer = { peer with Pending = max 0 (peer.Pending - 1) }

                let updatedState =
                    { state with
                        Peers = Map.add id updatedPeer state.Peers
                        DownloadedThisTick = state.DownloadedThisTick + int64 data.Length
                        SessionDownloaded = state.SessionDownloaded + int64 data.Length }

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

                    return updatedState
                else
                    return dispatchRequests id updatedState
    }

let private handleInboundRequest (ctx: TorrentContext) (state: AgentState) (peerId: byte[]) (pieceIdx: int) (offset: int) (len: int) =
    if state.Settings.SeedingEnabled && PieceStore.getBit state.Bitfield pieceIdx then
        let id = toHex peerId

        match Map.tryFind id state.Peers with
        | Option.None -> state
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

                { state with
                    UploadedThisTick = state.UploadedThisTick + int64 len
                    SessionUploaded = state.SessionUploaded + int64 len }
            else
                state
    else
        state

// Updates per-second speed counters, emits a Progress event,
// and resets any timed-out in-flight blocks.
let private handleProgressTick (ctx: TorrentContext) (state: AgentState) (inbox: MailboxProcessor<TorrentCommand>) =
    let tickCount = state.TickCount + 1

    let state =
        { state with
            TickCount = tickCount
            LastDownloadSpeedBps = state.DownloadedThisTick
            LastUploadSpeedBps = state.UploadedThisTick
            DownloadedThisTick = 0L
            RequestedThisTick = 0L
            UploadedThisTick = 0L }

    ctx.Notify(EngineEvent.Progress(makeProgress ctx state))

    if tickCount % 30 = 0 then
        inbox.Post PersistStats

    // Block timeout is 30s; checking every 10s is sufficient and avoids scanning all blocks per-second
    if tickCount % 10 = 0 then
        for _, ps in Map.toSeq state.PieceStates do
            for i in 0 .. ps.Blocks.Length - 1 do
                match ps.Blocks[i] with
                | InFlight(_, since) when DateTime.UtcNow - since > blockTimeoutSecs -> ps.Blocks[i] <- Missing
                | _ -> ()

    state


let private handleStart (ctx: TorrentContext) (state: AgentState) (inbox: MailboxProcessor<TorrentCommand>) =
    async {
        let cts = new Threading.CancellationTokenSource()
        ctx.Notify(EngineEvent.StatusChanged(ctx.TorrentId, TorrentStatus.Checking))
        PieceStore.prepareFiles ctx.Meta.Info.FileLayout ctx.SavePath
        let! corrected = PieceStore.verifyExistingPieces ctx.Meta ctx.SavePath state.Bitfield
        do! ctx.Repository.UpdateBitfieldAsync(ctx.TorrentId, corrected) |> Async.AwaitTask

        let complete = PieceStore.isComplete corrected ctx.Meta.Info.Pieces.Length

        dbg
            ctx.TorrentId
            $"Start: %d{PieceStore.countSet corrected}/%d{ctx.Meta.Info.Pieces.Length} pieces verified, complete=%b{complete}"

        let status =
            if complete then
                TorrentStatus.Seeding
            else
                TorrentStatus.Downloading

        ctx.Notify(EngineEvent.StatusChanged(ctx.TorrentId, status))

        let updatedState =
            { state with
                TickCts = Some cts
                Bitfield = corrected
                PieceStates = initPieceStates ctx.Meta corrected
                Status = status }

        fireAnnounce ctx updatedState inbox (if complete then Completed else Started)
        startTicks inbox cts
        return updatedState
    }

let private handleResume (ctx: TorrentContext) (state: AgentState) (inbox: MailboxProcessor<TorrentCommand>) =
    async {
        let cts = new Threading.CancellationTokenSource()
        PieceStore.prepareFiles ctx.Meta.Info.FileLayout ctx.SavePath

        let complete = PieceStore.isComplete state.Bitfield ctx.Meta.Info.Pieces.Length

        let status =
            if complete then
                TorrentStatus.Seeding
            else
                TorrentStatus.Downloading

        ctx.Notify(EngineEvent.StatusChanged(ctx.TorrentId, status))

        let updatedState =
            { state with
                TickCts = Some cts
                PieceStates = initPieceStates ctx.Meta state.Bitfield
                Status = status }

        fireAnnounce ctx updatedState inbox (if complete then Completed else Started)
        startTicks inbox cts
        return updatedState
    }

let private handlePause (ctx: TorrentContext) (state: AgentState) (inbox: MailboxProcessor<TorrentCommand>) =
    async {
        state.TickCts
        |> Option.iter (fun cts ->
            cts.Cancel()
            cts.Dispose())

        fireAnnounce ctx state inbox Stopped

        for _, peer in Map.toSeq state.Peers do
            peer.Agent.Post Disconnect
            peer.Agent.Dispose()

        ctx.Notify(EngineEvent.StatusChanged(ctx.TorrentId, TorrentStatus.Paused))

        do!
            ctx.Repository.UpdateStatusAsync(ctx.TorrentId, toStorageStatus TorrentStatus.Paused)
            |> Async.AwaitTask

        return
            { state with
                TickCts = Option.None
                Peers = Map.empty
                Status = TorrentStatus.Paused }
    }

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
            |> List.filter (PieceStore.getBit initialBitfield)
            |> List.sumBy (fun i -> int64 (PieceStore.actualPieceLength meta i))
          TotalUploaded = 0L
          LastDownloadSpeedBps = 0L
          LastUploadSpeedBps = 0L
          Settings = settings
          TickCts = Option.None
          TickCount = 0 }

    let agent =
        MailboxProcessor<TorrentCommand>.Start(fun inbox ->

            let rec loop state =
                async {
                    let! msg = inbox.Receive()

                    match msg with

                    // lifecycle

                    | Start ->
                        let! newState = handleStart ctx state inbox
                        return! loop newState

                    | Resume ->
                        let! newState = handleResume ctx state inbox
                        return! loop newState

                    | Pause ->
                        let! newState = handlePause ctx state inbox
                        return! loop newState

                    // tracker

                    | TrackerTick ->
                        if DateTime.UtcNow >= state.NextAnnounce then
                            let newState = { state with NextAnnounce = DateTime.UtcNow.AddSeconds(float state.AnnounceInterval) }
                            fireAnnounce ctx newState inbox AnnounceEvent.None
                            return! loop newState
                        else
                            return! loop state

                    | TrackerResult(Ok response) ->
                        dbg
                            ctx.TorrentId
                            $"Tracker OK: %d{response.Peers.Length} peers, interval=%d{response.Interval}s"

                        let newState =
                            { state with
                                TrackerId = response.TrackerId
                                NextAnnounce = DateTime.UtcNow.AddSeconds(float response.Interval)
                                AnnounceInterval = response.Interval }

                        response.Peers
                        |> List.truncate 50
                        |> List.iter (fun p -> inbox.Post(ConnectToPeer p))

                        return! loop newState

                    | TrackerResult(Error e) ->
                        dbg ctx.TorrentId $"Tracker error: %A{e} — retry in 30s"
                        let newState = { state with NextAnnounce = DateTime.UtcNow.AddSeconds 30.0 }
                        return! loop newState

                    // peer connections

                    | ConnectToPeer peerInfo ->
                        let (InfoHash infoHashBytes) = ctx.Meta.InfoHash
                        dbg ctx.TorrentId $"Connecting to %s{peerInfo.IP.ToString()}:%d{peerInfo.Port}"

                        Async.Start(
                            async {
                                try
                                    let client = new TcpClient()
                                    do! client.ConnectAsync(peerInfo.IP, int peerInfo.Port) |> Async.AwaitTask

                                    // peerIdRef is filled with the real peer ID after the handshake,
                                    // before PeerReady is posted, so events always route correctly.
                                    let peerIdRef = ref [||]
                                    let notify ev = inbox.Post(FromPeer(!peerIdRef, ev))

                                    let! result = PeerAgent.create client infoHashBytes ctx.OurPeerId notify

                                    match result with
                                    | Ok(peer, startRead) ->
                                        dbg
                                            ctx.TorrentId
                                            $"Handshake OK with %s{toHex peer.PeerId |> fun s -> s[..11]}"

                                        peerIdRef := peer.PeerId
                                        inbox.Post(PeerReady(peer, startRead))
                                    | Error msg ->
                                        dbg ctx.TorrentId $"Handshake failed: %s{msg}"
                                        client.Dispose()
                                with ex ->
                                    dbg ctx.TorrentId $"Connect failed: %s{ex.Message}"
                            }
                        )
                        return! loop state

                    | PeerReady(peerObj, startRead) ->
                        let peer = peerObj :?> PeerAgent
                        let newState = registerPeer ctx state peer
                        startRead ()
                        dbg ctx.TorrentId $"Peer registered, total peers: %d{Map.count newState.Peers}"
                        return! loop newState

                    | InboundPeer(client, peerId) ->
                        let peer, startRead = PeerAgent.createInbound client peerId (fun ev -> inbox.Post(FromPeer(peerId, ev)))
                        let newState = registerPeer ctx state peer
                        startRead ()
                        return! loop newState

                    // peer events

                    | FromPeer(peerId, ev) ->
                        let id = toHex peerId

                        match ev with
                        | PeerBitfieldReceived bits ->
                            match Map.tryFind id state.Peers with
                            | Option.None ->
                                dbg ctx.TorrentId $"Bitfield from unregistered peer %s{id[..7]} — dropped"
                                return! loop state
                            | Some peer ->
                                let updatedPeer = { peer with Bitfield = bits }
                                let piecesAvailable = PieceStore.countSet bits

                                let hasNeeded =
                                    state.PieceStates |> Map.exists (fun i _ -> PieceStore.getBit bits i)

                                dbg
                                    ctx.TorrentId
                                    $"Peer %s{id[..7]} bitfield: %d{piecesAvailable} pieces, hasNeeded=%b{hasNeeded}, choking=%b{peer.State.PeerChoking}"

                                if hasNeeded then
                                    let state' =
                                        if not peer.State.AmInterested then
                                            let p' = { updatedPeer with State = { updatedPeer.State with AmInterested = true } }
                                            p'.Agent.Post(SendMsg Interested)
                                            { state with Peers = Map.add id p' state.Peers }
                                        else
                                            { state with Peers = Map.add id updatedPeer state.Peers }

                                    if not peer.State.PeerChoking then
                                        return! loop (dispatchRequests id state')
                                    else
                                        return! loop state'
                                else
                                    return! loop { state with Peers = Map.add id updatedPeer state.Peers }

                        | PeerHasPiece idx ->
                            match Map.tryFind id state.Peers with
                            | Option.None -> return! loop state
                            | Some peer ->
                                let updatedBitfield = Array.copy peer.Bitfield
                                PieceStore.setBit updatedBitfield idx
                                let updatedPeer = { peer with Bitfield = updatedBitfield }

                                if Map.containsKey idx state.PieceStates then
                                    let state' =
                                        if not peer.State.AmInterested then
                                            let p' = { updatedPeer with State = { updatedPeer.State with AmInterested = true } }
                                            p'.Agent.Post(SendMsg Interested)
                                            { state with Peers = Map.add id p' state.Peers }
                                        else
                                            { state with Peers = Map.add id updatedPeer state.Peers }

                                    if not peer.State.PeerChoking then
                                        return! loop (dispatchRequests id state')
                                    else
                                        return! loop state'
                                else
                                    return! loop { state with Peers = Map.add id updatedPeer state.Peers }

                        | PeerUnchoked ->
                            match Map.tryFind id state.Peers with
                            | Option.None ->
                                dbg ctx.TorrentId $"Unchoke from unregistered peer %s{id[..7]} — dropped"
                                return! loop state
                            | Some peer ->
                                dbg ctx.TorrentId $"Peer %s{id[..7]} unchoked us"
                                let updatedPeer = { peer with State = { peer.State with PeerChoking = false } }
                                let newState = { state with Peers = Map.add id updatedPeer state.Peers }
                                return! loop (dispatchRequests id newState)

                        | PeerChoked ->
                            match Map.tryFind id state.Peers with
                            | Option.None -> return! loop state
                            | Some peer ->
                                let updatedPeer = { peer with State = { peer.State with PeerChoking = true }; Pending = 0 }
                                let newState = { state with Peers = Map.add id updatedPeer state.Peers }
                                resetInFlight newState peerId
                                return! loop newState

                        | BlockReceived(pieceIdx, offset, data) ->
                            let! newState = handleBlockReceived ctx state inbox peerId pieceIdx offset data
                            return! loop newState

                        | InboundRequest(pieceIdx, offset, len) ->
                            let newState = handleInboundRequest ctx state peerId pieceIdx offset len
                            return! loop newState

                        | Disconnected _ ->
                            state.Peers |> Map.tryFind id |> Option.iter (fun p -> p.Agent.Dispose())

                            let newState = { state with Peers = Map.remove id state.Peers }
                            resetInFlight newState peerId

                            let newState' =
                                if Map.isEmpty newState.Peers && newState.Status = TorrentStatus.Downloading then
                                    { newState with NextAnnounce = DateTime.UtcNow }
                                else
                                    newState

                            return! loop newState'

                        | PeerInterestedChanged _ -> return! loop state

                    // verification and tick

                    | PieceVerified(pieceIdx, true) ->
                        dbg
                            ctx.TorrentId
                            $"Piece %d{pieceIdx} verified OK (%d{Map.count state.PieceStates - 1} remaining)"

                        let updatedBitfield = Array.copy state.Bitfield
                        PieceStore.setBit updatedBitfield pieceIdx

                        do!
                            ctx.Repository.UpdateBitfieldAsync(ctx.TorrentId, updatedBitfield)
                            |> Async.AwaitTask

                        let updatedState =
                            { state with
                                Bitfield = updatedBitfield
                                PieceStates = Map.remove pieceIdx state.PieceStates
                                TotalDownloaded = state.TotalDownloaded + int64 (PieceStore.actualPieceLength ctx.Meta pieceIdx) }

                        for _, peer in Map.toSeq updatedState.Peers do
                            peer.Agent.Post(SendMsg(Have pieceIdx))

                        if PieceStore.isComplete updatedState.Bitfield ctx.Meta.Info.Pieces.Length then
                            let! finalState = handleCompletion ctx updatedState inbox
                            return! loop finalState
                        else
                            let mutable state' = updatedState
                            for id in updatedState.Peers |> Map.toSeq |> Seq.map fst do
                                state' <- dispatchRequests id state'
                            return! loop state'

                    | PieceVerified(pieceIdx, false) ->
                        dbg ctx.TorrentId $"Piece %d{pieceIdx} hash FAILED — resetting blocks"

                        match Map.tryFind pieceIdx state.PieceStates with
                        | Option.None -> return! loop state
                        | Some ps ->
                            let newBlocks = Array.copy ps.Blocks
                            Array.fill newBlocks 0 newBlocks.Length Missing
                            let newState = { state with PieceStates = Map.add pieceIdx { ps with Blocks = newBlocks } state.PieceStates }
                            return! loop newState

                    | SettingsUpdated newSettings -> return! loop { state with Settings = newSettings }

                    | GetProgress reply ->
                        reply.Reply(makeProgress ctx state)
                        return! loop state

                    | ProgressTick ->
                        let newState = handleProgressTick ctx state inbox
                        return! loop newState

                    | PersistStats ->
                        if state.SessionDownloaded > 0L || state.SessionUploaded > 0L then
                            let dl = state.SessionDownloaded
                            let ul = state.SessionUploaded

                            let! struct (totalDl, totalUl) =
                                ctx.Repository.IncrementTransferStatsAsync(ctx.TorrentId, ul, dl)
                                |> Async.AwaitTask

                            ctx.Notify(EngineEvent.GlobalStatsUpdate(totalDl, totalUl))

                            return! loop { state with SessionDownloaded = 0L; SessionUploaded = 0L }
                        else
                            return! loop state
                }

            loop initialState)

    { TorrentId = torrentId
      Post = agent.Post
      Dispose = fun () -> (agent :> IDisposable).Dispose() }
