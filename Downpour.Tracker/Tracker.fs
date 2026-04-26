module Downpour.Tracker.Tracker

open System
open System.Net.Http

let private httpClient = new HttpClient()

let announce (url: string) (req: AnnounceRequest) : Async<Result<AnnounceResponse, TrackerError>> =
    let uri = Uri(url)

    match uri.Scheme with
    | "http"
    | "https" -> HttpTracker.announce httpClient url req
    | "udp" -> UdpTracker.announce url req
    | scheme -> async { return Error(ParseError $"Unsupported tracker scheme: {scheme}") }
