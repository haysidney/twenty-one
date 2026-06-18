using TwentyOne.Game;
using Xunit;

namespace TwentyOne.Tests;

public class TradeRoutingTests
{
    [Fact]
    public void NoGil_RoutesToNone()
    {
        Assert.Equal(TradeDirection.None, TradeRouting.Resolve(0, 0));
    }

    [Fact]
    public void IncomingOnly_RoutesToDeposit()
    {
        Assert.Equal(TradeDirection.Deposit, TradeRouting.Resolve(gaveGil: 0, receivedGil: 1000));
    }

    [Fact]
    public void OutgoingOnly_RoutesToWithdraw()
    {
        Assert.Equal(TradeDirection.Withdraw, TradeRouting.Resolve(gaveGil: 725000, receivedGil: 0));
    }

    // The regression: a cashout where the player also put gil in the window used
    // to drop the incoming leg, leaving drift equal to that amount. It must now
    // route to TwoSided so both legs are ledgered.
    [Fact]
    public void Bidirectional_RoutesToTwoSided()
    {
        Assert.Equal(TradeDirection.TwoSided, TradeRouting.Resolve(gaveGil: 725000, receivedGil: 50000));
    }
}
