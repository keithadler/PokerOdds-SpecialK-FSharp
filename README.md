# PokerOdds-SpecialK-FSharp

A fast Texas Hold'em hand evaluator and equity calculator in F#, built on an F# port of
[Kenneth J. Shackleton's SKPokerEval](https://github.com/kennethshackleton/SKPokerEval)
perfect-hash tables.

Ranking a seven-card hand costs one array lookup — around **250 million hands per second**
on a single core — which is what makes exhaustive equity enumeration practical: all
1,712,304 possible boards for a preflop match-up are dealt and scored in single-digit
milliseconds.

```
$ pokerodds equity --hand AsKs --hand QdQh

      Hand          Equity       Win       Tie  Hand now
------------------------------------------------------------------
P1    As Ks         46.21%    46.02%     0.39%
P2    Qd Qh         53.79%    53.59%     0.39%

Exact over 1,712,304 boards in 0.10s
```

## Contents

| Project | What it is |
| --- | --- |
| `src/PokerOdds.Core` | The library: cards, both evaluators, hand naming, equity |
| `src/PokerOdds.Cli` | The `pokerodds` command line tool |
| `tests/PokerOdds.Tests` | The test suite, including exhaustive verification |

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download) or later.

```bash
git clone https://github.com/mrunks/PokerOdds-SpecialK-FSharp.git
cd PokerOdds-SpecialK-FSharp
dotnet test
dotnet run --project src/PokerOdds.Cli -- equity --hand AsKs --hand QdQh
```

## The command line tool

```
pokerodds equity [options]     Work out each player's share of the pot
pokerodds rank <cards>         Rank and name a five-, six- or seven-card hand
pokerodds verify [--full]      Check the evaluators against known results
pokerodds bench [--seconds N]  Measure evaluation throughput
```

Cards are a rank in `23456789TJQKA` followed by a suit in `shdc` — `As`, `Td`, `7c`.
Several may be run together (`AsKs`) or separated (`As Ks`).

```bash
# Heads up, preflop. Every board is enumerated, so the answer is exact.
pokerodds equity --hand AsKs --hand QdQh

# Three players on a flop.
pokerodds equity --hand AhKd --hand 7c7s --hand JsTs --board 2c7d9h

# One known hand against five random ones. Simulated, with a reproducible seed.
pokerodds equity --hand AsAd --opponents 5 --trials 500000 --seed 42

# Cards that are out of play but not on the board.
pokerodds equity --hand AhKh --hand 7c7s --board 2h7d9h --dead 3h4h

# Name a hand.
pokerodds rank Ah Ad Kc Ks 2d 7h 9c
```

Equity is enumerated exactly when at most `--max-exact` boards (10,000,000 by default)
need dealing and every player's cards are known; otherwise it is simulated over
`--trials` random run-outs. `--simulate` forces simulation. A given `--seed` reproduces
the same answer on any machine and any number of cores.

Install it as a global tool:

```bash
dotnet pack src/PokerOdds.Cli -c Release
dotnet tool install --global --add-source src/PokerOdds.Cli/bin/Release PokerOdds.Cli
```

## The library

```fsharp
open PokerOdds.SpecialK

// Rank five, six or seven cards. Higher is better; 1..7462.
Hand.rank (Card.parseMany "AsKsQsJsTs")   // 7462
Hand.describe (Card.parseMany "AcAdAhKcKd7c2c")  // "Full house, aces full of kings"
Hand.bestFive (Card.parseMany "7c7d7hKdKs2c2d")  // [| Ks; Kd; 7h; 7d; 7c |]
Hand.category (Card.parseMany "Ac9c7c5c3c")      // HandCategory.Flush

// Equity.
let request =
    { Hands = [| Card.parseMany "AsKs"; Card.parseMany "QdQh" |]
      Board = [||]
      Dead = [||] }

let result = Equity.exact request
result.Players.[0].Equity        // 0.4621
result.Trials                    // 1712304
```

The evaluators are also usable directly on raw deck indices, which is the fastest path
and the one the original C++ exposes:

```fsharp
open PokerOdds.SpecialK.SevenCardEvaluator

let eval = SevenEval.Shared          // tables build once, then shared
eval.GetRank(0, 4, 8, 12, 16, 20, 24)
```

Cards are indices `0..51`: **0 is the ace of spades and 51 the two of clubs**. Cards of
the same suit are congruent modulo four. `FiveEval` and `SevenEval` are immutable once
constructed and safe to use from several threads; the `Shared` instances build their
tables on first use (about 40 ms and 9 MB for `SevenEval`).

## How it works

A hand's rank is looked up rather than computed. Each face is given a weight chosen so
that the sum of the weights of the cards in a hand is unique to that hand's *face
pattern*, which turns the sum into an index straight into a table of precomputed ranks.

* **Five cards.** Face weights `{0, 1, 5, 22, 94, …, 79415}` give distinct sums for every
  five-card face combination, in a range small enough to index a 361 KB table.
* **Seven cards.** A second set of weights `{0, 1, 5, 22, 98, …, 1479181}` gives distinct
  sums for all 49,205 seven-card face patterns. Those sums span 7.8 million values, but
  folding them once about a circumference of 4,565,145 keeps every one distinct while
  halving the table.
* **Flushes** are handled separately. Each face also carries a one-hot bit, so a set of
  same-suited faces is identified by the bitwise-or of its bits. A seven-card hand holding
  five or more cards of a suit can hold neither quads nor a full house, so when a flush is
  present it is always the best hand — which is what lets the two paths stay independent.
* **Detecting the flush** costs nothing extra: suits carry weights `{0, 1, 8, 57}` chosen
  so the sum over seven cards identifies the suit split exactly, and it is packed into the
  low nine bits of the same key the face weights build.

The C++ original ships its tables as generated headers, and compresses the seven-card
ranks through a hash and an offsets table. This port builds the equivalent tables at
startup from the five-card evaluator instead, and indexes the folded key directly. That
keeps the source readable and means the seven-card path is derived from — and checkable
against — the simpler one, at the cost of a much larger table; see
[Where the remaining speed is](#where-the-remaining-speed-is).

## Verification

The seven-card evaluator is checked against the five-card one on **all 133,784,560
seven-card hands**, and the five-card evaluator against the published hand distribution
on **all 2,598,960 five-card hands**:

| Category | Hands | | Category | Hands |
| --- | ---: | --- | --- | ---: |
| High card | 1,302,540 | | Flush | 5,108 |
| Pair | 1,098,240 | | Full house | 3,744 |
| Two pair | 123,552 | | Four of a kind | 624 |
| Three of a kind | 54,912 | | Straight flush | 40 |
| Straight | 10,200 | | **Total** | **2,598,960** |

All 7,462 distinct ranks occur, and rank 0 — reserved to mean "no hand" — never does.

```bash
dotnet test                                  # sampled seven-card check, a couple of seconds
POKERODDS_EXHAUSTIVE=1 dotnet test           # all 133,784,560 hands, about twenty seconds
dotnet run --project src/PokerOdds.Cli -- verify --full
```

CI runs the exhaustive check on Linux, Windows and macOS on every commit.

## Benchmarks

`pokerodds bench`, single-threaded on an Apple M-series core:

| | Hands per second |
| --- | ---: |
| `SevenEval.GetRank` | 250,000,000 |
| `FiveEval.GetRank` | 238,000,000 |
| `FiveEval.GetRankFromSeven` (best of 21) | 5,800,000 |

Equity, best of several runs in a warmed process, tables already built:

| | |
| --- | ---: |
| Preflop heads up (1,712,304 boards) | 6.2 ms |
| Monte Carlo, 2,000,000 trials | 16 ms |
| Building the tables, once per process | 30 ms |

A single `pokerodds equity` invocation will report more like 60 ms for that first figure,
because the enumeration runs once and the JIT never gets a reason to optimise it properly.
`EquityResult.Elapsed` excludes table construction but not that warm-up, so treat one-shot
timings as a ceiling. Anything long-running sees the warmed numbers.

These are bound by memory, not arithmetic, so treat them as an upper bound rather than
a promise. Every hand lands somewhere effectively random in the 9.1 MB seven-card rank
table, and an Apple M-series last-level cache holds far more of that than a typical x86
server's does. Equity enumeration is parallel across cores.

### What was tried, and what it was worth

Three changes paid off and are in:

**Letting the JIT inline the lookup.** Splitting `GetRank` so that the key could be
supplied ready-made put a call boundary in the hot path, and the method was just big
enough — because of the flush branch — that the JIT stopped inlining it. That alone cost
about a fifth of throughput, and it made every measurement noisy. Marking
`GetRankFromKey` for aggressive inlining took `SevenEval.GetRank` from 206 to 250 million
hands per second, better than before the split.


**Hoisting the key out of the enumeration loop.** A hand's key is the sum of its cards'
weights, so `Equity.exact` now sums each player's hole cards once and carries the board's
partial sum down the enumeration, instead of re-reading all seven weights per player per
board. `SevenEval.CardKey` and `GetRankFromKey` expose the same split to callers. Scoring
also lost a floating-point division per board in favour of a small reciprocal table.
Worth about 15% on its own — less than hoped, because the loop was already running at
several hundred million evaluations per second. Together with the inlining fix, preflop
enumeration went from 10.6 ms to 6.2 ms and two million Monte Carlo trials from 26 ms to
16 ms.

**Building the tables in parallel.** Ranking the 49,205 face patterns is about a million
five-card evaluations and is the bulk of construction. It parallelises with no
coordination, since each pattern writes to its own key. Construction dropped from roughly
66 ms to about 30 ms, which matters mainly for one-shot command line runs, where building
the table costs more than the work being asked for.

One change was tried and rejected:

**Compressing the rank table.** Upstream stores the seven-card ranks through a hash and
an offsets indirection rather than at the key itself, which is why its tables are about
110 KB against this port's 9.1 MB. That was implemented here — Shackleton's hash constants
turn out to be injective over these keys, and greedy row displacement packs all 49,205
entries with no waste, giving a 96 KB table. It was then removed, because on the machine
it was measured on it made evaluation *slower*: about 160 million hands per second against
206 million for the direct table at the time, since the hash and the extra dependent lookup cost more
than the cache misses they avoid on a chip with a large last-level cache. It also added
roughly 800 ms to construction, as the packing search is slow once the table is nearly
full.

That result is hardware-specific and would likely reverse on a machine with a smaller
last-level cache, where 9.1 MB does not stay resident. Anyone hitting that case should
reach for the compressed layout; it is a real win there, just not here.

What is left, and what it is actually worth. Measuring where the time goes changes the
answer: in a two-player preflop enumeration, **ranking the hands is only about a quarter
of the work**. Walking the 1.7 million boards and settling each pot is the other three
quarters. That rules out the change that looks most attractive on paper.

* **Suit isomorphism.** Many boards are the same hand up to a relabelling of suits, and
  collapsing them is exact rather than an approximation. But it only saves evaluation,
  and the group of suit permutations fixing two specific holdings is usually of order one
  or two — `AsKs` against `QdQh` admits only swapping diamonds and hearts. Even a perfect
  halving would take about 10% off the total, before paying for the test that decides
  whether a board is its orbit's representative. It becomes worthwhile with many players,
  where evaluation grows with the field and the walk does not.
* **Flattening the innermost loop** so that the last board card costs no call was tried,
  and measured three times *slower* — presumably the larger loop body stopped fitting
  whatever the previous shape fitted in.
* **SIMD gathers**, to overlap the memory latency that sets the ceiling. `Vector256`
  gathers are x86-only; ARM has no equivalent instruction, so this was not tried.
* **Shipping the tables as a resource** rather than building them, if the ~30 ms of
  construction ever matters more than the 9 MB.

A caveat on all of the above: run-to-run variance on the machine these were measured on
is wide — a factor of two between runs of identical code is not unusual under load. Every
figure quoted here is a best-of-several, and anything under about 20% should be treated as
unproven rather than real.

## Credit and licence

The table design, the face and suit weights and the seven-card circumference are
Kenneth J. Shackleton's, from [SKPokerEval](https://github.com/kennethshackleton/SKPokerEval),
which is licensed under the Apache License 2.0. This project is licensed the same way —
see [LICENSE](LICENSE) and [NOTICE](NOTICE).
