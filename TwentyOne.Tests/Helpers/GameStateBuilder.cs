using System.Collections.Generic;
using TwentyOne.Game;

namespace TwentyOne.Tests.Helpers;

/// <summary>
/// Fluent builder for assembling <see cref="GameState"/> instances in tests.
/// Replaces ad-hoc object-initialiser blocks that vary the same handful of
/// fields (phase, dealer cards, one or more players, active hand index).
/// Defaults match a freshly-instantiated GameState so chained methods only
/// have to set what differs.
/// </summary>
internal sealed class GameStateBuilder
{
    private GamePhase            _phase             = GamePhase.Betting;
    private List<int>            _dealerCards       = [];
    private HandState            _dealerState       = HandState.Playing;
    private readonly List<Player> _players          = [];
    private int                  _activePlayerIndex;
    private int                  _activeHandIndex;
    private FiveCardCharlieRule  _fiveCardCharlie       = FiveCardCharlieRule.Disabled;
    private PayoutRatio          _bjPayout              = PayoutRatio.ThreeToTwo;
    private PayoutRatio          _charliePayout         = PayoutRatio.EvenMoney;
    private bool                 _dealerStandsOnSoft17  = false;
    private bool                 _doubleAfterSplit      = true;
    private bool                 _hitSplitAces          = false;
    private bool                 _waitingForNextPlayer;
    private bool                 _waitingForDealer;
    private bool                 _skipDealSummaryOnePlayer = true;
    private HashSet<string>?     _lastRoundWinners;
    private HashSet<string>?     _lastRoundPushers;

    public GameStateBuilder Phase(GamePhase phase)
    {
        _phase = phase;
        return this;
    }

    /// <summary>Set dealer cards; defaults to <see cref="HandState.Playing"/>.</summary>
    public GameStateBuilder Dealer(params int[] cards)
    {
        _dealerCards = [..cards];
        _dealerState = HandState.Playing;
        return this;
    }

    /// <summary>Set dealer cards with explicit state (e.g. Stand, Bust, Blackjack).</summary>
    public GameStateBuilder Dealer(HandState state, params int[] cards)
    {
        _dealerCards = [..cards];
        _dealerState = state;
        return this;
    }

    /// <summary>
    /// Add a player with one Playing hand. Pass cards via the <paramref name="cards"/>
    /// array; bet defaults to "100" since most tests don't depend on the exact value.
    /// </summary>
    public GameStateBuilder Player(string nickname, params int[] cards) =>
        Player(nickname, "100", HandState.Playing, cards);

    public GameStateBuilder Player(string nickname, string bet, params int[] cards) =>
        Player(nickname, bet, HandState.Playing, cards);

    public GameStateBuilder Player(string nickname, HandState state, params int[] cards) =>
        Player(nickname, "100", state, cards);

    public GameStateBuilder Player(string nickname, string bet, HandState state, params int[] cards)
    {
        _players.Add(new Player
        {
            Nickname = nickname,
            Bet      = bet,
            Hands    = [new Hand { Cards = [..cards], State = state }],
        });
        return this;
    }

    /// <summary>Append an already-constructed Player (for tests that need full control).</summary>
    public GameStateBuilder Player(Player player)
    {
        _players.Add(player);
        return this;
    }

    public GameStateBuilder ActiveHand(int playerIndex, int handIndex = 0)
    {
        _activePlayerIndex = playerIndex;
        _activeHandIndex   = handIndex;
        return this;
    }

    public GameStateBuilder Charlie(FiveCardCharlieRule rule, PayoutRatio? payout = null)
    {
        _fiveCardCharlie = rule;
        if (payout.HasValue) _charliePayout = payout.Value;
        return this;
    }

    public GameStateBuilder BjPayout(PayoutRatio payout)
    {
        _bjPayout = payout;
        return this;
    }

    public GameStateBuilder DealerStandsOnSoft17(bool value = true)
    {
        _dealerStandsOnSoft17 = value;
        return this;
    }

    public GameStateBuilder DoubleAfterSplit(bool value)
    {
        _doubleAfterSplit = value;
        return this;
    }

    public GameStateBuilder HitSplitAces(bool value = true)
    {
        _hitSplitAces = value;
        return this;
    }

    public GameStateBuilder WaitingForNextPlayer(bool value = true)
    {
        _waitingForNextPlayer = value;
        return this;
    }

    public GameStateBuilder WaitingForDealer(bool value = true)
    {
        _waitingForDealer = value;
        return this;
    }

    public GameStateBuilder SkipDealSummaryOnePlayer(bool value = true)
    {
        _skipDealSummaryOnePlayer = value;
        return this;
    }

    public GameStateBuilder LastRoundWinners(params string[] names)
    {
        _lastRoundWinners = new HashSet<string>(names);
        return this;
    }

    public GameStateBuilder LastRoundPushers(params string[] names)
    {
        _lastRoundPushers = new HashSet<string>(names);
        return this;
    }

    public GameState Build()
    {
        var state = new GameState
        {
            Phase                    = _phase,
            DealerHand               = new Hand { Cards = [.._dealerCards], State = _dealerState },
            Players                  = [.._players],
            ActivePlayerIndex        = _activePlayerIndex,
            ActiveHandIndex          = _activeHandIndex,
            FiveCardCharlie          = _fiveCardCharlie,
            BjPayout                 = _bjPayout,
            CharliePayout            = _charliePayout,
            DealerStandsOnSoft17     = _dealerStandsOnSoft17,
            DoubleAfterSplit         = _doubleAfterSplit,
            HitSplitAces             = _hitSplitAces,
            WaitingForNextPlayer     = _waitingForNextPlayer,
            WaitingForDealer         = _waitingForDealer,
            SkipDealSummaryOnePlayer = _skipDealSummaryOnePlayer,
        };
        if (_lastRoundWinners is not null) state.LastRoundWinners = _lastRoundWinners;
        if (_lastRoundPushers is not null) state.LastRoundPushers = _lastRoundPushers;
        return state;
    }
}
