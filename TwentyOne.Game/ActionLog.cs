namespace TwentyOne.Game;

// Compact textual encoding of engine-level actions for round-history logging.
// Returns null for actions that should not appear in the log: Announcement
// subtypes (narration-only) and NewRound (terminates a round; the next round's
// log starts at StartDeal). Roster actions are betting-phase and fire before
// StartDeal, so they're skipped here as well.
//
// Format choices favor compactness and parseability for future replay tooling.
// "D" denotes the dealer; numeric indexes are player/hand. Cards are 1-13
// (1 = ace, 11-13 = J/Q/K).
public static class ActionLog
{
    public static string? Format(GameAction action) => action switch
    {
        Announcement            => null,
        NewRound                => null,
        AddPlayer               => null,
        RemovePlayer            => null,
        SetPlayerBet            => null,
        RenamePlayer            => null,
        ReorderPlayers          => null,
        ToggleSittingOut        => null,

        StartDeal               => "StartDeal",
        BeginPlayerTurns        => "BeginPlayerTurns",
        BeginDealerTurn         => "BeginDealerTurn",
        GoToPayout              => "GoToPayout",
        AdvanceToNextPlayer     => "AdvancePlayer",

        AddDealerCard adc       => $"Deal:D:{adc.Card}",
        AddPlayerCard apc       => $"Deal:{apc.PlayerIndex}:{apc.HandIndex}:{apc.Card}",
        StandPlayer sp          => $"Stand:{sp.PlayerIndex}:{sp.HandIndex}",
        DoubleDown dd           => $"Dbl:{dd.PlayerIndex}:{dd.HandIndex}",
        SplitHand sh            => $"Spl:{sh.PlayerIndex}:{sh.HandIndex}",
        SurrenderHand srn       => $"Srn:{srn.PlayerIndex}:{srn.HandIndex}",
        AdjustBet ab            => $"AdjustBet:{ab.PlayerIndex}:{ab.Bet}",

        _ => null,
    };
}
