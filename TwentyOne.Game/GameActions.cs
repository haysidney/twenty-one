namespace TwentyOne.Game;

public abstract record GameAction;

// Card actions
public record AddDealerCard(int Card) : GameAction;
public record AddPlayerCard(int PlayerIndex, int HandIndex, int Card) : GameAction;
public record StandPlayer(int PlayerIndex, int HandIndex) : GameAction;

// Deal announcements (narration only, no state change)
public record AnnounceDealerDeal : GameAction;
public record AnnouncePlayerDeal(int PlayerIndex) : GameAction;

// Betting phase announcement (narration only)
public record AnnounceBettingOpen(string MinBet, string MaxBet) : GameAction;

// Phase transitions
public record StartDeal : GameAction;
public record BeginPlayerTurns : GameAction;
public record GoToPayout : GameAction;
public record NewRound : GameAction;

// Roster management (Betting phase only)
public record AddPlayer(string Name) : GameAction;
public record RemovePlayer(int Index) : GameAction;
public record SetPlayerBet(int PlayerIndex, string Bet) : GameAction;
public record RenamePlayer(int PlayerIndex, string Name) : GameAction;
