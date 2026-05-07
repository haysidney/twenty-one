using System;

namespace TwentyOne.Game;

public enum BankTransactionKind { Deposit, Withdrawal, Bet, Win, DoubleDown, Split }

[Serializable]
public class BankTransactionEntry
{
    public DateTime            Timestamp { get; set; }
    public BankTransactionKind Kind      { get; set; }
    public long                Amount    { get; set; }
    public long                Balance   { get; set; }
}

// Discriminated union — one type per bank event
public abstract record BankTransaction;
public record BankDeposit(long Amount)    : BankTransaction;
public record BankWithdrawal(long Amount) : BankTransaction;
public record BankBet(long Amount)        : BankTransaction;
public record BankWin(long Amount)        : BankTransaction;
public record BankDoubleDown(long Amount) : BankTransaction;
public record BankSplit(long Amount)      : BankTransaction;

public static class BankLedger
{
    /// <summary>
    /// Pure function. Returns new balance and a log entry. Never produces a negative balance.
    /// The timestamp is supplied by the caller so the core stays free of <c>DateTime.Now</c>.
    /// </summary>
    public static (long NewBalance, BankTransactionEntry Entry) Apply(
        long balance, BankTransaction tx, DateTime timestamp)
    {
        var (newBalance, kind, amount) = tx switch
        {
            BankDeposit    d => (balance + d.Amount,              BankTransactionKind.Deposit,    d.Amount),
            BankWithdrawal w => (Math.Max(0, balance - w.Amount), BankTransactionKind.Withdrawal, w.Amount),
            BankBet        b => (Math.Max(0, balance - b.Amount), BankTransactionKind.Bet,        b.Amount),
            BankWin        w => (balance + w.Amount,              BankTransactionKind.Win,        w.Amount),
            BankDoubleDown d => (Math.Max(0, balance - d.Amount), BankTransactionKind.DoubleDown, d.Amount),
            BankSplit      s => (Math.Max(0, balance - s.Amount), BankTransactionKind.Split,      s.Amount),
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
