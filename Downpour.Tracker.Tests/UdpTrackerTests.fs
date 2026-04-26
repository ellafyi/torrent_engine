module Downpour.Tracker.Tests.UdpTrackerTests

open System.Net
open System.Buffers.Binary
open Xunit
open Downpour.Tracker
open Downpour.Tracker.UdpTracker

let private defaultReq: AnnounceRequest =
    { InfoHash = Array.create 20 0xAAuy
      PeerId = Array.create 20 0xBBuy
      Port = 6881us
      Uploaded = 1000L
      Downloaded = 2000L
      Left = 3000L
      Event = AnnounceEvent.None
      TrackerId = Option.None }

let private readI32 (buf: byte[]) offset =
    BinaryPrimitives.ReadInt32BigEndian(System.ReadOnlySpan(buf, offset, 4))

let private readI64 (buf: byte[]) offset =
    BinaryPrimitives.ReadInt64BigEndian(System.ReadOnlySpan(buf, offset, 8))

// connect request

[<Fact>]
let ``UDP-01 buildConnectRequest is 16 bytes`` () =
    Assert.Equal(16, buildConnectRequest 12345 |> Array.length)

[<Fact>]
let ``UDP-02 buildConnectRequest magic constant at bytes 0-7`` () =
    let buf = buildConnectRequest 12345
    Assert.Equal(0x41727101980L, readI64 buf 0)

[<Fact>]
let ``UDP-03 buildConnectRequest action is 0 at bytes 8-11`` () =
    let buf = buildConnectRequest 12345
    Assert.Equal(0, readI32 buf 8)

[<Fact>]
let ``UDP-04 buildConnectRequest encodes transactionId at bytes 12-15`` () =
    let buf = buildConnectRequest 0x01020304
    Assert.Equal(0x01020304, readI32 buf 12)

// connect response

let private makeConnectResponse (action: int) (txId: int) (connId: int64) =
    let buf = Array.zeroCreate 16
    BinaryPrimitives.WriteInt32BigEndian(System.Span(buf, 0, 4), action)
    BinaryPrimitives.WriteInt32BigEndian(System.Span(buf, 4, 4), txId)
    BinaryPrimitives.WriteInt64BigEndian(System.Span(buf, 8, 8), connId)
    buf

[<Fact>]
let ``UDP-05 parseConnectResponse returns connection_id for valid response`` () =
    let buf = makeConnectResponse 0 42 99999L
    Assert.Equal(Ok 99999L, parseConnectResponse 42 buf)

[<Fact>]
let ``UDP-06 parseConnectResponse returns ParseError when too short`` () =
    Assert.True(Result.isError (parseConnectResponse 1 (Array.zeroCreate 15)))

[<Fact>]
let ``UDP-07 parseConnectResponse returns ParseError for wrong action`` () =
    let buf = makeConnectResponse 1 42 0L
    Assert.True(Result.isError (parseConnectResponse 42 buf))

[<Fact>]
let ``UDP-08 parseConnectResponse returns ParseError for txId mismatch`` () =
    let buf = makeConnectResponse 0 42 0L
    Assert.True(Result.isError (parseConnectResponse 99 buf))

[<Fact>]
let ``UDP-09 parseConnectResponse returns TrackerFailure for action=2`` () =
    let msg = System.Text.Encoding.ASCII.GetBytes("bad torrent")
    let buf = Array.concat [ [| 0uy; 0uy; 0uy; 2uy; 0uy; 0uy; 0uy; 42uy |]; msg ]

    match parseConnectResponse 42 buf with
    | Error(TrackerFailure _) -> ()
    | other -> Assert.Fail $"Expected TrackerFailure, got %A{other}"

// announce request

[<Fact>]
let ``UDP-10 buildAnnounceRequest is 98 bytes`` () =
    Assert.Equal(98, buildAnnounceRequest 1L 1 1 defaultReq |> Array.length)

[<Fact>]
let ``UDP-11 buildAnnounceRequest event None encodes as 0 at offset 80`` () =
    let buf =
        buildAnnounceRequest
            1L
            1
            1
            { defaultReq with
                Event = AnnounceEvent.None }

    Assert.Equal(0, readI32 buf 80)

[<Fact>]
let ``UDP-12 buildAnnounceRequest event Started encodes as 2 at offset 80`` () =
    let buf = buildAnnounceRequest 1L 1 1 { defaultReq with Event = Started }
    Assert.Equal(2, readI32 buf 80)

[<Fact>]
let ``UDP-13 buildAnnounceRequest event Completed encodes as 1 at offset 80`` () =
    let buf = buildAnnounceRequest 1L 1 1 { defaultReq with Event = Completed }
    Assert.Equal(1, readI32 buf 80)

[<Fact>]
let ``UDP-14 buildAnnounceRequest event Stopped encodes as 3 at offset 80`` () =
    let buf = buildAnnounceRequest 1L 1 1 { defaultReq with Event = Stopped }
    Assert.Equal(3, readI32 buf 80)

[<Fact>]
let ``UDP-15 buildAnnounceRequest info_hash at bytes 16-35`` () =
    let req =
        { defaultReq with
            InfoHash = Array.init 20 byte }

    let buf = buildAnnounceRequest 1L 1 1 req
    Assert.Equal<byte[]>(req.InfoHash, buf.[16..35])

[<Fact>]
let ``UDP-16 buildAnnounceRequest ip field is 0 at bytes 84-87`` () =
    let buf = buildAnnounceRequest 1L 1 1 defaultReq
    Assert.Equal(0, readI32 buf 84)

// announce response

let private makeAnnounceResponse (txId: int) interval leechers seeders (peerBytes: byte[]) =
    let header = Array.zeroCreate 20
    BinaryPrimitives.WriteInt32BigEndian(System.Span(header, 0, 4), 1) // action
    BinaryPrimitives.WriteInt32BigEndian(System.Span(header, 4, 4), txId)
    BinaryPrimitives.WriteInt32BigEndian(System.Span(header, 8, 4), interval)
    BinaryPrimitives.WriteInt32BigEndian(System.Span(header, 12, 4), leechers)
    BinaryPrimitives.WriteInt32BigEndian(System.Span(header, 16, 4), seeders)
    Array.concat [ header; peerBytes ]

[<Fact>]
let ``UDP-17 parseAnnounceResponse valid response with no peers`` () =
    let buf = makeAnnounceResponse 7 300 10 5 [||]

    match parseAnnounceResponse 7 buf with
    | Ok r ->
        Assert.Equal(300, r.Interval)
        Assert.Equal(5, r.Seeders)
        Assert.Equal(10, r.Leechers)
        Assert.Equal<PeerInfo list>([], r.Peers)
    | Error e -> Assert.Fail $"%A{e}"

[<Fact>]
let ``UDP-18 parseAnnounceResponse parses compact peers`` () =
    let peerBytes =
        [| 0xC0uy
           0xA8uy
           0x01uy
           0x05uy
           0x1Auy
           0xE1uy // 192.168.1.5:6881
           0x0Auy
           0x00uy
           0x00uy
           0x01uy
           0x1Fuy
           0x90uy |] // 10.0.0.1:8080

    let buf = makeAnnounceResponse 7 60 0 0 peerBytes

    match parseAnnounceResponse 7 buf with
    | Ok r ->
        let expected =
            [ { IP = IPAddress.Parse("192.168.1.5")
                Port = 6881us }
              { IP = IPAddress.Parse("10.0.0.1")
                Port = 8080us } ]

        Assert.Equal<PeerInfo list>(expected, r.Peers)
    | Error e -> Assert.Fail $"%A{e}"

[<Fact>]
let ``UDP-19 parseAnnounceResponse returns ParseError when too short`` () =
    Assert.True(Result.isError (parseAnnounceResponse 1 (Array.zeroCreate 19)))

[<Fact>]
let ``UDP-20 parseAnnounceResponse returns ParseError for wrong action`` () =
    let buf = makeAnnounceResponse 7 300 0 0 [||]
    BinaryPrimitives.WriteInt32BigEndian(System.Span(buf, 0, 4), 0)
    Assert.True(Result.isError (parseAnnounceResponse 7 buf))

[<Fact>]
let ``UDP-21 parseAnnounceResponse returns ParseError for txId mismatch`` () =
    let buf = makeAnnounceResponse 7 300 0 0 [||]
    Assert.True(Result.isError (parseAnnounceResponse 99 buf))

[<Fact>]
let ``UDP-22 parseAnnounceResponse returns TrackerFailure for action=2`` () =
    let msg = System.Text.Encoding.ASCII.GetBytes("invalid info_hash")
    let buf = Array.concat [ [| 0uy; 0uy; 0uy; 2uy; 0uy; 0uy; 0uy; 7uy |]; msg ]

    match parseAnnounceResponse 7 buf with
    | Error(TrackerFailure _) -> ()
    | other -> Assert.Fail $"Expected TrackerFailure, got %A{other}"
