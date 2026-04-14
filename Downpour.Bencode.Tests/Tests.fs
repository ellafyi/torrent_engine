module Downpour.Bencode.Tests

open System
open Xunit
open FsCheck.Xunit
open Downpour.Bencode.Types
open Downpour.Bencode.Decoder

// Helper
let decodeStr (s: string) = decode (System.Text.Encoding.ASCII.GetBytes s)
let decodeBytes (b: byte array) = decode b
let parseBytes (b: byte array) = parse (ReadOnlyMemory<byte>(b))

// === INTEGERS ===

[<Theory>]
[<InlineData("i0e", 0L)>] // [INT-01] Zero
[<InlineData("i42e", 42L)>] // [INT-02] Positive integer
[<InlineData("i-7e", -7L)>] // [INT-03] Negative integer
[<InlineData("i9007199254740992e", 9007199254740992L)>] // [INT-04] Large integer
let ``Valid Integers`` (input: string, expected: int64) =
    Assert.Equal(Ok (BencodeValue.Integer expected), decodeStr input)

[<Theory>]
[<InlineData("i-0e")>] // [INT-05] Negative zero
[<InlineData("i042e")>] // [INT-06] Leading zeros
[<InlineData("ie")>] // [INT-07] Empty integer body
let ``Invalid Integers`` (input: string) =
    Assert.True(Result.isError (decodeStr input))

// === STRINGS ===

[<Fact>]
let ``STR-01 Empty string`` () =
    Assert.Equal(Ok (BencodeValue.String Array.empty), decodeStr "0:")

[<Fact>]
let ``STR-02 ASCII word`` () =
    Assert.Equal(Ok (BencodeValue.String ("spam"B)), decodeStr "4:spam")

[<Fact>]
let ``STR-03 String with spaces`` () =
    Assert.Equal(Ok (BencodeValue.String ("hello ben"B)), decodeStr "9:hello ben")

[<Fact>]
let ``STR-04 Single character`` () =
    Assert.Equal(Ok (BencodeValue.String ("x"B)), decodeStr "1:x")

[<Fact>]
let ``STR-05 Numeric-looking string`` () =
    Assert.Equal(Ok (BencodeValue.String ("123"B)), decodeStr "3:123")

[<Fact>]
let ``STR-06 Binary / non-UTF-8 bytes`` () =
    let input = [| '4'B; ':'B; 0xFFuy; 0xFEuy; 0x00uy; 0x41uy |]
    let expected = [| 0xFFuy; 0xFEuy; 0x00uy; 0x41uy |]
    Assert.Equal(Ok (BencodeValue.String expected), decodeBytes input)

[<Fact>]
let ``STR-07 Length mismatch (invalid)`` () =
    Assert.True(Result.isError (decodeStr "5:hi"))

// === LISTS ===

[<Fact>]
let ``LIST-01 Empty list`` () =
    Assert.Equal(Ok (BencodeValue.List []), decodeStr "le")

[<Fact>]
let ``LIST-02 List of integers`` () =
    let expected = BencodeValue.List [BencodeValue.Integer 1L; BencodeValue.Integer 2L; BencodeValue.Integer 3L]
    Assert.Equal(Ok expected, decodeStr "li1ei2ei3ee")

[<Fact>]
let ``LIST-03 List of strings`` () =
    let expected = BencodeValue.List [BencodeValue.String "foo"B; BencodeValue.String "bar"B]
    Assert.Equal(Ok expected, decodeStr "l3:foo3:bare")

[<Fact>]
let ``LIST-04 Mixed list`` () =
    let expected = BencodeValue.List [BencodeValue.Integer 99L; BencodeValue.String "test"B]
    Assert.Equal(Ok expected, decodeStr "li99e4:teste")

[<Fact>]
let ``LIST-05 Nested lists`` () =
    let expected = 
        BencodeValue.List [
            BencodeValue.List [BencodeValue.Integer 1L; BencodeValue.Integer 2L]
            BencodeValue.List [BencodeValue.Integer 3L]
        ]
    Assert.Equal(Ok expected, decodeStr "lli1ei2eeli3eee")

[<Fact>]
let ``LIST-06 List missing terminator (invalid)`` () =
    Assert.True(Result.isError (decodeStr "li1ei2e"))

// === DICTIONARIES ===

[<Fact>]
let ``DICT-01 Empty dict`` () =
    Assert.Equal(Ok (BencodeValue.Dictionary Map.empty), decodeStr "de")

[<Fact>]
let ``DICT-02 Single key`` () =
    let expected = BencodeValue.Dictionary (Map.ofList [("foo"B, BencodeValue.Integer 1L)])
    Assert.Equal(Ok expected, decodeStr "d3:fooi1ee")

[<Fact>]
let ``DICT-03 Multiple keys`` () =
    let expected = 
        BencodeValue.Dictionary (
            Map.ofList [
                ("bar"B, BencodeValue.Integer 2L)
                ("foo"B, BencodeValue.Integer 1L)
            ]
        )
    Assert.Equal(Ok expected, decodeStr "d3:bari2e3:fooi1ee")

[<Fact>]
let ``DICT-04 String value`` () =
    let expected = BencodeValue.Dictionary (Map.ofList [("name"B, BencodeValue.String "alice"B)])
    Assert.Equal(Ok expected, decodeStr "d4:name5:alicee")

[<Fact>]
let ``DICT-05 Integer key (invalid)`` () =
    Assert.True(Result.isError (decodeStr "di1e3:vale"))

[<Fact>]
let ``DICT-06 Odd number of items (invalid)`` () =
    Assert.True(Result.isError (decodeStr "d3:fooe"))

[<Fact>]
let ``DICT-07 Unsorted keys (non-canonical)`` () =
    let expected = 
        BencodeValue.Dictionary (
            Map.ofList [
                ("bar"B, BencodeValue.Integer 2L)
                ("foo"B, BencodeValue.Integer 1L)
            ]
        )
    Assert.Equal(Ok expected, decodeStr "d3:fooi1e3:bari2ee")

// === COMBINATIONS ===

[<Fact>]
let ``CMB-01 Dict with list value`` () =
    let expected = 
        BencodeValue.Dictionary (
            Map.ofList [
                ("list"B, BencodeValue.List [BencodeValue.Integer 1L; BencodeValue.Integer 2L; BencodeValue.Integer 3L])
            ]
        )
    Assert.Equal(Ok expected, decodeStr "d4:listli1ei2ei3eee")

[<Fact>]
let ``CMB-02 List containing dicts`` () =
    let expected = 
        BencodeValue.List [
            BencodeValue.Dictionary (Map.ofList [("key"B, BencodeValue.Integer 1L)])
            BencodeValue.Dictionary (Map.ofList [("key"B, BencodeValue.Integer 2L)])
        ]
    Assert.Equal(Ok expected, decodeStr "ld3:keyi1eed3:keyi2eee")

[<Fact>]
let ``CMB-03 Nested dict`` () =
    let inner = BencodeValue.Dictionary (Map.ofList [("inner"B, BencodeValue.Integer 42L)])
    let expected = BencodeValue.Dictionary (Map.ofList [("outer"B, inner)])
    Assert.Equal(Ok expected, decodeStr "d5:outerd5:inneri42eee")

[<Fact>]
let ``CMB-04 Torrent-like info dict`` () =
    let expected = 
        BencodeValue.Dictionary (
            Map.ofList [
                ("length"B, BencodeValue.Integer 1048576L)
                ("name"B, BencodeValue.String "test.txt"B)
                ("piece length"B, BencodeValue.Integer 262144L)
                ("pieces"B, BencodeValue.List [
                    BencodeValue.String "aaaaaaaaaaaaaaaaaaaa"B
                    BencodeValue.String "bbbbbbbbbbbbbbbbbbbb"B
                ])
            ]
        )
    Assert.Equal(Ok expected, decodeStr "d6:lengthi1048576e4:name8:test.txt12:piece lengthi262144e6:piecesl20:aaaaaaaaaaaaaaaaaaaa20:bbbbbbbbbbbbbbbbbbbbee")

[<Fact>]
let ``CMB-05 Deeply nested structure`` () =
    let innerDict = BencodeValue.Dictionary (Map.ofList [("b"B, BencodeValue.Integer 2L)])
    let expected = 
        BencodeValue.Dictionary (
            Map.ofList [
                ("a"B, BencodeValue.List [BencodeValue.Integer 1L; innerDict])
            ]
        )
    Assert.Equal(Ok expected, decodeStr "d1:ali1ed1:bi2eeee")

[<Fact>]
let ``CMB-06 List of mixed dicts`` () =
    let dict1 = BencodeValue.Dictionary (Map.ofList [("type"B, BencodeValue.Integer 0L); ("value"B, BencodeValue.String "foo"B)])
    let dict2 = BencodeValue.Dictionary (Map.ofList [("type"B, BencodeValue.Integer 1L); ("value"B, BencodeValue.String "bar"B)])
    let expected = BencodeValue.List [dict1; dict2]
    Assert.Equal(Ok expected, decodeStr "ld4:typei0e5:value3:fooed4:typei1e5:value3:baree")

[<Fact>]
let ``CMB-07 Empty collections nested`` () =
    let input = "ld0:leedee"
    let dict1 = BencodeValue.Dictionary (Map.ofList [(Array.empty, BencodeValue.List [])])
    let dict2 = BencodeValue.Dictionary Map.empty
    let expected = BencodeValue.List [dict1; dict2]
    Assert.Equal(Ok expected, decodeStr input)

// === EDGE CASES ===

[<Fact>]
let ``EDGE-01 Trailing data after valid value`` () =
    let input = "i1ei2e"B
    let result = parseBytes input
    match result with
    | Ok (BencodeValue.Integer 1L, rem) ->
        Assert.Equal<byte array>("i2e"B, rem.ToArray())
    | _ -> Assert.Fail("Expected streaming parse to succeed and leave remainder")

[<Fact>]
let ``EDGE-02 Empty input`` () =
    Assert.True(Result.isError (decodeBytes Array.empty))

[<Fact>]
let ``EDGE-03 Very long string length prefix`` () =
    let payload = Array.create 1000000 'a'B
    let prefix = "1000000:"B
    let input = Array.append prefix payload
    
    let result = decodeBytes input
    match result with
    | Ok (BencodeValue.String bytes) ->
        Assert.Equal(1000000, bytes.Length)
        Assert.Equal<byte array>(payload, bytes)
    | _ -> Assert.Fail("Expected successful parse of 1MB string")
