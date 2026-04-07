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
    public static bool CanHitDealer(GameState state) =>
        (state.Phase == GamePhase.Deal && state.DealerHand.Cards.Count < 1)
        || (state.Phase == GamePhase.DealerTurn
            && DealerRecommendation(state.DealerHand) == "HIT"
            && GameEngine.HandValue(state.DealerHand.Cards) <= 21);

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

    public static string PayoutAmountString(GameState state, int playerIndex, int handIndex = 0)
    {
        var player = state.Players[playerIndex];
        var hand   = player.Hands[handIndex];
        var bet    = GetEffectiveBet(player, hand);
        if (bet <= 0) return string.Empty;
        var result = GetPayoutResult(state, playerIndex, handIndex);
        var delta  = result switch
        {
            PayoutResult.Win   => bet,
            PayoutResult.BjWin => Math.Round(bet * BjMultiplier(state.BjPayout), 2),
            PayoutResult.Lose  => -bet,
            _                  => 0m,
        };
        if (delta == 0) return string.Empty;
        return delta > 0 ? $"+{delta:0.##}" : $"{delta:0.##}";
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
        List<Player>?    players               = null,
        Hand?            dealerHand            = null,
        GamePhase?       phase                 = null,
        int?             activePlayerIndex     = null,
        int?             activeHandIndex       = null,
        bool?            waitingForNextPlayer  = null,
        BlackjackPayout? bjPayout              = null) =>
        new GameState
        {
            Players              = players           ?? s.Players,
            DealerHand           = dealerHand        ?? s.DealerHand,
            Phase                = phase             ?? s.Phase,
            ActivePlayerIndex    = activePlayerIndex ?? s.ActivePlayerIndex,
            ActiveHandIndex      = activeHandIndex   ?? s.ActiveHandIndex,
            WaitingForNextPlayer = waitingForNextPlayer ?? s.WaitingForNextPlayer,
            BjPayout             = bjPayout          ?? s.BjPayout,
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
                if (players[pi].Hands[hi].State == HandState.Playing)
                    return (pi, hi, GamePhase.PlayerTurns);
            }
        }
        var allBust = players.All(p => p.Hands.All(h => h.State == HandState.Bust));
        return (-1, -1, allBust ? GamePhase.Payout : GamePhase.DealerTurn);
    }

    // ── Apply ─────────────────────────────────────────────────────────────────

    public static (GameState State, IReadOnlyList<SideEffect> Effects) Apply(
        GameState state, GameAction action, NarrationTemplates? templates = null)
    {
        var t       = templates ?? new NarrationTemplates();
        var effects = new List<SideEffect>();
        void Narrate(string text)
        {
            foreach (var part in text.Split("{|}"))
                if (!string.IsNullOrWhiteSpace(part)) effects.Add(new SendChat(part.Trim()));
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
            Narrate(NarrationTemplates.Fmt(t.PlayerTurnStart,
                ("name",        name),
                ("score",       ScoreString(hand.Cards)),
                ("dealerCards", HandString(dealerHand.Cards)),
                ("dealerScore", ScoreString(dealerHand.Cards)),
                ("actions",     actions)));
        }

        void NarrateDealSummary(GameState s)
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
                ("cards", HandString(s.DealerHand.Cards))));
            Narrate(sb.ToString());
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
                        Narrate(NarrationTemplates.Fmt(t.DealerBust,
                            ("card", cardLbl), ("cards", cards), ("score", score)));
                    else if (newHand.Cards.Count == 2 && val == 21)
                        Narrate(NarrationTemplates.Fmt(t.DealerBJ,
                            ("card", cardLbl), ("cards", cards)));
                    else
                        Narrate(NarrationTemplates.Fmt(t.DealerHit,
                            ("card", cardLbl), ("cards", cards), ("score", score)));
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
                var newPhase   = state.Phase;
                var newActivePi            = state.ActivePlayerIndex;
                var newActiveHi            = state.ActiveHandIndex;
                var newWaitingForNextPlayer = false;

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
                            Narrate(NarrationTemplates.Fmt(t.PlayerSplitAce,
                                ("name", displayName), ("card", cardLbl), ("cards", cards), ("score", score)));
                        // If still Playing, NarratePlayerTurn below serves as the announcement.
                    }
                    else if (newHand.State == HandState.Bust)
                        Narrate(NarrationTemplates.Fmt(t.PlayerBust,
                            ("name", displayName), ("cards", cards), ("score", score)));
                    else if (newHand.State == HandState.Blackjack)
                        Narrate(NarrationTemplates.Fmt(t.PlayerBJ,
                            ("name", displayName), ("cards", cards)));
                    else if (newHand.Doubled && newHand.State == HandState.Stand)
                        Narrate(NarrationTemplates.Fmt(t.PlayerDouble,
                            ("name", displayName), ("card", cardLbl), ("cards", cards), ("score", score)));
                    else
                    {
                        Narrate(NarrationTemplates.Fmt(t.PlayerHit,
                            ("name", displayName), ("card", cardLbl), ("cards", cards), ("score", score)));
                        if (newHand.State == HandState.Playing && pi == state.ActivePlayerIndex && hi == state.ActiveHandIndex)
                        {
                            var cd2 = CanDouble(newHand, state.Players[pi].Bet);
                            var cs2 = CanSplit(newHand);
                            Narrate(NarrationTemplates.Fmt(t.PlayerAfterHit,
                                ("name",    displayName),
                                ("cards",   cards),
                                ("score",   score),
                                ("actions", ValidActionsString(newHand, cd2, cs2))));
                        }
                    }

                    if (pi == state.ActivePlayerIndex && hi == state.ActiveHandIndex)
                    {
                        if (newHand.State != HandState.Playing)
                        {
                            var (peekPi, peekHi, peekPhase) = AdvanceFrom(pi, hi, newPlayers);
                            if (peekPhase == GamePhase.PlayerTurns
                                && newPlayers[peekPi].Hands[peekHi].Cards.Count == 1)
                            {
                                // Auto-hit split hand — advance immediately, no button needed.
                                (newActivePi, newActiveHi, newPhase) = (peekPi, peekHi, peekPhase);
                                effects.Add(new AutoHit(newActivePi, newActiveHi));
                            }
                            else if (peekPhase != GamePhase.PlayerTurns)
                            {
                                // No more players — go straight to DealerTurn/Payout.
                                (newActivePi, newActiveHi, newPhase) = (peekPi, peekHi, peekPhase);
                            }
                            else
                            {
                                // Another player waiting — pause for button press.
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
                    waitingForNextPlayer: newWaitingForNextPlayer), effects);
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
                var newPhase              = state.Phase;
                var newActivePi            = state.ActivePlayerIndex;
                var newActiveHi            = state.ActiveHandIndex;
                var newWaitingForNextPlayer = false;

                if (state.Phase == GamePhase.PlayerTurns)
                {
                    var multiHand   = state.Players[pi].Hands.Count > 1;
                    var displayName = multiHand
                        ? $"{state.Players[pi].DisplayName} (Hand {hi + 1})"
                        : state.Players[pi].DisplayName;
                    Narrate(NarrationTemplates.Fmt(t.PlayerStand,
                        ("name",  displayName),
                        ("cards", HandString(hand.Cards)),
                        ("score", ScoreString(hand.Cards))));

                    if (pi == state.ActivePlayerIndex && hi == state.ActiveHandIndex)
                    {
                        var (peekPi, peekHi, peekPhase) = AdvanceFrom(pi, hi, newPlayers);
                        if (peekPhase == GamePhase.PlayerTurns
                            && newPlayers[peekPi].Hands[peekHi].Cards.Count == 1)
                        {
                            (newActivePi, newActiveHi, newPhase) = (peekPi, peekHi, peekPhase);
                            effects.Add(new AutoHit(newActivePi, newActiveHi));
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
                    waitingForNextPlayer: newWaitingForNextPlayer), effects);
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
                Narrate(NarrationTemplates.Fmt(t.PlayerSplit, ("name", name)));
                effects.Add(new AutoHit(pi, hi));
                return (With(state, players: newPlayers, activePlayerIndex: pi, activeHandIndex: hi), effects);
            }

            // ── AnnounceDealerHit / AnnouncePlayerHit ───────────────────────
            case AnnounceDealerHit:
                Narrate(t.DealerHitAnnounce);
                return (state, effects);

            case AnnouncePlayerHit a:
            {
                var player = state.Players[a.PlayerIndex];
                var name   = player.Hands.Count > 1 ? $"{player.DisplayName} (Hand {a.HandIndex + 1})" : player.DisplayName;
                Narrate(NarrationTemplates.Fmt(t.PlayerHitAnnounce, ("name", name)));
                return (state, effects);
            }

            // ── AnnounceDouble / AnnounceSplit ───────────────────────────────
            case AnnounceDouble a:
            {
                var player = state.Players[a.PlayerIndex];
                var hand   = player.Hands[a.HandIndex];
                var bet    = GetEffectiveBet(player, hand);
                var name   = player.Hands.Count > 1 ? $"{player.DisplayName} (Hand {a.HandIndex + 1})" : player.DisplayName;
                Narrate(NarrationTemplates.Fmt(t.PlayerDoubleRequest,
                    ("name", name), ("amount", bet.ToString("0.##"))));
                return (state, effects);
            }

            case AnnounceSplit a:
            {
                var player = state.Players[a.PlayerIndex];
                var hand   = player.Hands[a.HandIndex];
                var bet    = GetEffectiveBet(player, hand);
                var name   = player.Hands.Count > 1 ? $"{player.DisplayName} (Hand {a.HandIndex + 1})" : player.DisplayName;
                Narrate(NarrationTemplates.Fmt(t.PlayerSplitRequest,
                    ("name", name), ("amount", bet.ToString("0.##"))));
                return (state, effects);
            }

            // ── AnnounceBettingOpen ──────────────────────────────────────────
            case AnnounceBettingOpen:
                Narrate(t.BettingOpen);
                return (state, effects);

            // ── AnnounceDealerDeal / AnnouncePlayerDeal ──────────────────────
            case AnnounceDealerDeal:
                Narrate(t.DealDealerCard);
                return (state, effects);

            case AnnouncePlayerDeal a:
                Narrate(NarrationTemplates.Fmt(t.DealPlayerHand, ("name", state.Players[a.PlayerIndex].DisplayName)));
                return (state, effects);

            // ── StartDeal ────────────────────────────────────────────────────
            case StartDeal:
                return (With(state, phase: GamePhase.Deal), effects);

            // ── BeginPlayerTurns ─────────────────────────────────────────────
            case BeginPlayerTurns:
            {
                var (nextPi, nextHi, nextPhase) = AdvanceFrom(-1, -1, state.Players);
                if (nextPhase == GamePhase.PlayerTurns)
                    NarratePlayerTurn(nextPi, nextHi, state.Players, state.DealerHand);
                return (With(state, phase: nextPhase, activePlayerIndex: nextPi, activeHandIndex: nextHi), effects);
            }

            // ── AdvanceToNextPlayer ──────────────────────────────────────────
            case AdvanceToNextPlayer:
            {
                if (!state.WaitingForNextPlayer) return (state, effects);
                var (nextPi, nextHi, nextPhase) = AdvanceFrom(
                    state.ActivePlayerIndex, state.ActiveHandIndex, state.Players);
                if (nextPhase == GamePhase.PlayerTurns)
                    NarratePlayerTurn(nextPi, nextHi, state.Players, state.DealerHand);
                return (With(state, phase: nextPhase, activePlayerIndex: nextPi, activeHandIndex: nextHi,
                    waitingForNextPlayer: false), effects);
            }

            // ── GoToPayout ───────────────────────────────────────────────────
            case GoToPayout:
            {
                var dealerScore = HandValue(state.DealerHand.Cards);
                var dealerBust  = state.DealerHand.Cards.Count > 0 && dealerScore > 21;
                Narrate(dealerBust
                    ? NarrationTemplates.Fmt(t.PayoutDealerBust,
                        ("score", dealerScore.ToString()))
                    : NarrationTemplates.Fmt(t.PayoutDealerStands,
                        ("score", ScoreString(state.DealerHand.Cards))));

                for (var pi = 0; pi < state.Players.Count; pi++)
                {
                    var p         = state.Players[pi];
                    var multiHand = p.Hands.Count > 1;
                    for (var hi = 0; hi < p.Hands.Count; hi++)
                    {
                        var result = GetPayoutResult(state, pi, hi);
                        var label  = result switch
                        {
                            PayoutResult.Win   => "Win",
                            PayoutResult.BjWin => "BJ Win",
                            PayoutResult.Lose  => "Lose",
                            PayoutResult.Push  => "Push",
                            _                  => string.Empty,
                        };
                        if (label.Length == 0) continue;

                        var effectiveBet = GetEffectiveBet(p, p.Hands[hi]);
                        var amount       = PayoutAmountString(state, pi, hi);
                        var betStr       = effectiveBet > 0
                            ? $" (bet: {effectiveBet:0.##})"
                            : string.Empty;
                        var amountStr    = amount.Length > 0 ? $" {amount}" : string.Empty;
                        var name         = multiHand ? $"{p.DisplayName} (Hand {hi + 1})" : p.DisplayName;
                        Narrate(NarrationTemplates.Fmt(t.PayoutPlayer,
                            ("name",   name),
                            ("result", label),
                            ("bet",    betStr),
                            ("amount", amountStr)));
                    }
                }

                return (With(state, phase: GamePhase.Payout), effects);
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
                    BjPayout          = state.BjPayout,
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
