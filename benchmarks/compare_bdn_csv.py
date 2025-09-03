#!/usr/bin/env python3

"""
compare_bdn_csv.py — Compare two BenchmarkDotNet CSV exports (old vs new)

Usage:
  python compare_bdn_csv.py old.csv new.csv [--key KEYCSV] [--metrics METRICCSV]
                                            [--time-unit ns|us|ms|s]
                                            [--mem-unit B|KB|MB|GB]
                                            [--warn-time PCT] [--warn-alloc PCT] [--warn-throughput PCT]
                                            [--fail-on-regression]
                                            [--out compare.csv]

Examples:
  python compare_bdn_csv.py baseline.csv today.csv
  python compare_bdn_csv.py baseline.csv today.csv --key "Type,Method,Runtime,Job"
  python compare_bdn_csv.py baseline.csv today.csv --metrics "Mean,P95,Allocated,Op/s" --fail-on-regression

Notes:
- Works with standard BenchmarkDotNet CSV exporter.
- Tries hard to parse values regardless of whether units (ns, ms, s, KB, MB, GB) are in the cell or not.
- By default we auto-pick a stable key (Type,Method,Runtime,Job,Params if present).
- Deltas are percent: ((new - old) / old) * 100.
  * For time & memory: NEGATIVE is GOOD (faster / less memory).
  * For throughput (Op/s): POSITIVE is GOOD (more ops per second).
  * For GC counts (Gen 0/1/2): NEGATIVE is GOOD (fewer collections per 1k ops).

Exit codes:
  0 = OK (no regressions or not failing on regression)
  2 = Regressions found and --fail-on-regression specified
"""
import argparse
import csv
import math
import os
import re
import sys
from typing import Dict, List, Tuple, Optional

# ---------- Helpers for parsing & units ----------

TIME_METRICS = {"mean", "median", "p95", "min", "max", "q1", "q3", "stddev", "error"}
MEM_METRICS = {"allocated", "allocated/op", "alloc/op", "alloc b/op", "alloc"}
THROUGHPUT_METRICS = {"op/s", "ops/s", "op per s", "ops per s", "operationspersecond", "operations/s"}
GC_METRICS = {"gen 0", "gen 1", "gen 2"}

# Common metric header candidates (case-insensitive). We will take the intersection with actual headers.
CANDIDATE_METRICS = [
    "Mean", "Median", "P95", "Min", "Max", "StdDev", "Error",
    "Op/s", "OperationsPerSecond",
    "Allocated", "Alloc B/op", "Allocated/op",
    "Gen 0", "Gen 1", "Gen 2"
]

# Environment / non-identity columns to ignore when auto-picking keys
NON_ID_COLS = set([
    # stats/metrics (case-insensitive match)
    "mean","median","p95","min","max","q1","q3","stddev","error","op/s","operationspersecond","allocated",
    "alloc b/op","allocated/op","gen 0","gen 1","gen 2",
    # ranking/etc
    "rank","baseline",
    # run config that we usually *keep* in the key if present (Runtime, Job, Platform, Jit)
    # but we exclude them here only for the "param guessing"; we add them explicitly later.
    # Also ignore obvious extras:
    "iterationcount","launchcount","warmupcount","invocationcount","unrollfactor","toolchain",
    "evaluateoverhead","powerplan","servergc","concurrencyvisualizer",
    # misc
    "n","description","hardwareintrinsics","hardwarecounter","memoryrandomization","enginefactory",
])

TIME_UNITS = {"ns": 1.0, "us": 1_000.0, "µs": 1_000.0, "μs": 1_000.0, "ms": 1_000_000.0, "s": 1_000_000_000.0}
MEM_UNITS = {"b": 1, "kb": 1024, "kB": 1024, "mb": 1024**2, "gb": 1024**3}

def norm(s: str) -> str:
    return re.sub(r"\s+", " ", s.strip()).strip()

def normkey(s: str) -> str:
    return re.sub(r"\s+", " ", s.strip()).lower()

num_re = re.compile(r"[-+]?\d[\d,]*\.?\d*(?:[eE][-+]?\d+)?")

def parse_num(s: str) -> Optional[float]:
    """Extract first float from a string, ignoring commas and trailing text."""
    if s is None:
        return None
    if isinstance(s, (int, float)):
        return float(s)
    s = str(s).strip()
    if not s:
        return None
    m = num_re.search(s)
    if not m:
        return None
    token = m.group(0).replace(",", "")
    try:
        return float(token)
    except ValueError:
        return None

def unit_suffix(s: str) -> str:
    """Return a normalized unit suffix found after the first number token."""
    if s is None:
        return ""
    s = str(s).strip()
    m = num_re.search(s)
    if not m:
        return ""
    suffix = s[m.end():].strip().lower()
    # collapse things like "/op", "/ s", " per s" etc
    suffix = suffix.replace("per second", "/s").replace("per sec", "/s").replace(" per s", "/s")
    suffix = suffix.replace("μs", "us").replace("µs", "us")
    return suffix

def to_base_time(val: float, unit: str) -> float:
    """Convert any time to nanoseconds (ns)."""
    for u, factor in TIME_UNITS.items():
        if unit.startswith(u):
            return val * factor
    # No known unit; assume ns
    return val

def to_base_mem(val: float, unit: str) -> float:
    """Convert any mem to bytes."""
    u = unit
    if not u:
        # often Allocated has no suffix and is already bytes/op
        return val
    u = u.replace("bytes", "b").replace("byte", "b")
    u = u.replace("kb", "kb").replace("kib", "kb")
    u = u.replace("mb", "mb").replace("mib", "mb")
    u = u.replace("gb", "gb").replace("gib", "gb")
    u = u.strip().lower()
    if u in ("b",):
        return val
    if u in ("kb", "kib", "k"):
        return val * 1024.0
    if u in ("mb", "mib", "m"):
        return val * 1024.0**2
    if u in ("gb", "gib", "g"):
        return val * 1024.0**3
    return val

def metric_kind(header_name: str) -> str:
    k = normkey(header_name)
    if k in THROUGHPUT_METRICS:
        return "throughput"
    if k in MEM_METRICS or "alloc" in k or k.startswith("allocated"):
        return "memory"
    if k in GC_METRICS:
        return "gc"
    if k in TIME_METRICS:
        return "time"
    # Heuristics
    if "/s" in k or "persecond" in k:
        return "throughput"
    return "time"

def convert_to_base(metric: str, raw_value: str) -> Optional[float]:
    """Convert raw cell (possibly with units) to base units for comparison."""
    n = parse_num(raw_value)
    if n is None:
        return None
    k = metric_kind(metric)
    suf = unit_suffix(raw_value)
    if k == "time":
        return to_base_time(n, suf)
    if k == "memory":
        return to_base_mem(n, suf)
    # gc or throughput or unknown -> just the numeric
    return n

def fmt_value(metric: str, base_val: Optional[float], time_unit: str, mem_unit: str) -> str:
    if base_val is None or (isinstance(base_val, float) and math.isnan(base_val)):
        return "-"
    k = metric_kind(metric)
    if k == "time":
        # convert ns -> selected
        factor = TIME_UNITS.get(time_unit, 1.0)
        v = base_val / factor
        return f"{v:.3f} {time_unit}"
    if k == "memory":
        mu = mem_unit.lower()
        denom = 1.0
        if mu == "kb":
            denom = 1024.0
        elif mu == "mb":
            denom = 1024.0**2
        elif mu == "gb":
            denom = 1024.0**3
        v = base_val / denom
        return f"{v:.3f} {mem_unit.upper()}"
    # throughput or gc
    if k == "throughput":
        return f"{base_val:.3f} ops/s"
    return f"{base_val:.3f}"

def arrow(delta_pct: float, metric: str) -> str:
    """Return arrow indicating good/bad: ↓ good for time/mem/gc; ↑ good for throughput."""
    k = metric_kind(metric)
    if k in ("time", "memory", "gc"):
        return "↓" if delta_pct < 0 else ("↑" if delta_pct > 0 else "→")
    else:
        return "↑" if delta_pct > 0 else ("↓" if delta_pct < 0 else "→")

def pct(delta: Optional[float]) -> str:
    if delta is None or (isinstance(delta, float) and math.isnan(delta)):
        return "-"
    sign = "+" if delta > 0 else ""
    return f"{sign}{delta:.2f}%"

# ---------- CSV loading & key building ----------

def read_csv(path: str) -> Tuple[List[Dict[str, str]], List[str]]:
    with open(path, newline="", encoding="utf-8-sig") as f:
        rdr = csv.DictReader(f)
        rows = [ {h: v for h, v in row.items()} for row in rdr ]
        headers = rdr.fieldnames or []
    return rows, headers

def pick_key_columns(headers_a: List[str], headers_b: List[str], user_keys: Optional[List[str]] = None) -> List[str]:
    H1 = [h for h in headers_a]
    H2 = [h for h in headers_b]
    s1 = {normkey(h): h for h in H1}
    s2 = {normkey(h): h for h in H2}

    if user_keys:
        picked = []
        for k in user_keys:
            nk = normkey(k)
            if nk in s1 and nk in s2:
                picked.append(s1[nk])  # use original casing from first file
        if picked:
            return picked

    # Preferred identity columns in order
    preferred = ["FullName", "Benchmark", "Namespace", "Type", "Method", "Parameters", "Param", "Arguments"]
    # run config we DO like to include if present
    run_cfg = ["Runtime", "Job", "Platform", "Jit"]

    picked = []
    # If 'FullName' or 'Benchmark' exist, use that alone
    for p in preferred[:2]:
        if normkey(p) in s1 and normkey(p) in s2:
            return [s1[normkey(p)]]

    # else build composite
    for p in preferred[2:]:
        if normkey(p) in s1 and normkey(p) in s2:
            picked.append(s1[normkey(p)])

    for p in run_cfg:
        if normkey(p) in s1 and normkey(p) in s2:
            picked.append(s1[normkey(p)])

    # Add any obvious parameter columns (present in both and not a known metric)
    for h in H1:
        nk = normkey(h)
        if nk in NON_ID_COLS:
            continue
        # parameters are often custom names; include if present in both and look like a value (e.g., "N", "Size", "Payload")
        if nk in s2 and nk not in [normkey(x) for x in picked]:
            # Avoid adding numeric metric candidates by checking if the column is in candidate metrics
            if nk not in [normkey(x) for x in CANDIDATE_METRICS]:
                picked.append(h)

    if not picked:
        # Fallback to Method if nothing else
        if "Method" in s1.values() and "Method" in s2.values():
            return [s1["method"]]
        # else fallback to all shared headers minus non-id cols
        shared = [s1[nk] for nk in s1.keys() if nk in s2 and nk not in NON_ID_COLS]
        if shared:
            return shared
    return picked

def build_key(row: Dict[str, str], key_cols: List[str]) -> str:
    parts = []
    for c in key_cols:
        val = row.get(c, "")
        parts.append(f"{c}={val}")
    return " | ".join(parts)

def pick_metrics(headers_a: List[str], headers_b: List[str], requested: Optional[List[str]]) -> List[str]:
    s1 = set([normkey(h) for h in headers_a])
    s2 = set([normkey(h) for h in headers_b])

    if requested:
        res = []
        for m in requested:
            nm = normkey(m)
            # find original header case from either file
            orig = None
            for h in headers_a:
                if normkey(h) == nm:
                    orig = h; break
            if not orig:
                for h in headers_b:
                    if normkey(h) == nm:
                        orig = h; break
            if orig and nm in s1 and nm in s2:
                res.append(orig)
        if res:
            return res

    # else auto-pick intersection of known metrics
    res = []
    for cand in CANDIDATE_METRICS:
        nc = normkey(cand)
        for h in headers_a:
            if normkey(h) == nc and nc in s2:
                res.append(h)
                break
    # Keep unique while preserving order
    seen = set()
    uniq = []
    for r in res:
        rk = normkey(r)
        if rk not in seen:
            uniq.append(r); seen.add(rk)
    return uniq

# ---------- Comparison ----------

def compare(old_rows, new_rows, key_cols, metrics, time_unit, mem_unit,
            warn_time, warn_alloc, warn_throughput, fail_on_regression, out_path):
    # Index by key
    old_map = { build_key(r, key_cols): r for r in old_rows }
    new_map = { build_key(r, key_cols): r for r in new_rows }
    keys = sorted(set(old_map.keys()) | set(new_map.keys()))

    # Prepare output
    out_csv_rows = []
    regressions = []

    # Header for console
    header_cells = ["Benchmark Key", "Status"]
    for m in metrics:
        header_cells.extend([f"{m} (old)", f"{m} (new)", f"{m} Δ"])
    widths = [max(16, len(h)) for h in header_cells]

    def print_row(cells):
        line = "  ".join(str(c).ljust(w) for c, w in zip(cells, widths))
        print(line)

    print_row(header_cells)
    print("-" * (sum(widths) + 2 * (len(widths) - 1)))

    for k in keys:
        o = old_map.get(k)
        n = new_map.get(k)
        status = "changed" if o and n else ("added" if n and not o else "removed")
        row_cells = [k, status]
        for m in metrics:
            old_raw = o.get(m, "") if o else ""
            new_raw = n.get(m, "") if n else ""
            old_base = convert_to_base(m, old_raw) if o else None
            new_base = convert_to_base(m, new_raw) if n else None

            old_disp = fmt_value(m, old_base, time_unit, mem_unit) if o else "-"
            new_disp = fmt_value(m, new_base, time_unit, mem_unit) if n else "-"

            delta = None
            if o and n and old_base is not None and old_base != 0:
                delta = (new_base - old_base) / old_base * 100.0

            cell_delta = f"{pct(delta)} {arrow(delta if delta is not None else 0.0, m)}" if delta is not None else "-"
            row_cells.extend([old_disp, new_disp, cell_delta])

            # Regression detection
            if delta is not None:
                kind = metric_kind(m)
                if kind in ("time", "gc"):
                    if delta > warn_time:
                        regressions.append((k, m, delta))
                elif kind == "memory":
                    if delta > warn_alloc:
                        regressions.append((k, m, delta))
                elif kind == "throughput":
                    if delta < -warn_throughput:
                        regressions.append((k, m, delta))

            # CSV output row (long form)
            out_csv_rows.append({
                "key": k,
                "status": status,
                "metric": m,
                "old": old_disp,
                "new": new_disp,
                "delta_pct": f"{delta:.4f}" if delta is not None else "",
                "delta_sign": "pos" if (delta or 0) > 0 else ("neg" if (delta or 0) < 0 else "zero"),
                "better_direction": "down" if metric_kind(m) in ("time","memory","gc") else "up"
            })

        print_row(row_cells)

    # Summary
    if regressions:
        print("\nPotential regressions (thresholds applied):")
        for (k, m, d) in regressions:
            print(f" - {m} in {k}: {d:+.2f}%")
    else:
        print("\nNo regressions detected with current thresholds.")

    # Write optional CSV
    if out_path:
        with open(out_path, "w", newline="", encoding="utf-8") as f:
            w = csv.DictWriter(f, fieldnames=["key","status","metric","old","new","delta_pct","delta_sign","better_direction"])
            w.writeheader()
            for r in out_csv_rows:
                w.writerow(r)
        print(f"\nWrote detailed comparison to: {out_path}")

    # Exit status
    if regressions and fail_on_regression:
        sys.exit(2)

def main():
    ap = argparse.ArgumentParser(description="Compare two BenchmarkDotNet CSV exports.")
    ap.add_argument("old_csv", help="Baseline CSV (old)")
    ap.add_argument("new_csv", help="New CSV (to compare against baseline)")
    ap.add_argument("--key", help="Comma-separated list of columns to build the benchmark key (default: auto)", default=None)
    ap.add_argument("--metrics", help="Comma-separated list of metrics to compare (default: auto-pick common metrics)",
                    default=None)
    ap.add_argument("--time-unit", choices=["ns","us","ms","s"], default="ns", help="Display unit for time metrics (default: ns)")
    ap.add_argument("--mem-unit", choices=["B","KB","MB","GB"], default="B", help="Display unit for memory metrics (default: B)")
    ap.add_argument("--warn-time", type=float, default=5.0, help="Warn if time/gc increases by more than this percent (default: 5)")
    ap.add_argument("--warn-alloc", type=float, default=10.0, help="Warn if allocated bytes increase by more than this percent (default: 10)")
    ap.add_argument("--warn-throughput", type=float, default=5.0, help="Warn if throughput decreases by more than this percent (default: 5)")
    ap.add_argument("--fail-on-regression", action="store_true", help="Exit with code 2 if regressions detected")
    ap.add_argument("--out", help="Write a long-form comparison CSV to this path")
    args = ap.parse_args()

    old_rows, old_headers = read_csv(args.old_csv)
    new_rows, new_headers = read_csv(args.new_csv)

    user_keys = [c.strip() for c in args.key.split(",")] if args.key else None
    key_cols = pick_key_columns(old_headers, new_headers, user_keys)

    requested_metrics = [m.strip() for m in args.metrics.split(",")] if args.metrics else None
    metrics = pick_metrics(old_headers, new_headers, requested_metrics)

    if not key_cols:
        print("ERROR: Could not determine key columns. Please specify --key with column names present in both CSV files.", file=sys.stderr)
        sys.exit(1)
    if not metrics:
        print("ERROR: No comparable metrics found. Use --metrics to specify columns to compare.", file=sys.stderr)
        sys.exit(1)

    # Normalize mem unit
    mem_unit = args.mem_unit.upper()
    compare(old_rows, new_rows, key_cols, metrics, args.time_unit, mem_unit,
            args.warn_time, args.warn_alloc, args.warn_throughput, args.fail_on_regression, args.out)

if __name__ == "__main__":
    main()
