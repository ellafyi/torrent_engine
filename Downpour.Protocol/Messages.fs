namespace Downpour.Protocol

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
