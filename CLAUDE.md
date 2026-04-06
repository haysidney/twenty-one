## Project

This is a Final Fantasy XIV plugin that uses the Dalamud API (https://dalamud.dev/api/).

The FFXIVClientStructs repo is cloned at `FFXIVClientStructs/` for reference. Consult it locally before going to the web for information about FFXIV client structures.

It's a plugin meant for a dealer to use to run a blackjack game in a venue.

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
- `ChatEnabled`, `ChatChannel` — venue settings
- `NarrationUseChannelCommand`, `NarrationPanelOpen` — UI preferences

### Card input

All cards come from FFXIV chat rolls (`/random 13` or `/dice 13`). There are no manual text-entry fields. `OnChatMessage` parses the roll result and sets `deferredRoll`; the deferred value is applied at the top of the next `Draw()` to avoid re-entrancy with the chat system.

## Testing

Use only these player names in test cases: Lorah, Bekki, Nolla. If more than 3 names are needed, invent new ones. When a test requires a winning player, that player must always be Lorah. Write tests for all new features.

## Design Decisions

- Dealer hits on soft 17.
- `BjPayout` (3:2 / 6:5 / 1:1) is a venue setting stored in `GameState` so it is snapshotted with each undo entry. It is changed directly (not via `Apply`) since payout changes are not undoable game actions.
- `Player.Hands` supports multiple hands for splits. `GameState.ActiveHandIndex` tracks which hand is currently active alongside `ActivePlayerIndex`. `AdvanceFrom` iterates all `(player, hand)` pairs in order.
- Double Down and Split require a trade from the player before they take effect. The UI tracks this as `pendingDouble`/`pendingSplit` (not in `GameState`). Clicking Dbl/Spl fires an `AnnounceDouble`/`AnnounceSplit` narration-only action and opens the trade window; the actual `DoubleDown`/`SplitHand` action fires only after the dealer clicks Confirm.
- `AnnounceDouble` and `AnnounceSplit` are excluded from the undo stack (like `AnnounceBettingOpen`).
- Split rules: re-splits allowed (no limit); 21 on a split hand (`IsFromSplit=true`) is Playing/Stand, never Blackjack; split aces receive exactly one card then auto-stand (standard casino rule, see ToDo.txt for variant note).
- Payout is calculated per-hand. `Hand.Bet` holds the effective bet when a hand has been doubled (empty = inherit `Player.Bet`).
