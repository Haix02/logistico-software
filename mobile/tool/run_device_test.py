#!/usr/bin/env python3
"""Runs the integration test on an iOS simulator and takes a screenshot at each SHOT:<name> marker.
Usage (from mobile/): python3 tool/run_device_test.py <simulator-udid> <out-dir> [SERVER=http://localhost:5311]"""
import subprocess
import sys
from pathlib import Path

udid, out = sys.argv[1], Path(sys.argv[2])
server = sys.argv[3] if len(sys.argv) > 3 else "http://localhost:5311"
out.mkdir(parents=True, exist_ok=True)
proc = subprocess.Popen(
    ["flutter", "test", "integration_test/courier_flow_test.dart", "-d", udid, f"--dart-define=SERVER={server}"],
    stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
for line in proc.stdout:
    print(line, end="")
    if "SHOT:" in line:
        name = line.split("SHOT:")[1].strip().split()[0]
        subprocess.run(["xcrun", "simctl", "io", udid, "screenshot", str(out / f"{name}.png")], capture_output=True)
sys.exit(proc.wait())
