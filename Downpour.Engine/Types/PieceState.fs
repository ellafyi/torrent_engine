namespace Downpour.Engine.Types

type BlockStatus =
    | Missing
    | InFlight of peerId: byte[] * since: System.DateTime
    | Received

type PieceState =
    { Index: int
      ExpectedHash: byte[] // 20-byte SHA-1 from TorrentMetaInfo.Info.Pieces
      BlockCount: int
      Blocks: BlockStatus[]
      mutable Data: byte[] } // accumulates received block data; length = piece size
