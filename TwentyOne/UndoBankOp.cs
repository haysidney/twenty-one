using System;
using TwentyOne.Game;

namespace TwentyOne;

/// <summary>
/// A single bank deduction recorded against an undoable transition so it can be
/// reversed (via a compensating <see cref="BankReversal"/>) when that action is
/// undone or the round is aborted. <see cref="BalanceEffect"/> is the signed
/// change the original op made to the bank; the reversal applies its inverse.
/// </summary>
[Serializable]
public sealed class UndoBankOp
{
    public string              StatKey      { get; set; } = string.Empty;
    public string              DisplayName  { get; set; } = string.Empty;
    public BankTransactionKind Kind         { get; set; }
    public long                BalanceEffect { get; set; }
}
