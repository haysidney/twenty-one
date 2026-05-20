#if DEBUG
using System.Collections.Generic;
using TwentyOne.Debug;
using TwentyOne.Game;

namespace TwentyOne.Windows;

public partial class MainWindow : IScenarioCallbacks
{
    GameState IScenarioCallbacks.State => State;
    Configuration IScenarioCallbacks.Config => config;
    Dictionary<int, string> IScenarioCallbacks.BetEdits => betEdits;
    Queue<(bool IsDealer, int PlayerIndex, int HandIndex, bool IsFirstCard)> IScenarioCallbacks.AutoDealQueue => autoDealQueue;
#pragma warning disable S2292 // backing fields are used throughout the class
    (int PlayerIndex, int HandIndex)? IScenarioCallbacks.PendingDouble
    {
        get => pendingDouble;
        set => pendingDouble = value;
    }
    (int PlayerIndex, int HandIndex)? IScenarioCallbacks.PendingSplit
    {
        get => pendingSplit;
        set => pendingSplit = value;
    }
#pragma warning restore S2292
    void IScenarioCallbacks.Apply(GameAction action) => Apply(action);
    void IScenarioCallbacks.ApplyBank(PlayerStat stat, IBankTransaction tx) => ApplyBank(stat, tx);
    void IScenarioCallbacks.QueueHitRoll(bool isDealer, int pi, int hi) => QueueHitRoll(isDealer, pi, hi);
    void IScenarioCallbacks.UpdatePlayerStats() => UpdatePlayerStats();
    void IScenarioCallbacks.ConfirmDoublePayment(int pi, int hi) => ConfirmDoublePayment(pi, hi);
    void IScenarioCallbacks.ConfirmSplitPayment(int pi, int hi) => ConfirmSplitPayment(pi, hi);
}
#endif
