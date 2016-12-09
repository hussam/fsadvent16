namespace Eyas.Network

open System
open System.Collections.Generic
open System.Net
open System.Net.Sockets
open System.Threading

type Client(port : int, randomSeed : int) =

    member this.Run(servers : (string * int) [], minJobSize, maxJobSize, monitoringPeriod : int, msgsToSend : int, msgsPerSec : int, routingFunc) =
        let mutable queueLengths = servers |> Array.map(fun (hostname, port) -> (hostname, port, 0))    // assume all servers have empty queues when we start

        let featurize = fun (qlens) ->
            qlens |> Array.map(fun (h,p,q) -> q)


        // Periodically check the queue lengths at the different servers
        let serverMonitor = async {
            use socket = new UdpClient()
            let zero = BitConverter.GetBytes(0)
            let anySender = new IPEndPoint(IPAddress.Any, 0)    // needed by UdpClient.Receive()
            while true do
                queueLengths <- servers
                                |> Array.map (fun (hostname, port) ->
                                        socket.Send(zero, zero.Length, hostname, port) |> ignore
                                        let result = socket.Receive(ref anySender)
                                        let qlen = BitConverter.ToInt32(result, 0)
                                        (hostname, port, qlen) )
                Thread.Sleep(monitoringPeriod)
        }
        // Used to cancel the server queue monitoring thread
        use cancellationSource = new CancellationTokenSource()
        Async.Start(serverMonitor, cancellationSource.Token)

        let rand = new Random(randomSeed)
        let timer = new Diagnostics.Stopwatch()

        let jobsInFlight = new Dictionary<_, _>()

        let sender = async {
            let timeBetweenMsgs = new TimeSpan(int64(1000 * 1000 * 10 / msgsPerSec))  // a tick is 100 nanoseconds --> 1 sec = 10^7 ticks.
            use socket = new UdpClient()
            for i in 1..msgsToSend do
                //if i % 100 = 0 then printfn "--Sent %d messages." i
                // Pick the server to which the request will be forwarded
                let targetIndex, probabilityOfSelection = routingFunc(queueLengths)
                let serverHostname, serverPort, _ = queueLengths.[targetIndex]

                // Send the message to the server and measure the extra delay
                let jobSize = rand.Next(minJobSize, maxJobSize)
                let sendTime = timer.ElapsedMilliseconds
                let msg = Array.concat [| BitConverter.GetBytes(jobSize); BitConverter.GetBytes(i); BitConverter.GetBytes(port) |]
                socket.Send(msg, msg.Length, serverHostname, serverPort) |> ignore

                jobsInFlight.[i] <- (featurize(queueLengths), jobSize, targetIndex, probabilityOfSelection, sendTime)
                Thread.Sleep(timeBetweenMsgs)
            return null
        }

        let receiver = async {
            use socket = new UdpClient(port)
            let results = new List<_>()
            // This IPEndPoint object will allow us to read incoming datagrams sent from any source
            let anySender = new IPEndPoint(IPAddress.Any, 0)
            while results.Count < msgsToSend do
                //if results.Count % 100 = 0 then printfn "Received %d messages." results.Count
                let bytes = socket.Receive(ref anySender)
                let endTime = timer.ElapsedMilliseconds
                let jobId = BitConverter.ToInt32(bytes, 4)

                let (qlens, jobSize, selectedServerIdx, p, sendTime) = jobsInFlight.[jobId]
                let delay = int(endTime - sendTime - int64(jobSize))
                results.Add((qlens, jobSize, selectedServerIdx, p, delay))
            return results      // return the delays experienced
        }

        timer.Start()
        let results = [receiver; sender]
                        |> Async.Parallel
                        |> Async.RunSynchronously
                        |> Array.head
        timer.Stop()
        cancellationSource.Cancel()     // stop the monitoring thread
        results

