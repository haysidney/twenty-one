using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TwentyOne.Game;
using Xunit;

namespace TwentyOne.Tests;

public class ExtensionDataCleanerTests
{
    // Mock types in the TwentyOne.* namespace so the cleaner descends into them,
    // mirroring Configuration / VenueSettings (real list data + an ExtraData bag,
    // plus a [JsonIgnore] proxy that must be skipped).
    private sealed class FakeVenue
    {
        public string Name { get; set; } = "";
        public List<int> RoundHistory { get; set; } = new();
        [JsonExtensionData] public Dictionary<string, JToken> ExtraData { get; set; } = new();
    }

    private sealed class FakeConfig
    {
        public int SchemaVersion { get; set; }
        public List<FakeVenue> Venues { get; set; } = new();
        public Dictionary<string, FakeVenue> ByName { get; set; } = new();
        [JsonIgnore] public FakeVenue Active => Venues[0]; // proxy: would throw if walked on empty
        [JsonExtensionData] public Dictionary<string, JToken> ExtraData { get; set; } = new();
    }

    [Fact]
    public void ClearAll_ClearsNestedExtensionData_PreservesRealData()
    {
        var venue = new FakeVenue { Name = "Eden", RoundHistory = { 1, 2, 3 } };
        venue.ExtraData["StatsSessions"] = JArray.Parse("[1,2,3]");
        var cfg = new FakeConfig { SchemaVersion = 3, Venues = { venue } };
        cfg.ByName["Eden"] = venue;
        cfg.ExtraData["RoundHistory"] = JArray.Parse("[9,9,9]");
        cfg.ExtraData["ActiveVenue"] = JObject.Parse("{\"x\":1}");

        ExtensionDataCleaner.ClearAll(cfg);

        Assert.Empty(cfg.ExtraData);
        Assert.Empty(venue.ExtraData);
        // Real typed data is untouched.
        Assert.Equal(3, cfg.SchemaVersion);
        Assert.Equal("Eden", cfg.Venues[0].Name);
        Assert.Equal(new[] { 1, 2, 3 }, cfg.Venues[0].RoundHistory);
    }

    [Fact]
    public void ClearAll_ReachesVenuesThroughDictionaryValues()
    {
        var venue = new FakeVenue { Name = "Eden" };
        venue.ExtraData["junk"] = JValue.CreateString("x");
        var cfg = new FakeConfig();
        cfg.ByName["Eden"] = venue; // only reachable via the dictionary

        ExtensionDataCleaner.ClearAll(cfg);

        Assert.Empty(venue.ExtraData);
    }

    [Fact]
    public void ClearAll_SkipsJsonIgnoreProxy_AndToleratesNull()
    {
        ExtensionDataCleaner.ClearAll(null); // must not throw

        // Empty Venues makes the Active proxy throw if the walker touched it.
        var cfg = new FakeConfig();
        cfg.ExtraData["orphan"] = JValue.CreateString("x");
        var ex = Record.Exception(() => ExtensionDataCleaner.ClearAll(cfg));
        Assert.Null(ex);
        Assert.Empty(cfg.ExtraData);
    }
}
