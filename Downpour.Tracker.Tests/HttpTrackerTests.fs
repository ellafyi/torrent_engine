module Downpour.Tracker.Tests.HttpTrackerTests

open System.Net
open Xunit
open Downpour.Tracker
open Downpour.Tracker.HttpTracker

let private defaultReq: AnnounceRequest =
    { InfoHash = Array.create 20 0xAAuy
      PeerId = Array.create 20 0xBBuy
      Port = 6881us
      Uploaded = 0L
      Downloaded = 0L
      Left = 1000L
      Event = AnnounceEvent.None
      TrackerId = Option.None }

// build url

[<Fact>]
let ``HTTP-01 buildUrl includes event=started`` () =
    let url =
        buildUrl "http://tracker.example.com/announce" { defaultReq with Event = Started }

    Assert.Contains("event=started", url)

[<Fact>]
let ``HTTP-02 buildUrl includes event=completed`` () =
    let url =
        buildUrl "http://tracker.example.com/announce" { defaultReq with Event = Completed }

    Assert.Contains("event=completed", url)

[<Fact>]
let ``HTTP-03 buildUrl includes event=stopped`` () =
    let url =
        buildUrl "http://tracker.example.com/announce" { defaultReq with Event = Stopped }

    Assert.Contains("event=stopped", url)

[<Fact>]
let ``HTTP-04 buildUrl omits event when None`` () =
    let url = buildUrl "http://tracker.example.com/announce" defaultReq
    Assert.DoesNotContain("event=", url)

[<Fact>]
let ``HTTP-05 buildUrl includes trackerid when provided`` () =
    let url =
        buildUrl
            "http://tracker.example.com/announce"
            { defaultReq with
                TrackerId = Some "abc123" }

    Assert.Contains("trackerid=abc123", url)

[<Fact>]
let ``HTTP-06 buildUrl omits trackerid when None`` () =
    let url = buildUrl "http://tracker.example.com/announce" defaultReq
    Assert.DoesNotContain("trackerid", url)

[<Fact>]
let ``HTTP-07 buildUrl percent-encodes info_hash`` () =
    let req =
        { defaultReq with
            InfoHash = Array.init 20 byte }

    let url = buildUrl "http://tracker.example.com/announce" req
    Assert.Contains("info_hash=%00%01%02%03", url)

[<Fact>]
let ``HTTP-08 buildUrl always includes compact=1`` () =
    let url = buildUrl "http://tracker.example.com/announce" defaultReq
    Assert.Contains("compact=1", url)

// compact peer parsing

[<Fact>]
let ``HTTP-09 parseCompactPeers single peer`` () =
    let bytes = [| 0xC0uy; 0xA8uy; 0x01uy; 0x05uy; 0x1Auy; 0xE1uy |]
    let result = parseCompactPeers bytes

    Assert.Equal(
        Ok
            [ { IP = IPAddress.Parse("192.168.1.5")
                Port = 6881us } ],
        result
    )

[<Fact>]
let ``HTTP-10 parseCompactPeers two peers`` () =
    let bytes =
        [| 0xC0uy
           0xA8uy
           0x01uy
           0x05uy
           0x1Auy
           0xE1uy
           0x0Auy
           0x00uy
           0x00uy
           0x01uy
           0x1Fuy
           0x90uy |]

    let result = parseCompactPeers bytes

    let expected =
        Ok
            [ { IP = IPAddress.Parse("192.168.1.5")
                Port = 6881us }
              { IP = IPAddress.Parse("10.0.0.1")
                Port = 8080us } ]

    Assert.Equal(expected, result)

[<Fact>]
let ``HTTP-11 parseCompactPeers empty bytes returns empty list`` () =
    Assert.Equal(Ok [], parseCompactPeers [||])

[<Fact>]
let ``HTTP-12 parseCompactPeers non-multiple-of-6 returns ParseError`` () =
    Assert.True(Result.isError (parseCompactPeers [| 0uy; 1uy; 2uy; 3uy; 4uy; 5uy; 6uy |]))

// parsing response

// Builds a minimal valid bencoded announce response:
// d8:completei<s>e10:incompletei<l>e8:intervali<i>e5:peers0:e
let private basicResponse interval seeders leechers =
    let enc (s: string) = System.Text.Encoding.ASCII.GetBytes s

    Array.concat
        [ enc "d8:complete"
          enc $"i{seeders}e"
          enc "10:incomplete"
          enc $"i{leechers}e"
          enc "8:interval"
          enc $"i{interval}e"
          enc "5:peers0:e" ]

[<Fact>]
let ``HTTP-13 parseResponse extracts interval, seeders, leechers`` () =
    let bytes = basicResponse 300 5 10

    match parseResponse bytes with
    | Ok r ->
        Assert.Equal(300, r.Interval)
        Assert.Equal(5, r.Seeders)
        Assert.Equal(10, r.Leechers)
        Assert.Equal<PeerInfo list>([], r.Peers)
    | Error e -> Assert.Fail(sprintf "%A" e)

[<Fact>]
let ``HTTP-14 parseResponse returns TrackerFailure when failure reason present`` () =
    let bytes = "d14:failure reason22:torrent not registerede"B

    match parseResponse bytes with
    | Error(TrackerFailure msg) -> Assert.Equal("torrent not registered", msg)
    | other -> Assert.Fail $"Expected TrackerFailure, got %A{other}"

[<Fact>]
let ``HTTP-15 parseResponse returns ParseError when interval missing`` () =
    let bytes = "d8:completei5e10:incompletei10e5:peers0:e"B
    Assert.True(Result.isError (parseResponse bytes))

[<Fact>]
let ``HTTP-16 parseResponse parses compact peers`` () =
    let peerBytes = [| 0xC0uy; 0xA8uy; 0x01uy; 0x05uy; 0x1Auy; 0xE1uy |]

    let bytes =
        Array.concat [ "d8:completei1e10:incompletei2e8:intervali60e5:peers6:"B; peerBytes; "e"B ]

    match parseResponse bytes with
    | Ok r ->
        Assert.Equal<PeerInfo list>(
            [ { IP = IPAddress.Parse("192.168.1.5")
                Port = 6881us } ],
            r.Peers
        )
    | Error e -> Assert.Fail $"%A{e}"

[<Fact>]
let ``HTTP-17 parseResponse extracts min interval when present`` () =
    let bytes =
        "d8:completei0e10:incompletei0e8:intervali300e12:min intervali60e5:peers0:e"B

    match parseResponse bytes with
    | Ok r -> Assert.Equal(Some 60, r.MinInterval)
    | Error e -> Assert.Fail $"%A{e}"

[<Fact>]
let ``HTTP-18 parseResponse extracts tracker id when present`` () =
    let bytes =
        "d8:completei0e10:incompletei0e8:intervali300e5:peers0:10:tracker id6:abcdefe"B

    match parseResponse bytes with
    | Ok r -> Assert.Equal(Some "abcdef", r.TrackerId)
    | Error e -> Assert.Fail $"%A{e}"

[<Fact>]
let ``HTTP-19 parseResponse returns ParseError for non-dictionary input`` () =
    Assert.True(Result.isError (parseResponse "i42e"B))
