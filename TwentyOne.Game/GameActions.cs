namespace TwentyOne.Game;

public abstract record GameAction;

// Card actions
public record AddDealerCard(int Card) : GameAction;
public record AddPlayerCard(int PlayerIndex, int HandIndex, int Card) : GameAction;
public record StandPlayer(int PlayerIndex, int HandIndex) : GameAction;
public record DoubleDown(int PlayerIndex, int HandIndex) : GameAction;
public record SplitHand(int PlayerIndex, int HandIndex) : GameAction;

// Deal announcements (narration only, no state change)
public record AnnounceDealerDeal : GameAction;
public record AnnouncePlayerDeal(int PlayerIndex) : GameAction;

// Double/Split trade-request announcements (narration only, no state change)
public record AnnounceDouble(int PlayerIndex, int HandIndex) : GameAction;
public record AnnounceSplit(int PlayerIndex, int HandIndex) : GameAction;

// Hit announcements (narration only, no state change)
public record AnnounceDealerHit : GameAction;
public record AnnouncePlayerHit(int PlayerIndex, int HandIndex) : GameAction;

// Betting phase announcement (narration only)
public record AnnounceBettingOpen : GameAction;

// Bet-request announcement — sent when dealer shift+clicks Trade during Betting phase
public record AnnounceBetRequest(int PlayerIndex) : GameAction;

// Bet-confirm announcement — sent when dealer clicks Confirm in the Bet cell during Betting phase
public record AnnounceBetConfirm(int PlayerIndex) : GameAction;

// Phase transitions
public record StartDeal : GameAction;
public record BeginPlayerTurns : GameAction;
public record AdvanceToNextPlayer : GameAction;
public record BeginDealerTurn : GameAction;
public record GoToPayout : GameAction;
public record NewRound : GameAction;

// Roster management (Betting phase only)
public record AddPlayer(string Nickname, string FullName = "", string World = "") : GameAction;
public record RemovePlayer(int Index) : GameAction;
public record SetPlayerBet(int PlayerIndex, string Bet) : GameAction;
public record RenamePlayer(int PlayerIndex, string Nickname) : GameAction;
