using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TwentyOne.Game;
using Xunit;

namespace TwentyOne.Tests;

public class ConfigMigrationTests
{
    private static JObject LoadFixture(string name) =>
        JObject.Parse(File.ReadAllText(Path.Combine("Fixtures", name)));

    [Fact]
    public void Migrate_v0_StampsSchemaVersion()
    {
        var root = LoadFixture("config-v0.json");
        Assert.Null(root["SchemaVersion"]);

        ConfigMigrations.Migrate(root);

        Assert.Equal(ConfigMigrations.CurrentSchemaVersion, (int)root["SchemaVersion"]!);
    }

    [Fact]
    public void Migrate_IsIdempotent()
    {
        var root = LoadFixture("config-v0.json");
        ConfigMigrations.Migrate(root);
        var afterFirst = root.DeepClone();
        ConfigMigrations.Migrate(root);

        Assert.True(JToken.DeepEquals(afterFirst, root));
    }

    [Fact]
    public void Migrate_PreservesExistingFields()
    {
        var root = LoadFixture("config-v0.json");
        var originalVenueName = (string?)root["Venues"]![0]!["Name"];
        var originalCut       = (int?)root["Venues"]![0]!["VenueCutPct"];

        ConfigMigrations.Migrate(root);

        Assert.Equal(originalVenueName, (string?)root["Venues"]![0]!["Name"]);
        Assert.Equal(originalCut,       (int?)root["Venues"]![0]!["VenueCutPct"]);
    }

    [Fact]
    public void Migrate_v1_DropsPlayerHit_FromAllVenues()
    {
        var root = LoadFixture("config-v1.json");
        // Sanity: fixture starts with PlayerHit present on both venues.
        Assert.NotNull(root["Venues"]![0]!["NarrationTemplates"]!["PlayerHit"]);
        Assert.NotNull(root["Venues"]![1]!["NarrationTemplates"]!["PlayerHit"]);

        ConfigMigrations.Migrate(root);

        Assert.Equal(ConfigMigrations.CurrentSchemaVersion, (int)root["SchemaVersion"]!);
        Assert.Null(root["Venues"]![0]!["NarrationTemplates"]!["PlayerHit"]);
        Assert.Null(root["Venues"]![1]!["NarrationTemplates"]!["PlayerHit"]);
        // PlayerHitAnnounce / PlayerAfterHit remain.
        Assert.NotNull(root["Venues"]![0]!["NarrationTemplates"]!["PlayerHitAnnounce"]);
        Assert.NotNull(root["Venues"]![0]!["NarrationTemplates"]!["PlayerAfterHit"]);
    }

    [Fact]
    public void Migrate_v1_FixtureIsIdempotent()
    {
        var root = LoadFixture("config-v1.json");
        ConfigMigrations.Migrate(root);
        var afterFirst = root.DeepClone();
        ConfigMigrations.Migrate(root);
        Assert.True(JToken.DeepEquals(afterFirst, root));
    }

    [Fact]
    public void Migrate_v2_FixtureBumpsToCurrentAndIsIdempotent()
    {
        // config-v2.json carries the legacy orphan keys; the migration no longer
        // surgically removes them (ExtensionDataCleaner drops them on load), so the
        // only job here is the version bump - and it must be idempotent.
        var root = LoadFixture("config-v2.json");
        ConfigMigrations.Migrate(root);
        Assert.Equal(ConfigMigrations.CurrentSchemaVersion, (int)root["SchemaVersion"]!);

        var afterFirst = root.DeepClone();
        ConfigMigrations.Migrate(root);
        Assert.True(JToken.DeepEquals(afterFirst, root));
    }

    [Fact]
    public void Migrate_v3_ClearsUndoStacks()
    {
        // UndoStack/RedoStack changed element shape (GameState -> UndoEntry); the v4
        // step empties them since the old shape can't deserialize into the new type.
        var root = new JObject
        {
            ["SchemaVersion"] = 3,
            ["UndoStack"]     = new JArray(new JObject { ["Phase"] = "Deal" }),
            ["RedoStack"]     = new JArray(new JObject { ["Phase"] = "Betting" }),
        };

        ConfigMigrations.Migrate(root);

        Assert.Equal(ConfigMigrations.CurrentSchemaVersion, (int)root["SchemaVersion"]!);
        Assert.Empty((JArray)root["UndoStack"]!);
        Assert.Empty((JArray)root["RedoStack"]!);
    }

    [Fact]
    public void Migrate_v4_SplitsS17BoolIntoThresholdPair()
    {
        var root = LoadFixture("config-v4.json");
        var venues = (JArray)root["Venues"]!;

        ConfigMigrations.Migrate(root);

        Assert.Equal(ConfigMigrations.CurrentSchemaVersion, (int)root["SchemaVersion"]!);

        // S17 venue: stands on 17 and does NOT hit soft 17.
        Assert.Null(venues[0]["DealerStandsOnSoft17"]);
        Assert.Equal(17, (int)venues[0]["DealerStandThreshold"]!);
        Assert.False((bool)venues[0]["DealerHitsSoftThreshold"]!);

        // H17 venue (explicit false) and a venue that never touched the rule both
        // land on the H17 defaults - which is what they were already playing.
        Assert.Equal(17, (int)venues[1]["DealerStandThreshold"]!);
        Assert.True((bool)venues[1]["DealerHitsSoftThreshold"]!);
        Assert.Equal(17, (int)venues[2]["DealerStandThreshold"]!);
        Assert.True((bool)venues[2]["DealerHitsSoftThreshold"]!);
    }

    [Fact]
    public void Migrate_v4_CarriesRuleIntoRoundHistorySnapshotsAndLiveState()
    {
        // GameState is a record (no ExtensionData), so an unmigrated snapshot would
        // silently fall back to the H17 default and misreport an S17 night.
        var root = LoadFixture("config-v4.json");

        ConfigMigrations.Migrate(root);

        var rounds = (JArray)root["Venues"]![0]!["RoundHistory"]!;
        Assert.All(rounds, r =>
        {
            var snap = r["Snapshot"]!;
            Assert.Null(snap["DealerStandsOnSoft17"]);
            Assert.Equal(17, (int)snap["DealerStandThreshold"]!);
            Assert.False((bool)snap["DealerHitsSoftThreshold"]!);
        });

        var live = root["GameState"]!;
        Assert.Equal(17, (int)live["DealerStandThreshold"]!);
        Assert.True((bool)live["DealerHitsSoftThreshold"]!); // fixture's live state was H17
    }

    [Fact]
    public void Migrate_v4_FixtureIsIdempotent()
    {
        var root = LoadFixture("config-v4.json");
        ConfigMigrations.Migrate(root);
        var afterFirst = root.DeepClone();
        ConfigMigrations.Migrate(root);
        Assert.True(JToken.DeepEquals(afterFirst, root));
    }

    [Fact]
    public void DedupRoundHistory_CollapsesRepeatedBlocks_KeepingFirst()
    {
        // Two distinct rounds, list tripled by the doubling bug: [1,2,1,2,1,2].
        var rounds = Enumerable.Range(0, 3)
            .SelectMany(_ => new[] { 1, 2 })
            .Select(n => new RoundHistoryEntry { RoundNumber = n })
            .ToList();

        var removed = ConfigMigrations.DedupRoundHistory(rounds);

        Assert.Equal(4, removed);
        Assert.Equal(new[] { 1, 2 }, rounds.Select(r => r.RoundNumber));
    }

    [Fact]
    public void DedupRoundHistory_LeavesCleanHistoryUntouched()
    {
        var rounds = Enumerable.Range(1, 5)
            .Select(n => new RoundHistoryEntry { RoundNumber = n })
            .ToList();

        var removed = ConfigMigrations.DedupRoundHistory(rounds);

        Assert.Equal(0, removed);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, rounds.Select(r => r.RoundNumber));
    }
}
