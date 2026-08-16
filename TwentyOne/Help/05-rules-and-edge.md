# Rules and house edge

House rules belong to the venue, so two venues can run completely different
games. Open them from Settings, or here:

[[open:win:rules|Open the Blackjack Rules editor]]

## When a rule change takes effect

Rules are locked in at the moment you deal. An edit made during the **Betting**
phase applies to the round about to start. An edit made once cards are out does
**not** affect the round in progress, and takes effect on the next deal.

Each round is also archived with the rules it was played under, so looking at an
old round in History shows you the game as it was actually played.

## No hole card

You take **one** card at the deal and do not draw again until every player has
acted. There is no face-down card and no peek for blackjack.

The consequence worth knowing: when you do end up with blackjack, players who
have already doubled or split lose everything they put in, not just their
original bet. This is a real casino variant, and the house edge figure the
plugin shows already accounts for it.

## The rules

- **Blackjack Payout** - the multiplier on a natural. Buttons for 3:2, 6:5 and
  1:1, or type any multiplier. 3:2 is the standard and the default.
- **Five Card Charlie** - whether five cards without busting is an automatic
  win. Off by default. **Beats all** wins outright; **Loses to dealer BJ** loses
  to a dealer blackjack.
- **Charlie Payout** - what a charlie pays, when it is enabled.
- **Dealer stands on** - the total at which you stop drawing, from 15 to 18. 17
  is standard, though venues do run 16.
- **Hit soft N** - whether you draw again on a soft total at the threshold, that
  is, one counting an ace as 11. Standing on 17 with this on is the usual H17;
  with it off it is S17.
- **Allow double after split** - whether a hand created by splitting can be
  doubled. On by default.
- **Double on** - which two-card totals may be doubled. Any total, hard 9 to 11,
  hard 10 to 11, or hard totals only. This stacks with the setting above: both
  have to allow it.
- **Allow hitting split aces** - off by default, meaning a split ace takes
  exactly one card and stands. Note that 21 on a split hand is a plain 21, never
  a blackjack.
- **Allow resplitting aces** - whether a pair of aces from an earlier split can
  be split again.
- **Resplit cap** - how many hands a non-ace pair can become. Aces ignore this
  and follow the setting above.
- **Allow surrender** - lets a player give up an untouched two-card hand for
  half their bet. Off by default. Because there is no hole card, surrender here
  is early-style: half the bet is forfeited even when you turn out to have
  blackjack.

**Reset Rules** at the bottom restores every default. It needs Ctrl held.

## The house edge figure

At the bottom of the rules editor is the expected house edge under the rules you
have set, assuming a player who plays perfectly. Positive means the house is
favoured.

Beside each rule is a smaller figure in brackets: what that one setting is
costing or earning you compared to its default. Green means it moves the edge
your way, red means it moves it the players' way.

Two of these are worth staring at:

- Dropping blackjack from 3:2 to 6:5 is the single biggest swing available, and
  players notice.
- Every dealer stand threshold other than 17 is player-favoured.

## Realized against theoretical

The Session Ledger and History both show how the night actually went against
what the edge says it should have. Realized is simply what the bank made per
gil wagered.

They will not match, and that is expected. A night is a handful of hands, and
the theoretical figure is a long-run average. Treat a gap as interesting only
when it persists across many sessions, and the display warns you when the sample
is too small to read anything into.

The three views differ deliberately:

- **Session Ledger** uses your current rules: what should tonight look like
  under the rules I run now?
- **History, Rounds This Session** uses each round's own rules: what should have
  happened given what was in effect at the time?
- **History, Previous Sessions** shows the figure locked in when that session was
  archived.
