module PokerOdds.Tests.CardTests

open Xunit
open PokerOdds.SpecialK

[<Fact>]
let ``the deck is indexed ace of spades to two of clubs`` () =
    Assert.Equal(0, (Card.create Rank.Ace Suit.Spades).Index)
    Assert.Equal(3, (Card.create Rank.Ace Suit.Clubs).Index)
    Assert.Equal(51, (Card.create Rank.Two Suit.Clubs).Index)
    Assert.Equal(DECK_SIZE, Card.deck.Length)

[<Fact>]
let ``cards of the same suit are congruent modulo four`` () =
    for card in Card.deck do
        Assert.Equal(int card.Suit, card.Index % 4)

[<Fact>]
let ``every card round-trips through its text form`` () =
    for card in Card.deck do
        Assert.Equal(card, Card.parse (string card))

[<Fact>]
let ``rank and suit are recovered from the index`` () =
    for card in Card.deck do
        Assert.Equal(card, Card.create card.Rank card.Suit)

[<Theory>]
[<InlineData("as", "As")>]
[<InlineData("AS", "As")>]
[<InlineData("10h", "Th")>]
[<InlineData(" Kd ", "Kd")>]
let ``parsing accepts the usual spellings`` (input: string) (expected: string) =
    Assert.Equal(expected, string (Card.parse input))

[<Theory>]
[<InlineData("")>]
[<InlineData("A")>]
[<InlineData("1s")>]
[<InlineData("Ax")>]
[<InlineData("AsK")>]
let ``malformed cards are rejected`` (input: string) =
    Assert.Equal(ValueNone, Card.tryParse input)

[<Fact>]
let ``runs of cards parse with or without separators`` () =
    let expected = [| Card.parse "As"; Card.parse "Ks"; Card.parse "Td" |]
    Assert.Equal<Card[]>(expected, Card.parseMany "AsKsTd")
    Assert.Equal<Card[]>(expected, Card.parseMany "As Ks Td")
    Assert.Equal<Card[]>(expected, Card.parseMany "As,Ks,Td")
    Assert.Equal<Card[]>(expected, Card.parseMany "AsKs10d")

[<Fact>]
let ``a card index outside the deck is rejected`` () =
    Assert.Throws<System.ArgumentOutOfRangeException>(fun () -> Card 52 |> ignore) |> ignore
    Assert.Throws<System.ArgumentOutOfRangeException>(fun () -> Card -1 |> ignore) |> ignore

[<Fact>]
let ``better cards compare greater`` () =
    Assert.True(Card.parse "As" > Card.parse "Ks")
    Assert.True(Card.parse "2c" < Card.parse "2s")
