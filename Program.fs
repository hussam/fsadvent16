open Eyas
open Eyas.Configuration
open Eyas.Network
open FSharp.Charting
open System
open System.Diagnostics


[<EntryPoint>]
let main args =
    let config = Configuration.parse (Array.toList args)
    printfn "Configuration = %A\n" config
    match config.isServer with
    | true ->
        Server.Start(config.serverPort)
    | false ->
        let servers =
            config.servers
            |> List.map(fun (_host, port) -> Process.Start("eyas.exe", sprintf "-p %d" port))

        let results =
            [ config.refreshPeriod .. config.incRefreshPeriod .. config.maxRefreshPeriod ]
            |> List.map (fun r ->
                    printf "Flushing servers ... "
                    config.servers
                    |> List.mapi(fun i (host, port) -> async { Server.FlushPendingMessages(config.clientPortBase + i, host, port) })
                    |> Async.Parallel
                    |> Async.RunSynchronously
                    |> ignore
                    printfn "done"
                    printf "Starting run with refresh every %d ms ... " r
                    let timer = Stopwatch()
                    timer.Start()
                    let results =
                        [| 1..config.numClients |]
                        |> Array.map(fun i ->
                                        let c = new Client(config.clientPortBase + i, config.randomSeed)
                                        let routingFunc =
                                            let rand = new Random(config.randomSeed)
                                            match config.strategy with
                                            | RandomSpray -> Strategies.RandomSpray(rand)
                                            | WeightedRandom -> Strategies.WeightedRandom(rand)
                                            | ShortestQueue -> Strategies.ShortestQueue(rand)
                                        async { return c.Run( List.toArray config.servers, config.minJobSize, config.maxJobSize, config.refreshPeriod, config.msgsToSend, config.msgsPerSec, routingFunc ) } )
                        |> Async.Parallel
                        |> Async.RunSynchronously
                        |> Array.fold (fun accIn clientResults -> Array.append accIn (clientResults.ToArray())) [||]
                        |> Array.map (fun ((_, _, _, _, experiencedDelay) as r) -> experiencedDelay)    // cast results to integers
                        |> Array.sort
                        |> Array.mapi (fun i latency -> (100.0 * float(i+1) / float(config.msgsToSend), latency))
                    timer.Stop()
                    printfn "done in %d ms" timer.ElapsedMilliseconds
                    (r, results))

        servers |> List.iter (fun p -> p.Kill())

        let chart =
            results
            |> List.map (fun (r, results) -> let title = (sprintf "Probe Queues Every %d ms" r) in Chart.Line(results, Name=title).WithLegend(Enabled=true))
            |> Chart.Combine
            |> (fun chart -> chart.WithYAxis(Title="Added Latency (ms)").WithXAxis(Title="Pct of Requests"))
        //chart |> Chart.Save(sprintf "results/chart-%s.png" (DateTime.Now.ToString "MM-dd-HH-mm"))
        printfn "Number of results = %d" results.Length

        results
        |> List.map (fun (r, results) -> (r, (results |> Array.map (fun (i, latency) -> float latency) |> Array.average)))
        |> List.iter (fun (r, avg) -> printfn "When probing queues every %d ms, average added delay was %.3f" r avg)

        Windows.Forms.Application.Run(chart.ShowChart())
        Console.ReadLine() |> ignore
    0 // return an integer exit code