module Downpour.Protocol.Tests.HandshakeTests

open Xunit
open Downpour.Protocol

let private sampleInfoHash = Array.init 20 byte
let private samplePeerId = Array.init 20 (fun i -> byte (i + 20))

[<Fact>]
let ``HS-01 Round-trip preserves InfoHash and PeerId`` () =
    let bytes = Handshake.serialize sampleInfoHash samplePeerId

    match Handshake.deserialize bytes with
    | Ok msg ->
        Assert.Equal<byte[]>(sampleInfoHash, msg.InfoHash)
        Assert.Equal<byte[]>(samplePeerId, msg.PeerId)
    | Error e -> Assert.Fail(e)

[<Fact>]
let ``HS-02 Serialized handshake is exactly 68 bytes`` () =
    let bytes = Handshake.serialize sampleInfoHash samplePeerId
    Assert.Equal(68, bytes.Length)

[<Fact>]
let ``HS-03 Reserved bytes are all zero`` () =
    let bytes = Handshake.serialize sampleInfoHash samplePeerId

    match Handshake.deserialize bytes with
    | Ok msg -> Assert.Equal<byte[]>(Array.zeroCreate 8, msg.ReservedBytes)
    | Error e -> Assert.Fail(e)

[<Fact>]
let ``HS-04 Wrong total length returns error`` () =
    Assert.True(Result.isError (Handshake.deserialize (Array.zeroCreate 67)))
    Assert.True(Result.isError (Handshake.deserialize (Array.zeroCreate 69)))
    Assert.True(Result.isError (Handshake.deserialize [||]))

[<Fact>]
let ``HS-05 Wrong pstrlen byte returns error`` () =
    let bytes = Handshake.serialize sampleInfoHash samplePeerId |> Array.copy
    bytes[0] <- 18uy
    Assert.True(Result.isError (Handshake.deserialize bytes))

[<Fact>]
let ``HS-06 Corrupted protocol string returns error`` () =
    let bytes = Handshake.serialize sampleInfoHash samplePeerId |> Array.copy
    bytes[1] <- byte 'X'
    Assert.True(Result.isError (Handshake.deserialize bytes))
