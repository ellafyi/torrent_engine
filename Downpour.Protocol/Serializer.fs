module Downpour.Protocol.Serializer

let serialize (message: PeerMessage) : byte[] =
    match message with
    | KeepAlive -> [||]
    | Choke -> [||]
    | Unchoke -> [||]
    | Interested -> [||]
    | NotInterested -> [||]
    | Have v -> [||]
    | Bitfield bf -> [||]
    | Request(pieceIndex, blockOffset, blockLength) -> [||]
    | Piece(pieceIndex, blockOffset, bytes) -> [||]
    | Cancel(pieceIndex, blockOffset, blockLength) -> [||]
    | Port listenPort -> [||]
