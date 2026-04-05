using System.Collections.Generic;
using TwentyOne.Game;
using Xunit;

namespace TwentyOne.Tests;

public class HandValueTests
{
    [Theory]
    [InlineData(new[] { 5, 7 },        12)]
    [InlineData(new[] { 10, 13 },      20)]  // K=10, 10=10
    [InlineData(new[] { 10, 11, 12 },  30)]  // all face cards
    [InlineData(new[] { 1, 5 },        16)]  // A+5 = soft 16 (counted as 11+5)
    [InlineData(new[] { 1, 10 },       21)]  // A+10 = blackjack
    [InlineData(new[] { 1, 1, 9 },     21)]  // A+A+9: one ace = 11, other = 1
    [InlineData(new[] { 1, 1, 1, 8 },  21)]  // three aces + 8
    [InlineData(new[] { 9, 7, 8 },     24)]  // bust
    [InlineData(new[] { 10, 6, 6 },    22)]  // bust
    public void HandValue_ReturnsCorrectTotal(int[] cards, int expected)
    {
        Assert.Equal(expected, GameEngine.HandValue(cards));
    }

    [Fact]
    public void HandValue_EmptyHand_ReturnsZero()
    {
        Assert.Equal(0, GameEngine.HandValue([]));
    }

    [Theory]
    [InlineData(new[] { 1, 6 },   true)]   // soft 17
    [InlineData(new[] { 7, 10 },  false)]  // hard 17
    [InlineData(new[] { 1, 1 },   true)]   // A+A: low=2, high=12 → soft
    public void IsSoft_DetectsSoftHands(int[] cards, bool expected)
    {
        Assert.Equal(expected, GameEngine.IsSoft(cards));
    }
}

public class ComputeHandStateTests
{
    [Fact]
    public void Stand_IsPreserved()
    {
        var state = GameEngine.ComputeHandState([5, 10], HandState.Stand);
        Assert.Equal(HandState.Stand, state);
    }

    [Fact]
    public void TwoCardsValuing21_IsBlackjack()
    {
        var state = GameEngine.ComputeHandState([1, 10], HandState.Playing);
        Assert.Equal(HandState.Blackjack, state);
    }

    [Fact]
    public void ThreeCardsValuing21_IsPlaying()
    {
        var state = GameEngine.ComputeHandState([7, 7, 7], HandState.Playing);
        Assert.Equal(HandState.Playing, state);
    }

    [Fact]
    public void Over21_IsBust()
    {
        var state = GameEngine.ComputeHandState([10, 10, 5], HandState.Playing);
        Assert.Equal(HandState.Bust, state);
    }
}

public class ScoreStringTests
{
    [Fact]
    public void SoftHand_ShowsBothValues()
    {
        Assert.Equal("6/16", GameEngine.ScoreString([1, 5]));
    }

    [Fact]
    public void HardHand_ShowsSingleValue()
    {
        Assert.Equal("17", GameEngine.ScoreString([10, 7]));
    }

    [Fact]
    public void BustHand_ShowsBustTotal()
    {
        Assert.Equal("22", GameEngine.ScoreString([10, 10, 2]));
    }
}

public class DealerRecommendationTests
{
    private static Hand MakeHand(params int[] cards) =>
        new Hand { Cards = [..cards], State = HandState.Playing };

    [Fact]
    public void Under17_ReturnsHit()
    {
        Assert.Equal("HIT", GameEngine.DealerRecommendation(MakeHand(10, 6)));
    }

    [Fact]
    public void Hard17_ReturnsStand()
    {
        Assert.Equal("STAND", GameEngine.DealerRecommendation(MakeHand(10, 7)));
    }

    [Fact]
    public void Soft17_ReturnsHit()
    {
        // A+6 = soft 17 → dealer must hit
        Assert.Equal("HIT", GameEngine.DealerRecommendation(MakeHand(1, 6)));
    }

    [Fact]
    public void Soft18_ReturnsStand()
    {
        Assert.Equal("STAND", GameEngine.DealerRecommendation(MakeHand(1, 7)));
    }

    [Fact]
    public void EmptyHand_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, GameEngine.DealerRecommendation(new Hand()));
    }
}

public class ApplyAddDealerCardTests
{
    private static GameState DealerTurnState() => new GameState
    {
        Phase      = GamePhase.DealerTurn,
        DealerHand = new Hand { Cards = [10], State = HandState.Playing },
        Players    = [new Player { Name = "Lorah", Bet = "10", Hands = [new Hand { Cards = [5, 8], State = HandState.Playing }] }],
    };

    [Fact]
    public void AddDealerCard_DealerTurn_NarratesNormalDraw()
    {
        var (newState, effects) = GameEngine.Apply(DealerTurnState(), new AddDealerCard(7));
        Assert.Single(effects);
        Assert.Contains("Dealer draws 7", ((SendChat)effects[0]).Text);
        Assert.Equal(2, newState.DealerHand.Cards.Count);
    }

    [Fact]
    public void AddDealerCard_DealerTurn_NarratesBust()
    {
        var state = new GameState
        {
            Phase      = GamePhase.DealerTurn,
            DealerHand = new Hand { Cards = [10, 8], State = HandState.Playing },
        };
        var (_, effects) = GameEngine.Apply(state, new AddDealerCard(5));
        Assert.Contains("Bust", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AddDealerCard_DealPhase_NarratesDealerCard()
    {
        var state = new GameState { Phase = GamePhase.Deal };
        var (_, effects) = GameEngine.Apply(state, new AddDealerCard(10));
        Assert.Single(effects);
        Assert.Equal("Dealer's Card:", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AddDealerCard_DoesNotMutateInput()
    {
        var state      = DealerTurnState();
        var origCount  = state.DealerHand.Cards.Count;
        GameEngine.Apply(state, new AddDealerCard(7));
        Assert.Equal(origCount, state.DealerHand.Cards.Count);
    }
}

public class ApplyAddPlayerCardTests
{
    private static GameState ActivePlayerState(int activeIndex = 0) => new GameState
    {
        Phase             = GamePhase.PlayerTurns,
        ActivePlayerIndex = activeIndex,
        Players =
        [
            new Player { Name = "Lorah", Hands = [new Hand { Cards = [5, 6], State = HandState.Playing }] },
            new Player { Name = "Bekki",   Hands = [new Hand { Cards = [10, 8], State = HandState.Playing }] },
        ],
    };

    [Fact]
    public void AddPlayerCard_NarratesHit()
    {
        var (_, effects) = GameEngine.Apply(ActivePlayerState(), new AddPlayerCard(0, 0, 3));
        Assert.Single(effects);
        Assert.Contains("Lorah hits", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AddPlayerCard_Bust_NarratesBustAndAdvances()
    {
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            ActivePlayerIndex = 0,
            Players =
            [
                new Player { Name = "Lorah", Hands = [new Hand { Cards = [10, 8], State = HandState.Playing }] },
                new Player { Name = "Bekki",   Hands = [new Hand { Cards = [5, 6],  State = HandState.Playing }] },
            ],
        };
        var (newState, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 7));
        Assert.Contains("busts", ((SendChat)effects[0]).Text);
        Assert.Equal(1, newState.ActivePlayerIndex); // advanced to Bekki
        Assert.Equal(GamePhase.PlayerTurns, newState.Phase);
    }

    [Fact]
    public void AddPlayerCard_LastPlayerBusts_SkipsToPayout()
    {
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            ActivePlayerIndex = 0,
            Players =
            [
                new Player { Name = "Lorah", Hands = [new Hand { Cards = [10, 8], State = HandState.Playing }] },
            ],
        };
        var (newState, _) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 7));
        Assert.Equal(GamePhase.Payout, newState.Phase);
        Assert.Equal(-1, newState.ActivePlayerIndex);
    }

    [Fact]
    public void AddPlayerCard_AllBustExceptStanding_TransitionsToDealerTurn()
    {
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            ActivePlayerIndex = 0,
            Players =
            [
                new Player { Name = "Lorah", Hands = [new Hand { Cards = [10, 8], State = HandState.Playing }] },
                new Player { Name = "Bekki", Hands = [new Hand { Cards = [10, 7], State = HandState.Stand }] },
            ],
        };
        var (newState, _) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 7));
        Assert.Equal(GamePhase.DealerTurn, newState.Phase);
    }

    [Fact]
    public void AddPlayerCard_DealPhase_FirstCard_NarratesPlayerHand()
    {
        var state = new GameState
        {
            Phase             = GamePhase.Deal,
            ActivePlayerIndex = -1,
            Players           = [new Player { Name = "Lorah", Hands = [new Hand()] }],
        };
        var (newState, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 5));
        Assert.Single(effects);
        Assert.Equal("Lorah's Hand:", ((SendChat)effects[0]).Text);
        Assert.Equal(GamePhase.Deal, newState.Phase);
    }

    [Fact]
    public void AddPlayerCard_DealPhase_SecondCard_NoNarration()
    {
        var state = new GameState
        {
            Phase             = GamePhase.Deal,
            ActivePlayerIndex = -1,
            Players           = [new Player { Name = "Lorah", Hands = [new Hand { Cards = [5], State = HandState.Playing }] }],
        };
        var (_, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 7));
        Assert.Empty(effects);
    }
}

public class ApplyStandPlayerTests
{
    [Fact]
    public void Stand_NarratesAndAdvancesToNextPlayer()
    {
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            ActivePlayerIndex = 0,
            Players =
            [
                new Player { Name = "Lorah", Hands = [new Hand { Cards = [10, 7], State = HandState.Playing }] },
                new Player { Name = "Bekki",   Hands = [new Hand { Cards = [9, 8],  State = HandState.Playing }] },
            ],
        };
        var (newState, effects) = GameEngine.Apply(state, new StandPlayer(0, 0));
        Assert.Contains("Lorah stands", ((SendChat)effects[0]).Text);
        Assert.Equal(HandState.Stand, newState.Players[0].Hands[0].State);
        Assert.Equal(1, newState.ActivePlayerIndex);
    }

    [Fact]
    public void Stand_LastPlayer_TransitionsToDealerTurn()
    {
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            ActivePlayerIndex = 0,
            Players           = [new Player { Name = "Lorah", Hands = [new Hand { Cards = [10, 7], State = HandState.Playing }] }],
        };
        var (newState, _) = GameEngine.Apply(state, new StandPlayer(0, 0));
        Assert.Equal(GamePhase.DealerTurn, newState.Phase);
    }

    [Fact]
    public void Stand_AlreadyStood_NoChange()
    {
        var state = new GameState
        {
            Phase   = GamePhase.PlayerTurns,
            Players = [new Player { Name = "Lorah", Hands = [new Hand { Cards = [10, 7], State = HandState.Stand }] }],
        };
        var (newState, effects) = GameEngine.Apply(state, new StandPlayer(0, 0));
        Assert.Same(state, newState);
        Assert.Empty(effects);
    }
}

public class ApplyPhaseTransitionTests
{
    [Fact]
    public void StartDeal_TransitionsToDeal()
    {
        var state    = new GameState { Phase = GamePhase.Betting };
        var (ns, _)  = GameEngine.Apply(state, new StartDeal());
        Assert.Equal(GamePhase.Deal, ns.Phase);
    }

    [Fact]
    public void BeginPlayerTurns_NarratesDealSummary_SetsFirstActivePlayer()
    {
        var state = new GameState
        {
            Phase      = GamePhase.Deal,
            DealerHand = new Hand { Cards = [10], State = HandState.Playing },
            Players =
            [
                new Player { Name = "Lorah", Hands = [new Hand { Cards = [5, 8],  State = HandState.Playing }] },
                new Player { Name = "Bekki",   Hands = [new Hand { Cards = [10, 9], State = HandState.Playing }] },
            ],
        };
        var (newState, effects) = GameEngine.Apply(state, new BeginPlayerTurns());
        Assert.Equal(2, effects.Count);
        Assert.Contains("Deal —", ((SendChat)effects[0]).Text);
        Assert.Contains("Lorah's turn", ((SendChat)effects[1]).Text);
        Assert.Equal(0, newState.ActivePlayerIndex);
        Assert.Equal(GamePhase.PlayerTurns, newState.Phase);
    }

    [Fact]
    public void BeginPlayerTurns_AllBlackjacks_TransitionsToDealerTurn()
    {
        var state = new GameState
        {
            Phase      = GamePhase.Deal,
            DealerHand = new Hand { Cards = [10], State = HandState.Playing },
            Players =
            [
                new Player { Name = "Lorah", Hands = [new Hand { Cards = [1, 10], State = HandState.Blackjack }] },
            ],
        };
        var (newState, _) = GameEngine.Apply(state, new BeginPlayerTurns());
        Assert.Equal(GamePhase.DealerTurn, newState.Phase);
    }

    [Fact]
    public void NewRound_ResetsHandsKeepsPlayers()
    {
        var state = new GameState
        {
            Phase  = GamePhase.Payout,
            Players =
            [
                new Player { Name = "Lorah", Bet = "10", Hands = [new Hand { Cards = [5, 8], State = HandState.Stand }] },
            ],
            DealerHand = new Hand { Cards = [10, 7], State = HandState.Stand },
        };
        var (newState, _) = GameEngine.Apply(state, new NewRound());
        Assert.Equal(GamePhase.Betting, newState.Phase);
        Assert.Equal("Lorah", newState.Players[0].Name);
        Assert.Equal("10", newState.Players[0].Bet);
        Assert.Empty(newState.Players[0].Hands[0].Cards);
        Assert.Empty(newState.DealerHand.Cards);
    }
}

public class PayoutTests
{
    private static GameState PayoutState(int[] playerCards, HandState playerState,
        int[] dealerCards, HandState dealerState = HandState.Stand) => new GameState
    {
        Phase      = GamePhase.Payout,
        DealerHand = new Hand { Cards = [..dealerCards], State = dealerState },
        Players    = [new Player { Name = "Lorah", Bet = "100", Hands =
            [new Hand { Cards = [..playerCards], State = playerState }] }],
    };

    [Fact]
    public void PlayerBusts_Loses()
    {
        var state = PayoutState([10, 9, 5], HandState.Bust, [10, 7]);
        Assert.Equal(PayoutResult.Lose, GameEngine.GetPayoutResult(state, 0));
    }

    [Fact]
    public void PlayerBeatsDealer_Wins()
    {
        var state = PayoutState([10, 9], HandState.Stand, [10, 7]);
        Assert.Equal(PayoutResult.Win, GameEngine.GetPayoutResult(state, 0));
    }

    [Fact]
    public void DealerBusts_PlayerWins()
    {
        var state = PayoutState([10, 6], HandState.Stand, [10, 8, 7], HandState.Bust);
        Assert.Equal(PayoutResult.Win, GameEngine.GetPayoutResult(state, 0));
    }

    [Fact]
    public void Push_EqualScores()
    {
        var state = PayoutState([10, 7], HandState.Stand, [9, 8]);
        Assert.Equal(PayoutResult.Push, GameEngine.GetPayoutResult(state, 0));
    }

    [Fact]
    public void PlayerBlackjack_DealerNo_BjWin()
    {
        var state = PayoutState([1, 10], HandState.Blackjack, [10, 7]);
        Assert.Equal(PayoutResult.BjWin, GameEngine.GetPayoutResult(state, 0));
    }

    [Fact]
    public void BothBlackjack_Push()
    {
        var state = PayoutState([1, 10], HandState.Blackjack, [1, 10]);
        Assert.Equal(PayoutResult.Push, GameEngine.GetPayoutResult(state, 0));
    }

    [Theory]
    [InlineData(BlackjackPayout.ThreeToTwo, "+150")]   // 100 * 1.5 = 150
    [InlineData(BlackjackPayout.SixToFive,  "+120")]   // 100 * 1.2 = 120
    [InlineData(BlackjackPayout.EvenMoney,  "+100")]   // 100 * 1.0 = 100
    public void BjPayoutAmounts(BlackjackPayout payout, string expected)
    {
        var state = new GameState
        {
            Phase    = GamePhase.Payout,
            BjPayout = payout,
            DealerHand = new Hand { Cards = [10, 7], State = HandState.Stand },
            Players    = [new Player { Name = "Lorah", Bet = "100", Hands =
                [new Hand { Cards = [1, 10], State = HandState.Blackjack }] }],
        };
        Assert.Equal(expected, GameEngine.PayoutAmountString(state, 0));
    }

    [Fact]
    public void RegularWin_ReturnsBetAmount()
    {
        var state = PayoutState([10, 9], HandState.Stand, [10, 7]);
        state.BjPayout = BlackjackPayout.ThreeToTwo;
        Assert.Equal("+100", GameEngine.PayoutAmountString(state, 0));
    }
}

public class RosterManagementTests
{
    private static GameState BettingState() => new GameState { Phase = GamePhase.Betting };

    [Fact]
    public void AddPlayer_AppendsPlayer()
    {
        var (ns, _) = GameEngine.Apply(BettingState(), new AddPlayer("Lorah"));
        Assert.Single(ns.Players);
        Assert.Equal("Lorah", ns.Players[0].Name);
    }

    [Fact]
    public void RemovePlayer_RemovesCorrectIndex()
    {
        var state = new GameState
        {
            Phase   = GamePhase.Betting,
            Players = [new Player { Name = "Lorah" }, new Player { Name = "Bekki" }],
        };
        var (ns, _) = GameEngine.Apply(state, new RemovePlayer(0));
        Assert.Single(ns.Players);
        Assert.Equal("Bekki", ns.Players[0].Name);
    }

    [Fact]
    public void SetPlayerBet_UpdatesBet()
    {
        var state = new GameState
        {
            Phase   = GamePhase.Betting,
            Players = [new Player { Name = "Lorah", Bet = "10" }],
        };
        var (ns, _) = GameEngine.Apply(state, new SetPlayerBet(0, "50"));
        Assert.Equal("50", ns.Players[0].Bet);
    }

    [Fact]
    public void RenamePlayer_UpdatesName()
    {
        var state = new GameState
        {
            Players = [new Player { Name = "Lorah", Bet = "10" }],
        };
        var (ns, _) = GameEngine.Apply(state, new RenamePlayer(0, "Nolla"));
        Assert.Equal("Nolla", ns.Players[0].Name);
        Assert.Equal("10", ns.Players[0].Bet); // bet preserved
    }
}

public class NarrationTemplateTests
{
    private static GameState PlayerTurnsState() => new()
    {
        Phase             = GamePhase.PlayerTurns,
        ActivePlayerIndex = 0,
        DealerHand        = new Hand { Cards = [10], State = HandState.Playing },
        Players           =
        [
            new Player { Name = "Lorah", Bet = "50", Hands = [new Hand { Cards = [5, 6], State = HandState.Playing }] },
        ],
    };

    [Fact]
    public void CustomPlayerHit_UsesTemplate()
    {
        var t = new NarrationTemplates { PlayerHit = "CUSTOM {name} drew {card}" };
        var (_, effects) = GameEngine.Apply(PlayerTurnsState(), new AddPlayerCard(0, 0, 3), t);
        Assert.Equal("CUSTOM Lorah drew 3", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void CustomPlayerBust_UsesTemplate()
    {
        var t = new NarrationTemplates { PlayerBust = "OUT: {name}" };
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            ActivePlayerIndex = 0,
            Players           = [new Player { Name = "Lorah", Hands = [new Hand { Cards = [10, 8], State = HandState.Playing }] }],
        };
        var (_, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 7), t);
        Assert.Equal("OUT: Lorah", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void CustomDealerHit_UsesTemplate()
    {
        var t = new NarrationTemplates { DealerHit = "D+{card}={score}" };
        var state = new GameState
        {
            Phase      = GamePhase.DealerTurn,
            DealerHand = new Hand { Cards = [10], State = HandState.Playing },
        };
        var (_, effects) = GameEngine.Apply(state, new AddDealerCard(7), t);
        Assert.Equal("D+7=17", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void CustomPlayerStand_UsesTemplate()
    {
        var t = new NarrationTemplates { PlayerStand = "{name} done ({score})" };
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            ActivePlayerIndex = 0,
            Players           = [new Player { Name = "Lorah", Hands = [new Hand { Cards = [10, 7], State = HandState.Playing }] }],
        };
        var (_, effects) = GameEngine.Apply(state, new StandPlayer(0, 0), t);
        Assert.Equal("Lorah done (17)", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void CustomDealSummaryPrefix_UsesTemplate()
    {
        var t = new NarrationTemplates { DealSummaryPrefix = "DEALT: ", DealSummaryDealer = "" };
        var state = new GameState
        {
            Phase      = GamePhase.Deal,
            DealerHand = new Hand { Cards = [10], State = HandState.Playing },
            Players    = [new Player { Name = "Lorah", Hands = [new Hand { Cards = [5, 8], State = HandState.Playing }] }],
        };
        var (_, effects) = GameEngine.Apply(state, new BeginPlayerTurns(), t);
        Assert.StartsWith("DEALT: ", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void CustomPayoutPlayer_UsesTemplate()
    {
        var t = new NarrationTemplates { PayoutPlayer = "{name} {result}", PayoutDealerStands = "D" };
        var state = new GameState
        {
            Phase      = GamePhase.DealerTurn,
            DealerHand = new Hand { Cards = [10, 7], State = HandState.Stand },
            Players    = [new Player { Name = "Lorah", Bet = "100", Hands = [new Hand { Cards = [10, 9], State = HandState.Stand }] }],
        };
        var (_, effects) = GameEngine.Apply(state, new GoToPayout(), t);
        // effects[0] = dealer line, effects[1] = player line
        Assert.Equal("Lorah Win", ((SendChat)effects[1]).Text);
    }

    [Fact]
    public void NullTemplates_UsesDefaults()
    {
        var (_, effects) = GameEngine.Apply(PlayerTurnsState(), new AddPlayerCard(0, 0, 3));
        Assert.Contains("Lorah hits", ((SendChat)effects[0]).Text);
    }
}

public class ImmutabilityTests
{
    [Fact]
    public void Apply_NeverMutatesInputState()
    {
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            ActivePlayerIndex = 0,
            Players =
            [
                new Player { Name = "Lorah", Hands = [new Hand { Cards = [5, 6], State = HandState.Playing }] },
            ],
        };
        var originalCardCount = state.Players[0].Hands[0].Cards.Count;

        GameEngine.Apply(state, new AddPlayerCard(0, 0, 7));

        Assert.Equal(originalCardCount, state.Players[0].Hands[0].Cards.Count);
        Assert.Equal(GamePhase.PlayerTurns, state.Phase);
    }
}

public class CanGoToPayoutTests
{
    private static Player BjPlayer(string name) =>
        new() { Name = name, Hands = [new Hand { Cards = [1, 10], State = HandState.Blackjack }] };

    [Fact]
    public void AllBJ_DealerUpCardNotTenValue_CanPayout()
    {
        var state = new GameState
        {
            Phase      = GamePhase.DealerTurn,
            Players    = [BjPlayer("Lorah")],
            DealerHand = new Hand { Cards = [7] },
        };
        Assert.True(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void AllBJ_DealerUpCardAce_NeedHoleCard()
    {
        var state = new GameState
        {
            Phase      = GamePhase.DealerTurn,
            Players    = [BjPlayer("Lorah")],
            DealerHand = new Hand { Cards = [1] },
        };
        Assert.False(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void AllBJ_DealerUpCardTenValue_NeedHoleCard()
    {
        var state = new GameState
        {
            Phase      = GamePhase.DealerTurn,
            Players    = [BjPlayer("Lorah")],
            DealerHand = new Hand { Cards = [13] },
        };
        Assert.False(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void AllBJ_DealerHasTwoCards_CanPayout()
    {
        var state = new GameState
        {
            Phase      = GamePhase.DealerTurn,
            Players    = [BjPlayer("Lorah")],
            DealerHand = new Hand { Cards = [1, 10] },
        };
        Assert.True(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void NormalPlay_DealerMustStand_CanPayout()
    {
        var state = new GameState
        {
            Phase      = GamePhase.DealerTurn,
            Players    = [new Player { Name = "Lorah", Hands = [new Hand { Cards = [10, 8], State = HandState.Stand }] }],
            DealerHand = new Hand { Cards = [10, 8] },
        };
        Assert.True(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void NormalPlay_DealerMustHit_CannotPayout()
    {
        var state = new GameState
        {
            Phase      = GamePhase.DealerTurn,
            Players    = [new Player { Name = "Lorah", Hands = [new Hand { Cards = [10, 8], State = HandState.Stand }] }],
            DealerHand = new Hand { Cards = [10, 6] },
        };
        Assert.False(GameEngine.CanGoToPayout(state));
    }
}
