# Banks and trades

## The model

Every player at your table has a **bank**: gil they have handed you, which you
are holding on their behalf. Nothing else is tracked. There is no separate
"took a bet in a trade" path.

That means the flow is always the same:

1. They trade you gil. It goes into their bank.
2. You type a bet. It is funded from their bank.
3. At payout, their bet and any winnings go back into their bank.
4. When they leave, you trade their bank back to them.

The point of doing it this way is that at any moment the plugin knows exactly
how much of the gil in your pocket is yours and how much is theirs. That is what
makes the drift check on the "Reading the books" page possible.

## Trades are detected automatically

When a trade with someone at your table completes, the plugin reads it out of
your chat log and updates their bank immediately. There is no confirmation
prompt.

- Gil in becomes a **deposit**.
- Gil out becomes a **withdrawal**, even if their bank is empty. Nothing is
  silently absorbed.
- A trade where **both** sides put gil in is recorded as both legs.

Skipping the confirmation is only safe because of the backstop described on the
"Reading the books" page: if the plugin ever misses a trade, your wallet moves
with no matching record and it asks you about it a few seconds later.

For a trade to be matched to a player, they have to be at the table. Trading
someone who has already been removed will surface as unexplained gil instead.

## The Bank window

The **Manage** button in a player's Bank column opens their bank.

| Control | What it does |
|---|---|
| Deposit | Adds gil to their bank and narrates it. |
| Withdraw | Takes gil out of their bank and narrates it. |
| Credit | Adds to their bank with no real gil moving. |
| Dealer Tip | Moves gil out of their bank and keeps it as your tip. |
| Transfer To | Moves gil from their bank into another player's bank. |
| Remind | Tells them their bet and bank balance in chat. |
| Maintain Bet | Tracks their bank against their bet. |
| Clear All | Wipes the balance and history. Needs Ctrl. |

Deposit and Withdraw here are for corrections and for gil that moved outside a
normal trade. In ordinary play you should not need them, because real trades are
picked up on their own.

Below the buttons is that player's full transaction history: time, type, amount
and the balance after it. This is the first place to look when someone disputes
a number.

## Dealer Tip and Transfer To

Both of these move gil between books you are already holding, so no trade
happens and the reconciliation stays balanced.

**Dealer Tip** is for the player who says "keep the rest". It takes the amount
out of their bank and adds it to Tips in the Session Ledger. Tips never enter
the venue split - the whole amount is yours.

**Transfer To** moves gil from this player's bank into another seated player's
bank: one person covering another's buy-in, or settling something between
themselves. Pick the amount and the recipient, and a confirmation window spells
out the move before it is posted, because the only way back is a transfer in the
other direction.

Both are narrated in chat, and both show in the transaction history as **Tip**
and **Transfer**.

## Credit

**Credit** is venue-funded free play, for a VIP night or a comp. It adds to the
player's bank without any gil changing hands, on the understanding that the
venue has already fronted that money into your starting pile.

The ledger shows a "Credits issued" line for reference. It is deliberately not
a separate line at settlement, and that is correct rather than an oversight:

- Credit the player loses back never left your pile, so there is nothing to
  settle.
- Credit they cash out did leave your pile, so it is already counted as a loss
  in Table net, and your loss-coverage setting handles it.

## Maintain Bet

Turning **Maintain Bet** on for a player says "this person is playing the same
bet repeatedly, keep their bank level with it". It does two things:

- Deposit and withdrawal narration is suppressed for them, so a regular
  re-buying every round does not spam the channel.
- When their bank rises above their bet, an **Owe** figure appears in their Bank
  cell showing how much to hand back.

Use it for a regular. Leave it off for a casual player.

## Paying out

At payout the plugin has already moved each player's returned bet and winnings
into their bank. So paying someone is just trading them out of their bank, and
the **Copy** button on their payout row gives you the number to type.

Because payouts land in the bank rather than going straight out, a player who is
staying for another round needs no trade at all.

## Withdrawing a player mid-round

Hold Ctrl and click **Out** on their row during the Deal or Player Actions
phase. Their bet, including any double or split top-up, is refunded to their
bank, their cards are discarded, and the round continues normally. If it was
their turn, play advances as though they had stood.
