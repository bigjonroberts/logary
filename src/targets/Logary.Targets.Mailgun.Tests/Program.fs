﻿module Logary.Targets.Mailgun.Tests.Program

open Fuchu

open System
open System.Net.Mail
open Hopac
open Mailgun.Api
open Logary
open Logary.Internals
open Logary.Targets.Mailgun

let emptyRuntime = { serviceName = "tests"; logger = NullLogger() }

let flush = Target.flush >> Job.Ignore >> run

let stop = Target.shutdown >> Job.Ignore >> run

let env k =
  match Environment.GetEnvironmentVariable k with
  | null -> failwithf "couldn't load key %s" k
  | v -> v

let start () =
  let getOpts (domain, line) = { SendOpts.Create domain with testMode = true }

  // You'll have to set the environment var MAILGUN_API_KEY to run this;
  // but besides that, this is the only configuration you need:
  let conf = MailgunLogaryConf.Create(MailAddress "hi@haf.se",
                                      [ MailAddress "doesntexist@haf.se" ],
                                      { apiKey = env "MAILGUN_API_KEY" },
                                      "haf.se",
                                      getOpts = getOpts)

  Target.init emptyRuntime (create conf (PointName.ofSingle "mailgun"))

[<Tests>]
let helloWorld =
  testList "giving it a spin" [
    testCase "initialise" <| fun _ ->
      stop (start ())

    testCase "can send test e-mail" <| fun _ ->
      let target = start ()
      try
        Message.error "hello world"
        |> Target.send target
        |> run
        |> flush
      finally stop target
  ]

[<EntryPoint>]
let main argv =
  Tests.defaultMainThisAssembly argv