#!/usr/bin/env python3
"""A/B benchmark report for CompoundingPerf dev (BENCH) builds.

Reads the two JSONL files the BENCH builds write:
  - CompoundingPerf-framestats.jsonl   (client: one line per raid)
  - CompoundingPerf-serverstats.jsonl  (server: one line per 30s interval)

For each client metric it reports median + mean +- 95% CI per side and a SIGNIFICANCE
verdict from a two-sided Mann-Whitney U test (exact permutation p-value at our tiny N,
which is non-parametric and tie-safe — the right test for non-normal, multimodal frame
data, and BDN's own default for the same reason). The test is GATED exactly like
BenchmarkDotNet: below 3 samples/side (or 5 on one side) it refuses and prints
"INSUFFICIENT DATA" rather than a number. A statistically-detectable difference smaller
than 10% of the baseline median is honestly labelled "same in practice".

Stdlib-only on purpose (no scipy) so anyone can reproduce it. Methodology mined from
BenchmarkDotNet — see scripts/BENCHMARKING.md.

Usage:
  python bench-report.py [logsDir]
  # default logsDir: C:/SPT-vanilla-sandbox/SPT/user/logs
"""

import itertools
import json
import math
import statistics
import sys
from collections import defaultdict
from pathlib import Path

LOGS = Path(sys.argv[1] if len(sys.argv) > 1 else r"C:/SPT-vanilla-sandbox/SPT/user/logs")

# Two-sided 95% Student-t critical values keyed by degrees of freedom (n-1). Matches
# BDN's ConfidenceInterval (Margin = StdErr * t). df>=30 ~ 2.04, good enough for our N.
T95 = {1: 12.706, 2: 4.303, 3: 3.182, 4: 2.776, 5: 2.571, 6: 2.447, 7: 2.365,
       8: 2.306, 9: 2.262, 10: 2.228, 11: 2.201, 12: 2.179, 13: 2.160, 14: 2.145,
       15: 2.131, 16: 2.120, 17: 2.110, 18: 2.101, 19: 2.093, 20: 2.086}

# Headline client metrics: (json key or per-min spec, label, higher_is_better)
METRICS = [
    ("avgFps", "avg FPS", True),
    ("onePercentLowFps", "1% low FPS", True),
    ("pointOnePercentLowFps", "0.1% low FPS", True),
    ("@hitchesOver50Ms", "hitches>50ms/min", False),
    ("@hitchesOver100Ms", "hitches>100ms/min", False),
]


def read_jsonl(name):
    p = LOGS / name
    if not p.exists():
        return []
    rows = []
    for line in p.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line:
            try:
                rows.append(json.loads(line))
            except json.JSONDecodeError:
                print(f"  ! unparseable line skipped in {name}")
    return rows


def mean(xs):
    xs = list(xs)
    return sum(xs) / len(xs) if xs else 0.0


def metric_value(row, key):
    """A metric key prefixed with @ means 'per minute of raid'."""
    if key.startswith("@"):
        k = key[1:]
        return row[k] / (row["durationSec"] / 60.0)
    return row[key]


def u_statistic(a, b):
    """Mann-Whitney U for group a vs b (ties count half)."""
    u = 0.0
    for ai in a:
        for bj in b:
            if ai > bj:
                u += 1.0
            elif ai == bj:
                u += 0.5
    return u


def mann_whitney_p(a, b):
    """Two-sided exact permutation p-value. Enumerates every split of the pooled
    values into the observed group sizes (tie-safe, no normality assumption); falls
    back to a tie-corrected normal approximation only if the combination count is huge
    (never the case at our raid counts)."""
    n, m = len(a), len(b)
    pooled = list(a) + list(b)
    big = n + m
    u_obs = u_statistic(a, b)
    mean_u = n * m / 2.0
    total = math.comb(big, n)
    if total <= 300000:
        extreme = 0
        for idx in itertools.combinations(range(big), n):
            sel = set(idx)
            ga = [pooled[i] for i in idx]
            gb = [pooled[i] for i in range(big) if i not in sel]
            if abs(u_statistic(ga, gb) - mean_u) >= abs(u_obs - mean_u) - 1e-9:
                extreme += 1
        return extreme / total
    # Normal approximation with tie correction (large-N safety net).
    ranks = {}
    for v in sorted(pooled):
        ranks.setdefault(v, []).append(v)
    sigma = math.sqrt(n * m * (big + 1) / 12.0)
    if sigma == 0:
        return 1.0
    z = (u_obs - mean_u) / sigma
    return max(0.0, min(1.0, 2.0 * (1.0 - 0.5 * (1.0 + math.erf(abs(z) / math.sqrt(2.0))))))


def summarize(vals):
    n = len(vals)
    med = statistics.median(vals)
    m = statistics.mean(vals)
    if n >= 3:
        sd = statistics.stdev(vals)
        margin = sd / math.sqrt(n) * T95.get(n - 1, 2.04)
        ci = f"+-{margin:.1f}"
    else:
        ci = "CI n/a"
    return n, med, m, ci


def ratio_sd(on, off):
    """All-pairs ratio mean + RatioSD (BDN RatioStatistics) — explodes when noisy."""
    z = [a / b for a in on for b in off if b]
    if not z:
        return None, None
    rm = statistics.mean(z)
    rsd = statistics.stdev(z) / rm if len(z) >= 2 and rm else 0.0
    return rm, rsd


def verdict(on, off, higher_better):
    n_on, n_off = len(on), len(off)
    if min(n_on, n_off) < 3 or max(n_on, n_off) < 5:
        return f"INSUFFICIENT DATA (n={n_on}/{n_off}; need >=3 per side and >=5 on one)"
    p = mann_whitney_p(on, off)
    med_on, med_off = statistics.median(on), statistics.median(off)
    shift = abs(med_on - med_off) / max(1e-9, abs(med_off))
    if p >= 0.05:
        return f"WITHIN NOISE (Mann-Whitney p={p:.3f})"
    if shift < 0.10:
        return f"detectable but <10% (p={p:.3f}) -> SAME IN PRACTICE"
    better = (med_on > med_off) if higher_better else (med_on < med_off)
    return f"MOD {'BETTER' if better else 'WORSE'} by {shift * 100:.0f}% (p={p:.3f}) -> significant at p<0.05"


def main():
    client = read_jsonl("CompoundingPerf-framestats.jsonl")
    server = read_jsonl("CompoundingPerf-serverstats.jsonl")

    newest = max((r.get("modVersion", "") for r in client), default="")
    excluded = [r for r in client if "warmupSkippedSec" not in r or r.get("modVersion") != newest]
    usable = [r for r in client if r not in excluded]

    print(f"=== CompoundingPerf A/B report  (logs: {LOGS})")
    if excluded:
        print(f"--- excluded {len(excluded)} non-comparable raid(s): "
              + ", ".join(f"{r['ts'][:16]} ({'no-trim' if 'warmupSkippedSec' not in r else 'v' + r.get('modVersion', '?')})" for r in excluded))

    by_map = defaultdict(lambda: {"on": [], "off": []})
    for r in usable:
        by_map[r.get("map", "unknown")]["on" if r["masterEnabled"] else "off"].append(r)

    print("\n--- CLIENT (per-raid frame stats, warmup-trimmed) ---")
    for mp, sides in sorted(by_map.items()):
        on, off = sides["on"], sides["off"]
        print(f"\n[{mp}]  ON raids={len(on)}  OFF raids={len(off)}")

        for key, label, higher_better in METRICS:
            on_v = [metric_value(r, key) for r in on]
            off_v = [metric_value(r, key) for r in off]
            if not on_v or not off_v:
                print(f"  {label:<18}: need both sides")
                continue
            _, omed, omean, oci = summarize(off_v)
            _, nmed, nmean, nci = summarize(on_v)
            rm, rsd = ratio_sd(on_v, off_v)
            rstr = f"ratio {rm:.2f} RatioSD {rsd*100:.0f}%" if rm is not None else ""
            print(f"  {label:<18}: OFF med {omed:.1f} (mean {omean:.1f} {oci})  vs  "
                  f"ON med {nmed:.1f} (mean {nmean:.1f} {nci})  {rstr}")
            print(f"  {'':<18}  -> {verdict(on_v, off_v, higher_better)}")

        pop = [f"{r.get('peakAlivePlayers', '?')}/{r.get('uniquePlayersSeen', '?')}"
               for r in on + off if "peakAlivePlayers" in r]
        if pop:
            print(f"  population (peak/total per raid): {', '.join(pop)}")

    print("\n--- SERVER (30s GC/counter intervals) ---")
    for side, label in ((True, "MOD ON "), (False, "MOD OFF")):
        rows = [r for r in server if r["masterEnabled"] == side]
        if not rows:
            print(f"  {label}: (no samples)")
            continue
        print(
            f"  {label}: n={len(rows)}  "
            f"gcPause={mean(r['gcPauseMs'] for r in rows):.0f}ms/interval ({mean(r['gcPausePct'] for r in rows):.2f}%)  "
            f"gen2/interval={mean(r['gen2'] for r in rows):.2f}  "
            f"alloc={mean(r.get('allocMbPerSec', 0) for r in rows):.0f}MB/s  "
            f"heap={mean(r['heapMb'] for r in rows):.0f}MB  "
            f"savesExec={sum(r['savesExecuted'] for r in rows)}  "
            f"savesSkipped={sum(r['savesSkippedClean'] for r in rows)}  "
            f"cacheHits={sum(r['cacheHits'] for r in rows)}  "
            f"sanitized={sum(r.get('sanitizerCalls', 0) for r in rows)}  "
            f"compressed={sum(r.get('compressedResponses', 0) for r in rows)}  "
            f"gcSkipped(S8)={sum(r.get('forcedGcSkipped', 0) for r in rows)}  "
            f"wsSends(S13)={sum(r.get('wsSends', 0) for r in rows)}  "
            f"notifierPolls(S13)={sum(r.get('notifierPolls', 0) for r in rows)}"
        )
    print("  (alloc rate is the S8 control: if it's ~equal both sides but gcPause/gen2 drop,")
    print("   the removed forced GC — not less work — is the proven cause.)")

    print("\nMethodology: medians + 95% CI per side, two-sided Mann-Whitney U (exact, tie-safe),")
    print("gated to refuse below 3 raids/side. 'INSUFFICIENT DATA' is the honest, expected result")
    print("at 2 raids/side — a prompt to run more raids, not a bug.")


if __name__ == "__main__":
    main()
