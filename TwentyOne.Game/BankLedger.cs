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
    /// </summary>
    public static (long NewBalance, BankTransactionEntry Entry) Apply(long balance, BankTransaction tx)
    {
        var (newBalance, kind) = tx switch
        {
            BankDeposit    d => (balance + d.Amount,               BankTransactionKind.Deposit),
            BankWithdrawal w => (Math.Max(0, balance - w.Amount),  BankTransactionKind.Withdrawal),
            BankBet        b => (Math.Max(0, balance - b.Amount),  BankTransactionKind.Bet),
            BankWin        w => (balance + w.Amount,               BankTransactionKind.Win),
            BankDoubleDown d => (Math.Max(0, balance - d.Amount),  BankTransactionKind.DoubleDown),
            BankSplit      s => (Math.Max(0, balance - s.Amount),  BankTransactionKind.Split),
            _                => throw new ArgumentOutOfRangeException(nameof(tx)),
        };

        var amount = tx switch
        {
            BankDeposit    d => d.Amount,
            BankWithdrawal w => w.Amount,
            BankBet        b => b.Amount,
            BankWin        w => w.Amount,
            BankDoubleDown d => d.Amount,
            BankSplit      s => s.Amount,
            _                => 0L,
        };

        var entry = new BankTransactionEntry
        {
            Timestamp = DateTime.Now,
            Kind      = kind,
            Amount    = amount,
            Balance   = newBalance,
        };
        return (newBalance, entry);
    }
}
