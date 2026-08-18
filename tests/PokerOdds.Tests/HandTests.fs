module PokerOdds.Tests.HandTests

open Xunit
open PokerOdds.SpecialK

[<Theory>]
[<InlineData("AsKsQsJsTs", "Royal flush")>]
[<InlineData("9h8h7h6h5h", "Straight flush, nine high")>]
[<InlineData("5h4h3h2hAh", "Straight flush, five high")>]
[<InlineData("7c7d7h7sKd", "Four of a kind, sevens")>]
[<InlineData("AcAdAhKcKd", "Full house, aces full of kings")>]
[<InlineData("Ac9c7c5c3c", "Flush, ace high")>]
[<InlineData("Ts9d8h7s6c", "Straight, ten high")>]
[<InlineData("5c4d3h2sAc", "Straight, five high")>]
[<InlineData("QcQdQh7s2c", "Three of a kind, queens")>]
[<InlineData("AcAd8h8s2c", "Two pair, aces and eights")>]
[<InlineData("JcJd9h5s2c", "Pair of jacks")>]
[<InlineData("Ac9d7h5s3c", "Ace high")>]
let ``hands are described the way players say them`` (cards: string) (expected: string) =
    Assert.Equal(expected, Hand.describe (Card.parseMany cards))

[<Fact>]
let ``the best five of seven are picked out`` () =
    // Two pair on board plus a set in hand: the sevens play, the deuces do not.
    let cards = Card.parseMany "7c7d7h Kd Ks 2c 2d"
    Assert.Equal("Full house, sevens full of kings", Hand.describe cards)
    Assert.Equal("Ks Kd 7h 7d 7c", Card.formatMany (Hand.bestFive cards))

[<Fact>]
let ``the best five are returned with the best card first`` () =
    let best = Hand.bestFive (Card.parseMany "2c5d9hKsAc7d3s")
    Assert.Equal("Ac Ks 9h 7d 5d", Card.formatMany best)

[<Fact>]
let ``six cards rank as the best five they contain`` () =
    let six = Card.parseMany "AsKsQsJsTs2c"
    Assert.Equal(NUMBER_OF_RANKS, Hand.rank six)
    Assert.Equal(Hand.rank (Card.parseMany "AsKsQsJsTs"), Hand.rank six)

[<Fact>]
let ``seven cards rank as the best five they contain`` () =
    let seven = Card.parseMany "AsKsQsJsTs2c3d"
    Assert.Equal(NUMBER_OF_RANKS, Hand.rank seven)

[<Fact>]
let ``category names cover every category`` () =
    let names = [ 0..8 ] |> List.map (enum<HandCategory> >> Hand.categoryName)

    Assert.Equal(9, names.Length)
    Assert.Equal(9, List.distinct names |> List.length)
    Assert.False(List.contains "" names)

[<Fact>]
let ``ranks outside the scale are rejected`` () =
    Assert.Throws<System.ArgumentException>(fun () -> Hand.categoryOfRank 0 |> ignore) |> ignore
    Assert.Throws<System.ArgumentException>(fun () -> Hand.categoryOfRank (NUMBER_OF_RANKS + 1) |> ignore)
    |> ignore

[<Fact>]
let ``hands of the wrong size are rejected`` () =
    Assert.Throws<System.ArgumentException>(fun () -> Hand.rank (Card.parseMany "AsKsQs") |> ignore)
    |> ignore

    Assert.Throws<System.ArgumentException>(fun () -> Hand.rank (Card.parseMany "AsKsQsJsTs9s8s7s") |> ignore)
    |> ignore
