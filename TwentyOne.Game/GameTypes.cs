using System;
using System.Collections.Generic;

namespace TwentyOne.Game;

public enum HandState { Playing, Stand, Bust, Blackjack }
public enum GamePhase { Betting, Deal, PlayerTurns, DealerTurn, Payout }
public enum BlackjackPayout { ThreeToTwo, SixToFive, EvenMoney }
public enum PayoutResult { None, Win, BjWin, Lose, Push }

[Serializable]
public class Hand
{
    public List<int> Cards { get; set; } = [];
    public HandState State { get; set; } = HandState.Playing;
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
    public BlackjackPayout BjPayout { get; set; } = BlackjackPayout.ThreeToTwo;
}
