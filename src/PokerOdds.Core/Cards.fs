namespace PokerOdds.SpecialK

open System

/// The four suits, ordered as the evaluators index them.
type Suit =
    | Spades = 0
    | Hearts = 1
    | Diamonds = 2
    | Clubs = 3

/// Card faces, valued so that comparison matches poker ordering.
type Rank =
    | Two = 2
    | Three = 3
    | Four = 4
    | Five = 5
    | Six = 6
    | Seven = 7
    | Eight = 8
    | Nine = 9
    | Ten = 10
    | Jack = 11
    | Queen = 12
    | King = 13
    | Ace = 14

/// A single card, stored as the SKPokerEval deck index: 0 is the ace of spades
/// and 51 the two of clubs. Cards sharing a suit are congruent modulo four.
[<Struct; CustomEquality; CustomComparison>]
type Card =
    val Index: int

    new(index: int) =
        if index < 0 || index >= DECK_SIZE then
            raise (ArgumentOutOfRangeException(nameof index, index, "Card index must be in 0..51."))
        { Index = index }

    member this.Rank = enum<Rank> (14 - (this.Index >>> 2))
    member this.Suit = enum<Suit> (this.Index &&& 3)

    override this.ToString() =
        let r =
            match this.Rank with
            | Rank.Ten -> "T"
            | Rank.Jack -> "J"
            | Rank.Queen -> "Q"
            | Rank.King -> "K"
            | Rank.Ace -> "A"
            | other -> string (int other)

        let s =
            match this.Suit with
            | Suit.Spades -> "s"
            | Suit.Hearts -> "h"
            | Suit.Diamonds -> "d"
            | _ -> "c"

        r + s

    override this.Equals(other) =
        match other with
        | :? Card as c -> c.Index = this.Index
        | _ -> false

    override this.GetHashCode() = this.Index

    interface IComparable<Card> with
        // Better cards compare greater, so the natural order is the poker order.
        member this.CompareTo(other) = compare other.Index this.Index

    interface IComparable with
        member this.CompareTo(other) =
            match other with
            | :? Card as c -> compare c.Index this.Index
            | _ -> invalidArg (nameof other) "Cannot compare a Card to a different type."

/// Construction, parsing and formatting of cards.
module Card =

    /// The card of the given rank and suit.
    let create (rank: Rank) (suit: Suit) = Card((14 - int rank) * 4 + int suit)

    /// All 52 cards, best first (ace of spades .. two of clubs).
    let deck = Array.init DECK_SIZE Card

    let private rankOfChar c =
        match Char.ToUpperInvariant c with
        | '2' -> ValueSome Rank.Two
        | '3' -> ValueSome Rank.Three
        | '4' -> ValueSome Rank.Four
        | '5' -> ValueSome Rank.Five
        | '6' -> ValueSome Rank.Six
        | '7' -> ValueSome Rank.Seven
        | '8' -> ValueSome Rank.Eight
        | '9' -> ValueSome Rank.Nine
        | 'T' -> ValueSome Rank.Ten
        | 'J' -> ValueSome Rank.Jack
        | 'Q' -> ValueSome Rank.Queen
        | 'K' -> ValueSome Rank.King
        | 'A' -> ValueSome Rank.Ace
        | _ -> ValueNone

    let private suitOfChar c =
        match Char.ToLowerInvariant c with
        | 's' | '♠' -> ValueSome Suit.Spades
        | 'h' | '♥' -> ValueSome Suit.Hearts
        | 'd' | '♦' -> ValueSome Suit.Diamonds
        | 'c' | '♣' -> ValueSome Suit.Clubs
        | _ -> ValueNone

    /// Parse a single card such as "As", "Kd" or "10h".
    let tryParse (text: string) =
        match text with
        | null -> ValueNone
        | _ ->
            let t = text.Trim()
            // Accept "10x" as an alias for "Tx".
            let t = if t.Length = 3 && t.StartsWith "10" then "T" + string t.[2] else t

            if t.Length <> 2 then
                ValueNone
            else
                match rankOfChar t.[0], suitOfChar t.[1] with
                | ValueSome r, ValueSome s -> ValueSome(create r s)
                | _ -> ValueNone

    /// Parse a single card, raising on malformed input.
    let parse text =
        match tryParse text with
        | ValueSome c -> c
        | ValueNone -> invalidArg (nameof text) (sprintf "'%s' is not a card; expected a form like 'As' or 'Td'." text)

    /// Parse a run of cards written with or without separators, e.g. "AsKs" or "As Ks".
    let tryParseMany (text: string) =
        match text with
        | null -> ValueNone
        | _ ->
            let compact = String(text.ToCharArray() |> Array.filter (fun c -> not (Char.IsWhiteSpace c) && c <> ','))

            let rec loop i acc =
                if i >= compact.Length then
                    ValueSome(List.rev acc |> List.toArray)
                // Consume "10x" before the two-character form so the '1' is not misread.
                elif i + 2 < compact.Length && compact.[i] = '1' && compact.[i + 1] = '0' then
                    match tryParse (compact.Substring(i, 3)) with
                    | ValueSome c -> loop (i + 3) (c :: acc)
                    | ValueNone -> ValueNone
                elif i + 1 < compact.Length then
                    match tryParse (compact.Substring(i, 2)) with
                    | ValueSome c -> loop (i + 2) (c :: acc)
                    | ValueNone -> ValueNone
                else
                    ValueNone

            loop 0 []

    /// Parse a run of cards, raising on malformed input.
    let parseMany text =
        match tryParseMany text with
        | ValueSome cs -> cs
        | ValueNone -> invalidArg (nameof text) (sprintf "'%s' is not a sequence of cards; expected a form like 'AsKs'." text)

    /// Render a sequence of cards as a space-separated string.
    let formatMany (cards: seq<Card>) =
        cards |> Seq.map string |> String.concat " "
