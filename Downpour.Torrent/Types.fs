module Downpour.Torrent.Types

open System

type Sha1Hash = Sha1Hash of byte[]
type InfoHash = InfoHash of byte[]
    with
    member this.Hex =
        let (InfoHash bytes ) = this
        bytes
        |> Array.map (sprintf "%02x")
        |> String.concat ""
        
type TorrentFile = {
    Length: int64
    Path: string list
    Md5Sum: string option
}

type FileLayout =
    | SingleFile of length: int64 * md5sum: string option
    | MultiFile of files: TorrentFile list
    
type InfoDict = {
    Name: string
    PieceLength: int64
    Pieces: Sha1Hash list
    FileLayout: FileLayout
    Private: bool
}

type TorrentMetaInfo = {
    InfoHash: InfoHash
    Info: InfoDict
    Announce: string
    AnnounceList: string list list option
    Comment: string option
    CreatedBy: string option
    CreationDate: DateTimeOffset option
}