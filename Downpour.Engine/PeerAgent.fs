module Downpour.Engine.PeerAgent

open System
open System.Net.Sockets
open Downpour.Protocol
open Downpour.Engine.Types

type PeerAgent =
    { PeerId: byte[]
      Post: PeerCommand -> unit
      Dispose: unit -> unit }

let private readExactly (stream: NetworkStream) (buf: byte[]) (count: int) : Async<unit> =
    async {
        let mutable total = 0

        while total < count do
            let! n = stream.ReadAsync(buf, total, count - total) |> Async.AwaitTask

            if n = 0 then
                failwith "connection closed during read"

            total <- total + n
    }

let private dispatch (msg: PeerMessage) (notify: PeerEvent -> unit) =
    match msg with
    | KeepAlive -> ()
    | Choke -> notify PeerChoked
    | Unchoke -> notify PeerUnchoked
    | Interested -> notify (PeerInterestedChanged true)
    | NotInterested -> notify (PeerInterestedChanged false)
    | Have idx -> notify (PeerHasPiece idx)
    | Bitfield bits -> notify (PeerBitfieldReceived bits)
    | Piece(idx, off, data) -> notify (BlockReceived(idx, off, data))
    | Request(idx, off, len) -> notify (InboundRequest(idx, off, len))
    | Cancel _ -> () // not implemented for now, not needed for basic functionality
    | Port _ -> () // not implemented, needed only for DHT

let private readLoop (stream: NetworkStream) (notify: PeerEvent -> unit) : Async<unit> =
    async {
        let readBuf = Array.zeroCreate 65536
        let mutable pending = ReadOnlyMemory<byte>.Empty

        let rec loop () =
            async {
                let mutable parsing = true

                while parsing do
                    match Deserializer.parse pending with
                    | Ok(msg, remaining) ->
                        pending <- remaining
                        dispatch msg notify
                    | Error _ -> parsing <- false

                try
                    let! n = stream.ReadAsync(readBuf, 0, readBuf.Length) |> Async.AwaitTask

                    if n = 0 then
                        notify (Disconnected "connection closed")
                    else
                        let combined = Array.zeroCreate (pending.Length + n)
                        pending.CopyTo(Memory combined)
                        Array.blit readBuf 0 combined pending.Length n
                        pending <- ReadOnlyMemory combined
                        return! loop ()
                with
                | :? System.IO.IOException -> notify (Disconnected "connection closed")
                | :? ObjectDisposedException -> notify (Disconnected "connection closed")
            }

        return! loop ()
    }

let private writeLoop
    (stream: NetworkStream)
    (notify: PeerEvent -> unit)
    (agent: MailboxProcessor<PeerCommand>)
    : Async<unit> =
    async {
        let keepAliveMs = 90_000

        let rec loop (lastSentAt: DateTime) =
            async {
                let elapsed = int (DateTime.UtcNow - lastSentAt).TotalMilliseconds
                let timeout = max 1 (keepAliveMs - elapsed)
                let! cmd = agent.TryReceive(timeout)

                try
                    match cmd with
                    | Some(SendMsg msg) ->
                        let bytes = Serializer.serialize msg
                        do! stream.WriteAsync(bytes, 0, bytes.Length) |> Async.AwaitTask
                        return! loop DateTime.UtcNow

                    | Some Disconnect ->
                        stream.Close()
                        notify (Disconnected "disconnected by us")

                    | None ->
                        let ka = Serializer.serialize KeepAlive
                        do! stream.WriteAsync(ka, 0, ka.Length) |> Async.AwaitTask
                        return! loop DateTime.UtcNow
                with
                | :? System.IO.IOException -> notify (Disconnected "connection closed")
                | :? ObjectDisposedException -> notify (Disconnected "connection closed")
            }

        return! loop DateTime.UtcNow
    }

// Creates the write agent but does not start the read loop.
// Returns the peer and a function the caller must invoke to begin receiving messages.
// This allows the caller to register the peer before any FromPeer events can arrive.
let private makeAgent
    (client: TcpClient)
    (stream: NetworkStream)
    (peerId: byte[])
    (notify: PeerEvent -> unit)
    : PeerAgent * (unit -> unit) =
    let agent = MailboxProcessor<PeerCommand>.Start(writeLoop stream notify)
    let startRead () = Async.Start(readLoop stream notify)
    { PeerId = peerId; Post = agent.Post; Dispose = fun () -> client.Dispose() }, startRead

let create
    (client: TcpClient)
    (infoHash: byte[])
    (ourPeerId: byte[])
    (notify: PeerEvent -> unit)
    : Async<Result<PeerAgent * (unit -> unit), string>> =
    async {
        try
            let stream = client.GetStream()
            let hs = Handshake.serialize infoHash ourPeerId
            do! stream.WriteAsync(hs, 0, hs.Length) |> Async.AwaitTask

            let buf = Array.zeroCreate 68
            do! readExactly stream buf 68

            match Handshake.deserialize buf with
            | Error msg ->
                client.Dispose()
                return Error msg
            | Ok handshake ->
                if handshake.InfoHash <> infoHash then
                    client.Dispose()
                    return Error "info_hash mismatch"
                else
                    return Ok(makeAgent client stream handshake.PeerId notify)
        with ex ->
            client.Dispose()
            return Error ex.Message
    }

// Handshake already completed by EngineAgent before routing here.
let createInbound (client: TcpClient) (peerId: byte[]) (notify: PeerEvent -> unit) : PeerAgent * (unit -> unit) =
    makeAgent client (client.GetStream()) peerId notify
