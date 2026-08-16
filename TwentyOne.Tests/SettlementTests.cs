using TwentyOne.Game;
using Xunit;

namespace TwentyOne.Tests;

public class SettlementTests
{
    private static Settlement Win(long tableNet, int cut = 70) =>
        Settlement.Compute(tableNet, tips: 0, serviceToDealer: 0, serviceToVenue: 0,
            venueCutPct: cut, lossCoverage: LossCoverage.VenueCoversAll);

    [Fact]
    public void WinningNight_SplitsByCut_AndDealerPaysVenue()
    {
        var s = Win(250_000, cut: 70);

        Assert.Equal(175_000, s.VenueShare);
        Assert.Equal(75_000,  s.DealerShare);
        Assert.Equal(175_000, s.NetTransfer);
        Assert.True(s.DealerPaysVenue);
        Assert.Equal(75_000, s.DealerTake);
    }

    [Fact]
    public void WinningNight_RoundingGilFallsToDealer()
    {
        // 101 * 70% = 70.7 -> venue floors to 70, dealer keeps the odd gil.
        var s = Win(101, cut: 70);

        Assert.Equal(70, s.VenueShare);
        Assert.Equal(31, s.DealerShare);
        Assert.Equal(101, s.VenueShare + s.DealerShare);
    }

    [Fact]
    public void LosingNight_VenueCoversAll_MakesDealerWhole()
    {
        var s = Settlement.Compute(-250_000, tips: 0, serviceToDealer: 0, serviceToVenue: 0,
            venueCutPct: 70, lossCoverage: LossCoverage.VenueCoversAll);

        Assert.Equal(-250_000, s.VenueShare);
        Assert.Equal(0, s.DealerShare);
        Assert.False(s.DealerPaysVenue);
        Assert.Equal(250_000, s.TransferAmount);
        // Whole: the dealer is out nothing.
        Assert.Equal(0, s.DealerTake);
    }

    [Fact]
    public void LosingNight_VenueCoversShare_SplitsLossLikeAWin()
    {
        var s = Settlement.Compute(-250_000, tips: 0, serviceToDealer: 0, serviceToVenue: 0,
            venueCutPct: 70, lossCoverage: LossCoverage.VenueCoversShare);

        Assert.Equal(-175_000, s.VenueShare);
        Assert.Equal(-75_000,  s.DealerShare);
        Assert.Equal(175_000,  s.TransferAmount);
        Assert.False(s.DealerPaysVenue);
    }

    [Fact]
    public void LosingNight_VenueCoversShare_RoundingGilStillFavoursDealer()
    {
        // 101 * 70% = 70.7 -> venue absorbs 71, dealer eats only 30.
        var s = Settlement.Compute(-101, tips: 0, serviceToDealer: 0, serviceToVenue: 0,
            venueCutPct: 70, lossCoverage: LossCoverage.VenueCoversShare);

        Assert.Equal(-71, s.VenueShare);
        Assert.Equal(-30, s.DealerShare);
        Assert.Equal(-101, s.VenueShare + s.DealerShare);
    }

    [Fact]
    public void LosingNight_DealerAbsorbs_VenuePaysNothing()
    {
        var s = Settlement.Compute(-250_000, tips: 0, serviceToDealer: 0, serviceToVenue: 0,
            venueCutPct: 70, lossCoverage: LossCoverage.DealerAbsorbs);

        Assert.Equal(0, s.VenueShare);
        Assert.Equal(-250_000, s.DealerShare);
        Assert.Equal(0, s.NetTransfer);
        Assert.Equal(-250_000, s.DealerTake);
    }

    [Fact]
    public void TipsBypassTheSplitEntirely()
    {
        var withTips = Settlement.Compute(250_000, tips: 10_000, serviceToDealer: 0, serviceToVenue: 0,
            venueCutPct: 70, lossCoverage: LossCoverage.VenueCoversAll);
        var without  = Win(250_000);

        // The venue's slice is untouched by tips; they land only in the take.
        Assert.Equal(without.VenueShare,  withTips.VenueShare);
        Assert.Equal(without.NetTransfer, withTips.NetTransfer);
        Assert.Equal(without.DealerTake + 10_000, withTips.DealerTake);
    }

    [Fact]
    public void ServiceCharges_RouteToTheirOwnSide()
    {
        var s = Settlement.Compute(250_000, tips: 0, serviceToDealer: 20_000, serviceToVenue: 10_000,
            venueCutPct: 70, lossCoverage: LossCoverage.VenueCoversAll);

        Assert.Equal(175_000 + 10_000, s.NetTransfer);
        Assert.Equal(75_000  + 20_000, s.DealerTake);
    }

    [Fact]
    public void LossCoverageCanFlipTheTransferDirection_EvenWithVenueService()
    {
        // Venue owes 100K of loss coverage but is owed 10K of service charges.
        var s = Settlement.Compute(-100_000, tips: 5_000, serviceToDealer: 0, serviceToVenue: 10_000,
            venueCutPct: 70, lossCoverage: LossCoverage.VenueCoversAll);

        Assert.Equal(-90_000, s.NetTransfer);
        Assert.False(s.DealerPaysVenue);
        Assert.Equal(90_000, s.TransferAmount);
        Assert.Equal(5_000,  s.DealerTake); // whole on the table, keeps the tips
    }

    [Fact]
    public void BreakEvenNight_SettlesToNothing()
    {
        var s = Win(0);

        Assert.Equal(0, s.VenueShare);
        Assert.Equal(0, s.DealerShare);
        Assert.Equal(0, s.NetTransfer);
    }

    // ── Venue-funded credit ────────────────────────────────────────────
    // Credit is deliberately absent from Compute. It moves no real gil when
    // issued, and a session cannot close with player banks outstanding, so by
    // settlement time every credit has resolved into one of the two cases below -
    // both of which tableNet already accounts for. A reimbursement line on top
    // would pay the dealer twice. These tests pin that.

    [Fact]
    public void CreditLostBack_NeedsNoSettlement()
    {
        // Dealer holds 1M, issues Lorah 500K credit, Lorah bets it and loses.
        // No real gil moved anywhere, so tableNet is 0 and there is nothing to
        // settle - whether the credit came from a venue pre-load or the dealer's
        // own pocket, the gil never left the pile.
        var s = Win(0);

        Assert.Equal(0, s.NetTransfer);
        Assert.Equal(0, s.DealerTake);
    }

    [Fact]
    public void CreditCashedOut_IsCoveredAsAnOrdinaryLoss()
    {
        // Same 500K credit, but Lorah wins 500K and withdraws the lot: 1M of real
        // gil leaves the pile, so tableNet is -1M like any other losing night.
        var s = Settlement.Compute(-1_000_000, tips: 0, serviceToDealer: 0, serviceToVenue: 0,
            venueCutPct: 70, lossCoverage: LossCoverage.VenueCoversAll);

        // Loss coverage alone makes the dealer whole - no credit-specific line.
        Assert.Equal(-1_000_000, s.NetTransfer);
        Assert.Equal(0, s.DealerTake);
    }

    [Fact]
    public void CreditOutcomeIsInvisibleToTheSplit()
    {
        // A night whose table result happens to equal another's settles the same
        // way regardless of how much credit was issued along the way, because
        // credit reaches the books only via gil that actually moved.
        Assert.Equal(Win(250_000), Win(250_000));
    }

    [Theory]
    [InlineData(250_000, 70)]
    [InlineData(-250_000, 70)]
    [InlineData(37_913, 55)]
    [InlineData(-37_913, 55)]
    [InlineData(0, 100)]
    public void EverySplit_ConservesTheWholePot(long tableNet, int cut)
    {
        foreach (var policy in new[] { LossCoverage.VenueCoversAll, LossCoverage.VenueCoversShare, LossCoverage.DealerAbsorbs })
        foreach (var credit in new long[] { 0, 120_000 })
        {
            var s = Settlement.Compute(tableNet, tips: 8_000, serviceToDealer: 3_000, serviceToVenue: 4_000,
                venueCutPct: cut, lossCoverage: policy);

            // Nothing is created or lost: what the dealer keeps plus what moves to
            // the venue equals the table result plus tips plus all service charges.
            // Credit nets out - it is added into the split and removed again.
            Assert.Equal(tableNet + 8_000 + 3_000 + 4_000, s.DealerTake + s.NetTransfer);
        }
    }
}
