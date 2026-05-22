using System;
using System.Collections.Generic;
using System.Linq;
using TwentyOne.Game;
using TwentyOne.Game.Edge;
using TwentyOne.Tests.Helpers;
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
        Assert.Equal("HIT", GameEngine.DealerRecommendation(MakeHand(10, 6), standsOnSoft17: false));
        Assert.Equal("HIT", GameEngine.DealerRecommendation(MakeHand(10, 6), standsOnSoft17: true));
    }

    [Fact]
    public void Hard17_ReturnsStand()
    {
        Assert.Equal("STAND", GameEngine.DealerRecommendation(MakeHand(10, 7), standsOnSoft17: false));
        Assert.Equal("STAND", GameEngine.DealerRecommendation(MakeHand(10, 7), standsOnSoft17: true));
    }

    [Fact]
    public void Soft17_H17_ReturnsHit()
    {
        Assert.Equal("HIT", GameEngine.DealerRecommendation(MakeHand(1, 6), standsOnSoft17: false));
    }

    [Fact]
    public void Soft17_S17_ReturnsStand()
    {
        Assert.Equal("STAND", GameEngine.DealerRecommendation(MakeHand(1, 6), standsOnSoft17: true));
    }

    [Fact]
    public void Soft18_ReturnsStand()
    {
        Assert.Equal("STAND", GameEngine.DealerRecommendation(MakeHand(1, 7), standsOnSoft17: false));
        Assert.Equal("STAND", GameEngine.DealerRecommendation(MakeHand(1, 7), standsOnSoft17: true));
    }

    [Fact]
    public void EmptyHand_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, GameEngine.DealerRecommendation(new Hand(), standsOnSoft17: false));
    }
}

public class ApplyAddDealerCardTests
{
    private static GameState DealerTurnState() => new GameStateBuilder()
        .Phase(GamePhase.DealerTurn)
        .Dealer(10)
        .Player("Lorah", "10", 5, 8)
        .Build();

    [Fact]
    public void AddDealerCard_DealerTurn_NarratesNormalDraw()
    {
        // Draw a 4 → total 14, dealer must still hit - only one narration line
        var (newState, effects) = GameEngine.Apply(DealerTurnState(), new AddDealerCard(4));
        Assert.Single(effects);
        Assert.Contains("Dealer draws 4", ((SendChat)effects[0]).Text);
        Assert.Equal(2, newState.DealerHand.Cards.Length);
    }

    [Fact]
    public void AddDealerCard_DealerTurn_NarratesBust()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(10, 8)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new AddDealerCard(5));
        Assert.Contains("Bust", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AddDealerCard_DealPhase_NoNarration()
    {
        var state = new GameStateBuilder().Phase(GamePhase.Deal).Build();
        var (_, effects) = GameEngine.Apply(state, new AddDealerCard(10));
        Assert.Empty(effects);
    }

    [Fact]
    public void AddDealerCard_DoesNotMutateInput()
    {
        var state      = DealerTurnState();
        var origCount  = state.DealerHand.Cards.Length;
        GameEngine.Apply(state, new AddDealerCard(7));
        Assert.Equal(origCount, state.DealerHand.Cards.Length);
    }
}

public class ApplyAddPlayerCardTests
{
    private static GameState ActivePlayerState(int activeIndex = 0) => new GameStateBuilder()
        .Phase(GamePhase.PlayerTurns)
        .ActiveHand(activeIndex)
        .Player("Lorah", 5, 6)
        .Player("Bekki", 10, 8)
        .Build();

    [Fact]
    public void AddPlayerCard_NarratesHit()
    {
        var (_, effects) = GameEngine.Apply(ActivePlayerState(), new AddPlayerCard(0, 0, 3));
        Assert.Equal(2, effects.Count);
        Assert.Contains("Lorah hits", ((SendChat)effects[0]).Text);
        Assert.Contains("Lorah", ((SendChat)effects[1]).Text);
    }

    [Fact]
    public void AddPlayerCard_HitsTo21_AutoStands_NarratesHitThenStand()
    {
        // 5 + 6 + 10 = 21: hand auto-stands. PlayerHit fires, then PlayerStand
        // mirrors the dealer-side hit+stand pattern (and rescues configs that
        // intentionally empty PlayerHit by merging it into PlayerAfterHit).
        var (newState, effects) = GameEngine.Apply(ActivePlayerState(), new AddPlayerCard(0, 0, 10));
        Assert.Equal(HandState.Stand, newState.Players[0].Hands[0].State);
        Assert.Equal(2, effects.Count);
        Assert.Contains("hits", ((SendChat)effects[0]).Text);
        Assert.Contains("stands", ((SendChat)effects[1]).Text);
        Assert.Contains("21", ((SendChat)effects[1]).Text);
    }

    [Fact]
    public void AddPlayerCard_HitsTo21_EmptyPlayerHitTemplate_StillNarratesStand()
    {
        // Repro for the venue-config case where PlayerHit was intentionally
        // emptied (merged into PlayerAfterHit). Hitting to 21 used to be
        // completely silent because PlayerAfterHit only fires while Playing.
        var t = new NarrationTemplates { PlayerHit = [] };
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Player("Lorah", 5, 6)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 10), templates: t);
        Assert.Single(effects);
        Assert.Contains("stands", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AddPlayerCard_HitsTo21_SinglePlayer_AdvancesToDealerTurn()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Player("Lorah", 5, 6)
            .Build();
        var (newState, _) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 10));
        Assert.Equal(HandState.Stand, newState.Players[0].Hands[0].State);
        Assert.Equal(GamePhase.DealerTurn, newState.Phase);
    }

    [Fact]
    public void AddPlayerCard_HitsTo21_MultiPlayer_WaitsForNextPlayer()
    {
        var (newState, _) = GameEngine.Apply(ActivePlayerState(), new AddPlayerCard(0, 0, 10));
        Assert.True(newState.WaitingForNextPlayer);
        Assert.Equal(0, newState.ActivePlayerIndex);
    }

    [Fact]
    public void AddPlayerCard_SplitHand21_AutoStands()
    {
        // Split hand: 5+6+10 = 21. IsFromSplit means it can't be Blackjack - should still auto-stand.
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "100",
                Hands = [
                    new Hand { Cards = [5, 6], State = HandState.Playing, IsFromSplit = true },
                    new Hand { Cards = [3, 4], State = HandState.Playing, IsFromSplit = true },
                ],
            })
            .ActiveHand(0, 0)
            .Build();
        var (newState, _) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 10));
        Assert.Equal(HandState.Stand, newState.Players[0].Hands[0].State);
    }

    [Fact]
    public void AddPlayerCard_Bust_NarratesBustAndAdvances()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Player("Lorah", 10, 8)
            .Player("Bekki", 5, 6)
            .Build();
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
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Player("Lorah", 10, 8)
            .Build();
        var (newState, _) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 7));
        // All players bust → DealerTurn without WaitingForDealer (shows "Go to Payout" directly).
        Assert.Equal(GamePhase.DealerTurn, newState.Phase);
        Assert.False(newState.WaitingForDealer);
    }

    [Fact]
    public void AddPlayerCard_AllBustExceptStanding_TransitionsToDealerTurn()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Player("Lorah", 10, 8)
            .Player("Bekki", HandState.Stand, 10, 7)
            .Build();
        var (newState, _) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 7));
        Assert.Equal(GamePhase.DealerTurn, newState.Phase);
    }

    [Fact]
    public void AddPlayerCard_DealPhase_NoNarration()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .ActiveHand(-1)
            .Player("Lorah")
            .Build();
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
        var state = new GameStateBuilder().Phase(GamePhase.Deal).Build();
        var (newState, effects) = GameEngine.Apply(state, new AnnounceDealerDeal());
        Assert.Single(effects);
        Assert.Equal("Dealer's Card:", ((SendChat)effects[0]).Text);
        Assert.Equal(GamePhase.Deal, newState.Phase);
    }

    [Fact]
    public void AnnouncePlayerDeal_EmitsPlayerNameNarration()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .Player("Lorah")
            .Build();
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
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Player("Lorah", 10, 7)
            .Player("Bekki", 9, 8)
            .Build();
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
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Player("Lorah", 10, 7)
            .Build();
        var (newState, _) = GameEngine.Apply(state, new StandPlayer(0, 0));
        Assert.Equal(GamePhase.DealerTurn, newState.Phase);
    }

    [Fact]
    public void Stand_AlreadyStood_NoChange()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Player("Lorah", HandState.Stand, 10, 7)
            .Build();
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
        var state    = new GameStateBuilder().Phase(GamePhase.Betting).Build();
        var (ns, _)  = GameEngine.Apply(state, new StartDeal());
        Assert.Equal(GamePhase.Deal, ns.Phase);
    }

    [Fact]
    public void AddPlayerCard_CompletingDeal_NarratesDealSummary()
    {
        // Last card dealt (Bekki's 2nd card) completes the deal - summary fires here.
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .Dealer(10)
            .Player("Lorah", 5, 8)
            .Player("Bekki", 10)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new AddPlayerCard(1, 0, 9));
        Assert.Single(effects);
        Assert.Contains("Deal -", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void BeginPlayerTurns_SetsFirstActivePlayer()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .Dealer(10)
            .Player("Lorah", 5, 8)
            .Player("Bekki", 10, 9)
            .Build();
        var (newState, effects) = GameEngine.Apply(state, new BeginPlayerTurns());
        Assert.Single(effects);
        Assert.Contains("Lorah's turn", ((SendChat)effects[0]).Text);
        Assert.Equal(0, newState.ActivePlayerIndex);
        Assert.Equal(GamePhase.PlayerTurns, newState.Phase);
    }

    [Fact]
    public void BeginPlayerTurns_BlackjackPlayer_NarratesAndWaits()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .Dealer(10)
            .Player("Lorah", HandState.Blackjack, 1, 10)
            .Build();
        var (newState, effects) = GameEngine.Apply(state, new BeginPlayerTurns());
        // All hands are BJ → skip directly to DealerTurn (dealer has 10 upcard, need hole card)
        Assert.Equal(GamePhase.DealerTurn, newState.Phase);
        Assert.False(newState.WaitingForNextPlayer);
        Assert.True(newState.WaitingForDealer);
        Assert.Empty(effects);
    }

    [Fact]
    public void BeginPlayerTurns_AllBlackjacks_AdvanceToNextPlayerGoesToDealerTurn()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .Dealer(10)
            .Player("Lorah", HandState.Blackjack, 1, 10)
            .Build();
        var (mid, _)      = GameEngine.Apply(state, new BeginPlayerTurns());
        var (newState, _) = GameEngine.Apply(mid, new AdvanceToNextPlayer());
        Assert.Equal(GamePhase.DealerTurn, newState.Phase);
    }

    [Fact]
    public void NewRound_ResetsHandsKeepsPlayers()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .Dealer(HandState.Stand, 10, 7)
            .Player("Lorah", "10", HandState.Stand, 5, 8)
            .Build();
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
        int[] dealerCards, HandState dealerState = HandState.Stand) =>
        new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .Dealer(dealerState, dealerCards)
            .Player("Lorah", "100", playerState, playerCards)
            .Build();

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

    [Fact]
    public void DealerBlackjack_PlayerStand21_Loses()
    {
        // Player has 21 via three cards (not BJ); dealer has natural BJ - player loses
        var state = PayoutState([7, 7, 7], HandState.Stand, [1, 10]);
        Assert.Equal(PayoutResult.Lose, GameEngine.GetPayoutResult(state, 0));
    }

    [Fact]
    public void DealerBlackjack_PlayerStandLower_Loses()
    {
        var state = PayoutState([10, 9], HandState.Stand, [1, 10]);
        Assert.Equal(PayoutResult.Lose, GameEngine.GetPayoutResult(state, 0));
    }

    [Theory]
    [InlineData(1.5,  "+150")]   // 100 * 1.5 = 150
    [InlineData(1.2,  "+120")]   // 100 * 1.2 = 120
    [InlineData(1.0,  "+100")]   // 100 * 1.0 = 100
    [InlineData(1.37, "+137")]   // arbitrary multiplier rounds up via Math.Ceiling
    public void BjPayoutAmounts(double mul, string expected)
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .BjPayout(mul)
            .Dealer(HandState.Stand, 10, 7)
            .Player("Lorah", "100", HandState.Blackjack, 1, 10)
            .Build();
        Assert.Equal(expected, GameEngine.PayoutAmountString(state, 0));
    }

    [Fact]
    public void RegularWin_ReturnsBetAmount()
    {
        var state = PayoutState([10, 9], HandState.Stand, [10, 7]);
        state.BjPayout = 1.5;
        Assert.Equal("+100", GameEngine.PayoutAmountString(state, 0));
    }

    // PayoutTotalOwed = gross gil deposited at settlement (bet returned + profit).
    // Used by the bank-tooltip "After settlement" projection and UpdatePlayerStats.

    [Fact]
    public void PayoutTotalOwed_Win_IsTwiceBet()
    {
        var state = PayoutState([10, 9], HandState.Stand, [10, 7]);
        Assert.Equal(200m, GameEngine.PayoutTotalOwed(state, 0));
    }

    [Theory]
    [InlineData(1.5, 250)]   // 100 + 150
    [InlineData(1.2, 220)]   // 100 + 120
    [InlineData(1.0, 200)]   // 100 + 100
    public void PayoutTotalOwed_BjWin_IsBetPlusBjMultiplier(double mul, int expected)
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .BjPayout(mul)
            .Dealer(HandState.Stand, 10, 7)
            .Player("Lorah", "100", HandState.Blackjack, 1, 10)
            .Build();
        Assert.Equal((decimal)expected, GameEngine.PayoutTotalOwed(state, 0));
    }

    [Theory]
    [InlineData(PayoutRatio.ThreeToTwo, 250)]
    [InlineData(PayoutRatio.SixToFive,  220)]
    [InlineData(PayoutRatio.EvenMoney,  200)]
    public void PayoutTotalOwed_CharlieWin_IsBetPlusCharlieMultiplier(PayoutRatio payout, int expected)
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .Charlie(FiveCardCharlieRule.Disabled, payout)
            .Dealer(HandState.Stand, 10, 7)
            .Player("Lorah", "100", HandState.Charlie, 2, 3, 4, 5, 6)
            .Build();
        Assert.Equal((decimal)expected, GameEngine.PayoutTotalOwed(state, 0));
    }

    [Fact]
    public void PayoutTotalOwed_Push_ReturnsBet()
    {
        var state = PayoutState([10, 7], HandState.Stand, [10, 7]);
        Assert.Equal(100m, GameEngine.PayoutTotalOwed(state, 0));
    }

    [Fact]
    public void PayoutTotalOwed_Lose_IsZero()
    {
        var state = PayoutState([10, 6], HandState.Stand, [10, 7]);
        Assert.Equal(0m, GameEngine.PayoutTotalOwed(state, 0));
    }

    [Fact]
    public void PayoutTotalOwed_Bust_IsZero()
    {
        var state = PayoutState([10, 9, 5], HandState.Bust, [10, 7]);
        Assert.Equal(0m, GameEngine.PayoutTotalOwed(state, 0));
    }
}

public class RosterManagementTests
{
    private static GameState BettingState() => new GameStateBuilder().Phase(GamePhase.Betting).Build();

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
        var state = new GameStateBuilder()
            .Phase(GamePhase.Betting)
            .Player("Lorah")
            .Player("Bekki")
            .Build();
        var (ns, _) = GameEngine.Apply(state, new RemovePlayer(0));
        Assert.Single(ns.Players);
        Assert.Equal("Bekki", ns.Players[0].Nickname);
    }

    [Fact]
    public void SetPlayerBet_UpdatesBet()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Betting)
            .Player("Lorah", "10")
            .Build();
        var (ns, _) = GameEngine.Apply(state, new SetPlayerBet(0, "50"));
        Assert.Equal("50", ns.Players[0].Bet);
    }

    [Fact]
    public void RenamePlayer_UpdatesNickname()
    {
        var state = new GameStateBuilder()
            .Player("Lorah", "10")
            .Build();
        var (ns, _) = GameEngine.Apply(state, new RenamePlayer(0, "Nolla"));
        Assert.Equal("Nolla", ns.Players[0].Nickname);
        Assert.Equal("Nolla", ns.Players[0].DisplayName);
        Assert.Equal("10", ns.Players[0].Bet); // bet preserved
    }

    [Fact]
    public void RenamePlayer_PreservesFullNameAndWorld()
    {
        var state = new GameStateBuilder()
            .Player(new Player { FullName = "Lorah Banehene", World = "Adamantoise", Bet = "50" })
            .Build();
        var (ns, _) = GameEngine.Apply(state, new RenamePlayer(0, "Lory"));
        Assert.Equal("Lory", ns.Players[0].Nickname);
        Assert.Equal("Lory", ns.Players[0].DisplayName);
        Assert.Equal("Lorah Banehene", ns.Players[0].FullName);
        Assert.Equal("Adamantoise", ns.Players[0].World);
    }

    [Fact]
    public void RenamePlayer_ClearNickname_RevealsFfxivFirstName()
    {
        var state = new GameStateBuilder()
            .Player(new Player { Nickname = "Lory", FullName = "Lorah Banehene", World = "Adamantoise" })
            .Build();
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

    [Fact]
    public void ReorderPlayers_ChangesOrder()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Betting)
            .Player("Lorah").Player("Bekki").Player("Nolla")
            .Build();
        var (ns, _) = GameEngine.Apply(state, new ReorderPlayers([2, 0, 1]));
        Assert.Equal("Nolla", ns.Players[0].Nickname);
        Assert.Equal("Lorah", ns.Players[1].Nickname);
        Assert.Equal("Bekki", ns.Players[2].Nickname);
    }

    [Fact]
    public void ReorderPlayers_NoOpOutsideBetting()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Player("Lorah").Player("Bekki")
            .Build();
        var (ns, _) = GameEngine.Apply(state, new ReorderPlayers([1, 0]));
        Assert.Equal("Lorah", ns.Players[0].Nickname);
        Assert.Equal("Bekki", ns.Players[1].Nickname);
    }
}

public class NarrationTemplateTests
{
    private static GameState PlayerTurnsState() => new GameStateBuilder()
        .Phase(GamePhase.PlayerTurns)
        .Dealer(10)
        .Player("Lorah", "50", 5, 6)
        .Build();

    [Fact]
    public void CustomPlayerHit_UsesTemplate()
    {
        var t = new NarrationTemplates { PlayerHit = [["CUSTOM {name} drew {card}"]] };
        var (_, effects) = GameEngine.Apply(PlayerTurnsState(), new AddPlayerCard(0, 0, 3), t);
        Assert.Equal("CUSTOM Lorah drew 3", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void CustomPlayerBust_UsesTemplate()
    {
        var t = new NarrationTemplates { PlayerBust = [["OUT: {name}"]] };
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Player("Lorah", 10, 8)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 7), t);
        Assert.Equal("OUT: Lorah", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void CustomDealerHit_UsesTemplate()
    {
        var t = new NarrationTemplates { DealerHit = [["D+{card}={score}"]] };
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(10)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new AddDealerCard(7), t);
        Assert.Equal("D+7=17", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void DealerStand_NarratedAfterFinalHit()
    {
        var t = new NarrationTemplates { DealerStand = [["DS:{cards}={score}"]] };
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(10)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new AddDealerCard(7), t);
        // effects[0] = DealerHit, effects[1] = DealerStand
        Assert.Equal(2, effects.Count);
        Assert.Equal("DS:10 7=17", ((SendChat)effects[1]).Text);
    }

    [Fact]
    public void CustomPlayerStand_UsesTemplate()
    {
        var t = new NarrationTemplates { PlayerStand = [["{name} done ({score})"]] };
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Player("Lorah", 10, 7)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new StandPlayer(0, 0), t);
        Assert.Equal("Lorah done (17)", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void CustomDealSummaryPrefix_UsesTemplate()
    {
        var t = new NarrationTemplates { DealSummaryPrefix = "DEALT: ", DealSummaryDealer = "" };
        // Deal is incomplete: Lorah has 1 card. Adding 2nd card completes the deal.
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .SkipDealSummaryOnePlayer(false)
            .Dealer(10)
            .Player("Lorah", 5)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 8), t);
        Assert.StartsWith("DEALT: ", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void PayoutWin_UsesTemplate()
    {
        var t = new NarrationTemplates { PayoutWin = [["W:{name}"]], PayoutDealerStands = [["D"]] };
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(HandState.Stand, 10, 7)
            .Player("Lorah", "100", HandState.Stand, 10, 9)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new GoToPayout(), t);
        Assert.Equal("W:Lorah", ((SendChat)effects[2]).Text);
    }

    [Fact]
    public void PayoutBjWin_UsesTemplate()
    {
        var t = new NarrationTemplates { PayoutBjWin = [["BJ:{name}"]], PayoutDealerStands = [["D"]] };
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(HandState.Stand, 10, 7)
            .Player("Lorah", "100", HandState.Blackjack, 1, 10)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new GoToPayout(), t);
        Assert.Equal("BJ:Lorah", ((SendChat)effects[2]).Text);
    }

    [Fact]
    public void PayoutLose_UsesTemplate()
    {
        var t = new NarrationTemplates { PayoutLose = [["L:{name}"]], PayoutDealerStands = [["D"]] };
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(HandState.Stand, 10, 9)
            .Player("Lorah", "100", HandState.Stand, 10, 7)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new GoToPayout(), t);
        Assert.Equal("L:Lorah", ((SendChat)effects[2]).Text);
    }

    [Fact]
    public void PayoutPush_UsesTemplate()
    {
        var t = new NarrationTemplates { PayoutPush = [["P:{name}"]], PayoutDealerStands = [["D"]] };
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(HandState.Stand, 10, 7)
            .Player("Lorah", "100", HandState.Stand, 10, 7)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new GoToPayout(), t);
        Assert.Equal("P:Lorah", ((SendChat)effects[2]).Text);
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
        var t = new NarrationTemplates { PlayerAfterHit = [["SCORE:{score} DO:{actions}"]] };
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
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .Dealer(10)
            .Player("Lorah", 5, 8)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new BeginPlayerTurns());
        // effects[0] = PlayerTurnStart (deal summary is emitted when the last card is dealt)
        var turnStart = ((SendChat)effects[0]).Text;
        Assert.Contains("13", turnStart); // score of 5+8
    }

    [Fact]
    public void PlayerTurnStart_IncludesPlayerCards()
    {
        var t = new NarrationTemplates { PlayerTurnStart = [["{cards}"]] };
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .Dealer(10)
            .Player("Lorah", 5, 8)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new BeginPlayerTurns(), t);
        var turnStart = ((SendChat)effects[0]).Text;
        Assert.Contains("5", turnStart);
        Assert.Contains("8", turnStart);
    }

    [Fact]
    public void DealerTurnStart_SubstitutesCardsAndScore()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .WaitingForDealer()
            .Dealer(10, 7)
            .Player("Lorah", HandState.Stand, 5, 8)
            .Build();
        var t = new NarrationTemplates { DealerTurnStart = [["Dealer: {cards} ({score})"]] };
        var (_, effects) = GameEngine.Apply(state, new BeginDealerTurn(), t);
        var text = ((SendChat)effects[0]).Text;
        Assert.Contains("10", text);  // cards
        Assert.Contains("17", text);  // score of 10+7
    }

    [Fact]
    public void DealerHit_SubstitutesDealerName()
    {
        var t     = new NarrationTemplates { DealerHit = [["{dealer}+{card}"]] };
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(10)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new AddDealerCard(7), t, dealerName: "Vera");
        Assert.Equal("Vera+7", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void DealerTurnStart_SubstitutesDealerName()
    {
        var t     = new NarrationTemplates { DealerTurnStart = [["{dealer}: {cards}"]] };
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .WaitingForDealer()
            .Dealer(10, 7)
            .Player("Lorah", HandState.Stand, 5, 8)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new BeginDealerTurn(), t, dealerName: "Vera");
        Assert.StartsWith("Vera:", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void PayoutHeader_EmittedFirst()
    {
        var t     = new NarrationTemplates { PayoutHeader = [["SUMMARY"]], PayoutDealerStands = [["D"]], PayoutWin = [[""]] };
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(HandState.Stand, 10, 7)
            .Player("Lorah", "100", HandState.Stand, 10, 9)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new GoToPayout(), t);
        Assert.Equal("SUMMARY", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void PayoutDealerBust_SubstitutesDealerName()
    {
        var t     = new NarrationTemplates { PayoutDealerBust = [["{dealer} BUST"]], PayoutWin = [[""]] };
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(HandState.Bust, 10, 7, 8)
            .Player("Lorah", "100", HandState.Stand, 10, 9)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new GoToPayout(), t, dealerName: "Vera");
        Assert.Equal("Vera BUST", ((SendChat)effects[1]).Text);
    }

    [Fact]
    public void DealSummaryDealer_SubstitutesDealerName()
    {
        var t     = new NarrationTemplates { DealSummaryDealer = "|{dealer}:{cards}", DealSummaryPrefix = "", DealSummaryPlayer = "" };
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .SkipDealSummaryOnePlayer(false)
            .Dealer(10)
            .Player("Lorah", 5)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 8), t, dealerName: "Vera");
        Assert.Contains("Vera:", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void PlayerCharlie_UsesTemplate()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Charlie(FiveCardCharlieRule.BeatsAll)
            .Dealer(7)
            .Player("Lorah", "100", 2, 3, 4, 5)
            .Build();
        var t = new NarrationTemplates { PlayerCharlie = [["CHARLIE {name} {card}"]] };
        var (_, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 6), t);
        Assert.Equal("CHARLIE Lorah 6", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void PayoutCharlieWin_UsesTemplate()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Charlie(FiveCardCharlieRule.BeatsAll)
            .Dealer(HandState.Stand, 10, 7)
            .Player("Lorah", "100", HandState.Charlie, 2, 3, 4, 5, 6)
            .Build();
        var t = new NarrationTemplates { PayoutCharlieWin = [["CHARLIEWIN {name} {amount}"]] };
        var (_, effects) = GameEngine.Apply(state, new GoToPayout(), t);
        Assert.Contains(effects.OfType<SendChat>(), e => e.Text.StartsWith("CHARLIEWIN Lorah"));
    }
}

public class ImmutabilityTests
{
    [Fact]
    public void Apply_NeverMutatesInputState()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Player("Lorah", 5, 6)
            .Build();
        var originalCardCount = state.Players[0].Hands[0].Cards.Length;

        GameEngine.Apply(state, new AddPlayerCard(0, 0, 7));

        Assert.Equal(originalCardCount, state.Players[0].Hands[0].Cards.Length);
        Assert.Equal(GamePhase.PlayerTurns, state.Phase);
    }
}

// Verifies the "venue-rule edits don't leak into the running round" invariant
// from the per-venue refactor: house rules live on VenueSettings (canonical)
// and are mirrored on GameState at NewRound time. Once a round is in progress,
// nothing in the engine touches the GameState rule fields. The reseed only
// happens at the call site (Configuration.SeedRulesIntoGameState invoked from
// MainWindow.Apply when the action is NewRound).
public class VenueRulesDontLeakTests
{
    private static GameState NonDefaultRulesState() => new GameStateBuilder()
        .BjPayout(1.7)
        .Charlie(FiveCardCharlieRule.BeatsAll, PayoutRatio.SixToFive)
        .DealerStandsOnSoft17()
        .DoubleAfterSplit(false)
        .HitSplitAces()
        .ResplitAces()
        .ResplitCap(ResplitCap.Max2)
        .DoubleRestriction(DoubleRestriction.Hard9To11)
        .AllowSurrender()
        .Phase(GamePhase.PlayerTurns)
        .ActiveHand(0, 0)
        .Player("Lorah", "100", HandState.Playing, 10, 6)
        .Build();

    private static void AssertRulesEqual(GameState a, GameState b)
    {
        Assert.Equal(a.BjPayout,             b.BjPayout);
        Assert.Equal(a.CharliePayout,        b.CharliePayout);
        Assert.Equal(a.FiveCardCharlie,      b.FiveCardCharlie);
        Assert.Equal(a.DealerStandsOnSoft17, b.DealerStandsOnSoft17);
        Assert.Equal(a.DoubleAfterSplit,     b.DoubleAfterSplit);
        Assert.Equal(a.HitSplitAces,         b.HitSplitAces);
        Assert.Equal(a.ResplitAces,          b.ResplitAces);
        Assert.Equal(a.ResplitCap,           b.ResplitCap);
        Assert.Equal(a.DoubleRestriction,    b.DoubleRestriction);
        Assert.Equal(a.AllowSurrender,       b.AllowSurrender);
    }

    [Theory]
    [InlineData("Hit")]
    [InlineData("Stand")]
    [InlineData("Surrender")]
    public void MidRoundActions_PreserveRuleFields(string actionName)
    {
        var state = NonDefaultRulesState();
        GameAction action = actionName switch
        {
            "Hit"       => new AddPlayerCard(0, 0, 5),
            "Stand"     => new StandPlayer(0, 0),
            "Surrender" => new SurrenderHand(0, 0),
            _           => throw new ArgumentException(nameof(actionName)),
        };
        var (ns, _) = GameEngine.Apply(state, action);
        AssertRulesEqual(state, ns);
    }

    [Fact]
    public void NewRound_PreservesRuleFields()
    {
        // HandleNewRound resets phase/hands but leaves rules alone - the seed at
        // the call site (Configuration.SeedRulesIntoGameState) is what brings
        // VenueSettings → GameState. The engine itself must NOT erase the rule
        // fields, or in-progress snapshots and history viewer mode would lose them.
        var state = NonDefaultRulesState() with { Phase = GamePhase.Payout };
        var (ns, _) = GameEngine.Apply(state, new NewRound());
        AssertRulesEqual(state, ns);
        Assert.Equal(GamePhase.Betting, ns.Phase);
    }

    [Fact]
    public void AcrossFullRound_RuleFieldsNeverChange()
    {
        // Drive a small round through several actions and confirm rules survive.
        var state = new GameStateBuilder()
            .BjPayout(1.7)
            .DealerStandsOnSoft17()
            .AllowSurrender()
            .Phase(GamePhase.Deal)
            .Dealer(10)
            .Player("Lorah", "100", HandState.Playing, 5, 9)
            .Build();
        var rulesAtStart = state;

        (state, _) = GameEngine.Apply(state, new BeginPlayerTurns());
        AssertRulesEqual(rulesAtStart, state);

        (state, _) = GameEngine.Apply(state, new StandPlayer(0, 0));
        AssertRulesEqual(rulesAtStart, state);

        (state, _) = GameEngine.Apply(state, new BeginDealerTurn());
        AssertRulesEqual(rulesAtStart, state);

        (state, _) = GameEngine.Apply(state, new AddDealerCard(7));
        AssertRulesEqual(rulesAtStart, state);

        (state, _) = GameEngine.Apply(state, new GoToPayout());
        AssertRulesEqual(rulesAtStart, state);
    }
}

public class AnnounceBettingOpenTests
{
    [Fact]
    public void AnnounceBettingOpen_NarratesTemplate()
    {
        var state = new GameStateBuilder().Phase(GamePhase.Betting).Build();
        var (newState, effects) = GameEngine.Apply(state, new AnnounceBettingOpen());
        Assert.Same(state, newState);
        Assert.Equal(2, effects.Count); // default template: narration line + /wringhands emote
    }

    [Fact]
    public void AnnounceBettingOpen_CustomTemplate()
    {
        var t = new NarrationTemplates { BettingOpen = [["Betting is now open!"]] };
        var state = new GameStateBuilder().Phase(GamePhase.Betting).Build();
        var (_, effects) = GameEngine.Apply(state, new AnnounceBettingOpen(), t);
        Assert.Equal("Betting is now open!", ((SendChat)effects[0]).Text);
    }
}

public class LastRoundWinnersTests
{
    [Fact]
    public void GoToPayout_SetsWinnersForWinningPlayers()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(HandState.Stand, 10, 7)
            .Player("Lorah", "100", HandState.Stand, 10, 9)
            .Player("Bekki", "100", HandState.Stand, 10, 6)
            .Build();
        var (newState, _) = GameEngine.Apply(state, new GoToPayout());
        Assert.Contains("Lorah", newState.LastRoundWinners);
        Assert.DoesNotContain("Bekki", newState.LastRoundWinners);
    }

    [Fact]
    public void GoToPayout_BjWin_IncludedInWinners()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(HandState.Stand, 10, 7)
            .Player("Lorah", "100", HandState.Blackjack, 1, 10)
            .Build();
        var (newState, _) = GameEngine.Apply(state, new GoToPayout());
        Assert.Contains("Lorah", newState.LastRoundWinners);
    }

    [Fact]
    public void NewRound_PreservesLastRoundWinners()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .LastRoundWinners("Lorah")
            .Player("Lorah")
            .Build();
        var (newState, _) = GameEngine.Apply(state, new NewRound());
        Assert.Contains("Lorah", newState.LastRoundWinners);
    }

    [Fact]
    public void GoToPayout_UsesFullNameForFfxivPlayers()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(HandState.Stand, 10, 7)
            .Player(new Player { FullName = "Lorah Doe", World = "Cactuar", Bet = "100",
                Hands = [new Hand { Cards = [10, 9], State = HandState.Stand }] })
            .Build();
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
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(7)
            .Player(BjPlayer("Lorah"))
            .Build();
        Assert.True(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void AllBJ_DealerUpCardAce_NeedHoleCard()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(1)
            .Player(BjPlayer("Lorah"))
            .Build();
        Assert.False(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void AllBJ_DealerUpCardTenValue_NeedHoleCard()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(13)
            .Player(BjPlayer("Lorah"))
            .Build();
        Assert.False(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void AllBJ_DealerHasTwoCards_CanPayout()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(1, 10)
            .Player(BjPlayer("Lorah"))
            .Build();
        Assert.True(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void NormalPlay_DealerMustStand_CanPayout()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(10, 8)
            .Player("Lorah", HandState.Stand, 10, 8)
            .Build();
        Assert.True(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void NormalPlay_DealerMustHit_CannotPayout()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(10, 6)
            .Player("Lorah", HandState.Stand, 10, 8)
            .Build();
        Assert.False(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void DealerBust_CanGoToPayout()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(6, 8, 13)
            .Player("Lorah", HandState.Stand, 10, 8)
            .Build();
        Assert.True(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void AllCharlie_BeatsAll_CanPayout()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Charlie(FiveCardCharlieRule.BeatsAll)
            .Dealer(13)
            .Player(CharliePlayer("Lorah"))
            .Build();
        Assert.True(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void AllCharlie_BeatsAll_AceUpCard_CanPayout()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Charlie(FiveCardCharlieRule.BeatsAll)
            .Dealer(1)
            .Player(CharliePlayer("Lorah"))
            .Build();
        Assert.True(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void AllCharlie_LosesToDealerBJ_DealerUpCardNotTenValue_CanPayout()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Charlie(FiveCardCharlieRule.LosesToDealerBJ)
            .Dealer(7)
            .Player(CharliePlayer("Lorah"))
            .Build();
        Assert.True(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void AllCharlie_LosesToDealerBJ_DealerUpCardAce_NeedHoleCard()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Charlie(FiveCardCharlieRule.LosesToDealerBJ)
            .Dealer(1)
            .Player(CharliePlayer("Lorah"))
            .Build();
        Assert.False(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void AllCharlie_LosesToDealerBJ_DealerUpCardTenValue_NeedHoleCard()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Charlie(FiveCardCharlieRule.LosesToDealerBJ)
            .Dealer(13)
            .Player(CharliePlayer("Lorah"))
            .Build();
        Assert.False(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void AllCharlie_LosesToDealerBJ_DealerHasTwoCards_CanPayout()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Charlie(FiveCardCharlieRule.LosesToDealerBJ)
            .Dealer(1, 10)
            .Player(CharliePlayer("Lorah"))
            .Build();
        Assert.True(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void MixedBJAndCharlie_BeatsAll_SafeUpcard_CanPayout()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Charlie(FiveCardCharlieRule.BeatsAll)
            .Dealer(7)
            .Player(BjPlayer("Lorah"))
            .Player(CharliePlayer("Bekki"))
            .Build();
        Assert.True(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void MixedBJAndCharlie_BeatsAll_AceUpcard_NeedsHoleCard()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Charlie(FiveCardCharlieRule.BeatsAll)
            .Dealer(1)
            .Player(BjPlayer("Lorah"))
            .Player(CharliePlayer("Bekki"))
            .Build();
        Assert.False(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void MixedBJAndCharlie_LosesToDealerBJ_SafeUpcard_CanPayout()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Charlie(FiveCardCharlieRule.LosesToDealerBJ)
            .Dealer(7)
            .Player(BjPlayer("Lorah"))
            .Player(CharliePlayer("Bekki"))
            .Build();
        Assert.True(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void MixedBJAndCharlie_LosesToDealerBJ_AceUpcard_NeedsHoleCard()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Charlie(FiveCardCharlieRule.LosesToDealerBJ)
            .Dealer(1)
            .Player(BjPlayer("Lorah"))
            .Player(CharliePlayer("Bekki"))
            .Build();
        Assert.False(GameEngine.CanGoToPayout(state));
    }

    private static Player CharliePlayer(string name) =>
        new() { Nickname = name, Hands = [new Hand { Cards = [2, 3, 4, 5, 6], State = HandState.Charlie }] };
}

public class DoubleDownTests
{
    private static GameState ActiveState(int[] cards, string bet = "100") => new GameStateBuilder()
        .Phase(GamePhase.PlayerTurns)
        .ActiveHand(0, 0)
        .Dealer(10)
        .Player("Lorah", bet, cards)
        .Build();

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
        Assert.True(GameEngine.CanDouble(hand, "100", doubleAfterSplit: true, DoubleRestriction.Any));
    }

    [Fact]
    public void CanDouble_AlreadyDoubled_False()
    {
        var hand = new Hand { Cards = [5, 6], State = HandState.Playing, Doubled = true, Bet = "200" };
        Assert.False(GameEngine.CanDouble(hand, "100", doubleAfterSplit: true, DoubleRestriction.Any));
    }

    [Fact]
    public void CanDouble_ThreeCards_False()
    {
        var hand = new Hand { Cards = [5, 3, 6], State = HandState.Playing };
        Assert.False(GameEngine.CanDouble(hand, "100", doubleAfterSplit: true, DoubleRestriction.Any));
    }

    [Fact]
    public void CanDouble_FromSplit_True_WhenDoubleAfterSplitAllowed()
    {
        var hand = new Hand { Cards = [5, 6], State = HandState.Playing, IsFromSplit = true };
        Assert.True(GameEngine.CanDouble(hand, "100", doubleAfterSplit: true, DoubleRestriction.Any));
    }

    [Fact]
    public void CanDouble_FromSplit_False_WhenDoubleAfterSplitDisallowed()
    {
        var hand = new Hand { Cards = [5, 6], State = HandState.Playing, IsFromSplit = true };
        Assert.False(GameEngine.CanDouble(hand, "100", doubleAfterSplit: false, DoubleRestriction.Any));
    }

    [Fact]
    public void CanDouble_NotFromSplit_True_WhenDoubleAfterSplitDisallowed()
    {
        var hand = new Hand { Cards = [5, 6], State = HandState.Playing, IsFromSplit = false };
        Assert.True(GameEngine.CanDouble(hand, "100", doubleAfterSplit: false, DoubleRestriction.Any));
    }

    [Fact]
    public void AnnounceDouble_NarratesWithAmount()
    {
        var state = ActiveState([5, 6]);
        // !FromBank: BankAfter = shortfall = full bet (100) when no bank
        var (_, effects) = GameEngine.Apply(state, new AnnounceDouble(0, 0, FromBank: false, BankAfter: 100));
        Assert.Single(effects);
        Assert.Contains("100", ((SendChat)effects[0]).Text);
        Assert.Contains("double", ((SendChat)effects[0]).Text.ToLower());
    }

    [Fact]
    public void AnnounceDouble_FromBank_NarratesWithBankTemplate()
    {
        var state = ActiveState([5, 6]);
        var t = new NarrationTemplates { PlayerDoubleRequestBank = [["{name} dbl bank {amount} left {bank}"]] };
        var (_, effects) = GameEngine.Apply(state, new AnnounceDouble(0, 0, FromBank: true, BankAfter: 500), t);
        Assert.Single(effects);
        var text = ((SendChat)effects[0]).Text;
        Assert.Contains("Lorah", text);
        Assert.Contains("100", text);   // amount
        Assert.Contains("500", text);   // bank remaining
    }

    [Fact]
    public void AnnounceDouble_FromBank_ZeroRemaining_ShowsZeroNotNegative()
    {
        // Bank exactly covers the bet - BankAfter must be 0, never negative
        var state = ActiveState([5, 6]);
        var t = new NarrationTemplates { PlayerDoubleRequestBank = [["{name} {amount} {bank}"]] };
        var (_, effects) = GameEngine.Apply(state, new AnnounceDouble(0, 0, FromBank: true, BankAfter: 0), t);
        Assert.Single(effects);
        Assert.Contains("0", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AnnounceDouble_BankShort_UsesTradeTemplate()
    {
        // Bank insufficient → FromBank=false → regular trade-request template, not bank template
        var state = ActiveState([5, 6]);
        var t = new NarrationTemplates
        {
            PlayerDoubleRequest     = [["TRADE {name} {amount}"]],
            PlayerDoubleRequestBank = [["BANK {name} {amount} {bank}"]],
        };
        var (_, effects) = GameEngine.Apply(state, new AnnounceDouble(0, 0, FromBank: false), t);
        Assert.Single(effects);
        Assert.StartsWith("TRADE", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AnnounceDoubleConfirm_NarratesWithName()
    {
        var state = ActiveState([5, 6]);
        var t = new NarrationTemplates { PlayerDoubleConfirm = [["GL {name}!"]] };
        var (_, effects) = GameEngine.Apply(state, new AnnounceDoubleConfirm(0, 0), t);
        Assert.Single(effects);
        Assert.Equal("GL Lorah!", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void PayoutAmountString_DoubledHand_UsesDoubledBet()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .Dealer(HandState.Stand, 10, 7)
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "100",
                Hands    = [new Hand { Cards = [10, 9], State = HandState.Stand, Doubled = true, Bet = "200" }],
            })
            .Build();
        Assert.Equal("+200", GameEngine.PayoutAmountString(state, 0, 0));
    }
}

public class SplitHandTests
{
    private static GameState ActiveState(int c0, int c1, string bet = "100") => new GameStateBuilder()
        .Phase(GamePhase.PlayerTurns)
        .ActiveHand(0, 0)
        .Dealer(10)
        .Player("Lorah", bet, c0, c1)
        .Build();

    [Fact]
    public void SplitHand_CreatesTwoOneCardHands()
    {
        var (ns, _) = GameEngine.Apply(ActiveState(8, 8), new SplitHand(0, 0));
        Assert.Equal(2, ns.Players[0].Hands.Length);
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
        var t = new NarrationTemplates { PlayerSplitRoll = [["Rolling for {name}"]] };
        var (_, effects) = GameEngine.Apply(ActiveState(8, 8), new SplitHand(0, 0), t);
        Assert.Contains(effects, e => e is SendChat c && c.Text == "Rolling for Lorah (Hand 1)");
    }

    [Fact]
    public void AdvanceToNextPlayer_NarratesRollForOneCardHand()
    {
        var t = new NarrationTemplates { PlayerSplitRoll = [["Rolling for {name}"]] };
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .WaitingForNextPlayer()
            .Dealer(10)
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "100",
                Hands =
                [
                    new Hand { Cards = [8, 5], State = HandState.Stand, IsFromSplit = true },
                    new Hand { Cards = [8],    State = HandState.Playing, IsFromSplit = true },
                ],
            })
            .Build();
        var (_, effects) = GameEngine.Apply(state, new AdvanceToNextPlayer(), t);
        Assert.Contains(effects, e => e is SendChat c && c.Text == "Rolling for Lorah (Hand 2)");
        Assert.Contains(effects, e => e is AutoHit ah && ah.PlayerIndex == 0 && ah.HandIndex == 1);
    }

    [Fact]
    public void CanSplit_SameValue_True()
    {
        var hand = new Hand { Cards = [8, 8], State = HandState.Playing };
        Assert.True(GameEngine.CanSplit(hand, new Player { Hands = [hand] }, resplitAces: false, ResplitCap.Unlimited));
    }

    [Fact]
    public void CanSplit_SameRankTenValue_True()
    {
        // Q+Q: same rank, split allowed
        var hand = new Hand { Cards = [12, 12], State = HandState.Playing };
        Assert.True(GameEngine.CanSplit(hand, new Player { Hands = [hand] }, resplitAces: false, ResplitCap.Unlimited));
    }

    [Fact]
    public void CanSplit_DifferentRankTenValue_False()
    {
        // 10+Q: both worth 10 but different rank, no split
        var hand = new Hand { Cards = [10, 12], State = HandState.Playing };
        Assert.False(GameEngine.CanSplit(hand, new Player { Hands = [hand] }, resplitAces: false, ResplitCap.Unlimited));
    }

    [Fact]
    public void CanSplit_DifferentValues_False()
    {
        var hand = new Hand { Cards = [7, 8], State = HandState.Playing };
        Assert.False(GameEngine.CanSplit(hand, new Player { Hands = [hand] }, resplitAces: false, ResplitCap.Unlimited));
    }

    [Fact]
    public void CanSplit_SplitAcePair_False_WhenRsaDisallowed()
    {
        // Pair of aces produced by an earlier split, RSA off.
        var hand = new Hand { Cards = [1, 1], State = HandState.Playing, IsFromSplit = true };
        Assert.False(GameEngine.CanSplit(hand, new Player { Hands = [hand] }, resplitAces: false, ResplitCap.Unlimited));
    }

    [Fact]
    public void CanSplit_SplitAcePair_True_WhenRsaAllowed()
    {
        var hand = new Hand { Cards = [1, 1], State = HandState.Playing, IsFromSplit = true };
        Assert.True(GameEngine.CanSplit(hand, new Player { Hands = [hand] }, resplitAces: true, ResplitCap.Unlimited));
    }

    [Fact]
    public void CanSplit_OriginalAcePair_True_RegardlessOfRsa()
    {
        // First-time pair of aces (not from a previous split). Always splittable.
        var hand = new Hand { Cards = [1, 1], State = HandState.Playing, IsFromSplit = false };
        Assert.True(GameEngine.CanSplit(hand, new Player { Hands = [hand] }, resplitAces: false, ResplitCap.Unlimited));
        Assert.True(GameEngine.CanSplit(hand, new Player { Hands = [hand] }, resplitAces: true, ResplitCap.Unlimited));
    }

    [Fact]
    public void SplitAce_SecondCard_AutoStands()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Dealer(10)
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "100",
                Hands    = [new Hand { Cards = [1], State = HandState.Playing, IsFromSplit = true }],
            })
            .Build();
        var (ns, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 7));
        Assert.Equal(HandState.Stand, ns.Players[0].Hands[0].State);
        Assert.Contains("split ace", ((SendChat)effects[0]).Text.ToLower());
    }

    [Fact]
    public void SplitAce_SecondCard_StaysPlaying_WhenHSA()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Dealer(10)
            .HitSplitAces()
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "100",
                Hands    = [new Hand { Cards = [1], State = HandState.Playing, IsFromSplit = true }],
            })
            .Build();
        var (ns, _) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 7));
        // A + 7 = 18, with HSA the player can keep hitting.
        Assert.Equal(HandState.Playing, ns.Players[0].Hands[0].State);
    }

    [Fact]
    public void SplitAce_AcePlusTen_NotBlackjack_EvenWithHSA()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Dealer(10)
            .HitSplitAces()
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "100",
                Hands    = [new Hand { Cards = [1], State = HandState.Playing, IsFromSplit = true }],
            })
            .Build();
        var (ns, _) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 10));
        // 21 from a split hand stands; never Blackjack.
        Assert.Equal(HandState.Stand, ns.Players[0].Hands[0].State);
        Assert.NotEqual(HandState.Blackjack, ns.Players[0].Hands[0].State);
    }

    [Fact]
    public void SplitAce_AcePlusTen_NotBlackjack()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Dealer(10)
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "100",
                Hands    = [new Hand { Cards = [1], State = HandState.Playing, IsFromSplit = true }],
            })
            .Build();
        var (ns, _) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 10));
        Assert.Equal(HandState.Stand, ns.Players[0].Hands[0].State);
        Assert.NotEqual(HandState.Blackjack, ns.Players[0].Hands[0].State);
    }

    [Fact]
    public void ReSplit_AllowedOnSplitHand()
    {
        // A split hand that ends up with two equal-value cards can split again
        var hand = new Hand { Cards = [8, 8], State = HandState.Playing, IsFromSplit = true };
        Assert.True(GameEngine.CanSplit(hand, new Player { Hands = [hand] }, resplitAces: false, ResplitCap.Unlimited));
    }

    [Fact]
    public void SplitHand_StandOnFirstHand_EmitsAutoHitForSecondHand()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Dealer(10)
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "100",
                Hands =
                [
                    new Hand { Cards = [8, 5], State = HandState.Playing, IsFromSplit = true },
                    new Hand { Cards = [8],    State = HandState.Playing, IsFromSplit = true },
                ],
            })
            .Build();
        var (ns, effects) = GameEngine.Apply(state, new StandPlayer(0, 0));
        Assert.Equal(0, ns.ActiveHandIndex);
        Assert.True(ns.WaitingForNextPlayer);
        var (ns2, effects2) = GameEngine.Apply(ns, new AdvanceToNextPlayer());
        Assert.Equal(1, ns2.ActiveHandIndex);
        Assert.Contains(effects2, e => e is AutoHit ah && ah.PlayerIndex == 0 && ah.HandIndex == 1);
    }

    [Fact]
    public void SplitHand_BustOnFirstHand_EmitsAutoHitForSecondHand()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Dealer(10)
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "100",
                Hands =
                [
                    new Hand { Cards = [8, 7], State = HandState.Playing, IsFromSplit = true },
                    new Hand { Cards = [8],    State = HandState.Playing, IsFromSplit = true },
                ],
            })
            .Build();
        var (ns, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 9));
        Assert.Equal(HandState.Bust, ns.Players[0].Hands[0].State);
        Assert.Equal(0, ns.ActiveHandIndex);
        Assert.True(ns.WaitingForNextPlayer);
        var (ns2, effects2) = GameEngine.Apply(ns, new AdvanceToNextPlayer());
        Assert.Equal(1, ns2.ActiveHandIndex);
        Assert.Contains(effects2, e => e is AutoHit ah && ah.PlayerIndex == 0 && ah.HandIndex == 1);
    }

    [Fact]
    public void SplitHand_MandatoryCardOnFirstHand_ThenNarratesTurn()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Dealer(10)
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "100",
                Hands =
                [
                    new Hand { Cards = [8], State = HandState.Playing, IsFromSplit = true },
                    new Hand { Cards = [8], State = HandState.Playing, IsFromSplit = true },
                ],
            })
            .Build();
        var (ns, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 5));
        Assert.Equal(2, ns.Players[0].Hands[0].Cards.Length);
        Assert.Equal(HandState.Playing, ns.Players[0].Hands[0].State);
        Assert.Equal(0, ns.ActiveHandIndex);
        Assert.DoesNotContain(effects, e => e is AutoHit);
    }

    [Fact]
    public void SplitPayout_EachHandIndependent()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .Dealer(HandState.Stand, 10, 8)
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "100",
                Hands =
                [
                    new Hand { Cards = [10, 9], State = HandState.Stand, IsFromSplit = true },
                    new Hand { Cards = [8, 6],  State = HandState.Stand, IsFromSplit = true },
                ],
            })
            .Build();
        Assert.Equal(PayoutResult.Win,  GameEngine.GetPayoutResult(state, 0, 0));
        Assert.Equal(PayoutResult.Lose, GameEngine.GetPayoutResult(state, 0, 1));
        Assert.Equal("+100", GameEngine.PayoutAmountString(state, 0, 0));
        Assert.Equal("-100", GameEngine.PayoutAmountString(state, 0, 1));
    }

    [Fact]
    public void CanHit_TrueForPlayingHandWith2PlusCards()
    {
        var hand = new Hand { Cards = [5, 7], State = HandState.Playing };
        Assert.True(GameEngine.CanHit(hand, hitSplitAces: false));
        Assert.True(GameEngine.CanHit(hand, hitSplitAces: true));
    }

    [Fact]
    public void CanHit_FalseFor1CardSplitHand()
    {
        var hand = new Hand { Cards = [8], State = HandState.Playing, IsFromSplit = true };
        Assert.False(GameEngine.CanHit(hand, hitSplitAces: true));
    }

    [Fact]
    public void CanHit_FalseForStandHand()
    {
        var hand = new Hand { Cards = [5, 7, 3], State = HandState.Stand };
        Assert.False(GameEngine.CanHit(hand, hitSplitAces: false));
    }

    [Fact]
    public void CanHit_SplitAcePair_False_WhenHsaOff()
    {
        // [A,A] from an earlier split, kept Playing under RSA-on so the player can
        // resplit. HSA off must still block hitting.
        var hand = new Hand { Cards = [1, 1], State = HandState.Playing, IsFromSplit = true };
        Assert.False(GameEngine.CanHit(hand, hitSplitAces: false));
    }

    [Fact]
    public void CanHit_SplitAcePair_True_WhenHsaOn()
    {
        var hand = new Hand { Cards = [1, 1], State = HandState.Playing, IsFromSplit = true };
        Assert.True(GameEngine.CanHit(hand, hitSplitAces: true));
    }

    [Fact]
    public void CanStand_TrueOnPlayingTwoCardHand_RegardlessOfHsa()
    {
        var hand = new Hand { Cards = [1, 1], State = HandState.Playing, IsFromSplit = true };
        Assert.True(GameEngine.CanStand(hand));
    }

    [Fact]
    public void IsDealComplete_TrueWhenDealerHas1AndPlayersHave2()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .Dealer(10)
            .Player("Lorah", 5, 9)
            .Player("Bekki", 3, 8)
            .Build();
        Assert.True(GameEngine.IsDealComplete(state));
    }

    [Fact]
    public void IsDealComplete_FalseWhenPlayerMissingSecondCard()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .Dealer(10)
            .Player("Lorah", 5)
            .Build();
        Assert.False(GameEngine.IsDealComplete(state));
    }

    [Fact]
    public void IsDealComplete_FalseWhenDealerHasNoCard()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .Player("Lorah", 5, 9)
            .Build();
        Assert.False(GameEngine.IsDealComplete(state));
    }

    [Fact]
    public void CanHitDealer_TrueDuringDealWithNoDealerCard()
    {
        var state = new GameStateBuilder().Phase(GamePhase.Deal).Build();
        Assert.True(GameEngine.CanHitDealer(state));
    }

    [Fact]
    public void CanHitDealer_FalseDuringDealWhenDealerAlreadyHasCard()
    {
        var state = new GameStateBuilder().Phase(GamePhase.Deal).Dealer(7).Build();
        Assert.False(GameEngine.CanHitDealer(state));
    }

    [Fact]
    public void CanHitDealer_TrueDuringDealerTurnWhenShouldHit()
    {
        var state = new GameStateBuilder().Phase(GamePhase.DealerTurn).Dealer(1, 5).Build();
        Assert.True(GameEngine.CanHitDealer(state));
    }

    [Fact]
    public void CanHitDealer_FalseDuringDealerTurnWhenShouldStand()
    {
        var state = new GameStateBuilder().Phase(GamePhase.DealerTurn).Dealer(10, 8).Build();
        Assert.False(GameEngine.CanHitDealer(state));
    }

    [Fact]
    public void CanHitDealer_Soft17_HitsUnderH17()
    {
        // A+6 = soft 17. With H17 (default), dealer must hit.
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(1, 6)
            .Player("Lorah", "100", HandState.Stand, 10, 8)
            .Build();
        Assert.True(GameEngine.CanHitDealer(state));
    }

    [Fact]
    public void CanHitDealer_Soft17_StandsUnderS17()
    {
        // A+6 = soft 17. With S17 on, dealer stands.
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(1, 6)
            .DealerStandsOnSoft17()
            .Player("Lorah", "100", HandState.Stand, 10, 8)
            .Build();
        Assert.False(GameEngine.CanHitDealer(state));
    }

    [Fact]
    public void CanHitDealer_FalseDuringDealerTurnWhenBust()
    {
        var state = new GameStateBuilder().Phase(GamePhase.DealerTurn).Dealer(10, 8, 6).Build();
        Assert.False(GameEngine.CanHitDealer(state));
    }

    [Fact]
    public void CanHitDealer_False_WhenAllPlayersBJ_AndDealerCannotHaveBJ()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(HandState.Playing, 4)
            .Player("Lorah", "100", HandState.Blackjack, 1, 10)
            .Build();
        Assert.False(GameEngine.CanHitDealer(state));
    }

    [Fact]
    public void AnnounceSplit_NarratesWithAmount()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Player("Lorah", "100", HandState.Playing, 8, 8)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new AnnounceSplit(0, 0, FromBank: false, BankAfter: 100));
        Assert.Single(effects);
        Assert.Contains("100", ((SendChat)effects[0]).Text);
        Assert.Contains("split", ((SendChat)effects[0]).Text.ToLower());
    }

    [Fact]
    public void AnnounceSplit_FromBank_NarratesWithBankTemplate()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Player("Lorah", "100", HandState.Playing, 8, 8)
            .Build();
        var t = new NarrationTemplates { PlayerSplitRequestBank = [["{name} spl bank {amount} left {bank}"]] };
        var (_, effects) = GameEngine.Apply(state, new AnnounceSplit(0, 0, FromBank: true, BankAfter: 250), t);
        Assert.Single(effects);
        var text = ((SendChat)effects[0]).Text;
        Assert.Contains("Lorah", text);
        Assert.Contains("100", text);
        Assert.Contains("250", text);
    }

    [Fact]
    public void AnnounceSplit_FromBank_ZeroRemaining_ShowsZeroNotNegative()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Player("Lorah", "100", HandState.Playing, 8, 8)
            .Build();
        var t = new NarrationTemplates { PlayerSplitRequestBank = [["{name} {amount} {bank}"]] };
        var (_, effects) = GameEngine.Apply(state, new AnnounceSplit(0, 0, FromBank: true, BankAfter: 0), t);
        Assert.Single(effects);
        Assert.Contains("0", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AnnounceSplit_BankShort_UsesTradeTemplate()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Player("Lorah", "100", HandState.Playing, 8, 8)
            .Build();
        var t = new NarrationTemplates
        {
            PlayerSplitRequest     = [["TRADE {name} {amount}"]],
            PlayerSplitRequestBank = [["BANK {name} {amount} {bank}"]],
        };
        var (_, effects) = GameEngine.Apply(state, new AnnounceSplit(0, 0, FromBank: false), t);
        Assert.Single(effects);
        Assert.StartsWith("TRADE", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AnnounceBetRequest_NarratesPlayerName()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Betting)
            .Player("Lorah")
            .Build();
        var (_, effects) = GameEngine.Apply(state, new AnnounceBetRequest(0));
        Assert.Single(effects);
        Assert.Contains("Lorah", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AnnounceBetConfirm_NarratesNameAndAmount_NoBank()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Betting)
            .Player("Lorah", "50000")
            .Build();
        var t = new NarrationTemplates { PlayerBetConfirm = [["{name} bet={amount}"]] };
        var (_, effects) = GameEngine.Apply(state, new AnnounceBetConfirm(0, 0L), t);
        Assert.Single(effects);
        Assert.Equal("Lorah bet=50K", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AnnounceBetConfirmBank_NarratesNameAmountAndBank()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Betting)
            .Player("Lorah", "50000")
            .Build();
        var t = new NarrationTemplates { PlayerBetConfirmBank = [["{name} bet={amount} bank={bank} after={bank-after-bet}"]] };
        var (_, effects) = GameEngine.Apply(state, new AnnounceBetConfirm(0, 100000L), t);
        Assert.Single(effects);
        Assert.Equal("Lorah bet=50K bank=100K after=50K", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AnnounceBankRemind_NarratesNameAmountAndBank()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Betting)
            .Player("Lorah", "50000")
            .Build();
        var t = new NarrationTemplates { PlayerBankRemind = [["{name} bet={amount} bank={bank}"]] };
        var (_, effects) = GameEngine.Apply(state, new AnnounceBankRemind(0, 200000), t);
        Assert.Single(effects);
        Assert.Equal("Lorah bet=50K bank=200K", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AnnounceBankShortfall_NarratesNameAndShortfall()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Betting)
            .Player("Lorah", "100000")
            .Build();
        var t = new NarrationTemplates { PlayerBankShortfall = [["{name} needs {amount}"]] };
        var (_, effects) = GameEngine.Apply(state, new AnnounceBankShortfall(0, 60000), t);
        Assert.Single(effects);
        Assert.Equal("Lorah needs 60000", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AnnounceBankDeposit_NarratesNameAmountAndNewBalance()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Betting)
            .Player("Lorah")
            .Build();
        var t = new NarrationTemplates { PlayerBankDeposit = [["{name} dep={amount} bal={bank}"]] };
        var (_, effects) = GameEngine.Apply(state, new AnnounceBankDeposit(0, 50000, 150000), t);
        Assert.Single(effects);
        Assert.Equal("Lorah dep=50K bal=150K", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AnnounceBankWithdraw_NarratesNameAmountAndNewBalance()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Betting)
            .Player("Lorah")
            .Build();
        var t = new NarrationTemplates { PlayerBankWithdraw = [["{name} wd={amount} bal={bank}"]] };
        var (_, effects) = GameEngine.Apply(state, new AnnounceBankWithdraw(0, 30000, 70000), t);
        Assert.Single(effects);
        Assert.Equal("Lorah wd=30K bal=70K", ((SendChat)effects[0]).Text);
    }
}

public class AnnouncePlayerTurnTests
{
    [Fact]
    public void AnnouncePlayerTurn_NarratesPlayerTurnStart_NoStateChange()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Dealer(7)
            .Player("Lorah", "100", HandState.Playing, 5, 6)
            .Build();
        var t = new NarrationTemplates { PlayerTurnStart = [["{name}:{score}"]] };
        var (newState, effects) = GameEngine.Apply(state, new AnnouncePlayerTurn(0, 0), t);
        Assert.Same(state, newState);
        Assert.Single(effects);
        Assert.Equal("Lorah:11", ((SendChat)effects[0]).Text);
    }
}

public class PayoutSplitCombinedTests
{
    private static GameState SplitWinState(int[] hand0Cards, int[] hand1Cards, int[] dealerCards) =>
        new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(HandState.Stand, dealerCards)
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "100",
                Hands =
                [
                    new Hand { Cards = [..hand0Cards], State = HandState.Stand, IsFromSplit = true },
                    new Hand { Cards = [..hand1Cards], State = HandState.Stand, IsFromSplit = true },
                ],
            })
            .Build();

    [Fact]
    public void SplitBothHandsWin_EmitsCombinedNarration()
    {
        var state = SplitWinState([10, 9], [10, 8], [10, 6]);
        var t = new NarrationTemplates { PayoutSplitCombined = [["SPLIT:{name}={amount}"]], PayoutDealerStands = [["D"]] };
        var (_, effects) = GameEngine.Apply(state, new GoToPayout(), t);
        var texts = effects.OfType<SendChat>().Select(e => e.Text).ToList();
        Assert.Contains(texts, s => s.StartsWith("SPLIT:Lorah="));
        Assert.DoesNotContain(texts, s => s.Contains("Hand 1") || s.Contains("Hand 2"));
    }

    [Fact]
    public void SplitBothHandsWin_CombinedAmountIsSum()
    {
        var state = SplitWinState([10, 9], [10, 8], [10, 6]);
        var t = new NarrationTemplates { PayoutSplitCombined = [["{amount}"]], PayoutDealerStands = [["D"]] };
        var (_, effects) = GameEngine.Apply(state, new GoToPayout(), t);
        var combined = effects.OfType<SendChat>().Select(e => e.Text).First(s => s.Contains("+200"));
        Assert.Contains("+200", combined);
    }

    [Fact]
    public void SplitMixedResult_EmitsPerHandNarration()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(HandState.Stand, 10, 8)
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "100",
                Hands =
                [
                    new Hand { Cards = [10, 9], State = HandState.Stand, IsFromSplit = true },
                    new Hand { Cards = [10, 7], State = HandState.Stand, IsFromSplit = true },
                ],
            })
            .Build();
        var t = new NarrationTemplates
        {
            PayoutSplitCombined = [["SPLIT"]],
            PayoutWin  = [["WIN:{name}"]],
            PayoutLose = [["LOSE:{name}"]],
            PayoutDealerStands = [["D"]],
        };
        var (_, effects) = GameEngine.Apply(state, new GoToPayout(), t);
        var texts = effects.OfType<SendChat>().Select(e => e.Text).ToList();
        Assert.DoesNotContain("SPLIT", texts);
        Assert.Contains(texts, s => s.Contains("Hand 1") && s.StartsWith("WIN:"));
        Assert.Contains(texts, s => s.Contains("Hand 2") && s.StartsWith("LOSE:"));
    }

    [Fact]
    public void PayoutSplitCombined_TemplateVariable_Amount()
    {
        var state = SplitWinState([10, 9], [10, 8], [10, 6]);
        var t = new NarrationTemplates { PayoutSplitCombined = [["TOTAL={amount}"]], PayoutDealerStands = [["D"]] };
        var (_, effects) = GameEngine.Apply(state, new GoToPayout(), t);
        var texts = effects.OfType<SendChat>().Select(e => e.Text).ToList();
        Assert.Contains(texts, s => s == "TOTAL=+200");
    }
}

public class PlayerBJMovingAlongTests
{
    private static GameState MultiPlayerBJState() => new GameStateBuilder()
        .Phase(GamePhase.PlayerTurns)
        .ActiveHand(0, 0)
        .Dealer(HandState.Playing, 7)
        .Player("Lorah", HandState.Blackjack, 1, 10)
        .Player("Bekki", HandState.Playing, 10, 9)
        .Build();

    [Fact]
    public void BeginPlayerTurns_BJ_MultiPlayer_EmitsMovingAlong()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Dealer(HandState.Playing, 7)
            .Player("Lorah", HandState.Blackjack, 1, 10)
            .Player("Bekki", HandState.Playing, 10, 9)
            .Build();
        var t = new NarrationTemplates { PlayerBJMovingAlong = [["MOVING: {name}"]] };
        var (_, effects) = GameEngine.Apply(state, new BeginPlayerTurns(), t);
        var texts = effects.OfType<SendChat>().Select(e => e.Text).ToList();
        Assert.Contains(texts, s => s == "MOVING: Lorah");
    }

    [Fact]
    public void BeginPlayerTurns_BJ_SinglePlayer_NoMovingAlong()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Dealer(HandState.Playing, 7)
            .Player("Lorah", HandState.Blackjack, 1, 10)
            .Build();
        var t = new NarrationTemplates { PlayerBJMovingAlong = [["MOVING: {name}"]] };
        var (_, effects) = GameEngine.Apply(state, new BeginPlayerTurns(), t);
        var texts = effects.OfType<SendChat>().Select(e => e.Text).ToList();
        Assert.DoesNotContain(texts, s => s.StartsWith("MOVING:"));
    }

    [Fact]
    public void AdvanceToNextPlayer_BJ_MultiPlayer_EmitsMovingAlong()
    {
        var state = MultiPlayerBJState();
        state.WaitingForNextPlayer = true;
        var t = new NarrationTemplates { PlayerBJMovingAlong = [["MA:{name}"]], PlayerTurnStart = [["{name}"]] };
        var (_, effects) = GameEngine.Apply(state, new AdvanceToNextPlayer(), t);
        var texts = effects.OfType<SendChat>().Select(e => e.Text).ToList();
        Assert.Contains(texts, s => s == "MA:Bekki" || s == "Bekki");
    }

    [Fact]
    public void PlayerBJMovingAlong_TemplateVariable_Name()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Dealer(HandState.Playing, 7)
            .Player("Lorah", HandState.Blackjack, 1, 10)
            .Player("Bekki", HandState.Playing, 10, 9)
            .Build();
        var t = new NarrationTemplates { PlayerBJMovingAlong = [["BJ:{name} cards:{cards}"]] };
        var (_, effects) = GameEngine.Apply(state, new BeginPlayerTurns(), t);
        var texts = effects.OfType<SendChat>().Select(e => e.Text).ToList();
        Assert.Contains(texts, s => s == "BJ:Lorah cards:A 10");
    }
}

public class DealSummaryOnePlayerTests
{
    [Fact]
    public void SkipDealSummaryOnePlayer_True_SkipsSummary()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .SkipDealSummaryOnePlayer()
            .Dealer(HandState.Playing, 7)
            .Player("Lorah", HandState.Playing, 10)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 9));
        var texts = effects.OfType<SendChat>().Select(e => e.Text).ToList();
        Assert.DoesNotContain(texts, s => s.StartsWith("Deal"));
    }

    [Fact]
    public void SkipDealSummaryOnePlayer_False_EmitsSummary()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .SkipDealSummaryOnePlayer(false)
            .Dealer(HandState.Playing, 7)
            .Player("Lorah", HandState.Playing, 10)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 9));
        var texts = effects.OfType<SendChat>().Select(e => e.Text).ToList();
        Assert.Contains(texts, s => s.Contains("Deal"));
    }

    [Fact]
    public void SkipDealSummaryOnePlayer_MultiPlayer_AlwaysEmits()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .SkipDealSummaryOnePlayer()
            .Dealer(HandState.Playing, 7)
            .Player("Lorah", HandState.Playing, 10, 8)
            .Player("Bekki", HandState.Playing, 10)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new AddPlayerCard(1, 0, 9));
        var texts = effects.OfType<SendChat>().Select(e => e.Text).ToList();
        Assert.Contains(texts, s => s.Contains("Deal"));
    }
}

public class DealSummaryBJNarrationTests
{
    [Fact]
    public void DealComplete_BJ_EmitsPlayerBJAfterSummary()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .SkipDealSummaryOnePlayer(false)
            .Dealer(HandState.Playing, 7)
            .Player("Lorah", HandState.Playing, 10)
            .Player("Bekki", HandState.Playing, 10, 9)
            .Build();
        var (_, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 1));
        var texts = effects.OfType<SendChat>().Select(e => e.Text).ToList();
        Assert.Contains(texts, s => s.Contains("Deal"));
        Assert.Contains(texts, s => s.Contains("Blackjack"));
        var summaryIdx = texts.FindIndex(s => s.Contains("Deal"));
        var bjIdx      = texts.FindIndex(s => s.Contains("Blackjack"));
        Assert.True(summaryIdx < bjIdx);
    }

    [Fact]
    public void DealComplete_MultipleBJ_EmittedInPlayerOrder()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .SkipDealSummaryOnePlayer(false)
            .Dealer(HandState.Playing, 7)
            .Player("Lorah", HandState.Blackjack, 1, 10)
            .Player("Bekki", HandState.Playing, 10)
            .Player("Nolla", HandState.Playing, 1)
            .Build();
        var stateAfterBekki = GameEngine.Apply(state, new AddPlayerCard(1, 0, 1)).Item1;
        var (_, effects) = GameEngine.Apply(stateAfterBekki, new AddPlayerCard(2, 0, 10));
        var texts = effects.OfType<SendChat>().Select(e => e.Text).ToList();
        var lorahBjIdx = texts.FindIndex(s => s.Contains("Lorah") && s.Contains("Blackjack"));
        var nollaBjIdx = texts.FindIndex(s => s.Contains("Nolla") && s.Contains("Blackjack"));
        Assert.True(lorahBjIdx >= 0 && nollaBjIdx >= 0);
        Assert.True(lorahBjIdx < nollaBjIdx);
    }
}

public class DealerBJCheckTests
{
    [Fact]
    public void AnnounceDealerHit_AllBJ_UsesBJCheckTemplate()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(HandState.Playing, 1)
            .Player("Lorah", HandState.Blackjack, 1, 10)
            .Build();
        var t = new NarrationTemplates { DealerBJCheck = [["LUCKY CHECK"]] };
        var (_, effects) = GameEngine.Apply(state, new AnnounceDealerHit(), t);
        Assert.Equal("LUCKY CHECK", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AnnounceDealerHit_NotAllBJ_UsesHitAnnounceTemplate()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(HandState.Playing, 7)
            .Player("Lorah", HandState.Stand, 10, 8)
            .Build();
        var t = new NarrationTemplates { DealerHitAnnounce = [["HIT: {dealer}"]], DealerBJCheck = [["LUCKY CHECK"]] };
        var (_, effects) = GameEngine.Apply(state, new AnnounceDealerHit(), t, dealerName: "Vera");
        Assert.Equal("HIT: Vera", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AnnounceDealerHit_AllCharlie_LosesToDealerBJ_UsesBJCheckTemplate()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Charlie(FiveCardCharlieRule.LosesToDealerBJ)
            .Dealer(HandState.Playing, 1)
            .Player("Lorah", "100", HandState.Charlie, 2, 3, 4, 5, 6)
            .Build();
        var t = new NarrationTemplates { DealerBJCheck = [["LUCKY CHECK"]] };
        var (_, effects) = GameEngine.Apply(state, new AnnounceDealerHit(), t);
        Assert.Equal("LUCKY CHECK", ((SendChat)effects[0]).Text);
    }

    [Fact]
    public void AnnounceDealerHit_AllCharlie_BeatsAll_UsesHitAnnounceTemplate()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Charlie(FiveCardCharlieRule.BeatsAll)
            .Dealer(HandState.Playing, 1)
            .Player("Lorah", "100", HandState.Charlie, 2, 3, 4, 5, 6)
            .Build();
        var t = new NarrationTemplates { DealerHitAnnounce = [["ANNOUNCE_HIT"]], DealerBJCheck = [["LUCKY CHECK"]] };
        var (_, effects) = GameEngine.Apply(state, new AnnounceDealerHit(), t);
        Assert.Equal("ANNOUNCE_HIT", ((SendChat)effects[0]).Text);
    }
}

public class LastRoundPushersTests
{
    [Fact]
    public void GoToPayout_SetsPushersForPushingPlayers()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(HandState.Stand, 10, 7)
            .Player("Lorah", "100", HandState.Stand, 10, 7)
            .Player("Bekki", "100", HandState.Stand, 10, 6)
            .Build();
        var (newState, _) = GameEngine.Apply(state, new GoToPayout());
        Assert.Contains("Lorah", newState.LastRoundPushers);
        Assert.DoesNotContain("Bekki", newState.LastRoundPushers);
    }

    [Fact]
    public void GoToPayout_Winner_NotInPushers()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(HandState.Stand, 10, 7)
            .Player("Lorah", "100", HandState.Stand, 10, 9)
            .Build();
        var (newState, _) = GameEngine.Apply(state, new GoToPayout());
        Assert.DoesNotContain("Lorah", newState.LastRoundPushers);
    }

    [Fact]
    public void NewRound_PreservesLastRoundPushers()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .LastRoundPushers("Lorah")
            .Player("Lorah")
            .Build();
        var (newState, _) = GameEngine.Apply(state, new NewRound());
        Assert.Contains("Lorah", newState.LastRoundPushers);
    }
}

public class FiveCardCharlieTests
{
    private static GameState CharlieState(FiveCardCharlieRule rule, int[] playerCards, int[] dealerCards) =>
        new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .Charlie(rule)
            .Dealer(HandState.Stand, dealerCards)
            .Player("Lorah", "100", HandState.Charlie, playerCards)
            .Build();

    [Fact]
    public void ComputeHandState_FiveCards_Disabled_IsPlaying()
    {
        var state = GameEngine.ComputeHandState([2, 3, 4, 5, 6], HandState.Playing, false, false);
        Assert.Equal(HandState.Playing, state);
    }

    [Fact]
    public void ComputeHandState_FiveCards_Enabled_IsCharlie()
    {
        var state = GameEngine.ComputeHandState([2, 3, 4, 5, 6], HandState.Playing, false, true);
        Assert.Equal(HandState.Charlie, state);
    }

    [Fact]
    public void ComputeHandState_FourCards_Enabled_IsPlaying()
    {
        var state = GameEngine.ComputeHandState([2, 3, 4, 5], HandState.Playing, false, true);
        Assert.Equal(HandState.Playing, state);
    }

    [Fact]
    public void Charlie_BeatsAll_WinsAgainstDealer()
    {
        var state = CharlieState(FiveCardCharlieRule.BeatsAll, [2, 3, 4, 5, 6], [10, 7]);
        Assert.Equal(PayoutResult.CharlieWin, GameEngine.GetPayoutResult(state, 0));
    }

    [Fact]
    public void Charlie_BeatsAll_WinsAgainstDealerBJ()
    {
        var state = CharlieState(FiveCardCharlieRule.BeatsAll, [2, 3, 4, 5, 6], [1, 10]);
        Assert.Equal(PayoutResult.CharlieWin, GameEngine.GetPayoutResult(state, 0));
    }

    [Fact]
    public void Charlie_LosesToDealerBJ_WinsNormally()
    {
        var state = CharlieState(FiveCardCharlieRule.LosesToDealerBJ, [2, 3, 4, 5, 6], [10, 7]);
        Assert.Equal(PayoutResult.CharlieWin, GameEngine.GetPayoutResult(state, 0));
    }

    [Fact]
    public void Charlie_LosesToDealerBJ_LosesAgainstDealerBJ()
    {
        var state = CharlieState(FiveCardCharlieRule.LosesToDealerBJ, [2, 3, 4, 5, 6], [1, 10]);
        Assert.Equal(PayoutResult.Lose, GameEngine.GetPayoutResult(state, 0));
    }

    [Fact]
    public void Charlie_PayoutDelta_IsEvenMoney()
    {
        var state = CharlieState(FiveCardCharlieRule.BeatsAll, [2, 3, 4, 5, 6], [10, 7]);
        Assert.Equal("+100", GameEngine.PayoutAmountString(state, 0));
    }

    [Theory]
    [InlineData(PayoutRatio.ThreeToTwo, "+150")]   // 100 * 1.5 = 150
    [InlineData(PayoutRatio.SixToFive,  "+120")]   // 100 * 1.2 = 120
    [InlineData(PayoutRatio.EvenMoney,  "+100")]   // 100 * 1.0 = 100
    public void CharliePayoutAmounts(PayoutRatio payout, string expected)
    {
        var state = CharlieState(FiveCardCharlieRule.BeatsAll, [2, 3, 4, 5, 6], [10, 7]);
        state.CharliePayout = payout;
        Assert.Equal(expected, GameEngine.PayoutAmountString(state, 0));
    }

    [Fact]
    public void AddPlayerCard_FifthCard_Enabled_NarratesCharlie()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Charlie(FiveCardCharlieRule.BeatsAll)
            .ActiveHand(0, 0)
            .Dealer(7)
            .Player("Lorah", "100", 2, 3, 4, 5)
            .Build();
        var (newState, effects) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 6));
        Assert.Equal(HandState.Charlie, newState.Players[0].Hands[0].State);
        Assert.Contains(effects.OfType<SendChat>(), e => e.Text.Contains("Five Card Charlie"));
    }

    [Fact]
    public void NewRound_PreservesFiveCardCharlieRule()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .Charlie(FiveCardCharlieRule.LosesToDealerBJ)
            .Player("Lorah")
            .Build();
        var (newState, _) = GameEngine.Apply(state, new NewRound());
        Assert.Equal(FiveCardCharlieRule.LosesToDealerBJ, newState.FiveCardCharlie);
    }
}

public class SittingOutTests
{
    private static GameState BettingWithPlayers() => new GameStateBuilder()
        .Phase(GamePhase.Betting)
        .Player("Lorah", "100")
        .Player("Bekki", "100")
        .Build();

    [Fact]
    public void ToggleSittingOut_SetsSittingOut()
    {
        var (ns, _) = GameEngine.Apply(BettingWithPlayers(), new ToggleSittingOut(1));
        Assert.True(ns.Players[1].SittingOut);
        Assert.False(ns.Players[0].SittingOut);
    }

    [Fact]
    public void ToggleSittingOut_Toggles()
    {
        var (s1, _) = GameEngine.Apply(BettingWithPlayers(), new ToggleSittingOut(0));
        var (s2, _) = GameEngine.Apply(s1, new ToggleSittingOut(0));
        Assert.False(s2.Players[0].SittingOut);
    }

    [Fact]
    public void ToggleSittingOut_IgnoredOutsideBetting()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Player("Lorah")
            .Build();
        var (ns, _) = GameEngine.Apply(state, new ToggleSittingOut(0));
        Assert.False(ns.Players[0].SittingOut);
    }

    [Fact]
    public void NewRound_PreservesSittingOut()
    {
        var state = BettingWithPlayers();
        state.Players[1].SittingOut = true;
        var (ns, _) = GameEngine.Apply(state, new NewRound());
        Assert.False(ns.Players[0].SittingOut);
        Assert.True(ns.Players[1].SittingOut);
    }

    [Fact]
    public void IsDealComplete_SittingOutPlayerExcluded()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .Dealer(7)
            .Player(new Player { Nickname = "Lorah", Hands = [new Hand { Cards = [5, 10] }] })
            .Player(new Player { Nickname = "Bekki", SittingOut = true, Hands = [new Hand()] })
            .Build();
        Assert.True(GameEngine.IsDealComplete(state));
    }

    [Fact]
    public void AdvanceFrom_SkipsSittingOutPlayer()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Dealer(7)
            .Player(new Player { Nickname = "Lorah", SittingOut = true, Hands = [new Hand()] })
            .Player(new Player { Nickname = "Bekki", Hands = [new Hand { Cards = [6, 10], State = HandState.Playing }] })
            .Build();
        var (ns, _) = GameEngine.Apply(state, new BeginPlayerTurns());
        Assert.Equal(1, ns.ActivePlayerIndex);
    }

    [Fact]
    public void GoToPayout_SittingOutPlayer_NotNarrated()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(HandState.Playing, 7, 10)
            .Player(new Player { Nickname = "Lorah", Bet = "100", Hands = [new Hand { Cards = [10, 8], State = HandState.Playing }] })
            .Player(new Player { Nickname = "Bekki", SittingOut = true, Bet = "100", Hands = [new Hand()] })
            .Build();
        var (_, effects) = GameEngine.Apply(state, new GoToPayout());
        var chat = effects.OfType<SendChat>().Select(e => e.Text).ToList();
        Assert.DoesNotContain(chat, line => line.Contains("Bekki"));
    }
}

public class BankLedgerTests
{
    private static (long NewBalance, BankTransactionEntry Entry) Apply(long balance, IBankTransaction tx)
        => BankLedger.Apply(balance, tx, default);

    // ── Credits ───────────────────────────────────────────────────────────────

    [Fact]
    public void Deposit_IncreasesBalance()
    {
        var (bal, entry) = Apply(1000, new BankDeposit(500));
        Assert.Equal(1500, bal);
        Assert.Equal(1500, entry.Balance);
        Assert.Equal(500,  entry.Amount);
        Assert.Equal(BankTransactionKind.Deposit, entry.Kind);
    }

    [Fact]
    public void Win_IncreasesBalance()
    {
        var (bal, entry) = Apply(1000, new BankWin(300));
        Assert.Equal(1300, bal);
        Assert.Equal(BankTransactionKind.Win, entry.Kind);
    }

    [Fact]
    public void Win_ZeroAmount_BalanceUnchanged()
    {
        var (bal, _) = Apply(1000, new BankWin(0));
        Assert.Equal(1000, bal);
    }

    [Fact]
    public void Surrender_IncreasesBalance_AndLabelsCorrectly()
    {
        // After a surrender of bet 100: deal-start BankBet(100) leaves bank at 900,
        // settlement BankSurrender(50) refunds half. Net: bank = 950 (-50 vs start).
        var (afterBet, _) = Apply(1000, new BankBet(100));
        Assert.Equal(900, afterBet);
        var (afterRefund, entry) = Apply(afterBet, new BankSurrender(50));
        Assert.Equal(950, afterRefund);
        Assert.Equal(BankTransactionKind.Surrender, entry.Kind);
        Assert.Equal(50, entry.Amount);
    }

    // ── Debits - normal ────────────────────────────────────────────────────────

    [Fact]
    public void Withdrawal_DecreasesBalance()
    {
        var (bal, entry) = Apply(1000, new BankWithdrawal(400));
        Assert.Equal(600, bal);
        Assert.Equal(BankTransactionKind.Withdrawal, entry.Kind);
    }

    [Fact]
    public void Bet_DecreasesBalance()
    {
        var (bal, entry) = Apply(1000, new BankBet(1000));
        Assert.Equal(0, bal);
        Assert.Equal(BankTransactionKind.Bet, entry.Kind);
    }

    [Fact]
    public void DoubleDown_DecreasesBalance()
    {
        var (bal, entry) = Apply(1000, new BankDoubleDown(500));
        Assert.Equal(500, bal);
        Assert.Equal(BankTransactionKind.DoubleDown, entry.Kind);
    }

    [Fact]
    public void Split_DecreasesBalance()
    {
        var (bal, entry) = Apply(1000, new BankSplit(500));
        Assert.Equal(500, bal);
        Assert.Equal(BankTransactionKind.Split, entry.Kind);
    }

    // ── Debits - floor at zero, never negative ─────────────────────────────────

    [Fact]
    public void Withdrawal_ExceedsBalance_ClampsToZero()
    {
        var (bal, _) = Apply(100, new BankWithdrawal(999));
        Assert.Equal(0, bal);
    }

    [Fact]
    public void Bet_ExceedsBalance_ClampsToZero()
    {
        var (bal, _) = Apply(0, new BankBet(1000));
        Assert.Equal(0, bal);
    }

    [Fact]
    public void DoubleDown_ExceedsBalance_ClampsToZero()
    {
        // Player traded to cover the double - bank may be 0; deduction never goes negative
        var (bal, _) = Apply(0, new BankDoubleDown(1000));
        Assert.Equal(0, bal);
    }

    [Fact]
    public void Split_ExceedsBalance_ClampsToZero()
    {
        var (bal, _) = Apply(0, new BankSplit(1000));
        Assert.Equal(0, bal);
    }

    [Fact]
    public void DoubleDown_BankExactlyCoversBet_BalanceIsZero()
    {
        var (bal, entry) = Apply(500, new BankDoubleDown(500));
        Assert.Equal(0, bal);
        Assert.Equal(0, entry.Balance);
    }

    // ── Sequence - bet-all then double via trade ────────────────────────────────

    [Fact]
    public void BetAll_ThenDepositForDouble_ThenDoubleDown_CorrectSequence()
    {
        // Simulates: player banks 1000, bets 1000, deposits 1000 for double, double deducted at confirm
        long bank = 1000;
        (bank, _) = Apply(bank, new BankBet(1000));       // StartDeal
        Assert.Equal(0, bank);

        (bank, _) = Apply(bank, new BankDeposit(1000));   // trade received, deposited
        Assert.Equal(1000, bank);

        (bank, _) = Apply(bank, new BankDoubleDown(1000)); // Confirm Dbl
        Assert.Equal(0, bank);
    }

    [Fact]
    public void BetAll_ThenDepositExtra_ThenDoubleDown_ExcessStaysInBank()
    {
        // Player trades 2000 (only 1000 needed for double) - excess stays in bank
        long bank = 1000;
        (bank, _) = Apply(bank, new BankBet(1000));
        Assert.Equal(0, bank);

        (bank, _) = Apply(bank, new BankDeposit(2000));
        Assert.Equal(2000, bank);

        (bank, _) = Apply(bank, new BankDoubleDown(1000));
        Assert.Equal(1000, bank);
    }

    // ── Log entry fields ───────────────────────────────────────────────────────

    [Fact]
    public void Entry_BalanceReflectsPostTransactionState()
    {
        var (_, entry) = Apply(800, new BankBet(300));
        Assert.Equal(500, entry.Balance); // post-deduction balance
        Assert.Equal(300, entry.Amount);
    }

    [Fact]
    public void Entry_PreservesTimestamp()
    {
        var t = new System.DateTime(2025, 1, 15, 12, 0, 0);
        var (_, entry) = BankLedger.Apply(0, new BankDeposit(1), t);
        Assert.Equal(t, entry.Timestamp);
    }

    // ── BetAdjust - signed delta semantics ─────────────────────────────────────

    [Fact]
    public void BetAdjust_PositiveDelta_DeductsFromBank()
    {
        var (bal, entry) = Apply(1000, new BankBetAdjust(300));
        Assert.Equal(700, bal);
        Assert.Equal(700, entry.Balance);
        Assert.Equal(300, entry.Amount);
        Assert.Equal(BankTransactionKind.BetAdjust, entry.Kind);
    }

    [Fact]
    public void BetAdjust_NegativeDelta_RefundsToBank()
    {
        var (bal, entry) = Apply(500, new BankBetAdjust(-200));
        Assert.Equal(700, bal);
        Assert.Equal(700, entry.Balance);
        Assert.Equal(-200, entry.Amount); // signed amount preserves direction
        Assert.Equal(BankTransactionKind.BetAdjust, entry.Kind);
    }

    [Fact]
    public void BetAdjust_ZeroDelta_BalanceUnchanged()
    {
        var (bal, entry) = Apply(1000, new BankBetAdjust(0));
        Assert.Equal(1000, bal);
        Assert.Equal(0,    entry.Amount);
    }

    [Fact]
    public void BetAdjust_PositiveDeltaExceedingBalance_ClampsToZero()
    {
        // Caller is responsible for pre-validating; if not, balance still floors at 0.
        var (bal, _) = Apply(100, new BankBetAdjust(500));
        Assert.Equal(0, bal);
    }

    [Fact]
    public void BetAdjust_Sequence_BetIncreaseThenDecrease_NetsCorrectly()
    {
        // Player bets 1000 at deal start (deducted), then bumps bet to 1500 (additional 500 out),
        // then drops to 800 (refund 700). Bank should end at: 1000 - 1000 + 0 - 500 + 700 = 200.
        long bank = 1000;
        (bank, _) = Apply(bank, new BankBet(1000));         // initial deal
        Assert.Equal(0, bank);

        (bank, _) = Apply(bank, new BankDeposit(1000));     // player traded 1000
        Assert.Equal(1000, bank);

        (bank, _) = Apply(bank, new BankBetAdjust(500));    // bet up by 500
        Assert.Equal(500, bank);

        (bank, _) = Apply(bank, new BankBetAdjust(-700));   // bet down by 700
        Assert.Equal(1200, bank);
    }

    // ── Credit (VIP / free play) ───────────────────────────────────────────────

    [Fact]
    public void Credit_IncreasesBalance_AndLabelsCorrectly()
    {
        var (bal, entry) = Apply(0, new BankCredit(500));
        Assert.Equal(500, bal);
        Assert.Equal(500, entry.Balance);
        Assert.Equal(500, entry.Amount);
        Assert.Equal(BankTransactionKind.Credit, entry.Kind);
    }

    [Fact]
    public void Credit_StacksOntoExistingBalance()
    {
        var (bal, _) = Apply(1000, new BankCredit(500));
        Assert.Equal(1500, bal);
    }
}

public class AdjustBetTests
{
    [Fact]
    public void AdjustBet_DealPhase_UpdatesPlayerBet()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .Player("Lorah", "500")
            .Build();
        var (next, _) = GameEngine.Apply(state, new AdjustBet(0, "1000"));
        Assert.Equal("1000", next.Players[0].Bet);
    }

    [Fact]
    public void AdjustBet_OutsideDealPhase_IsNoop()
    {
        var bettingState = new GameStateBuilder()
            .Phase(GamePhase.Betting)
            .Player("Lorah", "500")
            .Build();
        var (next, _) = GameEngine.Apply(bettingState, new AdjustBet(0, "1000"));
        Assert.Equal("500", next.Players[0].Bet);

        var playerTurnsState = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Player("Lorah", "500", 5, 6)
            .Build();
        (next, _) = GameEngine.Apply(playerTurnsState, new AdjustBet(0, "1000"));
        Assert.Equal("500", next.Players[0].Bet);
    }

    [Fact]
    public void AdjustBet_SittingOutPlayer_IsNoop()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .Player(new Player
            {
                Nickname   = "Lorah",
                Bet        = "500",
                SittingOut = true,
                Hands      = [new Hand()],
            })
            .Build();
        var (next, _) = GameEngine.Apply(state, new AdjustBet(0, "1000"));
        Assert.Equal("500", next.Players[0].Bet);
    }

    [Fact]
    public void AdjustBet_DoesNotEmitNarration()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .Player("Lorah", "500")
            .Build();
        var (_, effects) = GameEngine.Apply(state, new AdjustBet(0, "1000"));
        Assert.Empty(effects);
    }

    [Fact]
    public void AdjustBet_DoesNotPushUndo()
    {
        Assert.False(new AdjustBet(0, "100").PushesUndo);
    }
}

public class GameActionPushesUndoTests
{
    public static System.Collections.Generic.IEnumerable<object[]> NonPushingActions => new[]
    {
        new object[] { new AnnounceBettingOpen() },
        new object[] { new AnnounceBetRequest(0) },
        new object[] { new AnnounceBetConfirm(0, 0) },
        new object[] { new AnnounceBankRemind(0, 0) },
        new object[] { new AnnounceBankShortfall(0, 0) },
        new object[] { new AnnounceBankDeposit(0, 0, 0) },
        new object[] { new AnnounceBankWithdraw(0, 0, 0) },
        new object[] { new AnnounceDouble(0, 0) },
        new object[] { new AnnounceDoubleConfirm(0, 0) },
        new object[] { new AnnounceSplit(0, 0) },
        new object[] { new AnnounceDealerHit() },
        new object[] { new AnnouncePlayerHit(0, 0) },
        new object[] { new AnnouncePlayerTurn(0, 0) },
        new object[] { new AnnouncePlayerDeal(0) },
        new object[] { new AnnounceDealerDeal() },
        new object[] { new BeginDealerTurn() },
        new object[] { new AdjustBet(0, "100") },
    };

    [Theory]
    [MemberData(nameof(NonPushingActions))]
    public void NarrationOnlyAndBeginDealerTurn_DoNotPushUndo(GameAction action)
    {
        Assert.False(action.PushesUndo);
    }

    public static System.Collections.Generic.IEnumerable<object[]> PushingActions => new[]
    {
        new object[] { new AddDealerCard(7) },
        new object[] { new AddPlayerCard(0, 0, 7) },
        new object[] { new StandPlayer(0, 0) },
        new object[] { new DoubleDown(0, 0) },
        new object[] { new SplitHand(0, 0) },
        new object[] { new SurrenderHand(0, 0) },
        new object[] { new StartDeal() },
        new object[] { new BeginPlayerTurns() },
        new object[] { new AdvanceToNextPlayer() },
        new object[] { new GoToPayout() },
        new object[] { new NewRound() },
        new object[] { new AddPlayer("Lorah") },
        new object[] { new RemovePlayer(0) },
        new object[] { new SetPlayerBet(0, "100") },
        new object[] { new RenamePlayer(0, "Lorah") },
        new object[] { new ToggleSittingOut(0) },
    };

    [Theory]
    [MemberData(nameof(PushingActions))]
    public void StateChangingActions_PushUndo(GameAction action)
    {
        Assert.True(action.PushesUndo);
    }
}

public class SurrenderTests
{
    private static GameState SurrenderableState(bool allowSurrender = true) =>
        new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Dealer(10)
            .AllowSurrender(allowSurrender)
            .Player("Lorah", "100", HandState.Playing, 10, 6)
            .Build();

    [Fact]
    public void CanSurrender_2CardInitialHand_True()
    {
        var hand = new Hand { Cards = [10, 6], State = HandState.Playing };
        Assert.True(GameEngine.CanSurrender(hand, allowSurrender: true));
    }

    [Fact]
    public void CanSurrender_OptionOff_False()
    {
        var hand = new Hand { Cards = [10, 6], State = HandState.Playing };
        Assert.False(GameEngine.CanSurrender(hand, allowSurrender: false));
    }

    [Fact]
    public void CanSurrender_AfterHit_False()
    {
        var hand = new Hand { Cards = [10, 6, 2], State = HandState.Playing };
        Assert.False(GameEngine.CanSurrender(hand, allowSurrender: true));
    }

    [Fact]
    public void CanSurrender_FromSplit_False()
    {
        var hand = new Hand { Cards = [10, 6], State = HandState.Playing, IsFromSplit = true };
        Assert.False(GameEngine.CanSurrender(hand, allowSurrender: true));
    }

    [Fact]
    public void CanSurrender_AfterDouble_False()
    {
        var hand = new Hand { Cards = [10, 6], State = HandState.Playing, Doubled = true, Bet = "200" };
        Assert.False(GameEngine.CanSurrender(hand, allowSurrender: true));
    }

    [Fact]
    public void Apply_Surrender_MarksHandSurrendered()
    {
        var state = SurrenderableState();
        var (ns, _) = GameEngine.Apply(state, new SurrenderHand(0, 0));
        Assert.Equal(HandState.Surrendered, ns.Players[0].Hands[0].State);
    }

    [Fact]
    public void Apply_Surrender_NoOp_WhenSurrenderDisallowed()
    {
        var state = SurrenderableState(allowSurrender: false);
        var (ns, _) = GameEngine.Apply(state, new SurrenderHand(0, 0));
        Assert.Equal(HandState.Playing, ns.Players[0].Hands[0].State);
    }

    [Fact]
    public void Surrender_Payout_LosesHalfBet()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .Dealer(HandState.Stand, 10, 7)
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "100",
                Hands = [new Hand { Cards = [10, 6], State = HandState.Surrendered }],
            })
            .Build();
        Assert.Equal(PayoutResult.Surrender, GameEngine.GetPayoutResult(state, 0));
        Assert.Equal(-50m, GameEngine.PayoutDelta(state, 0));
        // Bank gets half back at settlement.
        Assert.Equal(50m, GameEngine.PayoutTotalOwed(state, 0));
    }

    [Fact]
    public void Surrender_LosesHalf_EvenAgainstDealerBJ()
    {
        // ENHC: surrender takes priority, half bet forfeit regardless of dealer BJ.
        var state = new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .Dealer(HandState.Blackjack, 1, 10)
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "100",
                Hands = [new Hand { Cards = [10, 6], State = HandState.Surrendered }],
            })
            .Build();
        Assert.Equal(PayoutResult.Surrender, GameEngine.GetPayoutResult(state, 0));
        Assert.Equal(-50m, GameEngine.PayoutDelta(state, 0));
    }

    [Fact]
    public void Surrender_OddBet_RoundsUpForHouseFavor()
    {
        // 101 / 2 = 50.5; Math.Ceiling makes the player forfeit 51, keeping 50.
        var state = new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .Dealer(HandState.Stand, 10, 7)
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "101",
                Hands = [new Hand { Cards = [10, 6], State = HandState.Surrendered }],
            })
            .Build();
        Assert.Equal(-51m, GameEngine.PayoutDelta(state, 0));
        Assert.Equal(50m,  GameEngine.PayoutTotalOwed(state, 0));
    }

    [Fact]
    public void Surrender_AllPlayersSurrender_GoesStraightToPayout()
    {
        // After the last player surrenders, AdvanceFrom finds no Playing hands and
        // every hand is settled-loss. The transition should go to DealerTurn with
        // WaitingForDealer=false so the dealer can click "Go to Payout".
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Dealer(10)
            .AllowSurrender(true)
            .Player("Lorah", "100", HandState.Playing, 10, 6)
            .Build();
        var (ns, _) = GameEngine.Apply(state, new SurrenderHand(0, 0));
        Assert.Equal(GamePhase.DealerTurn, ns.Phase);
        Assert.False(ns.WaitingForDealer);
        Assert.True(GameEngine.CanGoToPayout(ns));
    }

    [Fact]
    public void Surrender_FirstOfTwoPlayers_SetsWaitingForNextPlayer()
    {
        // Player 0 surrenders. Player 1 still has cards in play, so the engine
        // should hold on player 0 with WaitingForNextPlayer=true (mirroring Stand).
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Dealer(10)
            .AllowSurrender(true)
            .Player("Lorah", "100", HandState.Playing, 10, 6)
            .Player("Bekki", "100", HandState.Playing, 9, 8)
            .Build();
        var (ns, _) = GameEngine.Apply(state, new SurrenderHand(0, 0));

        Assert.Equal(HandState.Surrendered, ns.Players[0].Hands[0].State);
        Assert.Equal(GamePhase.PlayerTurns, ns.Phase);
        Assert.True(ns.WaitingForNextPlayer);
        Assert.Equal(0, ns.ActivePlayerIndex); // stays on Lorah until Next Player clicked
        Assert.False(ns.WaitingForDealer);
    }

    [Fact]
    public void Surrender_AdvanceToNextPlayer_MovesToNextPlayingHand()
    {
        // After player 0 surrenders, clicking Next Player should advance to player 1.
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Dealer(10)
            .AllowSurrender(true)
            .Player("Lorah", "100", HandState.Playing, 10, 6)
            .Player("Bekki", "100", HandState.Playing, 9, 8)
            .Build();
        var (afterSurrender, _) = GameEngine.Apply(state, new SurrenderHand(0, 0));
        var (advanced, _)       = GameEngine.Apply(afterSurrender, new AdvanceToNextPlayer());

        Assert.Equal(GamePhase.PlayerTurns, advanced.Phase);
        Assert.Equal(1, advanced.ActivePlayerIndex);
        Assert.Equal(0, advanced.ActiveHandIndex);
        Assert.False(advanced.WaitingForNextPlayer);
    }

    [Fact]
    public void Surrender_LastPlayer_TransitionsToDealerTurn()
    {
        // Two-player round: player 0 already stood, player 1 surrenders. Engine
        // should jump straight to DealerTurn. Dealer upcard is 7 (no possible
        // dealer BJ), so WaitingForDealer must be true so the dealer actually plays
        // against Bekki's standing hand.
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(1, 0)
            .Dealer(7)
            .AllowSurrender(true)
            .Player("Lorah", "100", HandState.Stand,   10, 8)
            .Player("Bekki", "100", HandState.Playing, 10, 6)
            .Build();
        var (ns, _) = GameEngine.Apply(state, new SurrenderHand(1, 0));

        Assert.Equal(HandState.Surrendered, ns.Players[1].Hands[0].State);
        Assert.Equal(GamePhase.DealerTurn, ns.Phase);
        Assert.True(ns.WaitingForDealer);
        Assert.False(ns.WaitingForNextPlayer);
    }
}

// Combinations of rules that interact with each other. Each test sets up the
// minimum state to exercise the intersection and asserts the resulting hand
// state / action eligibility.
public class RuleInteractionTests
{
    private static GameState BuildSplitAcePair(bool hsa, bool rsa)
    {
        // Player has [A] from a previous split; we add another A so the post-deal
        // hand becomes [1, 1] IsFromSplit.
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Dealer(10)
            .HitSplitAces(hsa)
            .ResplitAces(rsa)
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "100",
                Hands = [new Hand { Cards = [1], State = HandState.Playing, IsFromSplit = true }],
            })
            .Build();
        var (ns, _) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 1));
        return ns;
    }

    [Fact]
    public void RsaOff_HsaOff_AcePair_AutoStands()
    {
        var ns = BuildSplitAcePair(hsa: false, rsa: false);
        var hand = ns.Players[0].Hands[0];
        Assert.Equal(HandState.Stand, hand.State);
        Assert.False(GameEngine.CanSplit(hand, new Player { Hands = [hand] }, resplitAces: false, ResplitCap.Unlimited));
        Assert.False(GameEngine.CanHit(hand, hitSplitAces: false));
    }

    [Fact]
    public void RsaOff_HsaOn_AcePair_Playing_CanHitOnly()
    {
        var ns = BuildSplitAcePair(hsa: true, rsa: false);
        var hand = ns.Players[0].Hands[0];
        Assert.Equal(HandState.Playing, hand.State);
        Assert.False(GameEngine.CanSplit(hand, new Player { Hands = [hand] }, resplitAces: false, ResplitCap.Unlimited));
        Assert.True(GameEngine.CanHit(hand, hitSplitAces: true));
        Assert.True(GameEngine.CanStand(hand));
    }

    [Fact]
    public void RsaOn_HsaOff_AcePair_Playing_CanSplitNotHit()
    {
        var ns = BuildSplitAcePair(hsa: false, rsa: true);
        var hand = ns.Players[0].Hands[0];
        Assert.Equal(HandState.Playing, hand.State);
        Assert.True(GameEngine.CanSplit(hand, new Player { Hands = [hand] }, resplitAces: true, ResplitCap.Unlimited));
        Assert.False(GameEngine.CanHit(hand, hitSplitAces: false));
        Assert.True(GameEngine.CanStand(hand));
    }

    [Fact]
    public void RsaOn_HsaOn_AcePair_Playing_AllActions()
    {
        var ns = BuildSplitAcePair(hsa: true, rsa: true);
        var hand = ns.Players[0].Hands[0];
        Assert.Equal(HandState.Playing, hand.State);
        Assert.True(GameEngine.CanSplit(hand, new Player { Hands = [hand] }, resplitAces: true, ResplitCap.Unlimited));
        Assert.True(GameEngine.CanHit(hand, hitSplitAces: true));
        Assert.True(GameEngine.CanStand(hand));
    }

    [Fact]
    public void RsaOff_HsaOff_NonAcePostSplit_AutoStands()
    {
        // [A, 5] from split with HSA off auto-stands regardless of RSA.
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Dealer(10)
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "100",
                Hands = [new Hand { Cards = [1], State = HandState.Playing, IsFromSplit = true }],
            })
            .Build();
        var (ns, _) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 5));
        Assert.Equal(HandState.Stand, ns.Players[0].Hands[0].State);
    }

    [Fact]
    public void RsaOn_HsaOff_NonAcePostSplit_StillAutoStands()
    {
        // RSA on but the second card isn't an ace - no resplit possible, so the
        // hand should still auto-stand under HSA off.
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .ActiveHand(0, 0)
            .Dealer(10)
            .ResplitAces(true)
            .Player(new Player
            {
                Nickname = "Lorah", Bet = "100",
                Hands = [new Hand { Cards = [1], State = HandState.Playing, IsFromSplit = true }],
            })
            .Build();
        var (ns, _) = GameEngine.Apply(state, new AddPlayerCard(0, 0, 5));
        Assert.Equal(HandState.Stand, ns.Players[0].Hands[0].State);
    }

    [Fact]
    public void DasOff_HsaOn_NoDoubleAfterSplitHit()
    {
        // Split-ace hand of [A, 5] (soft 16) with HSA on - the player can hit, but
        // with DAS off cannot double on the 2-card hand.
        var hand = new Hand { Cards = [1, 5], State = HandState.Playing, IsFromSplit = true };
        Assert.False(GameEngine.CanDouble(hand, "100", doubleAfterSplit: false, DoubleRestriction.Any));
        Assert.True(GameEngine.CanDouble(hand, "100", doubleAfterSplit: true, DoubleRestriction.Any));
    }

    [Fact]
    public void EightsSplit_DasOff_NoDouble()
    {
        // Pair-of-8s split, post-deal becomes [8, c]; with DAS off, no double.
        var hand = new Hand { Cards = [8, 5], State = HandState.Playing, IsFromSplit = true };
        Assert.False(GameEngine.CanDouble(hand, "100", doubleAfterSplit: false, DoubleRestriction.Any));
    }

    [Fact]
    public void NonSplit_DasOff_DoubleStillAllowed()
    {
        // DAS off must not block doubling on the original (non-split) hand.
        var hand = new Hand { Cards = [5, 6], State = HandState.Playing, IsFromSplit = false };
        Assert.True(GameEngine.CanDouble(hand, "100", doubleAfterSplit: false, DoubleRestriction.Any));
    }

    [Fact]
    public void S17_AllPlayerBJ_CanGoToPayoutImmediately()
    {
        // With S17 and all players BJ, dealer with soft-17 upcard shouldn't need
        // to play out the soft 17 - the standard "all-BJ" shortcut still applies
        // because the dealer reveal-on-BJ-check covers it.
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(HandState.Playing, 4) // safe upcard for BJ check
            .DealerStandsOnSoft17()
            .Player("Lorah", "100", HandState.Blackjack, 1, 10)
            .Build();
        Assert.True(GameEngine.CanGoToPayout(state));
    }

    [Fact]
    public void S17_DealerSoft17_StandsImmediately()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.DealerTurn)
            .Dealer(1, 6)
            .DealerStandsOnSoft17()
            .Player("Lorah", "100", HandState.Stand, 10, 8)
            .Build();
        Assert.False(GameEngine.CanHitDealer(state));
        Assert.True(GameEngine.CanGoToPayout(state));
    }
}

public class ResplitCapTests
{
    // Helper: build a player who already holds `handCount` Playing pair hands of
    // the same rank. Used to test the cap independently of the deal sequence.
    private static Player PairPlayer(int rank, int handCount, bool fromSplit = true)
    {
        var hands = new Hand[handCount];
        for (var i = 0; i < handCount; i++)
            hands[i] = new Hand { Cards = [rank, rank], State = HandState.Playing, IsFromSplit = fromSplit };
        return new Player { Nickname = "Lorah", Bet = "100", Hands = [..hands] };
    }

    [Theory]
    [InlineData(ResplitCap.Max2, 2, false)]
    [InlineData(ResplitCap.Max3, 2, true)]
    [InlineData(ResplitCap.Max3, 3, false)]
    [InlineData(ResplitCap.Max4, 3, true)]
    [InlineData(ResplitCap.Max4, 4, false)]
    [InlineData(ResplitCap.Unlimited, 8, true)]
    public void NonAce_CapBlocksAtLimit(ResplitCap cap, int existingHands, bool expectCanSplit)
    {
        var player = PairPlayer(rank: 8, handCount: existingHands);
        var hand   = player.Hands[0];
        Assert.Equal(expectCanSplit,
            GameEngine.CanSplit(hand, player, resplitAces: false, cap));
    }

    [Theory]
    [InlineData(ResplitCap.Max2)]
    [InlineData(ResplitCap.Max3)]
    [InlineData(ResplitCap.Max4)]
    public void Aces_IgnoreCap_WhenRsaOn(ResplitCap cap)
    {
        // Player already has 8 ace hands - far above any numeric cap. With RSA on,
        // splitting must still be allowed: aces are governed only by ResplitAces.
        var player = PairPlayer(rank: 1, handCount: 8);
        var hand   = player.Hands[0];
        Assert.True(GameEngine.CanSplit(hand, player, resplitAces: true, cap));
    }

    [Theory]
    [InlineData(ResplitCap.Max2)]
    [InlineData(ResplitCap.Max4)]
    [InlineData(ResplitCap.Unlimited)]
    public void Aces_RsaOff_NoResplitRegardlessOfCap(ResplitCap cap)
    {
        // A from-split ace pair with RSA off: never splittable, regardless of cap.
        var player = PairPlayer(rank: 1, handCount: 2);
        var hand   = player.Hands[0];
        Assert.False(GameEngine.CanSplit(hand, player, resplitAces: false, cap));
    }

    [Fact]
    public void OriginalAcePair_AlwaysSplittable_RegardlessOfCap()
    {
        // First-time ace pair (not from a previous split): always splittable, even
        // with a strict cap and RSA off.
        var player = new Player
        {
            Nickname = "Lorah", Bet = "100",
            Hands = [new Hand { Cards = [1, 1], State = HandState.Playing, IsFromSplit = false }],
        };
        Assert.True(GameEngine.CanSplit(player.Hands[0], player, resplitAces: false, ResplitCap.Max2));
    }

    // Solver: edge should be monotonically non-increasing (in house-favor terms)
    // as the cap loosens. Tighter cap removes player options, so house edge rises.
    [Fact]
    public void SolverEdge_MonotonicWithCap()
    {
        EdgeRules Make(ResplitCap cap) => new(
            BjPayout: 1.5,
            CharliePayout: PayoutRatio.EvenMoney,
            FiveCardCharlie: FiveCardCharlieRule.Disabled,
            ResplitCap: cap);

        var e2 = EdgeSolver.ComputeHouseEdge(Make(ResplitCap.Max2));
        var e3 = EdgeSolver.ComputeHouseEdge(Make(ResplitCap.Max3));
        var e4 = EdgeSolver.ComputeHouseEdge(Make(ResplitCap.Max4));
        var eu = EdgeSolver.ComputeHouseEdge(Make(ResplitCap.Unlimited));

        Assert.True(e2 >= e3, $"Max2 ({e2}) should be >= Max3 ({e3})");
        Assert.True(e3 >= e4, $"Max3 ({e3}) should be >= Max4 ({e4})");
        Assert.True(e4 >= eu, $"Max4 ({e4}) should be >= Unlimited ({eu})");
    }

    // Solver: the difference between Max4 and Unlimited is small (<0.05% on
    // standard rules). Sanity check that the tree-model approximation isn't blowing up.
    [Fact]
    public void SolverEdge_Max4_CloseToUnlimited()
    {
        var rules = new EdgeRules(
            BjPayout: 1.5,
            CharliePayout: PayoutRatio.EvenMoney,
            FiveCardCharlie: FiveCardCharlieRule.Disabled);

        var e4 = EdgeSolver.ComputeHouseEdge(rules with { ResplitCap = ResplitCap.Max4 });
        var eu = EdgeSolver.ComputeHouseEdge(rules with { ResplitCap = ResplitCap.Unlimited });

        Assert.True(Math.Abs(e4 - eu) < 0.001,
            $"Max4 ({e4:P4}) should be within 0.1% of Unlimited ({eu:P4})");
    }
}

public class DoubleRestrictionTests
{
    private static Hand TwoCard(int a, int b) =>
        new() { Cards = [a, b], State = HandState.Playing };

    [Theory]
    // Any: every 2-card hand qualifies
    [InlineData(DoubleRestriction.Any,        2, 3,  true)]  // hard 5
    [InlineData(DoubleRestriction.Any,        1, 5,  true)]  // soft 16
    [InlineData(DoubleRestriction.Any,       10, 10, true)]  // hard 20
    // Hard9To11: only hard 9, 10, 11
    [InlineData(DoubleRestriction.Hard9To11,  4, 5,  true)]  // hard 9
    [InlineData(DoubleRestriction.Hard9To11,  5, 5,  true)]  // hard 10
    [InlineData(DoubleRestriction.Hard9To11,  5, 6,  true)]  // hard 11
    [InlineData(DoubleRestriction.Hard9To11,  4, 4,  false)] // hard 8
    [InlineData(DoubleRestriction.Hard9To11,  6, 6,  false)] // hard 12
    [InlineData(DoubleRestriction.Hard9To11,  1, 8,  false)] // soft 19 - not hard 9
    [InlineData(DoubleRestriction.Hard9To11,  1, 10, false)] // soft 21 / BJ
    // Hard10To11: only hard 10, 11
    [InlineData(DoubleRestriction.Hard10To11, 4, 5,  false)] // hard 9
    [InlineData(DoubleRestriction.Hard10To11, 5, 5,  true)]  // hard 10
    [InlineData(DoubleRestriction.Hard10To11, 5, 6,  true)]  // hard 11
    [InlineData(DoubleRestriction.Hard10To11, 1, 9,  false)] // soft 20
    // HardOnly: any hard total
    [InlineData(DoubleRestriction.HardOnly,   2, 3,  true)]  // hard 5
    [InlineData(DoubleRestriction.HardOnly,  10, 10, true)]  // hard 20
    [InlineData(DoubleRestriction.HardOnly,   1, 5,  false)] // soft 16
    [InlineData(DoubleRestriction.HardOnly,   1, 9,  false)] // soft 20
    public void IsDoubleableTotal_Cases(DoubleRestriction r, int c1, int c2, bool expected)
    {
        Assert.Equal(expected, GameEngine.IsDoubleableTotal([c1, c2], r));
    }

    [Fact]
    public void CanDouble_HardOnly_BlocksSoftHand()
    {
        var hand = TwoCard(1, 5); // soft 16
        Assert.True (GameEngine.CanDouble(hand, "100", doubleAfterSplit: true, DoubleRestriction.Any));
        Assert.False(GameEngine.CanDouble(hand, "100", doubleAfterSplit: true, DoubleRestriction.HardOnly));
    }

    [Fact]
    public void CanDouble_Hard9To11_OnlyAllowsRange()
    {
        Assert.True (GameEngine.CanDouble(TwoCard(5, 5), "100", true, DoubleRestriction.Hard9To11)); // 10
        Assert.False(GameEngine.CanDouble(TwoCard(4, 4), "100", true, DoubleRestriction.Hard9To11)); // 8
        Assert.False(GameEngine.CanDouble(TwoCard(6, 6), "100", true, DoubleRestriction.Hard9To11)); // 12
    }

    [Fact]
    public void CanDouble_DasAndRestriction_StackIndependently()
    {
        // Post-split hard 10: DAS off + Any → blocked by DAS. DAS on + restriction → ok.
        var hand = new Hand { Cards = [5, 5], State = HandState.Playing, IsFromSplit = true };
        Assert.False(GameEngine.CanDouble(hand, "100", doubleAfterSplit: false, DoubleRestriction.Any));
        Assert.True (GameEngine.CanDouble(hand, "100", doubleAfterSplit: true,  DoubleRestriction.Hard9To11));
        // DAS on but restriction excludes total → blocked.
        var hand2 = new Hand { Cards = [4, 4], State = HandState.Playing, IsFromSplit = true };
        Assert.False(GameEngine.CanDouble(hand2, "100", doubleAfterSplit: true, DoubleRestriction.Hard9To11));
    }

    // Solver: house edge should rise monotonically as the rule restricts double options.
    [Fact]
    public void SolverEdge_MonotonicWithRestriction()
    {
        EdgeRules Make(DoubleRestriction r) => new(
            BjPayout: 1.5,
            CharliePayout: PayoutRatio.EvenMoney,
            FiveCardCharlie: FiveCardCharlieRule.Disabled,
            DoubleRestriction: r);

        var any  = EdgeSolver.ComputeHouseEdge(Make(DoubleRestriction.Any));
        var hard = EdgeSolver.ComputeHouseEdge(Make(DoubleRestriction.HardOnly));
        var d911 = EdgeSolver.ComputeHouseEdge(Make(DoubleRestriction.Hard9To11));
        var d1011= EdgeSolver.ComputeHouseEdge(Make(DoubleRestriction.Hard10To11));

        // Each restriction removes options from a strict superset, so the edge moves
        // toward the house. HardOnly disallows soft doubling; Hard9To11 further bans
        // hard totals outside 9-11; Hard10To11 is the tightest.
        Assert.True(hard  >= any,  $"HardOnly ({hard}) >= Any ({any})");
        Assert.True(d911  >= hard, $"Hard9To11 ({d911}) >= HardOnly ({hard})");
        Assert.True(d1011 >= d911, $"Hard10To11 ({d1011}) >= Hard9To11 ({d911})");
    }
}

public class ActionLogTests
{
    [Fact]
    public void Format_PhaseTransitions()
    {
        Assert.Equal("StartDeal",        ActionLog.Format(new StartDeal()));
        Assert.Equal("BeginPlayerTurns", ActionLog.Format(new BeginPlayerTurns()));
        Assert.Equal("BeginDealerTurn",  ActionLog.Format(new BeginDealerTurn()));
        Assert.Equal("GoToPayout",       ActionLog.Format(new GoToPayout()));
        Assert.Equal("AdvancePlayer",    ActionLog.Format(new AdvanceToNextPlayer()));
    }

    [Fact]
    public void Format_CardActions()
    {
        Assert.Equal("Deal:D:7",     ActionLog.Format(new AddDealerCard(7)));
        Assert.Equal("Deal:0:0:10",  ActionLog.Format(new AddPlayerCard(0, 0, 10)));
        Assert.Equal("Deal:2:1:1",   ActionLog.Format(new AddPlayerCard(2, 1, 1)));
    }

    [Fact]
    public void Format_PlayerDecisions()
    {
        Assert.Equal("Stand:0:0", ActionLog.Format(new StandPlayer(0, 0)));
        Assert.Equal("Dbl:1:0",   ActionLog.Format(new DoubleDown(1, 0)));
        Assert.Equal("Spl:0:1",   ActionLog.Format(new SplitHand(0, 1)));
        Assert.Equal("Srn:0:0",   ActionLog.Format(new SurrenderHand(0, 0)));
    }

    [Fact]
    public void Format_AdjustBet()
    {
        Assert.Equal("AdjustBet:0:500", ActionLog.Format(new AdjustBet(0, "500")));
    }

    [Fact]
    public void Format_Skips_Announcements()
    {
        Assert.Null(ActionLog.Format(new AnnounceDealerDeal()));
        Assert.Null(ActionLog.Format(new AnnouncePlayerDeal(0)));
        Assert.Null(ActionLog.Format(new AnnounceDealerHit()));
        Assert.Null(ActionLog.Format(new AnnouncePlayerHit(0, 0)));
        Assert.Null(ActionLog.Format(new AnnounceDouble(0, 0)));
        Assert.Null(ActionLog.Format(new AnnounceBettingOpen()));
    }

    [Fact]
    public void Format_Skips_RosterAndRoundEnd()
    {
        // Roster mutations happen in Betting phase, before StartDeal opens the log.
        Assert.Null(ActionLog.Format(new AddPlayer("Lorah")));
        Assert.Null(ActionLog.Format(new RemovePlayer(0)));
        Assert.Null(ActionLog.Format(new SetPlayerBet(0, "100")));
        Assert.Null(ActionLog.Format(new RenamePlayer(0, "L")));
        Assert.Null(ActionLog.Format(new ToggleSittingOut(0)));
        // NewRound closes the current round; the next round's log starts at StartDeal.
        Assert.Null(ActionLog.Format(new NewRound()));
    }
}
