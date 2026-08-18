namespace PokerOdds.SpecialK

open FiveCardEvaluator

/// Seven-card evaluator, ported from Kenneth J. Shackleton's SKPokerEval
/// (https://github.com/kennethshackleton/SpecialKEval), file src/SevenEval.h.
///
/// The C++ original ships precomputed tables; this builds the equivalent tables at
/// construction time from the five-card evaluator, which keeps the port readable
/// and self-checking at the cost of a few hundred milliseconds of startup.
module SevenCardEvaluator =

    /// Enumerates every multiset of seven face indices with no face repeated more
    /// than four times — the 49205 face patterns a seven-card hand can show.
    let faceMultisets () =
        let results = ResizeArray<int[]>()
        let current = Array.zeroCreate<int> 7

        let rec walk face slot remaining =
            if remaining = 0 then results.Add(Array.copy current)
            elif face < NUMBER_OF_FACES then
                // Enough faces must remain to place the outstanding cards.
                let maxCopies = min 4 remaining

                for copies in 0..maxCopies do
                    for c in 0 .. copies - 1 do
                        current.[slot + c] <- face

                    walk (face + 1) (slot + copies) (remaining - copies)

        walk 0 0 7
        results

    /// Deals a face multiset into seven distinct cards without creating a flush, so
    /// that evaluating them yields the hand's best *non-flush* five-card rank.
    ///
    /// Copies of a face must take different suits; assigning each group to the
    /// currently least-used suits keeps every suit at ceil(7/4) = 2 cards or fewer,
    /// comfortably short of the five needed for a flush.
    let dealWithoutFlush (faces: int[]) (cards: int[]) =
        let suitUse = Array.zeroCreate<int> NUMBER_OF_SUITS
        let taken = Array.zeroCreate<bool> NUMBER_OF_SUITS
        let mutable i = 0

        while i < 7 do
            let face = faces.[i]
            let mutable copies = 0

            while i + copies < 7 && faces.[i + copies] = face do
                copies <- copies + 1

            System.Array.Clear taken

            for c in 0 .. copies - 1 do
                let mutable suit = -1

                for s in 0 .. NUMBER_OF_SUITS - 1 do
                    if not taken.[s] && (suit < 0 || suitUse.[s] < suitUse.[suit]) then
                        suit <- s

                taken.[suit] <- true
                cards.[i + c] <- (face <<< 2) + suit
                suitUse.[suit] <- suitUse.[suit] + 1

            i <- i + copies

    /// Evaluates a seven-card hand with a single table lookup.
    ///
    /// Cards are indices 0..51: 0 is the ace of spades, 51 the two of clubs.
    /// Higher ranks are better hands; equal ranks tie. Ranks run 1..7462 and are
    /// on the same scale as `FiveEval`.
    ///
    /// Construction fills roughly 9 MB of lookup tables. Instances are immutable
    /// once constructed and safe to share across threads; prefer `SevenEval.Shared`.
    type SevenEval() =

        // Face-key table, folded once about CIRCUMFERENCE_SEVEN to halve its size.
        let rankTable = Array.zeroCreate<uint16> CIRCUMFERENCE_SEVEN
        // Indexed by the bitwise-or of the flush weights of the same-suited cards.
        let flushRankTable = Array.zeroCreate<uint16> (int MAX_SEVEN_FLUSH_KEY_INT + 1)
        // Indexed by the sum of the seven cards' suit weights; holds the suit
        // ordinal of the flush, or NOT_A_FLUSH.
        let flushCheck = Array.create (MAX_FLUSH_CHECK_SUM + 1) (int8 NOT_A_FLUSH)

        // Face weight shifted up, with the suit weight packed into the low nine bits.
        let deckKey = Array.zeroCreate<uint64> DECK_SIZE
        // Flush bit of each card's face.
        let deckFlush = Array.zeroCreate<uint16> DECK_SIZE

        let face = SEVEN_FACE
        let faceFlush = SEVEN_FACE_FLUSH
        let five = FiveEval.Shared

        let setDeckCards () =
            for n in 0 .. NUMBER_OF_FACES - 1 do
                let N = n <<< 2
                let start = uint64 (face.[n] <<< NON_FLUSH_BIT_SHIFT)

                for suit in 0 .. NUMBER_OF_SUITS - 1 do
                    deckKey.[N + suit] <- start + uint64 SUITS.[suit]
                    deckFlush.[N + suit] <- uint16 faceFlush.[n]

        let setNonFlushRanks () =
            let cards = Array.zeroCreate<int> 7

            for faces in faceMultisets () do
                dealWithoutFlush faces cards

                let rank =
                    five.GetRankFromSeven(cards.[0], cards.[1], cards.[2], cards.[3], cards.[4], cards.[5], cards.[6])

                let mutable key = 0

                for f in faces do
                    key <- key + face.[f]

                rankTable.[if key < CIRCUMFERENCE_SEVEN then key else key - CIRCUMFERENCE_SEVEN] <- rank

        // A hand holding five or more cards of one suit can hold neither quads nor a
        // full house, so its flush is always its best five-card hand. That lets each
        // flush table entry be keyed on the suited faces alone.
        let setSevenCardFlushRanks () =
            for i in 6 .. NUMBER_OF_FACES - 1 do
                for j in 5 .. i - 1 do
                    for k in 4 .. j - 1 do
                        for l in 3 .. k - 1 do
                            for m in 2 .. l - 1 do
                                for n in 1 .. m - 1 do
                                    for p in 0 .. n - 1 do
                                        let key =
                                            faceFlush.[i] ||| faceFlush.[j] ||| faceFlush.[k] ||| faceFlush.[l]
                                            ||| faceFlush.[m] ||| faceFlush.[n] ||| faceFlush.[p]

                                        flushRankTable.[int key] <-
                                            five.GetRankFromSeven(
                                                i <<< 2, j <<< 2, k <<< 2, l <<< 2, m <<< 2, n <<< 2, p <<< 2
                                            )

        let setSixCardFlushRanks () =
            for i in 5 .. NUMBER_OF_FACES - 1 do
                for j in 4 .. i - 1 do
                    for k in 3 .. j - 1 do
                        for l in 2 .. k - 1 do
                            for m in 1 .. l - 1 do
                                for n in 0 .. m - 1 do
                                    let key =
                                        faceFlush.[i] ||| faceFlush.[j] ||| faceFlush.[k] ||| faceFlush.[l]
                                        ||| faceFlush.[m] ||| faceFlush.[n]

                                    // Card 51 is the two of clubs: never one of the six spades above,
                                    // and it cannot improve on the flush.
                                    flushRankTable.[int key] <-
                                        five.GetRankFromSeven(i <<< 2, j <<< 2, k <<< 2, l <<< 2, m <<< 2, n <<< 2, 51)

        let setFiveCardFlushRanks () =
            for i in 4 .. NUMBER_OF_FACES - 1 do
                for j in 3 .. i - 1 do
                    for k in 2 .. j - 1 do
                        for l in 1 .. k - 1 do
                            for m in 0 .. l - 1 do
                                let key =
                                    faceFlush.[i] ||| faceFlush.[j] ||| faceFlush.[k] ||| faceFlush.[l]
                                    ||| faceFlush.[m]

                                flushRankTable.[int key] <- five.GetRank(i <<< 2, j <<< 2, k <<< 2, l <<< 2, m <<< 2)

        /// For every way seven cards can split across the suits, record whether some
        /// suit reaches five cards. The suit weights make the sum a unique fingerprint
        /// of that split.
        let setFlushCheck () =
            for spades in 0..7 do
                for hearts in 0 .. 7 - spades do
                    for diamonds in 0 .. 7 - spades - hearts do
                        let clubs = 7 - spades - hearts - diamonds
                        let counts = [| spades; hearts; diamonds; clubs |]

                        let key =
                            spades * SPADE + hearts * HEART + diamonds * DIAMOND + clubs * CLUB

                        flushCheck.[key] <-
                            match Array.tryFindIndex (fun c -> c >= 5) counts with
                            | Some suit -> int8 suit
                            | None -> int8 NOT_A_FLUSH

        do
            setDeckCards ()
            setFlushCheck ()
            setNonFlushRanks ()
            setSevenCardFlushRanks ()
            setSixCardFlushRanks ()
            setFiveCardFlushRanks ()

        /// Kept for compatibility with the original API; the tables are built by the
        /// constructor, so this only reports the number of distinct ranks (7462).
        member _.Initialize() = NUMBER_OF_RANKS

        /// The rank of a seven-card hand. Cards must be distinct indices in 0..51.
        member _.GetRank(c1, c2, c3, c4, c5, c6, c7) =
            let key =
                deckKey.[c1] + deckKey.[c2] + deckKey.[c3] + deckKey.[c4]
                + deckKey.[c5] + deckKey.[c6] + deckKey.[c7]

            let flushSuit = int flushCheck.[int (key &&& SUIT_BIT_MASK)]

            if flushSuit = NOT_A_FLUSH then
                let faceKey = int (key >>> NON_FLUSH_BIT_SHIFT)

                rankTable.[if faceKey < CIRCUMFERENCE_SEVEN then faceKey else faceKey - CIRCUMFERENCE_SEVEN]
            else
                let bit card =
                    if (card &&& 3) = flushSuit then int deckFlush.[card] else 0

                flushRankTable.[bit c1 ||| bit c2 ||| bit c3 ||| bit c4 ||| bit c5 ||| bit c6 ||| bit c7]

        /// A lazily built instance shared across the process.
        static member val Shared = SevenEval() with get
