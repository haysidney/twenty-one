using System.IO;
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

        Assert.Equal(2, (int)root["SchemaVersion"]!);
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
}
