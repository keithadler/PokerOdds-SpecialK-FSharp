namespace PokerOdds.SpecialK

/// Five-card evaluator, ported from Kenneth J. Shackleton's SKPokerEval
/// (https://github.com/kennethshackleton/SpecialKEval), files tests/FiveEval.{h,cpp}.
module FiveCardEvaluator =

    /// Where each hand category ends on the 1..7462 rank scale, in the order the
    /// tables are built. Recorded during construction so it can be checked against
    /// the published constants rather than trusted.
    type RankBoundaries =
        { HighCard: int
          Pair: int
          TwoPair: int
          ThreeOfAKind: int
          Straight: int
          Flush: int
          FullHouse: int
          FourOfAKind: int
          StraightFlush: int }

    /// True when faces i > j > k > l > m form a straight. `i - m = 4` catches the run
    /// of five consecutive faces; the second test catches the ace-low wheel (A-5-4-3-2).
    let inline private isStraight i j m = (i - m = 4) || (i = 12 && j = 3)

    /// Evaluates five-card hands by table lookup, and seven-card hands by taking
    /// the best of their twenty-one five-card subsets.
    ///
    /// Cards are indices 0..51: 0 is the ace of spades, 51 the two of clubs.
    /// Higher ranks are better hands; equal ranks tie. Ranks run 1..7462.
    ///
    /// Construction fills roughly 0.7 MB of lookup tables. Instances are immutable
    /// once constructed and safe to share across threads; prefer `FiveEval.Shared`.
    type FiveEval() =

        // Indexed by the sum of the five cards' face weights.
        let rankTable = Array.zeroCreate<uint16> (int MAX_FIVE_NONFLUSH_KEY_INT + 1)
        // Indexed by the bitwise-or of the five cards' flush weights.
        let flushRankTable = Array.zeroCreate<uint16> (int MAX_FIVE_FLUSH_KEY_INT + 1)

        let deckFace = Array.zeroCreate<uint32> DECK_SIZE
        let deckFlush = Array.zeroCreate<uint16> DECK_SIZE
        let deckSuit = Array.zeroCreate<uint16> DECK_SIZE

        let face = FIVE_FACE
        let faceFlush = FIVE_FACE_FLUSH

        // Face weights run two-to-ace but cards run ace-to-two, hence face.[12-n].
        let setDeckCards () =
            for n in 0 .. NUMBER_OF_FACES - 1 do
                let N = n <<< 2
                let faceWeight = face.[12 - n]
                let flushWeight = uint16 faceFlush.[12 - n]

                for suit in 0 .. NUMBER_OF_SUITS - 1 do
                    deckSuit.[N + suit] <- uint16 SUITS.[suit]
                    deckFace.[N + suit] <- faceWeight
                    deckFlush.[N + suit] <- flushWeight

        let setHighCardRanks counter =
            let mutable n = counter

            for i in 5 .. NUMBER_OF_FACES - 1 do
                for j in 3 .. i - 1 do
                    for k in 2 .. j - 1 do
                        for l in 1 .. k - 1 do
                            for m in 0 .. l - 1 do
                                if not (isStraight i j m) then
                                    rankTable.[int (face.[i] + face.[j] + face.[k] + face.[l] + face.[m])] <- n
                                    n <- n + 1us

            n

        let setPairRanks counter =
            let mutable n = counter

            for i in 0 .. NUMBER_OF_FACES - 1 do
                for j in 2 .. NUMBER_OF_FACES - 1 do
                    for k in 1 .. j - 1 do
                        for l in 0 .. k - 1 do
                            if i <> j && i <> k && i <> l then
                                rankTable.[int ((face.[i] <<< 1) + face.[j] + face.[k] + face.[l])] <- n
                                n <- n + 1us

            n

        let setTwoPairRanks counter =
            let mutable n = counter

            for i in 1 .. NUMBER_OF_FACES - 1 do
                for j in 0 .. i - 1 do
                    for k in 0 .. NUMBER_OF_FACES - 1 do
                        // Excluding k = i and k = j keeps full houses out.
                        if k <> i && k <> j then
                            rankTable.[int ((face.[i] <<< 1) + (face.[j] <<< 1) + face.[k])] <- n
                            n <- n + 1us

            n

        let setThreeOfAKindRanks counter =
            let mutable n = counter

            for i in 0 .. NUMBER_OF_FACES - 1 do
                for j in 1 .. NUMBER_OF_FACES - 1 do
                    for k in 0 .. j - 1 do
                        // Excluding i = j and i = k keeps quads out.
                        if i <> j && i <> k then
                            rankTable.[int ((3u * face.[i]) + face.[j] + face.[k])] <- n
                            n <- n + 1us

            n

        let setLowStraightNonFlushRanks counter =
            rankTable.[int (face.[12] + face.[0] + face.[1] + face.[2] + face.[3])] <- counter
            counter + 1us

        let setStraightNonFlushRanks counter =
            let mutable n = counter

            for i in 0 .. 8 do
                rankTable.[int (face.[i] + face.[i + 1] + face.[i + 2] + face.[i + 3] + face.[i + 4])] <- n
                n <- n + 1us

            n

        let setFlushNotStraightRanks counter =
            let mutable n = counter

            for i in 5 .. NUMBER_OF_FACES - 1 do
                for j in 3 .. i - 1 do
                    for k in 2 .. j - 1 do
                        for l in 1 .. k - 1 do
                            for m in 0 .. l - 1 do
                                if not (isStraight i j m) then
                                    flushRankTable.[int (
                                        faceFlush.[i] ||| faceFlush.[j] ||| faceFlush.[k]
                                        ||| faceFlush.[l] ||| faceFlush.[m]
                                    )] <- n

                                    n <- n + 1us

            n

        let setFullHouseRanks counter =
            let mutable n = counter

            for i in 0 .. NUMBER_OF_FACES - 1 do
                for j in 0 .. NUMBER_OF_FACES - 1 do
                    if i <> j then
                        rankTable.[int ((3u * face.[i]) + (face.[j] <<< 1))] <- n
                        n <- n + 1us

            n

        let setFourOfAKindRanks counter =
            let mutable n = counter

            for i in 0 .. NUMBER_OF_FACES - 1 do
                for j in 0 .. NUMBER_OF_FACES - 1 do
                    if i <> j then
                        rankTable.[int ((face.[i] <<< 2) + face.[j])] <- n
                        n <- n + 1us

            n

        let setLowStraightFlushRanks counter =
            flushRankTable.[int (
                faceFlush.[0] ||| faceFlush.[1] ||| faceFlush.[2] ||| faceFlush.[3] ||| faceFlush.[12]
            )] <- counter

            counter + 1us

        let setUsualStraightFlushRanks counter =
            let mutable n = counter

            for i in 0 .. 8 do
                flushRankTable.[int (
                    faceFlush.[i] ||| faceFlush.[i + 1] ||| faceFlush.[i + 2]
                    ||| faceFlush.[i + 3] ||| faceFlush.[i + 4]
                )] <- n

                n <- n + 1us

            n

        // Ranks are assigned in ascending order of hand strength, so the counter
        // after each category is that category's upper bound.
        let boundaries =
            setDeckCards ()
            // Rank 0 is reserved to mean "no hand", so counting starts at one.
            let highCard = setHighCardRanks 1us
            let pair = setPairRanks highCard
            let twoPair = setTwoPairRanks pair
            let trips = setThreeOfAKindRanks twoPair
            let straight = setLowStraightNonFlushRanks trips |> setStraightNonFlushRanks
            let flush = setFlushNotStraightRanks straight
            let fullHouse = setFullHouseRanks flush
            let quads = setFourOfAKindRanks fullHouse
            let straightFlush = setLowStraightFlushRanks quads |> setUsualStraightFlushRanks

            { HighCard = int highCard - 1
              Pair = int pair - 1
              TwoPair = int twoPair - 1
              ThreeOfAKind = int trips - 1
              Straight = int straight - 1
              Flush = int flush - 1
              FullHouse = int fullHouse - 1
              FourOfAKind = int quads - 1
              StraightFlush = int straightFlush - 1 }

        /// The rank at which each hand category ends, as computed while building the tables.
        member _.Boundaries = boundaries

        /// Kept for compatibility with the original API; the tables are built by the
        /// constructor, so this only reports the number of distinct ranks (7462).
        member _.Initialize() = boundaries.StraightFlush

        /// The rank of a five-card hand. Cards must be distinct indices in 0..51.
        member _.GetRank(cardOne, cardTwo, cardThree, cardFour, cardFive) =
            if
                deckSuit.[cardOne] = deckSuit.[cardTwo]
                && deckSuit.[cardOne] = deckSuit.[cardThree]
                && deckSuit.[cardOne] = deckSuit.[cardFour]
                && deckSuit.[cardOne] = deckSuit.[cardFive]
            then
                flushRankTable.[int (
                    deckFlush.[cardOne] ||| deckFlush.[cardTwo] ||| deckFlush.[cardThree]
                    ||| deckFlush.[cardFour] ||| deckFlush.[cardFive]
                )]
            else
                rankTable.[int (
                    deckFace.[cardOne] + deckFace.[cardTwo] + deckFace.[cardThree]
                    + deckFace.[cardFour] + deckFace.[cardFive]
                )]

        /// The rank of the best five-card hand contained in seven cards, found by
        /// trying all twenty-one subsets. `SevenEval` does this in one lookup and
        /// should be preferred; this is the reference the seven-card tables are built from.
        member this.GetRankFromSeven(c1, c2, c3, c4, c5, c6, c7) =
            let cards = [| c1; c2; c3; c4; c5; c6; c7 |]
            let temp = Array.zeroCreate<int> 5
            let mutable best = 0us

            // i and j index the two cards left out.
            for i in 1 .. 6 do
                for j in 0 .. i - 1 do
                    let mutable m = 0

                    for k in 0 .. 6 do
                        if k <> i && k <> j then
                            temp.[m] <- cards.[k]
                            m <- m + 1

                    let current = this.GetRank(temp.[0], temp.[1], temp.[2], temp.[3], temp.[4])

                    if best < current then
                        best <- current

            best

        /// A lazily built instance shared across the process.
        static member val Shared = FiveEval() with get
