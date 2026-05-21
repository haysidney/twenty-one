using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
        foreach (var c in cards)
        {
            if (c == 1) low += 1;
            else if (c >= 10) low += 10;
            else low += c;
        }
        return low != HandValue(cards);
    }

    public static string ScoreString(IReadOnlyList<int> cards)
    {
        if (cards.Count == 0) return string.Empty;
        var high = HandValue(cards);
        var low  = 0;
        foreach (var c in cards)
        {
            if (c == 1) low += 1;
            else if (c >= 10) low += 10;
            else low += c;
        }
        return (low != high && high <= 21) ? $"{low}/{high}" : high.ToString();
    }

    public static HandState ComputeHandState(IReadOnlyList<int> cards, HandState current, bool isFromSplit = false, bool fiveCardCharlie = false)
    {
        if (current == HandState.Stand) return HandState.Stand;
        var val = HandValue(cards);
        if (val > 21)                                          return HandState.Bust;
        if (!isFromSplit && cards.Count == 2 && val == 21)    return HandState.Blackjack;
        if (fiveCardCharlie && cards.Count >= 5)               return HandState.Charlie;
        return HandState.Playing;
    }

    public static string DealerRecommendation(Hand hand, bool standsOnSoft17)
    {
        if (hand.Cards.Length == 0) return string.Empty;
        var val = HandValue(hand.Cards);
        if (val > 21) return string.Empty;
        var hitsSoft17 = !standsOnSoft17;
        return (val < 17 || (val == 17 && hitsSoft17 && IsSoft(hand.Cards))) ? "HIT" : "STAND";
    }

    public static bool CanGoToPayout(GameState state)
    {
        if (state.Phase != GamePhase.DealerTurn) return false;

        var activePlayers = state.ActivePlayers().ToList();

        if (AllHaveState(activePlayers, HandState.Blackjack))
            return DealerHoleCardRevealedOrSafe(state);

        if (AllHaveState(activePlayers, HandState.Charlie))
        {
            if (state.FiveCardCharlie == FiveCardCharlieRule.LosesToDealerBJ)
                return DealerHoleCardRevealedOrSafe(state);
            return true;
        }

        if (AllHaveTerminalWin(activePlayers))
            return DealerHoleCardRevealedOrSafe(state);

        if (AllHaveState(activePlayers, HandState.Bust))
            return true;

        return DealerStoodOrBust(state);
    }

    private static bool AllHaveState(IReadOnlyList<Player> players, HandState hs) =>
        players.Count > 0 && players.All(p => p.Hands.All(h => h.State == hs));

    private static bool AllHaveTerminalWin(IReadOnlyList<Player> players) =>
        players.Count > 0 && players.All(p => p.Hands.All(h =>
            h.State == HandState.Blackjack || h.State == HandState.Charlie));

    private static bool DealerUpcardCouldBeBJ(GameState state) =>
        state.DealerHand.Cards.Length > 0
        && (state.DealerHand.Cards[0] == 1 || state.DealerHand.Cards[0] >= 10);

    private static bool DealerHoleCardRevealedOrSafe(GameState state) =>
        state.DealerHand.Cards.Length >= 2 || !DealerUpcardCouldBeBJ(state);

    private static bool DealerStoodOrBust(GameState state)
    {
        var dc = state.DealerHand.Cards;
        return dc.Length > 0
            && (HandValue(dc) > 21 || DealerRecommendation(state.DealerHand, state.DealerStandsOnSoft17) == "STAND");
    }

    // ── Action eligibility helpers (public for UI use) ────────────────────────

    // Returns the effective bet for a hand: hand.Bet if set, else player.Bet.
    public static decimal GetEffectiveBet(Player player, Hand hand) =>
        hand.Bet.Length > 0 ? ParseBet(hand.Bet) : ParseBet(player.Bet);

    // Double is allowed on any 2-card Playing hand that hasn't already been doubled,
    // provided the effective bet is numeric. When doubleAfterSplit is false, the
    // hand must not originate from a split.
    public static bool CanDouble(Hand hand, string playerBet, bool doubleAfterSplit) =>
        hand.Cards.Length == 2 && hand.State == HandState.Playing && !hand.Doubled
        && (doubleAfterSplit || !hand.IsFromSplit)
        && (hand.Bet.Length > 0 ? ParseBet(hand.Bet) : ParseBet(playerBet)) > 0;

    // Split is allowed on any 2-card Playing hand where both cards share the same rank.
    // Re-splits (splitting a split hand) are supported. Re-splitting aces requires the
    // resplitAces flag: a pair of aces from a previous split cannot be split again
    // unless RSA is enabled.
    public static bool CanSplit(Hand hand, bool resplitAces) =>
        hand.Cards.Length == 2 && hand.State == HandState.Playing
        && hand.Cards[0] == hand.Cards[1]
        && (resplitAces || !(hand.IsFromSplit && hand.Cards[0] == 1));

    // Hit is allowed on a Playing hand that already has ≥2 cards (1-card split hands
    // are auto-hit). When hitSplitAces is false, a split-ace hand (IsFromSplit with
    // an ace as the first card) cannot be hit further - it should already have been
    // forced to Stand, but the guard catches the RSA-on / HSA-off case where the
    // engine intentionally leaves a [A,A] pair Playing so the player can resplit.
    public static bool CanHit(Hand hand, bool hitSplitAces) =>
        hand.State == HandState.Playing && hand.Cards.Length >= 2
        && (hitSplitAces || !(hand.IsFromSplit && hand.Cards[0] == 1));

    // Stand is allowed whenever the hand is Playing and has ≥2 cards. Unlike Hit,
    // Stand is not gated by HSA - the player can always end their hand voluntarily.
    public static bool CanStand(Hand hand) =>
        hand.State == HandState.Playing && hand.Cards.Length >= 2;

    // Deal phase is complete when the dealer has ≥1 card and every player's first hand has ≥2 cards.
    public static bool IsDealComplete(GameState state) =>
        state.DealerHand.Cards.Length >= 1
        && state.Players.Length > 0
        && state.Players.All(p => p.SittingOut || p.Hands[0].Cards.Length >= 2);

    // Dealer may receive a card during Deal (exactly 1 card; 0 so far) or during DealerTurn (must hit).
    public static bool CanHitDealer(GameState state)
    {
        if (state.Phase == GamePhase.Deal) return state.DealerHand.Cards.Length < 1;
        if (state.Phase != GamePhase.DealerTurn || state.WaitingForDealer) return false;
        return !state.IsAllBust()
            && !CanGoToPayout(state)
            && DealerRecommendation(state.DealerHand, state.DealerStandsOnSoft17) == "HIT"
            && HandValue(state.DealerHand.Cards) <= 21;
    }

    public static string ValidActionsString(Hand hand, bool canDouble, bool canSplit)
    {
        if (hand.State != HandState.Playing) return string.Empty;
        var options = new List<string> { "Hit", "Stand" };
        if (canDouble) options.Add("Double Down");
        if (canSplit)  options.Add("Split");
        if (options.Count == 2) return $"{options[0]} or {options[1]}";
        return string.Join(", ", options[..^1]) + ", or " + options[^1];
    }

    // ── Payout helpers (public for UI use) ────────────────────────────────────

    public static PayoutResult GetPayoutResult(GameState state, int playerIndex, int handIndex = 0)
    {
        var hand = state.Players[playerIndex].Hands[handIndex];
        if (hand.Cards.Length == 0)        return PayoutResult.None;
        if (hand.State == HandState.Bust) return PayoutResult.Lose;

        var dealerVal  = HandValue(state.DealerHand.Cards);
        var dealerBust = state.DealerHand.Cards.Length > 0 && dealerVal > 21;
        var dealerBJ   = state.DealerHand.Cards.Length == 2 && dealerVal == 21;
        var playerBJ   = hand.State == HandState.Blackjack;
        var charlie    = hand.State == HandState.Charlie;

        if (charlie)
        {
            if (dealerBJ && state.FiveCardCharlie == FiveCardCharlieRule.LosesToDealerBJ)
                return PayoutResult.Lose;
            return PayoutResult.CharlieWin;
        }

        if (playerBJ && dealerBJ) return PayoutResult.Push;
        if (playerBJ)             return PayoutResult.BjWin;
        if (dealerBJ)             return PayoutResult.Lose;
        if (dealerBust)           return PayoutResult.Win;
        if (state.DealerHand.Cards.Length == 0) return PayoutResult.None;

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
        if (abs >= 1_000_000) return $"{v / 1_000_000:0.##}M";
        if (abs >= 1_000)     return $"{v / 1_000:0.##}K";
        return $"{v:0.##}";
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
            PayoutResult.Win        => bet,
            PayoutResult.BjWin      => Math.Ceiling(bet * (decimal)state.BjPayout),
            PayoutResult.CharlieWin => Math.Ceiling(bet * CharlieMultiplier(state.CharliePayout)),
            PayoutResult.Lose       => -bet,
            _                       => 0m,
        };
        return delta == 0 ? null : delta;
    }

    /// <summary>
    /// Total gil owed back to the player at settlement: bet returned + profit
    /// for wins, bet returned for push, zero for loss/no-bet. Used by the bank
    /// settlement path which deposits the gross amount.
    /// </summary>
    public static decimal PayoutTotalOwed(GameState state, int playerIndex, int handIndex = 0)
    {
        var player = state.Players[playerIndex];
        var hand   = player.Hands[handIndex];
        var bet    = GetEffectiveBet(player, hand);
        if (bet <= 0) return 0m;
        return GetPayoutResult(state, playerIndex, handIndex) switch
        {
            PayoutResult.Win        => bet * 2m,
            PayoutResult.BjWin      => bet + Math.Ceiling(bet * (decimal)state.BjPayout),
            PayoutResult.CharlieWin => bet + Math.Ceiling(bet * CharlieMultiplier(state.CharliePayout)),
            PayoutResult.Push       => bet,
            _                       => 0m,
        };
    }

    public static string PayoutAmountString(GameState state, int playerIndex, int handIndex = 0)
    {
        var delta = PayoutDelta(state, playerIndex, handIndex);
        if (delta == null) return string.Empty;
        return delta > 0 ? $"+{FormatGil(delta.Value)}" : FormatGil(delta.Value);
    }

    private static decimal PayoutMultiplier(PayoutRatio ratio) => ratio switch
    {
        PayoutRatio.SixToFive => 1.2m,
        PayoutRatio.EvenMoney => 1.0m,
        _                     => 1.5m,
    };

    private static decimal CharlieMultiplier(PayoutRatio payout) => PayoutMultiplier(payout);

    // ── Internal state builders ───────────────────────────────────────────────

    private static Hand AddCardToHand(Hand hand, int card, bool fiveCardCharlie = false)
    {
        ImmutableArray<int> cards = [..hand.Cards, card];
        return hand with
        {
            Cards = cards,
            State = ComputeHandState(cards, hand.State, hand.IsFromSplit, fiveCardCharlie),
        };
    }

    private static Player WithHand(Player player, int hi, Hand newHand) =>
        player with { Hands = player.Hands.Select((h, i) => i == hi ? newHand : h).ToImmutableArray() };

    private static ImmutableArray<Player> WithPlayer(ImmutableArray<Player> players, int pi, Player newPlayer) =>
        players.Select((p, i) => i == pi ? newPlayer : p).ToImmutableArray();

    /// <summary>
    /// Advances to the next Playing hand after <paramref name="fromPi"/>/<paramref name="fromHi"/>.
    /// Pass fromPi=-1 to start from the very first hand.
    /// Returns the new active (player, hand) and phase; transitions to DealerTurn (or Payout
    /// if all hands busted) when no more Playing hands remain.
    /// </summary>
    private static (int Pi, int Hi, GamePhase Phase) AdvanceFrom(
        int fromPi, int fromHi, ImmutableArray<Player> players)
    {
        var startPi = fromPi < 0 ? 0 : fromPi;
        for (var pi = startPi; pi < players.Length; pi++)
        {
            if (players[pi].SittingOut) continue;
            var startHi = (pi == fromPi) ? fromHi + 1 : 0;
            for (var hi = startHi; hi < players[pi].Hands.Length; hi++)
            {
                var hs = players[pi].Hands[hi].State;
                if (hs == HandState.Playing || hs == HandState.Blackjack)
                    return (pi, hi, GamePhase.PlayerTurns);
            }
        }
        var allBust = players.All(p => p.SittingOut || p.Hands.All(h => h.State == HandState.Bust));
        return (-1, -1, allBust ? GamePhase.Payout : GamePhase.DealerTurn);
    }

    // ── Narration context ─────────────────────────────────────────────────────

    private sealed record NarrationContext(
        NarrationTemplates Templates,
        string             DealerName,
        List<ISideEffect>   Effects)
    {
        public void Narrate(List<List<string>> variants, params (string Key, string Value)[] vars)
        {
            if (variants.Count == 0) return;
            var lines = variants[Random.Shared.Next(variants.Count)];
            foreach (var line in lines)
            {
                var resolved = vars.Length > 0 ? NarrationTemplates.Fmt(line, vars) : line;
                if (!string.IsNullOrWhiteSpace(resolved)) Effects.Add(new SendChat(resolved));
            }
        }

        public void NarrateStr(string text)
        {
            if (!string.IsNullOrWhiteSpace(text)) Effects.Add(new SendChat(text));
        }

        public void NarratePlayerTurn(int pi, int hi, ImmutableArray<Player> players, Hand dealerHand, bool doubleAfterSplit, bool resplitAces)
        {
            if (pi < 0 || pi >= players.Length) return;
            var player = players[pi];
            if (hi < 0 || hi >= player.Hands.Length) return;
            var hand = player.Hands[hi];
            if (hand.Cards.Length < 2) return;
            var cd = CanDouble(hand, player.Bet, doubleAfterSplit);
            var cs = CanSplit(hand, resplitAces);
            var actions = ValidActionsString(hand, cd, cs);
            var name = player.Hands.Length > 1 ? $"{player.DisplayName} (Hand {hi + 1})" : player.DisplayName;
            Narrate(Templates.PlayerTurnStart,
                ("name",        name),
                ("cards",       HandString(hand.Cards)),
                ("score",       ScoreString(hand.Cards)),
                ("dealerCards", HandString(dealerHand.Cards)),
                ("dealerScore", ScoreString(dealerHand.Cards)),
                ("actions",     actions));
        }

        public void NarrateDealSummary(GameState s)
        {
            var activePlayers = s.ActivePlayers().ToList();
            if (!(s.SkipDealSummaryOnePlayer && activePlayers.Count == 1))
            {
                var sb = new StringBuilder(Templates.DealSummaryPrefix);
                var first = true;
                for (var i = 0; i < s.Players.Length; i++)
                {
                    var p = s.Players[i];
                    if (p.SittingOut) continue;
                    if (!first) sb.Append(", ");
                    first = false;
                    var hand = p.Hands[0];
                    sb.Append(NarrationTemplates.Fmt(Templates.DealSummaryPlayer,
                        ("name",  p.DisplayName),
                        ("cards", HandString(hand.Cards)),
                        ("score", ScoreString(hand.Cards)),
                        ("bj",    hand.State == HandState.Blackjack ? " BJ!" : string.Empty)));
                }
                sb.Append(NarrationTemplates.Fmt(Templates.DealSummaryDealer,
                    ("dealer", DealerName), ("cards", HandString(s.DealerHand.Cards))));
                NarrateStr(sb.ToString());
            }
            foreach (var p in s.Players)
            {
                if (p.SittingOut || p.Hands.Length == 0) continue;
                var hand = p.Hands[0];
                if (hand.State == HandState.Blackjack)
                    Narrate(Templates.PlayerBJ, ("name", p.DisplayName), ("cards", HandString(hand.Cards)));
            }
        }
    }

    // ── Apply ─────────────────────────────────────────────────────────────────

    private static readonly Dictionary<Type, Func<GameState, GameAction, NarrationContext, GameState>> ActionHandlers = new()
    {
        [typeof(AddDealerCard)]        = (s, a, ctx) => HandleAddDealerCard(s, (AddDealerCard)a, ctx),
        [typeof(AddPlayerCard)]        = (s, a, ctx) => HandleAddPlayerCard(s, (AddPlayerCard)a, ctx),
        [typeof(StandPlayer)]          = (s, a, ctx) => HandleStandPlayer(s, (StandPlayer)a, ctx),
        [typeof(DoubleDown)]           = (s, a, _) => HandleDoubleDown(s, (DoubleDown)a),
        [typeof(SplitHand)]            = (s, a, ctx) => HandleSplitHand(s, (SplitHand)a, ctx),
        [typeof(AnnounceDealerHit)]    = (s, _, ctx) => HandleAnnounceDealerHit(s, ctx),
        [typeof(AnnouncePlayerHit)]    = (s, a, ctx) => HandleAnnouncePlayerHit(s, (AnnouncePlayerHit)a, ctx),
        [typeof(AnnouncePlayerTurn)]   = (s, a, ctx) => HandleAnnouncePlayerTurn(s, (AnnouncePlayerTurn)a, ctx),
        [typeof(AnnounceDouble)]       = (s, a, ctx) => HandleAnnounceDouble(s, (AnnounceDouble)a, ctx),
        [typeof(AnnounceDoubleConfirm)] = (s, a, ctx) => HandleAnnounceDoubleConfirm(s, (AnnounceDoubleConfirm)a, ctx),
        [typeof(AnnounceSplit)]        = (s, a, ctx) => HandleAnnounceSplit(s, (AnnounceSplit)a, ctx),
        [typeof(AnnounceBettingOpen)]  = (s, _, ctx) => HandleAnnounceBettingOpen(s, ctx),
        [typeof(AnnounceBetRequest)]   = (s, a, ctx) => HandleAnnounceBetRequest(s, (AnnounceBetRequest)a, ctx),
        [typeof(AnnounceBetConfirm)]   = (s, a, ctx) => HandleAnnounceBetConfirm(s, (AnnounceBetConfirm)a, ctx),
        [typeof(AnnounceBankRemind)]   = (s, a, ctx) => HandleAnnounceBankRemind(s, (AnnounceBankRemind)a, ctx),
        [typeof(AnnounceBankShortfall)] = (s, a, ctx) => HandleAnnounceBankShortfall(s, (AnnounceBankShortfall)a, ctx),
        [typeof(AnnounceBankDeposit)]  = (s, a, ctx) => HandleAnnounceBankDeposit(s, (AnnounceBankDeposit)a, ctx),
        [typeof(AnnounceBankWithdraw)] = (s, a, ctx) => HandleAnnounceBankWithdraw(s, (AnnounceBankWithdraw)a, ctx),
        [typeof(AnnounceDealerDeal)]   = (s, _, ctx) => HandleAnnounceDealerDeal(s, ctx),
        [typeof(AnnouncePlayerDeal)]   = (s, a, ctx) => HandleAnnouncePlayerDeal(s, (AnnouncePlayerDeal)a, ctx),
        [typeof(StartDeal)]            = (s, _, _) => HandleStartDeal(s),
        [typeof(BeginPlayerTurns)]     = (s, _, ctx) => HandleBeginPlayerTurns(s, ctx),
        [typeof(AdvanceToNextPlayer)]  = (s, _, ctx) => HandleAdvanceToNextPlayer(s, ctx),
        [typeof(BeginDealerTurn)]      = (s, _, ctx) => HandleBeginDealerTurn(s, ctx),
        [typeof(GoToPayout)]           = (s, _, ctx) => HandleGoToPayout(s, ctx),
        [typeof(NewRound)]             = (s, _, _) => HandleNewRound(s),
        [typeof(AddPlayer)]            = (s, a, _) => HandleAddPlayer(s, (AddPlayer)a),
        [typeof(RemovePlayer)]         = (s, a, _) => HandleRemovePlayer(s, (RemovePlayer)a),
        [typeof(SetPlayerBet)]         = (s, a, _) => HandleSetPlayerBet(s, (SetPlayerBet)a),
        [typeof(AdjustBet)]            = (s, a, _) => HandleAdjustBet(s, (AdjustBet)a),
        [typeof(RenamePlayer)]         = (s, a, _) => HandleRenamePlayer(s, (RenamePlayer)a),
        [typeof(ToggleSittingOut)]     = (s, a, _) => HandleToggleSittingOut(s, (ToggleSittingOut)a),
        [typeof(ReorderPlayers)]       = (s, a, _) => HandleReorderPlayers(s, (ReorderPlayers)a),
    };

    public static (GameState State, IReadOnlyList<ISideEffect> Effects) Apply(
        GameState state, GameAction action, NarrationTemplates? templates = null, string dealerName = "Dealer")
    {
        var t       = templates ?? new NarrationTemplates();
        var effects = new List<ISideEffect>();
        var ctx = new NarrationContext(t, dealerName, effects);

        var newState = ActionHandlers.TryGetValue(action.GetType(), out var handler)
            ? handler(state, action, ctx)
            : state;
        return (newState, effects);
    }

    private static GameState HandleAddDealerCard(GameState state, AddDealerCard a, NarrationContext ctx)
    {
        var newHand = AddCardToHand(state.DealerHand, a.Card);
        if (state.Phase == GamePhase.DealerTurn)
            NarrateDealerCard(ctx, a.Card, newHand, state.DealerStandsOnSoft17);
        var newStateD = state with { DealerHand = newHand };
        if (state.Phase == GamePhase.Deal && IsDealComplete(newStateD))
            ctx.NarrateDealSummary(newStateD);
        return newStateD;
    }

    private static void NarrateDealerCard(NarrationContext ctx, int card, Hand newHand, bool standsOnSoft17)
    {
        var t = ctx.Templates;
        var dealerName = ctx.DealerName;
        var cards   = HandString(newHand.Cards);
        var score   = ScoreString(newHand.Cards);
        var val     = HandValue(newHand.Cards);
        var cardLbl = CardLabel(card);
        if (val > 21)
            ctx.Narrate(t.DealerBust,
                ("dealer", dealerName), ("card", cardLbl), ("cards", cards), ("score", score));
        else if (newHand.Cards.Length == 2 && val == 21)
            ctx.Narrate(t.DealerBJ,
                ("dealer", dealerName), ("card", cardLbl), ("cards", cards));
        else
        {
            ctx.Narrate(t.DealerHit,
                ("dealer", dealerName), ("card", cardLbl), ("cards", cards), ("score", score));
            if (DealerRecommendation(newHand, standsOnSoft17) == "STAND")
                ctx.Narrate(t.DealerStand,
                    ("dealer", dealerName), ("cards", cards), ("score", score));
        }
    }

    private static GameState HandleAddPlayerCard(GameState state, AddPlayerCard a, NarrationContext ctx)
    {
        var pi            = a.PlayerIndex;
        var hi            = a.HandIndex;
        if (state.Players[pi].SittingOut) return state;
        var prevCardCount   = state.Players[pi].Hands[hi].Cards.Length;
        var fiveCardCharlie = state.FiveCardCharlie != FiveCardCharlieRule.Disabled;
        var newHand         = AddCardToHand(state.Players[pi].Hands[hi], a.Card, fiveCardCharlie);

        if (newHand.State == HandState.Playing)
        {
            if (newHand.Doubled)
                newHand = newHand with { State = HandState.Stand };
            else if (!state.HitSplitAces && newHand.IsFromSplit && newHand.Cards.Length == 2
                     && newHand.Cards[0] == 1
                     && !(state.ResplitAces && newHand.Cards[1] == 1))
                // Auto-stand split aces UNLESS the player should still get to choose
                // between Stand and Split on a fresh [A,A] pair (HSA off + RSA on).
                newHand = newHand with { State = HandState.Stand };
            else if (HandValue(newHand.Cards) == 21)
                newHand = newHand with { State = HandState.Stand };
        }

        var newPlayers = WithPlayer(state.Players, pi, WithHand(state.Players[pi], hi, newHand));
        var newPhase               = state.Phase;
        var newActivePi            = state.ActivePlayerIndex;
        var newActiveHi            = state.ActiveHandIndex;
        var newWaitingForNextPlayer = false;
        var newWaitingForDealer     = false;

        if (state.Phase == GamePhase.Deal)
        {
            var newStateP = state with { Players = newPlayers };
            if (IsDealComplete(newStateP))
                ctx.NarrateDealSummary(newStateP);
        }
        else if (state.Phase == GamePhase.PlayerTurns)
        {
            NarratePlayerCardInPlayerTurns(ctx, pi, hi, state, newHand, prevCardCount, a.Card);

            if (pi == state.ActivePlayerIndex && hi == state.ActiveHandIndex)
            {
                if (newHand.State != HandState.Playing)
                {
                    var (peekPi, peekHi, peekPhase) = AdvanceFrom(pi, hi, newPlayers);
                    if (peekPhase is GamePhase.DealerTurn or GamePhase.Payout)
                    {
                        newPhase = GamePhase.DealerTurn;
                        var provisional = state with { Phase = GamePhase.DealerTurn, Players = newPlayers };
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
                    ctx.NarratePlayerTurn(pi, hi, newPlayers, state.DealerHand, state.DoubleAfterSplit, state.ResplitAces);
                }
            }
        }

        return state with
        {
            Players = newPlayers,
            Phase = newPhase,
            ActivePlayerIndex = newActivePi,
            ActiveHandIndex = newActiveHi,
            WaitingForNextPlayer = newWaitingForNextPlayer,
            WaitingForDealer = newWaitingForDealer,
        };
    }

    private static void NarratePlayerCardInPlayerTurns(NarrationContext ctx, int pi, int hi, GameState state, Hand newHand, int prevCardCount, int addedCard)
    {
        var t = ctx.Templates;
        var multiHand   = state.Players[pi].Hands.Length > 1;
        var displayName = multiHand
            ? $"{state.Players[pi].DisplayName} (Hand {hi + 1})"
            : state.Players[pi].DisplayName;
        var cards   = HandString(newHand.Cards);
        var score   = ScoreString(newHand.Cards);
        var cardLbl = CardLabel(addedCard);

        if (prevCardCount == 1)
        {
            if (newHand.State == HandState.Stand)
                ctx.Narrate(t.PlayerSplitAce,
                    ("name", displayName), ("card", cardLbl), ("cards", cards), ("score", score));
        }
        else if (newHand.State == HandState.Bust)
            ctx.Narrate(t.PlayerBust,
                ("name", displayName), ("cards", cards), ("score", score));
        else if (newHand.State == HandState.Blackjack)
            ctx.Narrate(t.PlayerBJ,
                ("name", displayName), ("cards", cards));
        else if (newHand.State == HandState.Charlie)
            ctx.Narrate(t.PlayerCharlie,
                ("name", displayName), ("card", cardLbl), ("cards", cards), ("score", score));
        else if (newHand.Doubled && newHand.State == HandState.Stand)
            ctx.Narrate(t.PlayerDouble,
                ("name", displayName), ("card", cardLbl), ("cards", cards), ("score", score));
        else
        {
            ctx.Narrate(t.PlayerHit,
                ("name", displayName), ("card", cardLbl), ("cards", cards), ("score", score));
            if (newHand.State == HandState.Playing && pi == state.ActivePlayerIndex && hi == state.ActiveHandIndex)
            {
                var cd2 = CanDouble(newHand, state.Players[pi].Bet, state.DoubleAfterSplit);
                var cs2 = CanSplit(newHand, state.ResplitAces);
                ctx.Narrate(t.PlayerAfterHit,
                    ("name",    displayName),
                    ("cards",   cards),
                    ("score",   score),
                    ("actions", ValidActionsString(newHand, cd2, cs2)));
            }
        }
    }

    private static GameState HandleStandPlayer(GameState state, StandPlayer a, NarrationContext ctx)
    {
        var t = ctx.Templates;
        var pi   = a.PlayerIndex;
        var hi   = a.HandIndex;
        var hand = state.Players[pi].Hands[hi];
        if (hand.State != HandState.Playing) return state;

        var newHand = hand with { State = HandState.Stand };
        var newPlayers = WithPlayer(state.Players, pi, WithHand(state.Players[pi], hi, newHand));
        var newPhase               = state.Phase;
        var newActivePi            = state.ActivePlayerIndex;
        var newActiveHi            = state.ActiveHandIndex;
        var newWaitingForNextPlayer = false;
        var newWaitingForDealer     = false;

        if (state.Phase == GamePhase.PlayerTurns)
        {
            var multiHand   = state.Players[pi].Hands.Length > 1;
            var displayName = multiHand
                ? $"{state.Players[pi].DisplayName} (Hand {hi + 1})"
                : state.Players[pi].DisplayName;
            ctx.Narrate(t.PlayerStand,
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

        return state with
        {
            Players = newPlayers,
            Phase = newPhase,
            ActivePlayerIndex = newActivePi,
            ActiveHandIndex = newActiveHi,
            WaitingForNextPlayer = newWaitingForNextPlayer,
            WaitingForDealer = newWaitingForDealer,
        };
    }

    private static GameState HandleDoubleDown(GameState state, DoubleDown a)
    {
        var pi     = a.PlayerIndex;
        var hi     = a.HandIndex;
        var player = state.Players[pi];
        var hand   = player.Hands[hi];
        var bet    = GetEffectiveBet(player, hand);
        var newBet = (bet * 2).ToString("0.##");
        var newHand = hand with { Doubled = true, Bet = newBet };
        var newPlayers = WithPlayer(state.Players, pi, WithHand(player, hi, newHand));
        return state with { Players = newPlayers };
    }

    private static GameState HandleSplitHand(GameState state, SplitHand a, NarrationContext ctx)
    {
        var t = ctx.Templates;
        var pi     = a.PlayerIndex;
        var hi     = a.HandIndex;
        var player = state.Players[pi];
        var hand   = player.Hands[hi];
        var hand0  = new Hand { Cards = [hand.Cards[0]], State = HandState.Playing, IsFromSplit = true };
        var hand1  = new Hand { Cards = [hand.Cards[1]], State = HandState.Playing, IsFromSplit = true };
        var handsBuilder = player.Hands.ToBuilder();
        handsBuilder[hi] = hand0;
        handsBuilder.Insert(hi + 1, hand1);
        var newPlayer  = player with { Hands = handsBuilder.ToImmutable() };
        var newPlayers = WithPlayer(state.Players, pi, newPlayer);
        var name       = player.Hands.Length > 1 ? $"{player.DisplayName} (Hand {hi + 1})" : player.DisplayName;
        ctx.Narrate(t.PlayerSplit, ("name", name));
        var rollName   = $"{player.DisplayName} (Hand {hi + 1})";
        ctx.Narrate(t.PlayerSplitRoll, ("name", rollName));
        ctx.Effects.Add(new AutoHit(pi, hi));
        return state with { Players = newPlayers, ActivePlayerIndex = pi, ActiveHandIndex = hi };
    }

    private static GameState HandleAnnounceDealerHit(GameState state, NarrationContext ctx)
    {
        var t = ctx.Templates;
        var allBJ = state.Players.Length > 0
                 && state.Players.All(p => p.Hands.All(h => h.State == HandState.Blackjack));
        var allCharlie = state.Players.Length > 0
                      && state.Players.All(p => p.Hands.All(h => h.State == HandState.Charlie));
        var checkBJ = allBJ || (allCharlie && state.FiveCardCharlie == FiveCardCharlieRule.LosesToDealerBJ);
        ctx.Narrate(checkBJ ? t.DealerBJCheck : t.DealerHitAnnounce, ("dealer", ctx.DealerName));
        return state;
    }

    private static GameState HandleAnnouncePlayerHit(GameState state, AnnouncePlayerHit a, NarrationContext ctx)
    {
        var t = ctx.Templates;
        var player = state.Players[a.PlayerIndex];
        var name   = player.Hands.Length > 1 ? $"{player.DisplayName} (Hand {a.HandIndex + 1})" : player.DisplayName;
        ctx.Narrate(t.PlayerHitAnnounce, ("name", name));
        return state;
    }

    private static GameState HandleAnnouncePlayerTurn(GameState state, AnnouncePlayerTurn a, NarrationContext ctx)
    {
        ctx.NarratePlayerTurn(a.PlayerIndex, a.HandIndex, state.Players, state.DealerHand, state.DoubleAfterSplit, state.ResplitAces);
        return state;
    }

    private static GameState HandleAnnounceDouble(GameState state, AnnounceDouble a, NarrationContext ctx)
    {
        var t = ctx.Templates;
        var player = state.Players[a.PlayerIndex];
        var hand   = player.Hands[a.HandIndex];
        var bet    = GetEffectiveBet(player, hand);
        var name   = player.Hands.Length > 1 ? $"{player.DisplayName} (Hand {a.HandIndex + 1})" : player.DisplayName;
        if (a.FromBank)
            ctx.Narrate(t.PlayerDoubleRequestBank, ("name", name), ("amount", FormatGil(bet)), ("bank", FormatGil(a.BankAfter)));
        else
            ctx.Narrate(t.PlayerDoubleRequest, ("name", name), ("amount", $"{a.BankAfter}"));
        return state;
    }

    private static GameState HandleAnnounceDoubleConfirm(GameState state, AnnounceDoubleConfirm a, NarrationContext ctx)
    {
        var t = ctx.Templates;
        var player = state.Players[a.PlayerIndex];
        var name   = player.Hands.Length > 1 ? $"{player.DisplayName} (Hand {a.HandIndex + 1})" : player.DisplayName;
        ctx.Narrate(t.PlayerDoubleConfirm, ("name", name));
        return state;
    }

    private static GameState HandleAnnounceSplit(GameState state, AnnounceSplit a, NarrationContext ctx)
    {
        var t = ctx.Templates;
        var player = state.Players[a.PlayerIndex];
        var hand   = player.Hands[a.HandIndex];
        var bet    = GetEffectiveBet(player, hand);
        var name   = player.Hands.Length > 1 ? $"{player.DisplayName} (Hand {a.HandIndex + 1})" : player.DisplayName;
        if (a.FromBank)
            ctx.Narrate(t.PlayerSplitRequestBank, ("name", name), ("amount", FormatGil(bet)), ("bank", FormatGil(a.BankAfter)));
        else
            ctx.Narrate(t.PlayerSplitRequest, ("name", name), ("amount", $"{a.BankAfter}"));
        return state;
    }

    private static GameState HandleAnnounceBettingOpen(GameState state, NarrationContext ctx)
    {
        ctx.Narrate(ctx.Templates.BettingOpen);
        return state;
    }

    private static GameState HandleAnnounceBetRequest(GameState state, AnnounceBetRequest a, NarrationContext ctx)
    {
        var t = ctx.Templates;
        var player = state.Players[a.PlayerIndex];
        ctx.Narrate(t.PlayerBetRequest, ("name", player.DisplayName));
        return state;
    }

    private static GameState HandleAnnounceBetConfirm(GameState state, AnnounceBetConfirm a, NarrationContext ctx)
    {
        var t = ctx.Templates;
        var player = state.Players[a.PlayerIndex];
        var betAmt = ParseBet(player.Bet);
        if (a.Bank > 0)
        {
            var bankAfterBet = Math.Max(0, a.Bank - (long)Math.Ceiling(betAmt));
            ctx.Narrate(t.PlayerBetConfirmBank,
                ("name", player.DisplayName),
                ("amount", FormatGil(betAmt)),
                ("bank", FormatGil(a.Bank)),
                ("bank-after-bet", FormatGil(bankAfterBet)));
        }
        else
        {
            ctx.Narrate(t.PlayerBetConfirm, ("name", player.DisplayName), ("amount", FormatGil(betAmt)));
        }
        return state;
    }

    private static GameState HandleAnnounceBankRemind(GameState state, AnnounceBankRemind a, NarrationContext ctx)
    {
        var t = ctx.Templates;
        var player = state.Players[a.PlayerIndex];
        ctx.Narrate(t.PlayerBankRemind,
            ("name",   player.DisplayName),
            ("amount", FormatGil(ParseBet(player.Bet))),
            ("bank",   FormatGil(a.Bank)));
        return state;
    }

    private static GameState HandleAnnounceBankShortfall(GameState state, AnnounceBankShortfall a, NarrationContext ctx)
    {
        var t = ctx.Templates;
        var player = state.Players[a.PlayerIndex];
        ctx.Narrate(t.PlayerBankShortfall,
            ("name",   player.DisplayName),
            ("amount", $"{a.ShortfallAmount}"));
        return state;
    }

    private static GameState HandleAnnounceBankDeposit(GameState state, AnnounceBankDeposit a, NarrationContext ctx)
    {
        var t = ctx.Templates;
        var player = state.Players[a.PlayerIndex];
        ctx.Narrate(t.PlayerBankDeposit,
            ("name",   player.DisplayName),
            ("amount", FormatGil(a.Amount)),
            ("bank",   FormatGil(a.NewBalance)));
        return state;
    }

    private static GameState HandleAnnounceBankWithdraw(GameState state, AnnounceBankWithdraw a, NarrationContext ctx)
    {
        var t = ctx.Templates;
        var player = state.Players[a.PlayerIndex];
        ctx.Narrate(t.PlayerBankWithdraw,
            ("name",   player.DisplayName),
            ("amount", FormatGil(a.Amount)),
            ("bank",   FormatGil(a.NewBalance)));
        return state;
    }

    private static GameState HandleAnnounceDealerDeal(GameState state, NarrationContext ctx)
    {
        ctx.Narrate(ctx.Templates.DealDealerCard, ("dealer", ctx.DealerName));
        return state;
    }

    private static GameState HandleAnnouncePlayerDeal(GameState state, AnnouncePlayerDeal a, NarrationContext ctx)
    {
        ctx.Narrate(ctx.Templates.DealPlayerHand, ("name", state.Players[a.PlayerIndex].DisplayName));
        return state;
    }

    private static GameState HandleStartDeal(GameState state) =>
        state with { Phase = GamePhase.Deal };

    private static GameState HandleBeginPlayerTurns(GameState state, NarrationContext ctx)
    {
        var (nextPi, nextHi, nextPhase) = AdvanceFrom(-1, -1, state.Players);

        if (nextPhase == GamePhase.PlayerTurns
            && state.Players[nextPi].Hands[nextHi].State == HandState.Blackjack)
        {
            return HandleBeginPlayerTurnsBJScan(state, ctx, nextPi, nextHi);
        }

        return HandleBeginPlayerTurnsDefault(state, ctx, nextPi, nextHi, nextPhase);
    }

    private static GameState HandleBeginPlayerTurnsBJScan(GameState state, NarrationContext ctx, int nextPi, int nextHi)
    {
        var t = ctx.Templates;
        var (scanPi, scanHi, scanPhase) = AdvanceFrom(nextPi, nextHi, state.Players);
        while (scanPhase == GamePhase.PlayerTurns
               && state.Players[scanPi].Hands[scanHi].State == HandState.Blackjack)
        {
            (scanPi, scanHi, scanPhase) = AdvanceFrom(scanPi, scanHi, state.Players);
        }

        if (scanPhase == GamePhase.DealerTurn || scanPhase == GamePhase.Payout)
        {
            var provDealer = state with { Phase = GamePhase.DealerTurn };
            var nwWait = scanPhase == GamePhase.DealerTurn && !CanGoToPayout(provDealer);
            return state with
            {
                Phase = scanPhase,
                ActivePlayerIndex = scanPi,
                ActiveHandIndex = scanHi,
                WaitingForDealer = nwWait,
                WaitingForNextPlayer = false,
            };
        }

        var hand = state.Players[nextPi].Hands[nextHi];
        var name = state.Players[nextPi].Hands.Length > 1
            ? $"{state.Players[nextPi].DisplayName} (Hand {nextHi + 1})"
            : state.Players[nextPi].DisplayName;
        if (state.Players.Length > 1)
            ctx.Narrate(t.PlayerBJMovingAlong, ("name", name), ("cards", HandString(hand.Cards)));
        return state with
        {
            Phase = GamePhase.PlayerTurns,
            ActivePlayerIndex = nextPi,
            ActiveHandIndex = nextHi,
            WaitingForDealer = false,
            WaitingForNextPlayer = true,
        };
    }

    private static GameState HandleBeginPlayerTurnsDefault(GameState state, NarrationContext ctx, int nextPi, int nextHi, GamePhase nextPhase)
    {
        var provisionalDealer = state with { Phase = GamePhase.DealerTurn };
        var waitDealer = nextPhase == GamePhase.DealerTurn && !CanGoToPayout(provisionalDealer);
        var waitNext   = false;
        if (nextPhase == GamePhase.PlayerTurns)
            ctx.NarratePlayerTurn(nextPi, nextHi, state.Players, state.DealerHand, state.DoubleAfterSplit, state.ResplitAces);
        if (waitDealer) nextPhase = GamePhase.DealerTurn;
        return state with
        {
            Phase = nextPhase,
            ActivePlayerIndex = nextPi,
            ActiveHandIndex = nextHi,
            WaitingForDealer = waitDealer,
            WaitingForNextPlayer = waitNext,
        };
    }

    private static GameState HandleAdvanceToNextPlayer(GameState state, NarrationContext ctx)
    {
        if (!state.WaitingForNextPlayer) return state;
        var (nextPi, nextHi, nextPhase) = AdvanceFrom(
            state.ActivePlayerIndex, state.ActiveHandIndex, state.Players);

        if (nextPhase == GamePhase.PlayerTurns)
            return AdvanceToPlayerTurns(state, ctx, nextPi, nextHi);

        if (nextPhase == GamePhase.DealerTurn)
            return AdvanceToDealerTurn(state, nextPi, nextHi);

        return state with
        {
            Phase = nextPhase,
            ActivePlayerIndex = nextPi,
            ActiveHandIndex = nextHi,
            WaitingForNextPlayer = false,
        };
    }

    private static GameState AdvanceToPlayerTurns(GameState state, NarrationContext ctx, int nextPi, int nextHi)
    {
        var t = ctx.Templates;
        var nextHand = state.Players[nextPi].Hands[nextHi];
        if (nextHand.Cards.Length == 1)
        {
            var advName = $"{state.Players[nextPi].DisplayName} (Hand {nextHi + 1})";
            ctx.Narrate(t.PlayerSplitRoll, ("name", advName));
            ctx.Effects.Add(new AutoHit(nextPi, nextHi));
        }
        else if (nextHand.State == HandState.Blackjack)
        {
            var name = state.Players[nextPi].Hands.Length > 1
                ? $"{state.Players[nextPi].DisplayName} (Hand {nextHi + 1})"
                : state.Players[nextPi].DisplayName;
            if (state.Players.Length > 1)
                ctx.Narrate(t.PlayerBJMovingAlong, ("name", name), ("cards", HandString(nextHand.Cards)));
            return state with
            {
                Phase = GamePhase.PlayerTurns,
                ActivePlayerIndex = nextPi,
                ActiveHandIndex = nextHi,
                WaitingForNextPlayer = true,
            };
        }
        else
            ctx.NarratePlayerTurn(nextPi, nextHi, state.Players, state.DealerHand, state.DoubleAfterSplit, state.ResplitAces);

        return state with
        {
            Phase = GamePhase.PlayerTurns,
            ActivePlayerIndex = nextPi,
            ActiveHandIndex = nextHi,
            WaitingForNextPlayer = false,
        };
    }

    private static GameState AdvanceToDealerTurn(GameState state, int nextPi, int nextHi)
    {
        var provisional = state with { Phase = GamePhase.DealerTurn };
        var needWait    = !CanGoToPayout(provisional);
        return state with
        {
            Phase = GamePhase.DealerTurn,
            ActivePlayerIndex = nextPi,
            ActiveHandIndex = nextHi,
            WaitingForNextPlayer = false,
            WaitingForDealer = needWait,
        };
    }

    private static GameState HandleBeginDealerTurn(GameState state, NarrationContext ctx)
    {
        var t = ctx.Templates;
        if (!state.WaitingForDealer) return state;
        ctx.Narrate(t.DealerTurnStart,
            ("dealer", ctx.DealerName),
            ("cards", HandString(state.DealerHand.Cards)),
            ("score", ScoreString(state.DealerHand.Cards)));
        return state with { WaitingForDealer = false };
    }

    private static GameState HandleGoToPayout(GameState state, NarrationContext ctx)
    {
        var t = ctx.Templates;
        var dealerName = ctx.DealerName;
        ctx.Narrate(t.PayoutHeader);
        var dealerScore = HandValue(state.DealerHand.Cards);
        var dealerBust  = state.DealerHand.Cards.Length > 0 && dealerScore > 21;
        ctx.Narrate(dealerBust ? t.PayoutDealerBust : t.PayoutDealerStands,
            ("dealer", dealerName), ("score", dealerBust ? dealerScore.ToString() : ScoreString(state.DealerHand.Cards)));

        for (var pi = 0; pi < state.Players.Length; pi++)
        {
            if (state.Players[pi].SittingOut) continue;
            NarratePlayerPayout(state, pi, ctx);
        }

        var (winners, pushers) = ComputePayoutWinnersAndPushers(state);
        return state with { Phase = GamePhase.Payout, LastRoundWinners = winners, LastRoundPushers = pushers };
    }

    private static void NarratePlayerPayout(GameState state, int pi, NarrationContext ctx)
    {
        var t = ctx.Templates;
        var p = state.Players[pi];
        var multiHand = p.Hands.Length > 1;

        var allWin = multiHand && p.Hands
            .Select((_, hi) => GetPayoutResult(state, pi, hi))
            .All(r => r == PayoutResult.Win || r == PayoutResult.BjWin || r == PayoutResult.CharlieWin);
        if (allWin)
        {
            var total = 0m;
            for (var hi = 0; hi < p.Hands.Length; hi++)
            {
                var eb = GetEffectiveBet(p, p.Hands[hi]);
                total += GetPayoutResult(state, pi, hi) switch
                {
                    PayoutResult.BjWin      => Math.Ceiling(eb * (decimal)state.BjPayout),
                    PayoutResult.CharlieWin => Math.Ceiling(eb * CharlieMultiplier(state.CharliePayout)),
                    _                       => eb,
                };
            }
            var amtStr = total > 0 ? $"+{total:0.##}" : string.Empty;
            ctx.Narrate(t.PayoutSplitCombined,
                ("name",   p.DisplayName),
                ("amount", amtStr));
            return;
        }

        for (var hi = 0; hi < p.Hands.Length; hi++)
        {
            var result = GetPayoutResult(state, pi, hi);
            var template = result switch
            {
                PayoutResult.Win        => t.PayoutWin,
                PayoutResult.BjWin      => t.PayoutBjWin,
                PayoutResult.CharlieWin => t.PayoutCharlieWin,
                PayoutResult.Lose       => t.PayoutLose,
                PayoutResult.Push       => t.PayoutPush,
                _                       => null,
            };
            if (template == null) continue;

            var effectiveBet = GetEffectiveBet(p, p.Hands[hi]);
            var amount       = PayoutAmountString(state, pi, hi);
            var betStr       = effectiveBet > 0
                ? FormatGil(effectiveBet)
                : string.Empty;
            var amountStr    = amount;
            var name         = multiHand ? $"{p.DisplayName} (Hand {hi + 1})" : p.DisplayName;
            ctx.Narrate(template,
                ("name",   name),
                ("bet",    betStr),
                ("amount", amountStr));
        }
    }

    private static (HashSet<string> Winners, HashSet<string> Pushers) ComputePayoutWinnersAndPushers(GameState state)
    {
        var winners = new HashSet<string>(
            state.Players
                 .Where((p, pi) => p.Hands.Select((_, hi) => GetPayoutResult(state, pi, hi))
                                     .Any(r => r is PayoutResult.Win or PayoutResult.BjWin or PayoutResult.CharlieWin))
                 .Select(p => p.FullName.Length > 0 ? p.FullName : p.Nickname));
        var pushers = new HashSet<string>(
            state.Players
                 .Where((p, pi) => p.Hands.Select((_, hi) => GetPayoutResult(state, pi, hi))
                                     .Any(r => r == PayoutResult.Push)
                                 && !p.Hands.Select((_, hi) => GetPayoutResult(state, pi, hi))
                                     .Any(r => r is PayoutResult.Win or PayoutResult.BjWin or PayoutResult.CharlieWin))
                 .Select(p => p.FullName.Length > 0 ? p.FullName : p.Nickname));
        return (winners, pushers);
    }

    private static GameState HandleNewRound(GameState state) =>
        state with
        {
            Players = state.Players.Select(p => p with { Hands = [new Hand()] }).ToImmutableArray(),
            DealerHand        = new Hand(),
            Phase             = GamePhase.Betting,
            ActivePlayerIndex = -1,
            ActiveHandIndex   = -1,
        };

    private static GameState HandleAddPlayer(GameState state, AddPlayer a) =>
        state with { Players = [..state.Players, new Player { Nickname = a.Nickname, FullName = a.FullName, World = a.World, Hands = [new Hand()] }] };

    private static GameState HandleRemovePlayer(GameState state, RemovePlayer a)
    {
        var newPlayers = state.Players.Where((_, i) => i != a.Index).ToImmutableArray();
        var newActive  = state.ActivePlayerIndex >= newPlayers.Length
                             ? newPlayers.Length - 1
                             : state.ActivePlayerIndex;
        return state with { Players = newPlayers, ActivePlayerIndex = newActive };
    }

    private static GameState HandleSetPlayerBet(GameState state, SetPlayerBet a)
    {
        var p = state.Players[a.PlayerIndex];
        return state with { Players = WithPlayer(state.Players, a.PlayerIndex, p with { Bet = a.Bet }) };
    }

    private static GameState HandleAdjustBet(GameState state, AdjustBet a)
    {
        if (state.Phase != GamePhase.Deal) return state;
        var p = state.Players[a.PlayerIndex];
        if (p.SittingOut) return state;
        return state with { Players = WithPlayer(state.Players, a.PlayerIndex, p with { Bet = a.Bet }) };
    }

    private static GameState HandleRenamePlayer(GameState state, RenamePlayer a)
    {
        var p = state.Players[a.PlayerIndex];
        return state with { Players = WithPlayer(state.Players, a.PlayerIndex, p with { Nickname = a.Nickname }) };
    }

    private static GameState HandleToggleSittingOut(GameState state, ToggleSittingOut a)
    {
        if (state.Phase != GamePhase.Betting) return state;
        var p = state.Players[a.PlayerIndex];
        return state with { Players = WithPlayer(state.Players, a.PlayerIndex, p with { SittingOut = !p.SittingOut }) };
    }

    private static GameState HandleReorderPlayers(GameState state, ReorderPlayers a)
    {
        if (state.Phase != GamePhase.Betting) return state;
        var newPlayers = a.NewOrder.Select(i => state.Players[i]).ToImmutableArray();
        return state with { Players = newPlayers };
    }
}
