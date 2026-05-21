using System.Collections.Generic;

namespace TwentyOne.Game;

public abstract record GameAction
{
    /// <summary>
    /// Whether this action should snapshot the prior <see cref="GameState"/> onto
    /// the undo stack before <see cref="GameEngine.Apply"/> runs. Narration-only
    /// actions and <see cref="BeginDealerTurn"/> override this to <c>false</c>;
    /// transient phases (split-pending / double-pending) still suppress the push
    /// at the call site.
    /// </summary>
    public virtual bool PushesUndo => true;
}

/// <summary>
/// Narration-only action - produces chat side effects but does not change
/// <see cref="GameState"/>, so it must not push onto the undo stack.
/// </summary>
public abstract record Announcement : GameAction
{
    public override bool PushesUndo => false;
}

// Card actions
public record AddDealerCard(int Card) : GameAction;
public record AddPlayerCard(int PlayerIndex, int HandIndex, int Card) : GameAction;
public record StandPlayer(int PlayerIndex, int HandIndex) : GameAction;
public record DoubleDown(int PlayerIndex, int HandIndex) : GameAction;
public record SplitHand(int PlayerIndex, int HandIndex) : GameAction;
public record SurrenderHand(int PlayerIndex, int HandIndex) : GameAction;

// Deal announcements (narration only, no state change)
public record AnnounceDealerDeal : Announcement;
public record AnnouncePlayerDeal(int PlayerIndex) : Announcement;

// Double/Split trade-request announcements (narration only, no state change)
// FromBank=true → deducting from bank (no trade needed); BankAfter = balance after deduction
public record AnnounceDouble(int PlayerIndex, int HandIndex, bool FromBank = false, long BankAfter = 0) : Announcement;
public record AnnounceDoubleConfirm(int PlayerIndex, int HandIndex) : Announcement;
public record AnnounceSplit(int PlayerIndex, int HandIndex, bool FromBank = false, long BankAfter = 0) : Announcement;

// Hit announcements (narration only, no state change)
public record AnnounceDealerHit : Announcement;
public record AnnouncePlayerHit(int PlayerIndex, int HandIndex) : Announcement;

// Resend player turn start announcement (narration only, no state change)
public record AnnouncePlayerTurn(int PlayerIndex, int HandIndex) : Announcement;

// Betting phase announcement (narration only)
public record AnnounceBettingOpen : Announcement;

// Bet-request announcement - sent when dealer shift+clicks Trade during Betting phase
public record AnnounceBetRequest(int PlayerIndex) : Announcement;

// Bet-confirm announcement - sent when dealer clicks Confirm in the Bet cell during Betting phase
// Bank is carried here since it lives outside GameState
public record AnnounceBetConfirm(int PlayerIndex, long Bank) : Announcement;

// Bank remind - sent when dealer clicks Remind in the Bank cell; carries bank balance since it lives outside GameState
public record AnnounceBankRemind(int PlayerIndex, long Bank) : Announcement;

// Bank shortfall request - sent when dealer shift+clicks Deposit and bank < bet
public record AnnounceBankShortfall(int PlayerIndex, long ShortfallAmount) : Announcement;

// Bank deposit/withdraw narration - logged after direct bank mutations
public record AnnounceBankDeposit (int PlayerIndex, long Amount, long NewBalance) : Announcement;
public record AnnounceBankWithdraw(int PlayerIndex, long Amount, long NewBalance) : Announcement;

// Phase transitions
public record StartDeal : GameAction;
public record BeginPlayerTurns : GameAction;
public record AdvanceToNextPlayer : GameAction;
// BeginDealerTurn flips the hole card and lets the engine continue; it carries
// no state to restore via undo (the next concrete card action snapshots first).
public record BeginDealerTurn : GameAction { public override bool PushesUndo => false; }
public record GoToPayout : GameAction;
public record NewRound : GameAction;

// Roster management (Betting phase only)
public record AddPlayer(string Nickname, string FullName = "", string World = "") : GameAction;
public record RemovePlayer(int Index) : GameAction;
public record SetPlayerBet(int PlayerIndex, string Bet) : GameAction;
// Adjusts a player's bet during the Deal phase (between Start Deal and Begin Player Turns).
// Bank reconciliation is the caller's responsibility (handled in MainWindow). This action
// does NOT push undo: bank entries are append-only, so a state-only revert would leave the
// player's recorded bet inconsistent with their actual bank deductions.
public record AdjustBet(int PlayerIndex, string Bet) : GameAction { public override bool PushesUndo => false; }
public record RenamePlayer(int PlayerIndex, string Nickname) : GameAction;
public record ReorderPlayers(List<int> NewOrder) : GameAction;
public record ToggleSittingOut(int PlayerIndex) : GameAction;
