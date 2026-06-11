using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TwentyOne.Game;

namespace TwentyOne;

/// <summary>
/// Outgoing FFXIV-chat FIFO with per-message and global cooldowns. Owns the
/// queue, the last-sent timestamp, and the cooldown bookkeeping; pulled out of
/// MainWindow so the rate-limit policy lives in one place and the &lt;wait.N&gt;
/// markers are parsed off the hot path.
/// </summary>
internal sealed partial class ChatQueue
{
    public readonly record struct Entry(
        bool   IsRoll,
        Action Invoke,
        int    MinWaitAfterMs,
        int    MinWaitBeforeMs,
        bool   IsSlashRateLimited);

    private static readonly HashSet<string> RateLimitedSlashCommands = ["/random", "/dice"];

    // Slash commands that produce visible chat in some channel - used to detect "cross-channel"
    // overrides that would otherwise silently broadcast.
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

    private readonly Queue<Entry> q = new();
    private DateTime              lastChatSent      = DateTime.UtcNow;
    private int                   lastSentMinWaitMs = 0;

    public int  Count => q.Count;
    public void Clear() => q.Clear();
    public void Enqueue(Entry e) => q.Enqueue(e);

    /// <summary>
    /// Parse <c>&lt;wait.N&gt;</c> markers off the start/end of <paramref name="text"/>,
    /// resolve cross-channel overrides against <paramref name="configChannel"/>,
    /// and enqueue an entry that will hand the cleaned message to <paramref name="send"/>.
    /// </summary>
    public void EnqueueChat(string text, string configChannel, CrossChannelCommands crossChannel, Action<string> send)
    {
        var raw           = text;
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
                var body = raw.Split(' ', 2)[1];
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
        q.Enqueue(new Entry(false, () => send(msg), minWaitAfter, minWaitBefore, slashRateLimited));
    }

    /// <summary>
    /// Drain a single entry if the cooldown window allows. Roll entries are
    /// additionally held until any pending roll response has arrived.
    /// </summary>
    public void TryDrain(DateTime now, int cooldownMs, int slashCooldownMs, bool blockedByPendingHit)
    {
        if (q.Count == 0) return;
        var e = q.Peek();
        var effectiveCooldown = e.IsSlashRateLimited ? Math.Max(cooldownMs, slashCooldownMs) : cooldownMs;
        var requiredMs        = Math.Max(effectiveCooldown, lastSentMinWaitMs) + e.MinWaitBeforeMs;
        if ((now - lastChatSent).TotalMilliseconds < requiredMs) return;
        if (e.IsRoll && blockedByPendingHit) return;

        q.Dequeue();
        e.Invoke();
        lastChatSent      = now;
        lastSentMinWaitMs = e.MinWaitAfterMs;
    }

    private static bool IsCrossChannelCommand(string raw, string configChannel)
    {
        var cmd = raw.Split(' ', 2)[0];
        if (!ChannelCommands.Contains(cmd)) return false;
        return !string.Equals(cmd, configChannel, StringComparison.OrdinalIgnoreCase);
    }
}
