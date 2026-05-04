## Meta

After completing any feature or significant design decision, update CLAUDE.md to reflect it. Keep architecture sections current — future sessions depend on this file for context.

## Project

This is a Final Fantasy XIV plugin that uses the Dalamud API (https://dalamud.dev/api/).

The FFXIVClientStructs repo is cloned at `FFXIVClientStructs/` for reference. Consult it locally before going to the web for information about FFXIV client structures.

It's a plugin meant for a dealer to use to run a blackjack game in a venue.

## Debugging

Plugin config is saved to `/home/sidney/.xlcore/pluginConfigs/TwentyOne.json`.

### Debug build (DEBUG-only UI features)

All debug tooling is `#if DEBUG` — absent in Release builds. Enable via the **Debug** button in the MainWindow top bar (appears in Debug builds only).

#### DebugWindow

- **Roll queue** — pre-load card values (1–13) that `QueueHitRoll` consumes instead of sending chat rolls. Manual entry or loaded from scenario file.
- **Scenario** — loads a JSON scenario file that sets up players/bets, enqueues rolls, and scripts the exact sequence of UI button clicks to execute. An orange banner appears in MainWindow while a scenario is active showing the next required step.
- **Snapshot save/load** — serialize/restore `GameState` directly for reproducing specific game states.

#### Scenario format

```json
{
  "name": "Human-readable name",
  "players": [
    { "name": "Lorah", "bet": "1000" },
    { "name": "Bekki", "bet": "500" }
  ],
  "rolls": [1, 10, 7, 6],
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

- `TwentyOne.Game/` — pure .NET library, no Dalamud dependency. Contains all game logic.
- `TwentyOne/` — Dalamud plugin. UI and plugin lifecycle only. References `TwentyOne.Game`.
- `TwentyOne.Tests/` — xUnit tests. References `TwentyOne.Game` only.

### BankLedger (pure functional bank accounting)

`BankLedger.Apply(long balance, BankTransaction) → (long NewBalance, BankTransactionEntry)`

- Lives in `TwentyOne.Game/BankLedger.cs` — no Dalamud dependency, fully unit-testable.
- `BankTransaction` is a discriminated union: `BankDeposit`, `BankWithdrawal`, `BankBet`, `BankWin`, `BankDoubleDown`, `BankSplit`.
- Never produces a negative balance — debits clamp to zero.
- Returns both the new balance and a log entry (timestamp, kind, amount, post-transaction balance).
- `BankTransactionKind` and `BankTransactionEntry` also live here (moved from `Configuration.cs`).
- `MainWindow` calls `ApplyBank(stat, tx)` which is a thin wrapper: calls `BankLedger.Apply`, writes result back to `stat.Bank`, appends entry to `stat.BankLog`. **No raw bank arithmetic anywhere else.**
- Bank mutations are intentionally outside `GameState` and the undo stack — they represent real-money ledger entries and must not be reversed by game undo.
- Double/split bank deduction happens at **Confirm** time (not at the Dbl/Spl button click), so any trades the player makes between click and confirm are already deposited before the deduction fires.

### GameEngine (pure functional core)

`GameEngine.Apply(GameState, GameAction) → (GameState, IReadOnlyList<SideEffect>)`

- **Never mutates its input.** Always returns a new `GameState` object.
- All game logic lives here: card math, phase transitions, narration text, payout calculation.
- `SideEffect` is currently just `SendChat(string Text)` — a narration line.
- `MainWindow` calls `Apply`, sets `config.GameState = newState`, then processes effects (appends to `NarrationLog`, optionally sends to FFXIV chat).

### Actions

All state mutations go through `GameEngine.Apply` via a `GameAction` discriminated union. Do not mutate `config.GameState` directly except for settings that are intentionally outside the undo system (e.g. `BjPayout` in ConfigWindow).

### Undo

- Before every `Apply` call, `MainWindow` pushes `config.GameState` onto `config.UndoStack` (no cloning needed — GameEngine is pure and never touches the old reference).
- `NewRound` / Abort Round clears the stack instead of pushing.
- Undo restores the previous `GameState` and pops the stack. `NarrationLog` is **not** restored on undo (it is a permanent session log).
- The undo stack is persisted in `Configuration` so it survives plugin restarts within the same round.

### State persistence

`config.Save()` is called at the end of every `Apply` and `Undo`. There is no separate `SaveState`/`LoadState` conversion step — `config.GameState` IS the live game state.

Fields that belong in `Configuration` (persisted, outside undo):
- `GameState` — current round state
- `UndoStack` — undo history for the current round
- `NarrationLog` — session-wide narration history (never undone)
- `Venues` / `ActiveVenueIndex` — all venue-specific settings live in `VenueSettings`; proxy properties on `Configuration` delegate to `ActiveVenue` so call sites need not change
- `VenueMemory` — global `Dictionary<string, string>` mapping housing address key (`"{territory}:{ward}:{plot}"`) to venue GUID; updated when user manually switches venues in a housing zone

`VenueSettings` holds all per-venue config: chat, narration templates, dealer name, auto-trade/target, gil tracker, player stats, round history, and session tracking. Each venue has a stable `Guid Id` (never changes, survives renames). Venue switching is allowed during `GamePhase.Betting` but blocked once a round is in progress (any other phase).

`VenueSettings.RoundHistory` holds `RoundHistoryEntry` snapshots (one per completed round). Each entry stores the `GameState` at payout, the bank net for that round, and a round number. Appended by `UpdatePlayerStats` after `GoToPayout`.

`VenueSettings.ActiveSessionStartedAt` / `ActiveSessionLocationKey` — set on first `GoToPayout` of a new night via `SessionManager.TryStartSession`. Cleared by `NewSession`. Used to detect when a new session banner should be shown.

`VenueSettings.StatsSessions` holds `PlayerStatsSession` archives. Each session stores a snapshot of `PlayerStatData` (no Bank/BankLog), `List<RoundSummary>`, `BankNet`, `LocationKey`, and `Date`. Archived by `NewSession` in `SessionLedgerWindow`.

### Sessions

`SessionManager` (in `TwentyOne.Game/SessionManager.cs`) is a pure static class with no Dalamud dependency:
- `TryStartSession` — sets `ActiveSessionStartedAt` / `ActiveSessionLocationKey` if not already set.
- `ShouldShowSessionBanner` — returns true if session is null+rounds>0, or stale (>8h), or location changed.
- `BuildArchive` — converts live stats + round history into snapshot types for archiving.
- `ResetGameStats` — zeroes perf fields on `PlayerStatData` objects.

`MainWindow` shows a session banner (dismissible, resets on territory change) when `ShouldShowSessionBanner` is true. Banner includes inline venue dropdown and "New Session" button that delegates to `SessionLedgerWindow.NewSession()`.

`SessionLedgerWindow.NewSession()` (public): archives session, resets live stat fields (preserves Bank/BankLog), clears RoundHistory/Tips, resets GilTracker, clears session tracking fields, saves.

`RoundSummary` (in `TwentyOne.Game/GameTypes.cs`) — lightweight per-round archive record. No `GameState` snapshot; stores `RoundNumber`, `BankNet`, and `PlayerBanks` deltas only.

`PlayerStatData` (in `TwentyOne.Game/SessionManager.cs`) — game-layer stat snapshot (no Bank/BankLog). Used in archived `PlayerStatsSession.Stats`.

### Venue memory

`Configuration.VenueMemory` records which venue the user chose at each housing location. Address keys are `"{territory}:{ward}:{plot}"` (1-indexed). `Plugin.GetCurrentHousingAddressKey()` handles both outdoor housing districts and indoor house interiors (via `LastOutdoorHousingTerritoryId`, updated on `TerritoryChanged`). Outdoor housing territory IDs: Mist=339, Lavender Beds=340, The Goblet=341, Shirogane=641, Empyreum=979. When deleting a venue, all `VenueMemory` entries referencing its GUID must be removed.

`MainWindow` shows a dismissible suggestion banner when the current location has a remembered venue that differs from the active one. The banner resets on territory change.

### History viewer mode

`MainWindow.isHistoryView` is true when the user is viewing a historical round via `HistoryWindow`. While active:
- `UpdatePlayerStats` is a no-op (no stats changes, no new history entry).
- The current `GameState`, `UndoStack`, and `RedoStack` are saved in-memory and restored on `ExitHistoryView`.
- A banner is shown at the top of `MainWindow`; all other UI renders normally against the historical snapshot.

### Card input

All cards come from FFXIV chat rolls (`/random 13` or `/dice 13`). There are no manual text-entry fields. `OnChatMessage` parses the roll result and sets `deferredRoll`; the deferred value is applied at the top of the next `Draw()` to avoid re-entrancy with the chat system.

## Testing

Use only these player names in test cases: Lorah, Bekki, Nolla. If more than 3 names are needed, invent new ones. When a test requires a winning player, that player must always be Lorah. Write tests for all new features.

**Two distinct test layers — do not conflate them:**

- `TwentyOne.Tests/GameEngineTests.cs` — xUnit unit tests. Test `GameEngine.Apply()` calls in isolation. No Dalamud dependency. Covers individual action transitions, narration, payout math.
- `TwentyOne.Tests/SessionTests.cs` — unit tests for `SessionManager`: banner logic, archive building, stat reset, auto-session tracking.
- `Scenarios/*.json` — human-replay integration tests. Loaded via DebugWindow in-game. Test the full stack: `MainWindow` orchestration (autoDealQueue deal sequence, deferred rolls, `AutoHit` side effects, button gating). Cannot be automated without replicating `MainWindow` logic separately, which creates a divergence risk. Run manually by loading and stepping/fast-forwarding in-game.

## Narration Templates

`NarrationTemplates` properties are `List<List<string>>` — the outer list is random variants (one picked per use via `Random.Shared`), the inner list is the sequence of chat lines sent for that variant. Defaults always have exactly one variant. The three `DealSummary*` properties remain plain `string` (they are concatenated components, not narrated independently).

Every narration string emitted via `SendChat` must have a corresponding property in `NarrationTemplates` and a row in `ConfigWindow.DrawNarrationTemplates`. When adding a new `Narrate(...)` call in `GameEngine`, always:
1. Add a `List<List<string>>` property to `NarrationTemplates` with a sensible single-variant default.
2. Add an `NtListRow(...)` entry in the appropriate `ConfigWindow` section (or a new section if needed).
3. Add a test in `NarrationTemplateTests` verifying the new template variable(s) are substituted (initialise the property as `[["template string"]]`).

## UI Rules

- Never use non-ASCII characters (icons, arrows, symbols) on buttons unless explicitly requested. Use plain text labels only.
- To right-align a button within a cell/region: use `SameLine()` followed by `if (GetCursorPosX() < targetX) SetCursorPosX(targetX)`, where `targetX = cellRight - buttonWidth` (button width = `CalcTextSize(...).X + FramePadding.X * 2`). Do **not** pass a position directly to `SameLine(pos)` — if `pos` is behind the current cursor, ImGui will clip or hide the widget.

## Design Decisions

- Dealer hits on soft 17.
- `BjPayout` (3:2 / 6:5 / 1:1) is a venue setting stored in `GameState` so it is snapshotted with each undo entry. It is changed directly (not via `Apply`) since payout changes are not undoable game actions.
- `Player.Hands` supports multiple hands for splits. `GameState.ActiveHandIndex` tracks which hand is currently active alongside `ActivePlayerIndex`. `AdvanceFrom` iterates all `(player, hand)` pairs in order.
- Double Down and Split require additional funds before they take effect. The UI tracks this as `pendingDouble`/`pendingSplit` (not in `GameState`). Clicking Dbl/Spl fires `AnnounceDouble`/`AnnounceSplit` (which picks a bank-covers or trade-required narration template based on current bank balance) and optionally opens the trade window. The actual `DoubleDown`/`SplitHand` action fires only after the dealer clicks Confirm. Bank deduction via `BankLedger` happens at Confirm time so any mid-round deposits land first.
- `AnnounceDouble` and `AnnounceSplit` are excluded from the undo stack (like `AnnounceBettingOpen`).
- Split rules: re-splits allowed (no limit); 21 on a split hand (`IsFromSplit=true`) is Playing/Stand, never Blackjack; split aces receive exactly one card then auto-stand (standard casino rule, see ToDo.txt for variant note).
- Payout is calculated per-hand. `Hand.Bet` holds the effective bet when a hand has been doubled (empty = inherit `Player.Bet`).
- `WaitingForDealer` must never be set unconditionally when transitioning to `DealerTurn`. Always derive it as `!CanGoToPayout(provisionalDealerState)`. This ensures special cases (all-bust, all-BJ with safe upcard) skip the "Begin Dealer Turn" prompt and show "Go to Payout" directly. `CanGoToPayout` is the single source of truth for whether the dealer still needs to act.
- All-bust: `AdvanceFrom` returns `GamePhase.Payout`; the engine maps this to `DealerTurn` with `WaitingForDealer=false`. The dealer Hit button and recommendation label are suppressed when all hands are bust. `CanGoToPayout` returns `true` immediately for all-bust.
- All-BJ: dealer must reveal their hole card only if their upcard is an ace or 10-value (could be BJ). `CanGoToPayout` encodes this rule. When the hole card is not needed, the game goes straight to "Go to Payout". `AnnounceDealerHit` emits `DealerBJCheck` instead of `DealerHitAnnounce` when all players have Blackjack.
