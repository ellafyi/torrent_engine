module Downpour.Tracker.HttpTracker

open System
open System.Net
open System.Net.Http
open System.Text
open Downpour.Bencode.Types
open Downpour.Bencode

let private percentEncode (bytes: byte[]) =
    bytes |> Array.map (sprintf "%%%02x") |> String.concat ""

let private buildUrl (baseUrl: string) (req: AnnounceRequest) =
    let eventParam =
        match req.Event with
        | AnnounceEvent.None -> Option.None
        | Started -> Some "started"
        | Completed -> Some "completed"
        | Stopped -> Some "stopped"

    let sb = StringBuilder()
    sb.Append(baseUrl) |> ignore
    sb.Append("?info_hash=") |> ignore
    sb.Append(percentEncode req.InfoHash) |> ignore
    sb.Append("&peer_id=") |> ignore
    sb.Append(percentEncode req.PeerId) |> ignore
    sb.Append($"&port={req.Port}") |> ignore
    sb.Append($"&uploaded={req.Uploaded}") |> ignore
    sb.Append($"&downloaded={req.Downloaded}") |> ignore
    sb.Append($"&left={req.Left}") |> ignore
    sb.Append("&compact=1") |> ignore

    match eventParam with
    | Some e -> sb.Append($"&event={e}") |> ignore
    | _ -> ()

    match req.TrackerId with
    | Some tid -> sb.Append($"&trackerid={Uri.EscapeDataString tid}") |> ignore
    | _ -> ()

    sb.ToString()

let private traverseResults (f: 'a -> Result<'b, TrackerError>) (xs: 'a list) : Result<'b list, TrackerError> =
    List.foldBack
        (fun x acc ->
            match f x, acc with
            | Ok v, Ok vs -> Ok(v :: vs)
            | Error e, _ -> Error e
            | _, Error e -> Error e)
        xs
        (Ok [])

let private parseCompactPeers (bytes: byte[]) : Result<PeerInfo list, TrackerError> =
    if bytes.Length % 6 <> 0 then
        Error(ParseError $"Compact peer list length {bytes.Length} is not a multiple of 6")
    else
        bytes
        |> Array.chunkBySize 6
        |> Array.map (fun chunk ->
            let ip = IPAddress(chunk.[0..3])
            let port = uint16 ((int chunk.[4] <<< 8) ||| int chunk.[5])
            { IP = ip; Port = port })
        |> Array.toList
        |> Ok

let private parseDictPeer (entry: BencodeValue) : Result<PeerInfo, TrackerError> =
    match entry with
    | BencodeValue.Dictionary d ->
        match Map.tryFind "ip"B d, Map.tryFind "port"B d with
        | Some(BencodeValue.String ipBytes), Some(BencodeValue.Integer port) ->
            let ipStr = Encoding.ASCII.GetString ipBytes

            match IPAddress.TryParse ipStr with
            | true, ip -> Ok { IP = ip; Port = uint16 port }
            | _ -> Error(ParseError $"Invalid IP address: {ipStr}")
        | _ -> Error(ParseError "Peer entry missing ip or port")
    | _ -> Error(ParseError "Peer entry is not a dictionary")

let private parseResponse (bytes: byte[]) : Result<AnnounceResponse, TrackerError> =
    match Bencode.parse bytes with
    | Error e -> Error(ParseError e)
    | Ok(BencodeValue.Dictionary dict) ->
        match Map.tryFind "failure reason"B dict with
        | Some(BencodeValue.String reason) -> Error(TrackerFailure(Encoding.UTF8.GetString reason))
        | _ ->
            match Map.tryFind "interval"B dict with
            | Some(BencodeValue.Integer interval) ->
                let minInterval =
                    match Map.tryFind "min interval"B dict with
                    | Some(BencodeValue.Integer n) -> Some(int n)
                    | _ -> Option.None

                let trackerId =
                    match Map.tryFind "tracker id"B dict with
                    | Some(BencodeValue.String s) -> Some(Encoding.UTF8.GetString s)
                    | _ -> Option.None

                let seeders =
                    match Map.tryFind "complete"B dict with
                    | Some(BencodeValue.Integer n) -> int n
                    | _ -> 0

                let leechers =
                    match Map.tryFind "incomplete"B dict with
                    | Some(BencodeValue.Integer n) -> int n
                    | _ -> 0

                let peersResult =
                    match Map.tryFind "peers"B dict with
                    | Some(BencodeValue.String peerBytes) -> parseCompactPeers peerBytes
                    | Some(BencodeValue.List entries) -> traverseResults parseDictPeer entries
                    | Some _ -> Error(ParseError "Unexpected type for peers field")
                    | _ -> Ok []

                peersResult
                |> Result.map (fun peers ->
                    { Interval = int interval
                      MinInterval = minInterval
                      TrackerId = trackerId
                      Seeders = seeders
                      Leechers = leechers
                      Peers = peers })

            | _ -> Error(ParseError "Response missing required field: interval")
    | Ok _ -> Error(ParseError "Tracker response is not a dictionary")

let announce (client: HttpClient) (url: string) (req: AnnounceRequest) : Async<Result<AnnounceResponse, TrackerError>> =
    async {
        let fullUrl = buildUrl url req

        try
            let! bytes = client.GetByteArrayAsync(fullUrl) |> Async.AwaitTask
            return parseResponse bytes
        with ex ->
            return Error(NetworkError ex.Message)
    }
