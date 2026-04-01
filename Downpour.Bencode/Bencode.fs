namespace Downpour.Bencode


[<RequireQualifiedAccess>]
module Bencode =
    let parse (data: byte array) : Result<BencodeValue, string> =
        Decoder.decode data
        
    let stringify (value: BencodeValue) : byte array =
        Array.empty
