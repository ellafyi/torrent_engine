module Downpour.Torrent.Parser

open System
open System.Security.Cryptography
open Downpour.Bencode.Types
open Downpour.Torrent.Types

let private toKey (s: string) = Text.Encoding.UTF8.GetBytes(s)

let private asString =
    function
    | BencodeValue.String b -> Ok(Text.Encoding.UTF8.GetString(b))
    | v -> Error $"Expected string, got {v}"

let private asInt =
    function
    | BencodeValue.Integer i -> Ok i
    | v -> Error $"Expected integer, got {v}"

let private dictGet (key: string) =
    function
    | BencodeValue.Dictionary m -> Ok(Map.tryFind (toKey key) m)
    | v -> Error $"Expected dictionary, got {v}"

let private dictGetReq key dict =
    match dictGet key dict with
    | Ok(Some v) -> Ok v
    | Ok None -> Error $"Missing required key: {key}"
    | Error e -> Error e


type ResultBuilder() =
    member _.Return(x) = Ok x
    member _.ReturnFrom(x) = x
    member _.Bind(x, f) = Result.bind f x

let result = ResultBuilder()

let hash (pcsBytes: byte[]) : Result<Sha1Hash list, string> =
    if pcsBytes.Length % 20 <> 0 then
        Error $"Expected byte length divisible by 20, got {pcsBytes.Length}"
    else
        pcsBytes |> Array.chunkBySize 20 |> Array.map Sha1Hash |> Array.toList |> Ok

let private parsePieces (infoDict: BencodeValue) : Result<Sha1Hash list, string> =
    match dictGetReq "pieces" infoDict with
    | Ok(BencodeValue.String b) -> hash b
    | Ok v -> Error $"Expected string for pieces, got {v}"
    | Error e -> Error e

let private parseSingleFile (infoDict: BencodeValue) : Result<FileLayout, string> =
    result {
        let! lengthVal = dictGetReq "length" infoDict
        let! length = asInt lengthVal

        let! md5sum =
            match dictGet "md5sum" infoDict with
            | Ok(Some v) -> asString v |> Result.map Some
            | Ok None -> Ok None
            | Error e -> Error e

        return SingleFile(length, md5sum)
    }

let private parseMultiFile (infoDict: BencodeValue) : Result<FileLayout, string> =
    result {
        let! filesVal = dictGetReq "files" infoDict

        let! entries =
            match filesVal with
            | BencodeValue.List l -> Ok l
            | v -> Error $"Expected list for files, got {v}"

        let rec loop acc items =
            match items with
            | [] -> Ok(List.rev acc)
            | h :: t ->
                let res =
                    result {
                        let! lengthVal = dictGetReq "length" h
                        let! length = asInt lengthVal

                        let! pathVal = dictGetReq "path" h

                        let! segments =
                            match pathVal with
                            | BencodeValue.List s -> Ok s
                            | v -> Error $"Expected list for path, got {v}"

                        let rec loopSegments accS itemsS =
                            match itemsS with
                            | [] -> Ok(List.rev accS)
                            | hs :: ts ->
                                match asString hs with
                                | Ok s -> loopSegments (s :: accS) ts
                                | Error e -> Error e

                        let! path = loopSegments [] segments

                        let! md5sum =
                            match dictGet "md5sum" h with
                            | Ok(Some v) -> asString v |> Result.map Some
                            | Ok None -> Ok None
                            | Error e -> Error e

                        return
                            { Length = length
                              Path = path
                              Md5Sum = md5sum }
                    }

                match res with
                | Ok f -> loop (f :: acc) t
                | Error e -> Error e

        let! parsedFiles = loop [] entries
        return MultiFile parsedFiles
    }

let private parseFileLayout (infoDict: BencodeValue) : Result<FileLayout, string> =
    match dictGet "length" infoDict with
    | Ok(Some _) -> parseSingleFile infoDict
    | Ok None -> parseMultiFile infoDict
    | Error e -> Error e

let private parseInfo (infoDict: BencodeValue) : Result<InfoDict, string> =
    result {
        let! nameVal = dictGetReq "name" infoDict
        let! name = asString nameVal
        let! pieceLengthVal = dictGetReq "piece length" infoDict
        let! pieceLength = asInt pieceLengthVal
        let! pieces = parsePieces infoDict
        let! fileLayout = parseFileLayout infoDict

        let! priv =
            match dictGet "private" infoDict with
            | Ok(Some v) -> asInt v |> Result.map (fun i -> i = 1L) |> Result.map Some
            | Ok None -> Ok None
            | Error e -> Error e

        return
            { Name = name
              PieceLength = pieceLength
              Pieces = pieces
              FileLayout = fileLayout
              Private = priv |> Option.defaultValue false }
    }

let private computeInfoHash (rawBytes: byte[]) : Result<InfoHash, string> =
    match Downpour.Bencode.Decoder.findDictValueBytes "info" rawBytes with
    | Some infoBytes -> SHA1.HashData(infoBytes) |> InfoHash |> Ok
    | None -> Error "Missing info dictionary"

let private parseAnnounceList (value: BencodeValue option) : Result<string list list option, string> =
    match value with
    | None -> Ok None
    | Some(BencodeValue.List tiers) ->
        let rec loopTiers accT itemsT =
            match itemsT with
            | [] -> Ok(Some(List.rev accT))
            | BencodeValue.List urls :: t ->
                let rec loopUrls accU itemsU =
                    match itemsU with
                    | [] -> Ok(List.rev accU)
                    | hu :: tu ->
                        match asString hu with
                        | Ok s -> loopUrls (s :: accU) tu
                        | Error e -> Error e

                match loopUrls [] urls with
                | Ok u -> loopTiers (u :: accT) t
                | Error e -> Error e
            | v :: _ -> Error $"Expected list for announce-list tier, got {v}"

        loopTiers [] tiers
    | Some v -> Error $"Expected list for announce-list, got {v}"

let parse (bytes: byte[]) : Result<TorrentMetaInfo, string> =
    result {
        let! root = Downpour.Bencode.Decoder.decode bytes
        let! infoVal = dictGetReq "info" root

        let! announceVal = dictGetReq "announce" root
        let! announce = asString announceVal

        let! announceListVal = dictGet "announce-list" root
        let! announceList = parseAnnounceList announceListVal

        let! commentVal = dictGet "comment" root

        let! comment =
            match commentVal with
            | Some v -> asString v |> Result.map Some
            | None -> Ok None

        let! createdByVal = dictGet "created by" root

        let! createdBy =
            match createdByVal with
            | Some v -> asString v |> Result.map Some
            | None -> Ok None

        let! creationDateVal = dictGet "creation date" root

        let! creationDate =
            match creationDateVal with
            | Some v -> asInt v |> Result.map (fun i -> DateTimeOffset.FromUnixTimeSeconds i |> Some)
            | None -> Ok None

        let! info = parseInfo infoVal
        let! infoHash = computeInfoHash bytes

        return
            { Announce = announce
              AnnounceList = announceList
              Comment = comment
              CreatedBy = createdBy
              CreationDate = creationDate
              Info = info
              InfoHash = infoHash }
    }
