module Downpour.Engine.PieceStore

open System.IO
open System.Security.Cryptography
open Downpour.Torrent.Types

type FileSegment =
    { FilePath: string
      FileOffset: int64
      Length: int }

// bitfield helpers
let makeBitfield (pieceCount: int) : byte[] = Array.zeroCreate ((pieceCount + 7) / 8)

let getBit (bitfield: byte[]) (index: int) =
    let bit = 7 - (index % 8)
    (bitfield[index / 8] >>> bit) &&& 1uy = 1uy

let setBit (bitfield: byte[]) (index: int) =
    let bit = 7 - (index % 8)
    bitfield[index / 8] <- bitfield[index / 8] ||| (1uy <<< bit)

let private clearBit (bitfield: byte[]) (index: int) =
    let bit = 7 - (index % 8)
    bitfield[index / 8] <- bitfield[index / 8] &&& ~~~(1uy <<< bit)

let countSet (bitfield: byte[]) =
    bitfield
    |> Array.sumBy (fun b -> System.Numerics.BitOperations.PopCount(uint b))

let isComplete (bitfield: byte[]) (pieceCount: int) = countSet bitfield = pieceCount

let private resolveRange (layout: FileLayout) (saveBase: string) (virtualStart: int64) (len: int) : FileSegment list =
    match layout with
    | SingleFile _ ->
        [ { FilePath = saveBase
            FileOffset = virtualStart
            Length = len } ]
    | MultiFile files ->
        let virtualEnd = virtualStart + int64 len
        let mutable fileVStart = 0L

        [ for file in files do
              let fileVEnd = fileVStart + file.Length

              if fileVEnd > virtualStart && fileVStart < virtualEnd then
                  let overlapStart = max fileVStart virtualStart
                  let overlapEnd = min fileVEnd virtualEnd
                  let path = Path.Combine([| saveBase; yield! file.Path |])

                  yield
                      { FilePath = path
                        FileOffset = overlapStart - fileVStart
                        Length = int (overlapEnd - overlapStart) }

              fileVStart <- fileVStart + file.Length ]

let resolveSegments
    (layout: FileLayout)
    (saveBase: string)
    (pieceSize: int64)
    (pieceIndex: int)
    (pieceLength: int)
    : FileSegment list =
    let virtualStart = int64 pieceIndex * pieceSize
    resolveRange layout saveBase virtualStart pieceLength

let actualPieceLength (meta: TorrentMetaInfo) (pieceIndex: int) : int =
    let totalSize =
        match meta.Info.FileLayout with
        | SingleFile(len, _) -> len
        | MultiFile files -> files |> List.sumBy _.Length

    int (min meta.Info.PieceLength (totalSize - int64 pieceIndex * meta.Info.PieceLength))

let writeBlock (layout: FileLayout) (saveBase: string) (pieceSize: int64) (pieceIndex: int) (blockOffset: int) (data: byte[]) : Async<unit> =
    async {
        let virtualStart = int64 pieceIndex * pieceSize + int64 blockOffset
        let segments = resolveRange layout saveBase virtualStart data.Length
        let mutable pos = 0

        for seg in segments do
            Directory.CreateDirectory(Path.GetDirectoryName(seg.FilePath)) |> ignore
            use fs = new FileStream(seg.FilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite)
            fs.Seek(seg.FileOffset, SeekOrigin.Begin) |> ignore
            do! fs.WriteAsync(data, pos, seg.Length) |> Async.AwaitTask
            pos <- pos + seg.Length
    }

let readBlock (layout: FileLayout) (saveBase: string) (pieceSize: int64) (pieceIndex: int) (blockOffset: int) (blockLength: int) : Async<byte[]> =
    async {
        let virtualStart = int64 pieceIndex * pieceSize + int64 blockOffset
        let segments = resolveRange layout saveBase virtualStart blockLength
        let result = Array.zeroCreate blockLength
        let mutable pos = 0

        for seg in segments do
            use fs = new FileStream(seg.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            fs.Seek(seg.FileOffset, SeekOrigin.Begin) |> ignore
            let! _ = fs.ReadAsync(result, pos, seg.Length) |> Async.AwaitTask
            pos <- pos + seg.Length

        return result
    }

let verifyPiece (layout: FileLayout) (saveBase: string) (pieceSize: int64) (pieceIndex: int) (pieceLength: int) (Sha1Hash expected) : Async<bool> =
    async {
        let! data = readBlock layout saveBase pieceSize pieceIndex 0 pieceLength
        use sha1 = SHA1.Create()
        return sha1.ComputeHash(data) = expected
    }

let verifyExistingPieces (meta: TorrentMetaInfo) (saveBase: string) (storedBitfield: byte[]) : Async<byte[]> =
    async {
        let result = Array.copy storedBitfield

        for i in 0 .. meta.Info.Pieces.Length - 1 do
            if getBit result i then
                let pieceLen = actualPieceLength meta i
                let! ok = verifyPiece meta.Info.FileLayout saveBase meta.Info.PieceLength i pieceLen meta.Info.Pieces[i]

                if not ok then
                    clearBit result i

        return result
    }
