using System;

namespace TwentyOne.Game;

public enum BankTransactionKind { Deposit, Withdrawal, Bet, Win, DoubleDown, Split, BetAdjust, Surrender, Credit }

[Serializable]
public class BankTransactionEntry
{
    public DateTime            Timestamp { get; set; }
    public BankTransactionKind Kind      { get; set; }
    public long                Amount    { get; set; }
    public long                Balance   { get; set; }
}

// Discriminated union - one type per bank event
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
// Half-bet refund at settlement when the player surrendered. Amount is the gil
// returned to the bank (bet - ceil(bet/2)); the half-loss was already debited
// by the original BankBet at deal start.
public record BankSurrender(long Amount)  : IBankTransaction;
// Venue-funded deposit (VIP / free play). Behaves like a regular deposit in the
// bank ledger; tagged separately so the session ledger can total credits issued
// and the bank log distinguishes them from player deposits.
public record BankCredit(long Amount)     : IBankTransaction;

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
            BankSurrender  s => (balance + s.Amount,              BankTransactionKind.Surrender,  s.Amount),
            BankCredit     c => (balance + c.Amount,              BankTransactionKind.Credit,     c.Amount),
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
