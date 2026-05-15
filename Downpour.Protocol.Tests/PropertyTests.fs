module Downpour.Protocol.Tests.PropertyTests

open FsCheck.Xunit
open Downpour.Protocol

[<Property>]
let ``PROP-01 Have encodes piece index as big-endian int32`` (i: int) =
    let bytes = Serializer.serialize (Have i)

    bytes.Length = 9
    && bytes[4] = 4uy
    && bytes[5] = byte (i >>> 24)
    && bytes[6] = byte (i >>> 16)
    && bytes[7] = byte (i >>> 8)
    && bytes[8] = byte i

[<Property>]
let ``PROP-02 Port encodes all uint16 values correctly`` (p: uint16) =
    let bytes = Serializer.serialize (Port p)

    bytes.Length = 7
    && bytes[4] = 9uy
    && bytes[5] = byte (p >>> 8)
    && bytes[6] = byte p

[<Property>]
let ``PROP-03 Bitfield total length is 5 plus payload`` (bits: byte[]) =
    let bits = if bits = null then [||] else bits
    let bytes = Serializer.serialize (Bitfield bits)
    bytes.Length = 5 + bits.Length
