module Downpour.Bencode.Encoder


let encodeToStream (stream: System.IO.Stream) (value: BencodeValue) =
    match value with
    | BencodeValue.Integer i -> ()
    | BencodeValue.String s -> ()
    | BencodeValue.List l -> ()
    | BencodeValue.Dictionary d -> ()

