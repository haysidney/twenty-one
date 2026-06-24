using System;
using System.IO;
using Newtonsoft.Json;

namespace TwentyOne.Game;

/// <summary>
/// Append-only, disk-only forensic log of every gil-affecting event. No UI:
/// when the dealer notices ledger drift, the JSONL file is read offline to
/// localize when and why it entered. One JSON object per line, never edited.
///
/// The plugin extracts the primitives (it owns ConfigDirectory and the
/// inventory read) and passes them in, so this type has no Dalamud dependency
/// and is unit-testable. Writing is best-effort and never throws into a game
/// path - an audit failure must not block a trade, a bank op, or plugin load.
/// </summary>
public static class AuditLog
{
    /// <summary>
    /// Directory the JSONL files live in (<c>{ConfigDirectory}/audit</c> in the
    /// plugin; a temp dir in tests). Null or empty disables logging entirely -
    /// this doubles as the on/off gate.
    /// </summary>
    public static string? Root { get; set; }

    public static void Bank(string venueId, string player, string op, long amount,
                            long balanceBefore, long balanceAfter, long dealerGil)
        => Write(venueId, new
        {
            t = Now(), kind = "bank", player, op, amount, balanceBefore, balanceAfter, dealerGil,
        });

    public static void Trade(string venueId, string partner, long gave, long received,
                             string outcome, long dealerGil)
        => Write(venueId, new
        {
            t = Now(), kind = "trade", partner, gave, received, outcome, dealerGil,
        });

    public static void Prompt(string venueId, string prompt, string player, long amount, string resolution)
        => Write(venueId, new { t = Now(), kind = "prompt", prompt, player, amount, resolution });

    public static void Wallet(string venueId, long gil, long delta)
        => Write(venueId, new { t = Now(), kind = "wallet", gil, delta });

    private static string Now() => DateTime.Now.ToString("o");

    private static void Write(string venueId, object evt)
    {
        var root = Root;
        if (string.IsNullOrEmpty(root)) return;
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, $"{venueId}-{DateTime.Now:yyyy-MM-dd}.jsonl");
            File.AppendAllText(path, JsonConvert.SerializeObject(evt) + "\n");
        }
        catch
        {
            // Diagnostic only; swallow so logging never disrupts gameplay.
        }
    }
}
