using TwentyOne.Game;
using Xunit;

namespace TwentyOne.Tests;

/// <summary>
/// Cross-channel handling decides what gets broadcast where - a narration line
/// starting with /y while the dealer is configured for /p either shouts to the
/// zone, quietly redirects, or echoes locally. Previously untested.
/// </summary>
public class ChatRoutingTests
{
    private static string Msg(string text, string channel = "/p",
        CrossChannelCommands mode = CrossChannelCommands.Redirect) =>
        ChatRouting.Resolve(text, channel, mode).Message;

    // ── Plain text ────────────────────────────────────────────────────────────

    [Fact]
    public void PlainText_IsPrefixedWithTheConfiguredChannel()
    {
        Assert.Equal("/p Place your bets!", Msg("Place your bets!"));
        Assert.Equal("/say Place your bets!", Msg("Place your bets!", channel: "/say"));
    }

    [Fact]
    public void PlainText_IsUnaffectedByTheCrossChannelMode()
    {
        foreach (var mode in new[] { CrossChannelCommands.Block, CrossChannelCommands.Redirect, CrossChannelCommands.Allow })
            Assert.Equal("/p hi", Msg("hi", mode: mode));
    }

    // ── The three cross-channel modes ─────────────────────────────────────────

    [Fact]
    public void Redirect_RewritesToTheConfiguredChannel()
    {
        Assert.Equal("/p LETS GO", Msg("/y LETS GO", mode: CrossChannelCommands.Redirect));
    }

    [Fact]
    public void Block_EchoesLocallyInstead()
    {
        Assert.Equal("/echo LETS GO", Msg("/y LETS GO", mode: CrossChannelCommands.Block));
    }

    [Fact]
    public void Allow_LeavesTheCommandAlone()
    {
        Assert.Equal("/y LETS GO", Msg("/y LETS GO", mode: CrossChannelCommands.Allow));
    }

    // ── What counts as cross-channel ──────────────────────────────────────────

    [Fact]
    public void CommandMatchingTheConfiguredChannel_PassesThroughInEveryMode()
    {
        foreach (var mode in new[] { CrossChannelCommands.Block, CrossChannelCommands.Redirect, CrossChannelCommands.Allow })
            Assert.Equal("/p hi", Msg("/p hi", mode: mode));
    }

    [Fact]
    public void ChannelMatchIsCaseInsensitive()
    {
        Assert.Equal("/P hi", Msg("/P hi"));
    }

    [Fact]
    public void NonChannelSlashCommands_AreNeverRewritten()
    {
        // Emotes and rolls must survive intact whatever the channel is set to.
        Assert.Equal("/battlestance", Msg("/battlestance"));
        Assert.Equal("/random 13", Msg("/random 13"));
        Assert.Equal("/echo already local", Msg("/echo already local", mode: CrossChannelCommands.Block));
    }

    [Fact]
    public void BareChannelCommandWithNoBody_IsDropped()
    {
        // "/y" on its own has nothing to redirect, and sending it would silently
        // switch the dealer's active chat channel. It also used to throw
        // IndexOutOfRangeException on the body split.
        Assert.Equal(string.Empty, Msg("/y"));
        Assert.Equal(string.Empty, Msg("/y", mode: CrossChannelCommands.Block));
        // Allow mode does not inspect the body at all, so it still passes through.
        Assert.Equal("/y", Msg("/y", mode: CrossChannelCommands.Allow));
    }

    // ── Wait markers ──────────────────────────────────────────────────────────

    [Fact]
    public void TrailingWaitMarker_IsStrippedIntoMinWaitAfter()
    {
        var r = ChatRouting.Resolve("Summary: <se.15> <wait.1>", "/p", CrossChannelCommands.Redirect);
        Assert.Equal("/p Summary: <se.15>", r.Message);
        Assert.Equal(1000, r.MinWaitAfterMs);
        Assert.Equal(0, r.MinWaitBeforeMs);
    }

    [Fact]
    public void LeadingWaitMarker_IsStrippedIntoMinWaitBefore()
    {
        var r = ChatRouting.Resolve("<wait.3> Collecting Bets!", "/p", CrossChannelCommands.Redirect);
        Assert.Equal("/p Collecting Bets!", r.Message);
        Assert.Equal(3000, r.MinWaitBeforeMs);
        Assert.Equal(0, r.MinWaitAfterMs);
    }

    [Fact]
    public void BothWaitMarkers_AreStripped()
    {
        var r = ChatRouting.Resolve("<wait.1> middle <wait.2>", "/p", CrossChannelCommands.Redirect);
        Assert.Equal("/p middle", r.Message);
        Assert.Equal(1000, r.MinWaitBeforeMs);
        Assert.Equal(2000, r.MinWaitAfterMs);
    }

    [Fact]
    public void WaitMarkersSurviveACrossChannelRedirect()
    {
        var r = ChatRouting.Resolve("/y woo <wait.2>", "/p", CrossChannelCommands.Redirect);
        Assert.Equal("/p woo", r.Message);
        Assert.Equal(2000, r.MinWaitAfterMs);
    }

    // ── Roll rate limiting ────────────────────────────────────────────────────

    [Theory]
    [InlineData("/random 13", true)]
    [InlineData("/dice 13", true)]
    [InlineData("/battlestance", false)]
    [InlineData("Place your bets!", false)]
    public void RollCommands_AreFlaggedForTheSlowerCooldown(string text, bool expected)
    {
        Assert.Equal(expected, ChatRouting.Resolve(text, "/p", CrossChannelCommands.Redirect).IsSlashRateLimited);
    }
}
