# Changelog

## 1.2.1 — 2026-06-09

Fixes from an automated multi-lens code audit of our own source:

- **Fixed**: `CachingHttpRouter`'s cache used a case-sensitive comparer while its path whitelist was case-insensitive — variant-case requests could double-cache the same payload.
- **Fixed**: `LogSuppressorPatch` initialized its allowlist lazily from a Harmony Prefix with no memory barrier (double-checked-locking race). Under heavy multi-threaded logging this could observe a half-published state and throw. Initialization is now eager, from `Plugin.Awake()`, before any patch is applied.
- **Removed**: the three disabled experimental features (RaidLootPrewarmer, ShaderWarmup, PostRaidGC) are gone from the codebase rather than shipping as off-by-default toggles. Their history below stands; if a future version brings one back, it will be as a redesigned implementation that meets the bounded-cost bar.

## 1.2.0 — 2026-06-01

- **Added — S6 ThreadSafeRandomUtil**: DI override of SPT's `RandomUtil`. Four virtual methods that still touch the shared non-thread-safe `System.Random` (`GetDouble`, `GetBool`, `RandInt`, `RandNum`) are now lock-wrapped; the private `GetSecureRandomNumber` is Harmony-patched to `Random.Shared`. Pure correctness fix; unblocks future concurrency work (S5 re-enable, parallel bot generation).
- S5 RaidLootPrewarmer remains disabled pending re-validation on top of S6.

## 1.1.0 — 2026-05-26

- **Added — S2 ResponseCache**: DI override of `HttpRouter` caching fifteen static-after-load endpoints. First request per path runs the normal chain (other mods' modifications are captured); repeats serve from memory.
- **Added, then disabled — S5 RaidLootPrewarmer**: background pre-generation of next raid's loot. Crashed in play: the background generation raced on SPT's non-thread-safe `RandomUtil`. Disabled by default; root cause later fixed by S6.
- **Added, then disabled — C5 ShaderWarmup**: `Shader.WarmupAllShaders` during first raid load took 85 seconds on a ~50-plugin install. Disabled by default.
- Build: `-p:SkipDeploy=true` escape hatch for building while SPT is running.
- Fixed SAIN detection GUID (`me.sol.sain`).

## 1.0.0 — 2026-04-28

Initial release.

- **S1 ProfileSaveDebouncer**: trailing-edge save coalescer. Originally implemented via `HarmonyReversePatch` on the async `SaveProfileAsync` — which failed silently and broke saves (async kickoff IL can't be safely copied cross-assembly). Reimplemented the same day as a DI `TypeOverride` subclass of `SaveServer` using plain virtual dispatch; verified across multiple raid sessions since.
- **S3 LogLevelFilter**: Harmony Prefix on `SptLoggerQueueManager.EnqueueMessage` dropping blocklisted-namespace lines below a configurable level.
- **C4 HotPathLogSuppressor** + **RaidStateTracker** (client): in-raid BepInEx log suppression with a plugin allowlist.
- **C2 PostRaidGC** (client): post-raid forced GC. Caused a ~974 ms hitch on a 2 GB heap even with the gentlest collector flags; disabled by default.
