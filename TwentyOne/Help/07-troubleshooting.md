# When something goes wrong

Most disabled buttons in this plugin explain themselves. Hover the greyed-out
button and read the tooltip before hunting further.

## Start Deal is greyed out

In order of how often it is the answer:

- **No session is running.** Open the Session Ledger and click Start Session.
- **A player's bank cannot cover their bet.** The tooltip names them, and their
  Bank figure is amber. Take a trade, or lower the bet.
- **Someone has no bet typed in.** Every player who is not sitting out needs
  one.
- **No players at the table**, or you are still in Reorder mode.

## The table has gone silent

- Check the **Pause** button in the top bar. Paused holds all narration and
  dealing; the banner says so and the queued line count is shown.
- Check that chat is enabled in Settings and pointed at the channel you expect.
- If narration lines are being dropped in-game rather than by the plugin, raise
  the message delay in Settings. The game rate-limits public channels harder.
- If a line that should have gone to `/yell` came out somewhere else, that is
  the cross-channel setting doing its job.

Collapsing the table window does **not** stop the game. Narration and dealing
carry on regardless.

## A card never arrived

The plugin waits for the roll result before continuing. If one is lost, a
**Draw** button appears on the row that is short a card. Click it to roll again.

If rolls are consistently going missing, your message delay is probably too low
for the channel you are using.

## Someone left in the middle of a round

Hold Ctrl and click **Out** on their row. Their bet is refunded and their hand
discarded; everyone else's round is untouched.

## The deal went wrong

- During the **Deal** phase, **Abort Deal** scraps it, refunds every bet, and
  keeps the amounts typed in so you can deal again immediately. No Ctrl needed,
  because nothing has been played yet.
- Later, **Abort Round** does the same but needs Ctrl, because it destroys real
  play.

## I clicked the wrong button

**Undo** is at the top right. If the action you are undoing charged a player's
bank, a confirmation appears listing exactly which refunds it will post.

Undo is unavailable once a payout is complete. Settlement also moved gil into
banks and updated statistics, which undo cannot cleanly unwind. Use **New
Round** and correct the balance by hand in the player's bank if needed.

## The drift chip went red

Click it to open the ledger and read the reconciliation block. Usually the
plugin will already have asked you about the gil in question, and drift is what
remains after you dismissed something you should have assigned.

Working backwards:

1. Compare **Player banks** against what people actually handed you.
2. Open a suspect player's bank and read their transaction history.
3. If you still cannot place it, the audit log file holds every event with a
   timestamp. Its location is on the "Reading the books" page.

To square the books once you know the cause, either correct the player's bank in
their Bank window, or adjust **Starting Gil** if the movement was never
game-related. Hold Ctrl to use the **Current** button.

## The plugin says a trade never arrived

That is the phantom credit prompt: a trade was recorded but your wallet did not
move. **Reverse bank** if the gil genuinely never came, **Keep** if it is simply
still in flight.

## Where my data lives

In your Dalamud plugin configuration folder:

```
TwentyOne.json                      settings, venues, banks, round history
TwentyOne/sessions/{venue}/         one file per archived session
TwentyOne/audit/                    the audit log
```

Uninstalling the plugin leaves the sessions and audit folders behind. Delete
them by hand if you want them gone.

The plugin also writes a dated backup of the configuration beside it whenever an
update changes the file's format, so an upgrade always has a way back.
