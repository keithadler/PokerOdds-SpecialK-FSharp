namespace PokerOdds.SpecialK

open FiveCardEvaluator
open SevenCardEvaluator

/// The nine hand categories, ordered weakest to strongest.
type HandCategory =
    | HighCard = 0
    | Pair = 1
    | TwoPair = 2
    | ThreeOfAKind = 3
    | Straight = 4
    | Flush = 5
    | FullHouse = 6
    | FourOfAKind = 7
    | StraightFlush = 8

/// Card-oriented evaluation built on top of the rank tables.
module Hand =

    /// The highest rank belonging to each category. The evaluators assign ranks in
    /// ascending order of strength, so a rank's category is found by its position
    /// against these bounds. `FiveEval.Boundaries` reports the same numbers as
    /// actually built, and the test suite holds the two in agreement.
    module Boundaries =
        [<Literal>]
        let HighCard = 1277
        [<Literal>]
        let Pair = 4137
        [<Literal>]
        let TwoPair = 4995
        [<Literal>]
        let ThreeOfAKind = 5853
        [<Literal>]
        let Straight = 5863
        [<Literal>]
        let Flush = 7140
        [<Literal>]
        let FullHouse = 7296
        [<Literal>]
        let FourOfAKind = 7452
        [<Literal>]
        let StraightFlush = 7462

    /// The category a rank in 1..7462 belongs to.
    let categoryOfRank (rank: int) =
        if rank < 1 || rank > NUMBER_OF_RANKS then
            invalidArg (nameof rank) (sprintf "Rank %d is outside 1..%d." rank NUMBER_OF_RANKS)
        elif rank <= Boundaries.HighCard then HandCategory.HighCard
        elif rank <= Boundaries.Pair then HandCategory.Pair
        elif rank <= Boundaries.TwoPair then HandCategory.TwoPair
        elif rank <= Boundaries.ThreeOfAKind then HandCategory.ThreeOfAKind
        elif rank <= Boundaries.Straight then HandCategory.Straight
        elif rank <= Boundaries.Flush then HandCategory.Flush
        elif rank <= Boundaries.FullHouse then HandCategory.FullHouse
        elif rank <= Boundaries.FourOfAKind then HandCategory.FourOfAKind
        else HandCategory.StraightFlush

    let private categoryNames =
        [| "High card"; "Pair"; "Two pair"; "Three of a kind"; "Straight"
           "Flush"; "Full house"; "Four of a kind"; "Straight flush" |]

    /// The conventional name of a category, e.g. "Full house".
    let categoryName (category: HandCategory) = categoryNames.[int category]

    let private rankSingular =
        [| "two"; "three"; "four"; "five"; "six"; "seven"; "eight"; "nine"
           "ten"; "jack"; "queen"; "king"; "ace" |]

    let private rankPlural =
        [| "twos"; "threes"; "fours"; "fives"; "sixes"; "sevens"; "eights"; "nines"
           "tens"; "jacks"; "queens"; "kings"; "aces" |]

    let private singular (r: Rank) = rankSingular.[int r - 2]
    let private plural (r: Rank) = rankPlural.[int r - 2]

    /// The rank of five, six or seven cards. Cards must be distinct.
    let rank (cards: Card[]) =
        let idx = cards |> Array.map (fun c -> c.Index)

        match idx.Length with
        | 5 -> int (FiveEval.Shared.GetRank(idx.[0], idx.[1], idx.[2], idx.[3], idx.[4]))
        | 7 -> int (SevenEval.Shared.GetRank(idx.[0], idx.[1], idx.[2], idx.[3], idx.[4], idx.[5], idx.[6]))
        | 6 ->
            // Best of the six five-card subsets.
            let five = FiveEval.Shared
            let mutable best = 0us

            let t = Array.zeroCreate<int> 5

            for skip in 0..5 do
                let mutable m = 0

                for k in 0..5 do
                    if k <> skip then
                        t.[m] <- idx.[k]
                        m <- m + 1

                let r = five.GetRank(t.[0], t.[1], t.[2], t.[3], t.[4])

                if r > best then
                    best <- r

            int best
        | n -> invalidArg (nameof cards) (sprintf "Expected five, six or seven cards but got %d." n)

    /// The category of the best five-card hand available in the given cards.
    let category cards = categoryOfRank (rank cards)

    /// The five cards forming the best hand, best card first.
    let bestFive (cards: Card[]) =
        if cards.Length < 5 then
            invalidArg (nameof cards) (sprintf "Expected at least five cards but got %d." cards.Length)

        let five = FiveEval.Shared
        let n = cards.Length
        let mutable best = 0us
        let mutable bestHand = Array.sub cards 0 5

        // Every five-card subset, in index order.
        for a in 0 .. n - 5 do
            for b in a + 1 .. n - 4 do
                for c in b + 1 .. n - 3 do
                    for d in c + 1 .. n - 2 do
                        for e in d + 1 .. n - 1 do
                            let r =
                                five.GetRank(
                                    cards.[a].Index, cards.[b].Index, cards.[c].Index,
                                    cards.[d].Index, cards.[e].Index
                                )

                            if r > best then
                                best <- r
                                bestHand <- [| cards.[a]; cards.[b]; cards.[c]; cards.[d]; cards.[e] |]

        // Best card first, which is the order the descriptions below read in.
        Array.sortInPlaceWith (fun (a: Card) (b: Card) -> compare a.Index b.Index) bestHand
        bestHand

    /// A description of the best hand available, such as "Full house, aces full of kings".
    let describe (cards: Card[]) =
        let hand = bestFive cards
        let cat = categoryOfRank (rank hand)
        let ranks = hand |> Array.map (fun c -> c.Rank)

        // Faces grouped by count then by rank, both descending — the order poker
        // names them in ("aces full of kings", "two pair, aces and kings").
        let groups =
            ranks
            |> Array.countBy id
            |> Array.sortByDescending (fun (r, n) -> n, int r)

        match cat with
        | HandCategory.StraightFlush when ranks.[0] = Rank.Ace && ranks.[1] = Rank.King -> "Royal flush"
        | HandCategory.StraightFlush ->
            // The wheel is ace-low, so its top card is the five.
            let high = if ranks.[0] = Rank.Ace && ranks.[1] = Rank.Five then Rank.Five else ranks.[0]
            sprintf "Straight flush, %s high" (singular high)
        | HandCategory.FourOfAKind -> sprintf "Four of a kind, %s" (plural (fst groups.[0]))
        | HandCategory.FullHouse ->
            sprintf "Full house, %s full of %s" (plural (fst groups.[0])) (plural (fst groups.[1]))
        | HandCategory.Flush -> sprintf "Flush, %s high" (singular ranks.[0])
        | HandCategory.Straight ->
            let high = if ranks.[0] = Rank.Ace && ranks.[1] = Rank.Five then Rank.Five else ranks.[0]
            sprintf "Straight, %s high" (singular high)
        | HandCategory.ThreeOfAKind -> sprintf "Three of a kind, %s" (plural (fst groups.[0]))
        | HandCategory.TwoPair ->
            sprintf "Two pair, %s and %s" (plural (fst groups.[0])) (plural (fst groups.[1]))
        | HandCategory.Pair -> sprintf "Pair of %s" (plural (fst groups.[0]))
        | _ ->
            let name = singular ranks.[0]
            sprintf "%c%s high" (System.Char.ToUpperInvariant name.[0]) (name.Substring 1)
