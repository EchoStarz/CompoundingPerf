# CompoundingPerf

A performance mod for [SPT](https://www.sp-tarkov.com/) 4.0.13 that stacks several small, independent server-side optimizations under one config. Each feature is individually toggleable, designed to coexist with other mods, and held to one rule: **a feature ships only if its cost is bounded by construction** — nothing that could blow up on a heavily-modded install.

## What it does

| Feature | Default | What you get |
|---|---|---|
| **ProfileSaveDebouncer** (S1) | on | Coalesces rapid-fire profile saves. At most one save in flight plus one trailing per profile; the trailing save always captures the latest state, so nothing is ever lost — duplicate work just gets merged. |
| **ResponseCache** (S2) | on | Caches fifteen static-after-load HTTP endpoints (item DB, handbook, hideout recipes, globals…). First request runs the full router chain; repeats serve from memory. |
| **LogLevelFilter** (S3) | on | Drops server log lines from chatty namespaces below a configurable level. |
| **ThreadSafeRandomUtil** (S6) | on | Fixes a latent thread-safety hazard in SPT's `RandomUtil` (shared `System.Random` accessed without synchronization). Pure correctness — no behavior change, prevents RNG-state corruption under concurrent load. |
| **HotPathLogSuppressor** (C4, client) | on | Suppresses below-Warning BepInEx log noise while in raid. SAIN is allowlisted by default so its diagnostics always come through. |

Earlier experiments that didn't meet the bounded-cost bar (background loot pre-generation, shader pre-warming, post-raid GC) were removed rather than shipped disabled

## How it works

Server-side features avoid Harmony where possible in favor of SPT 4.0's DI `TypeOverride` mechanism — `CoalescingSaveServer : SaveServer`, `CachingHttpRouter : HttpRouter`, and `ThreadSafeRandomUtil : RandomUtil` replace the built-ins at container resolution, which means normal C# virtual dispatch, no IL surgery, and other mods' Harmony patches on those classes keep working (our subclass *is* the target they patch). The two remaining Harmony patches (S3's log filter, C4's suppressor) are single Prefixes on log funnels.

Compatibility is explicit: a runtime mod scan adjusts behavior when SAIN, AI Limit, or Performance Improvements are detected, and per-session endpoints (profile, quests, flea search) are never cached.

## Compatibility

- Works on unmodded SPT 4.0.13.
- Works with FIKA 2.2.6.
- Designed to coexist with other mods — DI overrides instead of method replacement where possible, per-session endpoints never cached, and a runtime mod scan adjusts behavior when SAIN, AI Limit, or Performance Improvements are detected.

## Install

Drop the release archive onto your SPT folder, or manually:

- `CompoundingPerf.dll` + `config.json` → `SPT/user/mods/CompoundingPerf/`
- `CompoundingPerf.Client.dll` → `BepInEx/plugins/CompoundingPerf.Client/`

Config lives in the server mod folder; the client reads the same file. Every feature has an `Enabled` flag — flip any of them without touching the rest.

## Building from source

Server targets `net9.0` against the `SPTarkov.Server.Core` 4.0.13 NuGet packages; client targets `net471` against your local SPT install's BepInEx and EFT assemblies (set `-p:SptRoot=<path>` if SPT isn't at `C:\SPT`). Both csproj files auto-deploy to the live install on build — pass `-p:SkipDeploy=true` to build without deploying.

```
dotnet build CompoundingPerf.csproj -c Release
dotnet build client/CompoundingPerf.Client.csproj -c Release
dotnet test tests/CompoundingPerf.Tests.csproj -c Release
```

45 unit tests cover the save-coalescer state machine (including its trailing-edge no-state-loss guarantee), the cache whitelist rules, and the thread-safety of the RandomUtil override under 16-thread hammering.

## License

MIT — see [LICENSE](LICENSE).
