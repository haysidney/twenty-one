using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TwentyOne.Game;

[Serializable]
public class RoundHistoryEntry
{
    public int       RoundNumber { get; set; }
    public GameState Snapshot    { get; set; } = new();
    // Bank net from the venue's perspective: positive = bank gained, negative = bank paid out.
    public long      BankNet     { get; set; }
    // Player bank balances after payout (key = PlayerStatKey). Used by history window tooltips.
    public Dictionary<string, long> PlayerBanks { get; set; } = [];
    // Player bank balances captured just before the round's payout was applied.
    // Allows per-round bank deltas to be derived without chaining adjacent entries.
    public Dictionary<string, long> PrePayoutPlayerBanks { get; set; } = [];
    // Wall-clock times for the round. StartedAt is set when the dealer transitions
    // out of Betting (StartDeal); FinishedAt at the end of payout settlement.
    // Both default to DateTime.MinValue for entries logged before this field existed.
    public DateTime StartedAt  { get; set; } = DateTime.MinValue;
    public DateTime FinishedAt { get; set; } = DateTime.MinValue;
    // Sequence of engine-level actions that produced this round, in order. Each
    // entry is a short tag produced by ActionLog.Format - e.g. "StartDeal",
    // "Deal:D:7", "Deal:0:0:10", "Stand:0:0", "Dbl:1:0", "BeginDealerTurn",
    // "GoToPayout". Empty for entries logged before this field existed.
    // Announcements (narration-only actions) are not included.
    public List<string> Actions { get; set; } = [];

    [JsonExtensionData] public Dictionary<string, JToken> ExtraData { get; set; } = new();
}

public enum HandState { Playing, Stand, Bust, Blackjack, Charlie, Surrendered }
public enum GamePhase { Betting, Deal, PlayerTurns, DealerTurn, Payout }
public enum PayoutRatio { ThreeToTwo, SixToFive, EvenMoney }
public enum PayoutResult { None, Win, BjWin, CharlieWin, Lose, Push, Surrender }
public enum FiveCardCharlieRule { Disabled, BeatsAll, LosesToDealerBJ }
public enum ResplitCap { Max2, Max3, Max4, Unlimited }
public enum DoubleRestriction { Any, Hard9To11, Hard10To11, HardOnly }

// How narration lines that start with a channel command (e.g. "/y ...") behave
// when that channel differs from the configured ChatChannel.
//   Block    - rewrite to "/echo /y ..." so only the dealer sees it locally.
//   Redirect - strip the override and send via the configured channel.
//   Allow    - send as-is, broadcasting in the override channel.
public enum CrossChannelCommands { Block, Redirect, Allow }

[Serializable]
public sealed record class Hand
{
    public ImmutableArray<int> Cards { get; set; } = [];
    public HandState State { get; set; } = HandState.Playing;
    // True after player doubles; Bet holds the new (doubled) amount.
    public bool Doubled { get; set; } = false;
    // Per-hand effective bet (empty string = inherit from Player.Bet). Set on double.
    public string Bet { get; set; } = string.Empty;
    // True when created by splitting: 21 is not a blackjack; aces forced-stand after 1 card.
    public bool IsFromSplit { get; set; } = false;
}

[Serializable]
public sealed record class Player
{
    // For manual players: their name. For FFXIV players: their nickname (may be empty).
    public string Nickname { get; set; } = string.Empty;
    // Set for players added via right-click; empty for manually-entered players.
    public string FullName { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
    public string Bet { get; set; } = string.Empty;
    public ImmutableArray<Hand> Hands { get; set; } = [];
    public bool SittingOut { get; set; } = false;

    // Nickname if set; else first name from FullName; else Nickname (empty for edge cases).
    public string DisplayName
    {
        get
        {
            if (Nickname.Length > 0) return Nickname;
            if (FullName.Length > 0) return FullName.Split(' ')[0];
            return Nickname;
        }
    }
}

[Serializable]
public sealed record class GameState
{
    public ImmutableArray<Player> Players { get; set; } = [];
    public Hand DealerHand { get; set; } = new();
    public GamePhase Phase { get; set; } = GamePhase.Betting;
    public int ActivePlayerIndex { get; set; } = -1;
    public int ActiveHandIndex { get; set; } = -1;
    public bool WaitingForNextPlayer { get; set; } = false;
    public bool WaitingForDealer { get; set; } = false;
    // Blackjack payout multiplier (e.g. 1.5 = 3:2, 1.2 = 6:5, 1.0 = even money).
    // Free-form so venues can offer any payout; the standard 3:2/6:5/1:1 are presets.
    public double BjPayout { get; set; } = 1.5;
    public PayoutRatio CharliePayout { get; set; } = PayoutRatio.EvenMoney;
    public FiveCardCharlieRule FiveCardCharlie { get; set; } = FiveCardCharlieRule.Disabled;
    // Total at which the dealer stops drawing (17 = standard). Venues run
    // variants - standing on 16 is common in FFXIV houses.
    public int DealerStandThreshold { get; set; } = 17;
    // Whether the dealer hits a SOFT hand exactly at the threshold. True (default)
    // with threshold 17 is the classic H17; false is S17.
    public bool DealerHitsSoftThreshold { get; set; } = true;
    // When true (default), the player may double after splitting. When false, doubling
    // is restricted to non-split hands.
    public bool DoubleAfterSplit { get; set; } = true;
    // When true, a split-ace hand may be hit beyond its first dealt card. When false
    // (default), split aces auto-stand after receiving their one extra card (standard rule).
    public bool HitSplitAces { get; set; } = false;
    // When true, a pair of aces produced by an earlier split may be split again. When
    // false (default), split-ace pairs cannot be resplit (standard rule).
    public bool ResplitAces { get; set; } = false;
    // Maximum number of hands a non-ace pair may be split into. Aces ignore this cap
    // and are governed solely by ResplitAces.
    public ResplitCap ResplitCap { get; set; } = ResplitCap.Unlimited;
    // Which 2-card totals may be doubled down. Any (default) allows every 2-card
    // hand; the other values restrict to common European/Reno style ranges. DAS
    // stacks independently - both must allow doubling on a post-split hand.
    public DoubleRestriction DoubleRestriction { get; set; } = DoubleRestriction.Any;
    // When true, the player may surrender an initial 2-card hand for a -0.5x bet
    // payout. Available only on the original hand (not after hit or split). Because
    // the engine is ENHC (no peek), this is effectively early surrender: half the bet
    // is forfeited even when the dealer ends up with Blackjack.
    public bool AllowSurrender { get; set; } = false;
    // FullNames (or Nicknames for manual players) of players who won last round.
    public HashSet<string> LastRoundWinners { get; set; } = [];
    // FullNames (or Nicknames for manual players) of players who pushed last round.
    public HashSet<string> LastRoundPushers { get; set; } = [];
    // Skip deal summary when there is only one player.
    public bool SkipDealSummaryOnePlayer { get; set; } = true;
}
