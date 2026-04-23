namespace Downpour.Bencode

open Types

[<RequireQualifiedAccess>]
module Bencode =
    let parse (data: byte array) : Result<BencodeValue, string> = Decoder.decode data

    let stringify (value: BencodeValue) : byte array = Encoder.encode value
