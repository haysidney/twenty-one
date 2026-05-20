#if DEBUG
using System.Collections.Generic;
using TwentyOne.Game;

namespace TwentyOne.Debug;

internal interface IScenarioCallbacks
{
    GameState State { get; }
    Configuration Config { get; }
    Dictionary<int, string> BetEdits { get; }
    Queue<(bool IsDealer, int PlayerIndex, int HandIndex, bool IsFirstCard)> AutoDealQueue { get; }
    (int PlayerIndex, int HandIndex)? PendingDouble { get; set; }
    (int PlayerIndex, int HandIndex)? PendingSplit { get; set; }
    void Apply(GameAction action);
    void ApplyBank(PlayerStat stat, IBankTransaction tx);
    void QueueHitRoll(bool isDealer, int pi, int hi);
    void UpdatePlayerStats();
    void ConfirmDoublePayment(int pi, int hi);
    void ConfirmSplitPayment(int pi, int hi);
}
#endif
