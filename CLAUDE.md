## Meta

After completing any feature or significant design decision, update CLAUDE.md to reflect it. Keep architecture sections current — future sessions depend on this file for context.

## Project

This is a Final Fantasy XIV plugin that uses the Dalamud API (https://dalamud.dev/api/).

The FFXIVClientStructs repo is cloned at `FFXIVClientStructs/` for reference. Consult it locally before going to the web for information about FFXIV client structures.

It's a plugin meant for a dealer to use to run a blackjack game in a venue.

## Debugging

Plugin config is saved to `/home/sidney/.xlcore/pluginConfigs/TwentyOne.json`.

## Build

Build commands:
```bash
dotnet build TwentyOne/TwentyOne.csproj -c Debug
dotnet build TwentyOne/TwentyOne.csproj -c Release
dotnet test TwentyOne.Tests/TwentyOne.Tests.csproj
```

## Architecture

### Project layout

- `TwentyOne.Game/` — pure .NET library, no Dalamud dependency. Contains all game logic.
- `TwentyOne/` — Dalamud plugin. UI and plugin lifecycle only. References `TwentyOne.Game`.
- `TwentyOne.Tests/` — xUnit tests. References `TwentyOne.Game` only.

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

`VenueSettings` holds all per-venue config: chat, narration templates, dealer name, auto-trade/target, gil tracker, player stats, round history. Each venue has a stable `Guid Id` (never changes, survives renames). Venue switching is allowed during `GamePhase.Betting` but blocked once a round is in progress (any other phase).

`VenueSettings.RoundHistory` holds `RoundHistoryEntry` snapshots (one per completed round). Each entry stores the `GameState` at payout, the bank net for that round, and a round number. Appended by `UpdatePlayerStats` after `GoToPayout`.

### Venue memory

`Configuration.VenueMemory` records which venue the user chose at each housing location. Address keys are `"{territory}:{ward}:{plot}"` (1-indexed). `Plugin.GetCurrentHousingAddressKey()` handles both outdoor housing districts and indoor house interiors (via `LastOutdoorHousingTerritoryId`, updated on `TerritoryChanged`). Outdoor housing territory IDs: Mist=339, Lavender Beds=340, The Goblet=341, Shirogane=641, Empyreum=979. When deleting a venue, all `VenueMemory` entries referencing its GUID must be removed.

`MainWindow` shows a dismissible suggestion banner when the current location has a remembered venue that differs from the active one. The banner resets on territory change.

### History viewer mode

`MainWindow.isHistoryView` is true when the user is viewing a previous round via `RoundHistoryWindow`. While active:
- `UpdatePlayerStats` is a no-op (no stats changes, no new history entry).
- The current `GameState`, `UndoStack`, and `RedoStack` are saved in-memory and restored on `ExitHistoryView`.
- A banner is shown at the top of `MainWindow`; all other UI renders normally against the historical snapshot.

### Card input

All cards come from FFXIV chat rolls (`/random 13` or `/dice 13`). There are no manual text-entry fields. `OnChatMessage` parses the roll result and sets `deferredRoll`; the deferred value is applied at the top of the next `Draw()` to avoid re-entrancy with the chat system.

## Testing

Use only these player names in test cases: Lorah, Bekki, Nolla. If more than 3 names are needed, invent new ones. When a test requires a winning player, that player must always be Lorah. Write tests for all new features.

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
- Double Down and Split require a trade from the player before they take effect. The UI tracks this as `pendingDouble`/`pendingSplit` (not in `GameState`). Clicking Dbl/Spl fires an `AnnounceDouble`/`AnnounceSplit` narration-only action and opens the trade window; the actual `DoubleDown`/`SplitHand` action fires only after the dealer clicks Confirm.
- `AnnounceDouble` and `AnnounceSplit` are excluded from the undo stack (like `AnnounceBettingOpen`).
- Split rules: re-splits allowed (no limit); 21 on a split hand (`IsFromSplit=true`) is Playing/Stand, never Blackjack; split aces receive exactly one card then auto-stand (standard casino rule, see ToDo.txt for variant note).
- Payout is calculated per-hand. `Hand.Bet` holds the effective bet when a hand has been doubled (empty = inherit `Player.Bet`).
- `WaitingForDealer` must never be set unconditionally when transitioning to `DealerTurn`. Always derive it as `!CanGoToPayout(provisionalDealerState)`. This ensures special cases (all-bust, all-BJ with safe upcard) skip the "Begin Dealer Turn" prompt and show "Go to Payout" directly. `CanGoToPayout` is the single source of truth for whether the dealer still needs to act.
- All-bust: `AdvanceFrom` returns `GamePhase.Payout`; the engine maps this to `DealerTurn` with `WaitingForDealer=false`. The dealer Hit button and recommendation label are suppressed when all hands are bust. `CanGoToPayout` returns `true` immediately for all-bust.
- All-BJ: dealer must reveal their hole card only if their upcard is an ace or 10-value (could be BJ). `CanGoToPayout` encodes this rule. When the hole card is not needed, the game goes straight to "Go to Payout". `AnnounceDealerHit` emits `DealerBJCheck` instead of `DealerHitAnnounce` when all players have Blackjack.
