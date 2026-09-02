using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TwentyOne.Game;

// Appended-only: the enum serializes by ordinal, so new members go at the end.
public enum BankTransactionKind { Deposit, Withdrawal, Bet, Win, DoubleDown, Split, BetAdjust, Surrender, Credit, Reversal, Tip, Transfer }

[Serializable]
public class BankTransactionEntry
{
    public DateTime            Timestamp { get; set; }
    public BankTransactionKind Kind      { get; set; }
    public long                Amount    { get; set; }
    public long                Balance   { get; set; }

    [JsonExtensionData] public Dictionary<string, JToken> ExtraData { get; set; } = new();
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
// Compensating entry that reverses a previously-applied bank op when its action
// is undone or the round is aborted. Signed Delta: positive credits the bank
// (reversing a deduction like Bet/DoubleDown/Split), negative debits it. Kept as
// a distinct kind so the audit log shows it as a reversal, not a fresh deposit.
public record BankReversal(long Delta)    : IBankTransaction;
// Player leaves part of their bank for the dealer. No gil moves - the dealer is
// already holding it - so this is a pure relabeling: the bank drops and the
// amount is added to Configuration.Tips. In the session-ledger identity the two
// cancel (banksHeld -N, tipTotal +N), leaving AdjustedDiff and drift untouched.
public record BankTip(long Amount)        : IBankTransaction;
// One leg of a bank-to-bank transfer. Signed Delta: negative debits the sender,
// positive credits the recipient. Two entries are posted per transfer, so the
// banksHeld total is unchanged and reconciliation stays neutral by construction.
public record BankTransfer(long Delta)    : IBankTransaction;

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
            BankReversal   r => (Math.Max(0, balance + r.Delta),  BankTransactionKind.Reversal,   r.Delta),
            BankTip        t => (Math.Max(0, balance - t.Amount), BankTransactionKind.Tip,        t.Amount),
            BankTransfer   t => (Math.Max(0, balance + t.Delta),  BankTransactionKind.Transfer,   t.Delta),
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
