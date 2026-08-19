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
      /// Time spent on this calculation. The evaluators' one-time table construction
      /// is excluded, so the first call is comparable with later ones.
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

    /// A k-way split is worth 1/k. Looked up rather than divided, because this sits in
    /// the innermost loop and division is far from free.
    let private shareOf = Array.init 64 (fun k -> if k = 0 then 0.0 else 1.0 / float k)

    /// Award one trial: the best rank takes the pot, or splits it evenly. Takes the
    /// running totals as plain arrays so the caller can hoist them out of its loop.
    let inline private settle (ranks: int[]) (wins: int64[]) (ties: int64[]) (equity: float[]) =
        let n = ranks.Length
        let mutable best = 0
        let mutable winners = 0

        for p in 0 .. n - 1 do
            let r = ranks.[p]

            if r > best then
                best <- r
                winners <- 1
            elif r = best then
                winners <- winners + 1

        let share = shareOf.[winners]

        for p in 0 .. n - 1 do
            if ranks.[p] = best then
                if winners = 1 then
                    wins.[p] <- wins.[p] + 1L
                else
                    ties.[p] <- ties.[p] + 1L

                equity.[p] <- equity.[p] + share

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
            invalidArg
                "Hands"
                (sprintf "%d cards still have to be dealt but only %d remain in the deck." needed available)

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

        // Touched before the clock starts: building the tables is a one-time cost and
        // reporting it as part of this calculation makes the first call look slow.
        let seven = SevenEval.Shared
        let stopwatch = Stopwatch.StartNew()
        let n = request.Hands.Length
        let holes = request.Hands |> Array.map (Array.map (fun (c: Card) -> c.Index))
        let known = request.Board |> Array.map (fun c -> c.Index)
        let deck = remainingDeck all
        let need = BOARD_CARDS - known.Length
        let total = Tally(n)

        // A hand's key is the sum of its cards' weights, so the parts that do not vary
        // are summed once here rather than seven times per player per board.
        let cardKey = Array.init DECK_SIZE seven.CardKey
        let holeKeys = holes |> Array.map (fun h -> cardKey.[h.[0]] + cardKey.[h.[1]])
        let knownKey = known |> Array.sumBy (fun c -> cardKey.[c])

        if need = 0 then
            let ranks = Array.zeroCreate<int> n

            for p in 0 .. n - 1 do
                ranks.[p] <-
                    int (
                        seven.GetRankFromKey(
                            holeKeys.[p] + knownKey,
                            holes.[p].[0], holes.[p].[1],
                            known.[0], known.[1], known.[2], known.[3], known.[4]
                        )
                    )

            settle ranks total.Wins total.Ties total.Equity
            total.Trials <- 1L
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
                    // Pulled out of the recursion so the hot loop touches locals only.
                    let wins = tally.Wins
                    let ties = tally.Ties
                    let equity = tally.Equity
                    let mutable trials = 0L

                    let rec choose start slot (boardKey: uint64) =
                        if slot = BOARD_CARDS then
                            for p in 0 .. n - 1 do
                                ranks.[p] <-
                                    int (
                                        seven.GetRankFromKey(
                                            holeKeys.[p] + boardKey,
                                            holes.[p].[0], holes.[p].[1],
                                            board.[0], board.[1], board.[2], board.[3], board.[4]
                                        )
                                    )

                            settle ranks wins ties equity
                            trials <- trials + 1L
                        else
                            for i in start .. deck.Length - (BOARD_CARDS - slot) do
                                let card = deck.[i]
                                board.[slot] <- card
                                choose (i + 1) (slot + 1) (boardKey + cardKey.[card])

                    board.[known.Length] <- deck.[first]
                    choose (first + 1) (known.Length + 1) (knownKey + cardKey.[deck.[first]])
                    tally.Trials <- tally.Trials + trials
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
        let seven = SevenEval.Shared
        let stopwatch = Stopwatch.StartNew()
        let n = request.Hands.Length
        let holes = request.Hands |> Array.map (Array.map (fun (c: Card) -> c.Index))
        let known = request.Board |> Array.map (fun c -> c.Index)
        let deck = remainingDeck all
        let boardNeed = BOARD_CARDS - known.Length
        let unknownPlayers = [| for p in 0 .. n - 1 do if holes.[p].Length = 0 then yield p |]
        let draws = boardNeed + unknownPlayers.Length * HOLE_CARDS
        let cardKey = Array.init DECK_SIZE seven.CardKey
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
                // Known players' hole keys never change; only the dealt ones are redone.
                let holeKeys = Array.zeroCreate<uint64> n
                for p in 0 .. n - 1 do
                    if holes.[p].Length = HOLE_CARDS then
                        holeKeys.[p] <- cardKey.[holes.[p].[0]] + cardKey.[holes.[p].[1]]
                let wins = tally.Wins
                let ties = tally.Ties
                let equity = tally.Equity
                let mutable trials = 0L
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
                        holeKeys.[p] <- cardKey.[bag.[next]] + cardKey.[bag.[next + 1]]
                        next <- next + 2

                    // Summed once, then shared by every player at this board.
                    let mutable boardKey = 0UL

                    for slot in 0 .. BOARD_CARDS - 1 do
                        boardKey <- boardKey + cardKey.[board.[slot]]

                    for p in 0 .. n - 1 do
                        ranks.[p] <-
                            int (
                                seven.GetRankFromKey(
                                    holeKeys.[p] + boardKey,
                                    hands.[p].[0], hands.[p].[1],
                                    board.[0], board.[1], board.[2], board.[3], board.[4]
                                )
                            )

                    settle ranks wins ties equity
                    trials <- trials + 1L

                tally.Trials <- tally.Trials + trials
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
