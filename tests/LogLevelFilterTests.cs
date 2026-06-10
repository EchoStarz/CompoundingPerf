using System.Reflection;
using System.Text.RegularExpressions;
using CompoundingPerf.Features;
using Xunit;

namespace CompoundingPerf.Tests;

/// <summary>
/// Validates the wildcard-to-regex translation used by S3's blocklist patterns.
/// We can't easily test the Harmony Prefix in isolation (it depends on SPT types and runtime
/// state), but the matching logic that decides whether a logger name is blocklisted is
/// pure and worth covering.
/// </summary>
public class LogLevelFilterTests
{
    private static Regex GlobToRegex(string pattern)
    {
        // Reflection access to the private static method — it's a pure utility we want
        // to keep behind an internal API but still exercise from tests.
        var m = typeof(LogLevelFilter).GetMethod("GlobToRegex", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Regex)m.Invoke(null, new object[] { pattern })!;
    }

    [Theory]
    [InlineData("SPTarkov.Server.Core.Routers.*", "SPTarkov.Server.Core.Routers.HttpRouter", true)]
    [InlineData("SPTarkov.Server.Core.Routers.*", "SPTarkov.Server.Core.Services.DatabaseService", false)]
    [InlineData("Foo.*",                          "Foo.Bar.Baz",                                   true)]
    [InlineData("Foo.*",                          "FooBar",                                        false)]
    [InlineData("Exact",                          "Exact",                                         true)]
    [InlineData("Exact",                          "Exactish",                                      false)]
    [InlineData("Mid?leMatch",                    "MiddleMatch",                                   true)]
    [InlineData("Mid?leMatch",                    "MidleMatch",                                    false)]
    public void Glob_matches_expected(string pattern, string input, bool shouldMatch)
    {
        var rx = GlobToRegex(pattern);
        Assert.Equal(shouldMatch, rx.IsMatch(input));
    }

    [Fact]
    public void Glob_is_case_insensitive()
    {
        var rx = GlobToRegex("Foo.*");
        Assert.Matches(rx, "foo.bar");
        Assert.Matches(rx, "FOO.BAR");
    }
}
