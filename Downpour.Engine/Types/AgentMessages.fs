namespace Downpour.Engine.Types

open Downpour.Protocol
open Downpour.Engine

//TO a PeerAgent by TorrentAgent
type PeerCommand =
    | SendMsg of PeerMessage
    | Disconnect

// FROM PeerAgent TO TorrentAgent
type PeerEvent =
    | BlockReceived of pieceIndex: int * blockOffset: int * data: byte[]
    | PeerChoked
    | PeerUnchoked
    | PeerHasPiece of pieceIndex: int
    | PeerBitfieldReceived of bits: byte[]
    | PeerInterestedChanged of interested: bool
    | InboundRequest of pieceIndex: int * blockOffset: int * blockLength: int
    | Disconnected of reason: string

// sent TO a TorrentAgent
type TorrentCommand =
    | Start
    | Pause
    | Resume
    | ConnectToPeer of Downpour.Tracker.PeerInfo
    | InboundPeer of client: System.Net.Sockets.TcpClient * peerId: byte[]
    | PeerReady of (unit -> unit) // registers a connected peer and starts its read loop
    | FromPeer of peerId: byte[] * PeerEvent
    | TrackerTick // check if re-announce is due
    | ProgressTick // compute and emit speed and progress
    | TrackerResult of Result<Downpour.Tracker.AnnounceResponse, Downpour.Tracker.TrackerError>
    | SettingsUpdated of EngineSettings
    | GetProgress of AsyncReplyChannel<TorrentProgress>
    | PieceVerified of pieceIndex: int * passed: bool

// sent TO the EngineAgent
type EngineCommand =
    | AddTorrent of bytes: byte[] * savePath: string * reply: AsyncReplyChannel<Result<int, string>>
    | RemoveTorrent of torrentId: int * deleteFiles: bool * reply: AsyncReplyChannel<unit>
    | PauseTorrent of torrentId: int * reply: AsyncReplyChannel<unit>
    | ResumeTorrent of torrentId: int * reply: AsyncReplyChannel<unit>
    | IncomingConn of client: System.Net.Sockets.TcpClient
    | TorrentEvent of torrentId: int * EngineEvent
    | UpdateSettings of EngineSettings * reply: AsyncReplyChannel<unit>
    | GetAllProgress of AsyncReplyChannel<TorrentProgress list>
    | Stop of AsyncReplyChannel<unit>
