# Dealer Hole Card: ENHC and the Peek Rule

## What we play

TwentyOne uses **European No-Hole-Card (ENHC)** style. The dealer takes only an
upcard at the start of the round; the hole card is not drawn until after all
player turns are complete. This is a legitimate blackjack variant played in
UK/EU casinos and is the natural fit for chat-based /dice play (see below).

## What it means at the table

If the dealer ends up with a natural blackjack (a 2-card 21 after revealing the
hole card), they win against any player who isn't also at 2-card 21 - even if
that player drew to 21 from 3+ cards, and even on the player's doubled or split
bets. The player's full stake is at risk against a possible dealer BJ for the
entire round.

## How it differs from "peek" rules

In a peek-rules game (most US casinos, most online blackjack):

- Before player turns, if the dealer's upcard is an Ace or any 10-value, the
  dealer privately checks the hole card.
- If the hole card completes a blackjack, the round ends immediately. Players
  who also have blackjack push; everyone else loses only their original bet.
- Players never get the chance to double or split into a losing hand.

In ENHC:

- The hole card is not drawn until the dealer's turn begins.
- Players don't know whether the dealer has blackjack while deciding how to play.
- A player who doubles or splits and is then beaten by a revealed dealer BJ
  loses their entire stake.

## Walk-through

Lorah bets 1000g. Dealer's upcard is an Ace. Lorah is dealt 5, 6 = 11.

**ENHC (what we play):**

1. Lorah doubles down on 11. Bet goes to 2000g.
2. She draws a 10. Hand is 21.
3. Dealer's turn. Dealer draws hole card: it's a 10. Dealer hand is A+10 = BJ.
4. Lorah loses 2000g.

**Peek (most US casinos):**

1. Dealer privately checks the hole card. It's a 10 - dealer has BJ.
2. Round ends immediately.
3. Lorah loses only her original 1000g; never gets to double.

Spread over many rounds the rule difference is worth roughly **+0.11%** to the
house edge under ENHC vs peek.

## Why we use ENHC (and not peek)

Peek depends on **asymmetric information** - the dealer privately knows whether
their hole card makes BJ while players don't. With public `/dice 13` rolls, every
card is visible to everyone instantly, so the natural mechanic doesn't support
peek directly.

Workarounds we considered:

- **Trust-based private check** - plugin rolls the hole card via local RNG
  (not chat), shows only the dealer, dealer announces "no blackjack" or reveals.
  Works only if players trust the dealer.
- **Reveal-on-suspect** - when upcard is A or 10, roll the hole card publicly
  before player turns. This is strictly more information than real peek and
  would shift basic strategy significantly; it isn't really "peek" anymore.
- **Cryptographic commit-reveal** - hash the hole card up front, reveal after
  the round, players can verify. Removes the trust requirement but is heavy
  machinery for a casual venue game.

ENHC sidesteps all of this. The rules are unambiguous, no trust assumptions are
required, and every card stays visible in chat.

## For your venue rules sign

> Dealer does not check for blackjack before player turns. If the dealer reveals
> a blackjack at the end of the round, all non-blackjack hands lose their full
> stake (including any doubles or splits).

Players who understand this quickly learn: be cautious about doubling or
splitting when the dealer's upcard is an Ace or any 10-value.

## Implications for the house edge

The `EdgeSolver` (`TwentyOne.Game/Edge/EdgeSolver.cs`) computes the expected
house edge for our exact rule set including ENHC. Standard published baselines
assume peek, so direct comparisons need to add ~0.11% to the peek baseline (or
subtract it from our number) to be apples-to-apples.

### Verifying against external calculators

The Wizard of Odds calculator (https://wizardofodds.com/games/blackjack/calculator/)
doesn't expose an "infinite deck" option, but you can extrapolate:

| Adjustment | Direction | Size |
|---|---|---|
| 8 decks → infinite | + | ~0.08% |
| Peek → ENHC | + | ~0.11% |

So for our baseline cell (3:2 BJ, no Charlie, H17, DAS):

```
WoO 8-deck H17 DAS no-surrender 3:2  →  ~0.60%  (with peek)
+ 0.11% for ENHC                     →  ~0.71%
+ 0.08% for infinite deck            →  ~0.79%
```

Our solver computes **0.84%** for that cell. The residual ~0.05% is within
expected solver/calculator drift and reflects truly-optimal play under
infinite-deck assumptions (real basic strategy charts are tuned for finite
shoes).

## If we ever wanted to switch to peek

Adding a peek option would require:

1. A `GameState.DealerPeeks` rule toggle.
2. In `HandleBeginPlayerTurns`, if peek enabled and upcard is A or 10-value,
   draw the hole card (privately or via plugin RNG) and resolve the BJ check
   before the first player acts.
3. New game phase / UI for "dealer checking for BJ" with an immediate Payout
   branch if BJ is found.
4. EdgeSolver gets a `DealerPeeks` parameter that propagates into the dealer
   distribution computation.

Not a small change. ENHC is genuinely the more natural fit for this format.
