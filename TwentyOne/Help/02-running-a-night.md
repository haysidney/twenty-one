# Running a night

## 1. Open a session

Open the Session Ledger and click **Start Session**. Until you do, the table
window carries a banner explaining why you cannot deal, and **Start Deal** stays
disabled.

Starting a session records your current on-hand gil as the night's starting
figure. Everything the ledger later tells you about the money is measured
against it.

[[open:win:ledger|Open the Session Ledger]]

If a previous night is still sitting closed on screen, Start Session first
offers to archive it into History. That clears tips, round history, the
narration log and the table, so say yes only when you are genuinely starting a
new night.

## 2. Add players

Two ways, both only available during the Betting phase:

- Target the player in the world, then click **Add Selected Player** under the
  table.
- Right-click their name anywhere it appears in game and choose **Add to
  Blackjack Table**.

Once added, a player's row gives you:

- **@** targets them.
- **R** sets a nickname, **C** clears it. Double-clicking the name does the same
  as **R**.
- **Sit Out** parks them for a round without removing them. **Resume** brings
  them back. Sat-out players drop to a greyed section at the bottom and are not
  counted in that round's statistics.
- **X** removes them from the table entirely.
- **Reorder** (next to the Players heading, when more than one is playing) lets
  you move rows up and down to match seating order.

## 3. Take their gil, then set their bet

This is the step new dealers get backwards. **Bets are funded from a player's
bank, not from the trade window.** So:

1. Trade with the player and take their gil. The **Trade** button on their row
   opens the trade for you. Shift+Click it to announce the bet request in chat
   first.
2. The plugin sees the completed trade and banks the gil automatically. No
   confirmation to click.
3. Type their wager into the **Bet** column and press Enter.

The **Bank** column shows what they are holding. If their bank cannot cover
their bet, the figure turns amber, a **Short** button appears (clicking it
announces the shortfall in chat), and Start Deal is blocked until it is
resolved.

**Announce Betting Open** narrates that the table is taking bets. **Remind**
tells a player what they are currently down for.

The "Banks and trades" page covers the money side properly.

## 4. Deal

Click **Start Deal**. The plugin rolls one card for you and two for each
player, in seat order, pacing the rolls so chat can keep up. The phase label
shows the progress, for example `(dealer: 1/1 players: 0-2/2)`.

If a card never arrives, a **Draw** button appears on the row that is missing
one so you can re-roll for it.

While in the Deal phase you can still:

- **Adjust** a player's bet. The bank is reconciled in the same click, so
  raising a bet beyond what they have is refused rather than silently allowed.
- **Abort Deal**, which scraps the deal, refunds every bet, and keeps the
  amounts typed in so you can simply deal again.

Then click **Begin Player Turns**, or let the plugin do it for you if you
enabled that in Settings.

## 5. Play the hands

The active hand is highlighted, and the phase label names whose turn it is and
what they may legally do.

| Button | What it does |
|---|---|
| Hit | Rolls another card. |
| Stand | Ends the hand. |
| Dbl | Doubles down. Needs confirming. |
| Spl | Splits a pair. Needs confirming. |
| Srn | Surrenders for half the bet. Only if the rule is enabled. |

**Dbl and Spl are two clicks.** The first click announces it in chat and, if the
player's bank cannot cover the extra, optionally opens a trade. Nothing is
charged yet. When their gil is in, click **Confirm Dbl** or **Confirm Spl** and
only then is their bank debited and the card drawn. **Cancel** backs out.

This two-step exists so that a player who says "double" and then cannot pay does
not leave you with a half-applied hand.

After a hand finishes, a **Next Player** (or **Next Hand**, for a split) button
appears. Split hands are played one at a time, and a player with more than one
hand gets a summary row above their hands showing their combined bet and bank.

If someone disappears mid-round, hold Ctrl and click **Out** on their row. Their
bet is refunded, their cards are discarded, and the round carries on without
them.

## 6. Play your own hand

Click **Begin Dealer Turn**. Your hand shows a suggested **HIT** or **STAND**
based on the house rules, and the **Hit** button rolls your next card.

Sometimes the plugin skips straight to **Go to Payout**. That is correct, not a
bug: if every player has busted, or every remaining hand already beats anything
you could make, there is nothing left for you to decide.

## 7. Pay out

Click **Go to Payout**. Every hand's result appears in the Status column, and
the player's bank is settled automatically: their returned bet and winnings are
put back into their bank.

To pay someone, use the **Copy** button on their row and trade them that amount.
Copy gives you the total owed; Ctrl+Click copies the total minus their original
bet instead, which is what you want when the player is leaving the bet on the
table for the next round.

Then click **New Round**.

## 8. Close the session

At the end of the night, click **Close Session** in the Session Ledger. The
books freeze, so you can trade, vendor and cash out afterwards without moving
the numbers you still have to settle from.

Closing requires that you are between rounds and that every player's bank is
back to zero. If someone still holds gil, the tooltip on the disabled button
names them.

See the "Settling up" page for what to do with the numbers once the books are
frozen.

## Things you can do at any time

- **Undo** and **Redo** sit at the top right. If an undo would reverse a bank
  charge, you get a confirmation listing exactly which refunds it will post.
  Undo is unavailable once a payout is complete, because settlement also moved
  gil and bumped statistics that it cannot cleanly unwind. Use New Round.
- **Pause** holds all narration and dealing. Buttons still work; their chat
  lines queue up and go out when you resume. Pausing is deliberately forgotten
  on reload, so a crash can never leave your table stuck.
- **Abort Round** (Ctrl held) scraps a round in progress and refunds every bet,
  double and split.
