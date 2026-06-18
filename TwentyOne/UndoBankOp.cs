using System;
using System.Collections.Generic;
using TwentyOne.Game;

namespace TwentyOne;

/// <summary>
/// One undo-stack entry: the <see cref="GameState"/> snapshot taken before an
/// undoable action, plus the bank deductions applied during that transition.
/// Bundling them in one type means the state and its compensating bank ops can
/// never desync - clearing or popping the undo stack carries the bank ops with it.
/// </summary>
[Serializable]
public sealed class UndoEntry
{
    public GameState         State   { get; set; } = new();
    public List<UndoBankOp>  BankOps { get; set; } = [];
}

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
