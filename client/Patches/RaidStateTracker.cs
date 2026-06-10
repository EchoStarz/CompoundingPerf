using EFT;
using HarmonyLib;
using CompoundingPerf.Client.Telemetry;

namespace CompoundingPerf.Client.Patches;

/// <summary>
/// Maintains a static <see cref="InRaid"/> flag by hooking GameWorld start/destroy.
/// Every other patch that needs to know "are we currently in a raid?" reads this.
/// Lightweight Postfixes — pure flag flips, no allocation.
/// </summary>
public static class RaidStateTracker
{
    public static bool InRaid { get; private set; }

    [HarmonyPatch(typeof(GameWorld), nameof(GameWorld.OnGameStarted))]
    [HarmonyPostfix]
    public static void OnGameStarted_Postfix()
    {
        InRaid = true;
        ClientTelemetry.Increment("raid_state.started");
    }

    [HarmonyPatch(typeof(GameWorld), nameof(GameWorld.OnDestroy))]
    [HarmonyPostfix]
    public static void OnDestroy_Postfix()
    {
        InRaid = false;
        ClientTelemetry.Increment("raid_state.ended");
    }
}
