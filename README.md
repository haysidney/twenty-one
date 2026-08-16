# Twenty One

A Dalamud plugin for running a blackjack table in an FFXIV venue. The dealer
drives the game from a window; the plugin narrates each beat to chat, draws
cards from `/random 13` rolls, and keeps the night's money straight.

Type `/twentyone` to open the table.

## What it does

- **Runs the table.** Deal, hit, stand, double, split, surrender, five-card
  charlie - with the dealer's next action suggested at each step.
- **Narrates to chat.** Every line is a template you can rewrite, with random
  variants so the table does not read like a robot. Rolls, emotes and `<wait.N>`
  pacing are handled for you.
- **Tracks the money.** Each player has a bank funded by trades. Bets, wins,
  doubles and splits move gil between that bank and the house, and the plugin
  reconciles the whole thing against your actual on-hand gil - so if the books
  drift, you know immediately rather than at 3am.
- **Per-venue rules and settings.** Blackjack payout, dealer stand rule, DAS,
  surrender, resplit limits and more, with the resulting house edge computed
  live so you can see what a rule change actually costs.

## Install

This is not in the official Dalamud plugin repository. Add the custom repo:

1. `/xlsettings` -> **Experimental** -> **Custom Plugin Repositories**
2. Add the repo URL, hit **Save**
3. Find **Twenty One** in `/xlplugins`

## Running a night

1. Open the **Session Ledger** and click **Start Session**. No rounds can be
   dealt outside an open session - this is what pins the night's starting gil.
2. Add players, take their bets by trade, deal.
3. When you're done, **Close Session**. The books freeze, so you can trade,
   vendor and cash out afterwards without disturbing the numbers. Closing
   requires being between rounds with every player's bank settled to zero.
4. **Start Session** again next time archives the closed night into History.

## When the numbers look wrong

The top bar shows a books-balance chip - green `Books OK`, red `Drift`, or gray
`Session closed`. If it goes red, the plugin has noticed gil that its ledger
cannot explain.

Every gil-affecting event is also written to an append-only log at:

```
<Dalamud config dir>/TwentyOne/audit/{venueId}-{date}.jsonl
```

One JSON object per line, never edited, covering bank operations, trades,
prompts, and raw wallet changes. When something does not add up, that file
tells you when it entered and what was happening at the time.

## Data and uninstalling

The plugin writes to your Dalamud plugin config directory:

- `TwentyOne.json` - settings, venues, player banks, round history
- `TwentyOne/sessions/{venueId}/` - one file per archived session
- `TwentyOne/audit/` - the audit log described above

Uninstalling the plugin does not remove the sessions and audit directories.
Delete them by hand if you want them gone.

## Building

Requires the .NET 10 SDK and a Dalamud dev install. A Nix flake is provided:

```bash
nix develop --command dotnet build TwentyOne/TwentyOne.csproj -c Release
nix develop --command dotnet test TwentyOne.Tests/TwentyOne.Tests.csproj
```

## Status

Pre-1.0 and shared with friends rather than announced. Issues are not enabled -
if you know me, tell me directly.

## License

MIT - see [LICENSE](LICENSE).
