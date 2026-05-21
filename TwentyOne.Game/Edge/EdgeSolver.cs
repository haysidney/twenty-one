using System;
using System.Collections.Generic;

namespace TwentyOne.Game.Edge;

// Rule axes that affect house edge. Mirrors the relevant GameState fields.
public readonly record struct EdgeRules(
    PayoutRatio BjPayout,
    PayoutRatio CharliePayout,
    FiveCardCharlieRule FiveCardCharlie,
    bool DealerStandsOnSoft17 = false,
    bool DoubleAfterSplit = true,
    bool HitSplitAces = false,
    bool ResplitAces = false);

public enum OptimalAction
{
    Stand,
    Hit,
    Double,
    Split,
}

/// <summary>
/// Exact infinite-deck EV solver for the venue rule set. Returns the house edge
/// (negative of optimal player EV) under basic-strategy decisions. Uses
/// fractional payout multipliers (1.5 / 1.2 / 1.0), not the engine's
/// bet-amount-dependent Math.Ceiling - the difference vanishes at typical
/// gil bet sizes.
/// </summary>
public static class EdgeSolver
{
    public static double ComputeHouseEdge(EdgeRules rules)
    {
        var s = new Solver(rules);
        return -s.OverallPlayerEV();
    }

    public static OptimalAction GetOptimalAction(int card1, int card2, int upcard, EdgeRules rules)
    {
        var s = new Solver(rules);
        return s.OptimalActionForInitial(card1, card2, upcard);
    }

    // Per-action EV for an initial 2-card hand vs dealer upcard. Useful for
    // diagnosing borderline strategy cells. Returns NaN for Split when the
    // initial hand isn't a pair.
    public static double GetActionEV(OptimalAction action, int card1, int card2, int upcard, EdgeRules rules)
    {
        var s = new Solver(rules);
        return s.ActionEVForInitial(action, card1, card2, upcard);
    }

    private sealed class Solver
    {
        private readonly EdgeRules _rules;
        private readonly bool _charlieOn;
        private readonly double _bjMul;
        private readonly double _charlieMul;

        private readonly Dictionary<int, double[]> _dealerCache = new();
        private readonly Dictionary<int, double>   _playerCache = new();

        // Dealer outcome bucket indices.
        private const int OBust = 0;
        private const int O17   = 1;
        private const int O18   = 2;
        private const int O19   = 3;
        private const int O20   = 4;
        private const int O21   = 5; // 21 from 3+ cards
        private const int OBJ   = 6; // natural 2-card 21
        private const int NumOutcomes = 7;

        public Solver(EdgeRules rules)
        {
            _rules      = rules;
            _charlieOn  = rules.FiveCardCharlie != FiveCardCharlieRule.Disabled;
            _bjMul      = Multiplier(rules.BjPayout);
            _charlieMul = Multiplier(rules.CharliePayout);
        }

        private static double Multiplier(PayoutRatio r) => r switch
        {
            PayoutRatio.ThreeToTwo => 1.5,
            PayoutRatio.SixToFive  => 1.2,
            PayoutRatio.EvenMoney  => 1.0,
            _                      => 1.5,
        };

        private static int FaceValue(int card)
        {
            if (card >= 10) return 10;
            return card;
        }

        // Mirrors GameEngine.HandValue + IsSoft incrementally.
        private static (int total, bool isSoft) AddCard(int total, bool isSoft, int card)
        {
            int newTotal = total + FaceValue(card);
            bool newSoft = isSoft;
            if (card == 1 && newTotal + 10 <= 21)
            {
                newTotal += 10;
                newSoft   = true;
            }
            if (newTotal > 21 && newSoft)
            {
                newTotal -= 10;
                newSoft   = false;
            }
            return (newTotal, newSoft);
        }

        // ── Dealer ─────────────────────────────────────────────────────────────

        private double[] DealerDist(int upcard)
        {
            if (_dealerCache.TryGetValue(upcard, out var cached)) return cached;
            var dist = new double[NumOutcomes];
            var (t, s) = AddCard(0, false, upcard);
            DealerRecurse(t, s, 1, 1.0, dist);
            _dealerCache[upcard] = dist;
            return dist;
        }

        private void DealerRecurse(int total, bool isSoft, int numCards, double prob, double[] dist)
        {
            if (total > 21)
            {
                dist[OBust] += prob;
                return;
            }
            bool hit = total < 17 || (total == 17 && isSoft && !_rules.DealerStandsOnSoft17);
            if (!hit)
            {
                int bucket = total switch
                {
                    17 => O17,
                    18 => O18,
                    19 => O19,
                    20 => O20,
                    21 => numCards == 2 ? OBJ : O21,
                    _  => OBust,
                };
                dist[bucket] += prob;
                return;
            }
            double sub = prob / 13.0;
            for (int c = 1; c <= 13; c++)
            {
                var (nt, ns) = AddCard(total, isSoft, c);
                DealerRecurse(nt, ns, numCards + 1, sub, dist);
            }
        }

        // ── Player ─────────────────────────────────────────────────────────────

        public double OverallPlayerEV()
        {
            double total = 0;
            double cardP = 1.0 / 13.0;
            double tripP = cardP * cardP * cardP;
            for (int c1 = 1; c1 <= 13; c1++)
            {
                for (int c2 = 1; c2 <= 13; c2++)
                {
                    for (int u = 1; u <= 13; u++)
                    {
                        total += tripP * EvalInitial(c1, c2, u);
                    }
                }
            }
            return total;
        }

        // Top-level decision for an initial 2-card hand. Considers Split in
        // addition to whatever EvalHand chooses.
        private double EvalInitial(int c1, int c2, int upcard)
        {
            var (t1, s1) = AddCard(0,  false, c1);
            var (t,  s)  = AddCard(t1, s1,    c2);

            if (t == 21) return BjEV(upcard);

            double best = EvalHand(t, s, 2, false, upcard);

            // Split eligibility matches GameEngine.CanSplit: same rank.
            if (c1 == c2)
            {
                double splitEV = EvalSplit(c1, upcard);
                if (splitEV > best) best = splitEV;
            }
            return best;
        }

        // Choose max over { Stand, Hit, Double (if 2 cards) }. EV in units of
        // the original bet. BJ is handled by the caller before this is invoked.
        private double EvalHand(int total, bool isSoft, int numCards, bool isFromSplit, int upcard)
        {
            if (total > 21) return -1;

            if (_charlieOn && numCards >= 5) return CharlieEV(upcard);

            if (total == 21) return StandEV(21, upcard);

            int key = PlayerKey(total, isSoft, numCards, isFromSplit, upcard);
            if (_playerCache.TryGetValue(key, out var cached)) return cached;

            double best = StandEV(total, upcard);

            double hitEV = HitEVInternal(total, isSoft, numCards, isFromSplit, upcard);
            if (hitEV > best) best = hitEV;

            if (numCards == 2 && (_rules.DoubleAfterSplit || !isFromSplit))
            {
                double doubled = DoubleEVInternal(total, isSoft, upcard);
                if (doubled > best) best = doubled;
            }

            _playerCache[key] = best;
            return best;
        }

        private double HitEVInternal(int total, bool isSoft, int numCards, bool isFromSplit, int upcard)
        {
            double sub = 1.0 / 13.0;
            double ev  = 0;
            for (int c = 1; c <= 13; c++)
            {
                var (nt, ns) = AddCard(total, isSoft, c);
                ev += sub * EvalHand(nt, ns, numCards + 1, isFromSplit, upcard);
            }
            return ev;
        }

        // Double down: one card then forced stand, with 2x stake.
        // Charlie cannot trigger (3 cards), BJ cannot trigger (>2 cards).
        private double DoubleEVInternal(int total, bool isSoft, int upcard)
        {
            double sub = 1.0 / 13.0;
            double ev  = 0;
            for (int c = 1; c <= 13; c++)
            {
                var (nt, _) = AddCard(total, isSoft, c);
                double outcomeEV;
                if (nt > 21) outcomeEV = -1.0;
                else         outcomeEV = StandEV(nt, upcard);
                ev += sub * outcomeEV;
            }
            return 2.0 * ev;
        }

        public double ActionEVForInitial(OptimalAction action, int c1, int c2, int upcard)
        {
            var (t1, soft1) = AddCard(0, false, c1);
            var (t,  soft)  = AddCard(t1, soft1, c2);
            return action switch
            {
                OptimalAction.Stand  => t == 21 ? BjEV(upcard) : StandEV(t, upcard),
                OptimalAction.Hit    => HitEVInternal(t, soft, 2, false, upcard),
                OptimalAction.Double => DoubleEVInternal(t, soft, upcard),
                OptimalAction.Split  => c1 == c2 ? EvalSplit(c1, upcard) : double.NaN,
                _                    => double.NaN,
            };
        }

        public OptimalAction OptimalActionForInitial(int c1, int c2, int upcard)
        {
            var (t1, soft1) = AddCard(0, false, c1);
            var (t,  soft)  = AddCard(t1, soft1, c2);

            // Natural BJ has no decision; treat as Stand for chart purposes.
            if (t == 21) return OptimalAction.Stand;

            double standEV  = StandEV(t, upcard);
            double hitEV    = HitEVInternal(t, soft, 2, false, upcard);
            double doubleEV = DoubleEVInternal(t, soft, upcard);

            var    best   = OptimalAction.Stand;
            double bestEV = standEV;
            if (hitEV > bestEV)    { best = OptimalAction.Hit;    bestEV = hitEV; }
            if (doubleEV > bestEV) { best = OptimalAction.Double; bestEV = doubleEV; }
            if (c1 == c2)
            {
                double splitEV = EvalSplit(c1, upcard);
                if (splitEV > bestEV) best = OptimalAction.Split;
            }
            return best;
        }

        // Split a pair of rank 'card'. In infinite deck the two post-split
        // hands are independent, so total EV = 2 * single-hand EV.
        //
        // For non-aces the engine allows unlimited re-splits, which would make
        // a naive recursion infinite. Solve the fixed-point analytically:
        //   S = H_neq + (1/13) * max(H_eq, 2S)
        // where H_neq = sum over c != card of (1/13)*play(card,c),
        //       H_eq  = play(card,card) without splitting.
        // Two consistent cases:
        //   no re-split: S = H_neq + H_eq/13
        //   re-split:    S = (13/11) * H_neq
        // Take the max.
        private double EvalSplit(int card, int upcard)
        {
            double sub = 1.0 / 13.0;

            if (card == 1)
            {
                // Split aces. The next dealt card is forced; HSA / RSA control
                // what happens after.
                //   HSA off, RSA off (default): each new hand gets one card and
                //     auto-stands. No re-split.
                //   HSA on,  RSA off: the post-deal hand is played normally via
                //     EvalHand (isFromSplit=true). 21 here is Stand, not BJ.
                //   HSA off, RSA on:  one card per hand, but a paired ace can be
                //     re-split. Apply the same fixed-point as non-ace splits to
                //     the play(ace,ace) branch.
                //   HSA on,  RSA on:  combine the two - re-split when paired
                //     aces, otherwise fall through to EvalHand.
                double PlayAceSecondCard(int c)
                {
                    var (t, soft) = AddCard(11, true, c);
                    if (_rules.HitSplitAces)
                    {
                        return t == 21
                            ? StandEV(21, upcard)
                            : EvalHand(t, soft, 2, true, upcard);
                    }
                    return StandEV(t, upcard);
                }

                if (!_rules.ResplitAces)
                {
                    double singleEV = 0;
                    for (int c = 1; c <= 13; c++)
                        singleEV += sub * PlayAceSecondCard(c);
                    return 2.0 * singleEV;
                }

                double hNeqA = 0;
                double hEqA  = PlayAceSecondCard(1);
                for (int c = 2; c <= 13; c++)
                    hNeqA += sub * PlayAceSecondCard(c);
                double aNoReSplit = hNeqA + hEqA / 13.0;
                double aReSplit   = (13.0 / 11.0) * hNeqA;
                double singleA    = Math.Max(aNoReSplit, aReSplit);
                return 2.0 * singleA;
            }

            int startVal = FaceValue(card);
            double hNeq = 0;
            double hEq  = 0;
            for (int c = 1; c <= 13; c++)
            {
                var (t, soft) = AddCard(startVal, false, c);
                double playEV;
                if (t == 21)
                {
                    // 21 from split auto-stands (not BJ).
                    playEV = StandEV(21, upcard);
                }
                else
                {
                    playEV = EvalHand(t, soft, 2, true, upcard);
                }
                if (c == card) hEq = playEV;
                else           hNeq += sub * playEV;
            }
            double sNoReSplit = hNeq + hEq / 13.0;
            double sReSplit   = (13.0 / 11.0) * hNeq;
            double single     = Math.Max(sNoReSplit, sReSplit);
            return 2.0 * single;
        }

        // ── Outcome EV given player terminal state ─────────────────────────────

        // Player has a non-busted, non-BJ, non-Charlie total. Engine: dealer BJ
        // beats anything that isn't player BJ or Charlie, regardless of total.
        private double StandEV(int playerTotal, int upcard)
        {
            var dist = DealerDist(upcard);
            double ev = 0;
            ev += dist[OBust] *  1.0;
            ev += dist[OBJ]   * -1.0;
            int[] totals  = { 17, 18, 19, 20, 21 };
            int[] buckets = { O17, O18, O19, O20, O21 };
            for (int i = 0; i < totals.Length; i++)
            {
                double p = dist[buckets[i]];
                if (playerTotal > totals[i])      ev += p *  1.0;
                else if (playerTotal < totals[i]) ev += p * -1.0;
            }
            return ev;
        }

        private double BjEV(int upcard)
        {
            var dist = DealerDist(upcard);
            double notBJ = 1.0 - dist[OBJ];
            return notBJ * _bjMul;
        }

        private double CharlieEV(int upcard)
        {
            if (_rules.FiveCardCharlie == FiveCardCharlieRule.BeatsAll)
                return _charlieMul;
            var dist = DealerDist(upcard);
            double bj = dist[OBJ];
            return (1.0 - bj) * _charlieMul + bj * -1.0;
        }

        // Pack the player memo key. total (5), isSoft (1), numCards 2..5 (3),
        // isFromSplit (1), upcard 1..13 (4).
        private static int PlayerKey(int total, bool isSoft, int numCards, bool isFromSplit, int upcard)
        {
            int n = numCards > 5 ? 5 : numCards;
            return total
                 | ((isSoft ? 1 : 0) << 5)
                 | (n << 6)
                 | ((isFromSplit ? 1 : 0) << 9)
                 | (upcard << 10);
        }
    }
}
