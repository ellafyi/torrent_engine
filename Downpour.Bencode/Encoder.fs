module Downpour.Bencode.Encoder

open System.Text
open Types
open System


let rec encode (value: BencodeValue) : byte[] =
    match value with
    | BencodeValue.Integer i ->
        Encoding.ASCII.GetBytes($"i{i}e")
    | BencodeValue.String bytes ->
        let prefix = Encoding.ASCII.GetBytes($"{bytes.Length}")
        Array.append prefix bytes
    | BencodeValue.List items ->
        let body =
            items
            |> List.map encode
            |> Array.concat
        Array.concat [ "l"B; body; "e"B ]
    | BencodeValue.Dictionary d ->
        let sortedPairs =
            d
            |> Map.toSeq
            |> Seq.sortWith (fun (a, _) (b, _) ->
                let len = min a.Length b.Length
                let mutable i = 0
                let mutable result = 0
                while i < len && result = 0 do
                    result <- compare a.[i] b.[i]
                    i <- i + 1
                if result = 0 then compare a.Length b.Length else result)
 
        let body =
            sortedPairs
            |> Seq.map (fun (k, v) -> Array.append (encode (BencodeValue.String k)) (encode v))
            |> Array.concat
            
        Array.concat [ "d"B; body; "e"B ]

