"""Builds docs/requirement-test-matrix.md from a test run (.trx file).

Usage:  dotnet test --logger "trx;LogFileName=results.trx" --results-directory TestResults
        python tools/requirement_matrix.py TestResults/results.trx docs/requirement-test-matrix.md

Every test whose name contains a requirement ID (FR_INV_03, NFR_SEC_05 ...) is listed under
that requirement with its result from the run.
"""
import re
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict

trx, out = sys.argv[1], sys.argv[2]
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
root = ET.parse(trx).getroot()
by_req = defaultdict(list)
total = passed = 0
for r in root.iterfind(".//t:UnitTestResult", ns):
    name = r.get("testName")
    outcome = r.get("outcome")
    total += 1
    passed += outcome == "Passed"
    short = name.split("(")[0].split(".")[-1]
    for m in re.finditer(r"(N?FR)_([A-Z]+)_(\d+)", short):
        by_req[f"{m.group(1)}-{m.group(2)}-{m.group(3)}"].append((short, outcome))

def key(req):
    kind, area, num = req.split("-")
    return (kind, area, int(num))

lines = [
    "# Requirement-to-test table",
    "",
    f"Generated from a test run: **{passed} of {total} tests passed**. "
    "Regenerate with `tools/requirement_matrix.py` (see the file for the command).",
    "",
    "| Requirement | Tests | Result |",
    "|---|---|---|",
]
for req in sorted(by_req, key=key):
    tests = sorted(set(by_req[req]))
    names = "<br>".join(t for t, _ in tests)
    ok = all(o == "Passed" for _, o in tests)
    lines.append(f"| {req} | {names} | {'Pass' if ok else 'FAIL'} ({len(tests)}) |")
lines += ["", f"{len(by_req)} requirements are covered by named tests. Other tests (startup, layout, helpers) "
          "support these but are not tied to one requirement ID."]
open(out, "w", encoding="utf-8").write("\n".join(lines) + "\n")
print(f"{len(by_req)} requirements, {passed}/{total} passed")
