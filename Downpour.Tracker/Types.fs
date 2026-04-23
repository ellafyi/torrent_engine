namespace Downpour.Tracker

open System.Net

type AnnounceEvent =
    | None
    | Started
    | Completed
    | Stopped

type AnnounceRequest =
    { InfoHash: byte[]
      PeerId: byte[]
      Port: uint16
      Uploaded: int64
      Downloaded: int64
      Left: int64
      Event: AnnounceEvent
      TrackerId: string option }

type PeerInfo = { IP: IPAddress; Port: uint16 }

type AnnounceResponse =
    { Interval: int
      MinInterval: int option
      TrackerId: string option
      Seeders: int
      Leechers: int
      Peers: PeerInfo list }

type TrackerError = 
    | TrackerFailure of reason: string
    | NetworkError of message: string
    | ParseError of message: string
    | Timeout
