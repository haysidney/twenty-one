# Before your first night

Everything on this page lives in **Settings** (the first button in the table
window's top bar). You only have to do it once per venue.

[[open:win:settings|Open Settings]]

## Venues

Every setting on this page belongs to a **venue**, not to the plugin. Your
rules, chat channel, narration, player banks, round history and session ledger
are all stored per venue, so dealing at two different houses never mixes their
books together.

The venue selector sits at the top of Settings.

- **+** adds a new venue.
- **Rename** and **Duplicate** do what they say. Duplicate is the quick way to
  set up a second venue with the same rules.
- **Delete** needs Ctrl held, and is unavailable when you only have one venue.

You cannot switch venues while a round is in progress.

The plugin also remembers which venue you used at a given house. Walk into that
house again with a different venue selected and a banner offers to switch you
back.

## Dealer name

Whatever you type here is substituted for `{dealer}` in the narration
templates. Set it to the name your table knows you by.

## Chat

**Enable FFXIV chat (narration + rolls)** is the master switch, and the
dropdown beside it picks the channel: `/say`, `/yell`, `/shout`, `/p`, `/a`,
`/fc`, a cross-world linkshell, or a linkshell.

Two things to know:

- Cards come from real rolls in that channel. Public channels (`/say`, `/yell`,
  `/shout`) use `/random 13`; everything else uses `/dice 13`.
- **If you turn chat off, the plugin draws cards itself** with its own random
  numbers instead of rolling. Nothing is narrated. That is fine for trying the
  interface out, but it is not how you run a real table, because the players
  cannot see the draws.

### Cross-channel commands

Some narration lines start with their own channel command, for example a `/y`
celebration when someone hits blackjack. This setting decides what happens when
that does not match the channel you picked above:

| Setting | What happens |
|---|---|
| Block | The line is rewritten to `/echo` so only you see it. |
| Redirect | The override is stripped and the line goes to your channel. |
| Allow | The line is sent as written, in the override channel. |

**Redirect** is the default and is the safe choice for a quiet venue.

### Timing

**Time between messages** paces narration so the game does not rate-limit you.
Public channels are limited harder than private ones, so the plugin keeps a
separate number for each and shows whichever one applies to your current
channel. **Slash command delay** paces `/random` and `/dice` separately, and
applies when it is longer than the message delay.

If narration starts arriving out of order or getting dropped in-game, raise
these.

## Automation

Five checkboxes, all optional:

- **Auto-open trade for Double Down / Split** - opens a trade window when a
  player doubles or splits and their bank cannot cover it.
- **Prompt to update bank when trade detected** - the master switch for trade
  detection. Leave this on. Without it the plugin cannot see the gil coming in.
- **Start player turns automatically after the deal** - skips the "Begin Player
  Turns" click once the deal has finished narrating.
- **Auto-target active player on their turn** - targets whoever is up.
- **Target player before sending Remind message** - targets the player first so
  the reminder is obviously aimed at them.

## Rules and narration

Two buttons at the bottom of Settings:

- **Edit Blackjack Rules** - the house rules, with the resulting house edge
  computed live. The "Rules and house edge" page covers these in detail.
- **Edit Narration Templates** - every line the plugin says. Each line can have
  several variants, and one is picked at random each time, so the table does not
  read like a robot.

You do not have to touch either to run a night. The defaults are a working
3:2 table.
