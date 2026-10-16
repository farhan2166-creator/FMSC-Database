"""Produce an honest 45-scenario acceptance matrix from fresh, sanitized execution evidence."""
import argparse
from collections import Counter
import json
from pathlib import Path
import re

CATALOG = Path(__file__).with_name("catalog.json")
STATUSES = ("PASS", "FAIL", "BLOCKED", "NOT RUN", "LOCAL REQUIRED")
LOCAL = {"UI", "FILES", "MSI", "PRIVACY", "DPI", "RESOURCE", "REPORT", "PRINTER"}


def validate(report):
    rows = report["scenarios"]
    expected = {r[0] for r in json.loads(CATALOG.read_text())}
    if len(rows) != 45 or {r["id"] for r in rows} != expected:
        raise ValueError("Every planned scenario must appear exactly once.")
    counts = Counter(r["status"] for r in rows)
    if any(status not in STATUSES for status in counts) or report["counts"] != {s: counts[s] for s in STATUSES}:
        raise ValueError("Status totals disagree with the complete scenario matrix.")
    if report["ready"] and counts["PASS"] != 45:
        raise ValueError("Incomplete acceptance cannot be described as deployment ready.")
    for row in rows:
        if row["status"] == "PASS" and (row["localRequired"] or not row["evidence"] or not row["cloud"] or any(c["status"] != "PASS" for c in row["cloud"])):
            raise ValueError("PASS needs actual evidence for all mandatory parts.")
    for measurements in report["measurements"].values():
        if any(not re.fullmatch(r"[a-zA-Z][a-zA-Z0-9_]{0,79}", k) or type(v) not in (int, float) or not 0 <= v < 1e12 for k, v in measurements.items()):
            raise ValueError("Only bounded numeric measurements are permitted.")
    text = json.dumps(report)
    if re.search(r"(?i)Password=|SQLCMDPASSWORD|MSSQL_SA_PASSWORD|FMSC_AUDIT_CONNECTION|PasswordHash|PasswordSalt", text):
        raise ValueError("Sensitive content must not enter Level 3 evidence.")
    return report


def build(sql=None, install=None, windows=None, desktop=None, security=None):
    sources = {k: v for k, v in {"sql": sql, "install": install, "windows": windows, "desktop": desktop, "security": security}.items() if v}
    commits = {s.get("commit", s.get("sourceCommit", s.get("head_sha"))) for s in sources.values()}
    if None in commits or len(commits) > 1 or any(not re.fullmatch(r"[a-f0-9]{40}", c) for c in commits):
        raise ValueError("Evidence must identify one exact tested revision; do not combine unrelated runs.")
    commit = next(iter(commits), None)
    stages = {s["name"]: s for s in (sql or {}).get("scenarios", [])}
    rows = []
    install_checks = (install or {}).get("checksPassed", [])
    cleanup = stages.get("Cleanup", {}).get("result")
    for test_id, description, names, kind, expected in json.loads(CATALOG.read_text()):
        components = []
        for name in names:
            stage = stages.get(name)
            components.append({"name": name, "status": stage["result"] if stage else "NOT RUN", "assertions": stage.get("assertions", 0) if stage else 0,
                               "seconds": stage.get("seconds", 0) if stage else 0, "evidence": "sql/report.json" if stage else "",
                               "execution": "Real SQL / linked production code" if stage else "Not executed"})
        if kind in {"INSTALL", "MSI"}:
            prefix = "Ran actual core.ps1" if test_id == "L3-INS-01" else test_id
            matched = [c for c in install_checks if c.startswith(prefix)]
            passed = bool(matched) and (install or {}).get("cleanup") == "PASS"
            attempted = install is not None
            stage_failed = attempted and install.get("lastStage", "").startswith(test_id) and install.get("result") == "FAIL"
            components.append({"name": "Owned disposable Windows installer" if kind == "INSTALL" else "Missing source executable refusal; MSI mocks are supplementary",
                               "status": "PASS" if passed else "FAIL" if stage_failed else "BLOCKED" if attempted else "NOT RUN",
                               "assertions": len(matched), "seconds": 0, "evidence": "install/report.json" if attempted else "", "execution": "Actual installer / simulated activation fault" if attempted else "Not executed"})
        if kind in LOCAL:
            if desktop and desktop.get("status") == "completed":
                components.append({"name": "Fresh Windows control contracts (supplementary)", "status": "PASS" if desktop.get("conclusion") == "success" else "BLOCKED", "assertions": 0, "seconds": 0, "evidence": desktop["html_url"], "execution": "Noninteractive; not visual acceptance"})
            if kind in {"RESOURCE", "REPORT"} and windows:
                components.append({"name": "Bounded production dialog/export components", "status": windows["result"], "assertions": 0, "seconds": windows.get("measurements", {}).get("DialogCycleSeconds", 0), "evidence": "windows/report.json", "execution": "Actual Windows controls/PDF/CSV, no operator desktop"})
        if test_id == "L3-SEC-05" and security:
            components.append({"name": "Fresh security workflow", "status": "PASS" if security.get("conclusion") == "success" else "FAIL", "assertions": 0, "seconds": 0, "evidence": security["html_url"], "execution": "Gitleaks and sensitive-file checks"})
        outcomes = {c["status"] for c in components}
        status = "FAIL" if "FAIL" in outcomes else "LOCAL REQUIRED" if kind in LOCAL else "BLOCKED" if "BLOCKED" in outcomes or (names and cleanup != "PASS") else "NOT RUN" if not components or "NOT RUN" in outcomes else "PASS"
        actual = "; ".join(c["name"] + ": " + c["status"] for c in components) or "Cloud portion not executed."
        if kind in LOCAL:
            actual += "; mandatory local acceptance not executed."
        rows.append({"id": test_id, "description": description, "environment": "Disposable Windows runner" if kind in {"INSTALL", "MSI"} else "Linux owned SQL container" if names else "Windows desktop / noninteractive supplementary checks",
                     "expected": expected, "actual": actual, "status": status, "cloud": components, "localRequired": kind in LOCAL,
                     "evidence": sorted({c["evidence"] for c in components if c["evidence"]}), "defect": "Inspect failing component; no application defect is inferred solely from environment failure." if status == "FAIL" else ""})
    counts = Counter(r["status"] for r in rows)
    report = {"commit": commit, "workingTreeDirty": (sql or {}).get("workingTreeDirty", False), "runId": (sql or {}).get("runId"), "schemaVersion": (sql or {}).get("schemaVersion"),
              "engine": (sql or {}).get("engine"), "planned": 45, "counts": {s: counts[s] for s in STATUSES}, "ready": counts["PASS"] == 45,
              "sqlCleanup": cleanup or "NOT RUN", "installCleanup": (install or {}).get("cleanup", "NOT RUN"),
              "measurements": {"sql": (sql or {}).get("measurements", {}), "windows": (windows or {}).get("measurements", {})},
              "workflowRuns": {k: v["html_url"] for k, v in sources.items() if "html_url" in v}, "scenarios": rows,
              "limitations": ["Historical Level 2 results are not fresh Level 3 passes.", "One source stage may support multiple scenarios; assertion counts must not be summed as unique assertions.",
                              "Simulation covers controlled exceptions, session termination and a killed journal process, not physical power or hardware failure.",
                              "1000 students and bounded component cycles establish a repeatable cloud baseline, not production-scale or full-day performance certification.",
                              "No current exported DACPAC approval/verification is performed; only the separate data-free fixture and runtime compatibility checks are used."]}
    return validate(report)


def markdown(report):
    lines = ["# FMSC_SMS Level 3 scenario evidence", "", f"Tested revision: `{report['commit'] or 'not executed'}`", f"Uncommitted work present: {report['workingTreeDirty']}", "", " · ".join(f"{s}: {report['counts'][s]}" for s in STATUSES), "",
             "These are 45 composite acceptance scenarios. Supplementary cloud checks do not complete mandatory local assertions.", "",
             "| Test ID | Description | Environment | Expected | Actual | Status | Evidence | Defect |", "|---|---|---|---|---|---|---|---|"]
    escape = lambda value: str(value).replace("|", "/").replace("\n", " ")
    for row in report["scenarios"]:
        lines.append("| " + " | ".join(escape(value) for value in [row["id"], row["description"], row["environment"], row["expected"], row["actual"], row["status"], ", ".join(row["evidence"]), row["defect"]]) + " |")
    lines += ["", "## Measured workloads", "", "```json", json.dumps(report["measurements"], indent=2), "```", "", "## Limitations", ""]
    lines += ["- " + value for value in report["limitations"]]
    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for source in ("sql", "install", "windows", "desktop", "security"):
        parser.add_argument("--" + source, type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    report = build(**{key: json.loads(getattr(args, key).read_text(encoding="utf-8-sig")) if getattr(args, key) else None for key in ("sql", "install", "windows", "desktop", "security")})
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    (args.output / "summary.md").write_text(markdown(report))
    print(" · ".join(f"{s}: {report['counts'][s]}" for s in STATUSES))
    return 1 if report["counts"]["FAIL"] or (args.sql and report["sqlCleanup"] != "PASS") else 0


if __name__ == "__main__":
    raise SystemExit(main())
