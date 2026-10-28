"""Check retained requirement IDs and the owner's explicit readiness scale."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[1]
baseline = json.loads((root / "docs/requirements-baseline.json").read_text(encoding="utf-8"))["original_requirement_ids"]
if len(baseline) != 184 or len(set(baseline)) != 184:
    raise ValueError("The original requirement baseline must contain 184 unique IDs")
trace = (root / "REQUIREMENTS_TRACEABILITY.md").read_text(encoding="utf-8")
rows = [line.split("|") for line in trace.splitlines() if re.match(r"^\| [A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+\b", line)]
ids = [row[1].strip().split()[0] for row in rows]
if len(ids) != len(set(ids)) or set(baseline) - set(ids):
    raise ValueError("A requirement was removed or duplicated")
statuses = {"Implemented & Verified", "Implemented, Not Verified", "Partially Implemented", "Not Implemented", "Blocked by External Requirements", "DEFERRED"}
for row in rows:
    if len(row) < 10 or row[4].strip() not in statuses:
        raise ValueError("Invalid requirement row: " + row[1].strip().split()[0])
deferred = {"COMPAT-SINYTRA-01", "COMPAT-SINYTRA-02", "COMPAT-SINYTRA-03", "COMPAT-KILT-01", "COMPAT-CROSSLOADER-01"}
for required in deferred:
    if not any(row[1].strip().split()[0] == required and row[4].strip() == "DEFERRED" for row in rows):
        raise ValueError("Missing explicit deferred requirement: " + required)
states = {"VERIFIED", "PARTIALLY VERIFIED", "IMPLEMENTED BUT UNVERIFIED", "NOT IMPLEMENTED", "BLOCKED EXTERNALLY", "DEFERRED"}
grades = {"0%", "25%", "50%", "75%", "100%"}
priorities = {"P0", "P1", "P2", "Foundation", "Release", "Deferred"}
summary = (root / "PROJECT_PROGRESS_SUMMARY.md").read_text(encoding="utf-8")
modules = []
for line in summary.splitlines():
    if not line.startswith("| "):
        continue
    cols = [col.strip() for col in line.split("|")]
    if len(cols) < 8 or cols[2] not in priorities:
        continue
    if cols[3] not in states or cols[4] not in grades:
        raise ValueError("Invalid module state/grade")
    if cols[4] == "100%" and cols[3] != "VERIFIED":
        raise ValueError("Only a verified module may be marked 100%")
    modules.append(cols[1])
if not modules or len(modules) != len(set(modules)):
    raise ValueError("Missing or duplicate progress modules")
print(f"Requirements PASS: {len(baseline)} original IDs retained; {len(ids)} current IDs; {len(modules)} graded modules")
