using System;
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
    public const int CurrentSchemaVersion = 1;

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
            // No-op stub: confirms the migration plumbing runs and a no-change
            // bump round-trips cleanly. Replace this block when a real v0->v1
            // change is needed; until then, keep the stub so the test exercises
            // the version-bump path.
            version = 1;
        }

        root["SchemaVersion"] = version;
        return root;
    }
}
