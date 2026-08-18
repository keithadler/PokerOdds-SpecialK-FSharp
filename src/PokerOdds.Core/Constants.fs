namespace PokerOdds.SpecialK

/// Perfect-hash constants ported from Kenneth J. Shackleton's SKPokerEval
/// (https://github.com/kennethshackleton/SpecialKEval), file src/Constants.h.
///
/// The evaluators work on card indices 0..51 where 0 is the ace of spades and
/// 51 the two of clubs. Two indices of the same residue modulo four share a suit.
///
/// Two independent families of face weights are used:
///
///   * `FIVE_FACE` — weights whose five-element subset sums are distinct, used
///     by the five-card evaluator. Indexed low-to-high (index 0 is a two).
///   * `SEVEN_FACE` — weights whose seven-element multiset sums (multiplicity
///     at most four) are distinct, used by the seven-card evaluator. Indexed
///     high-to-low (index 0 is an ace) so that face index `n` maps to card
///     indices `4n .. 4n+3`.
///
/// Flush weights are one-hot bits, so a set of same-suited faces is identified
/// by the bitwise-or (equivalently the sum, as the bits are distinct) of its
/// weights.
[<AutoOpen>]
module Common =

    [<Literal>]
    let DECK_SIZE = 52
    [<Literal>]
    let NUMBER_OF_SUITS = 4
    [<Literal>]
    let NUMBER_OF_FACES = 13

    // Suit weights. Chosen so that the sum over any seven cards determines the
    // suit multiset uniquely while staying below 512 (see NON_FLUSH_BIT_SHIFT).
    [<Literal>]
    let SPADE = 0
    [<Literal>]
    let HEART = 1
    [<Literal>]
    let DIAMOND = 8
    [<Literal>]
    let CLUB = 57

    // Five-card face weights: every 5-subset sum is distinct.
    [<Literal>]
    let TWO_FIVE = 0u
    [<Literal>]
    let THREE_FIVE = 1u
    [<Literal>]
    let FOUR_FIVE = 5u
    [<Literal>]
    let FIVE_FIVE = 22u
    [<Literal>]
    let SIX_FIVE = 94u
    [<Literal>]
    let SEVEN_FIVE = 312u
    [<Literal>]
    let EIGHT_FIVE = 992u
    [<Literal>]
    let NINE_FIVE = 2422u
    [<Literal>]
    let TEN_FIVE = 5624u
    [<Literal>]
    let JACK_FIVE = 12522u
    [<Literal>]
    let QUEEN_FIVE = 19998u
    [<Literal>]
    let KING_FIVE = 43258u
    [<Literal>]
    let ACE_FIVE = 79415u

    // Flush face weights: one bit per face.
    [<Literal>]
    let TWO_FLUSH = 1u
    [<Literal>]
    let THREE_FLUSH = 2u
    [<Literal>]
    let FOUR_FLUSH = 4u
    [<Literal>]
    let FIVE_FLUSH = 8u
    [<Literal>]
    let SIX_FLUSH = 16u
    [<Literal>]
    let SEVEN_FLUSH = 32u
    [<Literal>]
    let EIGHT_FLUSH = 64u
    [<Literal>]
    let NINE_FLUSH = 128u
    [<Literal>]
    let TEN_FLUSH = 256u
    [<Literal>]
    let JACK_FLUSH = 512u
    [<Literal>]
    let QUEEN_FLUSH = 1024u
    [<Literal>]
    let KING_FLUSH = 2048u
    [<Literal>]
    let ACE_FLUSH = 4096u

    // Seven-card face weights: every 7-multiset sum (multiplicity <= 4) is distinct.
    [<Literal>]
    let TWO = 0
    [<Literal>]
    let THREE = 1
    [<Literal>]
    let FOUR = 5
    [<Literal>]
    let FIVE = 22
    [<Literal>]
    let SIX = 98
    [<Literal>]
    let SEVEN = 453
    [<Literal>]
    let EIGHT = 2031
    [<Literal>]
    let NINE = 8698
    [<Literal>]
    let TEN = 22854
    [<Literal>]
    let JACK = 83661
    [<Literal>]
    let QUEEN = 262349
    [<Literal>]
    let KING = 636345
    [<Literal>]
    let ACE = 1479181

    /// Largest five-card non-flush key: four aces plus a king.
    [<Literal>]
    let MAX_FIVE_NONFLUSH_KEY_INT = 360918u // (4u * ACE_FIVE) + KING_FIVE

    /// Largest five-card flush key: A-K-Q-J-T of one suit.
    [<Literal>]
    let MAX_FIVE_FLUSH_KEY_INT = 7936u // ACE_FLUSH ||| KING_FLUSH ||| QUEEN_FLUSH ||| JACK_FLUSH ||| TEN_FLUSH

    /// Largest seven-card flush key: A-K-Q-J-T-9-8 of one suit.
    [<Literal>]
    let MAX_SEVEN_FLUSH_KEY_INT = 8128u // MAX_FIVE_FLUSH_KEY_INT ||| NINE_FLUSH ||| EIGHT_FLUSH

    /// Largest possible seven-suit-weight sum: seven clubs.
    [<Literal>]
    let MAX_FLUSH_CHECK_SUM = 399 // 7 * CLUB

    /// Seven-card face keys span [3, 7825759] but only 49205 values occur. Folding
    /// once about this circumference keeps them distinct while halving the table.
    [<Literal>]
    let CIRCUMFERENCE_SEVEN = 4565145

    // Sentinels for the flush-check table. Distinct from every suit weight.
    [<Literal>]
    let UNVERIFIED = -2
    [<Literal>]
    let NOT_A_FLUSH = -1

    /// Seven-card keys pack the suit-weight sum into the low bits and the
    /// face-weight sum above them. 2^9 = 512 > MAX_FLUSH_CHECK_SUM, so the two
    /// halves never interfere.
    [<Literal>]
    let SUIT_BIT_MASK = 511UL
    [<Literal>]
    let NON_FLUSH_BIT_SHIFT = 9

    /// The number of distinct five-card hand ranks in Texas Hold'em.
    [<Literal>]
    let NUMBER_OF_RANKS = 7462

    /// Five-card face weights, index 0 = two .. index 12 = ace.
    let FIVE_FACE =
        [| TWO_FIVE; THREE_FIVE; FOUR_FIVE; FIVE_FIVE; SIX_FIVE; SEVEN_FIVE; EIGHT_FIVE
           NINE_FIVE; TEN_FIVE; JACK_FIVE; QUEEN_FIVE; KING_FIVE; ACE_FIVE |]

    /// Flush bit weights, index 0 = two .. index 12 = ace.
    let FIVE_FACE_FLUSH =
        [| TWO_FLUSH; THREE_FLUSH; FOUR_FLUSH; FIVE_FLUSH; SIX_FLUSH; SEVEN_FLUSH; EIGHT_FLUSH
           NINE_FLUSH; TEN_FLUSH; JACK_FLUSH; QUEEN_FLUSH; KING_FLUSH; ACE_FLUSH |]

    /// Seven-card face weights, index 0 = ace .. index 12 = two, matching card order.
    let SEVEN_FACE =
        [| ACE; KING; QUEEN; JACK; TEN; NINE; EIGHT; SEVEN; SIX; FIVE; FOUR; THREE; TWO |]

    /// Flush bit weights, index 0 = ace .. index 12 = two, matching card order.
    let SEVEN_FACE_FLUSH = Array.rev FIVE_FACE_FLUSH

    /// Suit weights indexed by suit ordinal (0 spades, 1 hearts, 2 diamonds, 3 clubs).
    let SUITS = [| SPADE; HEART; DIAMOND; CLUB |]
