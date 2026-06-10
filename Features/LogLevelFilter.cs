using System.Text.RegularExpressions;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils.Logger;
using CompoundingPerf.Telemetry;
// SPT and Microsoft both ship a LogLevel — alias SPT's so we don't accidentally use the wrong one.
using LogLevel = SPTarkov.Server.Core.Models.Spt.Logging.LogLevel;

namespace CompoundingPerf.Features;

/// <summary>
/// S3 — Drops SPT log messages whose logger-name matches a blocklist pattern AND whose
/// level is below the configured minimum. Targets <see cref="SptLoggerQueueManager.EnqueueMessage"/>:
/// non-generic, the single funnel for all SPT log messages. Unlike NLog this is SPT's
/// own queue manager — Harmony Prefix returning false drops the message entirely.
/// </summary>
public static class LogLevelFilter
{
    private static Regex[] _blocklistRegexes = Array.Empty<Regex>();
    private static LogLevel _minLevel = LogLevel.Info;
    private static bool _enabled = false;

    public static void Apply(Harmony harmony, LogLevelFilterOptions options, ISptLogger<CompoundingPerfMod> logger)
    {
        if (!options.Enabled) return;

        _minLevel = ParseLevel(options.MinLevel) ?? LogLevel.Info;
        _blocklistRegexes = options.BlocklistNamespaces
            .Select(p => GlobToRegex(p))
            .ToArray();

        if (_blocklistRegexes.Length == 0)
        {
            logger.Warning("[CompoundingPerf/S3] no blocklist patterns configured — filter not applied");
            return;
        }

        var target = AccessTools.Method(typeof(SptLoggerQueueManager), nameof(SptLoggerQueueManager.EnqueueMessage));
        if (target is null)
        {
            logger.Warning("[CompoundingPerf/S3] SptLoggerQueueManager.EnqueueMessage not found — filter not applied");
            return;
        }

        var prefix = new HarmonyMethod(typeof(LogLevelFilter), nameof(Prefix));
        harmony.Patch(target, prefix: prefix);

        _enabled = true;
        logger.Success($"[CompoundingPerf/S3] log filter active — {_blocklistRegexes.Length} patterns at min={_minLevel}");
    }

    /// <summary>
    /// Returns false (skip enqueue) when the message's logger matches a blocklist pattern
    /// AND its level is below the threshold. Otherwise lets the original run.
    /// </summary>
    public static bool Prefix(SptLogMessage message)
    {
        if (!_enabled || message is null) return true;

        // Levels are ordered ascending: Fatal=0, Error=1, Warn=2, Info=3, Debug=4, Trace=5
        // (verify enum order — drop messages whose level value is *greater than or equal to*
        // the threshold's value, because a higher number = less important).
        if ((int)message.LogLevel < (int)_minLevel) return true;

        var name = message.Logger ?? string.Empty;
        for (var i = 0; i < _blocklistRegexes.Length; i++)
        {
            if (_blocklistRegexes[i].IsMatch(name))
            {
                TelemetryHub.Increment("s3.log_filter.dropped");
                return false;
            }
        }
        return true;
    }

    private static LogLevel? ParseLevel(string s) =>
        s?.ToLowerInvariant() switch
        {
            "trace" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            "info"  => LogLevel.Info,
            "warn"  or "warning" => LogLevel.Warn,
            "error" => LogLevel.Error,
            "fatal" => LogLevel.Fatal,
            _ => null
        };

    /// <summary>Convert NLog-style wildcard (*) to a compiled regex anchored at both ends.</summary>
    private static Regex GlobToRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".");
        return new Regex("^" + escaped + "$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }
}
