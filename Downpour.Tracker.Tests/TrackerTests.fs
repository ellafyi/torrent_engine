module Downpour.Tracker.Tests.TrackerTests

open Xunit
open Downpour.Tracker
open Downpour.Tracker.Tracker

let private defaultReq: AnnounceRequest =
    { InfoHash = Array.create 20 0x00uy
      PeerId = Array.create 20 0x00uy
      Port = 6881us
      Uploaded = 0L
      Downloaded = 0L
      Left = 0L
      Event = AnnounceEvent.None
      TrackerId = Option.None }

[<Fact>]
let ``TRK-01 unsupported scheme returns ParseError`` () =
    let result =
        announce "ftp://tracker.example.com:1234/announce" defaultReq
        |> Async.RunSynchronously

    match result with
    | Error(ParseError msg) -> Assert.Contains("ftp", msg)
    | other -> Assert.Fail $"Expected ParseError, got %A{other}"
