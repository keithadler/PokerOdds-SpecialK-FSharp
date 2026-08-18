namespace PokerOdds.SpecialK

open System
open System.Diagnostics
open System.Threading.Tasks
open SevenCardEvaluator

/// One player's share of the pot.
type PlayerEquity =
    { /// The player's hole cards, or an empty array if they were dealt at random.
      Hole: Card[]
      /// Trials this player won outright.
      Wins: int64
      /// Trials this player shared with at least one other.
      Ties: int64
      /// Share of the pot, counting a k-way tie as 1/k. Equities sum to 1.
      Equity: float
      /// Fraction of trials won outright.
      WinProbability: float
      /// Fraction of trials shared.
      TieProbability: float }

/// The outcome of an equity calculation.
type EquityResult =
    { Players: PlayerEquity[]
      /// Boards enumerated (exhaustive) or hands simulated (Monte Carlo).
      Trials: int64
      /// True when every possible board was enumerated, so the numbers are exact.
      Exhaustive: bool
      Elapsed: TimeSpan }

/// What to work out the equity of.
type EquityRequest =
    { /// Two hole cards per player; an empty array means "deal this player at random".
      Hands: Card[][]
      /// Zero to five community cards already dealt.
      Board: Card[]
      /// Cards removed from the deck without being in play.
      Dead: Card[] }

/// Hold'em equity by exhaustive enumeration or Monte Carlo simulation.
module Equity =

    [<Literal>]
    let private HOLE_CARDS = 2

    [<Literal>]
    let private BOARD_CARDS = 5

    /// Monte Carlo work is split into a fixed number of chunks so that a given seed
    /// reproduces the same answer regardless of how many cores are available.
    [<Literal>]
    let private MONTE_CARLO_CHUNKS = 64

    /// Running totals for one worker.
    type private Tally(playerCount) =
        member val Wins = Array.zeroCreate<int64> playerCount
        member val Ties = Array.zeroCreate<int64> playerCount
        member val Equity = Array.zeroCreate<float> playerCount
        member val Trials = 0L with get, set

        member this.Merge(other: Tally) =
            for p in 0 .. this.Wins.Length - 1 do
                this.Wins.[p] <- this.Wins.[p] + other.Wins.[p]
                this.Ties.[p] <- this.Ties.[p] + other.Ties.[p]
                this.Equity.[p] <- this.Equity.[p] + other.Equity.[p]

            this.Trials <- this.Trials + other.Trials

    /// Award one trial: the best rank takes the pot, or splits it evenly.
    let private score (ranks: int[]) (tally: Tally) =
        let n = ranks.Length
        let mutable best = 0
        let mutable winners = 0

        for p in 0 .. n - 1 do
            if ranks.[p] > best then
                best <- ranks.[p]
                winners <- 1
            elif ranks.[p] = best then
                winners <- winners + 1

        let share = 1.0 / float winners

        for p in 0 .. n - 1 do
            if ranks.[p] = best then
                if winners = 1 then
                    tally.Wins.[p] <- tally.Wins.[p] + 1L
                else
                    tally.Ties.[p] <- tally.Ties.[p] + 1L

                tally.Equity.[p] <- tally.Equity.[p] + share

        tally.Trials <- tally.Trials + 1L

    let private validate (request: EquityRequest) =
        let n = request.Hands.Length

        if n < 2 then
            invalidArg "Hands" "At least two players are needed."

        if request.Board.Length > BOARD_CARDS then
            invalidArg "Board" (sprintf "A board holds at most %d cards but %d were given." BOARD_CARDS request.Board.Length)

        request.Hands
        |> Array.iteri (fun i hand ->
            if hand.Length <> 0 && hand.Length <> HOLE_CARDS then
                invalidArg "Hands" (sprintf "Player %d has %d hole cards; expected %d, or none to deal at random." (i + 1) hand.Length HOLE_CARDS))

        let all =
            [| yield! Array.concat request.Hands
               yield! request.Board
               yield! request.Dead |]

        let duplicate = all |> Array.countBy id |> Array.tryFind (fun (_, c) -> c > 1)

        match duplicate with
        | Some(card, _) -> invalidArg "Hands" (sprintf "The %O appears more than once." card)
        | None -> ()

        let unknown = request.Hands |> Array.sumBy (fun h -> if h.Length = 0 then 1 else 0)
        let needed = unknown * HOLE_CARDS + (BOARD_CARDS - request.Board.Length)
        let available = DECK_SIZE - all.Length

        if needed > available then
            invalidArg "Hands" (sprintf "%d cards must be dealt but only %d remain in the deck." needed available)

        unknown, all

    /// Cards still in the deck, in a stable order.
    let private remainingDeck (used: Card[]) =
        let taken = Array.zeroCreate<bool> DECK_SIZE

        for c in used do
            taken.[c.Index] <- true

        [| for i in 0 .. DECK_SIZE - 1 do
               if not taken.[i] then
                   yield i |]

    let private toResult (request: EquityRequest) (tally: Tally) exhaustive (elapsed: TimeSpan) =
        let trials = float tally.Trials

        { Players =
            request.Hands
            |> Array.mapi (fun p hole ->
                { Hole = hole
                  Wins = tally.Wins.[p]
                  Ties = tally.Ties.[p]
                  Equity = if trials = 0.0 then 0.0 else tally.Equity.[p] / trials
                  WinProbability = if trials = 0.0 then 0.0 else float tally.Wins.[p] / trials
                  TieProbability = if trials = 0.0 then 0.0 else float tally.Ties.[p] / trials })
          Trials = tally.Trials
          Exhaustive = exhaustive
          Elapsed = elapsed }

    /// The number of boards `exact` would enumerate, or None when some player's hole
    /// cards are unknown and exhaustive enumeration is not possible.
    let exactTrials (request: EquityRequest) =
        let unknown, all = validate request

        if unknown > 0 then
            None
        else
            let deck = DECK_SIZE - all.Length
            let need = BOARD_CARDS - request.Board.Length

            let mutable total = 1L

            for i in 1..need do
                total <- total * int64 (deck - need + i) / int64 i

            Some total

    /// Enumerate every remaining board exactly once. Requires all hole cards to be known.
    let exact (request: EquityRequest) =
        let unknown, all = validate request

        if unknown > 0 then
            invalidArg "Hands" "Exhaustive enumeration needs every player's hole cards; use monteCarlo instead."

        let stopwatch = Stopwatch.StartNew()
        let n = request.Hands.Length
        let holes = request.Hands |> Array.map (Array.map (fun (c: Card) -> c.Index))
        let known = request.Board |> Array.map (fun c -> c.Index)
        let deck = remainingDeck all
        let need = BOARD_CARDS - known.Length
        let seven = SevenEval.Shared
        let total = Tally(n)

        let evaluateBoard (board: int[]) (ranks: int[]) (tally: Tally) =
            for p in 0 .. n - 1 do
                ranks.[p] <-
                    int (seven.GetRank(holes.[p].[0], holes.[p].[1], board.[0], board.[1], board.[2], board.[3], board.[4]))

            score ranks tally

        if need = 0 then
            evaluateBoard known (Array.zeroCreate n) total
        else
            // Partition on the first card dealt so the work parallelises cleanly.
            let partitions = deck.Length - need + 1

            Parallel.For(
                0,
                partitions,
                (fun () -> Tally(n)),
                (fun first _ (tally: Tally) ->
                    let board = Array.zeroCreate<int> BOARD_CARDS
                    Array.blit known 0 board 0 known.Length
                    let ranks = Array.zeroCreate<int> n

                    let rec choose start slot =
                        if slot = BOARD_CARDS then
                            evaluateBoard board ranks tally
                        else
                            for i in start .. deck.Length - (BOARD_CARDS - slot) do
                                board.[slot] <- deck.[i]
                                choose (i + 1) (slot + 1)

                    board.[known.Length] <- deck.[first]
                    choose (first + 1) (known.Length + 1)
                    tally),
                (fun tally -> lock total (fun () -> total.Merge tally))
            )
            |> ignore

        toResult request total true stopwatch.Elapsed

    /// Simulate `trials` random run-outs. Unknown hole cards are dealt each trial.
    /// The same `seed` gives the same answer on any machine.
    let monteCarlo (request: EquityRequest) (trials: int64) (seed: int) =
        if trials <= 0L then
            invalidArg (nameof trials) "At least one trial is needed."

        let _, all = validate request
        let stopwatch = Stopwatch.StartNew()
        let n = request.Hands.Length
        let holes = request.Hands |> Array.map (Array.map (fun (c: Card) -> c.Index))
        let known = request.Board |> Array.map (fun c -> c.Index)
        let deck = remainingDeck all
        let boardNeed = BOARD_CARDS - known.Length
        let unknownPlayers = [| for p in 0 .. n - 1 do if holes.[p].Length = 0 then yield p |]
        let draws = boardNeed + unknownPlayers.Length * HOLE_CARDS
        let seven = SevenEval.Shared
        let total = Tally(n)

        let chunks = int (min (int64 MONTE_CARLO_CHUNKS) trials)
        let baseTrials = trials / int64 chunks
        let remainder = trials % int64 chunks

        Parallel.For(
            0,
            chunks,
            (fun () -> Tally(n)),
            (fun chunk _ (tally: Tally) ->
                // Deriving each chunk's seed from the caller's keeps runs reproducible
                // whatever the scheduler does.
                let random = Random(HashCode.Combine(seed, chunk))
                let bag = Array.copy deck
                let board = Array.zeroCreate<int> BOARD_CARDS
                Array.blit known 0 board 0 known.Length
                let hands = Array.map Array.copy holes
                for p in unknownPlayers do
                    hands.[p] <- Array.zeroCreate HOLE_CARDS
                let ranks = Array.zeroCreate<int> n
                let count = baseTrials + (if int64 chunk < remainder then 1L else 0L)

                for _ in 1L .. count do
                    // Partial Fisher-Yates: only the cards actually needed are drawn.
                    for i in 0 .. draws - 1 do
                        let j = random.Next(i, bag.Length)
                        let t = bag.[i]
                        bag.[i] <- bag.[j]
                        bag.[j] <- t

                    let mutable next = 0

                    for slot in known.Length .. BOARD_CARDS - 1 do
                        board.[slot] <- bag.[next]
                        next <- next + 1

                    for p in unknownPlayers do
                        hands.[p].[0] <- bag.[next]
                        hands.[p].[1] <- bag.[next + 1]
                        next <- next + 2

                    for p in 0 .. n - 1 do
                        ranks.[p] <-
                            int (
                                seven.GetRank(
                                    hands.[p].[0], hands.[p].[1], board.[0], board.[1], board.[2], board.[3], board.[4]
                                )
                            )

                    score ranks tally

                tally),
            (fun tally -> lock total (fun () -> total.Merge tally))
        )
        |> ignore

        toResult request total false stopwatch.Elapsed

    /// Work out equities exactly when that is cheap enough, otherwise simulate.
    /// `maxExactTrials` caps how many boards exhaustive enumeration may visit.
    let compute (request: EquityRequest) (maxExactTrials: int64) (trials: int64) (seed: int) =
        match exactTrials request with
        | Some total when total <= maxExactTrials -> exact request
        | _ -> monteCarlo request trials seed
