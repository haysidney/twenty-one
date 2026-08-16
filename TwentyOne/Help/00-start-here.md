# Start here

This plugin runs a blackjack table for you. You deal, it narrates every beat to
chat, draws cards from `/random` rolls, tracks what every player has put in, and
keeps the night's gil straight so you know at any moment whether the books
balance.

It assumes you know how blackjack is played. It does not assume you know
anything about the plugin.

## Sessions

**A session must be open before you can deal.** Start Deal stays greyed out
until you open one. Starting a session records your current gil as the night's
opening figure, which is what lets the plugin tell you later whether the money
adds up. Closing it at the end freezes those figures so you can settle from
them.

[[open:night|Read: Running a night]]

## The shape of a round

Every round moves through five phases, shown just above the buttons at the
bottom of the table window:

1. **Betting** - add players, take their gil, type their bets.
2. **Deal** - one card to you, two to each player, drawn from rolls.
3. **Player Actions** - each player hits, stands, doubles or splits in turn.
4. **Dealer Turn** - you draw your own hand out.
5. **Payout** - results and amounts owed, then New Round.

The button at the bottom always shows the next thing to do, and the phase label
above it tells you whose turn it is and what they are allowed to do.

## The windows

| Window | What it is for |
|---|---|
| Table | The round in progress. Opened with `/twentyone`. |
| Session Ledger | Opening and closing the night, tips, and the money. |
| History | Past rounds and past sessions. |
| Settings | Venue, dealer name, chat channel, automation. |
| Blackjack Rules | House rules, with the resulting house edge. |

[[open:win:ledger|Open the Session Ledger]]

## Your first night

- Set your dealer name and chat channel in Settings.
- Open the Session Ledger and click **Start Session**.
- Target a player in the world and click **Add Selected Player**.
- Trade to take their gil. The plugin banks it automatically.
- Type their bet, then click **Start Deal**.
- At the end of the night, click **Close Session** and settle up.

Each of those has a page of its own in the list on the left.

## No hole card

You take **one** card at the deal and do not draw again until every player has
finished acting. There is no face-down card and no peek for blackjack. This is a
real casino variant, and the house edge the plugin reports already accounts for
it.
