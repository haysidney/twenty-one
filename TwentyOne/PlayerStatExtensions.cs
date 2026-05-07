using System.Diagnostics.CodeAnalysis;
using TwentyOne.Game;

namespace TwentyOne;

/// <summary>
/// Plugin-side glue between a pure-core <see cref="Player"/> and the venue's
/// <see cref="Configuration.PlayerStatsStore"/>. Centralises the lookup +
/// banking-check pattern that otherwise gets repeated at every call site.
/// </summary>
internal static class PlayerStatExtensions
{
    /// <summary>
    /// Stable identity key used by <see cref="Configuration.PlayerStatsStore"/>:
    /// <c>"FullName@World"</c> for FFXIV characters, the nickname for manually
    /// added players.
    /// </summary>
    public static string StatsKey(this Player p) =>
        p.FullName.Length > 0 ? $"{p.FullName}@{p.World}" : p.Nickname;

    /// <summary>True if the player has any non-zero balance or any prior bank activity.</summary>
    public static bool IsBanking(this PlayerStat stat) =>
        stat.Bank > 0 || stat.BankLog.Count > 0;

    /// <summary>
    /// Look up the player's stats, if any. Mirrors <see cref="System.Collections.Generic.Dictionary{TKey, TValue}.TryGetValue"/>.
    /// </summary>
    public static bool TryGetStat(this Player p, Configuration config,
        [MaybeNullWhen(false)] out PlayerStat stat) =>
        config.PlayerStatsStore.TryGetValue(p.StatsKey(), out stat);

    /// <summary>True only if the player has stats AND those stats indicate banking activity.</summary>
    public static bool TryGetBankingStat(this Player p, Configuration config,
        [MaybeNullWhen(false)] out PlayerStat stat)
    {
        if (!p.TryGetStat(config, out stat)) return false;
        if (!stat.IsBanking()) { stat = null; return false; }
        return true;
    }

    /// <summary>Current bank balance for the player, or zero if no stats record exists.</summary>
    public static long BankBalance(this Player p, Configuration config) =>
        p.TryGetStat(config, out var stat) ? stat.Bank : 0L;
}
