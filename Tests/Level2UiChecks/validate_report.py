"""Validate the public evidence envelope; private state/raw logs are never artifacts."""
import json
from pathlib import Path

STATUSES = ("Pass", "Fail", "Blocked", "Not Run")
FIELDS = {"Id", "Module", "ScenarioName", "Expected", "Actual", "Status", "Evidence", "Defect", "DatabaseVerification"}


def validate(report):
    rows = report["scenarios"]
    if not rows or report["total"] != len(rows):
        raise ValueError("Scenario count differs")
    if len({r["Id"] for r in rows}) != len(rows):
        raise ValueError("Duplicate test IDs")
    counts = {status: sum(r["Status"] == status for r in rows) for status in STATUSES}
    if report["counts"] != counts or sum(counts.values()) != len(rows):
        raise ValueError("Status totals differ")
    if report["level3Ready"] != (counts["Pass"] == len(rows) and report.get("cleanup", "Pending").startswith("Pass:")):
        raise ValueError("Readiness cannot ignore unexecuted/failing scenarios")
    for row in rows:
        if set(row) != FIELDS or not row["Id"].startswith("L2-"):
            raise ValueError("Unexpected scenario payload")
        if row["Status"] == "Pass" and (
                not row["Actual"] or row["Actual"].startswith("Not executed") or
                row["DatabaseVerification"] == "Not executed." or not row["Evidence"]):
            raise ValueError("Pass requires observed outcome, persistence result and evidence")
        if row["Evidence"] and (".." in row["Evidence"] or ":" in row["Evidence"] or "\\" in row["Evidence"] or "/" in row["Evidence"]):
            raise ValueError("Evidence references must stay inside the public evidence directory")
    text = json.dumps(report).lower()
    if any(token in text for token in ("password=", "passwordhash", "passwordsalt", "credentials.json", "connectionstring")):
        raise ValueError("Private state in public evidence")


if __name__ == "__main__":
    import sys
    root = Path(sys.argv[1])
    report = json.loads((root / "report.json").read_text(encoding="utf-8-sig"))
    validate(report)
    for row in report["scenarios"]:
        if row["Status"] == "Pass" and any(not (root / ref.strip()).is_file() for ref in row["Evidence"].split(",")):
            raise ValueError("Pass refers to missing evidence")
    for path in root.iterdir():
        if path.name not in {"report.json", "summary.md", "environment.json"} and not (
                path.name.startswith(("L2-", "startup-")) and path.suffix in {".png", ".txt"}):
            raise ValueError("Unexpected artifact file")
    print("PASS: bounded public Level 2 evidence; result counts agree.")
