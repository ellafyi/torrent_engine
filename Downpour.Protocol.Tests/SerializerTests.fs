module Downpour.Protocol.Tests.SerializerTests

open System
open Xunit
open Downpour.Protocol

[<Fact>]
let ``SER-01 KeepAlive is four zero bytes`` () =
    Assert.Equal<byte[]>([| 0uy; 0uy; 0uy; 0uy |], Serializer.serialize KeepAlive)

[<Fact>]
let ``SER-02 Choke`` () =
    Assert.Equal<byte[]>([| 0uy; 0uy; 0uy; 1uy; 0uy |], Serializer.serialize Choke)

[<Fact>]
let ``SER-03 Unchoke`` () =
    Assert.Equal<byte[]>([| 0uy; 0uy; 0uy; 1uy; 1uy |], Serializer.serialize Unchoke)

[<Fact>]
let ``SER-04 Interested`` () =
    Assert.Equal<byte[]>([| 0uy; 0uy; 0uy; 1uy; 2uy |], Serializer.serialize Interested)

[<Fact>]
let ``SER-05 NotInterested`` () =
    Assert.Equal<byte[]>([| 0uy; 0uy; 0uy; 1uy; 3uy |], Serializer.serialize NotInterested)

[<Fact>]
let ``SER-06 Have 0`` () =
    Assert.Equal<byte[]>([| 0uy; 0uy; 0uy; 5uy; 4uy; 0uy; 0uy; 0uy; 0uy |], Serializer.serialize (Have 0))

[<Fact>]
let ``SER-07 Have big-endian byte order`` () =
    // 0x01020304 -> bytes 1, 2, 3, 4
    Assert.Equal<byte[]>([| 0uy; 0uy; 0uy; 5uy; 4uy; 1uy; 2uy; 3uy; 4uy |], Serializer.serialize (Have 0x01020304))

[<Fact>]
let ``SER-08 Bitfield empty`` () =
    Assert.Equal<byte[]>([| 0uy; 0uy; 0uy; 1uy; 5uy |], Serializer.serialize (Bitfield [||]))

[<Fact>]
let ``SER-09 Bitfield two bytes`` () =
    Assert.Equal<byte[]>(
        [| 0uy; 0uy; 0uy; 3uy; 5uy; 0xFFuy; 0xAAuy |],
        Serializer.serialize (Bitfield [| 0xFFuy; 0xAAuy |])
    )

[<Fact>]
let ``SER-10 Request`` () =
    // length=13, id=6, index=0, begin=0, blockLength=16384 (0x4000)
    let expected =
        [| 0uy; 0uy; 0uy; 13uy; 6uy
           0uy; 0uy; 0uy; 0uy   // index
           0uy; 0uy; 0uy; 0uy   // begin
           0uy; 0uy; 0x40uy; 0uy |]  // blockLength

    Assert.Equal<byte[]>(expected, Serializer.serialize (Request(0, 0, 16384)))

[<Fact>]
let ``SER-11 Piece`` () =
    let data = [| 0xBEuy; 0xEFuy |]
    // length=11, id=7, index=1, begin=0, data
    let expected =
        [| 0uy; 0uy; 0uy; 11uy; 7uy
           0uy; 0uy; 0uy; 1uy   // index
           0uy; 0uy; 0uy; 0uy   // begin
           0xBEuy; 0xEFuy |]    // data

    Assert.Equal<byte[]>(expected, Serializer.serialize (Piece(1, 0, data)))

[<Fact>]
let ``SER-12 Cancel`` () =
    // same structure as Request but id=8
    let expected =
        [| 0uy; 0uy; 0uy; 13uy; 8uy
           0uy; 0uy; 0uy; 0uy   // index
           0uy; 0uy; 0uy; 0uy   // begin
           0uy; 0uy; 0x40uy; 0uy |]  // blockLength

    Assert.Equal<byte[]>(expected, Serializer.serialize (Cancel(0, 0, 16384)))

[<Fact>]
let ``SER-13 Port 6881`` () =
    // 6881 = 0x1AE1
    Assert.Equal<byte[]>([| 0uy; 0uy; 0uy; 3uy; 9uy; 0x1Auy; 0xE1uy |], Serializer.serialize (Port 6881us))

[<Fact>]
let ``SER-14 Port 0`` () =
    Assert.Equal<byte[]>([| 0uy; 0uy; 0uy; 3uy; 9uy; 0uy; 0uy |], Serializer.serialize (Port 0us))

[<Fact>]
let ``SER-15 Port max`` () =
    Assert.Equal<byte[]>([| 0uy; 0uy; 0uy; 3uy; 9uy; 0xFFuy; 0xFFuy |], Serializer.serialize (Port UInt16.MaxValue))
