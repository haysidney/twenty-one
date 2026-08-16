# Settling up

## Close the session first

Click **Close Session** in the Session Ledger. The numbers stay on screen, they
simply stop following your wallet, so you can trade and cash out while settling
without the figures shifting under you.

Two things have to be true first, and the tooltip on the disabled button tells
you which one is missing:

- You are between rounds, not mid-hand.
- Every player's bank is back to zero, meaning you have paid everyone out. If
  someone still holds gil, the tooltip names them.

[[open:win:ledger|Open the Session Ledger]]

## The idea behind the settlement block

You are already physically holding every gil on the screen: the night's
winnings, the tips, the service charges. Nobody hands you anything at the end.

So the settlement block does not tell you who receives what. It tells you the
one thing you actually have to do, which is a single trade with the venue, and
then what you are left with afterwards.

## The lines

**Table net** is what the table won or lost against the players. Bets still in
play, player banks, tips and service charges are already out of it. Green is a
winning night, red is a losing one.

**Pay venue** or **Collect from venue** is the call to action: one direction,
one amount. Click the number to copy it, then trade it. Expanding the row shows
what it is made of:

- The venue's cut of the table.
- Any service charges you collected but routed to the venue.

**Your take** is what you walk away with. Expanding it shows:

- Your share of the table.
- Tips, in full. **Tips are never split.**
- Service charges routed to you.

Every figure on this block can be clicked to copy, and they print in full rather
than abbreviated, because they get typed into a trade window.

## The two settings that drive it

**Venue Cut %** is the venue's share of the table's winnings.

**On a losing night** is a separate question, because who covers a loss is an
arrangement rather than arithmetic:

| Setting | What it means |
|---|---|
| Venue covers the loss | You walk away whole. This is the default. |
| Venue covers its cut % | The loss splits the way a win would. |
| You absorb the loss | The venue pays nothing back. |

Agree this with the venue before you deal, not after a bad night.

Rounding always goes your way, by at most a gil.

## Credits

If you issued any free play, a **Credits issued** line appears for reference.
It is not settled separately, and adding it to what the venue owes you would pay
you twice. The reasoning is on the "Banks and trades" page.

## Archiving the night

The next time you click **Start Session**, the closed night is archived into
History first: per-player statistics, every round, and the edge figures locked
in as they stood.

You can look at it later under History, Previous Sessions, and it survives
independently of your live configuration.

[[open:win:history|Open History]]
