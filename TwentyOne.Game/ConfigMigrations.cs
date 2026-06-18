using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace TwentyOne.Game;

/// <summary>
/// Pure-JObject schema migrations applied at startup before the strong-typed
/// Configuration deserialization. Each step is gated on the persisted
/// SchemaVersion and writes the new version into the JObject before returning,
/// so the chain is idempotent if a crash interrupts the save-back.
///
/// Workflow when bumping the schema:
///   1. Increment <see cref="CurrentSchemaVersion"/>.
///   2. Add an <c>if (version &lt; N)</c> block in <see cref="Migrate"/>.
///   3. Snapshot the previous Save() output into Fixtures/config-v{N-1}.json
///      and add a test in ConfigMigrationTests asserting the migrated shape.
///
/// Field removals do NOT need a migration step: <see cref="ExtensionDataCleaner"/>
/// drops every captured unknown key on load for any config at or below the
/// current version, so a removed property's key is cleaned automatically. A
/// migration step is only needed to <em>transform</em> surviving data (rename a
/// key while keeping its value, restructure an object, backfill a default).
/// </summary>
public static class ConfigMigrations
{
    public const int CurrentSchemaVersion = 3;

    /// <summary>
    /// Runs all pending migrations on <paramref name="root"/> in-place and
    /// returns the same JObject for chaining. Safe to call on a fresh config
    /// (no SchemaVersion field present) - treated as version 0.
    /// </summary>
    public static JObject Migrate(JObject root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var version = (int?)root["SchemaVersion"] ?? 0;

        if (version < 1)
        {
            // No-op bump: the v0 baseline was the first config shape captured
            // after the rename pass (DealerCutPct -> VenueCutPct, TotalWon ->
            // TotalNet, AllowCrossChannelCommands -> CrossChannelCommands).
            version = 1;
        }

        if (version < 2)
        {
            // PlayerHit narration removed - PlayerAfterHit covers the same beat.
            // (Kept for the historical chain; ExtensionDataCleaner would now drop
            // the key on load anyway.)
            if (root["Venues"] is JArray venues)
            {
                foreach (var venue in venues.OfType<JObject>())
                    if (venue["NarrationTemplates"] is JObject nt)
                        nt.Remove("PlayerHit");
            }
            version = 2;
        }

        if (version < 3)
        {
            // No-op bump. v3 introduced load-time ExtensionData cleanup (see
            // ExtensionDataCleaner), which removes the orphaned proxy/session keys
            // that previously had to be dropped here - no JObject surgery needed.
            version = 3;
        }

        root["SchemaVersion"] = version;
        return root;
    }

    /// <summary>
    /// Removes RoundHistory entries that share a RoundNumber, keeping the first
    /// occurrence and preserving order. A live venue's RoundHistory is numbered
    /// 1..N with no repeats, so a duplicate RoundNumber is unambiguous corruption.
    /// Mutates the list in place; returns the number removed. Applied to every
    /// venue on load as a hard bound on the (still-being-diagnosed) doubling.
    /// </summary>
    public static int DedupRoundHistory(List<RoundHistoryEntry> rounds)
    {
        if (rounds.Count < 2) return 0;
        var seen = new HashSet<int>();
        return rounds.RemoveAll(r => !seen.Add(r.RoundNumber));
    }
}
