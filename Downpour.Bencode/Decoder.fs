module Downpour.Bencode.Decoder

open System
open Downpour.Bencode


let (|MemRem|MemNil|) (mem: ReadOnlyMemory<byte>) =
    if mem.Length > 0 then
        MemRem(mem.Span.[0], mem.Slice(1)) 
    else 
        MemNil


let rec parse (mem: ReadOnlyMemory<byte>) : ParseResult =
    match mem with
    | MemRem('i'B, rest) -> parseInt 0L false rest
    | MemRem(digit, _) as data when digit >= '0'B && digit <= '9'B -> parseString 0 data
    | MemRem('l'B, rest) -> parse rest
    | MemRem('d'B, rest) -> parse rest
    | MemRem(unknown, _) -> Error $"Unexpected byte identifier: {unknown}"
    | MemNil -> Error $"Unexpected end of input data"
   
   
and parseInt (acc: int64) (neg: bool) (mem: ReadOnlyMemory<byte>) : Result<BencodeValue * ReadOnlyMemory<byte>, string> =
    match mem with
    | MemRem('e'B, rest) ->
        let result = acc * (if neg then -1L else 1L)
        Ok (BencodeValue.Integer result, rest)
    | MemRem('-'B, rest) -> parseInt acc true mem
    | MemRem(digit, rest) when digit >= '0'B && digit <= '9'B ->
        let numeric = int64 (digit - '0'B)
        let newAcc = (acc * 10L) + numeric
        parseInt newAcc neg rest
    | MemNil -> Error $"Unexpected EOF when parsing integer"
    | MemRem(bad, _) -> Error $"Invalid character found within integer: {bad}"

and parseString (acc: int) (mem: ReadOnlyMemory<byte>) : Result<BencodeValue * ReadOnlyMemory<byte>, string> =
    match mem with
    | MemRem(':'B, rest) ->
        if acc > rest.Length then
            Error $"String length {acc} exceeds remaining bytes {rest.Length}"
        else
            let toString = rest.Slice(0, acc)
            Ok (BencodeValue.String (toString.ToArray()), rest.Slice(acc))
    | MemRem(digit, rest) when digit >= '0'B && digit <= '9'B ->
        let numeric = int (digit - '0'B)
        let newAcc = (acc * 10) + numeric
        parseString newAcc rest
    | MemRem(bad, _) -> Error $"Invalid character in string length: {bad}"
    | MemNil -> Error $"Unexpected end of input data"
    
    
// and parseList (acc: BencodeValue list) (mem: ReadOnlyMemory<byte>) : Result<BencodeValue * ReadOnlyMemory<byte>, string> =
//     match mem with
//     | MemRem('e'B, rest) -> Ok (BencodeValue.List acc, rest)
//     | MemRem(_, _) ->
//         // let value, rest = parse mem
        
        
let decode (data: byte array) : Result<BencodeValue, string> =
    if Array.isEmpty data then
        Error "Invalid value: empty byte array"
    else
        let initialMem = ReadOnlyMemory<byte>(data)
        
        match parse initialMem with
        | Ok (result, remaining) ->
            if remaining.Length > 0 then
                Error $"Incomplete parse: {remaining.Length} bytes remain"
            else
                Ok result
        | Error err -> Error err
            