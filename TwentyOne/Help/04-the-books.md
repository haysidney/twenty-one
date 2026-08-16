# Reading the books

## The drift chip

The top bar of the table window always shows one of three chips. Clicking any of
them opens the Session Ledger.

| Chip | Meaning |
|---|---|
| Session closed | No session running. The books are frozen. |
| Books OK | Every gil is accounted for. |
| Drift: +X | The plugin cannot explain X gil. |

Green is the normal state, and it should stay green all night. If it goes red,
something happened that the ledger does not have an entry for. Deal with it
while you still remember what you were doing.

## Why the books can balance at all

Only one thing moves gil in and out of your pocket: **trades**. Bets, wins,
doubles, splits and credit are all internal relabelling, moving gil between the
house and a player's bank while it sits in the same pile.

So the plugin can check itself continuously. It watches your actual wallet, it
knows which trades it recorded, and any wallet movement without a matching trade
is a real discrepancy rather than a rounding artifact.

## The reconciliation block

In the Session Ledger, above the settlement section:

| Line | What it is |
|---|---|
| House Difference | Your gil now, minus your gil at Start Session. |
| Bets held | Gil currently staked on the table. |
| Player banks | Gil you are holding for players. |
| Tips held | Tips recorded this session. |
| Service revenue | Service charges recorded this session. |
| Credits issued | Free play issued. Reference only. |
| Table net | What the table actually won or lost. |
| Player Net | The players' combined net, from the other direction. |

**Table net** is House Difference with everything that is not table winnings
taken back out of it: bets still in play, gil belonging to players, tips and
service charges. It is the number the whole settlement is built on.

**Player Net** is the same night measured from the players' side. Table net and
Player Net have to cancel out, and the **OK** / **MISMATCH** marker beside them
says whether they do. OK there and a green chip in the table window are the same
statement.

## The other fields

- **Starting Gil** is set for you when you start a session. The **Current**
  button overwrites it with your wallet right now, and needs Ctrl held, because
  changing it silently rewrites the night's arithmetic.
- **Ending Gil** tracks your wallet automatically while a session is open, and
  is marked `(frozen)` once you close it.
- **Tips** are gil you keep. They pass straight through to you and are never
  split with the venue.
- **Service Charges** are fees you charged separately from the game. Each one
  toggles between **To Dealer** and **To Venue** depending on who ends up with
  it. Double-click a charge's amount or note to edit it.
- **Venue Cut %** and **On a losing night** belong to settlement, and are
  covered on the "Settling up" page.

## When the plugin asks you about gil

Two prompts can appear. Both write a line to the narration log so there is a
record of what you chose.

### Unexplained gil

Your wallet moved and no trade explains it. Three answers:

- **Assign to bank** - it was a trade the plugin missed. Pick the player and
  their bank is corrected. This is the right answer most of the time.
- **Add as tip** - someone tipped you. Only offered for gil coming in.
- **Not game-related (dismiss)** - you bought something, repaired, or sold on
  the marketboard. The starting figure is nudged so the books re-zero.

Answer honestly rather than dismissing everything; dismissing gil that really
was a player's leaves their bank wrong.

### Phantom credit

The opposite case: a trade was recorded but your wallet never changed. The named
player's bank may have been credited for gil that never arrived.

- **Reverse bank** undoes the credit.
- **Keep** leaves it, for when the gil is simply still in flight.

## The audit log

Every gil-affecting event is also written to a file, one line per event, never
edited:

```
<Dalamud config folder>/TwentyOne/audit/{venue}-{date}.jsonl
```

It covers bank operations, trades, prompt answers and raw wallet changes. There
is no interface for it on purpose. It exists so that if you find drift the next
morning, the file can tell you exactly when it entered and what was happening at
the time.
