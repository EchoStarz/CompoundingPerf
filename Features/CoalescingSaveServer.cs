using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using CompoundingPerf.Telemetry;

namespace CompoundingPerf.Features;

/// <summary>
/// S1 — Subclass of <see cref="SaveServer"/> registered via SPT.DI's
/// <see cref="Injectable.TypeOverride"/> mechanism. Replaces the built-in SaveServer at
/// container resolution time, so every consumer that depends on <c>SaveServer</c> gets
/// our coalescing version instead.
///
/// <para>Why DI override instead of Harmony: <see cref="SaveServer.SaveProfileAsync"/> is
/// an <c>async</c>-keyword method whose IL references a compiler-generated state machine
/// internal to SPT.Server.Core. Harmony's reverse-patch can't safely copy that IL into
/// our assembly. Normal C# virtual dispatch via <c>override</c> + <c>base</c> sidesteps
/// the entire problem — the runtime does the right thing automatically.</para>
///
/// <para>Behavior: when <see cref="ProfileSaveDebouncer.IsEnabled"/> is true, calls are
/// routed through <see cref="SaveCoalescer{TKey,TResult}"/> — at most one save in flight
/// + one trailing per profile. The trailing save captures the latest in-memory state when
/// it fires, so no profile mutations are ever dropped. When disabled, calls pass through
/// directly to <c>base.SaveProfileAsync</c> with zero overhead.</para>
/// </summary>
[Injectable(TypeOverride = typeof(SaveServer), TypePriority = 100)]
public class CoalescingSaveServer : SaveServer
{
    private readonly SaveCoalescer<MongoId, long> _coalescer;

    public CoalescingSaveServer(
        FileUtil                                   fileUtil,
        IEnumerable<SaveLoadRouter>                saveLoadRouters,
        JsonUtil                                   jsonUtil,
        HashUtil                                   hashUtil,
        ServerLocalisationService                  serverLocalisationService,
        ProfileValidatorService                    profileValidatorService,
        BackupService                              backupService,
        ISptLogger<SaveServer>                     logger,
        ConfigServer                               configServer)
        : base(fileUtil, saveLoadRouters, jsonUtil, hashUtil,
               serverLocalisationService, profileValidatorService,
               backupService, logger, configServer)
    {
        _coalescer = new SaveCoalescer<MongoId, long>(InvokeBaseSave);
    }

    public override Task<long> SaveProfileAsync(MongoId sessionID)
    {
        if (!ProfileSaveDebouncer.IsEnabled)
        {
            // Kill-switch: behave exactly like the built-in SaveServer.
            return base.SaveProfileAsync(sessionID);
        }

        TelemetryHub.Increment("s1.profile_save.requested");
        return _coalescer.RequestSave(sessionID);
    }

    private Task<long> InvokeBaseSave(MongoId sessionID)
    {
        TelemetryHub.Increment("s1.profile_save.executed");
        return base.SaveProfileAsync(sessionID);
    }
}
