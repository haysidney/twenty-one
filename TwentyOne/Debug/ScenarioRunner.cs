#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using TwentyOne.Game;
using TwentyOne.Windows;

namespace TwentyOne.Debug;

/// <summary>
/// Runtime state for the in-game scenario test harness (DEBUG-only). Holds the
/// active scripted scenario, the gating + fast-forward toggles, and the
/// pre-loaded debug roll queue. Pulled out of MainWindow so the scenario state
/// lives in one place; the action-dispatch (StartDeal / Hit:pi:hi / etc.)
/// lives here via ExecuteNextStep(IScenarioCallbacks).
/// </summary>
public sealed class ScenarioRunner
{
    /// <summary>Non-null while a scripted test scenario is running.</summary>
    public ActiveScenario? ActiveScenario { get; set; }

    /// <summary>When true, only the button matching the next scenario action is enabled.</summary>
    public bool GateButtons { get; set; } = true;

    /// <summary>When true, auto-steps through scenario actions as the chat queue drains each frame.</summary>
    public bool FastForward { get; set; } = false;

    /// <summary>Pre-loaded card values consumed by QueueHitRoll instead of /random rolls.</summary>
    public readonly Queue<int> RollQueue = new();

    /// <summary>True if no scenario is active, gating is off, or the next scripted step matches <paramref name="key"/>.</summary>
    public bool IsStep(string key) =>
        ActiveScenario == null || !GateButtons || ActiveScenario.PeekNext() == key;

    /// <summary>Advance the scenario pointer after a scripted button has been clicked.</summary>
    public void Advance() => ActiveScenario?.Advance();

    internal void ExecuteNextStep(IScenarioCallbacks cb)
    {
        var step = ActiveScenario?.PeekNext();
        if (step == null) return;
        Advance();
        switch (step)
        {
            case "StartDeal":
                foreach (var (idx, val) in cb.BetEdits.ToList())
                {
                    cb.BetEdits.Remove(idx);
                    if (val != cb.State.Players[idx].Bet)
                        cb.Apply(new SetPlayerBet(idx, val));
                }
                cb.Apply(new StartDeal());
                foreach (var p in cb.State.Players)
                {
                    if (p.SittingOut) continue;
                    var betAmt = (long)Math.Ceiling(GameEngine.ParseBet(p.Bet));
                    if (betAmt <= 0) continue;
                    if (!p.TryGetBankingStat(cb.Config, out var betStat)) continue;
                    cb.ApplyBank(betStat, new BankBet(betAmt));
                }
                for (var i = 0; i < cb.State.Players.Length; i++)
                {
                    if (cb.State.Players[i].SittingOut) continue;
                    cb.AutoDealQueue.Enqueue((false, i, 0, true));
                    cb.AutoDealQueue.Enqueue((false, i, 0, false));
                }
                cb.Apply(new AnnounceDealerDeal());
                cb.QueueHitRoll(isDealer: true, -1, -1);
                break;
            case "BeginPlayerTurns":
                cb.Apply(new BeginPlayerTurns());
                break;
            case "BeginDealerTurn":
                cb.Apply(new BeginDealerTurn());
                break;
            case "GoToPayout":
                cb.Apply(new GoToPayout());
                cb.UpdatePlayerStats();
                break;
            case "NewRound":
                cb.Apply(new NewRound());
                break;
            case "DealerHit":
                cb.Apply(new AnnounceDealerHit());
                cb.QueueHitRoll(isDealer: true, -1, -1);
                break;
            case "AdvancePlayer":
                cb.Apply(new AdvanceToNextPlayer());
                break;
            default:
            {
                var parts = step.Split(':');
                if (parts.Length < 3 || !int.TryParse(parts[1], out var pi) || !int.TryParse(parts[2], out var hi))
                    break;
                var p    = pi < cb.State.Players.Length ? cb.State.Players[pi] : null;
                var hand = p != null && hi < p.Hands.Length ? p.Hands[hi] : null;
                if (p == null || hand == null) break;
                switch (parts[0])
                {
                    case "Hit":
                        cb.Apply(new AnnouncePlayerHit(pi, hi));
                        cb.QueueHitRoll(isDealer: false, pi, hi);
                        break;
                    case "Stand":
                        cb.Apply(new StandPlayer(pi, hi));
                        break;
                    case "Dbl":
                    {
                        var dblBet     = GameEngine.GetEffectiveBet(p, hand);
                        var dblBank    = p.BankBalance(cb.Config);
                        var dblRounded = (long)Math.Ceiling(dblBet);
                        var fromBank   = dblBank >= dblRounded;
                        var bankAfter  = fromBank ? dblBank - dblRounded : dblRounded - dblBank;
                        cb.PendingDouble = (pi, hi);
                        cb.Apply(new AnnounceDouble(pi, hi, fromBank, bankAfter));
                        break;
                    }
                    case "Spl":
                    {
                        var splBet     = GameEngine.GetEffectiveBet(p, hand);
                        var splBank    = p.BankBalance(cb.Config);
                        var splRounded = (long)Math.Ceiling(splBet);
                        var fromBank   = splBank >= splRounded;
                        var bankAfter  = fromBank ? splBank - splRounded : splRounded - splBank;
                        cb.PendingSplit = (pi, hi);
                        cb.Apply(new AnnounceSplit(pi, hi, fromBank, bankAfter));
                        break;
                    }
                    case "ConfirmDbl":
                        cb.ConfirmDoublePayment(pi, hi);
                        break;
                    case "ConfirmSpl":
                        cb.ConfirmSplitPayment(pi, hi);
                        break;
                }
                break;
            }
        }
    }
}
#endif
