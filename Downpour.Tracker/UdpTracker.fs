module Downpour.Tracker.UdpTracker

open System
open System.Net
open System.Net.Sockets
open System.Buffers.Binary
open System.Text
open System.Threading

// helpers
let private readInt32BE (buf: byte[]) (offset: int) =
    BinaryPrimitives.ReadInt32BigEndian(ReadOnlySpan(buf, offset, 4))

let private readInt64BE (buf: byte[]) (offset: int) =
    BinaryPrimitives.ReadInt64BigEndian(ReadOnlySpan(buf, offset, 8))

let private writeInt32BE (value: int32) =
    let buf = Array.zeroCreate 4
    BinaryPrimitives.WriteInt32BigEndian(Span buf, value)
    buf

let private writeInt64BE (value: int64) =
    let buf = Array.zeroCreate 8
    BinaryPrimitives.WriteInt64BigEndian(Span buf, value)
    buf

let private writeUInt16BE (value: uint16) =
    let buf = Array.zeroCreate 2
    BinaryPrimitives.WriteUInt16BigEndian(Span buf, value)
    buf
// end helpers

let private parseCompactPeers (bytes: byte[]) : Result<PeerInfo list, TrackerError> =
    if bytes.Length % 6 <> 0 then
        Error(ParseError $"Compact peer list length {bytes.Length} is not a multiple of 6")
    else
        bytes
        |> Array.chunkBySize 6
        |> Array.map (fun chunk ->
            let ip = IPAddress(chunk.[0..3])
            let port = uint16 ((int chunk.[4] <<< 8) ||| int chunk.[5])
            { IP = ip; Port = port })
        |> Array.toList
        |> Ok


let internal buildConnectRequest (transactionId: int32) : byte[] =
    Array.concat [ writeInt64BE 0x41727101980L; writeInt32BE 0; writeInt32BE transactionId ]

let internal parseConnectResponse (expectedTxId: int32) (buf: byte[]) : Result<int64, TrackerError> =
    if buf.Length < 16 then
        Error(ParseError $"Connect response too short: {buf.Length} bytes")
    else
        let action = readInt32BE buf 0
        let txId = readInt32BE buf 4

        if action = 2 then
            Error(TrackerFailure(Encoding.ASCII.GetString(buf, 8, buf.Length - 8)))
        elif action <> 0 then
            Error(ParseError $"Expected action 0 (connect), got {action}")
        elif txId <> expectedTxId then
            Error(ParseError $"Transaction ID mismatch: expected {expectedTxId}, got {txId}")
        else
            Ok(readInt64BE buf 8)


let private encodeEvent (e: AnnounceEvent) =
    match e with
    | AnnounceEvent.None -> 0
    | Completed -> 1
    | Started -> 2
    | Stopped -> 3

let internal buildAnnounceRequest
    (connectionId: int64)
    (transactionId: int32)
    (key: int32)
    (req: AnnounceRequest)
    : byte[] =
    Array.concat
        [ writeInt64BE connectionId
          writeInt32BE 1
          writeInt32BE transactionId
          req.InfoHash
          req.PeerId
          writeInt64BE req.Downloaded
          writeInt64BE req.Left
          writeInt64BE req.Uploaded
          writeInt32BE (encodeEvent req.Event)
          writeInt32BE 0
          writeInt32BE key
          writeInt32BE -1
          writeUInt16BE req.Port ]

let internal parseAnnounceResponse (expectedTxId: int32) (buf: byte[]) : Result<AnnounceResponse, TrackerError> =
    if buf.Length < 20 then
        Error(ParseError $"Announce response too short: {buf.Length} bytes")
    else
        let action = readInt32BE buf 0
        let txId = readInt32BE buf 4

        if action = 2 then
            Error(TrackerFailure(Encoding.ASCII.GetString(buf, 8, buf.Length - 8)))
        elif action <> 1 then
            Error(ParseError $"Expected action 1 (announce), got {action}")
        elif txId <> expectedTxId then
            Error(ParseError $"Transaction ID mismatch: expected {expectedTxId}, got {txId}")
        else
            let interval = readInt32BE buf 8
            let leechers = readInt32BE buf 12
            let seeders = readInt32BE buf 16
            let peerBytes = buf.[20..]

            parseCompactPeers peerBytes
            |> Result.map (fun peers ->
                { Interval = interval
                  MinInterval = Option.None
                  TrackerId = Option.None
                  Seeders = seeders
                  Leechers = leechers
                  Peers = peers })


// retry
let private sendAndReceive
    (client: UdpClient)
    (packet: byte[])
    (parse: byte[] -> Result<'a, TrackerError>)
    (maxAttempts: int)
    : Async<Result<'a, TrackerError>> =
    let rec loop n =
        async {
            if n >= maxAttempts then
                return Error TrackerError.Timeout
            else
                client.Send(packet, packet.Length) |> ignore
                let timeoutMs = 15_000 * (1 <<< n)
                use cts = new CancellationTokenSource(timeoutMs)

                try
                    let! result = client.ReceiveAsync(cts.Token).AsTask() |> Async.AwaitTask

                    match parse result.Buffer with
                    | Ok v -> return Ok v
                    | Error _ -> return! loop (n + 1)
                with :? OperationCanceledException ->
                    return! loop (n + 1)
        }

    loop 0

let announce (url: string) (req: AnnounceRequest) : Async<Result<AnnounceResponse, TrackerError>> =
    async {
        let uri = Uri(url)
        let host = uri.Host
        let port = uri.Port

        use client = new UdpClient()

        try
            client.Connect(host, port)

            let rng = Random.Shared
            let key = rng.Next()

            let connectTxId = rng.Next()
            let connectPacket = buildConnectRequest connectTxId
            let! connectResult = sendAndReceive client connectPacket (parseConnectResponse connectTxId) 8

            match connectResult with
            | Error e -> return Error e
            | Ok connectionId ->

                let rec announceLoop (connId: int64) (connTime: DateTime) (n: int) =
                    async {
                        if n >= 8 then
                            return Error TrackerError.Timeout
                        else
                            let! refreshResult =
                                if (DateTime.UtcNow - connTime).TotalMinutes >= 2.0 then
                                    async {
                                        let txId = rng.Next()
                                        let packet = buildConnectRequest txId
                                        let! r = sendAndReceive client packet (parseConnectResponse txId) 1
                                        return r |> Result.map (fun cid -> cid, DateTime.UtcNow)
                                    }
                                else
                                    async { return Ok(connId, connTime) }

                            match refreshResult with
                            | Error e -> return Error e
                            | Ok(currentConnId, currentConnTime) ->

                                let txId = rng.Next()
                                let packet = buildAnnounceRequest currentConnId txId key req
                                let timeoutMs = 15_000 * (1 <<< n)
                                use cts = new CancellationTokenSource(timeoutMs)

                                try
                                    client.Send(packet, packet.Length) |> ignore
                                    let! result = client.ReceiveAsync(cts.Token).AsTask() |> Async.AwaitTask

                                    match parseAnnounceResponse txId result.Buffer with
                                    | Ok r -> return Ok r
                                    | Error _ -> return! announceLoop currentConnId currentConnTime (n + 1)
                                with :? OperationCanceledException ->
                                    return! announceLoop currentConnId currentConnTime (n + 1)
                    }

                return! announceLoop connectionId DateTime.UtcNow 0
        with ex ->
            return Error(NetworkError ex.Message)
    }
