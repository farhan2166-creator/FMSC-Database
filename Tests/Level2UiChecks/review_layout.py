"""Record an explicit visual review after inspecting this run's fictional images."""
import argparse
import json
from datetime import datetime, timezone
from pathlib import Path
from validate_report import validate, STATUSES


def review(report, available, result, notes):
    validate(report)
    images = {"L2-UX-05-1280x720.png", "L2-UX-05-maximized.png"}
    if not images.issubset(available) or result not in ("Pass", "Fail") or not notes.strip():
        raise ValueError("Both viewport captures, a review result and observed notes are required")
    row = next(r for r in report["scenarios"] if r["Id"] == "L2-UX-05")
    row.update(Status=result, Actual=notes.strip(), Evidence=", ".join(sorted(images)),
               Defect="" if result == "Pass" else "Visual review: see observed layout findings.",
               DatabaseVerification="Viewport capture checked owned catalog; visual review performs no SQL operations.")
    report["counts"] = {s: sum(r["Status"] == s for r in report["scenarios"]) for s in STATUSES}
    report["level3Ready"] = report["counts"]["Pass"] == report["total"] and report.get("cleanup", "").startswith("Pass:")
    report["layoutReviewUtc"] = datetime.now(timezone.utc).isoformat()
    validate(report)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--result", choices=("Pass", "Fail"), required=True)
    parser.add_argument("--notes", required=True)
    args = parser.parse_args()
    report = json.loads((args.directory / "report.json").read_text(encoding="utf-8-sig"))
    review(report, {p.name for p in args.directory.iterdir()}, args.result, args.notes)
    (args.directory / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    def cell(value):
        return str(value).replace("|", "/").replace("\r", " ").replace("\n", " ")
    fields = ("Id", "Module", "ScenarioName", "Expected", "Actual", "Status", "Evidence", "Defect", "DatabaseVerification")
    lines = ["# FMSC_SMS Level 2 desktop evidence", "", "Commit: " + report["sourceCommit"], "",
             "Total: " + str(report["total"]) + "; " + "; ".join(f"{s}: {n}" for s, n in report["counts"].items()), "",
             "| Test ID | Module | Scenario | Expected | Actual | Status | Evidence | Defect | Database verification |",
             "|---|---|---|---|---|---|---|---|---|"]
    lines.extend("| " + " | ".join(cell(row[f]) for f in fields) + " |" for row in report["scenarios"])
    lines += ["", "Cleanup: " + report.get("cleanup", "Pending"), "",
              "Readiness for Level 3: " + ("Ready" if report["level3Ready"] else "Not established"), "",
              "Layout review: " + args.notes]
    (args.directory / "summary.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
