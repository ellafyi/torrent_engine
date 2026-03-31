namespace Downpour.Bencode

[<RequireQualifiedAccess>]
type BencodeValue =
    | Integer of int64
    | String of byte array
    | List of BencodeValue list
    | Dictionary of Map<byte array, BencodeValue>