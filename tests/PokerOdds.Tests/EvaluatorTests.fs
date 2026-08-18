module PokerOdds.Tests.EvaluatorTests

open System
open Xunit
open PokerOdds.SpecialK
open FiveCardEvaluator
open SevenCardEvaluator

let private five = FiveEval.Shared
let private seven = SevenEval.Shared

/// Set POKERODDS_EXHAUSTIVE=1 to cross-check all 133,784,560 seven-card hands
/// instead of a random sample. CI does this on release builds.
let private exhaustive =
    Environment.GetEnvironmentVariable "POKERODDS_EXHAUSTIVE" = "1"

let private rank5 (text: string) =
    let c = Card.parseMany text
    Assert.Equal(5, c.Length)
    int (five.GetRank(c.[0].Index, c.[1].Index, c.[2].Index, c.[3].Index, c.[4].Index))

// ------------------------------------------------------------- five-card ----

[<Fact>]
let ``ranks run from one to 7462`` () =
    Assert.Equal(1, rank5 "7c5d4h3s2c") // the worst hand in hold'em
    Assert.Equal(NUMBER_OF_RANKS, rank5 "AsKsQsJsTs") // the best

[<Fact>]
let ``every five-card hand is ranked and all 7462 ranks occur`` () =
    let seen = Array.zeroCreate<bool> (NUMBER_OF_RANKS + 1)

    for a in 0 .. DECK_SIZE - 5 do
        for b in a + 1 .. DECK_SIZE - 4 do
            for c in b + 1 .. DECK_SIZE - 3 do
                for d in c + 1 .. DECK_SIZE - 2 do
                    for e in d + 1 .. DECK_SIZE - 1 do
                        seen.[int (five.GetRank(a, b, c, d, e))] <- true

    Assert.False(seen.[0], "rank 0 means 'no hand' and must never be returned")
    Assert.Equal(NUMBER_OF_RANKS, seen |> Array.skip 1 |> Array.filter id |> Array.length)

[<Fact>]
let ``the number of hands in each category matches the textbook counts`` () =
    let counts = Array.zeroCreate<int> 9

    for a in 0 .. DECK_SIZE - 5 do
        for b in a + 1 .. DECK_SIZE - 4 do
            for c in b + 1 .. DECK_SIZE - 3 do
                for d in c + 1 .. DECK_SIZE - 2 do
                    for e in d + 1 .. DECK_SIZE - 1 do
                        let category = int (Hand.categoryOfRank (int (five.GetRank(a, b, c, d, e))))
                        counts.[category] <- counts.[category] + 1

    Assert.Equal<int[]>([| 1302540; 1098240; 123552; 54912; 10200; 5108; 3744; 624; 40 |], counts)
    Assert.Equal(2598960, Array.sum counts)

[<Fact>]
let ``the published category boundaries are the ones the tables were built with`` () =
    let built = five.Boundaries
    Assert.Equal(Hand.Boundaries.HighCard, built.HighCard)
    Assert.Equal(Hand.Boundaries.Pair, built.Pair)
    Assert.Equal(Hand.Boundaries.TwoPair, built.TwoPair)
    Assert.Equal(Hand.Boundaries.ThreeOfAKind, built.ThreeOfAKind)
    Assert.Equal(Hand.Boundaries.Straight, built.Straight)
    Assert.Equal(Hand.Boundaries.Flush, built.Flush)
    Assert.Equal(Hand.Boundaries.FullHouse, built.FullHouse)
    Assert.Equal(Hand.Boundaries.FourOfAKind, built.FourOfAKind)
    Assert.Equal(Hand.Boundaries.StraightFlush, built.StraightFlush)
    Assert.Equal(NUMBER_OF_RANKS, built.StraightFlush)

[<Fact>]
let ``the categories rank in the conventional order`` () =
    let ordered =
        [ "7c5d4h3s2c" // high card
          "2c2d7h5s4c" // pair
          "2c2d3h3s4c" // two pair
          "2c2d2h5s4c" // trips
          "6c5d4h3s2c" // straight
          "Ac9c7c5c3c" // flush
          "2c2d2h3s3c" // full house
          "2c2d2h2s3c" // quads
          "6c5c4c3c2c" ] // straight flush
        |> List.map rank5

    Assert.Equal<int list>(List.sort ordered, ordered)
    Assert.Equal(9, ordered |> List.map Hand.categoryOfRank |> List.distinct |> List.length)

[<Fact>]
let ``the wheel is the lowest straight and a nine-high straight beats it`` () =
    Assert.True(rank5 "5c4d3h2sAc" < rank5 "6c5d4h3s2c")
    Assert.Equal(HandCategory.Straight, Hand.categoryOfRank (rank5 "5c4d3h2sAc"))

[<Fact>]
let ``an ace-high flush beats a king-high flush and loses to a full house`` () =
    Assert.True(rank5 "Ac9c7c5c3c" > rank5 "Kc9c7c5c3c")
    Assert.True(rank5 "Ac9c7c5c3c" < rank5 "2c2d2h3s3c")

[<Fact>]
let ``suits are of equal value`` () =
    for suit in 0..3 do
        let card n = Card((n <<< 2) + suit)
        let royal = [| card 0; card 1; card 2; card 3; card 4 |] // A-K-Q-J-T of one suit
        Assert.Equal(NUMBER_OF_RANKS, Hand.rank royal)

// ------------------------------------------------------------ seven-card ----

[<Fact>]
let ``seven-card lookups agree with the best of the twenty-one five-card subsets`` () =
    let mutable examined = 0L
    let mutable mismatches = 0

    let check a b c d e f g =
        examined <- examined + 1L

        if seven.GetRank(a, b, c, d, e, f, g) <> five.GetRankFromSeven(a, b, c, d, e, f, g) then
            mismatches <- mismatches + 1

    if exhaustive then
        for a in 0 .. DECK_SIZE - 7 do
            for b in a + 1 .. DECK_SIZE - 6 do
                for c in b + 1 .. DECK_SIZE - 5 do
                    for d in c + 1 .. DECK_SIZE - 4 do
                        for e in d + 1 .. DECK_SIZE - 3 do
                            for f in e + 1 .. DECK_SIZE - 2 do
                                for g in f + 1 .. DECK_SIZE - 1 do
                                    check a b c d e f g
    else
        let random = Random 20260817
        let bag = Array.init DECK_SIZE id

        for _ in 1..500_000 do
            for i in 0..6 do
                let j = random.Next(i, DECK_SIZE)
                let t = bag.[i]
                bag.[i] <- bag.[j]
                bag.[j] <- t

            check bag.[0] bag.[1] bag.[2] bag.[3] bag.[4] bag.[5] bag.[6]

    Assert.Equal(0, mismatches)
    Assert.Equal((if exhaustive then 133784560L else 500_000L), examined)

[<Fact>]
let ``the seven-card evaluator finds the flush that beats a lower straight`` () =
    // Ac Kc on Qc Jc 2c: a club flush, not the ace-high straight the faces suggest.
    let cards = Card.parseMany "AcKcQcJc2c9d8h"
    Assert.Equal(HandCategory.Flush, Hand.category cards)

[<Fact>]
let ``a full house beats a flush draw that never completes`` () =
    let cards = Card.parseMany "AcAdAh KcKd 7c 2c"
    Assert.Equal(HandCategory.FullHouse, Hand.category cards)

// --------------------------------------------------- table-building rules ----

[<Fact>]
let ``there are 49205 seven-card face patterns and each has a distinct key`` () =
    let multisets = SevenCardEvaluator.faceMultisets ()
    Assert.Equal(49205, multisets.Count)

    let keys =
        multisets |> Seq.map (Array.sumBy (fun f -> SEVEN_FACE.[f])) |> Seq.toArray

    Assert.Equal(keys.Length, (Set.ofArray keys).Count)

    // Folding once about the circumference must keep the keys apart.
    let folded =
        keys |> Array.map (fun k -> if k < CIRCUMFERENCE_SEVEN then k else k - CIRCUMFERENCE_SEVEN)

    Assert.Equal(folded.Length, (Set.ofArray folded).Count)
    Assert.True(Array.max folded < CIRCUMFERENCE_SEVEN)

[<Fact>]
let ``every face pattern deals into seven distinct cards with no flush`` () =
    let cards = Array.zeroCreate<int> 7

    for faces in SevenCardEvaluator.faceMultisets () do
        SevenCardEvaluator.dealWithoutFlush faces cards

        Assert.Equal(7, (Set.ofArray cards).Count)
        Assert.Equal<int[]>(faces, cards |> Array.map (fun c -> c >>> 2))

        let perSuit = Array.zeroCreate<int> NUMBER_OF_SUITS

        for c in cards do
            perSuit.[c &&& 3] <- perSuit.[c &&& 3] + 1

        // Five of a suit would make the hand a flush and spoil the non-flush table.
        Assert.True(Array.max perSuit <= 4, sprintf "faces %A produced a flush" faces)

[<Fact>]
let ``the seven suit-weight sums are distinct for every split of the suits`` () =
    let keys =
        [| for spades in 0..7 do
               for hearts in 0 .. 7 - spades do
                   for diamonds in 0 .. 7 - spades - hearts do
                       let clubs = 7 - spades - hearts - diamonds
                       yield spades * SPADE + hearts * HEART + diamonds * DIAMOND + clubs * CLUB |]

    Assert.Equal(keys.Length, (Set.ofArray keys).Count)
    Assert.True(Array.max keys <= MAX_FLUSH_CHECK_SUM)
    Assert.True(MAX_FLUSH_CHECK_SUM < (1 <<< NON_FLUSH_BIT_SHIFT))

[<Fact>]
let ``Initialize stays available and reports the rank count`` () =
    Assert.Equal(NUMBER_OF_RANKS, five.Initialize())
    Assert.Equal(NUMBER_OF_RANKS, seven.Initialize())
