using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TwentyOne.Game;

public static class GameEngine
{
    // ── Card helpers (public for UI use) ──────────────────────────────────────

    public static string CardLabel(int card) => card switch
    {
        1  => "A",
        11 => "J",
        12 => "Q",
        13 => "K",
        _  => card.ToString()
    };

    // Face value contribution to hand total (aces count as 1 here; HandValue handles soft/hard).
    public static int CardValue(int card) => card >= 10 ? 10 : card;

    public static string HandString(IReadOnlyList<int> cards)
    {
        if (cards.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var c in cards)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(CardLabel(c));
        }
        return sb.ToString();
    }

    public static int HandValue(IReadOnlyList<int> cards)
    {
        var total = 0;
        var aces  = 0;
        foreach (var c in cards)
        {
            if      (c == 1)  { aces++; total += 11; }
            else if (c >= 10) total += 10;
            else              total += c;
        }
        while (total > 21 && aces > 0) { total -= 10; aces--; }
        return total;
    }

    public static bool IsSoft(IReadOnlyList<int> cards)
    {
        var low = 0;
        foreach (var c in cards) low += c == 1 ? 1 : c >= 10 ? 10 : c;
        return low != HandValue(cards);
    }

    public static string ScoreString(IReadOnlyList<int> cards)
    {
        if (cards.Count == 0) return string.Empty;
        var high = HandValue(cards);
        var low  = 0;
        foreach (var c in cards) low += c == 1 ? 1 : c >= 10 ? 10 : c;
        return (low != high && high <= 21) ? $"{low}/{high}" : high.ToString();
    }

    public static HandState ComputeHandState(IReadOnlyList<int> cards, HandState current, bool isFromSplit = false)
    {
        if (current == HandState.Stand) return HandState.Stand;
        var val = HandValue(cards);
        if (val > 21)                                          return HandState.Bust;
        if (!isFromSplit && cards.Count == 2 && val == 21)    return HandState.Blackjack;
        return HandState.Playing;
    }

    public static string DealerRecommendation(Hand hand)
    {
        if (hand.Cards.Count == 0) return string.Empty;
        var val = HandValue(hand.Cards);
        if (val > 21) return string.Empty;
        return (val < 17 || (val == 17 && IsSoft(hand.Cards))) ? "HIT" : "STAND";
    }

    public static bool CanGoToPayout(GameState state)
    {
        if (state.Phase != GamePhase.DealerTurn) return false;

        var allBJ = state.Players.Count > 0
                 && state.Players.All(p => p.Hands.All(h => h.State == HandState.Blackjack));
        if (allBJ)
        {
            var dc     = state.DealerHand.Cards;
            var upCard = dc.Count > 0 ? dc[0] : 0;
            var couldHaveBJ = upCard == 1 || upCard >= 10;
            return dc.Count >= 2 || !couldHaveBJ;
        }

        var allBust = state.Players.Count > 0
                   && state.Players.All(p => p.Hands.All(h => h.State == HandState.Bust));
        if (allBust) return true;

        var dc2 = state.DealerHand.Cards;
        return dc2.Count > 0
            && (HandValue(dc2) > 21 || DealerRecommendation(state.DealerHand) == "STAND");
    }

    // ── Action eligibility helpers (public for UI use) ────────────────────────

    // Returns the effective bet for a hand: hand.Bet if set, else player.Bet.
    public static decimal GetEffectiveBet(Player player, Hand hand) =>
        hand.Bet.Length > 0 ? ParseBet(hand.Bet) : ParseBet(player.Bet);

    // Double is allowed on any 2-card Playing hand that hasn't already been doubled,
    // provided the effective bet is numeric.
    public static bool CanDouble(Hand hand, string playerBet) =>
        hand.Cards.Count == 2 && hand.State == HandState.Playing && !hand.Doubled
        && (hand.Bet.Length > 0 ? ParseBet(hand.Bet) : ParseBet(playerBet)) > 0;

    // Split is allowed on any 2-card Playing hand where both cards share the same CardValue.
    // Re-splits (splitting a split hand) are supported.
    public static bool CanSplit(Hand hand) =>
        hand.Cards.Count == 2 && hand.State == HandState.Playing
        && CardValue(hand.Cards[0]) == CardValue(hand.Cards[1]);

    // Hit is allowed on a Playing hand that already has ≥2 cards (1-card split hands are auto-hit).
    public static bool CanHit(Hand hand) =>
        hand.State == HandState.Playing && hand.Cards.Count >= 2;

    // Deal phase is complete when the dealer has ≥1 card and every player's first hand has ≥2 cards.
    public static bool IsDealComplete(GameState state) =>
        state.DealerHand.Cards.Count >= 1
        && state.Players.Count > 0
        && state.Players.TrueForAll(p => p.Hands.Count > 0 && p.Hands[0].Cards.Count >= 2);

    // Dealer may receive a card during Deal (exactly 1 card; 0 so far) or during DealerTurn (must hit).
    public static bool CanHitDealer(GameState state)
    {
        if (state.Phase == GamePhase.Deal) return state.DealerHand.Cards.Count < 1;
        if (state.Phase != GamePhase.DealerTurn || state.WaitingForDealer) return false;
        var allBust = state.Players.Count > 0
                   && state.Players.All(p => p.Hands.All(h => h.State == HandState.Bust));
        return !allBust
            && DealerRecommendation(state.DealerHand) == "HIT"
            && HandValue(state.DealerHand.Cards) <= 21;
    }

    public static string ValidActionsString(Hand hand, bool canDouble, bool canSplit)
    {
        if (hand.State != HandState.Playing) return string.Empty;
        var sb = new StringBuilder("Hit or Stand");
        if (canDouble) sb.Append(", Double");
        if (canSplit)  sb.Append(", Split");
        return sb.ToString();
    }

    // ── Payout helpers (public for UI use) ────────────────────────────────────

    public static PayoutResult GetPayoutResult(GameState state, int playerIndex, int handIndex = 0)
    {
        var hand = state.Players[playerIndex].Hands[handIndex];
        if (hand.Cards.Count == 0)        return PayoutResult.None;
        if (hand.State == HandState.Bust) return PayoutResult.Lose;

        var dealerVal  = HandValue(state.DealerHand.Cards);
        var dealerBust = state.DealerHand.Cards.Count > 0 && dealerVal > 21;
        var dealerBJ   = state.DealerHand.Cards.Count == 2 && dealerVal == 21;
        var playerBJ   = hand.State == HandState.Blackjack;

        if (playerBJ && dealerBJ) return PayoutResult.Push;
        if (playerBJ)             return PayoutResult.BjWin;
        if (dealerBust)           return PayoutResult.Win;
        if (state.DealerHand.Cards.Count == 0) return PayoutResult.None;

        var pv = HandValue(hand.Cards);
        if (pv > dealerVal) return PayoutResult.Win;
        if (pv < dealerVal) return PayoutResult.Lose;
        return PayoutResult.Push;
    }

    public static decimal ParseBet(string bet) =>
        decimal.TryParse(bet.Trim(), out var v) && v > 0 ? v : 0;

    public static string FormatGil(decimal v)
    {
        var abs = Math.Abs(v);
        return abs >= 1_000_000 ? $"{v / 1_000_000:0.##}M"
             : abs >= 1_000     ? $"{v / 1_000:0.##}K"
             : $"{v:0.##}";
    }

    public static decimal? PayoutDelta(GameState state, int playerIndex, int handIndex = 0)
    {
        var player = state.Players[playerIndex];
        var hand   = player.Hands[handIndex];
        var bet    = GetEffectiveBet(player, hand);
        if (bet <= 0) return null;
        var result = GetPayoutResult(state, playerIndex, handIndex);
        var delta  = result switch
        {
            PayoutResult.Win   => bet,
            PayoutResult.BjWin => Math.Round(bet * BjMultiplier(state.BjPayout), 2),
            PayoutResult.Lose  => -bet,
            _                  => 0m,
        };
        return delta == 0 ? null : delta;
    }

    public static string PayoutAmountString(GameState state, int playerIndex, int handIndex = 0)
    {
        var delta = PayoutDelta(state, playerIndex, handIndex);
        if (delta == null) return string.Empty;
        return delta > 0 ? $"+{FormatGil(delta.Value)}" : FormatGil(delta.Value);
    }

    private static decimal BjMultiplier(BlackjackPayout payout) => payout switch
    {
        BlackjackPayout.SixToFive => 1.2m,
        BlackjackPayout.EvenMoney => 1.0m,
        _                         => 1.5m,
    };

    // ── Internal state builders ───────────────────────────────────────────────

    private static Hand AddCardToHand(Hand hand, int card)
    {
        var cards = new List<int>(hand.Cards) { card };
        return new Hand
        {
            Cards       = cards,
            State       = ComputeHandState(cards, hand.State, hand.IsFromSplit),
            Doubled     = hand.Doubled,
            Bet         = hand.Bet,
            IsFromSplit = hand.IsFromSplit,
        };
    }

    private static Hand SetHandState(Hand hand, HandState state) =>
        new Hand
        {
            Cards       = [..hand.Cards],
            State       = state,
            Doubled     = hand.Doubled,
            Bet         = hand.Bet,
            IsFromSplit = hand.IsFromSplit,
        };

    private static Player WithHand(Player player, int hi, Hand newHand) =>
        new Player
        {
            Nickname = player.Nickname,
            FullName = player.FullName,
            World    = player.World,
            Bet      = player.Bet,
            Hands    = player.Hands.Select((h, i) => i == hi ? newHand : h).ToList()
        };

    private static List<Player> WithPlayer(List<Player> players, int pi, Player newPlayer) =>
        players.Select((p, i) => i == pi ? newPlayer : p).ToList();

    private static GameState With(GameState s,
        List<Player>?       players               = null,
        Hand?               dealerHand            = null,
        GamePhase?          phase                 = null,
        int?                activePlayerIndex     = null,
        int?                activeHandIndex       = null,
        bool?               waitingForNextPlayer  = null,
        bool?               waitingForDealer      = null,
        BlackjackPayout?    bjPayout              = null,
        HashSet<string>?    lastRoundWinners      = null,
        HashSet<string>?    lastRoundPushers      = null) =>
        new GameState
        {
            Players                   = players              ?? s.Players,
            DealerHand                = dealerHand           ?? s.DealerHand,
            Phase                     = phase                ?? s.Phase,
            ActivePlayerIndex         = activePlayerIndex    ?? s.ActivePlayerIndex,
            ActiveHandIndex           = activeHandIndex      ?? s.ActiveHandIndex,
            WaitingForNextPlayer      = waitingForNextPlayer ?? s.WaitingForNextPlayer,
            WaitingForDealer          = waitingForDealer     ?? s.WaitingForDealer,
            BjPayout                  = bjPayout             ?? s.BjPayout,
            LastRoundWinners          = lastRoundWinners     ?? s.LastRoundWinners,
            LastRoundPushers          = lastRoundPushers     ?? s.LastRoundPushers,
            SkipDealSummaryOnePlayer  = s.SkipDealSummaryOnePlayer,
        };

    /// <summary>
    /// Advances to the next Playing hand after <paramref name="fromPi"/>/<paramref name="fromHi"/>.
    /// Pass fromPi=-1 to start from the very first hand.
    /// Returns the new active (player, hand) and phase; transitions to DealerTurn (or Payout
    /// if all hands busted) when no more Playing hands remain.
    /// </summary>
    private static (int Pi, int Hi, GamePhase Phase) AdvanceFrom(
        int fromPi, int fromHi, List<Player> players)
    {
        var startPi = fromPi < 0 ? 0 : fromPi;
        for (var pi = startPi; pi < players.Count; pi++)
        {
            var startHi = (pi == fromPi) ? fromHi + 1 : 0;
            for (var hi = startHi; hi < players[pi].Hands.Count; hi++)
            {
                var hs = players[pi].Hands[hi].State;
                if (hs == HandState.Playing || hs == HandState.Blackjack)
                    return (pi, hi, GamePhase.PlayerTurns);
            }
        }
        var allBust = players.All(p => p.Hands.All(h => h.State == HandState.Bust));
        return (-1, -1, allBust ? GamePhase.Payout : GamePhase.DealerTurn);
    }

    // ── Apply ─────────────────────────────────────────────────────────────────

    public static (GameState State, IReadOnlyList<SideEffect> Effects) Apply(
        GameState state, GameAction action, NarrationTemplates? templates = null, string dealerName = "Dealer")
    {
        var t       = templates ?? new NarrationTemplates();
        var effects = new List<SideEffect>();
        void Narrate(List<string> lines, params (string Key, string Value)[] vars)
        {
            foreach (var line in lines)
            {
                var resolved = vars.Length > 0 ? NarrationTemplates.Fmt(line, vars) : line;
                if (!string.IsNullOrWhiteSpace(resolved)) effects.Add(new SendChat(resolved));
            }
        }
        void NarrateStr(string text)
        {
            if (!string.IsNullOrWhiteSpace(text)) effects.Add(new SendChat(text));
        }

        void NarratePlayerTurn(int pi, int hi, List<Player> players, Hand dealerHand)
        {
            if (pi < 0 || pi >= players.Count) return;
            var player = players[pi];
            if (hi < 0 || hi >= player.Hands.Count) return;
            var hand = player.Hands[hi];
            if (hand.Cards.Count < 2) return; // 1-card split hand — wait for mandatory hit
            var cd = CanDouble(hand, player.Bet);
            var cs = CanSplit(hand);
            var actions = ValidActionsString(hand, cd, cs);
            var name = player.Hands.Count > 1 ? $"{player.DisplayName} (Hand {hi + 1})" : player.DisplayName;
            Narrate(t.PlayerTurnStart,
                ("name",        name),
                ("cards",       HandString(hand.Cards)),
                ("score",       ScoreString(hand.Cards)),
                ("dealerCards", HandString(dealerHand.Cards)),
                ("dealerScore", ScoreString(dealerHand.Cards)),
                ("actions",     actions));
        }

        void NarrateDealSummary(GameState s)
        {
            if (!(s.SkipDealSummaryOnePlayer && s.Players.Count == 1))
            {
                var sb = new StringBuilder(t.DealSummaryPrefix);
                for (var i = 0; i < s.Players.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var p    = s.Players[i];
                    var hand = p.Hands[0];
                    sb.Append(NarrationTemplates.Fmt(t.DealSummaryPlayer,
                        ("name",  p.DisplayName),
                        ("cards", HandString(hand.Cards)),
                        ("score", ScoreString(hand.Cards)),
                        ("bj",    hand.State == HandState.Blackjack ? " BJ!" : string.Empty)));
                }
                sb.Append(NarrationTemplates.Fmt(t.DealSummaryDealer,
                    ("dealer", dealerName), ("cards", HandString(s.DealerHand.Cards))));
                NarrateStr(sb.ToString());
            }
            // Announce natural blackjacks immediately after the deal summary, in player order.
            foreach (var p in s.Players)
            {
                var hand = p.Hands[0];
                if (hand.State == HandState.Blackjack)
                    Narrate(t.PlayerBJ, ("name", p.DisplayName), ("cards", HandString(hand.Cards)));
            }
        }

        switch (action)
        {
            // ── AddDealerCard ────────────────────────────────────────────────
            case AddDealerCard a:
            {
                var newHand = AddCardToHand(state.DealerHand, a.Card);
                if (state.Phase == GamePhase.DealerTurn)
                {
                    var cards   = HandString(newHand.Cards);
                    var score   = ScoreString(newHand.Cards);
                    var val     = HandValue(newHand.Cards);
                    var cardLbl = CardLabel(a.Card);
                    if (val > 21)
                        Narrate(t.DealerBust,
                            ("dealer", dealerName), ("card", cardLbl), ("cards", cards), ("score", score));
                    else if (newHand.Cards.Count == 2 && val == 21)
                        Narrate(t.DealerBJ,
                            ("dealer", dealerName), ("card", cardLbl), ("cards", cards));
                    else
                    {
                        Narrate(t.DealerHit,
                            ("dealer", dealerName), ("card", cardLbl), ("cards", cards), ("score", score));
                        if (DealerRecommendation(newHand) == "STAND")
                            Narrate(t.DealerStand,
                                ("dealer", dealerName), ("cards", cards), ("score", score));
                    }
                }
                var newStateD = With(state, dealerHand: newHand);
                if (state.Phase == GamePhase.Deal && IsDealComplete(newStateD))
                    NarrateDealSummary(newStateD);
                return (newStateD, effects);
            }

            // ── AddPlayerCard ────────────────────────────────────────────────
            case AddPlayerCard a:
            {
                var pi            = a.PlayerIndex;
                var hi            = a.HandIndex;
                var prevCardCount = state.Players[pi].Hands[hi].Cards.Count;
                var newHand       = AddCardToHand(state.Players[pi].Hands[hi], a.Card);

                // Forced stand: doubled hand gets exactly one card then stands.
                // Forced stand: split aces get exactly one card then stand (per standard rules).
                if (newHand.State == HandState.Playing)
                {
                    if (newHand.Doubled)
                        newHand = SetHandState(newHand, HandState.Stand);
                    else if (newHand.IsFromSplit && newHand.Cards.Count == 2
                             && newHand.Cards[0] == 1) // split ace
                        newHand = SetHandState(newHand, HandState.Stand);
                }

                var newPlayers = WithPlayer(state.Players, pi, WithHand(state.Players[pi], hi, newHand));
                var newPhase               = state.Phase;
                var newActivePi            = state.ActivePlayerIndex;
                var newActiveHi            = state.ActiveHandIndex;
                var newWaitingForNextPlayer = false;
                var newWaitingForDealer     = false;

                if (state.Phase == GamePhase.Deal)
                {
                    var newStateP = With(state, players: newPlayers);
                    if (IsDealComplete(newStateP))
                        NarrateDealSummary(newStateP);
                }
                else if (state.Phase == GamePhase.PlayerTurns)
                {
                    var multiHand   = state.Players[pi].Hands.Count > 1;
                    var displayName = multiHand
                        ? $"{state.Players[pi].DisplayName} (Hand {hi + 1})"
                        : state.Players[pi].DisplayName;
                    var cards   = HandString(newHand.Cards);
                    var score   = ScoreString(newHand.Cards);
                    var cardLbl = CardLabel(a.Card);

                    // Narrate the card
                    if (prevCardCount == 1)
                    {
                        // Mandatory 2nd card on a split hand. Only narrate if forced-stood (split ace).
                        if (newHand.State == HandState.Stand)
                            Narrate(t.PlayerSplitAce,
                                ("name", displayName), ("card", cardLbl), ("cards", cards), ("score", score));
                        // If still Playing, NarratePlayerTurn below serves as the announcement.
                    }
                    else if (newHand.State == HandState.Bust)
                        Narrate(t.PlayerBust,
                            ("name", displayName), ("cards", cards), ("score", score));
                    else if (newHand.State == HandState.Blackjack)
                        Narrate(t.PlayerBJ,
                            ("name", displayName), ("cards", cards));
                    else if (newHand.Doubled && newHand.State == HandState.Stand)
                        Narrate(t.PlayerDouble,
                            ("name", displayName), ("card", cardLbl), ("cards", cards), ("score", score));
                    else
                    {
                        Narrate(t.PlayerHit,
                            ("name", displayName), ("card", cardLbl), ("cards", cards), ("score", score));
                        if (newHand.State == HandState.Playing && pi == state.ActivePlayerIndex && hi == state.ActiveHandIndex)
                        {
                            var cd2 = CanDouble(newHand, state.Players[pi].Bet);
                            var cs2 = CanSplit(newHand);
                            Narrate(t.PlayerAfterHit,
                                ("name",    displayName),
                                ("cards",   cards),
                                ("score",   score),
                                ("actions", ValidActionsString(newHand, cd2, cs2)));
                        }
                    }

                    if (pi == state.ActivePlayerIndex && hi == state.ActiveHandIndex)
                    {
                        if (newHand.State != HandState.Playing)
                        {
                            var (peekPi, peekHi, peekPhase) = AdvanceFrom(pi, hi, newPlayers);
                            if (peekPhase is GamePhase.DealerTurn or GamePhase.Payout)
                            {
                                newPhase = GamePhase.DealerTurn;
                                var provisional = With(state, phase: GamePhase.DealerTurn, players: newPlayers);
                                newWaitingForDealer = !CanGoToPayout(provisional);
                            }
                            else if (peekPhase != GamePhase.PlayerTurns)
                            {
                                (newActivePi, newActiveHi, newPhase) = (peekPi, peekHi, peekPhase);
                            }
                            else
                            {
                                newWaitingForNextPlayer = true;
                            }
                        }
                        else if (prevCardCount == 1)
                        {
                            // Split hand now has 2 cards and is Playing — announce the turn.
                            NarratePlayerTurn(pi, hi, newPlayers, state.DealerHand);
                        }
                    }
                }

                return (With(state, players: newPlayers, phase: newPhase,
                    activePlayerIndex: newActivePi, activeHandIndex: newActiveHi,
                    waitingForNextPlayer: newWaitingForNextPlayer,
                    waitingForDealer: newWaitingForDealer), effects);
            }

            // ── StandPlayer ──────────────────────────────────────────────────
            case StandPlayer a:
            {
                var pi   = a.PlayerIndex;
                var hi   = a.HandIndex;
                var hand = state.Players[pi].Hands[hi];
                if (hand.State != HandState.Playing) return (state, effects);

                var newHand    = SetHandState(hand, HandState.Stand);
                var newPlayers = WithPlayer(state.Players, pi, WithHand(state.Players[pi], hi, newHand));
                var newPhase               = state.Phase;
                var newActivePi            = state.ActivePlayerIndex;
                var newActiveHi            = state.ActiveHandIndex;
                var newWaitingForNextPlayer = false;
                var newWaitingForDealer     = false;

                if (state.Phase == GamePhase.PlayerTurns)
                {
                    var multiHand   = state.Players[pi].Hands.Count > 1;
                    var displayName = multiHand
                        ? $"{state.Players[pi].DisplayName} (Hand {hi + 1})"
                        : state.Players[pi].DisplayName;
                    Narrate(t.PlayerStand,
                        ("name",  displayName),
                        ("cards", HandString(hand.Cards)),
                        ("score", HandValue(hand.Cards).ToString()));

                    if (pi == state.ActivePlayerIndex && hi == state.ActiveHandIndex)
                    {
                        var (peekPi, peekHi, peekPhase) = AdvanceFrom(pi, hi, newPlayers);
                        if (peekPhase == GamePhase.DealerTurn)
                        {
                            newPhase = GamePhase.DealerTurn;
                            newWaitingForDealer = true;
                        }
                        else if (peekPhase != GamePhase.PlayerTurns)
                        {
                            (newActivePi, newActiveHi, newPhase) = (peekPi, peekHi, peekPhase);
                        }
                        else
                        {
                            newWaitingForNextPlayer = true;
                        }
                    }
                }

                return (With(state, players: newPlayers, phase: newPhase,
                    activePlayerIndex: newActivePi, activeHandIndex: newActiveHi,
                    waitingForNextPlayer: newWaitingForNextPlayer,
                    waitingForDealer: newWaitingForDealer), effects);
            }

            // ── DoubleDown ───────────────────────────────────────────────────
            case DoubleDown a:
            {
                var pi     = a.PlayerIndex;
                var hi     = a.HandIndex;
                var player = state.Players[pi];
                var hand   = player.Hands[hi];
                var bet    = GetEffectiveBet(player, hand);
                var newBet = (bet * 2).ToString("0.##");
                var newHand = new Hand
                {
                    Cards       = [..hand.Cards],
                    State       = hand.State,
                    Doubled     = true,
                    Bet         = newBet,
                    IsFromSplit = hand.IsFromSplit,
                };
                var newPlayers = WithPlayer(state.Players, pi, WithHand(player, hi, newHand));
                return (With(state, players: newPlayers), effects);
            }

            // ── SplitHand ────────────────────────────────────────────────────
            case SplitHand a:
            {
                var pi     = a.PlayerIndex;
                var hi     = a.HandIndex;
                var player = state.Players[pi];
                var hand   = player.Hands[hi];
                var hand0  = new Hand { Cards = [hand.Cards[0]], State = HandState.Playing, IsFromSplit = true };
                var hand1  = new Hand { Cards = [hand.Cards[1]], State = HandState.Playing, IsFromSplit = true };
                var newHands = player.Hands.ToList();
                newHands[hi] = hand0;
                newHands.Insert(hi + 1, hand1);
                var newPlayer  = new Player { Nickname = player.Nickname, FullName = player.FullName, World = player.World, Bet = player.Bet, Hands = newHands };
                var newPlayers = WithPlayer(state.Players, pi, newPlayer);
                var name       = player.Hands.Count > 1 ? $"{player.DisplayName} (Hand {hi + 1})" : player.DisplayName;
                Narrate(t.PlayerSplit, ("name", name));
                var rollName   = $"{player.DisplayName} (Hand {hi + 1})";
                Narrate(t.PlayerSplitRoll, ("name", rollName));
                effects.Add(new AutoHit(pi, hi));
                return (With(state, players: newPlayers, activePlayerIndex: pi, activeHandIndex: hi), effects);
            }

            // ── AnnounceDealerHit / AnnouncePlayerHit ───────────────────────
            case AnnounceDealerHit:
            {
                var allBJ = state.Players.Count > 0
                         && state.Players.All(p => p.Hands.All(h => h.State == HandState.Blackjack));
                Narrate(allBJ ? t.DealerBJCheck : t.DealerHitAnnounce, ("dealer", dealerName));
                return (state, effects);
            }

            case AnnouncePlayerHit a:
            {
                var player = state.Players[a.PlayerIndex];
                var name   = player.Hands.Count > 1 ? $"{player.DisplayName} (Hand {a.HandIndex + 1})" : player.DisplayName;
                Narrate(t.PlayerHitAnnounce, ("name", name));
                return (state, effects);
            }

            case AnnouncePlayerTurn a:
            {
                NarratePlayerTurn(a.PlayerIndex, a.HandIndex, state.Players, state.DealerHand);
                return (state, effects);
            }

            // ── AnnounceDouble / AnnounceSplit ───────────────────────────────
            case AnnounceDouble a:
            {
                var player = state.Players[a.PlayerIndex];
                var hand   = player.Hands[a.HandIndex];
                var bet    = GetEffectiveBet(player, hand);
                var name   = player.Hands.Count > 1 ? $"{player.DisplayName} (Hand {a.HandIndex + 1})" : player.DisplayName;
                Narrate(t.PlayerDoubleRequest, ("name", name), ("amount", FormatGil(bet)));
                return (state, effects);
            }

            case AnnounceDoubleConfirm a:
            {
                var player = state.Players[a.PlayerIndex];
                var name   = player.Hands.Count > 1 ? $"{player.DisplayName} (Hand {a.HandIndex + 1})" : player.DisplayName;
                Narrate(t.PlayerDoubleConfirm, ("name", name));
                return (state, effects);
            }

            case AnnounceSplit a:
            {
                var player = state.Players[a.PlayerIndex];
                var hand   = player.Hands[a.HandIndex];
                var bet    = GetEffectiveBet(player, hand);
                var name   = player.Hands.Count > 1 ? $"{player.DisplayName} (Hand {a.HandIndex + 1})" : player.DisplayName;
                Narrate(t.PlayerSplitRequest, ("name", name), ("amount", FormatGil(bet)));
                return (state, effects);
            }

            // ── AnnounceBettingOpen ──────────────────────────────────────────
            case AnnounceBettingOpen:
                Narrate(t.BettingOpen);
                return (state, effects);

            // ── AnnounceBetRequest ───────────────────────────────────────────
            case AnnounceBetRequest a:
            {
                var player = state.Players[a.PlayerIndex];
                Narrate(t.PlayerBetRequest, ("name", player.DisplayName));
                return (state, effects);
            }

            // ── AnnounceBetConfirm ───────────────────────────────────────────
            case AnnounceBetConfirm a:
            {
                var player = state.Players[a.PlayerIndex];
                Narrate(t.PlayerBetConfirm, ("name", player.DisplayName), ("amount", FormatGil(ParseBet(player.Bet))));
                return (state, effects);
            }

            // ── AnnounceDealerDeal / AnnouncePlayerDeal ──────────────────────
            case AnnounceDealerDeal:
                Narrate(t.DealDealerCard, ("dealer", dealerName));
                return (state, effects);

            case AnnouncePlayerDeal a:
                Narrate(t.DealPlayerHand, ("name", state.Players[a.PlayerIndex].DisplayName));
                return (state, effects);

            // ── StartDeal ────────────────────────────────────────────────────
            case StartDeal:
                return (With(state, phase: GamePhase.Deal), effects);

            // ── BeginPlayerTurns ─────────────────────────────────────────────
            case BeginPlayerTurns:
            {
                var (nextPi, nextHi, nextPhase) = AdvanceFrom(-1, -1, state.Players);
                var provisionalDealer = With(state, phase: GamePhase.DealerTurn);
                var waitDealer = nextPhase == GamePhase.DealerTurn && !CanGoToPayout(provisionalDealer);
                var waitNext   = false;
                if (nextPhase == GamePhase.PlayerTurns)
                {
                    var nextHand = state.Players[nextPi].Hands[nextHi];
                    if (nextHand.State == HandState.Blackjack)
                    {
                        var name = state.Players[nextPi].DisplayName;
                        if (state.Players.Count > 1)
                            Narrate(t.PlayerBJMovingAlong, ("name", name), ("cards", HandString(nextHand.Cards)));
                        waitNext = true;
                    }
                    else
                        NarratePlayerTurn(nextPi, nextHi, state.Players, state.DealerHand);
                }
                if (waitDealer) nextPhase = GamePhase.DealerTurn;
                return (With(state, phase: nextPhase, activePlayerIndex: nextPi, activeHandIndex: nextHi,
                    waitingForDealer: waitDealer, waitingForNextPlayer: waitNext), effects);
            }

            // ── AdvanceToNextPlayer ──────────────────────────────────────────
            case AdvanceToNextPlayer:
            {
                if (!state.WaitingForNextPlayer) return (state, effects);
                var (nextPi, nextHi, nextPhase) = AdvanceFrom(
                    state.ActivePlayerIndex, state.ActiveHandIndex, state.Players);
                if (nextPhase == GamePhase.PlayerTurns)
                {
                    var nextHand = state.Players[nextPi].Hands[nextHi];
                    if (nextHand.Cards.Count == 1)
                    {
                        var advPlayer  = state.Players[nextPi];
                        var advName    = $"{advPlayer.DisplayName} (Hand {nextHi + 1})";
                        Narrate(t.PlayerSplitRoll, ("name", advName));
                        effects.Add(new AutoHit(nextPi, nextHi));
                    }
                    else if (nextHand.State == HandState.Blackjack)
                    {
                        var name = state.Players[nextPi].Hands.Count > 1
                            ? $"{state.Players[nextPi].DisplayName} (Hand {nextHi + 1})"
                            : state.Players[nextPi].DisplayName;
                        if (state.Players.Count > 1)
                            Narrate(t.PlayerBJMovingAlong, ("name", name), ("cards", HandString(nextHand.Cards)));
                        return (With(state, phase: nextPhase, activePlayerIndex: nextPi, activeHandIndex: nextHi,
                            waitingForNextPlayer: true), effects);
                    }
                    else
                        NarratePlayerTurn(nextPi, nextHi, state.Players, state.DealerHand);
                }
                else if (nextPhase == GamePhase.DealerTurn)
                {
                    var provisional = With(state, phase: GamePhase.DealerTurn);
                    var needWait    = !CanGoToPayout(provisional);
                    return (With(state, phase: nextPhase, activePlayerIndex: nextPi, activeHandIndex: nextHi,
                        waitingForNextPlayer: false, waitingForDealer: needWait), effects);
                }
                return (With(state, phase: nextPhase, activePlayerIndex: nextPi, activeHandIndex: nextHi,
                    waitingForNextPlayer: false), effects);
            }

            // ── BeginDealerTurn ──────────────────────────────────────────────
            case BeginDealerTurn:
            {
                if (!state.WaitingForDealer) return (state, effects);
                Narrate(t.DealerTurnStart,
                    ("dealer", dealerName),
                    ("cards", HandString(state.DealerHand.Cards)),
                    ("score", ScoreString(state.DealerHand.Cards)));
                return (With(state, waitingForDealer: false), effects);
            }

            // ── GoToPayout ───────────────────────────────────────────────────
            case GoToPayout:
            {
                Narrate(t.PayoutHeader);
                var dealerScore = HandValue(state.DealerHand.Cards);
                var dealerBust  = state.DealerHand.Cards.Count > 0 && dealerScore > 21;
                Narrate(dealerBust ? t.PayoutDealerBust : t.PayoutDealerStands,
                    ("dealer", dealerName), ("score", dealerBust ? dealerScore.ToString() : ScoreString(state.DealerHand.Cards)));

                for (var pi = 0; pi < state.Players.Count; pi++)
                {
                    var p         = state.Players[pi];
                    var multiHand = p.Hands.Count > 1;

                    // For split hands where every hand wins, emit one combined line.
                    var allWin = multiHand && p.Hands
                        .Select((_, hi) => GetPayoutResult(state, pi, hi))
                        .All(r => r == PayoutResult.Win || r == PayoutResult.BjWin);
                    if (allWin)
                    {
                        var total = 0m;
                        for (var hi = 0; hi < p.Hands.Count; hi++)
                        {
                            var eb = GetEffectiveBet(p, p.Hands[hi]);
                            total += GetPayoutResult(state, pi, hi) == PayoutResult.BjWin
                                ? Math.Round(eb * BjMultiplier(state.BjPayout), 2)
                                : eb;
                        }
                        var amtStr = total > 0 ? $" +{total:0.##}" : string.Empty;
                        Narrate(t.PayoutSplitCombined,
                            ("name",   p.DisplayName),
                            ("amount", amtStr));
                        continue;
                    }

                    for (var hi = 0; hi < p.Hands.Count; hi++)
                    {
                        var result = GetPayoutResult(state, pi, hi);
                        var template = result switch
                        {
                            PayoutResult.Win   => t.PayoutWin,
                            PayoutResult.BjWin => t.PayoutBjWin,
                            PayoutResult.Lose  => t.PayoutLose,
                            PayoutResult.Push  => t.PayoutPush,
                            _                  => null,
                        };
                        if (template == null) continue;

                        var effectiveBet = GetEffectiveBet(p, p.Hands[hi]);
                        var amount       = PayoutAmountString(state, pi, hi);
                        var betStr       = effectiveBet > 0
                            ? $" (bet: {FormatGil(effectiveBet)})"
                            : string.Empty;
                        var amountStr    = amount.Length > 0 ? $" {amount}" : string.Empty;
                        var name         = multiHand ? $"{p.DisplayName} (Hand {hi + 1})" : p.DisplayName;
                        Narrate(template,
                            ("name",   name),
                            ("bet",    betStr),
                            ("amount", amountStr));
                    }
                }

                var winners = new HashSet<string>(
                    state.Players
                         .Where((p, pi) => p.Hands.Select((_, hi) => GetPayoutResult(state, pi, hi))
                                            .Any(r => r is PayoutResult.Win or PayoutResult.BjWin))
                         .Select(p => p.FullName.Length > 0 ? p.FullName : p.Nickname));
                var pushers = new HashSet<string>(
                    state.Players
                         .Where((p, pi) => p.Hands.Select((_, hi) => GetPayoutResult(state, pi, hi))
                                            .Any(r => r == PayoutResult.Push)
                                        && !p.Hands.Select((_, hi) => GetPayoutResult(state, pi, hi))
                                            .Any(r => r is PayoutResult.Win or PayoutResult.BjWin))
                         .Select(p => p.FullName.Length > 0 ? p.FullName : p.Nickname));
                return (With(state, phase: GamePhase.Payout, lastRoundWinners: winners, lastRoundPushers: pushers), effects);
            }

            // ── NewRound ─────────────────────────────────────────────────────
            case NewRound:
                return (new GameState
                {
                    Players = state.Players.Select(p => new Player
                    {
                        Nickname = p.Nickname,
                        FullName = p.FullName,
                        World    = p.World,
                        Bet      = p.Bet,
                        Hands    = [new Hand()],
                    }).ToList(),
                    DealerHand        = new Hand(),
                    Phase             = GamePhase.Betting,
                    ActivePlayerIndex = -1,
                    ActiveHandIndex   = -1,
                    BjPayout                 = state.BjPayout,
                    LastRoundWinners         = state.LastRoundWinners,
                    LastRoundPushers         = state.LastRoundPushers,
                    SkipDealSummaryOnePlayer = state.SkipDealSummaryOnePlayer,
                }, effects);

            // ── Roster management ────────────────────────────────────────────
            case AddPlayer a:
                return (With(state, players:
                    [..state.Players, new Player { Nickname = a.Nickname, FullName = a.FullName, World = a.World, Hands = [new Hand()] }]), effects);

            case RemovePlayer a:
            {
                var newPlayers = state.Players.Where((_, i) => i != a.Index).ToList();
                var newActive  = state.ActivePlayerIndex >= newPlayers.Count
                                     ? newPlayers.Count - 1
                                     : state.ActivePlayerIndex;
                return (With(state, players: newPlayers, activePlayerIndex: newActive), effects);
            }

            case SetPlayerBet a:
            {
                var p = state.Players[a.PlayerIndex];
                return (With(state, players: WithPlayer(state.Players, a.PlayerIndex,
                    new Player { Nickname = p.Nickname, FullName = p.FullName, World = p.World, Bet = a.Bet, Hands = p.Hands })), effects);
            }

            case RenamePlayer a:
            {
                var p = state.Players[a.PlayerIndex];
                return (With(state, players: WithPlayer(state.Players, a.PlayerIndex,
                    new Player { Nickname = a.Nickname, FullName = p.FullName, World = p.World, Bet = p.Bet, Hands = p.Hands })), effects);
            }

            default:
                return (state, effects);
        }
    }
}
