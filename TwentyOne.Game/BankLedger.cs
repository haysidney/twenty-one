using System;

namespace TwentyOne.Game;

public enum BankTransactionKind { Deposit, Withdrawal, Bet, Win, DoubleDown, Split, BetAdjust }

[Serializable]
public class BankTransactionEntry
{
    public DateTime            Timestamp { get; set; }
    public BankTransactionKind Kind      { get; set; }
    public long                Amount    { get; set; }
    public long                Balance   { get; set; }
}

// Discriminated union — one type per bank event
public interface IBankTransaction;

public record BankDeposit(long Amount)    : IBankTransaction;
public record BankWithdrawal(long Amount) : IBankTransaction;
public record BankBet(long Amount)        : IBankTransaction;
public record BankWin(long Amount)        : IBankTransaction;
public record BankDoubleDown(long Amount) : IBankTransaction;
public record BankSplit(long Amount)      : IBankTransaction;
// Signed delta: positive deducts from bank (bet increased), negative refunds (bet decreased).
// The recorded Amount keeps the sign so the audit log shows the direction.
public record BankBetAdjust(long Delta)   : IBankTransaction;

public static class BankLedger
{
    /// <summary>
    /// Pure function. Returns new balance and a log entry. Never produces a negative balance.
    /// The timestamp is supplied by the caller so the core stays free of <c>DateTime.Now</c>.
    /// </summary>
    public static (long NewBalance, BankTransactionEntry Entry) Apply(
        long balance, IBankTransaction tx, DateTime timestamp)
    {
        var (newBalance, kind, amount) = tx switch
        {
            BankDeposit    d => (balance + d.Amount,              BankTransactionKind.Deposit,    d.Amount),
            BankWithdrawal w => (Math.Max(0, balance - w.Amount), BankTransactionKind.Withdrawal, w.Amount),
            BankBet        b => (Math.Max(0, balance - b.Amount), BankTransactionKind.Bet,        b.Amount),
            BankWin        w => (balance + w.Amount,              BankTransactionKind.Win,        w.Amount),
            BankDoubleDown d => (Math.Max(0, balance - d.Amount), BankTransactionKind.DoubleDown, d.Amount),
            BankSplit      s => (Math.Max(0, balance - s.Amount), BankTransactionKind.Split,      s.Amount),
            BankBetAdjust  a => (Math.Max(0, balance - a.Delta),  BankTransactionKind.BetAdjust,  a.Delta),
            _                => throw new ArgumentOutOfRangeException(nameof(tx)),
        };

        var entry = new BankTransactionEntry
        {
            Timestamp = timestamp,
            Kind      = kind,
            Amount    = amount,
            Balance   = newBalance,
        };
        return (newBalance, entry);
    }
}
