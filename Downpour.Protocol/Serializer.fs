module Downpour.Protocol.Serializer

let private int32BE (n: int) : byte[] =
    [| byte (n >>> 24); byte (n >>> 16); byte (n >>> 8); byte n |]

let private uint16BE (n: uint16) : byte[] = [| byte (n >>> 8); byte n |]

let private frame (id: byte) (payload: byte[]) : byte[] =
    Array.concat [ int32BE (1 + payload.Length); [| id |]; payload ]

let serialize (message: PeerMessage) : byte[] =
    match message with
    | KeepAlive -> int32BE 0
    | Choke -> frame 0uy [||]
    | Unchoke -> frame 1uy [||]
    | Interested -> frame 2uy [||]
    | NotInterested -> frame 3uy [||]
    | Have pieceIndex -> frame 4uy (int32BE pieceIndex)
    | Bitfield bits -> frame 5uy bits
    | Request(pi, bo, blockLength) -> frame 6uy (Array.concat [ int32BE pi; int32BE bo; int32BE blockLength ])
    | Piece(pi, bo, bytes) -> frame 7uy (Array.concat [ int32BE pi; int32BE bo; bytes ])
    | Cancel(pi, bo, blockLength) -> frame 8uy (Array.concat [ int32BE pi; int32BE bo; int32BE blockLength ])
    | Port listenPort -> frame 9uy (uint16BE listenPort)
