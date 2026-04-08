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
        Players    = [new Player { Nickname = "Lorah", Bet = "10", Hands = [new Hand { Cards = [5, 8], State = HandState.Playing }] }],
    };

    [Fact]
    public void AddDealerCard_DealerTurn_NarratesNormalDraw()
    {
        // Draw a 4 → total 14, dealer must still hit — only one narration line
        var (newState, effects) = GameEngine.Apply(DealerTurnState(), new AddDealerCard(4));
        Assert.Single(effects);
        Assert.Contains("Dealer draws 4", ((SendChat)effects[0]).Text);
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
    public void AddDealerCard_DealPhase_NoNarration()
    {
        var state = new GameState { Phase = GamePhase.Deal };
        var (_, effects) = GameEngine.Apply(state, new AddDealerCard(10));
        Assert.Empty(effects);
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
        ActiveHandIndex   = 0,
        Players =
        [
            new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [5, 6], State = HandState.Playing }] },
            new Player { Nickname = "Bekki",   Hands = [new Hand { Cards = [10, 8], State = HandState.Playing }] },
        ],
    };

    [Fact]
    public void AddPlayerCard_NarratesHit()
    {
        var (_, effects) = GameEngine.Apply(ActivePlayerState(), new AddPlayerCard(0, 0, 3));
        Assert.Equal(2, effects.Count);
        Assert.Contains("Lorah hits", ((SendChat)effects[0]).Text);
        Assert.Contains("Lorah", ((SendChat)effects[1]).Text);
    }

    [Fact]
    public void AddPlayerCard_Bust_NarratesBustAndAdvances()
    {
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            ActivePlayerIndex = 0,
            ActiveHandIndex   = 0,
            Players =
            [
                new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [10, 8], State = HandState.Playing }] },
                new Player { Nickname = "Bekki",   Hands = [new Hand { Cards = [5, 6],  State = HandState.Playing }] },
            ],
        };
        var (newState, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 7));
        Assert.Contains("busts", ((SendChat)effects[0]).Text);
        Assert.True(newState.WaitingForNextPlayer);
        Assert.Equal(0, newState.ActivePlayerIndex); // stays on Lorah until Next Player clicked

        var (advState, _) = GameEngine.Apply(newState, new AdvanceToNextPlayer());
        Assert.Equal(1, advState.ActivePlayerIndex);
        Assert.Equal(GamePhase.PlayerTurns, advState.Phase);
    }

    [Fact]
    public void AddPlayerCard_LastPlayerBusts_SkipsToPayout()
    {
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            ActivePlayerIndex = 0,
            ActiveHandIndex   = 0,
            Players =
            [
                new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [10, 8], State = HandState.Playing }] },
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
            ActiveHandIndex   = 0,
            Players =
            [
                new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [10, 8], State = HandState.Playing }] },
                new Player { Nickname = "Bekki", Hands = [new Hand { Cards = [10, 7], State = HandState.Stand }] },
            ],
        };
        var (newState, _) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 7));
        Assert.Equal(GamePhase.DealerTurn, newState.Phase);
    }

    [Fact]
    public void AddPlayerCard_DealPhase_NoNarration()
    {
        var state = new GameState
        {
            Phase             = GamePhase.Deal,
            ActivePlayerIndex = -1,
            Players           = [new Player { Nickname = "Lorah", Hands = [new Hand()] }],
        };
        var (newState, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 5));
        Assert.Empty(effects);
        Assert.Equal(GamePhase.Deal, newState.Phase);
    }
}

public class ApplyAnnounceTests
{
    [Fact]
    public void AnnounceDealerDeal_EmitsNarrationNoStateChange()
    {
        var state = new GameState { Phase = GamePhase.Deal };
        var (newState, effects) = GameEngine.Apply(state, new AnnounceDealerDeal());
        Assert.Single(effects);
        Assert.Equal("Dealer's Card:", ((SendChat)effects[0]).Text);
        Assert.Equal(GamePhase.Deal, newState.Phase);
    }

    [Fact]
    public void AnnouncePlayerDeal_EmitsPlayerNameNarration()
    {
        var state = new GameState
        {
            Phase   = GamePhase.Deal,
            Players = [new Player { Nickname = "Lorah", Hands = [new Hand()] }],
        };
        var (newState, effects) = GameEngine.Apply(state, new AnnouncePlayerDeal(0));
        Assert.Single(effects);
        Assert.Equal("Lorah's Hand:", ((SendChat)effects[0]).Text);
        Assert.Equal(GamePhase.Deal, newState.Phase);
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
            ActiveHandIndex   = 0,
            Players =
            [
                new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [10, 7], State = HandState.Playing }] },
                new Player { Nickname = "Bekki",   Hands = [new Hand { Cards = [9, 8],  State = HandState.Playing }] },
            ],
        };
        var (newState, effects) = GameEngine.Apply(state, new StandPlayer(0, 0));
        Assert.Contains("Lorah stands", ((SendChat)effects[0]).Text);
        Assert.Equal(HandState.Stand, newState.Players[0].Hands[0].State);
        Assert.True(newState.WaitingForNextPlayer);
        Assert.Equal(0, newState.ActivePlayerIndex); // stays on Lorah until Next Player clicked

        var (advState, _) = GameEngine.Apply(newState, new AdvanceToNextPlayer());
        Assert.Equal(1, advState.ActivePlayerIndex);
    }

    [Fact]
    public void Stand_LastPlayer_TransitionsToDealerTurn()
    {
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            ActivePlayerIndex = 0,
            ActiveHandIndex   = 0,
            Players           = [new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [10, 7], State = HandState.Playing }] }],
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
            Players = [new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [10, 7], State = HandState.Stand }] }],
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
    public void AddPlayerCard_CompletingDeal_NarratesDealSummary()
    {
        // Last card dealt (Bekki's 2nd card) completes the deal — summary fires here.
        var state = new GameState
        {
            Phase      = GamePhase.Deal,
            DealerHand = new Hand { Cards = [10], State = HandState.Playing },
            Players =
            [
                new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [5, 8],  State = HandState.Playing }] },
                new Player { Nickname = "Bekki",   Hands = [new Hand { Cards = [10], State = HandState.Playing }] },
            ],
        };
        var (_, effects) = GameEngine.Apply(state, new AddPlayerCard(1, 0, 9));
        Assert.Single(effects);
        Assert.Contains("Deal —", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void BeginPlayerTurns_SetsFirstActivePlayer()
    {
        var state = new GameState
        {
            Phase      = GamePhase.Deal,
            DealerHand = new Hand { Cards = [10], State = HandState.Playing },
            Players =
            [
                new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [5, 8],  State = HandState.Playing }] },
                new Player { Nickname = "Bekki",   Hands = [new Hand { Cards = [10, 9], State = HandState.Playing }] },
            ],
        };
        var (newState, effects) = GameEngine.Apply(state, new BeginPlayerTurns());
        Assert.Single(effects);
        Assert.Contains("Lorah's turn", ((SendChat)effects[0]).Text);
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
                new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [1, 10], State = HandState.Blackjack }] },
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
                new Player { Nickname = "Lorah", Bet = "10", Hands = [new Hand { Cards = [5, 8], State = HandState.Stand }] },
            ],
            DealerHand = new Hand { Cards = [10, 7], State = HandState.Stand },
        };
        var (newState, _) = GameEngine.Apply(state, new NewRound());
        Assert.Equal(GamePhase.Betting, newState.Phase);
        Assert.Equal("Lorah", newState.Players[0].Nickname);
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
        Players    = [new Player { Nickname = "Lorah", Bet = "100", Hands =
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
            Players    = [new Player { Nickname = "Lorah", Bet = "100", Hands =
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
        Assert.Equal("Lorah", ns.Players[0].Nickname);
    }

    [Fact]
    public void RemovePlayer_RemovesCorrectIndex()
    {
        var state = new GameState
        {
            Phase   = GamePhase.Betting,
            Players = [new Player { Nickname = "Lorah" }, new Player { Nickname = "Bekki" }],
        };
        var (ns, _) = GameEngine.Apply(state, new RemovePlayer(0));
        Assert.Single(ns.Players);
        Assert.Equal("Bekki", ns.Players[0].Nickname);
    }

    [Fact]
    public void SetPlayerBet_UpdatesBet()
    {
        var state = new GameState
        {
            Phase   = GamePhase.Betting,
            Players = [new Player { Nickname = "Lorah", Bet = "10" }],
        };
        var (ns, _) = GameEngine.Apply(state, new SetPlayerBet(0, "50"));
        Assert.Equal("50", ns.Players[0].Bet);
    }

    [Fact]
    public void RenamePlayer_UpdatesNickname()
    {
        var state = new GameState
        {
            Players = [new Player { Nickname = "Lorah", Bet = "10" }],
        };
        var (ns, _) = GameEngine.Apply(state, new RenamePlayer(0, "Nolla"));
        Assert.Equal("Nolla", ns.Players[0].Nickname);
        Assert.Equal("Nolla", ns.Players[0].DisplayName);
        Assert.Equal("10", ns.Players[0].Bet); // bet preserved
    }

    [Fact]
    public void RenamePlayer_PreservesFullNameAndWorld()
    {
        var state = new GameState
        {
            Players = [new Player { FullName = "Lorah Banehene", World = "Adamantoise", Bet = "50" }],
        };
        var (ns, _) = GameEngine.Apply(state, new RenamePlayer(0, "Lory"));
        Assert.Equal("Lory", ns.Players[0].Nickname);
        Assert.Equal("Lory", ns.Players[0].DisplayName);
        Assert.Equal("Lorah Banehene", ns.Players[0].FullName);
        Assert.Equal("Adamantoise", ns.Players[0].World);
    }

    [Fact]
    public void RenamePlayer_ClearNickname_RevealsFfxivFirstName()
    {
        var state = new GameState
        {
            Players = [new Player { Nickname = "Lory", FullName = "Lorah Banehene", World = "Adamantoise" }],
        };
        var (ns, _) = GameEngine.Apply(state, new RenamePlayer(0, ""));
        Assert.Equal("", ns.Players[0].Nickname);
        Assert.Equal("Lorah", ns.Players[0].DisplayName);
    }

    [Fact]
    public void DisplayName_FfxivPlayer_ShowsFirstNameWhenNoNickname()
    {
        var p = new Player { FullName = "Lorah Banehene", World = "Adamantoise" };
        Assert.Equal("Lorah", p.DisplayName);
    }
}

public class NarrationTemplateTests
{
    private static GameState PlayerTurnsState() => new()
    {
        Phase             = GamePhase.PlayerTurns,
        ActivePlayerIndex = 0,
        ActiveHandIndex   = 0,
        DealerHand        = new Hand { Cards = [10], State = HandState.Playing },
        Players           =
        [
            new Player { Nickname = "Lorah", Bet = "50", Hands = [new Hand { Cards = [5, 6], State = HandState.Playing }] },
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
            ActiveHandIndex   = 0,
            Players           = [new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [10, 8], State = HandState.Playing }] }],
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
    public void DealerStand_NarratedAfterFinalHit()
    {
        var t = new NarrationTemplates { DealerStand = "DS:{cards}={score}" };
        var state = new GameState
        {
            Phase      = GamePhase.DealerTurn,
            DealerHand = new Hand { Cards = [10], State = HandState.Playing },
        };
        var (_, effects) = GameEngine.Apply(state, new AddDealerCard(7), t);
        // effects[0] = DealerHit, effects[1] = DealerStand
        Assert.Equal(2, effects.Count);
        Assert.Equal("DS:10 7=17", ((SendChat)effects[1]).Text);
    }

    [Fact]
    public void CustomPlayerStand_UsesTemplate()
    {
        var t = new NarrationTemplates { PlayerStand = "{name} done ({score})" };
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            ActivePlayerIndex = 0,
            ActiveHandIndex   = 0,
            Players           = [new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [10, 7], State = HandState.Playing }] }],
        };
        var (_, effects) = GameEngine.Apply(state, new StandPlayer(0, 0), t);
        Assert.Equal("Lorah done (17)", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void CustomDealSummaryPrefix_UsesTemplate()
    {
        var t = new NarrationTemplates { DealSummaryPrefix = "DEALT: ", DealSummaryDealer = "" };
        // Deal is incomplete: Lorah has 1 card. Adding 2nd card completes the deal.
        var state = new GameState
        {
            Phase      = GamePhase.Deal,
            DealerHand = new Hand { Cards = [10], State = HandState.Playing },
            Players    = [new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [5], State = HandState.Playing }] }],
        };
        var (_, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 8), t);
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
            Players    = [new Player { Nickname = "Lorah", Bet = "100", Hands = [new Hand { Cards = [10, 9], State = HandState.Stand }] }],
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

    [Fact]
    public void CustomPlayerAfterHit_UsesTemplate()
    {
        var t = new NarrationTemplates { PlayerAfterHit = "SCORE:{score} DO:{actions}" };
        var (_, effects) = GameEngine.Apply(PlayerTurnsState(), new AddPlayerCard(0, 0, 3), t);
        // effects[0] = PlayerHit, effects[1] = PlayerAfterHit
        Assert.Equal(2, effects.Count);
        Assert.Contains("SCORE:14", ((SendChat)effects[1]).Text);
        Assert.Contains("DO:", ((SendChat)effects[1]).Text);
    }

    [Fact]
    public void PlayerTurnStart_IncludesScore()
    {
        // BeginPlayerTurns triggers NarratePlayerTurn; verify {score} is substituted
        var state = new GameState
        {
            Phase      = GamePhase.Deal,
            DealerHand = new Hand { Cards = [10], State = HandState.Playing },
            Players    = [new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [5, 8], State = HandState.Playing }] }],
        };
        var (_, effects) = GameEngine.Apply(state, new BeginPlayerTurns());
        // effects[0] = PlayerTurnStart (deal summary is emitted when the last card is dealt)
        var turnStart = ((SendChat)effects[0]).Text;
        Assert.Contains("13", turnStart); // score of 5+8
    }

    [Fact]
    public void DealerTurnStart_SubstitutesCardsAndScore()
    {
        var state = new GameState
        {
            Phase             = GamePhase.DealerTurn,
            WaitingForDealer  = true,
            DealerHand        = new Hand { Cards = [10, 7], State = HandState.Playing },
            Players           = [new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [5, 8], State = HandState.Stand }] }],
        };
        var t = new NarrationTemplates { DealerTurnStart = "Dealer: {cards} ({score})" };
        var (_, effects) = GameEngine.Apply(state, new BeginDealerTurn(), t);
        var text = ((SendChat)effects[0]).Text;
        Assert.Contains("10", text);  // cards
        Assert.Contains("17", text);  // score of 10+7
    }

    [Fact]
    public void DealerHit_SubstitutesDealerName()
    {
        var t     = new NarrationTemplates { DealerHit = "{dealer}+{card}" };
        var state = new GameState
        {
            Phase      = GamePhase.DealerTurn,
            DealerHand = new Hand { Cards = [10], State = HandState.Playing },
        };
        var (_, effects) = GameEngine.Apply(state, new AddDealerCard(7), t, dealerName: "Vera");
        Assert.Equal("Vera+7", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void DealerTurnStart_SubstitutesDealerName()
    {
        var t     = new NarrationTemplates { DealerTurnStart = "{dealer}: {cards}" };
        var state = new GameState
        {
            Phase            = GamePhase.DealerTurn,
            WaitingForDealer = true,
            DealerHand       = new Hand { Cards = [10, 7], State = HandState.Playing },
            Players          = [new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [5, 8], State = HandState.Stand }] }],
        };
        var (_, effects) = GameEngine.Apply(state, new BeginDealerTurn(), t, dealerName: "Vera");
        Assert.StartsWith("Vera:", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void PayoutDealerBust_SubstitutesDealerName()
    {
        var t     = new NarrationTemplates { PayoutDealerBust = "{dealer} BUST", PayoutPlayer = "" };
        var state = new GameState
        {
            Phase      = GamePhase.DealerTurn,
            DealerHand = new Hand { Cards = [10, 7, 8], State = HandState.Bust },
            Players    = [new Player { Nickname = "Lorah", Bet = "100", Hands = [new Hand { Cards = [10, 9], State = HandState.Stand }] }],
        };
        var (_, effects) = GameEngine.Apply(state, new GoToPayout(), t, dealerName: "Vera");
        Assert.Equal("Vera BUST", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void DealSummaryDealer_SubstitutesDealerName()
    {
        var t     = new NarrationTemplates { DealSummaryDealer = "|{dealer}:{cards}", DealSummaryPrefix = "", DealSummaryPlayer = "" };
        var state = new GameState
        {
            Phase      = GamePhase.Deal,
            DealerHand = new Hand { Cards = [10], State = HandState.Playing },
            Players    = [new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [5], State = HandState.Playing }] }],
        };
        var (_, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 8), t, dealerName: "Vera");
        Assert.Contains("Vera:", ((SendChat)effects[0]).Text);
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
            ActiveHandIndex   = 0,
            Players =
            [
                new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [5, 6], State = HandState.Playing }] },
            ],
        };
        var originalCardCount = state.Players[0].Hands[0].Cards.Count;

        GameEngine.Apply(state, new AddPlayerCard(0, 0, 7));

        Assert.Equal(originalCardCount, state.Players[0].Hands[0].Cards.Count);
        Assert.Equal(GamePhase.PlayerTurns, state.Phase);
    }
}

public class AnnounceBettingOpenTests
{
    [Fact]
    public void AnnounceBettingOpen_NarratesTemplate()
    {
        var state = new GameState { Phase = GamePhase.Betting };
        var (newState, effects) = GameEngine.Apply(state, new AnnounceBettingOpen());
        Assert.Same(state, newState);
        Assert.Equal(2, effects.Count); // default template: narration line + /wringhands emote
    }

    [Fact]
    public void AnnounceBettingOpen_CustomTemplate()
    {
        var t = new NarrationTemplates { BettingOpen = "Betting is now open!" };
        var state = new GameState { Phase = GamePhase.Betting };
        var (_, effects) = GameEngine.Apply(state, new AnnounceBettingOpen(), t);
        Assert.Equal("Betting is now open!", ((SendChat)effects[0]).Text);
    }
}

public class LastRoundWinnersTests
{
    [Fact]
    public void GoToPayout_SetsWinnersForWinningPlayers()
    {
        var state = new GameState
        {
            Phase      = GamePhase.DealerTurn,
            DealerHand = new Hand { Cards = [10, 7], State = HandState.Stand },
            Players    =
            [
                new Player { Nickname = "Lorah", Bet = "100", Hands = [new Hand { Cards = [10, 9], State = HandState.Stand }] },
                new Player { Nickname = "Bekki", Bet = "100", Hands = [new Hand { Cards = [10, 6], State = HandState.Stand }] },
            ],
        };
        var (newState, _) = GameEngine.Apply(state, new GoToPayout());
        Assert.Contains("Lorah", newState.LastRoundWinners);
        Assert.DoesNotContain("Bekki", newState.LastRoundWinners);
    }

    [Fact]
    public void GoToPayout_BjWin_IncludedInWinners()
    {
        var state = new GameState
        {
            Phase      = GamePhase.DealerTurn,
            DealerHand = new Hand { Cards = [10, 7], State = HandState.Stand },
            Players    = [new Player { Nickname = "Lorah", Bet = "100", Hands = [new Hand { Cards = [1, 10], State = HandState.Blackjack }] }],
        };
        var (newState, _) = GameEngine.Apply(state, new GoToPayout());
        Assert.Contains("Lorah", newState.LastRoundWinners);
    }

    [Fact]
    public void NewRound_PreservesLastRoundWinners()
    {
        var state = new GameState
        {
            Phase            = GamePhase.Payout,
            LastRoundWinners = ["Lorah"],
            Players          = [new Player { Nickname = "Lorah", Hands = [new Hand()] }],
        };
        var (newState, _) = GameEngine.Apply(state, new NewRound());
        Assert.Contains("Lorah", newState.LastRoundWinners);
    }

    [Fact]
    public void GoToPayout_UsesFullNameForFfxivPlayers()
    {
        var state = new GameState
        {
            Phase      = GamePhase.DealerTurn,
            DealerHand = new Hand { Cards = [10, 7], State = HandState.Stand },
            Players    = [new Player { FullName = "Lorah Doe", World = "Cactuar", Bet = "100", Hands = [new Hand { Cards = [10, 9], State = HandState.Stand }] }],
        };
        var (newState, _) = GameEngine.Apply(state, new GoToPayout());
        Assert.Contains("Lorah Doe", newState.LastRoundWinners);
    }
}

public class CanGoToPayoutTests
{
    private static Player BjPlayer(string name) =>
        new() { Nickname = name, Hands = [new Hand { Cards = [1, 10], State = HandState.Blackjack }] };

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
            Players    = [new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [10, 8], State = HandState.Stand }] }],
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
            Players    = [new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [10, 8], State = HandState.Stand }] }],
            DealerHand = new Hand { Cards = [10, 6] },
        };
        Assert.False(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void DealerBust_CanGoToPayout()
    {
        var state = new GameState
        {
            Phase      = GamePhase.DealerTurn,
            Players    = [new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [10, 8], State = HandState.Stand }] }],
            DealerHand = new Hand { Cards = [6, 8, 13] },
        };
        Assert.True(GameEngine.CanGoToPayout(state));
    }
}

public class DoubleDownTests
{
    private static GameState ActiveState(int[] cards, string bet = "100") => new()
    {
        Phase             = GamePhase.PlayerTurns,
        ActivePlayerIndex = 0,
        ActiveHandIndex   = 0,
        DealerHand        = new Hand { Cards = [10], State = HandState.Playing },
        Players           =
        [
            new Player { Nickname = "Lorah", Bet = bet, Hands = [new Hand { Cards = [..cards], State = HandState.Playing }] },
        ],
    };

    [Fact]
    public void DoubleDown_SetDoubledFlagAndDoublesBet()
    {
        var (ns, _) = GameEngine.Apply(ActiveState([5, 6]), new DoubleDown(0, 0));
        Assert.True(ns.Players[0].Hands[0].Doubled);
        Assert.Equal("200", ns.Players[0].Hands[0].Bet);
    }

    [Fact]
    public void DoubleDown_CardLands_AutoStands_NarratesDouble()
    {
        var (s1, _) = GameEngine.Apply(ActiveState([5, 6]), new DoubleDown(0, 0));
        var (s2, effects) = GameEngine.Apply(s1, new AddPlayerCard(0, 0, 3));
        Assert.Equal(HandState.Stand, s2.Players[0].Hands[0].State);
        Assert.Contains("doubles down", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void DoubleDown_CardLands_AdvancesToDealerTurn()
    {
        var (s1, _) = GameEngine.Apply(ActiveState([5, 6]), new DoubleDown(0, 0));
        var (s2, _) = GameEngine.Apply(s1, new AddPlayerCard(0, 0, 3));
        Assert.Equal(GamePhase.DealerTurn, s2.Phase);
    }

    [Fact]
    public void CanDouble_TwoCards_NumericBet_True()
    {
        var hand = new Hand { Cards = [5, 6], State = HandState.Playing };
        Assert.True(GameEngine.CanDouble(hand, "100"));
    }

    [Fact]
    public void CanDouble_AlreadyDoubled_False()
    {
        var hand = new Hand { Cards = [5, 6], State = HandState.Playing, Doubled = true, Bet = "200" };
        Assert.False(GameEngine.CanDouble(hand, "100"));
    }

    [Fact]
    public void CanDouble_ThreeCards_False()
    {
        var hand = new Hand { Cards = [5, 3, 6], State = HandState.Playing };
        Assert.False(GameEngine.CanDouble(hand, "100"));
    }

    [Fact]
    public void AnnounceDouble_NarratesWithAmount()
    {
        var state = ActiveState([5, 6]);
        var (_, effects) = GameEngine.Apply(state, new AnnounceDouble(0, 0));
        Assert.Single(effects);
        Assert.Contains("100", ((SendChat)effects[0]).Text);
        Assert.Contains("double", ((SendChat)effects[0]).Text.ToLower());
    }

    [Fact]
    public void PayoutAmountString_DoubledHand_UsesDoubledBet()
    {
        var state = new GameState
        {
            Phase      = GamePhase.Payout,
            DealerHand = new Hand { Cards = [10, 7], State = HandState.Stand },
            Players    =
            [
                new Player
                {
                    Nickname = "Lorah", Bet = "100",
                    Hands    = [new Hand { Cards = [10, 9], State = HandState.Stand, Doubled = true, Bet = "200" }],
                },
            ],
        };
        Assert.Equal("+200", GameEngine.PayoutAmountString(state, 0, 0));
    }
}

public class SplitHandTests
{
    private static GameState ActiveState(int c0, int c1, string bet = "100") => new()
    {
        Phase             = GamePhase.PlayerTurns,
        ActivePlayerIndex = 0,
        ActiveHandIndex   = 0,
        DealerHand        = new Hand { Cards = [10], State = HandState.Playing },
        Players           =
        [
            new Player { Nickname = "Lorah", Bet = bet, Hands = [new Hand { Cards = [c0, c1], State = HandState.Playing }] },
        ],
    };

    [Fact]
    public void SplitHand_CreatesTwoOneCardHands()
    {
        var (ns, _) = GameEngine.Apply(ActiveState(8, 8), new SplitHand(0, 0));
        Assert.Equal(2, ns.Players[0].Hands.Count);
        Assert.Equal([8], ns.Players[0].Hands[0].Cards.ToArray());
        Assert.Equal([8], ns.Players[0].Hands[1].Cards.ToArray());
    }

    [Fact]
    public void SplitHand_BothHandsMarkedIsFromSplit()
    {
        var (ns, _) = GameEngine.Apply(ActiveState(8, 8), new SplitHand(0, 0));
        Assert.True(ns.Players[0].Hands[0].IsFromSplit);
        Assert.True(ns.Players[0].Hands[1].IsFromSplit);
    }

    [Fact]
    public void SplitHand_ActiveRemainsAtFirstSplitHand()
    {
        var (ns, _) = GameEngine.Apply(ActiveState(8, 8), new SplitHand(0, 0));
        Assert.Equal(0, ns.ActivePlayerIndex);
        Assert.Equal(0, ns.ActiveHandIndex);
    }

    [Fact]
    public void SplitHand_NarratesSplit()
    {
        var (_, effects) = GameEngine.Apply(ActiveState(8, 8), new SplitHand(0, 0));
        Assert.Contains(effects, e => e is SendChat c && c.Text.ToLower().Contains("splits"));
        Assert.Contains(effects, e => e is AutoHit ah && ah.PlayerIndex == 0 && ah.HandIndex == 0);
    }

    [Fact]
    public void SplitHand_NarratesRollBeforeAutoHit()
    {
        var t = new NarrationTemplates { PlayerSplitRoll = "Rolling for {name}" };
        var (_, effects) = GameEngine.Apply(ActiveState(8, 8), new SplitHand(0, 0), t);
        Assert.Contains(effects, e => e is SendChat c && c.Text == "Rolling for Lorah (Hand 1)");
    }

    [Fact]
    public void AdvanceToNextPlayer_NarratesRollForOneCardHand()
    {
        var t = new NarrationTemplates { PlayerSplitRoll = "Rolling for {name}" };
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            WaitingForNextPlayer = true,
            ActivePlayerIndex = 0,
            ActiveHandIndex   = 0,
            DealerHand        = new Hand { Cards = [10] },
            Players           =
            [
                new Player
                {
                    Nickname = "Lorah", Bet = "100",
                    Hands    =
                    [
                        new Hand { Cards = [8, 5], State = HandState.Stand, IsFromSplit = true },
                        new Hand { Cards = [8],    State = HandState.Playing, IsFromSplit = true },
                    ],
                },
            ],
        };
        var (_, effects) = GameEngine.Apply(state, new AdvanceToNextPlayer(), t);
        Assert.Contains(effects, e => e is SendChat c && c.Text == "Rolling for Lorah (Hand 2)");
        Assert.Contains(effects, e => e is AutoHit ah && ah.PlayerIndex == 0 && ah.HandIndex == 1);
    }

    [Fact]
    public void CanSplit_SameValue_True()
    {
        var hand = new Hand { Cards = [8, 8], State = HandState.Playing };
        Assert.True(GameEngine.CanSplit(hand));
    }

    [Fact]
    public void CanSplit_TenValueCards_True()
    {
        // 10, J, Q, K all have CardValue 10 — split allowed
        var hand = new Hand { Cards = [10, 12], State = HandState.Playing };
        Assert.True(GameEngine.CanSplit(hand));
    }

    [Fact]
    public void CanSplit_DifferentValues_False()
    {
        var hand = new Hand { Cards = [7, 8], State = HandState.Playing };
        Assert.False(GameEngine.CanSplit(hand));
    }

    [Fact]
    public void SplitAce_SecondCard_AutoStands()
    {
        // Simulate split ace: 1-card hand with ace, IsFromSplit = true
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            ActivePlayerIndex = 0,
            ActiveHandIndex   = 0,
            DealerHand        = new Hand { Cards = [10] },
            Players           =
            [
                new Player
                {
                    Nickname = "Lorah", Bet = "100",
                    Hands    = [new Hand { Cards = [1], State = HandState.Playing, IsFromSplit = true }],
                },
            ],
        };
        var (ns, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 7));
        Assert.Equal(HandState.Stand, ns.Players[0].Hands[0].State);
        Assert.Contains("split ace", ((SendChat)effects[0]).Text.ToLower());
    }

    [Fact]
    public void SplitAce_AcePlusTen_NotBlackjack()
    {
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            ActivePlayerIndex = 0,
            ActiveHandIndex   = 0,
            DealerHand        = new Hand { Cards = [10] },
            Players           =
            [
                new Player
                {
                    Nickname = "Lorah", Bet = "100",
                    Hands    = [new Hand { Cards = [1], State = HandState.Playing, IsFromSplit = true }],
                },
            ],
        };
        var (ns, _) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 10));
        // A + 10 = 21 but IsFromSplit → forced Stand, not Blackjack
        Assert.Equal(HandState.Stand, ns.Players[0].Hands[0].State);
        Assert.NotEqual(HandState.Blackjack, ns.Players[0].Hands[0].State);
    }

    [Fact]
    public void ReSplit_AllowedOnSplitHand()
    {
        // A split hand that ends up with two equal-value cards can split again
        var hand = new Hand { Cards = [8, 8], State = HandState.Playing, IsFromSplit = true };
        Assert.True(GameEngine.CanSplit(hand));
    }

    [Fact]
    public void SplitHand_StandOnFirstHand_EmitsAutoHitForSecondHand()
    {
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            ActivePlayerIndex = 0,
            ActiveHandIndex   = 0,
            DealerHand        = new Hand { Cards = [10] },
            Players           =
            [
                new Player
                {
                    Nickname = "Lorah", Bet = "100",
                    Hands    =
                    [
                        new Hand { Cards = [8, 5], State = HandState.Playing, IsFromSplit = true },
                        new Hand { Cards = [8],    State = HandState.Playing, IsFromSplit = true },
                    ],
                },
            ],
        };
        var (ns, effects) = GameEngine.Apply(state, new StandPlayer(0, 0));
        Assert.Equal(0, ns.ActiveHandIndex);
        Assert.True(ns.WaitingForNextPlayer);
        // AutoHit for hand 1 fires when AdvanceToNextPlayer is clicked
        var (ns2, effects2) = GameEngine.Apply(ns, new AdvanceToNextPlayer());
        Assert.Equal(1, ns2.ActiveHandIndex);
        Assert.Contains(effects2, e => e is AutoHit ah && ah.PlayerIndex == 0 && ah.HandIndex == 1);
    }

    [Fact]
    public void SplitHand_BustOnFirstHand_EmitsAutoHitForSecondHand()
    {
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            ActivePlayerIndex = 0,
            ActiveHandIndex   = 0,
            DealerHand        = new Hand { Cards = [10] },
            Players           =
            [
                new Player
                {
                    Nickname = "Lorah", Bet = "100",
                    Hands    =
                    [
                        new Hand { Cards = [8, 7], State = HandState.Playing, IsFromSplit = true },
                        new Hand { Cards = [8],    State = HandState.Playing, IsFromSplit = true },
                    ],
                },
            ],
        };
        // Draw a card that busts hand 0
        var (ns, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 9));
        Assert.Equal(HandState.Bust, ns.Players[0].Hands[0].State);
        Assert.Equal(0, ns.ActiveHandIndex);
        Assert.True(ns.WaitingForNextPlayer);
        // AutoHit for hand 1 fires when AdvanceToNextPlayer is clicked
        var (ns2, effects2) = GameEngine.Apply(ns, new AdvanceToNextPlayer());
        Assert.Equal(1, ns2.ActiveHandIndex);
        Assert.Contains(effects2, e => e is AutoHit ah && ah.PlayerIndex == 0 && ah.HandIndex == 1);
    }

    [Fact]
    public void SplitHand_MandatoryCardOnFirstHand_ThenNarratesTurn()
    {
        // After the mandatory 2nd card lands on hand 0 (still Playing), no AutoHit —
        // the player now acts on hand 0 before hand 1 gets its card.
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            ActivePlayerIndex = 0,
            ActiveHandIndex   = 0,
            DealerHand        = new Hand { Cards = [10] },
            Players           =
            [
                new Player
                {
                    Nickname = "Lorah", Bet = "100",
                    Hands    =
                    [
                        new Hand { Cards = [8],    State = HandState.Playing, IsFromSplit = true },
                        new Hand { Cards = [8],    State = HandState.Playing, IsFromSplit = true },
                    ],
                },
            ],
        };
        var (ns, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 5));
        Assert.Equal(2, ns.Players[0].Hands[0].Cards.Count);
        Assert.Equal(HandState.Playing, ns.Players[0].Hands[0].State);
        Assert.Equal(0, ns.ActiveHandIndex); // still on hand 0
        Assert.DoesNotContain(effects, e => e is AutoHit);
    }

    [Fact]
    public void SplitPayout_EachHandIndependent()
    {
        // Hand 0 wins, Hand 1 loses
        var state = new GameState
        {
            Phase      = GamePhase.Payout,
            DealerHand = new Hand { Cards = [10, 8], State = HandState.Stand },
            Players    =
            [
                new Player
                {
                    Nickname = "Lorah", Bet = "100",
                    Hands    =
                    [
                        new Hand { Cards = [10, 9], State = HandState.Stand, IsFromSplit = true }, // 19 > 18 → Win
                        new Hand { Cards = [8, 6],  State = HandState.Stand, IsFromSplit = true }, // 14 < 18 → Lose
                    ],
                },
            ],
        };
        Assert.Equal(PayoutResult.Win,  GameEngine.GetPayoutResult(state, 0, 0));
        Assert.Equal(PayoutResult.Lose, GameEngine.GetPayoutResult(state, 0, 1));
        Assert.Equal("+100", GameEngine.PayoutAmountString(state, 0, 0));
        Assert.Equal("-100", GameEngine.PayoutAmountString(state, 0, 1));
    }

    [Fact]
    public void CanHit_TrueForPlayingHandWith2PlusCards()
    {
        var hand = new Hand { Cards = [5, 7], State = HandState.Playing };
        Assert.True(GameEngine.CanHit(hand));
    }

    [Fact]
    public void CanHit_TrueFor1CardSplitHand()
    {
        // After undo, a 1-card split hand must be hittable so the player can re-draw.
        var hand = new Hand { Cards = [8], State = HandState.Playing, IsFromSplit = true };
        Assert.True(GameEngine.CanHit(hand));
    }

    [Fact]
    public void CanHit_FalseForStandHand()
    {
        var hand = new Hand { Cards = [5, 7, 3], State = HandState.Stand };
        Assert.False(GameEngine.CanHit(hand));
    }

    [Fact]
    public void IsDealComplete_TrueWhenDealerHas1AndPlayersHave2()
    {
        var state = new GameState
        {
            Phase      = GamePhase.Deal,
            DealerHand = new Hand { Cards = [10] },
            Players    =
            [
                new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [5, 9] }] },
                new Player { Nickname = "Bekki", Hands = [new Hand { Cards = [3, 8] }] },
            ],
        };
        Assert.True(GameEngine.IsDealComplete(state));
    }

    [Fact]
    public void IsDealComplete_FalseWhenPlayerMissingSecondCard()
    {
        var state = new GameState
        {
            Phase      = GamePhase.Deal,
            DealerHand = new Hand { Cards = [10] },
            Players    =
            [
                new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [5] }] },
            ],
        };
        Assert.False(GameEngine.IsDealComplete(state));
    }

    [Fact]
    public void IsDealComplete_FalseWhenDealerHasNoCard()
    {
        var state = new GameState
        {
            Phase      = GamePhase.Deal,
            DealerHand = new Hand { Cards = [] },
            Players    =
            [
                new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [5, 9] }] },
            ],
        };
        Assert.False(GameEngine.IsDealComplete(state));
    }

    [Fact]
    public void CanHitDealer_TrueDuringDealWithNoDealerCard()
    {
        var state = new GameState { Phase = GamePhase.Deal, DealerHand = new Hand { Cards = [] } };
        Assert.True(GameEngine.CanHitDealer(state));
    }

    [Fact]
    public void CanHitDealer_FalseDuringDealWhenDealerAlreadyHasCard()
    {
        var state = new GameState { Phase = GamePhase.Deal, DealerHand = new Hand { Cards = [7] } };
        Assert.False(GameEngine.CanHitDealer(state));
    }

    [Fact]
    public void CanHitDealer_TrueDuringDealerTurnWhenShouldHit()
    {
        // Soft 16 — dealer must hit
        var state = new GameState { Phase = GamePhase.DealerTurn, DealerHand = new Hand { Cards = [1, 5] } };
        Assert.True(GameEngine.CanHitDealer(state));
    }

    [Fact]
    public void CanHitDealer_FalseDuringDealerTurnWhenShouldStand()
    {
        // Hard 18 — dealer stands
        var state = new GameState { Phase = GamePhase.DealerTurn, DealerHand = new Hand { Cards = [10, 8] } };
        Assert.False(GameEngine.CanHitDealer(state));
    }

    [Fact]
    public void CanHitDealer_FalseDuringDealerTurnWhenBust()
    {
        var state = new GameState { Phase = GamePhase.DealerTurn, DealerHand = new Hand { Cards = [10, 8, 6] } };
        Assert.False(GameEngine.CanHitDealer(state));
    }

    [Fact]
    public void AnnounceSplit_NarratesWithAmount()
    {
        var state = new GameState
        {
            Phase             = GamePhase.PlayerTurns,
            ActivePlayerIndex = 0,
            ActiveHandIndex   = 0,
            Players           =
            [
                new Player { Nickname = "Lorah", Bet = "100", Hands = [new Hand { Cards = [8, 8], State = HandState.Playing }] },
            ],
        };
        var (_, effects) = GameEngine.Apply(state, new AnnounceSplit(0, 0));
        Assert.Single(effects);
        Assert.Contains("100", ((SendChat)effects[0]).Text);
        Assert.Contains("split", ((SendChat)effects[0]).Text.ToLower());
    }

    [Fact]
    public void AnnounceBetRequest_NarratesPlayerName()
    {
        var state = new GameState
        {
            Phase   = GamePhase.Betting,
            Players = [new Player { Nickname = "Lorah" }],
        };
        var (_, effects) = GameEngine.Apply(state, new AnnounceBetRequest(0));
        Assert.Single(effects);
        Assert.Contains("Lorah", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AnnounceBetConfirm_NarratesNameAndAmount()
    {
        var state = new GameState
        {
            Phase   = GamePhase.Betting,
            Players = [new Player { Nickname = "Lorah", Bet = "50000" }],
        };
        var t = new NarrationTemplates { PlayerBetConfirm = "{name} bet={amount}" };
        var (_, effects) = GameEngine.Apply(state, new AnnounceBetConfirm(0), t);
        Assert.Single(effects);
        Assert.Equal("Lorah bet=50000", ((SendChat)effects[0]).Text);
    }
}
