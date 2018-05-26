﻿module Program

open Expecto
open Logary
open Logary.Configuration
open Logary.Adapters.Facade
open Hopac
open NodaTime

let stubLogger (minLevel: LogLevel) (message: Message ref) name =
  { new Logger with // stub/tests
      member x.logWithAck (_, level) messageFactory =
        message := messageFactory level
        Alt.always (Ok (Promise (())))
      member x.name = name
      member x.level = minLevel }

let stubLogManager (message: Message ref) =
  { new LogManager with
      member x.runtimeInfo =
        Internals.RuntimeInfo.create "Facade Tests" "localhost" :> _
      member x.getLogger name =
        stubLogger Verbose message name
      member x.getLoggerWithMiddleware name mid =
        stubLogger Verbose message name
      member x.flushPending dur =
        Alt.always (FlushInfo([],[]))
      member x.shutdown () = Alt.always ()
      member x.flushPending () = Alt.always ()
      member x.shutdown (fDur,sDur) =
        Alt.always (FlushInfo([],[]), ShutdownInfo([],[]))
      member x.switchLoggerLevel (path, minLevel) = ()
  }

[<Tests>]
let tests =
  let assertWorkMessage (msg: Message) =
    Expect.equal msg.level Warn "Should have logged at Warn level"
    Expect.equal msg.value ("Hey {user}!") "Should have logged event template"
    let userName = msg |> Message.tryGetField "user"
    Expect.equal userName (Some "haf") "Should have logged user as String"
    Expect.equal msg.timestamp 1470047883029045000L "Should have correct timestamp"

  testList "facades" [
    testList "shared" [
      testProperty "convert string to unit" <| fun (x: Units) ->
        match x with
        | Other _ -> true
        | x -> match Units.tryParse x.symbol with
               | Other _ ->
                 true
               | res ->
                 res = x
    ]

    testList "v1" [
      testList "logger" [
        let createLoggerSubject () =
          let msg = ref (Message.event Info "empty")
          let stub = stubLogger LogLevel.Info msg (PointName.parse "Libryy.Core")
          LoggerAdapter.createGeneric<Libryy.LoggingV1.Logger> stub,
          msg

        yield testCase "create adapter" <| fun _ ->
          let msg = ref (Message.event Info "empty")
          let stub = stubLogger LogLevel.Info msg (PointName.parse "Libryy.Core")
          let logger = LoggerAdapter.createString "Libryy.LoggingV1.Logger, Libryy" stub
          Expect.isNotNull logger "Should have gotten logger back"

        yield testCase "end to end with adapter, full logWithAck method" <| fun _ ->
          let libryyLogger, msg = createLoggerSubject ()
          let res = Libryy.CoreV1.work libryyLogger
          Expect.equal 42 res "Should get result back"
          assertWorkMessage (!msg)
          Expect.equal (!msg).name (PointName.parse "Libryy.Core.work") "Should have set name"

        yield testCase "end to end with adapter, log method" <| fun _ ->
          let libryyLogger, msg = createLoggerSubject ()
          let res = Libryy.CoreV1.workNonAsync libryyLogger
          Expect.equal 45 res "Should get result back"
          assertWorkMessage (!msg)
          Expect.equal (!msg).name (PointName.parse "Libryy.Core.work") "Should have set name"

        yield testCase "end to end with adapter, logSimple method" <| fun _ ->
          let libryyLogger, msg = createLoggerSubject ()
          let res = Libryy.CoreV1.simpleWork libryyLogger
          Expect.equal 43 res "Should get result back"
          Expect.equal (!msg).level Error "Should have logged at Error level"
          Expect.equal (!msg).value ("Too simplistic") "Should have logged event template"
          Expect.notEqual (!msg).timestamp 0L "Should have non-zero timestamp"
          Expect.notEqual (!msg).name (PointName [||]) "Should have non-empty point name"
          Expect.equal (!msg).name (PointName [| "Libryy"; "Core" |])
                       "Should have point name corresponding to the passed logger"
      ]

      testList "global config" [
        let createLogManagerSubject () =
          let msg = ref (Message.event Info "empty")
          stubLogManager msg, msg

        yield testCase "initialise with LogManager" <| fun _ ->
          let logManager, msg = createLogManagerSubject ()
          LogaryFacadeAdapter.initialise<Libryy.LoggingV1.Logger> logManager
          let res = Libryy.CoreV1.staticWork ()
          Expect.equal res 49 "Should return 49"
          Expect.equal (!msg).level Debug "Should have logged at Debug level"
          Expect.equal (!msg).value ("A debug log") "Should have logged event template"
      ]
    ]

    testList "v3" [
      testList "logger" [
        let createLoggerSubject () =
          let msg = ref (Message.event Info "empty")
          let stub = stubLogger LogLevel.Info msg (PointName.parse "Libryy.CoreV3")
          LoggerAdapter.createGeneric<Libryy.LoggingV3.Logger> stub,
          msg

        yield testCase "create adapter" <| fun _ ->
          let msg = ref (Message.event Info "empty")
          let stub = stubLogger LogLevel.Info msg (PointName.parse "Libryy.CoreV3")
          let logger = LoggerAdapter.createString "Libryy.LoggingV3.Logger, Libryy" stub
          Expect.isNotNull logger "Should have gotten logger back"

        yield testCase "end to end with adapter, full logWithAck method" <| fun _ ->
          let libryyLogger, msg = createLoggerSubject ()
          let res = Libryy.CoreV3.work libryyLogger
          Expect.equal 42 res "Should get result back"
          assertWorkMessage (!msg)
          Expect.equal (!msg).name (PointName.parse "Libryy.Core.work") "Should have set name"

        yield testCase "end to end with adapter, log method" <| fun _ ->
          let libryyLogger, msg = createLoggerSubject ()
          let res = Libryy.CoreV3.workBackpressure libryyLogger
          Expect.equal 45 res "Should get result back"
          assertWorkMessage (!msg)
          Expect.equal (!msg).name (PointName.parse "Libryy.Core.work") "Should have set name"

        yield testCase "end to end with adapter, errorWithBP method" <| fun _ ->
          let libryyLogger, msg = createLoggerSubject ()
          let res = Libryy.CoreV3.errorWithBP libryyLogger
          Expect.equal 43 res "Should get result back"
          Expect.equal (!msg).level Error "Should have logged at Error level"
          Expect.equal (!msg).value ( "Too simplistic") "Should have logged event template"
          Expect.notEqual (!msg).timestamp 0L "Should have non-zero timestamp"
          Expect.notEqual (!msg).name (PointName [||]) "Should have non-empty point name"
          Expect.equal (!msg).name (PointName [| "Libryy"; "Core" |])
                       "Should have point name corresponding to the passed logger"

        yield testCase "with exns" <| fun _ ->
          let libryyLogger, msg = createLoggerSubject ()
          let res = Libryy.CoreV3.generateAndLogExn libryyLogger
          let exns = Message.getExns !msg
          Expect.equal 2 exns.Length "Has two exns"
      ]

      testList "global config" [
        let createLogManagerSubject () =
          let msg = ref (Message.event Info "empty")
          stubLogManager msg, msg

        yield testCase "initialise with LogManager" <| fun _ ->
          let logManager, msg = createLogManagerSubject ()
          LogaryFacadeAdapter.initialise<Libryy.LoggingV3.Logger> logManager
          let res = Libryy.CoreV3.staticWork () |> Async.RunSynchronously
          Expect.equal res 49 "Should return 49"
          Expect.equal (!msg).level Debug "Should have logged at Debug level"
          Expect.equal (!msg).value ( "A debug log") "Should have logged event template"
      ]

      // input:Facade Gauge, output; Logary gauge message
      testList "gauge" [
        let namedGauge name value units: Libryy.LoggingV3.Message =
          { name      = name
            value     = Libryy.LoggingV3.Gauge (value, units)
            fields    = Map.empty
            timestamp = Libryy.LoggingV3.Global.timestamp ()
            level     = Libryy.LoggingV3.Debug }

        let expectGauge name value units (msg: Message) =
          let g = msg.context |> HashMap.tryFind name
          Expect.isSome g "gauge is in context"

          match g.Value with
          | :? Gauge as gauge ->
            let (Gauge (v, u)) = gauge
            Expect.equal v value "has correct value"
            Expect.equal u units "has correct units"
          | _ ->
            Expect.equal true false "Wrong type of gauge object"

        yield testCase "Seconds" <| fun _ ->
          let gauge = namedGauge [|"response"; "lag"|] 0.12 "Seconds"
          LoggerAdapter.toMsg Reflection.ApiVersion.V3 [||] gauge
          |> expectGauge "_logary.gauge.lag" (Float 0.12) Units.Seconds

        yield testCase "s" <| fun _ ->
          let gauge = namedGauge [|"response"; "lag"|] 0.12 "s"
          LoggerAdapter.toMsg Reflection.ApiVersion.V3 [||] gauge
          |> expectGauge "_logary.gauge.lag" (Float 0.12) Units.Seconds

        yield testCase "Milliseconds" <| fun _ ->
          let gauge = namedGauge [|"response"; "lag"|] 120.0 "Milliseconds"
          LoggerAdapter.toMsg Reflection.ApiVersion.V3 [||] gauge
          |> expectGauge "_logary.gauge.lag" (Float 120.0) (Units.Scaled (Units.Seconds, 1000.0))

        yield testCase "ms" <| fun _ ->
          let gauge = namedGauge [|"response"; "lag"|] 120.0 "ms"
          LoggerAdapter.toMsg Reflection.ApiVersion.V3 [||] gauge
          |> expectGauge "_logary.gauge.lag" (Float 120.0) (Units.Scaled (Units.Seconds, 1000.0))

        yield testCase "µs" <| fun _ ->
          let gauge = namedGauge [|"response"; "lag"|] 120.0 "µs"
          LoggerAdapter.toMsg Reflection.ApiVersion.V3 [||] gauge
          |> expectGauge "_logary.gauge.lag" (Float 120.0) (Units.Scaled (Units.Seconds, 1.0e6))

        yield testCase "Nanoseconds" <| fun _ ->
          let gauge = namedGauge [|"response"; "lag"|] 120.0 "Nanoseconds"
          LoggerAdapter.toMsg Reflection.ApiVersion.V3 [||] gauge
          |> expectGauge "_logary.gauge.lag" (Float 120.0) (Units.Scaled (Units.Seconds, 1.0e9))

        yield testCase "ns" <| fun _ ->
          let gauge = namedGauge [|"response"; "lag"|] 120.0 "ns"
          LoggerAdapter.toMsg Reflection.ApiVersion.V3 [||] gauge
          |> expectGauge "_logary.gauge.lag" (Float 120.0) (Units.Scaled (Units.Seconds, 1.0e9))

        yield testCase "unknown turns is Units.Other" <| fun _ ->
          let gauge = namedGauge [|"response"; "lag"|] 120.0 "Moments"
          LoggerAdapter.toMsg Reflection.ApiVersion.V3 [||] gauge
          |> expectGauge "_logary.gauge.lag" (Float 120.0) (Units.Other "Moments")

        yield testCase "default name" <| fun _ ->
          let gauge = namedGauge [||] 120.0 ""
          LoggerAdapter.toMsg Reflection.ApiVersion.V3 [||] gauge
          |> expectGauge "_logary.gauge.default-gauge" (Float 120.0) Units.Scalar
      ]
    ]

    testList "cs" [
      testList "logger" [
        let createLoggerSubject () =
          let msg = ref (Message.event Info "empty")
          let stub = stubLogger LogLevel.Info msg (PointName.parse "Cibryy.Core")
          LoggerCSharpAdapter.createGeneric<Cibryy.Logging.ILogger> stub,
          msg

        yield testCase "create adapter" <| fun _ ->
          let msg = ref (Message.event Info "empty")
          let stub = stubLogger LogLevel.Info msg (PointName.parse "Cibryy.Core")
          let logger = LoggerAdapter.createString "Cibryy.Logging.ILogger, Cibryy" stub
          Expect.isNotNull logger "Should have gotten logger back"

        yield testCase "end to end with adapter, full LogWithAck method" <| fun _ ->
          let cibryyLogger, msg = createLoggerSubject ()
          let res = Cibryy.Core.Work cibryyLogger
          Expect.equal 42 res "Should get result back"
          assertWorkMessage (!msg)
          let actual = (!msg).name
          let expected = PointName.parse "Cibryy.Core.Work"
          Expect.equal actual expected "Should have set name"

        yield testCase "end to end with adapter, Log method" <| fun _ ->
          let cibryyLogger, msg = createLoggerSubject ()
          let res = Cibryy.Core.WorkBackpressure cibryyLogger
          Expect.equal 45 res "Should get result back"
          assertWorkMessage (!msg)
          let actual = (!msg).name
          let expected = PointName.parse "Cibryy.Core.WorkBackpressure"
          Expect.equal actual expected "Should have set name"

        yield testCase "end to end with adapter, ErrorWithBP method" <| fun _ ->
          let cibryyLogger, msg = createLoggerSubject ()
          let res = Cibryy.Core.ErrorWithBP cibryyLogger
          Expect.equal 43 res "Should get result back"
          Expect.equal (!msg).level Error "Should have logged at Error level"
          Expect.equal (!msg).value ( "Too simplistic") "Should have logged event template"
          Expect.notEqual (!msg).timestamp 0L "Should have non-zero timestamp"
          Expect.notEqual (!msg).name (PointName [||]) "Should have non-empty point name"
          Expect.equal (!msg).name (PointName [| "Cibryy"; "Core" |])
                       "Should have point name corresponding to the passed logger"
      ]

      testList "global config" [
        let createLogManagerSubject () =
          let msg = ref (Message.event Info "empty")
          stubLogManager msg, msg

        yield testCase "using ILoggerConfig" <| fun _ ->
          let logManager, msg = createLogManagerSubject ()
          let cfgT = System.Type.GetType "Cibryy.Logging.ILoggingConfig, Cibryy"
          Expect.isNotNull cfgT "Should find ILoggingConfig"
          let loggerT = System.Type.GetType "Cibryy.Logging.ILogger, Cibryy"
          Expect.isNotNull loggerT "Should find ILogger"
          let cfg = LogaryFacadeAdapter.createCSharpConfig cfgT loggerT logManager
          Expect.isNotNull cfg "Should return a non-null cfg"
          let ts = cfgT.GetMethod("GetTimestamp").Invoke(cfg, [||]) :?> int64
          Expect.notEqual ts 0L "Should be non-zero"
          let l = cfgT.GetMethod("GetLogger").Invoke(cfg, [| [|"A"; "B"|] |])
          Expect.isNotNull l "Should return a non-null logger"

        yield testCase "initialise with LogManager" <| fun _ ->
          let logManager, msg = createLogManagerSubject ()
          LogaryFacadeAdapter.initialise<Cibryy.Logging.ILogger> logManager
          let res = Cibryy.Core.StaticWork().Result
          Expect.equal res 49 "Should return 49"
          Expect.equal (!msg).level Debug "Should have logged at Debug level"
          Expect.equal (!msg).value ("A debug log") "Should have logged event template"
      ]

    ]
  ]

[<EntryPoint>]
let main argv =
  Tests.runTestsInAssembly defaultConfig argv