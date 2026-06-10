using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using CompoundingPerf.Client.Telemetry;

namespace CompoundingPerf.Client.Patches;

/// <summary>
/// C4 — Prefix on <see cref="ManualLogSource.Log"/>. While the player is in a raid
/// (per <see cref="RaidStateTracker.InRaid"/>), drops log entries whose level is below
/// <see cref="HotPathLogSuppressorOptions.MinLevelInRaid"/> — UNLESS the source plugin
/// is in the allowlist. SAIN's GUID is in the allowlist by default so its diagnostics
/// always come through.
/// </summary>
[HarmonyPatch(typeof(ManualLogSource), nameof(ManualLogSource.Log))]
public static class LogSuppressorPatch
{
    // Initialized eagerly from Plugin.Awake() — see Initialize() below. Avoiding the
    // double-checked-locking pattern entirely because the Prefix runs on many threads
    // concurrently under heavy logging; DCL without a memory barrier can publish the
    // _initialized=true flag before _allowlist's reference is visible, causing
    // NullReferenceException on _allowlist!.Contains under load.
    private static HashSet<string> _allowlist = new(System.StringComparer.OrdinalIgnoreCase);
    private static LogLevel _minInRaid = LogLevel.Warning;

    /// <summary>
    /// Called once at Plugin.Awake() before any Harmony patches are applied. Single-threaded
    /// init eliminates the DCL race that an EnsureInitialized() pattern would have.
    /// </summary>
    public static void Initialize(HotPathLogSuppressorOptions opts)
    {
        _allowlist = new HashSet<string>(opts.AllowlistPlugins ?? new List<string>(), System.StringComparer.OrdinalIgnoreCase);
        _minInRaid = ParseLevel(opts.MinLevelInRaid);
    }

    /// <returns><c>false</c> to skip the original logging call.</returns>
    [HarmonyPrefix]
    public static bool Prefix(ManualLogSource __instance, LogLevel level)
    {
        if (!Plugin.LoadedConfig.Client.HotPathLogSuppressor.Enabled) return true;
        if (!RaidStateTracker.InRaid) return true;

        // BepInEx LogLevel is a [Flags] enum: Fatal=1, Error=2, Warning=4, Message=8,
        // Info=16, Debug=32. *Lower* numeric value = more severe. To pass our threshold,
        // the message's bits must include something at-or-above the configured level.
        if ((level & LevelMask(_minInRaid)) != 0) return true;

        // Source allowlist: never suppress logs from these plugins.
        if (_allowlist.Contains(__instance.SourceName)) return true;

        ClientTelemetry.Increment("c4.log_suppressor.dropped");
        return false;
    }

    /// <summary>Build a flags mask that includes the given level and every more-severe level.</summary>
    private static LogLevel LevelMask(LogLevel min) => min switch
    {
        LogLevel.Fatal   => LogLevel.Fatal,
        LogLevel.Error   => LogLevel.Fatal | LogLevel.Error,
        LogLevel.Warning => LogLevel.Fatal | LogLevel.Error | LogLevel.Warning,
        LogLevel.Message => LogLevel.Fatal | LogLevel.Error | LogLevel.Warning | LogLevel.Message,
        LogLevel.Info    => LogLevel.Fatal | LogLevel.Error | LogLevel.Warning | LogLevel.Message | LogLevel.Info,
        LogLevel.Debug   => LogLevel.All,
        _                => LogLevel.All,
    };

    private static LogLevel ParseLevel(string s) => s?.ToLowerInvariant() switch
    {
        "fatal"   => LogLevel.Fatal,
        "error"   => LogLevel.Error,
        "warn" or "warning" => LogLevel.Warning,
        "message" => LogLevel.Message,
        "info"    => LogLevel.Info,
        "debug"   => LogLevel.Debug,
        _         => LogLevel.Warning,
    };
}
