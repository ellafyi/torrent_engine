module Downpour.Torrent.Tests

open Xunit
open Downpour.Torrent.Parser
open Downpour.Torrent.Types
open System.IO

[<Theory>]
[<InlineData("TestData/debian-13.4.0-amd64-netinst.iso.torrent",
             "http://bttracker.debian.org:6969/announce",
             "debian-13.4.0-amd64-netinst.iso")>]
[<InlineData("TestData/ubuntu-25.10-desktop-amd64.iso.torrent",
             "https://torrent.ubuntu.com/announce",
             "ubuntu-25.10-desktop-amd64.iso")>]
[<InlineData("TestData/cachyos-desktop-linux-260308.torrent",
             "udp://fosstorrents.com:6969/announce",
             "cachyos-desktop-linux-260308.iso")>]
let ``Parse real torrent files`` (filePath, expectedAnnounce, expectedName) =
    let bytes = File.ReadAllBytes(filePath)
    let meta = parse bytes

    Assert.Equal(expectedAnnounce, meta.Announce)
    Assert.Equal(expectedName, meta.Info.Name)
    Assert.True(meta.Info.PieceLength > 0L)
    Assert.NotEmpty(meta.Info.Pieces)

[<Fact>]
let ``Parse succeeds and produces at least one piece`` () =
    let bytes = File.ReadAllBytes("TestData/debian-13.4.0-amd64-netinst.iso.torrent")
    let meta = parse bytes
    Assert.True(meta.Info.Pieces.Length > 0)

[<Fact>]
let ``InfoHash matches known value for debian torrent`` () =
    let bytes = File.ReadAllBytes("TestData/debian-13.4.0-amd64-netinst.iso.torrent")
    let meta = parse bytes
    Assert.Equal("80e68d5bfb383aef4f8eb947404a6a82f7af2f07", meta.InfoHash.Hex)

[<Fact>]
let ``Can handle announce-list if present`` () =
    let bytes = File.ReadAllBytes("TestData/ubuntu-25.10-desktop-amd64.iso.torrent")
    let meta = parse bytes

    match meta.AnnounceList with
    | Some list ->
        Assert.NotEmpty(list)
        Assert.All(list, (fun tier -> Assert.NotEmpty(tier)))
    | None -> () // announce-list is optional
