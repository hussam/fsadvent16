namespace Eyas.Network

module Server =
    open System
    open System.Net
    open System.Net.Sockets
    open System.Threading

    type ServerMessage =
        | Flush of responsePort:int
        | Job of size:int * buffer:byte[] * responsePort:int

    let FlushPendingMessages(localPort : int, serverHostname : string, serverPort : int) =
        use socket = new UdpClient(localPort)
        let flushMsg = Array.concat [| BitConverter.GetBytes(-1); BitConverter.GetBytes(0); BitConverter.GetBytes(localPort) |]
        socket.Send(flushMsg, flushMsg.Length, serverHostname, serverPort) |> ignore
        socket.Receive(ref (new IPEndPoint(IPAddress.Any, 0))) |> ignore

    let Start(port : int) =
        // The server loop that does the actual job execution
        let server = MailboxProcessor<ServerMessage>.Start(fun inbox ->
             async {
                use replySocket = new UdpClient()
                let rec flush () =  async {
                    let! opt = inbox.TryReceive(1000)    // 1 second timeout to receive any in-flight messages -- this is overkill for co-located clients/servers
                    match opt with
                    | None -> ()
                    | Some _ -> do! flush()
                }
                while true do
                    let! msg = inbox.Receive()
                    match msg with
                    | Flush responsePort ->
                       printf "Flushing..."
                       do! flush()
                       printfn "done!"
                       replySocket.Send([|0uy|], 1, "127.0.0.1", responsePort) |> ignore
                    | Job (jobSize, buffer, port) ->
                       Thread.Sleep(jobSize)
                       replySocket.Send(buffer, buffer.Length, "127.0.0.1", port) |> ignore
             })

        // The server loop that listens to incomming requests from clients
        use rcvSocket = new UdpClient(port)
        while true do
            let result = rcvSocket.ReceiveAsync() |> Async.AwaitTask |> Async.RunSynchronously
            let jobSize = BitConverter.ToInt32(result.Buffer, 0)
            if jobSize = 0 then     // report queue length
                let sender = result.RemoteEndPoint
                let response = BitConverter.GetBytes(server.CurrentQueueLength)
                rcvSocket.Send(response, response.Length, sender) |> ignore
            else     // append message to queue
                let port = BitConverter.ToInt32(result.Buffer, 8)
                server.Post <|
                    if jobSize = -1 then Flush (port)    // special code to flush queue
                    else Job(jobSize, result.Buffer, port)