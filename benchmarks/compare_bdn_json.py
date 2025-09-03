# tools/compare_bdn.py
import json, sys
from collections import defaultdict

def load(path):
    with open(path) as f:
        return json.load(f)

def map_results(bdn_json):
    # Map: (typeName, method) -> { "mean": ns, "p95": ns, "alloc": bytes }
    m = {}
    for r in bdn_json["Benchmarks"]:
        k = (r["FullName"], r["MethodTitle"])
        stats = r["Statistics"]
        alloc = 0
        if r.get("Memory") and r["Memory"].get("AllocatedBytes"):
            alloc = r["Memory"]["AllocatedBytes"]
        m[k] = {
            "mean": stats["Mean"],
            "p95": stats.get("Percentiles", {}).get("P95", None),
            "alloc": alloc
        }
    return m

def pct(n): return f"{n:+.2f}%"

if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("Usage: python tools/compare_bdn.py <old.json> <new.json>")
        sys.exit(1)
    old = map_results(load(sys.argv[1]))
    new = map_results(load(sys.argv[2]))

    rows = []
    for k in sorted(set(old) | set(new)):
        o = old.get(k)
        n = new.get(k)
        name = f"{k[0]}::{k[1]}"
        if not o or not n:
            status = "added" if n and not o else "removed"
            rows.append((name, status, "", "", ""))
            continue
        mean_delta = (n["mean"] - o["mean"]) / o["mean"] * 100.0
        p95_delta = (n["p95"] - o["p95"]) / o["p95"] * 100.0 if (n["p95"] and o["p95"]) else None
        alloc_delta = (n["alloc"] - o["alloc"]) / o["alloc"] * 100.0 if (o["alloc"] > 0) else None
        rows.append((name, "changed", pct(mean_delta), pct(p95_delta) if p95_delta is not None else "-", pct(alloc_delta) if alloc_delta is not None else "-"))

    print(f"{'Benchmark':70}  {'Status':8}  {'Mean Δ':>8}  {'P95 Δ':>8}  {'Alloc Δ':>8}")
    print("-"*110)
    for r in rows:
        print(f"{r[0]:70}  {r[1]:8}  {r[2]:>8}  {r[3]:>8}  {r[4]:>8}")

