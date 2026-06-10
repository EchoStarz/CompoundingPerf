using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Routers;
using CompoundingPerf.Features;
using CompoundingPerf.Telemetry;

namespace CompoundingPerf;

/// <summary>SPT 4.0 mod metadata. No package.json — this record replaces it.</summary>
public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid    { get; init; } = "com.echostarz.compoundingperf";
    public override string Name       { get; init; } = "CompoundingPerf";
    public override string Author     { get; init; } = "EchoStarz";
    public override SemanticVersioning.Version Version    { get; init; } = new("1.2.1");
    public override SemanticVersioning.Range   SptVersion { get; init; } = new("~4.0.13");
    public override string License { get; init; } = "MIT";

    public override List<string>?                              Contributors      { get; init; }
    public override List<string>?                              Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url          { get; init; }
    public override bool?   IsBundleMod  { get; init; }
}

/// <summary>
/// Server-side entry for CompoundingPerf. Reads <c>config.json</c> and turns on the
/// individual features. S1 (profile-save coalescer) is implemented as a DI override
/// — see <see cref="CoalescingSaveServer"/> — so this method only needs to flip its
/// kill-switch flag. S3 (log-level filter) still uses Harmony to patch
/// <c>SptLoggerQueueManager.EnqueueMessage</c>.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 50)]
public class CompoundingPerfMod(
    ISptLogger<CompoundingPerfMod> logger,
    ModHelper                      modHelper,
    HttpRouter                     httpRouter) : IOnLoad
{
    public const string ModGuid = "com.echostarz.compoundingperf";

    public Task OnLoad()
    {
        try
        {
            var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            var config  = modHelper.GetJsonDataFromFile<CompoundingPerfConfig>(modPath, "config.json");

            TelemetryHub.TimingEnabled = config.Telemetry.TimingEnabled;

            // S1: pure flag flip — CoalescingSaveServer is already in the DI container.
            ProfileSaveDebouncer.Apply(config.Server.ProfileSaveDebouncer, logger);

            // S2: configure the DI-overridden router. If our subclass is registered,
            // the DI container resolved `httpRouter` as a CachingHttpRouter and we can
            // turn caching on; otherwise the override didn't take and we log a warning.
            if (httpRouter is CachingHttpRouter cachingRouter)
            {
                cachingRouter.Configure(config.Server.ResponseCache);
                if (config.Server.ResponseCache.Enabled)
                    logger.Success($"[CompoundingPerf/S2] response cache ACTIVE — {CachingHttpRouter.DefaultCacheablePaths.Count} default paths + {config.Server.ResponseCache.AdditionalPaths.Count} additional");
                else
                    logger.Info("[CompoundingPerf/S2] response cache disabled in config");
            }
            else
            {
                logger.Warning($"[CompoundingPerf/S2] HttpRouter DI override did NOT take — actual type is {httpRouter.GetType().FullName}. Response caching inactive.");
            }

            // S3: Harmony patch on SptLoggerQueueManager.EnqueueMessage.
            var harmony = new Harmony(ModGuid);
            LogLevelFilter.Apply(harmony, config.Server.LogLevelFilter, logger);

            // S6: Harmony patch on the non-virtual GetSecureRandomNumber. The DI override
            // of RandomUtil (ThreadSafeRandomUtil) was wired up at container build — by
            // the time OnLoad runs, the container is already resolving RandomUtil
            // dependencies to our subclass automatically.
            if (config.Server.ThreadSafeRandom.Enabled)
            {
                ThreadSafeRandomUtilPatches.Apply(harmony, logger);
                logger.Success("[CompoundingPerf/S6] thread-safe RandomUtil ACTIVE — DI override + GetSecureRandomNumber patched, unblocks S5/S4 concurrency");
            }
            else
            {
                logger.Info("[CompoundingPerf/S6] thread-safe RandomUtil disabled in config");
            }

            logger.Success("[CompoundingPerf] server-side features loaded");
        }
        catch (Exception ex)
        {
            logger.Error($"[CompoundingPerf] failed to load: {ex}");
        }
        return Task.CompletedTask;
    }
}
