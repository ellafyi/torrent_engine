module Downpour.Bencode.Types

open System

[<RequireQualifiedAccess>]
type BencodeValue =
    | Integer of int64
    | String of byte array
    | List of BencodeValue list
    | Dictionary of Map<byte array, BencodeValue>

type ParseResult = Result<BencodeValue * ReadOnlyMemory<byte>, string>
