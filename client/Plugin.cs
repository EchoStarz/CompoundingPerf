using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using CompoundingPerf.Client.Compat;
using CompoundingPerf.Client.Patches;
using CompoundingPerf.Client.Telemetry;

namespace CompoundingPerf.Client;

[BepInPlugin(ModGuid, ModName, ModVersion)]
public class Plugin : BaseUnityPlugin
{
    public const string ModGuid    = "com.echostarz.compoundingperf.client";
    public const string ModName    = "CompoundingPerf.Client";
    public const string ModVersion = "1.2.1";

    public static CompoundingPerfConfig LoadedConfig { get; private set; } = new();
    public static DetectedMods Mods { get; private set; } = new();
    public static ManualLogSource? Log { get; private set; }
    public static string? SptUserLogsDir { get; private set; }

    private void Awake()
    {
        Log = Logger;
        Log.LogInfo($"{ModName} v{ModVersion} loading");

        try
        {
            var (loaded, sptLogsDir) = ConfigLoader.Load();
            LoadedConfig = loaded;
            SptUserLogsDir = sptLogsDir;
            ClientTelemetry.TimingEnabled = LoadedConfig.Telemetry.TimingEnabled;

            Mods = ModDetector.Scan();
            if (LoadedConfig.Compat.Verbose)
            {
                Log.LogInfo($"{ModName} compat scan: SAIN={Mods.HasSain} AILimit={Mods.HasAiLimit} PerfImprovements={Mods.HasPerformanceImprovements} Tyfon={Mods.HasTyfonUIFixes}");
            }

            // Initialize patch-local static state BEFORE PatchAll wires up the prefixes,
            // so the prefixes never see a partially-constructed allowlist (was a DCL
            // race in V1.2 — the lazy EnsureInitialized() pattern could publish the
            // _initialized flag before the field reference was visible on other threads).
            LogSuppressorPatch.Initialize(LoadedConfig.Client.HotPathLogSuppressor);

            // Apply Harmony patches in this assembly (Patches/ subfolder).
            // Patches early-exit when their config flag is off.
            var harmony = new Harmony(ModGuid);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            Log.LogInfo($"{ModName} ready");
        }
        catch (Exception ex)
        {
            Log.LogError($"{ModName} failed to initialize: {ex}");
        }
    }

    private void OnApplicationQuit()
    {
        if (!LoadedConfig.Telemetry.Enabled || string.IsNullOrEmpty(SptUserLogsDir)) return;
        try { TelemetryDumper.Dump(SptUserLogsDir!, "shutdown"); }
        catch { /* best-effort */ }
    }
}
