module Downpour.Torrent.Parser

open System
open System.Security.Cryptography
open Downpour.Bencode.Types
open Downpour.Torrent.Types

let private toKey (s: string) = Text.Encoding.UTF8.GetBytes(s)

let private asString =
    function
    | BencodeValue.String b -> Text.Encoding.UTF8.GetString(b)
    | v -> failwith $"Expected string, got {v}"

let private asInt =
    function
    | BencodeValue.Integer i -> i
    | v -> failwith $"Expected integer, got {v}"

let private dictGet (key: string) =
    function
    | BencodeValue.Dictionary m -> Map.tryFind (toKey key) m
    | v -> failwith $"Expected dictionary, got {v}"

let private dictGetReq key dict =
    dictGet key dict
    |> Option.defaultWith (fun () -> failwith $"Missing required key: {key}")


let hash (pcsBytes: byte[]) : Sha1Hash list =
    if pcsBytes.Length % 20 <> 0 then
        failwith $"Expected byte length divisible by 20"

    pcsBytes |> Array.chunkBySize 20 |> Array.map Sha1Hash |> Array.toList

// pieces
let private parsePieces (infoDict: BencodeValue) : Sha1Hash list =
    dictGetReq "pieces" infoDict
    |> (function
    | BencodeValue.String b -> b
    | v -> failwith $"Expected string for pieces, got {v}")
    |> hash

/// file handling
let private parseSingleFile (infoDict: BencodeValue) : FileLayout =
    let length = dictGetReq "length" infoDict |> asInt
    let md5sum = dictGet "md5sum" infoDict |> Option.map asString
    SingleFile(length, md5sum)

let private parseMultiFile (infoDict: BencodeValue) : FileLayout =
    let files =
        match dictGetReq "files" infoDict with
        | BencodeValue.List entries ->
            entries
            |> List.map (fun entry ->
                let length = dictGetReq "length" entry |> asInt

                let path =
                    match dictGetReq "path" entry with
                    | BencodeValue.List segments -> segments |> List.map asString
                    | v -> failwith $"Expected list for path, got {v}"

                let md5sum = dictGet "md5sum" entry |> Option.map asString

                { Length = length
                  Path = path
                  Md5Sum = md5sum })
        | v -> failwith $"Expected list for files, got {v}"

    MultiFile files

let private parseFileLayout (infoDict: BencodeValue) : FileLayout =
    match dictGet "length" infoDict with
    | Some _ -> parseSingleFile infoDict
    | None -> parseMultiFile infoDict

/// info
let private parseInfo (infoDict: BencodeValue) : InfoDict =
    { Name = dictGetReq "name" infoDict |> asString
      PieceLength = dictGetReq "piece length" infoDict |> asInt
      Pieces = parsePieces infoDict
      FileLayout = parseFileLayout infoDict
      Private =
        dictGet "private" infoDict
        |> Option.map asInt
        |> Option.map (fun i -> i = 1L)
        |> Option.defaultValue false }

// info hash: raw bytes from the original file, not re-encoded bytes
let private computeInfoHash (rawBytes: byte[]) : InfoHash =
    match Downpour.Bencode.Decoder.findDictValueBytes "info" rawBytes with
    | Some infoBytes -> SHA1.HashData(infoBytes) |> InfoHash
    | None -> failwith "Missing info dictionary"

// announce list
let private parseAnnounceList (value: BencodeValue option) : string list list option =
    value
    |> Option.map (function
        | BencodeValue.List tiers ->
            tiers
            |> List.map (function
                | BencodeValue.List urls -> urls |> List.map asString
                | v -> failwith $"Expected list for announce-list tier, got {v}")
        | v -> failwith $"Expected list for announce-list, got {v}")

let parse (bytes: byte[]) : TorrentMetaInfo =
    let root =
        match Downpour.Bencode.Decoder.decode bytes with
        | Ok v -> v
        | Error e -> failwith e

    let infoVal = dictGetReq "info" root

    { Announce = dictGetReq "announce" root |> asString
      AnnounceList = dictGet "announce-list" root |> parseAnnounceList
      Comment = dictGet "comment" root |> Option.map asString
      CreatedBy = dictGet "created by" root |> Option.map asString
      CreationDate =
        dictGet "creation date" root
        |> Option.map asInt
        |> Option.map DateTimeOffset.FromUnixTimeSeconds
      Info = parseInfo infoVal
      InfoHash = computeInfoHash bytes }
