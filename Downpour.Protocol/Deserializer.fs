module Downpour.Protocol.Deserializer

open System

let private int32BE (span: ReadOnlySpan<byte>) : int =
    (int span.[0] <<< 24)
    ||| (int span.[1] <<< 16)
    ||| (int span.[2] <<< 8)
    ||| int span.[3]

let private uint16BE (span: ReadOnlySpan<byte>) : uint16 =
    (uint16 span.[0] <<< 8) ||| uint16 span.[1]

let parse (mem: ReadOnlyMemory<byte>) : Result<PeerMessage * ReadOnlyMemory<byte>, string> =
    if mem.Length < 4 then
        Error "Insufficient bytes for length prefix"
    else
        let length = int32BE (mem.Slice(0, 4).Span)

        if length = 0 then
            Ok(KeepAlive, mem.Slice(4))
        elif mem.Length < 4 + length then
            Error $"Expected {length} bytes, have {mem.Length - 4}"
        else
            let id = mem.Span.[4]
            let payload = mem.Slice(5, length - 1)
            let rest = mem.Slice(4 + length)

            match id, length with
            | 0uy, 1 -> Ok(Choke, rest)
            | 1uy, 1 -> Ok(Unchoke, rest)
            | 2uy, 1 -> Ok(Interested, rest)
            | 3uy, 1 -> Ok(NotInterested, rest)
            | 4uy, 5 -> Ok(Have(int32BE (payload.Slice(0, 4).Span)), rest)
            | 5uy, _ when length >= 1 -> Ok(Bitfield(payload.ToArray()), rest)
            | 6uy, 13 ->
                Ok(
                    Request(
                        int32BE (payload.Slice(0, 4).Span),
                        int32BE (payload.Slice(4, 4).Span),
                        int32BE (payload.Slice(8, 4).Span)
                    ),
                    rest
                )
            | 7uy, _ when length >= 9 ->
                Ok(
                    Piece(
                        int32BE (payload.Slice(0, 4).Span),
                        int32BE (payload.Slice(4, 4).Span),
                        payload.Slice(8).ToArray()
                    ),
                    rest
                )
            | 8uy, 13 ->
                Ok(
                    Cancel(
                        int32BE (payload.Slice(0, 4).Span),
                        int32BE (payload.Slice(4, 4).Span),
                        int32BE (payload.Slice(8, 4).Span)
                    ),
                    rest
                )
            | 9uy, 3 -> Ok(Port(uint16BE (payload.Slice(0, 2).Span)), rest)
            | unknownId, _ -> Error $"Unknown message ID: {unknownId}"

let deserialize (data: byte[]) : Result<PeerMessage, string> =
    match parse (ReadOnlyMemory<byte>(data)) with
    | Ok(msg, remaining) when remaining.Length = 0 -> Ok msg
    | Ok(_, remaining) -> Error $"Incomplete parse: {remaining.Length} bytes remain"
    | Error e -> Error e
