using System.Text.Json;
using Xunit;

namespace CompoundingPerf.Tests;

public class ConfigTests
{
    [Fact]
    public void Defaults_match_expected_safe_values()
    {
        var c = new CompoundingPerfConfig();

        Assert.True(c.Server.ProfileSaveDebouncer.Enabled);

        Assert.True(c.Server.LogLevelFilter.Enabled);
        Assert.Equal("Info", c.Server.LogLevelFilter.MinLevel);
        Assert.Single(c.Server.LogLevelFilter.BlocklistNamespaces);

        Assert.True(c.Server.ResponseCache.Enabled);
        Assert.True(c.Server.ThreadSafeRandom.Enabled);

        // SAIN's GUID should be in the suppressor allowlist by default — never silence its logs.
        // SAIN's actual BepInEx GUID is me.sol.sain (verified via KmyTarkov listing).
        Assert.Contains("me.sol.sain", c.Client.HotPathLogSuppressor.AllowlistPlugins);

        // Telemetry off by default — opt-in is intentional.
        Assert.False(c.Telemetry.Enabled);
        Assert.False(c.Telemetry.TimingEnabled);
    }

    [Fact]
    public void Json_roundtrip_preserves_values()
    {
        var original = new CompoundingPerfConfig
        {
            Server = new ServerToggles
            {
                ProfileSaveDebouncer = new ProfileSaveDebouncerOptions { Enabled = false },
                ResponseCache = new ResponseCacheOptions { Enabled = false, AdditionalPaths = new List<string> { "/custom/path" } },
            },
            Client = new ClientToggles
            {
                HotPathLogSuppressor = new HotPathLogSuppressorOptions { Enabled = false, MinLevelInRaid = "Info" },
            },
        };

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<CompoundingPerfConfig>(json)!;

        Assert.False(roundTripped.Server.ProfileSaveDebouncer.Enabled);
        Assert.False(roundTripped.Server.ResponseCache.Enabled);
        Assert.Contains("/custom/path", roundTripped.Server.ResponseCache.AdditionalPaths);
        Assert.False(roundTripped.Client.HotPathLogSuppressor.Enabled);
        Assert.Equal("Info", roundTripped.Client.HotPathLogSuppressor.MinLevelInRaid);
    }

    [Fact]
    public void ShippedConfig_json_parses_cleanly()
    {
        // The shipping config.json next to the csproj is the source of truth users see —
        // make sure it still deserializes after any schema changes.
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config.json");
        path = Path.GetFullPath(path);
        Assert.True(File.Exists(path), $"config.json not found at {path}");

        var raw = File.ReadAllText(path);
        var parsed = JsonSerializer.Deserialize<CompoundingPerfConfig>(raw, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        Assert.NotNull(parsed);
        Assert.True(parsed!.Server.ProfileSaveDebouncer.Enabled);
        Assert.True(parsed.Server.LogLevelFilter.Enabled);
        Assert.True(parsed.Server.ResponseCache.Enabled);
        Assert.True(parsed.Server.ThreadSafeRandom.Enabled);
        Assert.True(parsed.Client.HotPathLogSuppressor.Enabled);
    }
}
