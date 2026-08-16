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
  "resplitCap": "Max4",
  "doubleRestriction": "Any",
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

Each rule override is optional; omitted fields use the standard defaults (3:2 BJ, EvenMoney Charlie, Charlie disabled, H17, DAS-on, no HSA / RSA / Surrender, ResplitCap Max4, DoubleRestriction Any). Scenarios are intentionally insulated from the active venue's rules so they stay reproducible.

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

**Never bundle an assembly Dalamud provides (Newtonsoft.Json, ImGui, etc.).** The
plugin loads them from Dalamud at runtime. A non-Dalamud project that needs one
for compilation (e.g. `TwentyOne.Game` uses Newtonsoft for `JObject`) must mark
the reference compile-only with `<ExcludeAssets>runtime</ExcludeAssets>`; the
test project references it normally (no Dalamud there). If a second copy ships in
`bin/Debug`, the plugin's `[JsonIgnore]`/`[JsonExtensionData]` attribute types
won't match the ones Dalamud's serializer checks for, so they're silently ignored
- proxies serialize, nothing captures into `ExtraData`, and the config bloats.
This is the bug behind `docs/troubleshooting/config-file-bloat.md`. Sanity check:
`bin/Debug/Newtonsoft.Json.dll` must NOT exist. Symptom signature: serialization
attributes "work in tests but not in-game".

## Architecture

### Project layout

- `TwentyOne.Game/` - pure .NET library, no Dalamud dependency. Contains all game logic.
- `TwentyOne/` - Dalamud plugin. UI and plugin lifecycle only. References `TwentyOne.Game`.
- `TwentyOne.Tests/` - xUnit tests. References `TwentyOne.Game` only.

### BankLedger (pure functional bank accounting)

`BankLedger.Apply(long balance, BankTransaction) → (long NewBalance, BankTransactionEntry)`

- Lives in `TwentyOne.Game/BankLedger.cs` - no Dalamud dependency, fully unit-testable.
- `BankTransaction` is a discriminated union: `BankDeposit`, `BankWithdrawal`, `BankBet`, `BankWin`, `BankDoubleDown`, `BankSplit`, `BankBetAdjust`, `BankSurrender`, `BankCredit`.
- Never produces a negative balance - debits clamp to zero.
- Returns both the new balance and a log entry (timestamp, kind, amount, post-transaction balance).
- `MainWindow` calls `ApplyBank(stat, tx)` which calls `BankLedger.Apply`, writes result back to `stat.Bank`, appends entry to `stat.BankLog`. No raw bank arithmetic anywhere else.
- Bank mutations are intentionally outside `GameState` and the undo stack - they represent real-money ledger entries.
- Double/split bank deduction happens at **Confirm** time (not at Dbl/Spl button click).
- `BankBetAdjust` carries a **signed** `Delta`: positive deducts (bet went up), negative refunds (bet went down). The stored `BankTransactionEntry.Amount` keeps the sign so the audit log shows direction.
- `BankCredit` is a venue-funded deposit (VIP / free play). Conceptually the venue pre-loads the dealer's starting gil with a credit pool; "issuing credit" relabels that gil into the player's bank without any physical trade. The bank ledger treats it like a deposit (real balance goes up). The session-ledger reconciliation includes `creditIssued` (sum of `BankCredit` entries this session) in the balance check: `adjustedDiff + grandTotal + creditIssued == 0`. The "Credits issued" line appears in the ledger for venue settlement reporting - the venue covers this cost out-of-band (e.g., off their cut).

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

- Before every `Apply` call, `MainWindow` pushes `config.GameState` onto `config.UndoStack` (via `PushUndoSnapshot`).
- `NewRound` / Abort Round clears the stack instead of pushing.
- Undo restores previous `GameState` and pops the stack. `NarrationLog` is **not** restored.
- The undo stack is persisted in `Configuration`.

#### Undo vs bank ops (compensating reversals)

Bank deductions (`BankBet` at StartDeal, `BankDoubleDown`/`BankSplit` at confirm)
live outside `GameState`, so a plain state-restore would leave balances diverged
and re-dealing would double-charge. Fix:

- `UndoStack` / `RedoStack` are `List<UndoEntry>` where `UndoEntry { GameState
  State; List<UndoBankOp> BankOps }`. Bundling the snapshot with its bank ops means
  they can't desync - clearing/popping the stack carries the ops with it (so the
  external `UndoStack.Clear()` sites in DebugWindow / SessionLedgerWindow are
  automatically correct). Pushing goes through `PushUndoSnapshot`; clearing through
  `ClearUndoState`. This changed the persisted stack shape, so it has a schema-v4
  migration that empties the stacks (undo state is ephemeral - see ConfigMigrations).
- `ApplyBankUndoable(player, tx)` records the op's signed `BalanceEffect` onto the
  current entry's `BankOps`. Used only by StartDeal / Confirm Double / Confirm
  Split. Plain `ApplyBank` (trades, manage, **payout settlement**) is *not* tracked.
- Undo across a non-empty bucket opens `DrawUndoConfirmModal` describing each
  reversal; on confirm, `ConfirmUndoWithReversals` posts `BankReversal(-effect)`
  per op (+ `[Audit]` narration), pops, and clears redo (no redo across a
  reversal). `BankReversal(long Delta)` is the ledger primitive (signed; clamps >=0).
- **Undo is blocked in `Payout`** - settlement also bumped round history / stat
  counters that undo can't unwind. Use New Round.
- **Abort Round** calls `RefundRoundBankOps` (reverse every bucket) so a misdeal
  returns bets/doubles/splits instead of pocketing them.

### State persistence

`config.Save()` called at the end of every `Apply` and `Undo`. `config.GameState` IS the live game state.

Fields in `Configuration` (persisted, outside undo):
- `GameState`, `UndoStack`, `NarrationLog`, `Venues` / `ActiveVenueIndex`, `VenueMemory`

`VenueSettings` holds per-venue config: chat, narration templates, dealer name, auto-trade/target, gil tracker, player stats, round history, session tracking, **house rules** (`BjPayout` / `CharliePayout` / `FiveCardCharlie`). Each venue has a stable `Guid Id`.

House rules live in two places by design: canonical on `VenueSettings` (edited via `RulesEditorWindow`, persisted per venue) and mirrored on `GameState` (snapshotted with undo entries and round history so historical rounds replay with the rules they were played under). `Configuration.SeedRulesIntoGameState()` copies venue → GameState; `MainWindow.Apply` invokes it on the `StartDeal` action. Rule edits during the Betting phase are picked up by this seed, so they apply to the round about to be dealt. Edits made during Deal or later do **not** affect the running round - the dealer must call `NewRound` (and then `StartDeal` again) for them to take effect. Rule edits themselves are not on the undo stack - they bypass `GameEngine.Apply` and write directly to `VenueSettings`.

`VenueSettings.RoundHistory` holds `RoundHistoryEntry` snapshots (one per completed round). Each entry carries the full `GameState`, bank net, pre- and post-payout player balances, `StartedAt`/`FinishedAt` timestamps, and the engine-action sequence (`Actions`) produced by `ActionLog.Format` - e.g. `["StartDeal", "Deal:D:7", "Deal:0:0:10", ..., "Stand:0:0", "BeginDealerTurn", "Deal:D:6", "GoToPayout"]`. Announcements (narration-only actions) are not logged. Undo doesn't pop log entries: the list is a faithful record of every `Apply` call that happened during the round, including any that were later reverted.

### Config schema migrations

The persisted JSON shape is versioned via `Configuration.SchemaVersion` (separate from Dalamud's `IPluginConfiguration.Version`, which we don't touch). `ConfigMigrations.CurrentSchemaVersion` is the latest. On every plugin load, `Plugin.MigrateConfigFileIfNeeded()` parses the config file as a `JObject`, runs `ConfigMigrations.Migrate(root)`, and writes the migrated JSON back **before** Dalamud's strong-typed loader runs. If the on-disk version is below current, a sibling backup `TwentyOne.json.bak-schema-{oldVersion}-{timestamp}` is written first.

`Plugin.StampPluginVersion()` runs after the typed config is loaded; it sets `Configuration.PluginVersion`, `Configuration.SchemaVersion`, and each venue's `LastModifiedPluginVersion` from the assembly version, then saves.

**Forward-compat (unknown fields are preserved):** these types carry `[JsonExtensionData] public Dictionary<string, JToken> ExtraData { get; set; }`:

- `Configuration`, `VenueSettings`, `PlayerStat`, `PlayerStatsSession`, `ServiceCharge`
- `NarrationTemplates`, `BankTransactionEntry`, `RoundHistoryEntry`

Records (`GameState`, `Player`, `Hand`) are deliberately excluded: extension data would change synthesized record equality and break engine/undo logic that compares hands.

**The version-gated cleanup (this is the load-time rule that keeps `ExtraData` honest):** `[JsonExtensionData]` cannot distinguish a genuine *future* field from an *orphan* (a field that was renamed, removed, or became `[JsonIgnore]`). Left alone it re-emits orphans forever, which is what once ballooned a config to ~1 GB. So on load `Plugin` applies one rule:

> If the on-disk `SchemaVersion <= CurrentSchemaVersion`, every captured key is provably an orphan -> clear **all** `ExtraData`. If it is greater (a config from a newer plugin), keep `ExtraData` so the unknown fields survive the downgrade round-trip.

`ExtensionDataCleaner.ClearAll` (in `TwentyOne.Game`) does the clearing: it reflects over the config graph and empties every `[JsonExtensionData]` dictionary. It is **safe by construction** - those dictionaries only ever hold unknown keys, never real typed data, so it cannot lose config data. It descends only into `TwentyOne.*` types plus collections, skips `[JsonIgnore]` proxies, and is cycle-safe. The `Plugin` call site reads `Configuration.SchemaVersion` *before* `StampPluginVersion` overwrites it, and is wrapped in try/catch so a cleanup bug can never block plugin load.

**Bumping the schema:**

1. Increment `ConfigMigrations.CurrentSchemaVersion`.
2. Add an `if (version < N) { ...; version = N; }` block in `ConfigMigrations.Migrate`. Each block must end by writing its target version into the JObject so a crash mid-chain still results in an idempotent rerun.
3. Snapshot a real `Save()` output from before the bump into `TwentyOne.Tests/Fixtures/config-v{N-1}.json` and add a `ConfigMigrationTests` case asserting the migrated shape.

**Rules to remember:**

- **Removals do NOT need a migration step.** The version-gated cleanup above drops any orphaned key on load, so a removed/renamed/now-`[JsonIgnore]` field cleans itself. A migration step is only for *transforming* surviving data (rename a key while keeping its value, restructure an object, backfill a default) - not for deletion.
- **`[JsonExtensionData]` emits captured keys as flat siblings**, not nested under an `"ExtraData"` object. A config can therefore show an empty `"ExtraData": {}` while still carrying orphan keys at the parent's root - do not trust an empty ExtraData object as proof of a clean file. (This is why the cleanup empties the typed `ExtraData` dictionaries after load rather than diffing the raw JSON.)
- **Migrations operate on raw `JObject`, not the typed `Configuration`.** This lets a migration handle fields that no longer exist as CLR properties.
- **Idempotency:** writing the new version into the JObject at the end of each step guarantees a re-run from any partial state still converges.
- **`$type` markers** (from Newtonsoft's `TypeNameHandling`) are recognized by the serializer and consumed before extension-data capture - they should not appear in `ExtraData`. If you see one, suspect a serializer setting drift.

### Session persistence

Archived sessions (`PlayerStatsSession`) are **not** stored in the main config JSON. Each session lives in its own file at `{ConfigDirectory}/sessions/{venueId}/{yyyy-MM-dd_HHmmss}-{shortGuid}.json`.

- `SessionStore` (in `TwentyOne/SessionStore.cs`) handles disk I/O: `LoadAll(venueId)`, `Save(venueId, session)`, `Delete(venueId, session)`.
- `VenueSettings.StatsSessions` is `[JsonIgnore]` and acts as an in-memory cache: populated by `Plugin` at startup from `SessionStore.LoadAll`, appended on `NewSession`.
- Each `PlayerStatsSession` carries `List<RoundHistoryEntry>` (full per-round snapshots), `PlayerBankLogs` (per-player `BankLog` captured at archive time), `TotalWagered` + `TheoreticalBankNet` (cached edge aggregates), and `PluginVersion` (string from assembly). Storing full snapshots means future solver fixes or new aggregates can be retroactively computed from the raw data.
- A "Recompute Stats" button in History → Previous Sessions detail re-runs `EdgeStats.Aggregate` and rewrites the session file. Useful after solver fixes or new rule axes.
- `RoundSummary` (the old degraded per-round type) was removed; winner/loser/push classification is now computed on demand by `HistoryWindow.ClassifyRound` from each `RoundHistoryEntry.Snapshot`.

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
- `docs/troubleshooting/` - operational runbooks for plugin failures in the
  field (symptom -> root cause -> recovery -> prevention). Start at
  `docs/troubleshooting/README.md`. Notably
  `docs/troubleshooting/config-file-bloat.md` covers the config-doubling bug
  that ballooned `TwentyOne.json` to ~1 GB and the schema-v3 cleanup migration.

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

The two live views (Session Ledger, History > Rounds This Session) cache their `EdgeStats.Aggregate` result via `EdgeStatsCache` (in `TwentyOne/Windows/EdgeStatsCache.cs`). The cache is keyed on `(EdgeRules?, RoundHistory.Count, last RoundNumber)` so it invalidates on rules edit, new round, history clear, or venue switch. Without it, the solver re-runs every frame the window is open. Any new per-frame edge display should reuse this helper.

### Sessions (explicit open/close lifecycle)

Two states, and "no session yet" is just Closed with no data:

- **Open** (`ActiveSessionStartedAt != null && SessionClosedAt == null`) - rounds
  allowed, reconciler fed, books track the live wallet.
- **Closed** (anything else, including a fresh install) - **no rounds**, books
  frozen at `SessionClosedGil` so end-of-night trading, vendoring, and cash-outs
  cannot move the numbers. `ActiveSessionStartedAt` survives Close so the archive
  written at the next Start Session knows when the night began.

`Configuration.SessionOpen` is the predicate; `Configuration.ReconciliationGil` is
the on-hand figure the books reconcile against (live `GilEnd` while open, frozen
`SessionClosedGil` while closed). `SessionLedgerWindow.Compute` reads the latter,
so the ledger and the drift chip freeze together.

- `SessionLedgerWindow.CloseSession()` - stamps `SessionClosedAt` /
  `SessionClosedGil`, writes an `[Audit]` narration line.
- `SessionLedgerWindow.StartSession()` - archives the closed session if there is
  one (`ArchiveSession`, the old `NewSession` body), then `OpenSession` clears the
  narration log, re-baselines `GilStart`/`GilEnd` to the live wallet, and stamps
  `ActiveSessionStartedAt` / location.
- `SessionManager.CheckClose(sessionOpen, phase, banks) -> CloseCheck` is the pure,
  unit-tested guard: closing requires `GamePhase.Betting` **and** every player bank
  settled to zero (`BankHolders` names who still holds gil so the UI can say so).
  Freezing the books mid-round or with gil still owed out would freeze numbers that
  are missing gil still in play.

There is **no lazy session start** any more - `UpdatePlayerStats` used to call
`SessionManager.TryStartSession` on first payout; sessions are opened explicitly.

UI: Start Deal is disabled while closed (tooltip points at the ledger), a
non-dismissible `DrawNoSessionBanner` explains why, and the drift chip shows a
gray `Session closed`. The older stale/moved nag (`ShouldShowSessionBanner`) now
only fires for an *open* session and just opens the ledger.

The new fields are additive with sensible defaults, so **no schema migration**.
An existing config mid-night upgrades to Open, which is correct.

`SessionManager` (in `TwentyOne.Game/SessionManager.cs`) - pure static class:
`CheckClose`, `ShouldShowSessionBanner`, `ResetGameStats`.

### Venue memory

`Configuration.VenueMemory` maps housing address key (`"{territory}:{ward}:{plot}"`) to venue GUID. `Plugin.GetCurrentHousingAddressKey()` handles outdoor housing districts and indoor interiors. When deleting a venue, all `VenueMemory` entries referencing its GUID must be removed.

`MainWindow` shows a dismissible suggestion banner when the current location has a remembered venue that differs from the active one.

### History viewer mode

`MainWindow.isHistoryView` - when viewing a historical round via `HistoryWindow`:
- `UpdatePlayerStats` is a no-op.
- Current `GameState`, `UndoStack`, `RedoStack` saved in-memory, restored on `ExitHistoryView`.
- A banner shown; all other UI renders normally against the historical snapshot.

### Card input

All cards from FFXIV chat rolls (`/random 13` or `/dice 13`). `OnChatMessage` parses the roll result and sets `deferredRoll`; `MainWindow.Pump()` applies it on the next framework tick (deferred to avoid re-entrancy).

### MainWindow.Pump (framework tick, not Draw)

`Pump()` drains the outgoing `chatQueue` and applies `deferredRoll` (advancing
`autoDealQueue` as cards resolve). It is driven by `Plugin.OnFrameworkUpdate`,
**before** the gil poll's login/inventory gates.

It must **never** move back into `Draw()`: ImGui does not call `Draw` on a
collapsed window, so running the pump there stalled every outgoing narration line
and left an arriving `/random` result stuck in `deferredRoll` until the dealer
expanded the window. (This was a real, long-lived bug - collapsing the window mid-round
silently froze the table.)

`MainWindow.paused` is the dealer-facing hold that replaces collapse-as-pause:
`Pump()` returns early while set, so queued narration stays queued and the deal
queue stops advancing, while table buttons stay clickable and their narration
flushes on resume. Transient by design - a reload must never leave the table
stuck paused. Top-bar toggle + `DrawPauseBanner`.

### Bank-only mode (every tracked player banks)

There is no non-banking player path. A player having a `PlayerStat` row **is** the
banking record - the old `IsBanking()` predicate and `TryGetBankingStat` are gone.
This removes the silent-absorb footgun where a trade equal to a bet amount could
vanish from the ledger.

- `PlayerStatExtensions`: `TryGetStat` (row lookup, no banking gate) and
  `GetOrCreateStat` (the canonical accessor for any path that funds/settles a bet).
- Incoming trade -> bank deposit; outgoing trade -> bank withdrawal (always, even
  on an empty bank, so nothing is absorbed). Bets are typed by the dealer and
  funded from the bank via `BankBet` at `StartDeal`; `Player.Bet` is just the
  per-round wager amount. There is no "trade equals the bet" shortcut.
- Start-Deal shortfall guard treats a missing/short bank as a shortfall that
  blocks dealing (`x.p.BankBalance(config) < bet`).
- The `AutoBetFromTrades` config was removed (dead under bank-only). Removal needs
  no migration - the version-gated `ExtensionDataCleaner` drops the orphan key on
  load. `AutoDepositFromTrades` alone gates trade detection.

### TradeRouting (pure trade -> ledger decision)

`TwentyOne.Game/TradeRouting.cs` - `Resolve(long gaveGil, long receivedGil) ->
TradeDirection { None, Deposit, Withdraw, TwoSided }`. Pure and unit-tested
(`TradeRoutingTests`). `TradeMonitor.OnChat` (plugin, not test-reachable because it
needs Dalamud's `PlayerPayload`) delegates the decision here. A **bidirectional
trade** (both sides put gil in the window) routes to `TwoSided` - this was the
session-ledger drift bug: the old code returned a withdrawal-only outcome and
silently dropped the incoming leg, so drift always equaled some player's bet.

### Trade auto-commit (no confirm prompt)

Detected trades **auto-commit** to the player's bank - there is no per-trade
confirm modal. `OnChatMessage`'s switch over `TradeMonitor.Outcome` calls the
relevant `CommitTrade*` helper: `CommitTradeDeposit` (incoming -> `BankDeposit`),
`CommitTradeWithdraw` (outgoing -> `BankWithdrawal`), `CommitTwoSided` (both legs).
The special auto-helpers (`TryAutoDoubleOrSplitDeposit`, `TryAutoMaintainBet*`,
`TryAutoCashOutWithdraw`) still get first refusal; the commit is the fall-through.

Removing the prompt killed the prompt-overwrite money-loss bug (a second trade
clobbering an unconfirmed first prompt) by construction. It is **safe only
because of the `GilReconciler` backstop** (below): a *mis-detected* trade (wrong
/ no partner match -> `OnChat` returns `None`) records nothing and would silently
vanish under auto-commit, but the wallet poll observes the on-hand change with no
recorded trade and surfaces it as an unexplained-gil prompt. The two features are
a pair.

### PendingPrompt (unexplained-gil modal)

After auto-commit, the only modal left is the reconciler's unexplained-gil prompt:
```csharp
private abstract record PendingPrompt
{
    public sealed record Unexplained(long Delta) : PendingPrompt;            // gil moved, no trade
    public sealed record PhantomCredit(long Delta, string StatsKey) : PendingPrompt; // trade, no gil
}
```
`GilReconciler.Finding`s from the framework-tick reconciler are queued
(`findingQueue`) and surfaced one at a time by `DrawFindingModals`, which pops the
next finding into the matching prompt:

- **Unexplained** (`DrawUnexplainedPromptModal`): dealer **assigns** the delta to
  a player's bank (deposit if +, withdrawal if -; candidate list = current
  players, then any other `PlayerStatsStore` row for someone who already left the
  table) - recovering it into the ledger - **records it as a tip** (positive
  deltas only; appends to `config.Tips`, which `AdjustedDiff` subtracts, so the
  books re-zero with no `GilStart` nudge) - or **dismisses** it as non-game gil,
  which nudges `config.GilStart` by the delta so the reconciliation re-zeros.
- **PhantomCredit** (`DrawPhantomCreditModal`): dealer **reverses** the
  over-credit (opposite-sign `ApplyBank` on the tagged player) or **keeps** it
  (gil may still be in flight).

All paths write an `[Audit]` narration line and an `AuditLog.Prompt` record.

### GilReconciler (continuous on-hand-gil reconciliation)

`TwentyOne.Game/GilReconciler.cs` - pure, Dalamud-free, unit-tested. Matches
*expected* on-hand changes (trades the plugin detected) against *observed* ones
(the framework-tick wallet poll). Only trades move on-hand gil (bets / wins /
double / split / credit are internal bank relabeling), so every observed delta
should pair with a recorded trade.

- `RecordExpected(signedDelta, now)` - called in `OnChatMessage` beside each
  `AuditTrade`, for every detected trade (auto-committed or not). +deposit /
  -withdrawal / net two-sided.
- `Observe(actualDelta, now)` - called by the gil poll on each real wallet change.
- `Tick(now)` - returns `Finding(Delta, Phantom, Tag)` for any entry unmatched
  past the **grace window** (default 3s; trade chat lines land ~10-40ms after the
  gil moves). Two FIFO queues; each new event greedily cancels an opposite-side
  entry of equal signed amount (whole-entry, trades are discrete).
  - **Observed unmatched** -> `Phantom = false`: gil moved, no trade -> the
    `Unexplained` prompt (assign / dismiss).
  - **Expected unmatched** -> `Phantom = true`: a trade was recorded but no gil
    arrived (bank over-credited) -> the `PhantomCredit` prompt (reverse / keep);
    `Tag` is the credited player's stats-key so the modal can name / reverse it.
- The shared instance lives on `Plugin` (`Reconciler`), passed to `MainWindow`.
  `Plugin.OnFrameworkUpdate` feeds `Observe`/`Tick` and calls
  `MainWindow.RaiseFinding` per finding; `MainWindow.OnChatMessage` feeds
  `RecordExpected`. This is the runtime catcher for the dropped-chat-line trade
  miss that caused the 2026-06-26 +1M silent drift (diagnosed offline from the
  audit log: one `wallet` delta with no `trade` line).

### Drift chip (main-window top bar)

`MainWindow.DrawDriftChip()` renders an always-visible books-balance signal:
gray `Session closed` while no session is open (books frozen - see Sessions),
green `Books OK` when reconciled, red `Drift: +X` otherwise, clickable to open the
Session Ledger, suppressed in history view. It and the Session Ledger share one
source of truth: `SessionLedgerWindow.Compute(Configuration) -> Reconciliation`
(a readonly record struct with `AdjustedDiff` / `Drift` / `Reconciled`), so the two
displays can never diverge. Computed per-frame (cheap arithmetic, no solver).

The chip's input `config.GilEnd` is kept live by the framework-tick gil poll
(below) - not by the Session Ledger window. Previously the only poll lived in
`SessionLedgerWindow.Draw`, so the chip reconciled against a frozen `GilEnd`
whenever that window was closed.

### Gil poll (framework tick)

`Plugin.OnFrameworkUpdate` polls the dealer's on-hand gil into `config.GilEnd`
every frame (when logged in), independent of which windows are open (subscribed
via `Framework.Update`, unsubscribed in `Dispose`). The wallet is **always
tracked** - there is no on/off toggle (the old `AutoUpdateGilEnd` venue setting
was removed; it is an orphan key cleaned on load, no migration). This is the
single live wallet-tracking site; `SessionLedgerWindow` shows `config.GilEnd`
read-only ("Ending Gil (live)"). First poll after (re)login adopts the live value
as a baseline (no delta against a stale `GilEnd`); each subsequent real change
writes an `AuditLog.Wallet` checkpoint.

- **Login gate:** the poll is skipped (and re-baselined) when
  `!ClientState.IsLoggedIn` - the wallet reads 0 while not logged in, which
  previously logged spurious ~32M log-out/log-in `wallet` deltas.
- **Reconciler feed (gated on `Configuration.SessionOpen`):** while a session is
  open, each real change calls `Reconciler.Observe(delta)` and every frame calls
  `Reconciler.Tick`, raising `MainWindow.RaiseFinding` for any unmatched delta
  (see GilReconciler above). While **closed** the reconciler is not fed - between
  nights the dealer trades, vendors, repairs and cashes out freely, and feeding
  those deltas in would queue unexplained-gil findings for gil that was never part
  of a game. `GilEnd` keeps polling regardless, so the accumulated non-game
  movement folds into the next session's baseline instead of surfacing.
  This gate used to be `MainWindow.IsOpen`; the explicit session lifecycle is the
  honest signal for "is the dealer running the table" (it is the "night finished"
  concept this note used to anticipate). Window state is now irrelevant to the
  reconciler - findings raised while the window is closed or collapsed simply queue
  and surface on the next draw.

### Audit log (disk-only forensic trail)

`TwentyOne.Game/AuditLog.cs` - append-only JSONL log of every gil-affecting
event, written to `{ConfigDirectory}/audit/{venueId}-{yyyy-MM-dd}.jsonl`. One
JSON object per line, never edited. **No UI by design** - it exists so that
when the dealer notices ledger drift, the file is read offline to localize
when/why it entered (the original +250K silent-drift bug had no trail). Ships
in Release (the drift only happens live); capture is cheap.

- Four event kinds: `bank` (every `MainWindow.ApplyBank` - the single choke
  point, so all `BankLedger` tx types are covered), `trade` (each completed
  trade, logged in `OnChatMessage` via `AuditTrade` with gave/received +
  outcome), `prompt` (trade-modal Confirm/Cancel resolutions), `wallet`
  (unexplained on-hand-gil delta from the framework-tick poll - the
  silent-drift catcher). `dealerGil` on bank/trade events is the polled
  `config.GilEnd` for offline correlation.
- The plugin extracts the primitives and passes them in, so `AuditLog` has no
  Dalamud dependency and is unit-tested (`AuditLogTests`). It uses Newtonsoft
  (compile-only in `TwentyOne.Game`, provided by Dalamud at runtime).
- `AuditLog.Root` is the enable gate: the plugin sets it to the audit dir at
  startup; null/empty no-ops every write. Writes are best-effort (swallow all
  exceptions) so a logging failure never disrupts a trade, bank op, or load.
- No `VenueSettings`/`Configuration` field, **no schema migration** - the log
  lives entirely on disk, outside the config graph, so it cannot reintroduce
  the config-bloat risk class. Classification of explained vs unexplained
  deltas is done offline when reading the file, not live in the plugin.

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

`NarrationTemplates` properties are `List<List<string>>` - the outer list is random variants (one picked per use via the variant selector, `Random.Shared` in game), the inner list is the sequence of chat lines sent for that variant. Some defaults ship multiple variants (e.g. `PlayerBust`). The three `DealSummary*` properties remain plain `string` (they are concatenated components, not narrated independently).

**Tests must never assert on randomly-selected narration.** `GameEngine.Apply` takes an optional `pickVariant` selector (defaults to `Random.Shared.Next`); any test that asserts on `SendChat` text must pass `pickVariant: TestNarration.First` (always variant 0) so the assertion is deterministic. Asserting content without it is the classic flaky test - a multi-variant default will fail whenever a variant that lacks the asserted phrase is picked.

Every narration string emitted via `SendChat` must have a corresponding property in `NarrationTemplates` and a row in `NarrationEditorWindow`. When adding a new `Narrate(...)` call in `GameEngine`, always:
1. Add a `List<List<string>>` property to `NarrationTemplates` with a sensible default.
2. Add an `NtListRow(...)` entry in the appropriate `NarrationEditorWindow` section (or a new section if needed).
3. Add a test in `NarrationTemplateTests` verifying the new template variable(s) are substituted (initialise the property as `[["template string"]]`, or pass `pickVariant: TestNarration.First` when using multi-variant defaults).

## Commits

Commit messages follow `type(scope): message` style. Every commit builds (Debug + Release) and passes tests.

## Versioning

- **Scheme:** `0.MINOR.PATCH` while pre-release. The csproj `<Version>` is the
  4-part `0.MINOR.PATCH.0` (Dalamud needs a `System.Version`); the 4th component
  is always `0` and ignored. No `-alpha`/`-beta` suffixes - the leading `0`
  already signals "unstable."
- **Pre-1.0 bumps:** `MINOR` = new feature or behavior change; `PATCH` = bug fix /
  polish only. The first build handed to friends becomes `1.0.0`; standard SemVer
  applies after that (`MAJOR` = breaking).
- **Bump discipline:** bump `<Version>` in the same commit that earns it. The
  version stamps into config (`PluginVersion`) so the loaded build is identifiable
  in-game.
- **Git tags:** annotated `vMAJOR.MINOR.PATCH`, one per build actually run live or
  shared. No ad-hoc tag names. Stale pre-consolidation tags (`v0.1-alpha`,
  `v0.2-beta`, `banking-beta`, `wip/bet-payout-tracking`) are kept until the
  public `1.0.0` cutover, then pruned (leaving `vX.Y.Z` + `archive/*`).
- **`Configuration.SchemaVersion` is independent** - an integer that bumps only on
  persisted-JSON shape changes, unrelated to the plugin version.

## UI Rules

- Never use non-ASCII characters (icons, arrows, symbols) on buttons unless explicitly requested. Use plain text labels only.
- To right-align a button within a cell/region: use `SameLine()` followed by `if (GetCursorPosX() < targetX) SetCursorPosX(targetX)`, where `targetX = cellRight - buttonWidth` (button width = `CalcTextSize(...).X + FramePadding.X * 2`). Do **not** pass a position directly to `SameLine(pos)` - if `pos` is behind the current cursor, ImGui will clip or hide the widget.
- For text inputs that need an inline label-when-empty (e.g. an unlabelled "amount" or "description" field), use `ImGui.InputTextWithHint(label, hint, ref buf, maxLen, flags)` instead of `InputText`. The hint string is shown in disabled-text colour while the buffer is empty and vanishes as soon as the user types. Same return value and flags as `InputText` (Dalamud's binding exposes the same overloads). Prefer this over a separate leading `Text("Amount")` whenever the field already lives in a row with other widgets - it keeps the row tight and self-describing.

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
- Double-down restriction (`DoubleRestriction`, default `Any`): `Any` allows doubling on every 2-card hand. `Hard9To11` and `Hard10To11` restrict to those hard totals (soft 19 / soft 20 do not qualify, matching standard casino convention). `HardOnly` permits any hard 2-card total but disallows soft doubling. DAS and the restriction stack independently - both must allow doubling on a post-split hand.
- Split rules: re-splits are bounded by `ResplitCap` (default Max4 - i.e., up to 4 hands total). Aces are exempt from the numeric cap and gated solely by `ResplitAces`. 21 on a split hand (`IsFromSplit=true`) is Playing/Stand, never Blackjack; split aces receive exactly one card then auto-stand (standard casino rule, see ToDo.txt for variant note).
- Payout is calculated per-hand. `Hand.Bet` holds the effective bet when a hand has been doubled (empty = inherit `Player.Bet`).
- `WaitingForDealer` must never be set unconditionally when transitioning to `DealerTurn`. Always derive it as `!CanGoToPayout(provisionalDealerState)`. This ensures special cases (all-bust, all-BJ, all-Charlie, or mixed terminal winning hands with safe upcard) skip the "Begin Dealer Turn" prompt and show "Go to Payout" directly. `CanGoToPayout` is the single source of truth for whether the dealer still needs to act.
- All-bust: `AdvanceFrom` returns `GamePhase.Payout`; the engine maps this to `DealerTurn` with `WaitingForDealer=false`. The dealer Hit button and recommendation label are suppressed when all hands are bust. `CanGoToPayout` returns `true` immediately for all-bust.
- All-BJ, all-Charlie (LosesToDealerBJ), or mixed BJ+Charlie: dealer must reveal their hole card only if their upcard is an ace or 10-value (could be BJ or Charlie-losing scenario). `CanGoToPayout` checks `allTerminalWin` for mixed hands, applying the same upcard rule. When the hole card is not needed, the game goes straight to "Go to Payout". `AnnounceDealerHit` emits `DealerBJCheck` instead of `DealerHitAnnounce` when all players have Blackjack.
- `BeginPlayerTurns` auto-skips consecutive BJ hands when all remaining active hands are BJ. Scans ahead through BJ hands; if all are BJ, transitions directly to `DealerTurn` (or `Payout`) with no narration. If a non-BJ hand follows, narrates "moving along" for the first BJ and waits (old behavior). `AdvanceToNextPlayer` does NOT auto-skip - narrates and waits for each BJ hand encountered mid-round. The `allTerminalWin` check in `CanGoToPayout` ensures mixed BJ+Charlie hands skip the dealer turn when appropriate.
