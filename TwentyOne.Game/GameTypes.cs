using System;
using System.Collections.Generic;

namespace TwentyOne.Game;

[Serializable]
public class RoundSummary
{
    public int RoundNumber { get; set; }
    public long BankNet { get; set; }
    // Key: "{FullName}@{World}" or Nickname — same format as RoundHistoryEntry.PlayerBanks
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
public class Hand
{
    public List<int> Cards { get; set; } = [];
    public HandState State { get; set; } = HandState.Playing;
    // True after player doubles; Bet holds the new (doubled) amount.
    public bool Doubled { get; set; } = false;
    // Per-hand effective bet (empty string = inherit from Player.Bet). Set on double.
    public string Bet { get; set; } = string.Empty;
    // True when created by splitting: 21 is not a blackjack; aces forced-stand after 1 card.
    public bool IsFromSplit { get; set; } = false;
}

[Serializable]
public class Player
{
    // For manual players: their name. For FFXIV players: their nickname (may be empty).
    public string Nickname { get; set; } = string.Empty;
    // Set for players added via right-click; empty for manually-entered players.
    public string FullName { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
    public string Bet { get; set; } = string.Empty;
    public List<Hand> Hands { get; set; } = [];
    public bool SittingOut { get; set; } = false;

    // Nickname if set; else first name from FullName; else Nickname (empty for edge cases).
    public string DisplayName => Nickname.Length > 0
        ? Nickname
        : FullName.Length > 0
            ? FullName.Split(' ')[0]
            : Nickname;
}

[Serializable]
public class GameState
{
    public List<Player> Players { get; set; } = [];
    public Hand DealerHand { get; set; } = new();
    public GamePhase Phase { get; set; } = GamePhase.Betting;
    public int ActivePlayerIndex { get; set; } = -1;
    public int ActiveHandIndex { get; set; } = -1;
    public bool WaitingForNextPlayer { get; set; } = false;
    public bool WaitingForDealer { get; set; } = false;
    public PayoutRatio BjPayout { get; set; } = PayoutRatio.ThreeToTwo;
    public PayoutRatio CharliePayout { get; set; } = PayoutRatio.EvenMoney;
    public FiveCardCharlieRule FiveCardCharlie { get; set; } = FiveCardCharlieRule.Disabled;
    // FullNames (or Nicknames for manual players) of players who won last round.
    public HashSet<string> LastRoundWinners { get; set; } = [];
    // FullNames (or Nicknames for manual players) of players who pushed last round.
    public HashSet<string> LastRoundPushers { get; set; } = [];
    // Skip deal summary when there is only one player.
    public bool SkipDealSummaryOnePlayer { get; set; } = true;
}
