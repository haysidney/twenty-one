using System.Linq;
using TwentyOne.Game;
using TwentyOne.Tests.Helpers;
using Xunit;

namespace TwentyOne.Tests;

/// <summary>
/// Pulling a player out of a round in progress: the cash-out-right-after-the-deal
/// case (Deal phase) and the AFK/disconnect case (PlayerTurns). The bank refund is
/// MainWindow's job; these cover the state transitions only.
/// </summary>
public class WithdrawFromRoundTests
{
    private static (GameState State, System.Collections.Generic.IReadOnlyList<ISideEffect> Effects) Apply(
        GameState state, GameAction action) =>
        GameEngine.Apply(state, action, pickVariant: TestNarration.First);

    // ── Deal phase ────────────────────────────────────────────────────────────

    [Fact]
    public void Withdraw_InDeal_SitsOutClearsHandAndBet()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .Dealer(10)
            .Player("Lorah", "5000", 8, 7)
            .Player("Bekki", "500", 10, 9)
            .Build();

        var (result, _) = Apply(state, new WithdrawFromRound(0));

        Assert.True(result.Players[0].SittingOut);
        Assert.Empty(result.Players[0].Hands[0].Cards);
        Assert.Equal(string.Empty, result.Players[0].Bet);
        // Everyone else is untouched.
        Assert.False(result.Players[1].SittingOut);
        Assert.Equal("500", result.Players[1].Bet);
        Assert.Equal(GamePhase.Deal, result.Phase);
    }

    [Fact]
    public void Withdraw_Narrates()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .Dealer(10)
            .Player("Lorah", 8, 7)
            .Build();

        var templates = new NarrationTemplates
        {
            PlayerWithdraw = [["{name} is out - bet returned."]],
        };

        var (_, effects) = GameEngine.Apply(state, new WithdrawFromRound(0), templates,
            pickVariant: TestNarration.First);

        var chat = effects.OfType<SendChat>().Select(e => e.Text).ToList();
        Assert.Contains("Lorah is out - bet returned.", chat);
    }

    // ── Guards ────────────────────────────────────────────────────────────────

    [Fact]
    public void Withdraw_InBetting_IsNoop()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Betting)
            .Player("Lorah", "5000")
            .Build();

        var (result, effects) = Apply(state, new WithdrawFromRound(0));

        Assert.Equal(state, result);
        Assert.Empty(effects);
    }

    [Fact]
    public void Withdraw_AlreadySittingOut_IsNoop()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Dealer(10)
            .Player(new Player { Nickname = "Lorah", Bet = "100", SittingOut = true, Hands = [new Hand()] })
            .Build();

        var (result, effects) = Apply(state, new WithdrawFromRound(0));

        Assert.Equal(state, result);
        Assert.Empty(effects);
    }

    [Fact]
    public void Withdraw_OutOfRangeIndex_IsNoop()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .Player("Lorah", 8, 7)
            .Build();

        var (result, _) = Apply(state, new WithdrawFromRound(5));

        Assert.Equal(state, result);
    }

    [Theory]
    [InlineData(HandState.Stand)]
    [InlineData(HandState.Bust)]
    [InlineData(HandState.Blackjack)]
    [InlineData(HandState.Charlie)]
    [InlineData(HandState.Surrendered)]
    public void Withdraw_PlayerWhoFinishedTheirTurn_IsNoop(HandState finished)
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Dealer(10)
            .Player("Lorah", "100", finished, 10, 9)
            .Player("Bekki", 10, 9)
            .ActiveHand(1)
            .Build();

        var (result, effects) = Apply(state, new WithdrawFromRound(0));

        Assert.Equal(state, result);
        Assert.Empty(effects);
        Assert.False(GameEngine.CanWithdraw(state, 0));
        Assert.True(GameEngine.CanWithdraw(state, 1));
    }

    [Fact]
    public void Withdraw_SplitWithOneHandStillPlaying_IsAllowed()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Dealer(10)
            .Player(new Player
            {
                Nickname = "Lorah",
                Bet      = "100",
                Hands    =
                [
                    new Hand { Cards = [8, 10], State = HandState.Stand },
                    new Hand { Cards = [8, 3],  State = HandState.Playing },
                ],
            })
            .ActiveHand(0, 1)
            .Build();

        Assert.True(GameEngine.CanWithdraw(state, 0));

        var (result, _) = Apply(state, new WithdrawFromRound(0));

        Assert.True(result.Players[0].SittingOut);
    }

    // Deal phase is exempt: nobody has acted, so a terminal hand there (a dealt
    // blackjack) is still withdrawable.
    [Fact]
    public void Withdraw_BlackjackInDeal_IsAllowed()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.Deal)
            .Dealer(10)
            .Player("Lorah", "100", HandState.Blackjack, 1, 10)
            .Build();

        Assert.True(GameEngine.CanWithdraw(state, 0));

        var (result, _) = Apply(state, new WithdrawFromRound(0));

        Assert.True(result.Players[0].SittingOut);
    }

    // ── PlayerTurns: not the active player ────────────────────────────────────

    [Fact]
    public void Withdraw_NonActivePlayer_LeavesTurnOrderAlone()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Dealer(10)
            .Player("Lorah", 8, 7)
            .Player("Bekki", 10, 9)
            .ActiveHand(0)
            .Build();

        var (result, _) = Apply(state, new WithdrawFromRound(1));

        Assert.True(result.Players[1].SittingOut);
        Assert.Equal(GamePhase.PlayerTurns, result.Phase);
        Assert.Equal(0, result.ActivePlayerIndex);
    }

    // ── PlayerTurns: the active player (the fiddly case) ──────────────────────

    [Fact]
    public void Withdraw_ActivePlayer_AdvancesToNextPlayer()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Dealer(10)
            .Player("Lorah", 8, 7)
            .Player("Bekki", 10, 9)
            .ActiveHand(0)
            .Build();

        var (result, _) = Apply(state, new WithdrawFromRound(0));

        Assert.Equal(GamePhase.PlayerTurns, result.Phase);
        Assert.Equal(1, result.ActivePlayerIndex);
        Assert.Equal(0, result.ActiveHandIndex);
        Assert.False(result.WaitingForNextPlayer);
    }

    [Fact]
    public void Withdraw_LastActivePlayer_GoesToDealerTurn()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Dealer(10)
            .Player("Lorah", HandState.Stand, 10, 9)
            .Player("Bekki", 8, 7)
            .ActiveHand(1)
            .Build();

        var (result, _) = Apply(state, new WithdrawFromRound(1));

        Assert.Equal(GamePhase.DealerTurn, result.Phase);
        // A stood 19 against a 10 upcard means the dealer still has to play.
        Assert.True(result.WaitingForDealer);
    }

    [Fact]
    public void Withdraw_EveryoneOut_LandsInDealerTurnReadyForPayout()
    {
        // The everyone-withdrew edge: must not strand the round in limbo, and must
        // not skip the dealer's "Go to Payout" click (that's where settlement runs).
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Dealer(10)
            .Player("Lorah", 8, 7)
            .ActiveHand(0)
            .Build();

        var (result, _) = Apply(state, new WithdrawFromRound(0));

        Assert.Equal(GamePhase.DealerTurn, result.Phase);
        Assert.False(result.WaitingForDealer); // nothing left to play against
        Assert.True(GameEngine.CanGoToPayout(result));
    }

    [Fact]
    public void Withdraw_ActivePlayerWithSplitHands_DiscardsAllHands()
    {
        var state = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Dealer(10)
            .Player(new Player
            {
                Nickname = "Lorah",
                Bet      = "1000",
                Hands    =
                [
                    new Hand { Cards = [8, 3], State = HandState.Playing, IsFromSplit = true },
                    new Hand { Cards = [8, 5], State = HandState.Playing, IsFromSplit = true },
                ],
            })
            .Player("Bekki", 10, 9)
            .ActiveHand(0, 1)
            .Build();

        var (result, _) = Apply(state, new WithdrawFromRound(0));

        Assert.True(result.Players[0].SittingOut);
        Assert.Single(result.Players[0].Hands);
        Assert.Empty(result.Players[0].Hands[0].Cards);
        Assert.Equal(1, result.ActivePlayerIndex);
    }
}
