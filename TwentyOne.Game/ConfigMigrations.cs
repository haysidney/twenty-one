using System;
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
/// Removals require a migration step too: a removed property keeps round-
/// tripping via JsonExtensionData until the migration explicitly drops it.
/// </summary>
public static class ConfigMigrations
{
    public const int CurrentSchemaVersion = 2;

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
            // Drop the now-unknown property from every venue's NarrationTemplates
            // so it doesn't linger in ExtraData forever.
            if (root["Venues"] is JArray venues)
            {
                foreach (var venue in venues.OfType<JObject>())
                    if (venue["NarrationTemplates"] is JObject nt)
                        nt.Remove("PlayerHit");
            }
            version = 2;
        }

        root["SchemaVersion"] = version;
        return root;
    }
}
