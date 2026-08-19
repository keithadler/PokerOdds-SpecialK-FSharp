module PokerOdds.Cli.Program

open System
open System.Diagnostics
open PokerOdds.SpecialK
open FiveCardEvaluator
open SevenCardEvaluator

let private usage =
    """pokerodds - Texas Hold'em hand evaluation and equity

USAGE
  pokerodds equity [options]     Work out each player's share of the pot
  pokerodds rank <cards>         Rank and name a five-, six- or seven-card hand
  pokerodds verify [--full]      Check the evaluators against known results
  pokerodds bench [--seconds N]  Measure evaluation throughput

EQUITY OPTIONS
  --hand <cards>       Two hole cards, e.g. AsKs. Repeat once per known player.
  --opponents <n>      Add n opponents holding random cards.
  --board <cards>      Community cards dealt so far, e.g. 2c7d9h.
  --dead <cards>       Cards out of play, e.g. discards.
  --trials <n>         Monte Carlo trials when enumeration is impractical.
                       Default 200000.
  --seed <n>           Seed for reproducible simulation. Default 0.
  --simulate           Always simulate, even when enumeration would be cheap.
  --max-exact <n>      Enumerate only if at most n boards. Default 10000000.

CARDS
  A rank in 23456789TJQKA followed by a suit in shdc, e.g. As, Td, 7c.
  Several cards may be run together: AsKs, or "As Ks".

EXAMPLES
  pokerodds equity --hand AsKs --hand QdQh
  pokerodds equity --hand AhKd --hand 7c7s --board 2c7d9h
  pokerodds equity --hand AsAd --opponents 5 --trials 500000
  pokerodds rank AsKsQsJsTs
"""

exception private UserError of string

let private fail message = raise (UserError message)

let private parseCards what (text: string) =
    match Card.tryParseMany text with
    | ValueSome cards -> cards
    | ValueNone -> fail (sprintf "Could not read %s from '%s'. Cards look like As, Td or 7c." what text)

let private parseInt what (text: string) =
    match Int64.TryParse text with
    | true, value -> value
    | _ -> fail (sprintf "%s must be a whole number but was '%s'." what text)

// ---------------------------------------------------------------- equity ----

type private EquityOptions =
    { Hands: Card[] list
      Opponents: int
      Board: Card[]
      Dead: Card[]
      Trials: int64
      Seed: int
      Simulate: bool
      MaxExact: int64 }

let private defaultEquityOptions =
    { Hands = []
      Opponents = 0
      Board = [||]
      Dead = [||]
      Trials = 200_000L
      Seed = 0
      Simulate = false
      MaxExact = 10_000_000L }

let rec private readEquityOptions (options: EquityOptions) args =
    match args with
    | [] -> options
    | "--hand" :: value :: rest ->
        readEquityOptions { options with Hands = options.Hands @ [ parseCards "a hand" value ] } rest
    | "--opponents" :: value :: rest ->
        readEquityOptions { options with Opponents = int (parseInt "--opponents" value) } rest
    | "--board" :: value :: rest -> readEquityOptions { options with Board = parseCards "the board" value } rest
    | "--dead" :: value :: rest -> readEquityOptions { options with Dead = parseCards "the dead cards" value } rest
    | "--trials" :: value :: rest -> readEquityOptions { options with Trials = parseInt "--trials" value } rest
    | "--seed" :: value :: rest -> readEquityOptions { options with Seed = int (parseInt "--seed" value) } rest
    | "--max-exact" :: value :: rest ->
        readEquityOptions { options with MaxExact = parseInt "--max-exact" value } rest
    | "--simulate" :: rest -> readEquityOptions { options with Simulate = true } rest
    | flag :: _ when flag.StartsWith "--" -> fail (sprintf "'%s' is not an equity option, or is missing its value." flag)
    | value :: _ -> fail (sprintf "Unexpected argument '%s'. Hole cards go after --hand." value)

let private runEquity args =
    let options = readEquityOptions defaultEquityOptions args

    if options.Opponents < 0 then
        fail "--opponents cannot be negative."

    let hands =
        [| yield! options.Hands
           for _ in 1 .. options.Opponents -> Array.empty<Card> |]

    if hands.Length < 2 then
        fail "Give at least two players, with --hand and/or --opponents."

    let request: EquityRequest =
        { Hands = hands
          Board = options.Board
          Dead = options.Dead }

    let result =
        if options.Simulate then
            Equity.monteCarlo request options.Trials options.Seed
        else
            Equity.compute request options.MaxExact options.Trials options.Seed

    if options.Board.Length > 0 then
        printfn "Board   %s" (Card.formatMany options.Board)

    if options.Dead.Length > 0 then
        printfn "Dead    %s" (Card.formatMany options.Dead)

    printfn ""
    printfn "%-4s  %-10s  %8s  %8s  %8s  %s" "" "Hand" "Equity" "Win" "Tie" "Hand now"
    printfn "%s" (String('-', 66))

    result.Players
    |> Array.iteri (fun i player ->
        let label = sprintf "P%d" (i + 1)
        let hand = if player.Hole.Length = 0 then "random" else Card.formatMany player.Hole

        // With a board out, name the best hand each known holding currently makes.
        let current =
            if player.Hole.Length = 0 || options.Board.Length < 3 then
                ""
            else
                Hand.describe (Array.append player.Hole options.Board)

        printfn
            "%-4s  %-10s  %7.2f%%  %7.2f%%  %7.2f%%  %s"
            label
            hand
            (player.Equity * 100.0)
            (player.WinProbability * 100.0)
            (player.TieProbability * 100.0)
            current)

    printfn ""

    printfn
        "%s over %s %s in %.2fs"
        (if result.Exhaustive then "Exact" else "Simulated")
        (result.Trials.ToString "N0")
        (if result.Exhaustive then (if result.Trials = 1L then "board" else "boards") else "trials")
        result.Elapsed.TotalSeconds

    0

// ------------------------------------------------------------------ rank ----

let private runRank args =
    let cards = args |> String.concat "" |> parseCards "the hand"

    if cards.Length < 5 || cards.Length > 7 then
        fail (sprintf "Give five, six or seven cards; %d were read." cards.Length)

    if (cards |> Array.distinct |> Array.length) <> cards.Length then
        fail "The same card was given more than once."

    let rank = Hand.rank cards
    let best = Hand.bestFive cards

    printfn "Cards    %s" (Card.formatMany cards)
    printfn "Best 5   %s" (Card.formatMany best)
    printfn "Hand     %s" (Hand.describe cards)
    printfn "Category %s" (Hand.categoryName (Hand.categoryOfRank rank))
    printfn "Rank     %d of %d" rank NUMBER_OF_RANKS
    0

// ---------------------------------------------------------------- verify ----

let private runVerify args =
    let full = args |> List.contains "--full"
    let five = FiveEval.Shared
    let mutable failures = 0

    let show (value: obj) =
        match value with
        | :? int as n -> n.ToString "N0"
        | :? int64 as n -> n.ToString "N0"
        | other -> string other

    let check name (expected: obj) (actual: obj) =
        let ok = expected.Equals actual

        if not ok then
            failures <- failures + 1

        printfn "  %-38s %14s  %s" name (show actual) (if ok then "ok" else sprintf "EXPECTED %s" (show expected))

    printfn "Five-card evaluator: all 2,598,960 hands"
    let counts = Array.zeroCreate<int> 9
    let distinct = Array.zeroCreate<bool> (NUMBER_OF_RANKS + 1)

    for a in 0 .. DECK_SIZE - 5 do
        for b in a + 1 .. DECK_SIZE - 4 do
            for c in b + 1 .. DECK_SIZE - 3 do
                for d in c + 1 .. DECK_SIZE - 2 do
                    for e in d + 1 .. DECK_SIZE - 1 do
                        let r = int (five.GetRank(a, b, c, d, e))
                        distinct.[r] <- true
                        let category = int (Hand.categoryOfRank r)
                        counts.[category] <- counts.[category] + 1

    check "distinct ranks" NUMBER_OF_RANKS (distinct |> Array.skip 1 |> Array.filter id |> Array.length)
    check "rank 0 never returned" false distinct.[0]
    check "category boundaries" Hand.Boundaries.StraightFlush five.Boundaries.StraightFlush

    let expected = [| 1302540; 1098240; 123552; 54912; 10200; 5108; 3744; 624; 40 |]

    for i in 0..8 do
        check (Hand.categoryName (enum<HandCategory> i)) expected.[i] counts.[i]

    printfn ""
    let seven = SevenEval.Shared

    if full then
        printfn "Seven-card evaluator: all 133,784,560 hands against the five-card reference"
    else
        printfn "Seven-card evaluator: 2,000,000 sampled hands against the five-card reference (--full for all)"

    let stopwatch = Stopwatch.StartNew()
    let mutable mismatches = 0
    let mutable examined = 0L

    let compare a b c d e f g =
        examined <- examined + 1L

        if seven.GetRank(a, b, c, d, e, f, g) <> five.GetRankFromSeven(a, b, c, d, e, f, g) then
            mismatches <- mismatches + 1

    if full then
        for a in 0 .. DECK_SIZE - 7 do
            for b in a + 1 .. DECK_SIZE - 6 do
                for c in b + 1 .. DECK_SIZE - 5 do
                    for d in c + 1 .. DECK_SIZE - 4 do
                        for e in d + 1 .. DECK_SIZE - 3 do
                            for f in e + 1 .. DECK_SIZE - 2 do
                                for g in f + 1 .. DECK_SIZE - 1 do
                                    compare a b c d e f g
    else
        let random = Random 20260817
        let bag = Array.init DECK_SIZE id

        for _ in 1..2_000_000 do
            for i in 0..6 do
                let j = random.Next(i, DECK_SIZE)
                let t = bag.[i]
                bag.[i] <- bag.[j]
                bag.[j] <- t

            compare bag.[0] bag.[1] bag.[2] bag.[3] bag.[4] bag.[5] bag.[6]

    printfn "  %-38s %14s" "hands examined" (examined.ToString "N0")
    check "mismatches" 0 mismatches
    printfn "  %-38s %13.2fs" "elapsed" stopwatch.Elapsed.TotalSeconds
    printfn ""

    if failures = 0 then
        printfn "All checks passed."
        0
    else
        printfn "%d check(s) failed." failures
        1

// ----------------------------------------------------------------- bench ----

let private runBench args =
    let seconds =
        match args with
        | "--seconds" :: value :: _ -> float (parseInt "--seconds" value)
        | _ -> 2.0

    let five = FiveEval.Shared
    let seven = SevenEval.Shared
    let random = Random 1
    let hands = 200_000

    // One flat array, so the loop below is not really measuring the cost of
    // chasing pointers to 200,000 separate little arrays.
    let deals = Array.zeroCreate<int> (hands * 7)
    let bag = Array.init DECK_SIZE id

    for h in 0 .. hands - 1 do
        for i in 0..6 do
            let j = random.Next(i, DECK_SIZE)
            let t = bag.[i]
            bag.[i] <- bag.[j]
            bag.[j] <- t

        for i in 0..6 do
            deals.[h * 7 + i] <- bag.[i]

    let report name (rounds: int) (elapsed: TimeSpan) (checksum: int64) =
        let rate = float rounds * float hands / elapsed.TotalSeconds
        printfn "  %-28s %14s hands/s   (checksum %d)" name ((int64 rate).ToString "N0") checksum

    printfn "Evaluating for about %.0fs each, %s pre-dealt hands" seconds (hands.ToString "N0")

    // Each loop is written out rather than passed as a function, so the timing is
    // the lookup rather than an indirect call around it.
    let stopwatch = Stopwatch.StartNew()
    let mutable checksum = 0L
    let mutable rounds = 0

    while stopwatch.Elapsed.TotalSeconds < seconds do
        let mutable h = 0

        while h < hands do
            let b = h * 7

            checksum <-
                checksum
                + int64 (seven.GetRank(deals.[b], deals.[b + 1], deals.[b + 2], deals.[b + 3], deals.[b + 4], deals.[b + 5], deals.[b + 6]))

            h <- h + 1

        rounds <- rounds + 1

    report "SevenEval.GetRank" rounds stopwatch.Elapsed checksum

    let stopwatch = Stopwatch.StartNew()
    let mutable checksum = 0L
    let mutable rounds = 0

    while stopwatch.Elapsed.TotalSeconds < seconds do
        let mutable h = 0

        while h < hands do
            let b = h * 7

            checksum <-
                checksum
                + int64 (five.GetRankFromSeven(deals.[b], deals.[b + 1], deals.[b + 2], deals.[b + 3], deals.[b + 4], deals.[b + 5], deals.[b + 6]))

            h <- h + 1

        rounds <- rounds + 1

    report "FiveEval.GetRankFromSeven" rounds stopwatch.Elapsed checksum

    let stopwatch = Stopwatch.StartNew()
    let mutable checksum = 0L
    let mutable rounds = 0

    while stopwatch.Elapsed.TotalSeconds < seconds do
        let mutable h = 0

        while h < hands do
            let b = h * 7

            checksum <-
                checksum
                + int64 (five.GetRank(deals.[b], deals.[b + 1], deals.[b + 2], deals.[b + 3], deals.[b + 4]))

            h <- h + 1

        rounds <- rounds + 1

    report "FiveEval.GetRank" rounds stopwatch.Elapsed checksum

    printfn ""
    printfn "The seven-card rank table holds %s entries (%.1f MB), so throughput here is"
        (seven.RankTableSize.ToString "N0") (float seven.RankTableSize * 2.0 / 1e6)
    printfn "bound by memory. Expect less on a machine with a smaller last-level cache."
    0

// ------------------------------------------------------------------ main ----

[<EntryPoint>]
let main argv =
    try
        match List.ofArray argv with
        | [] | "--help" :: _ | "-h" :: _ | "help" :: _ ->
            printf "%s" usage
            0
        | "--version" :: _ ->
            let version =
                Reflection.Assembly.GetExecutingAssembly().GetName().Version

            printfn "pokerodds %O" version
            0
        | "equity" :: rest -> runEquity rest
        | "rank" :: rest -> runRank rest
        | "verify" :: rest -> runVerify rest
        | "bench" :: rest -> runBench rest
        | command :: _ ->
            eprintfn "Unknown command '%s'. Run 'pokerodds --help'." command
            2
    with
    | UserError message ->
        eprintfn "%s" message
        2
    | :? ArgumentException as e ->
        eprintfn "%s" (e.Message.Split(" (Parameter").[0])
        2
