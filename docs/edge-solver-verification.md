# EdgeSolver Verification

The `EdgeSolver` (`TwentyOne.Game/Edge/EdgeSolver.cs`) has been cross-checked
against published blackjack data on two axes: the headline house-edge number
and the per-cell optimal action chart. Both checks pass. This doc records the
methodology and findings so future changes can be re-verified the same way.

## Headline house edge

Tested cell: H17, DAS, unlimited resplits, no RSA, no HSA, no surrender, BJ
pays 3:2, no Charlie, infinite deck, ENHC.

| Step | Value |
|---|---|
| Wizard of Odds calculator, 8-deck, with "loses only original bet vs dealer BJ = Yes" (peek-equivalent) | 0.64493% |
| Same calculator with "loses only original bet vs dealer BJ = No" (matches our ENHC) | **0.75620%** |
| Add ~0.08% for 8-deck → infinite-deck extrapolation | ~0.836% |
| EdgeSolver computes | **0.83973%** |

Match within **~0.005%** (half a basis point). The observed ENHC vs peek delta
of 0.7562% - 0.6449% = **0.1113%** also matches the published ~0.11% figure
quoted in standard rule-variant tables.

Calculator used: https://wizardofodds.com/games/blackjack/calculator/

## Strategy chart cross-check

For the same baseline cell (3:2, no Charlie), the solver's optimal action was
extracted for every (player hand, dealer upcard) combination and compared
against the standard H17 DAS chart.

**196 of 210 cells match exactly.** All 6 deviations are explained.

### The 6 deviations

| Cell | Standard says | We say | Why we deviate |
|---|---|---|---|
| Hard 11 vs 10 | Double | Hit | ENHC: dealer 10 has P(BJ)≈7.7% - doubling risks losing 2x stake |
| Hard 11 vs A | Double | Hit | ENHC: dealer A has P(BJ)≈30.8% - doubling is heavily punished |
| 8,8 vs 10 | Split | Hit | ENHC: splitting doubles exposure to dealer BJ |
| 8,8 vs A | Split | Hit | ENHC: same, much larger swing due to dealer A |
| A,A vs A | Split | Hit | ENHC: same, also affects split-aces hands which can't be hit |
| A,2 vs 5 | Double | Hit | Composition-dependent: infinite vs finite deck |

The first five are **textbook ENHC adjustments**, listed in every reference for
European-style no-hole-card blackjack (see `docs/dealer-hole-card.md`). The
solver discovered them independently from EV math, which is strong evidence
the recursion is correct.

### The A,2 vs 5 anomaly

Dealer 5 can't produce a BJ, so ENHC does not explain this cell. It's a
known composition-dependent borderline:

| Source | Hit EV | Double EV | Margin |
|---|---|---|---|
| WoO 6-deck H17 DAS appendix | +0.10867 | +0.12401 | +0.01534 favoring Double |
| Our infinite-deck solver | +0.13363 | +0.12721 | +0.00642 favoring **Hit** |

The Hit-vs-Double margin flips sign as you go from finite to infinite deck (a
~0.021 swing on this cell). This is documented behavior for borderline soft-13
cells: removing card-removal effects shifts a small number of close decisions.
Not a bug.

## Reproducing the verification

Both checks live in `TwentyOne.Tests/EdgeSolverTests.cs`:

- `H17_3to2_NoCharlie_EdgeInPublishedRange` and the other headline tests assert
  the overall number and the published deltas for 6:5 and 1:1 payouts.
- `Print_BasicStrategy_For_3to2_NoCharlie` dumps the full chart to test stdout
  for visual diff against any published H17 DAS reference.
- `Print_ActionEVs_For_Deviation_Cells` dumps Stand/Hit/Double/Split EVs at
  each of the 6 deviation cells, so anyone investigating a suspected bug can
  see the EV margin and decide whether it's a real solver issue or just a
  close decision.

Run them with:

```bash
nix develop --command dotnet test TwentyOne.Tests/TwentyOne.Tests.csproj \
  --filter "FullyQualifiedName~EdgeSolverTests" \
  --logger "console;verbosity=detailed"
```

## When this should be re-run

Re-verify any time the solver, the engine's payout resolution, or the rule
enum changes:

- Adding or removing rule axes (e.g. a peek toggle) requires re-doing the
  headline check at the affected cells.
- Changes to `GameEngine.ComputePayoutResult` or the BJ/Charlie semantics in
  `GameEngine.GetPayoutResult` need both checks - the solver must match the
  engine's rules, not the textbook.
- Refactors of `EdgeSolver` internals (memoization, hand-state encoding,
  split-recursion fixed-point) should not change any output. Treat any
  deviation from the numbers in this doc as a regression to investigate.

The published numbers will not change; only the solver might drift. Anchor
all future comparisons against the values recorded above.

## Re-run: dealer stand threshold (2026-08-16, v0.7.0)

`DealerStandsOnSoft17` (bool) became `DealerStandThreshold` (int) +
`DealerHitsSoftThreshold` (bool), so the dealer draw rule and the outcome
buckets both changed. Verified as a pure refactor at threshold 17: the same
verification cell computed **0.83973% (H17)** and **0.62301% (S17)** both
before and after the change - bit-identical, checked by building the previous
commit in a scratch worktree. (The 0.83970% previously recorded above was
stale; the current code and the pre-change code agree exactly.)

**One real bug found and fixed by this work.** The dealer outcome distribution
bucketed standing totals 17-21 only, with a `_ => OBust` default arm. That was
unreachable while the dealer always stood on 17+, but a sub-17 threshold made
it live: a dealer standing on 15 or 16 was scored as a **bust**, crediting the
player a win against every hand. Buckets `O15`/`O16` were added.
`EdgeSolverTests.StandOn16_DealerSixteenIsNotScoredAsABust` guards it.

New reference values for the same cell (3:2, no Charlie, DAS, unlimited
resplits, no RSA/HSA/surrender, infinite deck, ENHC):

| Stand rule | House edge |
|---|---|
| Stand 15 | -4.61456% |
| Stand 16 | **-0.25576%** |
| Stand 17, hit soft 17 (H17, default) | 0.83973% |
| Stand 17, stand soft 17 (S17) | 0.62301% |
| Stand 18 | -6.58697% |

Note what these say about the non-standard thresholds: **every one of them is
player-favored**, and 15/18 ruinously so. Standing on 16 forfeits all the
dealer's draws to 17-21 against a player who has already stood on 17+;
standing on 18 forces the dealer to draw from 17 and bust constantly. A venue
asking to "stand on 16" is asking to run a -0.26% game. The Rules Editor's
live house-edge display shows this, which is the point of surfacing it there.
