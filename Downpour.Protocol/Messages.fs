namespace Downpour.Protocol

type PeerId = PeerId of byte[] // 20 bytes

type HandshakeMessage =
    { ReservedBytes: byte[]
      InfoHash: byte[]
      PeerId: byte[] }


type PeerMessage =
    | KeepAlive
    | Choke
    | Unchoke
    | Interested
    | NotInterested
    | Have of pieceIndex: int
    | Bitfield of bits: byte[]
    | Request of pieceIndex: int * blockOffset: int * blockLength: int
    | Piece of pieceIndex: int * blockOffset: int * data: byte[]
    | Cancel of pieceIndex: int * blockOffset: int * blockLength: int
    | Port of listenPort: uint16


type PeerState =
    { AmChoking: bool
      AmInterested: bool
      PeerChoking: bool
      PeerInterested: bool
      PeerBitfield: byte[] }


module PeerState =
    let initial =
        { AmChoking = true
          AmInterested = false
          PeerChoking = true
          PeerInterested = false
          PeerBitfield = [||] }
