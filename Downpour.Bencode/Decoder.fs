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
    | MemRem('i'B, rest) -> parseInt rest
    | MemRem(digit, _) as data when digit >= '0'B && digit <= '9'B -> parseString 0 data
    | MemRem('l'B, rest) -> parseList [] rest
    | MemRem('d'B, rest) -> parseDict Map.empty rest
    | MemRem(unknown, _) -> Error $"Unexpected byte identifier: {unknown}"
    | MemNil -> Error $"Unexpected end of input data"
   
/// Integers
and parseInt (mem: ReadOnlyMemory<byte>) : ParseResult =
    match mem with
    | MemRem('-'B, rest) ->
        match parseIntDigits rest with
        | Ok (BencodeValue.Integer 0L, _) -> Error "i-0e is invalid"
        | Ok (BencodeValue.Integer n, rest) -> Ok (BencodeValue.Integer -n, rest)
        | err -> err
    | _ -> parseIntDigits mem

and parseIntDigits (mem: ReadOnlyMemory<byte>) : ParseResult =
    match mem with
    | MemRem('0'B, MemRem('e'B, rest)) ->
        Ok (BencodeValue.Integer 0L, rest) // 0 allowed only when alone
    | MemRem('0'B, _) ->
        Error "Leading zeros are not permitted in integers"
    | MemRem(digit, rest) when digit >= '1'B && digit <= '9'B ->
        let numeric = int64 (digit - '0'B)
        parseIntAcc numeric rest
    | MemNil -> Error "Unexpected EOF when parsing integer"
    | MemRem(bad, _) -> Error $"Invalid character in integer: {bad}"

and parseIntAcc (acc: int64) (mem: ReadOnlyMemory<byte>) : ParseResult =
    match mem with
    | MemRem('e'B, rest) -> Ok (BencodeValue.Integer acc, rest)
    | MemRem(digit, rest) when digit >= '0'B && digit <= '9'B ->
        parseIntAcc (acc * 10L + int64 (digit - '0'B)) rest
    | MemNil -> Error "Unexpected EOF when parsing integer"
    | MemRem(bad, _) -> Error $"Invalid character in integer: {bad}"

/// Strings
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
    
/// Lists
and parseList (acc: BencodeValue list) (mem: ReadOnlyMemory<byte>) : Result<BencodeValue * ReadOnlyMemory<byte>, string> =
    match mem with
    | MemRem('e'B, rest) -> Ok (BencodeValue.List (List.rev acc), rest)
    | _ ->
        match parse mem with
        | Error err -> Error err
        | Ok (value, rest) -> parseList (value :: acc) rest
        
/// Dicts
and parseDict (acc: Map<byte array, BencodeValue>) (mem: ReadOnlyMemory<byte>) : Result<BencodeValue * ReadOnlyMemory<byte>, string> =
    match mem with
    | MemRem('e'B, rest) -> Ok (BencodeValue.Dictionary acc, rest)
    | _ ->
        match parse mem with
        | Error err -> Error err
        | Ok (BencodeValue.String key, rest) ->
            match parse rest with
            | Error err -> Error err
            | Ok (value, rest2) -> parseDict (Map.add key value acc) rest2
        | Ok _ -> Error $"Dictionary key must be string"
        
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
            