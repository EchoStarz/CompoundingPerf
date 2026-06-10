// Compile-included by both the server project (net9.0) and the client project (net471).
// Avoid `required` keyword for net471 compatibility — use defaults instead.

namespace CompoundingPerf;

public record CompoundingPerfConfig
{
    public ServerToggles Server { get; set; } = new();
    public ClientToggles Client { get; set; } = new();
    public TelemetryOptions Telemetry { get; set; } = new();
    public CompatOptions Compat { get; set; } = new();
}

public record ServerToggles
{
    public ProfileSaveDebouncerOptions ProfileSaveDebouncer { get; set; } = new();
    public LogLevelFilterOptions       LogLevelFilter       { get; set; } = new();
    public ResponseCacheOptions        ResponseCache        { get; set; } = new();
    public ThreadSafeRandomOptions     ThreadSafeRandom     { get; set; } = new();
}

public record ClientToggles
{
    public HotPathLogSuppressorOptions HotPathLogSuppressor { get; set; } = new();
}

public record ProfileSaveDebouncerOptions
{
    // V1.0: enabled by default — implemented via SPT.DI TypeOverride of SaveServer
    // (see CoalescingSaveServer). Trailing-edge semantics preserve durability:
    // at most one save in flight + one trailing per profile, the trailing save
    // captures the latest in-memory state, no mutations are dropped.
    public bool Enabled { get; set; } = true;
}

public record ResponseCacheOptions
{
    // V1.1: enabled by default — caches a conservative whitelist of static-after-load
    // endpoints (item DB, handbook, hideout recipes, globals, etc.). First request per
    // path runs through the normal router chain (so other mods' modifications are
    // captured); subsequent requests return the cached JSON directly.
    public bool Enabled { get; set; } = true;

    /// <summary>Extra paths to cache beyond the built-in conservative whitelist.
    /// Use only for paths whose responses don't change at runtime.</summary>
    public List<string> AdditionalPaths { get; set; } = new();
}

public record ThreadSafeRandomOptions
{
    // S6: replace the unsafe instance Random in SPT's RandomUtil with a lock-protected
    // path (for the four virtual methods that touch it: GetDouble, GetBool, RandInt,
    // RandNum) and a Harmony-patched Random.Shared path (for the non-virtual
    // GetSecureRandomNumber). Pure correctness fix — no behavior change, just lets
    // concurrent callers stop corrupting RNG state.
    public bool Enabled { get; set; } = true;
}

public record LogLevelFilterOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Minimum NLog level for the blocklisted namespaces. Below this is dropped.</summary>
    public string MinLevel { get; set; } = "Info";

    /// <summary>NLog logger-name patterns to filter. Supports NLog wildcard syntax.</summary>
    public List<string> BlocklistNamespaces { get; set; } = new() { "SPTarkov.Server.Core.Routers.*" };
}

public record HotPathLogSuppressorOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>While in raid, BepInEx log lines below this level are dropped — unless their
    /// source plugin is in <see cref="AllowlistPlugins"/>.</summary>
    public string MinLevelInRaid { get; set; } = "Warning";

    /// <summary>Plugin source-names that are never suppressed. SAIN's GUID is included by default
    /// because SAIN's logging is signal, not noise.</summary>
    public List<string> AllowlistPlugins { get; set; } = new() { "me.sol.sain", "SAIN" };
}

public record TelemetryOptions
{
    public bool Enabled { get; set; } = false;
    public int DumpEveryNRaids { get; set; } = 10;
    public bool DumpOnRaidEnd { get; set; } = true;
    public bool DumpOnServerShutdown { get; set; } = true;

    /// <summary>Stopwatch timing on hot paths is opt-in because the measurement itself
    /// has overhead. Counters are always cheap.</summary>
    public bool TimingEnabled { get; set; } = false;
}

public record CompatOptions
{
    public bool AutoDisableOnConflict { get; set; } = true;
    public bool Verbose { get; set; } = true;
}
