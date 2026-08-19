module PokerOdds.Tests.EquityTests

open Xunit
open PokerOdds.SpecialK

let private request hands board =
    { Hands = hands |> List.map Card.parseMany |> List.toArray
      Board = Card.parseMany board
      Dead = [||] }

let private preflop hands =
    { Hands = hands |> List.map Card.parseMany |> List.toArray
      Board = [||]
      Dead = [||] }

/// Reference figures for these match-ups are stable across every hold'em
/// calculator; a percentage point of tolerance covers rounding in the sources.
let private closeTo (expected: float) (actual: float) =
    Assert.True(abs (expected - actual) < 0.01, sprintf "expected about %.4f but got %.4f" expected actual)

[<Fact>]
let ``pocket aces beat pocket kings about four times in five`` () =
    let result = Equity.exact (preflop [ "AsAd"; "KsKd" ])
    Assert.True result.Exhaustive
    closeTo 0.8264 result.Players.[0].Equity
    closeTo 0.1736 result.Players.[1].Equity
    closeTo 0.8236 result.Players.[0].WinProbability
    closeTo 0.0054 result.Players.[0].TieProbability

[<Fact>]
let ``a suited ace-king is a small underdog to pocket queens`` () =
    let result = Equity.exact (preflop [ "AsKs"; "QdQh" ])
    closeTo 0.4619 result.Players.[0].Equity
    closeTo 0.5381 result.Players.[1].Equity

[<Fact>]
let ``the classic coin flip is close to even`` () =
    let result = Equity.exact (preflop [ "AhKd"; "7c7s" ])
    closeTo 0.4461 result.Players.[0].Equity
    closeTo 0.5539 result.Players.[1].Equity

[<Fact>]
let ``equities always sum to one`` () =
    let result = Equity.exact (request [ "AhKd"; "7c7s"; "JsTs" ] "2c7d9h")
    closeTo 1.0 (result.Players |> Array.sumBy (fun p -> p.Equity))

[<Fact>]
let ``identical hands split the pot every time`` () =
    // The same holding in different suits, on a board that gives neither an edge.
    let result = Equity.exact (request [ "AhKd"; "AsKc" ] "2c7d9h")
    closeTo 0.5 result.Players.[0].Equity
    closeTo 0.5 result.Players.[1].Equity
    Assert.Equal(0L, result.Players.[0].Wins)
    Assert.Equal(result.Trials, result.Players.[0].Ties)

[<Fact>]
let ``a drawing dead hand wins nothing`` () =
    // Ace-king cannot beat a set of sevens: no straight, flush or better pair is left.
    let result = Equity.exact (request [ "AhKd"; "7c7s" ] "2c7d9h")
    Assert.Equal(0.0, result.Players.[0].Equity)
    Assert.Equal(1.0, result.Players.[1].Equity)
    Assert.Equal(990L, result.Trials) // C(45,2) turn-and-river combinations

[<Fact>]
let ``a settled board is decided in a single trial`` () =
    let result = Equity.exact (request [ "AhKd"; "7c7s" ] "AdAcKh2s3d")
    Assert.Equal(1L, result.Trials)
    // Aces full of kings beats aces full of sevens.
    Assert.Equal(1.0, result.Players.[0].Equity)
    Assert.Equal(1L, result.Players.[0].Wins)
    Assert.Equal(0.0, result.Players.[1].Equity)

[<Fact>]
let ``dead cards change the odds`` () =
    // Ace-king with a heart draw against a set of sevens.
    let live = request [ "AhKh"; "7c7s" ] "2h7d9h"
    let burnt = { live with Dead = Card.parseMany "3h4h" }

    let before = Equity.exact live
    let after = Equity.exact burnt

    Assert.Equal(990L, before.Trials) // C(45,2)
    Assert.Equal(903L, after.Trials) // C(43,2)
    // Two fewer hearts to hit means less equity for the draw.
    Assert.True(after.Players.[0].Equity < before.Players.[0].Equity)
    closeTo 0.2465 before.Players.[0].Equity
    closeTo 0.1960 after.Players.[0].Equity

[<Fact>]
let ``simulation converges on the exact answer`` () =
    let hands = preflop [ "AsAd"; "KsKd" ]
    let exact = Equity.exact hands
    let simulated = Equity.monteCarlo hands 400_000L 12345

    Assert.False simulated.Exhaustive
    Assert.Equal(400_000L, simulated.Trials)
    Assert.True(abs (exact.Players.[0].Equity - simulated.Players.[0].Equity) < 0.005)

[<Fact>]
let ``a seed reproduces its run exactly`` () =
    let hands = preflop [ "AsAd"; "KsKd"; "7c2h" ]
    let first = Equity.monteCarlo hands 50_000L 99
    let second = Equity.monteCarlo hands 50_000L 99
    let different = Equity.monteCarlo hands 50_000L 100

    Assert.Equal<int64[]>(first.Players |> Array.map (fun p -> p.Wins), second.Players |> Array.map (fun p -> p.Wins))
    Assert.NotEqual<int64[]>(first.Players |> Array.map (fun p -> p.Wins), different.Players |> Array.map (fun p -> p.Wins))

[<Fact>]
let ``random opponents can be simulated`` () =
    let hands =
        { Hands = [| Card.parseMany "AsAd"; [||]; [||] |]
          Board = [||]
          Dead = [||] }

    let result = Equity.monteCarlo hands 100_000L 7
    // Aces against two random hands hold up around two thirds of the time.
    Assert.True(result.Players.[0].Equity > 0.6 && result.Players.[0].Equity < 0.75)
    closeTo 1.0 (result.Players |> Array.sumBy (fun p -> p.Equity))

[<Fact>]
let ``exhaustive enumeration is refused when a hand is unknown`` () =
    let hands =
        { Hands = [| Card.parseMany "AsAd"; [||] |]
          Board = [||]
          Dead = [||] }

    Assert.Equal(None, Equity.exactTrials hands)
    Assert.Throws<System.ArgumentException>(fun () -> Equity.exact hands |> ignore) |> ignore

[<Fact>]
let ``compute enumerates when it is cheap and simulates when it is not`` () =
    let flop = request [ "AsAd"; "KsKd" ] "2c7d9h"
    Assert.True((Equity.compute flop 10_000_000L 1_000L 0).Exhaustive)
    Assert.False((Equity.compute flop 10L 1_000L 0).Exhaustive)

[<Fact>]
let ``the board count is reported before any work is done`` () =
    Assert.Equal(Some 1712304L, Equity.exactTrials (preflop [ "AsKs"; "QdQh" ]))
    Assert.Equal(Some 990L, Equity.exactTrials (request [ "AsKs"; "QdQh" ] "2c7d9h"))
    Assert.Equal(Some 1L, Equity.exactTrials (request [ "AsKs"; "QdQh" ] "2c7d9h4s5c"))

[<Fact>]
let ``fewer than two players is rejected`` () =
    for players in [ 0; 1 ] do
        let hands =
            { Hands = Array.init players (fun _ -> Card.parseMany "AsAd")
              Board = [||]
              Dead = [||] }

        Assert.Throws<System.ArgumentException>(fun () -> Equity.exact hands |> ignore) |> ignore

[<Fact>]
let ``a card cannot be in two places at once`` () =
    Assert.Throws<System.ArgumentException>(fun () -> Equity.exact (preflop [ "AsAd"; "AsKd" ]) |> ignore)
    |> ignore

    Assert.Throws<System.ArgumentException>(fun () -> Equity.exact (request [ "AsAd"; "KsKd" ] "As7d9h") |> ignore)
    |> ignore

[<Fact>]
let ``hole cards must come in pairs`` () =
    let hands =
        { Hands = [| Card.parseMany "AsAdKc"; Card.parseMany "7c2h" |]
          Board = [||]
          Dead = [||] }

    Assert.Throws<System.ArgumentException>(fun () -> Equity.exact hands |> ignore) |> ignore

[<Fact>]
let ``a board longer than five cards is rejected`` () =
    Assert.Throws<System.ArgumentException>(fun () ->
        Equity.exact (request [ "AsAd"; "KsKd" ] "2c7d9h4s5c6d") |> ignore)
    |> ignore

[<Fact>]
let ``the deck must hold enough cards for everyone`` () =
    let hands =
        { Hands = Array.init 24 (fun _ -> [||])
          Board = [||]
          Dead = [||] }

    Assert.Throws<System.ArgumentException>(fun () -> Equity.monteCarlo hands 10L 0 |> ignore) |> ignore

// ---------------------------------------------------------------------------
// The enumeration in `Equity.exact` hoists each hand's key out of the loop and
// carries the board's partial sum down it. These check the answer against a plain
// implementation that does none of that, so the optimisation cannot quietly drift.
// ---------------------------------------------------------------------------

/// Equity worked out the obvious way: walk every board, rank seven cards at a time,
/// split the pot. Deliberately not sharing any code with the implementation.
let private referenceEquity (request: EquityRequest) =
    let seven = SevenCardEvaluator.SevenEval.Shared

    let used =
        [| yield! Array.concat request.Hands; yield! request.Board; yield! request.Dead |]
        |> Array.map (fun (c: Card) -> c.Index)
        |> Set.ofArray

    let deck = [| for i in 0 .. DECK_SIZE - 1 do if not (used.Contains i) then yield i |]
    let holes = request.Hands |> Array.map (Array.map (fun (c: Card) -> c.Index))
    let known = request.Board |> Array.map (fun c -> c.Index)
    let n = request.Hands.Length
    let equity = Array.zeroCreate<float> n
    let board = Array.zeroCreate<int> 5
    Array.blit known 0 board 0 known.Length
    let mutable trials = 0L

    let rec walk start slot =
        if slot = 5 then
            trials <- trials + 1L

            let ranks =
                holes
                |> Array.map (fun h ->
                    int (seven.GetRank(h.[0], h.[1], board.[0], board.[1], board.[2], board.[3], board.[4])))

            let best = Array.max ranks
            let winners = ranks |> Array.filter ((=) best) |> Array.length

            for p in 0 .. n - 1 do
                if ranks.[p] = best then
                    equity.[p] <- equity.[p] + 1.0 / float winners
        else
            for i in start .. deck.Length - (5 - slot) do
                board.[slot] <- deck.[i]
                walk (i + 1) (slot + 1)

    walk 0 known.Length
    equity |> Array.map (fun e -> e / float trials), trials

let private agreesWithReference (request: EquityRequest) =
    let expected, expectedTrials = referenceEquity request
    let actual = Equity.exact request

    Assert.Equal(expectedTrials, actual.Trials)

    for p in 0 .. expected.Length - 1 do
        Assert.True(
            abs (expected.[p] - actual.Players.[p].Equity) < 1e-12,
            sprintf "player %d: reference %.15f, exact %.15f" (p + 1) expected.[p] actual.Players.[p].Equity
        )

[<Fact>]
let ``exact enumeration matches a plain reference preflop`` () =
    agreesWithReference (preflop [ "AsKs"; "QdQh" ])

[<Fact>]
let ``exact enumeration matches a plain reference on a flop`` () =
    agreesWithReference (request [ "AhKd"; "7c7s"; "JsTs" ] "2c7d9h")

[<Fact>]
let ``exact enumeration matches a plain reference on a turn`` () =
    agreesWithReference (request [ "AhKd"; "7c7s"; "JsTs"; "4d4c" ] "2c7d9h8s")

[<Fact>]
let ``exact enumeration matches a plain reference with dead cards`` () =
    agreesWithReference
        { request [ "AhKh"; "7c7s" ] "2h7d9h" with Dead = Card.parseMany "3h4h5hTh" }

[<Fact>]
let ``exact enumeration matches a plain reference for many players`` () =
    agreesWithReference (request [ "AhKd"; "7c7s"; "JsTs"; "4d4c"; "9s9d"; "2h3h" ] "2c7d9h")
