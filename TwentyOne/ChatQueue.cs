using System;
using System.Collections.Generic;
using TwentyOne.Game;

namespace TwentyOne;

/// <summary>
/// Outgoing FFXIV-chat FIFO with per-message and global cooldowns. Owns the
/// queue, the last-sent timestamp, and the cooldown bookkeeping; pulled out of
/// MainWindow so the rate-limit policy lives in one place and the &lt;wait.N&gt;
/// markers are parsed off the hot path.
/// </summary>
internal sealed class ChatQueue
{
    public readonly record struct Entry(
        bool   IsRoll,
        Action Invoke,
        int    MinWaitAfterMs,
        int    MinWaitBeforeMs,
        bool   IsSlashRateLimited);

    private readonly Queue<Entry> q = new();
    private DateTime              lastChatSent      = DateTime.UtcNow;
    private int                   lastSentMinWaitMs = 0;

    public int  Count => q.Count;
    public void Clear() => q.Clear();
    public void Enqueue(Entry e) => q.Enqueue(e);

    /// <summary>
    /// Resolve a narration line via <see cref="ChatRouting"/> (wait markers,
    /// cross-channel handling) and enqueue it for <paramref name="send"/>.
    /// </summary>
    public void EnqueueChat(string text, string configChannel, CrossChannelCommands crossChannel, Action<string> send)
    {
        var outgoing = ChatRouting.Resolve(text, configChannel, crossChannel);
        if (outgoing.Message.Length == 0) return;
        q.Enqueue(new Entry(false, () => send(outgoing.Message),
            outgoing.MinWaitAfterMs, outgoing.MinWaitBeforeMs, outgoing.IsSlashRateLimited));
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
}
