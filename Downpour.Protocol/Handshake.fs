module Downpour.Protocol.Handshake

let private protocolString = "BitTorrent protocol"B

let serialize (infoHash: byte[]) (peerId: byte[]) : byte[] =
    Array.concat [ [| 19uy |]; protocolString; Array.zeroCreate 8; infoHash; peerId ]

let deserialize (data: byte[]) : Result<HandshakeMessage, string> =
    if data.Length <> 68 then
        Error "Handshake must be 68 bytes"
    elif data[0] <> 19uy then
        Error "Invalid protocol string length"
    elif data[1..19] <> protocolString then
        Error "Invalid protocol string"
    else
        Ok
            { ReservedBytes = data.[20..27]
              InfoHash = data.[28..47]
              PeerId = data.[48..67] }
