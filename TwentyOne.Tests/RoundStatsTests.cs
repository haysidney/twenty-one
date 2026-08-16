using System.Collections.Immutable;
using System.Linq;
using TwentyOne.Game;
using TwentyOne.Tests.Helpers;
using Xunit;

namespace TwentyOne.Tests;

/// <summary>
/// Hand-computed checks on the per-round stats derivation - the numbers the
/// History window and the player-stats table report. Every case states the
/// expected outcome independently of the implementation.
/// </summary>
public class RoundStatsTests
{
    // Dealer stands on 20 unless a test says otherwise, so player outcomes are
    // unambiguous: 10+10 = 20, Stand.
    private static GameStateBuilder Settled() =>
        new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .Dealer(HandState.Stand, 10, 10);

    private static PlayerRoundResult For(GameState s, string name) =>
        RoundStats.PerPlayer(s).Single(r => r.DisplayName == name);

    // ── Win / lose / push ─────────────────────────────────────────────────────

    [Fact]
    public void PlayerBeatingTheDealer_CountsAsAWin()
    {
        // Lorah 5+6+10 = 21 vs dealer 20: win, +1000 on a 1000 bet.
        var s = Settled().Player("Lorah", "1000", HandState.Stand, 5, 6, 10).Build();

        var r = For(s, "Lorah");
        Assert.True(r.Won);
        Assert.False(r.Lost);
        Assert.False(r.Pushed);
        Assert.Equal(1000m, r.Net);
        Assert.False(r.HadBlackjack);
        Assert.Equal(0, r.Charlies);
    }

    [Fact]
    public void PlayerBustingCountsAsALoss()
    {
        var s = Settled().Player("Bekki", "500", HandState.Bust, 10, 10, 5).Build();

        var r = For(s, "Bekki");
        Assert.True(r.Lost);
        Assert.Equal(-500m, r.Net);
    }

    [Fact]
    public void MatchingTheDealerCountsAsAPush()
    {
        var s = Settled().Player("Nolla", "750", HandState.Stand, 10, 10).Build();

        var r = For(s, "Nolla");
        Assert.True(r.Pushed);
        Assert.False(r.Won);
        Assert.False(r.Lost);
        Assert.Equal(0m, r.Net);
    }

    [Fact]
    public void LosingToAHigherDealerTotalCountsAsALoss()
    {
        var s = Settled().Player("Bekki", "300", HandState.Stand, 10, 9).Build();

        Assert.True(For(s, "Bekki").Lost);
        Assert.Equal(-300m, For(s, "Bekki").Net);
    }

    // ── Blackjack / Charlie ───────────────────────────────────────────────────

    [Fact]
    public void Blackjack_IsFlaggedAndPaysTheBjMultiplier()
    {
        // 3:2 on a 1000 bet = 1500.
        var s = new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .Dealer(HandState.Stand, 10, 10)
            .BjPayout(1.5)
            .Player("Lorah", "1000", HandState.Blackjack, 1, 10)
            .Build();

        var r = For(s, "Lorah");
        Assert.True(r.HadBlackjack);
        Assert.True(r.Won);
        Assert.Equal(1500m, r.Net);
    }

    [Fact]
    public void FiveCardCharlie_IsCountedOnceAndPaysOut()
    {
        var s = new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .Dealer(HandState.Stand, 10, 10)
            .Charlie(FiveCardCharlieRule.BeatsAll, PayoutRatio.EvenMoney)
            .Player("Lorah", "1000", HandState.Charlie, 2, 3, 4, 2, 3)
            .Build();

        var r = For(s, "Lorah");
        Assert.Equal(1, r.Charlies);
        Assert.True(r.Won);
        Assert.Equal(1000m, r.Net);
    }

    // ── Surrender ─────────────────────────────────────────────────────────────

    [Fact]
    public void Surrender_LosesHalfTheBetAndIsNotAWin()
    {
        // Odd bets round in the house's favour: 501 -> forfeits 251.
        var s = new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .Dealer(HandState.Stand, 10, 10)
            .Player("Bekki", "501", HandState.Surrendered, 10, 6)
            .Build();

        var r = For(s, "Bekki");
        Assert.False(r.Won);
        Assert.True(r.Lost);
        Assert.Equal(-251m, r.Net);
    }

    [Fact]
    public void Surrender_IsClassifiedAsALossNotAPush()
    {
        var s = new GameStateBuilder()
            .Phase(GamePhase.Payout)
            .Dealer(HandState.Stand, 10, 10)
            .Player("Bekki", "500", HandState.Surrendered, 10, 6)
            .Build();

        var c = RoundStats.Classify(s);
        Assert.Contains("Bekki", c.Losers);
        Assert.Empty(c.Pushes);
        Assert.Empty(c.Winners);
    }

    // ── Splits and doubles ────────────────────────────────────────────────────

    [Fact]
    public void SplitWithOneWinAndOneLoss_NetsToTheDifferenceAndCountsAsAWin()
    {
        // Two 500 hands: 21 beats 20 (+500), 19 loses to 20 (-500) -> net 0.
        var split = new Player
        {
            Nickname = "Lorah",
            Bet      = "500",
            Hands =
            [
                new Hand { Cards = [5, 6, 10], State = HandState.Stand, IsFromSplit = true },
                new Hand { Cards = [10, 9],  State = HandState.Stand, IsFromSplit = true },
            ],
        };
        var s = Settled().Player(split).Build();

        var r = For(s, "Lorah");
        Assert.Equal(0m, r.Net);
        // Net is zero, so the counters call it a push...
        Assert.True(r.Pushed);
        // ...but the History classifier reports any winning hand as a win, which is
        // what a dealer reading the round list expects to see.
        Assert.Contains("Lorah", RoundStats.Classify(s).Winners);
    }

    [Fact]
    public void DoubledHand_PaysOnTheDoubledStake()
    {
        var doubled = new Player
        {
            Nickname = "Lorah",
            Bet      = "500",
            Hands    = [new Hand { Cards = [5, 6, 10], State = HandState.Stand, Doubled = true, Bet = "1000" }],
        };
        var s = Settled().Player(doubled).Build();

        Assert.Equal(1000m, For(s, "Lorah").Net);
    }

    // ── Who is in the round at all ────────────────────────────────────────────

    [Fact]
    public void SittingOutPlayers_AreExcludedEntirely()
    {
        var s = Settled()
            .Player("Lorah", "1000", HandState.Stand, 5, 6, 10)
            .Player(new Player { Nickname = "Bekki", Bet = "500", SittingOut = true, Hands = [new Hand()] })
            .Build();

        Assert.Single(RoundStats.PerPlayer(s));
        Assert.Equal("Lorah", RoundStats.PerPlayer(s)[0].DisplayName);
    }

    [Fact]
    public void SittingOutPlayers_AreNotReportedAsLosers()
    {
        // Regression: the History classifier used to fall through to "loser" for
        // any player whose hand produced PayoutResult.None - which is exactly what
        // an empty hand produces, so everyone sitting out was listed as losing.
        var s = Settled()
            .Player("Lorah", "1000", HandState.Stand, 5, 6, 10)
            .Player(new Player { Nickname = "Bekki", Bet = "500", SittingOut = true, Hands = [new Hand()] })
            .Build();

        var c = RoundStats.Classify(s);
        Assert.Equal(["Lorah"], c.Winners);
        Assert.Empty(c.Losers);
        Assert.Empty(c.Pushes);
    }

    [Fact]
    public void WithdrawnPlayers_AreExcludedFromBothViews()
    {
        // A player withdrawn mid-round is sat out with their hand discarded; they
        // staked nothing, so they must not appear as a loss in either view.
        var mid = new GameStateBuilder()
            .Phase(GamePhase.PlayerTurns)
            .Dealer(10)
            .Player("Lorah", 10, 9)
            .Player("Bekki", 8, 7)
            .ActiveHand(0)
            .Build();

        var (withdrawn, _) = GameEngine.Apply(mid, new WithdrawFromRound(1),
            pickVariant: TestNarration.First);
        var settled = withdrawn with { Phase = GamePhase.Payout, DealerHand = new Hand { Cards = [10, 10], State = HandState.Stand } };

        Assert.Single(RoundStats.PerPlayer(settled));
        Assert.Equal("Lorah", RoundStats.PerPlayer(settled)[0].DisplayName);
        Assert.DoesNotContain("Bekki", RoundStats.Classify(settled).Losers);
    }

    // ── A whole table at once ─────────────────────────────────────────────────

    [Fact]
    public void MixedTable_ProducesTheHandComputedTotals()
    {
        // Dealer 20. Lorah 21 (+1000), Bekki bust (-500), Nolla 20 push (0).
        var s = Settled()
            .Player("Lorah", "1000", HandState.Stand, 5, 6, 10)
            .Player("Bekki", "500",  HandState.Bust,  10, 10, 5)
            .Player("Nolla", "750",  HandState.Stand, 10, 10)
            .Build();

        var results = RoundStats.PerPlayer(s);
        Assert.Equal(3, results.Count);
        Assert.Equal(1000m, results.Single(r => r.DisplayName == "Lorah").Net);
        Assert.Equal(-500m, results.Single(r => r.DisplayName == "Bekki").Net);
        Assert.Equal(0m,    results.Single(r => r.DisplayName == "Nolla").Net);

        // Bank net is the negation of the players' net: -1000 + 500 + 0 = -500.
        Assert.Equal(-500m, -results.Sum(r => r.Net));

        var c = RoundStats.Classify(s);
        Assert.Equal(["Lorah"], c.Winners);
        Assert.Equal(["Bekki"], c.Losers);
        Assert.Equal(["Nolla"], c.Pushes);
    }

    [Fact]
    public void StatsKey_PrefersTheCharacterIdentityOverTheNickname()
    {
        var ffxiv  = new Player { Nickname = "Lorah", FullName = "Lorahsande Banehene", World = "Balmung" };
        var manual = new Player { Nickname = "Bekki" };

        Assert.Equal("Lorahsande Banehene@Balmung", ffxiv.StatsKey());
        Assert.Equal("Bekki", manual.StatsKey());
    }
}
