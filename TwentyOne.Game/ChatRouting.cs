using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TwentyOne.Game;

/// <summary>
/// A narration line resolved into the exact text to send, plus its pacing hints.
/// </summary>
public readonly record struct OutgoingChat(
    string Message,
    int    MinWaitBeforeMs,
    int    MinWaitAfterMs,
    bool   IsSlashRateLimited);

/// <summary>
/// Pure shaping of an outgoing narration line: strips <c>&lt;wait.N&gt;</c> markers
/// and resolves cross-channel slash commands against the configured channel.
/// Extracted from <c>ChatQueue</c> (which lives in the Dalamud project and so is
/// unreachable from tests) for the same reason as <see cref="TradeRouting"/> - this
/// is the part with real branching, and it decides what gets broadcast where.
/// </summary>
public static partial class ChatRouting
{
    private static readonly HashSet<string> RateLimitedSlashCommands = ["/random", "/dice"];

    // Slash commands that produce visible chat in some channel - used to detect
    // "cross-channel" overrides that would otherwise silently broadcast.
    private static readonly HashSet<string> ChannelCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "/say", "/s", "/yell", "/y", "/shout", "/sh",
        "/party", "/p", "/alliance", "/a",
        "/fc", "/linkshell", "/l",
        "/ls1", "/ls2", "/ls3", "/ls4", "/ls5", "/ls6", "/ls7", "/ls8",
        "/cwlinkshell", "/cwl",
        "/cwl1", "/cwl2", "/cwl3", "/cwl4", "/cwl5", "/cwl6", "/cwl7", "/cwl8",
        "/tell", "/t", "/reply", "/r", "/novice", "/beginner",
    };

    [GeneratedRegex(@"^\s*<wait\.(\d+)>")]
    private static partial Regex BeforeWaitRegex();

    [GeneratedRegex(@"<wait\.(\d+)>\s*$")]
    private static partial Regex AfterWaitRegex();

    public static OutgoingChat Resolve(string text, string configChannel, CrossChannelCommands crossChannel)
    {
        var raw           = text ?? string.Empty;
        var minWaitAfter  = 0;
        var minWaitBefore = 0;

        var mAfter = AfterWaitRegex().Match(raw);
        if (mAfter.Success)
        {
            minWaitAfter = int.Parse(mAfter.Groups[1].Value) * 1000;
            raw          = raw[..mAfter.Index].Trim();
        }

        var mBefore = BeforeWaitRegex().Match(raw);
        if (mBefore.Success)
        {
            minWaitBefore = int.Parse(mBefore.Groups[1].Value) * 1000;
            raw           = raw[(mBefore.Index + mBefore.Length)..].Trim();
        }

        string msg;
        if (raw.StartsWith('/'))
        {
            if (crossChannel != CrossChannelCommands.Allow && IsCrossChannelCommand(raw, configChannel))
            {
                // A bare channel command with no body ("/y") carries nothing to
                // redirect, so it is dropped rather than rewritten - it would
                // otherwise switch the player's active chat channel silently.
                var parts = raw.Split(' ', 2);
                var body  = parts.Length > 1 ? parts[1] : string.Empty;
                if (body.Length == 0)
                    return new OutgoingChat(string.Empty, minWaitBefore, minWaitAfter, false);

                raw = crossChannel == CrossChannelCommands.Redirect
                    ? configChannel + " " + body
                    : "/echo " + body;
            }
            msg = raw;
        }
        else
        {
            msg = configChannel + " " + raw;
        }

        var slashRateLimited = RateLimitedSlashCommands.Contains(raw.Split(' ')[0]);
        return new OutgoingChat(msg, minWaitBefore, minWaitAfter, slashRateLimited);
    }

    private static bool IsCrossChannelCommand(string raw, string configChannel)
    {
        var cmd = raw.Split(' ', 2)[0];
        if (!ChannelCommands.Contains(cmd)) return false;
        return !string.Equals(cmd, configChannel, StringComparison.OrdinalIgnoreCase);
    }
}
