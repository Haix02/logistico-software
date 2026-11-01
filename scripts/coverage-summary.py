#!/usr/bin/env python3
"""Merges the test runs' Cobertura reports and checks the server code's line coverage.

Usage: python3 scripts/coverage-summary.py <results-dir> [--min 85]
A line counts as covered when any test project ran it. Generated migrations, the browser client and build output are
left out. Prints a table per module (and to $GITHUB_STEP_SUMMARY in CI) and fails when coverage is below --min.
"""
import collections
import glob
import os
import sys
import xml.etree.ElementTree as ET

results = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith("--") else "TestResults"
minimum = float(sys.argv[sys.argv.index("--min") + 1]) if "--min" in sys.argv else 0.0

lines = collections.defaultdict(dict)
for report in glob.glob(os.path.join(results, "**", "coverage.cobertura.xml"), recursive=True):
    for cls in ET.parse(report).getroot().iter("class"):
        name = cls.get("filename").replace("\\", "/")
        if "/Migrations/" in name or "/obj/" in name or "Logistico.Web.Client" in name:
            continue
        for line in cls.iter("line"):
            number = int(line.get("number"))
            lines[name][number] = lines[name].get(number, False) or int(line.get("hits")) > 0

if not lines:
    sys.exit(f"No coverage reports under {results}")

def module(path):
    for marker in ("Modules/Shipments/Logistico.Modules.Shipments.Contracts", "Modules/Shipments/Logistico.Modules.Shipments",
                   "Modules/Merchants", "Modules/Audit", "Logistico.SharedKernel", "Logistico.Web"):
        if marker in path:
            return marker.split("/")[-1]
    return "other"

totals = collections.defaultdict(lambda: [0, 0])
for path, hits in lines.items():
    totals[module(path)][0] += sum(hits.values())
    totals[module(path)][1] += len(hits)
covered, total = sum(c for c, _ in totals.values()), sum(t for _, t in totals.values())
rate = 100 * covered / total

table = ["| Module | Lines covered | Coverage |", "|---|---:|---:|"]
table += [f"| {m} | {c:,} / {t:,} | {100 * c / t:.1f}% |" for m, (c, t) in sorted(totals.items())]
table.append(f"| **All server code** | **{covered:,} / {total:,}** | **{rate:.1f}%** |")
report = "\n".join(["### Test coverage", "", *table, "", f"Minimum: {minimum:.0f}%"])
print(report)
if os.environ.get("GITHUB_STEP_SUMMARY"):
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
        summary.write(report + "\n")
if rate < minimum:
    sys.exit(f"Coverage {rate:.1f}% is below the minimum of {minimum:.0f}%")
