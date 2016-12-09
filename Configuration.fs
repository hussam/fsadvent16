namespace Eyas

module Configuration =
    open System

    /// Load balancer's strategy for routing jobs
    type Strategy =
        | RandomSpray
        | WeightedRandom
        | ShortestQueue


    /// General configuration of the program.
    /// Including things like: Is this a client or a server? How many jobs to send? How frequently? How to balancer requests? ...etc
    type Config = {
        isServer : bool
        numClients : int
        serverPort : int
        minJobSize : int
        maxJobSize : int
        refreshPeriod : int
        maxRefreshPeriod : int
        incRefreshPeriod : int
        servers : (string * int) list
        msgsToSend : int
        msgsPerSec : int
        clientPortBase : int
        randomSeed : int
        strategy : Strategy
    } with
        static member Default = {
            isServer = true
            numClients = 10
            serverPort = 8000
            minJobSize = 10
            maxJobSize = 100
            refreshPeriod = 30
            maxRefreshPeriod = 30
            incRefreshPeriod = Int32.MaxValue
            servers = []
            msgsToSend = 1000
            msgsPerSec = 100
            clientPortBase = 4000
            randomSeed = 3000
            strategy = RandomSpray
        }

    module private Internals =
       let rec parse args config =
           // XXX: no input validation whatsoever. This is bad if it were real code
           match args with
           | "--port" :: port :: tail | "-p" :: port :: tail ->
               let p = Int32.Parse(port)
               {config with serverPort = p} |> parse tail
           | "--client" :: nc :: tail | "-c" :: nc :: tail ->
               let numClients = Int32.Parse(nc)
               {config with isServer = false; numClients = numClients} |> parse tail
           | "--jobs" :: range :: tail | "-j" :: range :: tail ->
               match (range.Split('-') |> Array.map Int32.Parse) with
               | [| size |] ->
                   {config with minJobSize = size; maxJobSize = size} |> parse tail
               | [| min ; max |] ->
                   {config with minJobSize = min; maxJobSize = max} |> parse tail
               | _ ->
                   config |> parse tail
           | "--refreshRate" :: range :: tail | "-r" :: range :: tail ->
               match range.Split(':') with
               | [| period |] ->
                   let p = Int32.Parse(period)
                   {config with refreshPeriod = p; maxRefreshPeriod = p} |> parse tail
               | [| start ; step ; _end |] ->
                   let s = Int32.Parse(start)
                   let e = Int32.Parse(_end)
                   let t = Int32.Parse(step)
                   {config with refreshPeriod = s; maxRefreshPeriod = e; incRefreshPeriod = t} |> parse tail
               | _ ->
                   config |> parse tail
           | "--server" :: server :: tail | "-s" :: server :: tail ->
               let addr = server.Split(':')
               let host = addr.[0]
               let port = Int32.Parse(addr.[1])
               {config with servers = (host, port) :: config.servers} |> parse tail
           | "--msgsToSend" :: nm :: tail | "-m" :: nm :: tail ->
               let n = Int32.Parse(nm)
               {config with msgsToSend = n} |> parse tail
           | "--msgsPerSec" :: mps :: tail | "-mps" :: mps :: tail ->
               let r = Int32.Parse(mps)
               {config with msgsPerSec = r} |> parse tail
           | "--randomSeed" :: rs :: tail | "-rs" :: rs :: tail ->
               let r = Int32.Parse(rs)
               {config with randomSeed = r} |> parse tail
           | "--strategy" :: s :: tail | "-t" :: s :: tail ->
               let c =
                   match s with
                   | "random" -> {config with strategy = RandomSpray}
                   | "weighted" -> {config with strategy = WeightedRandom}
                   | "shortestQ" -> {config with strategy = ShortestQueue}
                   | _ -> config
               c |> parse tail
           | arg :: tail ->  // unrecognized argument
               printfn "UNKOWN ARGUMENT: %s" arg
               config |> parse tail
           | [] ->
               config

    let parse args =
        Internals.parse args Config.Default