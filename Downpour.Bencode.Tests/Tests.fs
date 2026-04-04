module Downpour.Bencode.Tests

open Xunit
open FsCheck.Xunit
open Downpour.Bencode
open Downpour.Bencode.Decoder

[<Fact>]
let ``parses integer`` () =
    let input = "i42e"B
    let result = decode input
    Assert.Equal(Ok (BencodeValue.Integer 42L), result)

[<Fact>]
let ``parses string`` () =
    let input = "4:spam"B
    Assert.Equal(Ok (BencodeValue.String "spam"B), decode input)
    
[<Theory>]
[<InlineData($"i0e", 0L)>]
[<InlineData($"i42e", 42L)>]
[<InlineData($"i-7e", -7L)>]
[<InlineData($"i9007199254740992e", 9007199254740992L)>]
[<InlineData($"i0e", 0L)>]
let ``parses valid integers`` (s: string, n: int64) =
    let input = System.Text.Encoding.ASCII.GetBytes s
    Assert.Equal(Ok (BencodeValue.Integer n), decode input)

[<Theory>]
[<InlineData($"i-0e")>]
[<InlineData($"i042e")>]
[<InlineData($"iе")>]
let ``parses invalid integers`` (s: string) =
    let input = System.Text.Encoding.ASCII.GetBytes s
    Assert.True(Result.isError (decode input))
    
[<Property>]
let ``parses any integer`` (n: int64) =
    let input = System.Text.Encoding.ASCII.GetBytes $"i{n}e"
    decode input = Ok (BencodeValue.Integer n)