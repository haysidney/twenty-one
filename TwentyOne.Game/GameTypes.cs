using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TwentyOne.Game;

[Serializable]
public class RoundSummary
{
    public int RoundNumber { get; set; }
    public long BankNet { get; set; }
    // Key: "{FullName}@{World}" or Nickname - same format as RoundHistoryEntry.PlayerBanks
    public Dictionary<string, long> PlayerBanks { get; set; } = [];
    public List<string> Winners { get; set; } = [];
    public List<string> Losers  { get; set; } = [];
    public List<string> Pushes  { get; set; } = [];
}

[Serializable]
public class RoundHistoryEntry
{
    public int       RoundNumber { get; set; }
    public GameState Snapshot    { get; set; } = new();
    // Bank net from the venue's perspective: positive = bank gained, negative = bank paid out.
    public long      BankNet     { get; set; }
    // Player bank balances after payout (key = PlayerStatKey). Used by history window tooltips.
    public Dictionary<string, long> PlayerBanks { get; set; } = [];
}

public enum HandState { Playing, Stand, Bust, Blackjack, Charlie }
public enum GamePhase { Betting, Deal, PlayerTurns, DealerTurn, Payout }
public enum PayoutRatio { ThreeToTwo, SixToFive, EvenMoney }
public enum PayoutResult { None, Win, BjWin, CharlieWin, Lose, Push }
public enum FiveCardCharlieRule { Disabled, BeatsAll, LosesToDealerBJ }

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
    // When true, dealer stands on soft 17 (S17). When false (default), dealer hits soft 17 (H17).
    public bool DealerStandsOnSoft17 { get; set; } = false;
    // When true (default), the player may double after splitting. When false, doubling
    // is restricted to non-split hands.
    public bool DoubleAfterSplit { get; set; } = true;
    // When true, a split-ace hand may be hit beyond its first dealt card. When false
    // (default), split aces auto-stand after receiving their one extra card (standard rule).
    public bool HitSplitAces { get; set; } = false;
    // When true, a pair of aces produced by an earlier split may be split again. When
    // false (default), split-ace pairs cannot be resplit (standard rule).
    public bool ResplitAces { get; set; } = false;
    // FullNames (or Nicknames for manual players) of players who won last round.
    public HashSet<string> LastRoundWinners { get; set; } = [];
    // FullNames (or Nicknames for manual players) of players who pushed last round.
    public HashSet<string> LastRoundPushers { get; set; } = [];
    // Skip deal summary when there is only one player.
    public bool SkipDealSummaryOnePlayer { get; set; } = true;
}
