namespace TwentyOne.Game;

/// <summary>
/// How an FFXIV trade's gil flow maps onto the bank ledger.
/// </summary>
public enum TradeDirection
{
    /// No gil moved in either direction.
    None,
    /// Player put gil in: deposit to their bank.
    Deposit,
    /// Dealer handed gil over: withdraw from their bank (cashout).
    Withdraw,
    /// Both sides put gil in the window: confirm a withdrawal and a deposit.
    TwoSided,
}

/// <summary>
/// Pure routing decision for a completed trade. Bank-only mode: incoming gil
/// always deposits, outgoing always withdraws, and a bidirectional trade is
/// resolved as both. Nothing is ever silently dropped - the bidirectional case
/// (both > 0) used to fall through to a withdrawal-only path that absorbed the
/// incoming gil, the root cause of session-ledger drift.
/// </summary>
public static class TradeRouting
{
    public static TradeDirection Resolve(long gaveGil, long receivedGil)
    {
        if (gaveGil > 0 && receivedGil > 0) return TradeDirection.TwoSided;
        if (gaveGil > 0)                    return TradeDirection.Withdraw;
        if (receivedGil > 0)                return TradeDirection.Deposit;
        return TradeDirection.None;
    }
}
