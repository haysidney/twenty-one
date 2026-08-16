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
    // StatsKey moved to TwentyOne.Game (RoundStats) so the pure stats derivation
    // can key its results; it is still in scope here via `using TwentyOne.Game`.

    /// <summary>
    /// Look up the player's stats, if any. Mirrors <see cref="System.Collections.Generic.Dictionary{TKey, TValue}.TryGetValue"/>.
    /// Bank-only mode: a stats row existing IS the banking record - there is no
    /// separate "is this player banking?" predicate. Every tracked player banks.
    /// </summary>
    public static bool TryGetStat(this Player p, Configuration config,
        [MaybeNullWhen(false)] out PlayerStat stat) =>
        config.PlayerStatsStore.TryGetValue(p.StatsKey(), out stat);

    /// <summary>
    /// Look up the player's stats, creating an empty banking row if none exists.
    /// In bank-only mode every table player is a banking player, so this is the
    /// canonical accessor for any path that funds or settles a bet.
    /// </summary>
    public static PlayerStat GetOrCreateStat(this Player p, Configuration config)
    {
        var key = p.StatsKey();
        if (!config.PlayerStatsStore.TryGetValue(key, out var stat))
        {
            stat = new PlayerStat { DisplayName = p.DisplayName };
            config.PlayerStatsStore[key] = stat;
        }
        return stat;
    }

    /// <summary>Current bank balance for the player, or zero if no stats record exists.</summary>
    public static long BankBalance(this Player p, Configuration config) =>
        p.TryGetStat(config, out var stat) ? stat.Bank : 0L;
}
