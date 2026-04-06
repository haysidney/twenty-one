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

    public static HandState ComputeHandState(IReadOnlyList<int> cards, HandState current)
    {
        if (current == HandState.Stand) return HandState.Stand;
        var val = HandValue(cards);
        if (val > 21)                          return HandState.Bust;
        if (cards.Count == 2 && val == 21)     return HandState.Blackjack;
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
                 && state.Players.All(p => p.Hands[0].State == HandState.Blackjack);
        if (allBJ)
        {
            var dc    = state.DealerHand.Cards;
            var upCard = dc.Count > 0 ? dc[0] : 0;
            // Dealer can only have BJ if up-card is an ace or ten-value.
            var couldHaveBJ = upCard == 1 || upCard >= 10;
            return dc.Count >= 2 || !couldHaveBJ;
        }

        return DealerRecommendation(state.DealerHand) == "STAND"
            && state.DealerHand.Cards.Count > 0;
    }

    // ── Payout helpers (public for UI use) ────────────────────────────────────

    public static PayoutResult GetPayoutResult(GameState state, int playerIndex)
    {
        var hand = state.Players[playerIndex].Hands[0];
        if (hand.Cards.Count == 0)             return PayoutResult.None;
        if (hand.State == HandState.Bust)      return PayoutResult.Lose;

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

    public static string PayoutAmountString(GameState state, int playerIndex)
    {
        var bet = ParseBet(state.Players[playerIndex].Bet);
        if (bet <= 0) return string.Empty;
        var result = GetPayoutResult(state, playerIndex);
        var delta = result switch
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
        return new Hand { Cards = cards, State = ComputeHandState(cards, hand.State) };
    }

    private static Hand SetHandState(Hand hand, HandState state) =>
        new Hand { Cards = [..hand.Cards], State = state };

    private static Player WithHand(Player player, int hi, Hand newHand) =>
        new Player
        {
            Name  = player.Name,
            Bet   = player.Bet,
            Hands = player.Hands.Select((h, i) => i == hi ? newHand : h).ToList()
        };

    private static List<Player> WithPlayer(List<Player> players, int pi, Player newPlayer) =>
        players.Select((p, i) => i == pi ? newPlayer : p).ToList();

    private static GameState With(GameState s,
        List<Player>? players           = null,
        Hand?         dealerHand        = null,
        GamePhase?    phase             = null,
        int?          activePlayerIndex = null,
        BlackjackPayout? bjPayout       = null) =>
        new GameState
        {
            Players           = players           ?? s.Players,
            DealerHand        = dealerHand        ?? s.DealerHand,
            Phase             = phase             ?? s.Phase,
            ActivePlayerIndex = activePlayerIndex ?? s.ActivePlayerIndex,
            BjPayout          = bjPayout          ?? s.BjPayout,
        };

    public static string ValidActionsString(Hand hand)
    {
        if (hand.State != HandState.Playing) return string.Empty;
        // Future: append ", Split" or ", Double" when those actions are supported.
        return "Hit or Stand";
    }

    /// <summary>
    /// Searches for the next Playing hand starting after <paramref name="fromIndex"/>.
    /// Returns the new active index and phase (transitions to DealerTurn if none found).
    /// </summary>
    private static (int ActiveIndex, GamePhase Phase) AdvanceFrom(int fromIndex, List<Player> players)
    {
        for (var i = fromIndex + 1; i < players.Count; i++)
        {
            if (players[i].Hands[0].State == HandState.Playing)
                return (i, GamePhase.PlayerTurns);
        }
        // If every player busted, dealer has nothing to beat — skip to payout.
        var allBust = players.All(p => p.Hands[0].State == HandState.Bust);
        return (-1, allBust ? GamePhase.Payout : GamePhase.DealerTurn);
    }

    // ── Apply ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pure state transition: takes the current state and an action, returns
    /// the new state and any side effects (narration / chat messages).
    /// Never mutates <paramref name="state"/>.
    /// </summary>
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
        void NarratePlayerTurn(int pi, List<Player> players, Hand dealerHand)
        {
            if (pi < 0 || pi >= players.Count) return;
            var hand = players[pi].Hands[0];
            var actions = ValidActionsString(hand);
            Narrate(NarrationTemplates.Fmt(t.PlayerTurnStart,
                ("name",        players[pi].Name),
                ("dealerCards", HandString(dealerHand.Cards)),
                ("dealerScore", ScoreString(dealerHand.Cards)),
                ("actions",     actions)));
        }

        switch (action)
        {
            // ── AddDealerCard ────────────────────────────────────────────────
            case AddDealerCard a:
            {
                var newHand = AddCardToHand(state.DealerHand, a.Card);
                if (state.Phase == GamePhase.DealerTurn)
                {
                    var cards    = HandString(newHand.Cards);
                    var score    = ScoreString(newHand.Cards);
                    var val      = HandValue(newHand.Cards);
                    var cardLbl  = CardLabel(a.Card);
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
                return (With(state, dealerHand: newHand), effects);
            }

            // ── AddPlayerCard ────────────────────────────────────────────────
            case AddPlayerCard a:
            {
                var pi      = a.PlayerIndex;
                var hi      = a.HandIndex;
                var newHand = AddCardToHand(state.Players[pi].Hands[hi], a.Card);
                var newPlayers = WithPlayer(state.Players, pi, WithHand(state.Players[pi], hi, newHand));

                var newPhase  = state.Phase;
                var newActive = state.ActivePlayerIndex;

                if (state.Phase == GamePhase.PlayerTurns)
                {
                    var name    = state.Players[pi].Name;
                    var cards   = HandString(newHand.Cards);
                    var score   = ScoreString(newHand.Cards);
                    var cardLbl = CardLabel(a.Card);
                    switch (newHand.State)
                    {
                        case HandState.Bust:
                            Narrate(NarrationTemplates.Fmt(t.PlayerBust,
                                ("name", name), ("cards", cards), ("score", score)));
                            break;
                        case HandState.Blackjack:
                            Narrate(NarrationTemplates.Fmt(t.PlayerBJ,
                                ("name", name), ("cards", cards)));
                            break;
                        default:
                            Narrate(NarrationTemplates.Fmt(t.PlayerHit,
                                ("name", name), ("card", cardLbl), ("cards", cards), ("score", score)));
                            break;
                    }

                    if (pi == state.ActivePlayerIndex && newHand.State != HandState.Playing)
                    {
                        (newActive, newPhase) = AdvanceFrom(state.ActivePlayerIndex, newPlayers);
                        if (newPhase == GamePhase.PlayerTurns)
                            NarratePlayerTurn(newActive, newPlayers, state.DealerHand);
                    }
                }

                return (With(state, players: newPlayers, phase: newPhase,
                    activePlayerIndex: newActive), effects);
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

                var newPhase  = state.Phase;
                var newActive = state.ActivePlayerIndex;

                if (state.Phase == GamePhase.PlayerTurns)
                {
                    Narrate(NarrationTemplates.Fmt(t.PlayerStand,
                        ("name", state.Players[pi].Name),
                        ("cards", HandString(hand.Cards)),
                        ("score", ScoreString(hand.Cards))));

                    if (pi == state.ActivePlayerIndex)
                    {
                        (newActive, newPhase) = AdvanceFrom(state.ActivePlayerIndex, newPlayers);
                        if (newPhase == GamePhase.PlayerTurns)
                            NarratePlayerTurn(newActive, newPlayers, state.DealerHand);
                    }
                }

                return (With(state, players: newPlayers, phase: newPhase,
                    activePlayerIndex: newActive), effects);
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
                Narrate(NarrationTemplates.Fmt(t.DealPlayerHand, ("name", state.Players[a.PlayerIndex].Name)));
                return (state, effects);

            // ── StartDeal ────────────────────────────────────────────────────
            case StartDeal:
                return (With(state, phase: GamePhase.Deal), effects);

            // ── BeginPlayerTurns ─────────────────────────────────────────────
            case BeginPlayerTurns:
            {
                var sb = new StringBuilder(t.DealSummaryPrefix);
                for (var i = 0; i < state.Players.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var p    = state.Players[i];
                    var hand = p.Hands[0];
                    sb.Append(NarrationTemplates.Fmt(t.DealSummaryPlayer,
                        ("name",  p.Name),
                        ("cards", HandString(hand.Cards)),
                        ("score", ScoreString(hand.Cards)),
                        ("bj",    hand.State == HandState.Blackjack ? " BJ!" : string.Empty)));
                }
                sb.Append(NarrationTemplates.Fmt(t.DealSummaryDealer,
                    ("cards", HandString(state.DealerHand.Cards))));
                Narrate(sb.ToString());

                var (nextActive, nextPhase) = AdvanceFrom(-1, state.Players);
                if (nextPhase == GamePhase.PlayerTurns)
                    NarratePlayerTurn(nextActive, state.Players, state.DealerHand);
                return (With(state, phase: nextPhase, activePlayerIndex: nextActive), effects);
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

                for (var i = 0; i < state.Players.Count; i++)
                {
                    var result = GetPayoutResult(state, i);
                    var label  = result switch
                    {
                        PayoutResult.Win   => "Win",
                        PayoutResult.BjWin => "BJ Win",
                        PayoutResult.Lose  => "Lose",
                        PayoutResult.Push  => "Push",
                        _                  => string.Empty,
                    };
                    if (label.Length == 0) continue;

                    var amount    = PayoutAmountString(state, i);
                    var betStr    = string.IsNullOrWhiteSpace(state.Players[i].Bet)
                                       ? string.Empty
                                       : $" (bet: {state.Players[i].Bet})";
                    var amountStr = amount.Length > 0 ? $" {amount}" : string.Empty;
                    Narrate(NarrationTemplates.Fmt(t.PayoutPlayer,
                        ("name",   state.Players[i].Name),
                        ("result", label),
                        ("bet",    betStr),
                        ("amount", amountStr)));
                }

                return (With(state, phase: GamePhase.Payout), effects);
            }

            // ── NewRound ─────────────────────────────────────────────────────
            case NewRound:
                return (new GameState
                {
                    Players = state.Players.Select(p => new Player
                    {
                        Name  = p.Name,
                        Bet   = p.Bet,
                        Hands = [new Hand()],
                    }).ToList(),
                    DealerHand        = new Hand(),
                    Phase             = GamePhase.Betting,
                    ActivePlayerIndex = -1,
                    BjPayout          = state.BjPayout,
                }, effects);

            // ── Roster management ────────────────────────────────────────────
            case AddPlayer a:
                return (With(state, players:
                    [..state.Players, new Player { Name = a.Name, FullName = a.FullName, World = a.World, Hands = [new Hand()] }]), effects);

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
                    new Player { Name = p.Name, Bet = a.Bet, Hands = p.Hands })), effects);
            }

            case RenamePlayer a:
            {
                var p = state.Players[a.PlayerIndex];
                return (With(state, players: WithPlayer(state.Players, a.PlayerIndex,
                    new Player { Name = a.Name, Bet = p.Bet, Hands = p.Hands })), effects);
            }

            default:
                return (state, effects);
        }
    }
}
