# Extending EdgeSolver

Reference for adding new rule axes or features to `EdgeSolver`. Covers how the
solver is structured internally, what each kind of new rule costs, and the
checklist for wiring one in.

## How the solver works

The solver computes the player's expected value under optimal play, then
returns `-EV` as the house edge.

### Two recursions, both memoized

1. **Dealer outcome distribution** - for each upcard 1-13, compute the
   probability of ending at each terminal bucket: Bust, 17, 18, 19, 20, 21
   (from 3+ cards), and BJ (natural 2-card 21). Bucketed because nothing else
   about the dealer hand matters for payout resolution.

2. **Player EV table** - for each `(total, isSoft, numCards, isFromSplit,
   upcard)` state, compute the EV under optimal play from there. The memo key
   is packed into a single `int` for speed.

### State space and decisions

Player decisions are evaluated in two stages:

- `EvalInitial(c1, c2, upcard)` - top-level entry for a 2-card hand. Considers
  Stand / Hit / Double / Split. Split is only evaluated here, not deeper.
- `EvalHand(total, isSoft, numCards, isFromSplit, upcard)` - mid-hand recursion.
  Considers Stand / Hit / Double (Double only when `numCards == 2`).

Char rules:

- BJ (2-card 21, not from split) is recognized at the top level and returns
  `BjEV` directly - no decision to make.
- Charlie (`numCards >= 5` when enabled) is a terminal in `EvalHand`.
- 21 always stands.
- Bust returns -1 immediately.

### Split fixed-point

Splits use a closed-form fixed point so unlimited re-splits don't recurse
infinitely. For a pair of rank X:

```
S = single-hand EV after a split
H_neq = sum over c != X of (1/13) * play(X, c)
H_eq  = play(X, X) as one hand (no re-split)

S = H_neq + (1/13) * max(H_eq, 2S)

Solutions:
  no re-split:  S = H_neq + H_eq / 13
  re-split:     S = (13/11) * H_neq

Pick the max; return 2*S as the total split EV.
```

For a finite `ResplitCap`, the closed form is replaced with a depth-bounded
recursion (`EvalSplitBounded`). The top-level call seeds budget = `cap - 2`
(initial split consumed 1, 2 hands now exist) and each further split decrements
the subtree's budget. This is a tree-model approximation - children of a split
get `budget - 1` rather than sharing a pool - which can over-count splits at
deeper levels with probability `(1/13)^depth`, well under 0.01% at any
realistic cap.

Aces split don't go through either branch - they auto-stand after one card per
the engine rule. The cap also does not apply to aces; they remain gated solely
by `ResplitAces`.

### Multipliers and payouts

`Multiplier(PayoutRatio)` maps the enum to 1.5 / 1.2 / 1.0. The solver uses
the fractional multiplier, not the engine's `Math.Ceiling` rounding - the
difference is negligible at any realistic bet size.

## Adding rule axes

When in doubt about cost, think about three things: does the rule change the
**state space** the solver explores, the **transitions** between states, or
just the **aggregation** at the end.

### Rule-by-rule cost

| Rule | Engine work | Solver work | Per-cell speed | Rule space |
|---|---|---|---|---|
| **S17** (stand on soft 17) ✓ implemented | Update `DealerRecommendation` to check the rule | Dealer recursion hits less often | Slightly faster | ×2 |
| **DAS toggle** ✓ implemented | Add `bool DoubleAfterSplit` field; gate `CanDouble` on it when `isFromSplit` | One-line guard in the Double branch of `EvalHand` | Unchanged | ×2 |
| **HSA** (hit split aces multiple times) ✓ implemented | Remove the forced-stand on split aces; allow normal play after the first card | Replace the ace branch in `EvalSplit` with a call into `EvalHand` (with appropriate flags) | ~10-20% slower per cell | ×2 |
| **RSA** (re-split aces) ✓ implemented | Allow split aces to remain Playing if they pair again | Apply the fixed-point math to the ace branch too | ~5-10% slower per cell | ×2 |
| **Surrender** ✓ implemented (effectively early surrender, since ENHC has no peek) | New action `SurrenderHand`, new `HandState.Surrendered`, new `PayoutResult.Surrender` | Surrender option added to `EvalInitial` at -0.5 EV | Unchanged | ×2 |
| **Peek toggle** (US-style) | New phase, dealer reveals BJ before player turns when upcard is A or 10 | DealerDist for A/10 upcards conditions on "not BJ" if peek is on; payout resolution changes for non-BJ players vs dealer BJ | Unchanged | ×2 |
| **Charlie at N cards** (currently 5) | Generalize `ComputeHandState` Charlie check | Generalize the Charlie terminal in `EvalHand` | Unchanged | linear in N range |
| **Player splits limited to N hands** ✓ implemented as `ResplitCap` (Max2/Max3/Max4/Unlimited; default Max4). Aces are exempt from the numeric cap and gated by `ResplitAces`. | Cap counts total `player.Hands.Length`; `CanSplit` refuses once at the limit. | Bounded form unrolls `EvalSplitBounded(budget=cap-2)`; Unlimited uses the closed-form fixed-point. Tree-model approximation - each subtree gets `budget-1` rather than sharing a pool - over-counts only at probability `(1/13)^depth`, negligible at any cap. | Tiny | ×4 |
| **Double restriction** ✓ implemented as `DoubleRestriction` (Any/Hard9To11/Hard10To11/HardOnly; default Any). DAS and the restriction stack independently. | `CanDouble` calls `IsDoubleableTotal` to check the (total, isSoft) pair against the rule. | `TotalDoubleable` guard in front of the Double branch in `EvalHand`, `ActionEVForInitial`, and `OptimalActionForInitial`. | Negligible | ×4 |

Adding all of S17, DAS toggle, HSA, RSA, peek, surrender = 21 base cells × 64
combinations = ~1300 cells. At ~50ms per cell that's ~65 seconds for a full
sweep, but still <100ms for any single live computation. The UX challenge
(presenting that many cells in a config window) is the actual constraint.

### Continuous-valued payouts

Currently `BjPayout` and `CharliePayout` are enums with three values. Going to
arbitrary decimal multipliers has very different costs depending on which one.

**BjPayout - effectively free.** ✓ implemented as `double`. Blackjack is
auto-resolved (2-card 21, no decisions), so the BJ payout multiplier doesn't
affect any optimal action. The current code re-solves on each multiplier
change but caches by `EdgeRules.Equals`, so a single live computation stays
under ~50 ms. A future optimization could factor the BJ contribution out:

```
edge(bjMul) = base_edge_without_bj + bjMul * bj_rate
```

where `bj_rate = P(player BJ) * (1 - P(dealer BJ | upcard))`, integrated over
upcards. One solve gets `base_edge_without_bj` and `bj_rate`; the UI would do
the multiplication for any value. A slider over 1.0x-2.0x in 0.01 steps would
be 100 "cells" at zero additional compute cost. Worth doing if/when we add a
sweep view.

**CharliePayout - not free.** Charlie payout *does* affect optimal play: the
player hits more aggressively when Charlie pays more. Each distinct
`CharliePayout` value requires a full re-solve. Caching helps - if the UI
shows a slider, only the requested value gets computed. Worst-case for a
0.1x-stepped slider is ~26 solves × ~40ms = ~1 second. Tolerable but not
free.

If both payouts go continuous: refactor `BjPayout` out of the solver's hot
path (good in any case), keep `CharliePayout` as a re-solve parameter.

## Implementation checklist (new boolean rule)

1. **Engine.** Add field to `GameState`, default to existing behavior. Update
   the relevant engine logic (`DealerRecommendation`, `CanDouble`,
   `HandleAddPlayerCard` post-card adjustments, etc.) to consult the new
   field. Make sure to push undo correctly.

2. **Solver.** Add field to `EdgeRules`. Threading depends on the rule:
   - Dealer-side: update `DealerRecurse` / `DealerDist`.
   - Decision-side: update `EvalHand` / `EvalInitial` / `EvalSplit`.
   - Memoization: add a bit to `PlayerKey` if the new rule meaningfully
     partitions states. (Most boolean toggles don't, because they're constant
     for a given solve.)

3. **Tests.** Add a test asserting the headline edge changes in the expected
   direction. Re-run the strategy chart printer and confirm no unexpected
   cell flips. Update `docs/edge-solver-verification.md` if the baseline
   numbers shift.

4. **Config UI.** Add the toggle to `ConfigWindow`. The cached edge
   automatically invalidates because `EdgeRules.Equals` compares all fields.

5. **AGENTS.md.** If the rule is structurally interesting (e.g. peek changes
   the game phase model), document it in the EdgeSolver or engine
   architecture section.

## When not to add a rule

If a rule is fully cosmetic (narration variants, UI color preferences) it
doesn't belong in `EdgeRules` and shouldn't get plumbed into the solver. The
solver only cares about anything that changes the expected value of any
decision or the payout for any outcome.
