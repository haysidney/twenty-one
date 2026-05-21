## Meta

After completing any feature or significant design decision, update CLAUDE.md to reflect it. Keep architecture sections current - future sessions depend on this file for context.

## Project

This is a Final Fantasy XIV plugin that uses the Dalamud API (https://dalamud.dev/api/).

The FFXIVClientStructs repo is cloned at `FFXIVClientStructs/` for reference. Consult it locally before going to the web for information about FFXIV client structures.

It's a plugin meant for a dealer to use to run a blackjack game in a venue.

## Debugging

Plugin config is saved to `/home/sidney/.xlcore/pluginConfigs/TwentyOne.json`.

### Debug build (DEBUG-only UI features)

All debug tooling is `#if DEBUG` - absent in Release builds. Enable via the **Debug** button in the MainWindow top bar (appears in Debug builds only).

#### DebugWindow

- **Roll queue** - pre-load card values (1–13) that `QueueHitRoll` consumes instead of sending chat rolls. Manual entry or loaded from scenario file.
- **Scenario** - loads a JSON scenario file that sets up players/bets, enqueues rolls, and scripts the exact sequence of UI button clicks to execute. An orange banner appears in MainWindow while a scenario is active showing the next required step.
- **Snapshot save/load** - serialize/restore `GameState` directly for reproducing specific game states.

#### Scenario format

```json
{
  "name": "Human-readable name",
  "players": [
    { "name": "Lorah", "bet": "1000" },
    { "name": "Bekki", "bet": "500" }
  ],
  "rolls": [1, 10, 7, 6],
  "bjPayout": 1.5,
  "charliePayout": "ThreeToTwo",
  "fiveCardCharlie": "Disabled",
  "dealerStandsOnSoft17": false,
  "doubleAfterSplit": true,
  "hitSplitAces": false,
  "resplitAces": false,
  "allowSurrender": false,
  "actions": [
    "StartDeal",
    "BeginPlayerTurns",
    "Stand:0:0",
    "Stand:1:0",
    "BeginDealerTurn",
    "GoToPayout",
    "NewRound"
  ]
}
```

Each rule override is optional; omitted fields use the standard defaults (3:2 BJ, EvenMoney Charlie, Charlie disabled, H17, DAS-on, no HSA / RSA / Surrender). Scenarios are intentionally insulated from the active venue's rules so they stay reproducible.

**Action strings:**

| Action | Trigger |
|--------|---------|
| `StartDeal` | "Start Deal →" button |
| `BeginPlayerTurns` | "Begin Player Turns →" button |
| `BeginDealerTurn` | "Begin Dealer Turn →" button |
| `GoToPayout` | "Go to Payout →" button |
| `NewRound` | "New Round" button |
| `DealerHit` | Dealer Hit button |
| `Hit:pi:hi` | Player pi hand hi Hit button |
| `Stand:pi:hi` | Player pi hand hi Stand button |
| `Dbl:pi:hi` | Player pi hand hi Dbl button (sets pendingDouble) |
| `ConfirmDbl:pi:hi` | "Confirm Dbl" button after Dbl |
| `Spl:pi:hi` | Player pi hand hi Spl button (sets pendingSplit) |
| `ConfirmSpl:pi:hi` | "Confirm Spl" button after Spl |
| `Srn:pi:hi` | Player pi hand hi Surrender button (requires `allowSurrender: true`) |
| `AdvancePlayer` | "Next Player ↓" / "Next Hand ↓" button |

**Roll order during deal:** dealer card first, then player 0 card 1, player 0 card 2, player 1 card 1, player 1 card 2, etc. Subsequent hit rolls consumed in action order.

**Button gating:** while a scenario is active, only the button matching the next action is enabled by default. Uncheck "Gate buttons" in DebugWindow to see all buttons enabled (useful for testing button visibility) while still tracking scenario progress. Step button executes the next action programmatically without clicking the actual button.

**Abort:** click Abort in the banner or in DebugWindow. Clears scenario and roll queue; GameState is left wherever it was.

**Right-click any row in Round History** → "Save snapshot..." to export a `GameState` for later debugging or to load into a scenario.

#### Scenario files

Scenario JSON files live in `Scenarios/`. Scenario names use test player names: Lorah, Bekki, Nolla (Lorah = winning player per test convention).

## Build

Dev environment uses Nix. Enter with `nix develop` (requires `flake.nix` to be git-tracked). This installs `dotnet-sdk_10`.

Build commands:
```bash
nix develop --command dotnet build TwentyOne/TwentyOne.csproj -c Debug
nix develop --command dotnet build TwentyOne/TwentyOne.csproj -c Release
nix develop --command dotnet test TwentyOne.Tests/TwentyOne.Tests.csproj
```

## Architecture

### Project layout

- `TwentyOne.Game/` - pure .NET library, no Dalamud dependency. Contains all game logic.
- `TwentyOne/` - Dalamud plugin. UI and plugin lifecycle only. References `TwentyOne.Game`.
- `TwentyOne.Tests/` - xUnit tests. References `TwentyOne.Game` only.

### BankLedger (pure functional bank accounting)

`BankLedger.Apply(long balance, BankTransaction) → (long NewBalance, BankTransactionEntry)`

- Lives in `TwentyOne.Game/BankLedger.cs` - no Dalamud dependency, fully unit-testable.
- `BankTransaction` is a discriminated union: `BankDeposit`, `BankWithdrawal`, `BankBet`, `BankWin`, `BankDoubleDown`, `BankSplit`, `BankBetAdjust`.
- Never produces a negative balance - debits clamp to zero.
- Returns both the new balance and a log entry (timestamp, kind, amount, post-transaction balance).
- `MainWindow` calls `ApplyBank(stat, tx)` which calls `BankLedger.Apply`, writes result back to `stat.Bank`, appends entry to `stat.BankLog`. No raw bank arithmetic anywhere else.
- Bank mutations are intentionally outside `GameState` and the undo stack - they represent real-money ledger entries.
- Double/split bank deduction happens at **Confirm** time (not at Dbl/Spl button click).
- `BankBetAdjust` carries a **signed** `Delta`: positive deducts (bet went up), negative refunds (bet went down). The stored `BankTransactionEntry.Amount` keeps the sign so the audit log shows direction.

### GameEngine (pure functional core)

`GameEngine.Apply(GameState, GameAction) → (GameState, IReadOnlyList<SideEffect>)`

- **Never mutates its input.** Always returns a new `GameState` object.
- Apply is a thin switch expression dispatching to per-action `Handle{Action}` static methods.
- Narration is handled via a `NarrationContext` record (captures templates, dealer name, effects list). Passed to each handler.
- `SideEffect` is currently `SendChat(string Text)` and `AutoHit(int, int)`.
- `MainWindow` calls `Apply`, sets `config.GameState = newState`, processes effects.

### Actions

All state mutations go through `GameEngine.Apply` via a `GameAction` discriminated union. Do not mutate `config.GameState` directly except via `config.SetGameRule` (reserved for non-undoable GameState fields like `SkipDealSummaryOnePlayer`) or `config.SeedRulesIntoGameState` (copies house rules from the active venue at StartDeal time).

### Undo

- Before every `Apply` call, `MainWindow` pushes `config.GameState` onto `config.UndoStack`.
- `NewRound` / Abort Round clears the stack instead of pushing.
- Undo restores previous `GameState` and pops the stack. `NarrationLog` is **not** restored.
- The undo stack is persisted in `Configuration`.

### State persistence

`config.Save()` called at the end of every `Apply` and `Undo`. `config.GameState` IS the live game state.

Fields in `Configuration` (persisted, outside undo):
- `GameState`, `UndoStack`, `NarrationLog`, `Venues` / `ActiveVenueIndex`, `VenueMemory`

`VenueSettings` holds per-venue config: chat, narration templates, dealer name, auto-trade/target, gil tracker, player stats, round history, session tracking, **house rules** (`BjPayout` / `CharliePayout` / `FiveCardCharlie`). Each venue has a stable `Guid Id`.

House rules live in two places by design: canonical on `VenueSettings` (edited via `RulesEditorWindow`, persisted per venue) and mirrored on `GameState` (snapshotted with undo entries and round history so historical rounds replay with the rules they were played under). `Configuration.SeedRulesIntoGameState()` copies venue → GameState; `MainWindow.Apply` invokes it on the `StartDeal` action. Rule edits during the Betting phase are picked up by this seed, so they apply to the round about to be dealt. Edits made during Deal or later do **not** affect the running round - the dealer must call `NewRound` (and then `StartDeal` again) for them to take effect. Rule edits themselves are not on the undo stack - they bypass `GameEngine.Apply` and write directly to `VenueSettings`.

`VenueSettings.RoundHistory` holds `RoundHistoryEntry` snapshots (one per completed round).

### Docs

`docs/` holds long-form notes about rule decisions and design rationale.

- `docs/dealer-hole-card.md` - explains why the engine uses European No-Hole-Card
  (ENHC) style, walks through how it differs from peek rules, and documents the
  ~0.11% house-edge impact. Read this before considering any changes to dealer
  BJ resolution (`GameEngine.cs:213`).
- `docs/edge-solver-verification.md` - records the methodology and reference
  numbers used to verify `EdgeSolver` against Wizard of Odds and the standard
  H17 DAS chart. Documents the 6 expected strategy-chart deviations so future
  refactors can distinguish "real bug" from "known borderline." Re-run the
  checks here any time the solver, engine payout resolution, or rule enum
  changes.
- `docs/edge-solver-extending.md` - architectural reference for the solver
  (memoization, split fixed-point) and a rule-by-rule cost table for adding
  new rule axes (S17, DAS toggle, HSA, RSA, peek, surrender, continuous
  payouts). Read before plumbing a new rule into `EdgeRules`.

### EdgeSolver (house-edge calculator)

`TwentyOne.Game/Edge/EdgeSolver.cs` - pure exact EV solver. Computes the expected house edge for a given `(BjPayout, CharliePayout, FiveCardCharlie)` triple under optimal player strategy with infinite-deck draws. No Dalamud dependency, fully unit-tested.

- Entry point: `EdgeSolver.ComputeHouseEdge(EdgeRules rules) -> double` (positive = house favored, negative = player favored).
- Computes dealer outcome distribution per upcard, then player EV via memoized recursion over `(total, isSoft, numCards, isFromSplit, upcard)`.
- Splits use a closed-form fixed-point to handle unlimited re-splits without infinite recursion: `S = max(H_neq + H_eq/13, (13/11) * H_neq)`.
- Uses fractional payout multipliers (1.5 / 1.2 / 1.0), not the engine's `Math.Ceiling` - difference is negligible at gil bet sizes.
- Matches engine quirks: dealer BJ beats player 3-card 21; splits require same rank not value; split aces auto-stand; DAS allowed; Charlie at exactly 5+ cards; Charlie LosesToDealerBJ checks 2-card dealer 21 only.
- Sweep of all 21 rule cells runs in <150 ms.
- `RulesEditorWindow` displays the live house edge and per-knob "vs default" deltas (cached and invalidated when any rule changes). Opened from `ConfigWindow` via the "Edit Blackjack Rules" button. Also exposes a Ctrl-held "Reset Rules" button that restores every rule to its `GameState` default.

### EdgeStats (realized vs theoretical comparison)

`TwentyOne.Game/Edge/EdgeStats.cs` - aggregates a sequence of `RoundHistoryEntry` into `AggregateStats(TotalWagered, RealizedBankNet, TheoreticalBankNet)`. Theoretical is computed by running `EdgeSolver` once per distinct rule set encountered (cached per-call) and summing `bet × edge` per round. With an `overrideRules` parameter, every round is evaluated under those rules instead of its snapshot rules.

- **Session Ledger** displays this live using the venue's *current* rules ("what should this session look like under my current rule set?").
- **History > Rounds This Session** uses each round's snapshot rules ("what should have happened given the rules in effect at the time").
- **History > Previous Sessions detail** displays the snapshot-rule version, locked in at session-archive time. `PlayerStatsSession` stores `TotalWagered` and `TheoreticalBankNet` so the figure stays stable even if venue rules change later. Pre-feature sessions have `TotalWagered = 0` and render as "-".

`EdgeStatsDisplay` (in `TwentyOne/Windows/EdgeStatsDisplay.cs`) is the shared ImGui render block used by all three views.

### Sessions

`SessionManager` (in `TwentyOne.Game/SessionManager.cs`) - pure static class:
- `TryStartSession`, `ShouldShowSessionBanner`, `BuildArchive`, `ResetGameStats`

`MainWindow` shows a session banner (dismissible, resets on territory change). `SessionLedgerWindow.NewSession()` archives session, resets stat fields, clears round history/tips/gil tracker.

### Venue memory

`Configuration.VenueMemory` maps housing address key (`"{territory}:{ward}:{plot}"`) to venue GUID. `Plugin.GetCurrentHousingAddressKey()` handles outdoor housing districts and indoor interiors. When deleting a venue, all `VenueMemory` entries referencing its GUID must be removed.

`MainWindow` shows a dismissible suggestion banner when the current location has a remembered venue that differs from the active one.

### History viewer mode

`MainWindow.isHistoryView` - when viewing a historical round via `HistoryWindow`:
- `UpdatePlayerStats` is a no-op.
- Current `GameState`, `UndoStack`, `RedoStack` saved in-memory, restored on `ExitHistoryView`.
- A banner shown; all other UI renders normally against the historical snapshot.

### Card input

All cards from FFXIV chat rolls (`/random 13` or `/dice 13`). `OnChatMessage` parses roll result, sets `deferredRoll`; applied at top of next `Draw()` to avoid re-entrancy.

### PendingPrompt (trade-result modals)

A single `PendingPrompt? pendingPrompt` field replaces the three mutable nullable-tuple fields:
```csharp
private abstract record PendingPrompt
{
    public sealed record Bet(int Pi, long Gil) : PendingPrompt;
    public sealed record BankDeposit(int Pi, long Gil) : PendingPrompt;
    public sealed record BankWithdraw(int Pi, long Gil) : PendingPrompt;
    public sealed record BetOrBank(int Pi, long Gil) : PendingPrompt;
}
```
Set by `OnChatMessage` switch over `TradeMonitor.Outcome`. Consumed by `DrawTradeBetPromptModal`, `DrawBetOrBankPromptModal`, `DrawBankTradePromptModal`. The type system prevents two prompts from being active simultaneously.

### MainWindow.Render.cs cell helpers

The player table in `Draw()` uses extracted per-cell methods for navigability:
- `DrawNameCell(RowCtx ctx, float cellRight)` - name with rename/spade/target/clear
- `DrawBetCell(RowCtx ctx, float cellRight)` - bet input/display + Trade + Remind
- `DrawCardsCell(Hand hand)` - hand string + "2x" badge
- `DrawScoreCell(IReadOnlyList<int> cards, HandState state)` - score colored by bust/21
- `DrawStatusCell(RowCtx ctx, float cellRight)` - payout result / hand state + Sit Out / Remind
- `DrawActionsCell(RowCtx ctx, ScenarioGates gates, float cellRight, ref int removePlayerIndex)` - Hit/Stand/Dbl/Spl/Confirm/Cancel/Remove
- `DrawSummaryRow(int pi, Player p, int displayPi, bool hasWorld, bool hasNickname, bool uiBusy)` - merged row for split-hand players
- `DrawBankCell(loopIdx, actualIdx, player, bankCellRight, uiBusy)` - bank balance/shortfall/manage
- `DrawBankManageButton(playerIndex, cellRight, idSuffix, uiBusy)` - Manage button

`RowCtx` and `ScenarioGates` are readonly record structs that bundle per-row state.

## Testing

Use only these player names in test cases: Lorah, Bekki, Nolla. If more than 3 names are needed, invent new ones. When a test requires a winning player, that player must always be Lorah. Write tests for all new features.

**Two distinct test layers - do not conflate them:**

- `TwentyOne.Tests/GameEngineTests.cs` - xUnit unit tests. Test `GameEngine.Apply()` calls in isolation. No Dalamud dependency. Covers individual action transitions, narration, payout math.
- `TwentyOne.Tests/SessionTests.cs` - unit tests for `SessionManager`: banner logic, archive building, stat reset, auto-session tracking.
- `TwentyOne.Tests/Helpers/GameStateBuilder.cs` - fluent builder for assembling `GameState` in tests. Supports `.Phase()`, `.Dealer()`, `.Player()`, `.ActiveHand()`, `.Charlie()`, `.BjPayout()`, `.DealerStandsOnSoft17()`, `.WaitingForNextPlayer()`, `.WaitingForDealer()`, `.SkipDealSummaryOnePlayer()`, `.LastRoundWinners()`, `.LastRoundPushers()`. Use the `Player(Player)` overload for complex cases (split hands, sitting out, doubled hands).
- `Scenarios/*.json` - human-replay integration tests. Loaded via DebugWindow in-game. Test the full stack: `MainWindow` orchestration (autoDealQueue deal sequence, deferred rolls, `AutoHit` side effects, button gating). Cannot be automated without replicating `MainWindow` logic separately, which creates a divergence risk. Run manually by loading and stepping/fast-forwarding in-game.

## Narration Templates

`NarrationTemplates` properties are `List<List<string>>` - the outer list is random variants (one picked per use via `Random.Shared`), the inner list is the sequence of chat lines sent for that variant. Defaults always have exactly one variant. The three `DealSummary*` properties remain plain `string` (they are concatenated components, not narrated independently).

Every narration string emitted via `SendChat` must have a corresponding property in `NarrationTemplates` and a row in `NarrationEditorWindow`. When adding a new `Narrate(...)` call in `GameEngine`, always:
1. Add a `List<List<string>>` property to `NarrationTemplates` with a sensible single-variant default.
2. Add an `NtListRow(...)` entry in the appropriate `NarrationEditorWindow` section (or a new section if needed).
3. Add a test in `NarrationTemplateTests` verifying the new template variable(s) are substituted (initialise the property as `[["template string"]]`).

## Branch

Active refactoring work is on the `refactor` branch (off `experimental`). Commit messages follow `type(scope): message` style. Every commit builds (Debug + Release) and passes tests.

## UI Rules

- Never use non-ASCII characters (icons, arrows, symbols) on buttons unless explicitly requested. Use plain text labels only.
- To right-align a button within a cell/region: use `SameLine()` followed by `if (GetCursorPosX() < targetX) SetCursorPosX(targetX)`, where `targetX = cellRight - buttonWidth` (button width = `CalcTextSize(...).X + FramePadding.X * 2`). Do **not** pass a position directly to `SameLine(pos)` - if `pos` is behind the current cursor, ImGui will clip or hide the widget.

### Record types for immutable state

`GameState`, `Player`, and `Hand` are `sealed record class` types. Use `with { ... }` to
create modified copies:
```csharp
state with { Phase = GamePhase.DealerTurn };
player with { Bet = "500" };
hand with { State = HandState.Stand };
```
The old `GameEngine.With(...)` optional-parameter helper has been removed.
`WithPlayer` (list-insert utility) and `WithHand` (hand-replacement utility) remain.

## Design Decisions

- Dealer hits on soft 17 by default; `DealerStandsOnSoft17` venue rule flips to S17.
- Surrender (`AllowSurrender`, off by default) is **early-style** because the engine is ENHC: the player forfeits exactly half their bet even when the dealer ends up with Blackjack. New `HandState.Surrendered`, new `PayoutResult.Surrender`, new `SurrenderHand` action. Surrender is only offered on the initial 2-card hand (not post-hit/split/double). Bank pays back `bet - ceil(bet/2)` at settlement; odd bets round in the house's favor by 1 gil.
- `BjPayout` is a free-form `double` multiplier (e.g. 1.5 = 3:2, 1.2 = 6:5, 1.0 = even money). Canonical on `VenueSettings`, mirrored on `GameState` so it is snapshotted with each undo entry. Edited via the `config.BjPayout` proxy (writes to the active venue); seeded into the live `GameState` by `Configuration.SeedRulesIntoGameState()` on `StartDeal`. Edits during Deal or later do not affect the running round. Engine payout uses `Math.Ceiling(bet * (decimal)state.BjPayout)`. CharliePayout remains an enum (3:2 / 6:5 / 1:1) since each value requires a full solver re-solve.
- `Player.Hands` supports multiple hands for splits. `GameState.ActiveHandIndex` tracks which hand is currently active alongside `ActivePlayerIndex`. `AdvanceFrom` iterates all `(player, hand)` pairs in order.
- Double Down and Split require additional funds before they take effect. The UI tracks this as `pendingDouble`/`pendingSplit` (not in `GameState`). Clicking Dbl/Spl fires `AnnounceDouble`/`AnnounceSplit` (which picks a bank-covers or trade-required narration template based on current bank balance) and optionally opens the trade window. The actual `DoubleDown`/`SplitHand` action fires only after the dealer clicks Confirm. Bank deduction via `BankLedger` happens at Confirm time so any mid-round deposits land first.
- Bet adjustment during the Deal phase (between Start Deal and Begin Player Turns): the dealer can click "Adjust" next to a player's bet to change it after cards have started dealing. `MainWindow.TryAdjustBet` computes the delta vs. the prior bet, validates bank can cover an increase (shortfall blocks the commit), applies a single `BankBetAdjust(delta)` ledger entry, then dispatches an `AdjustBet` action. The `AdjustBet` action has `PushesUndo => false` - bank entries are append-only, so a state-only revert would diverge from the ledger. Only allowed in `GamePhase.Deal`; sitting-out players are ignored. Cleared on `NewRound` and `BeginPlayerTurns`.
- `AnnounceDouble` and `AnnounceSplit` are excluded from the undo stack (like `AnnounceBettingOpen`).
- Split rules: re-splits allowed (no limit); 21 on a split hand (`IsFromSplit=true`) is Playing/Stand, never Blackjack; split aces receive exactly one card then auto-stand (standard casino rule, see ToDo.txt for variant note).
- Payout is calculated per-hand. `Hand.Bet` holds the effective bet when a hand has been doubled (empty = inherit `Player.Bet`).
- `WaitingForDealer` must never be set unconditionally when transitioning to `DealerTurn`. Always derive it as `!CanGoToPayout(provisionalDealerState)`. This ensures special cases (all-bust, all-BJ, all-Charlie, or mixed terminal winning hands with safe upcard) skip the "Begin Dealer Turn" prompt and show "Go to Payout" directly. `CanGoToPayout` is the single source of truth for whether the dealer still needs to act.
- All-bust: `AdvanceFrom` returns `GamePhase.Payout`; the engine maps this to `DealerTurn` with `WaitingForDealer=false`. The dealer Hit button and recommendation label are suppressed when all hands are bust. `CanGoToPayout` returns `true` immediately for all-bust.
- All-BJ, all-Charlie (LosesToDealerBJ), or mixed BJ+Charlie: dealer must reveal their hole card only if their upcard is an ace or 10-value (could be BJ or Charlie-losing scenario). `CanGoToPayout` checks `allTerminalWin` for mixed hands, applying the same upcard rule. When the hole card is not needed, the game goes straight to "Go to Payout". `AnnounceDealerHit` emits `DealerBJCheck` instead of `DealerHitAnnounce` when all players have Blackjack.
- `BeginPlayerTurns` auto-skips consecutive BJ hands when all remaining active hands are BJ. Scans ahead through BJ hands; if all are BJ, transitions directly to `DealerTurn` (or `Payout`) with no narration. If a non-BJ hand follows, narrates "moving along" for the first BJ and waits (old behavior). `AdvanceToNextPlayer` does NOT auto-skip - narrates and waits for each BJ hand encountered mid-round. The `allTerminalWin` check in `CanGoToPayout` ensures mixed BJ+Charlie hands skip the dealer turn when appropriate.
