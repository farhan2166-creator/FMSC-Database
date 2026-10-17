"""Read-only source checks. Never print secret values or retain scanner reports."""
import argparse
import json
from pathlib import Path, PurePosixPath
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def forbidden_file(name):
    path = PurePosixPath(name.lower().replace("\\", "/"))
    if path.suffix in {".mdf", ".ldf", ".ndf", ".bak", ".pfx", ".p12", ".snk", ".publishsettings"}:
        return True
    if path.name.endswith(".pubxml.user"):
        return True
    if path.name in {"secrets.json", "credentials.json", "appsettings.production.json", "id_rsa", "id_ed25519", "id_dsa", "id_ecdsa"}:
        return True
    if path.name == ".env" or path.name.startswith(".env."):
        return not path.name.endswith((".example", ".sample", ".template"))
    return (path.name.startswith("appsettings.") and path.name.endswith(".local.json")) or str(path).endswith(".aws/credentials")


def git_output(repo, *args):
    return subprocess.check_output(["git", "-C", str(repo), *args], stderr=subprocess.PIPE)


def tracked_and_historical_paths(repo):
    current = git_output(repo, "ls-files", "-z")
    history = git_output(repo, "log", "--all", "--format=", "--name-only", "-z", "--diff-filter=ACMR")
    return {p.decode("utf-8", "replace").lstrip("\n") for p in (current + b"\0" + history).split(b"\0") if p.strip()}


def scan(repo, binary):
    blocked = sorted(p for p in tracked_and_historical_paths(repo) if forbidden_file(p))
    if blocked:
        print("ERROR: Sensitive files are tracked or occur in Git history. Keep database snapshots/backups and private configuration outside Git.")
        for path in blocked:
            print("  File: " + json.dumps(path))
        return 1
    scanner = shutil.which(binary)
    if scanner is None:
        print("ERROR: Gitleaks CLI was not found. Install the pinned CLI or supply --gitleaks PATH; no scan was performed.")
        return 2
    findings = set()
    with tempfile.TemporaryDirectory(prefix="fmsc-security-") as directory:
        report = Path(directory) / "findings.json"
        ignore = Path(directory) / "empty.ignore"
        ignore.write_text("")
        common = ["--config", str(ROOT / ".gitleaks.toml"), "--redact=100", "--no-banner", "--no-color",
                  "--log-level=error", "--exit-code=10", "--ignore-gitleaks-allow", "--gitleaks-ignore-path", str(ignore),
                  "--report-format=json", "--report-path", str(report)]
        # Include all fetched history and staged edits, including a secret added
        # then deleted. Inline allow comments/ignore files cannot bypass this check.
        for options in (["--log-opts=--all"], ["--pre-commit", "--staged"]):
            report.unlink(missing_ok=True)
            result = subprocess.run([scanner, "git", str(repo), *options, *common], capture_output=True)
            if result.returncode not in (0, 10):
                print("ERROR: Gitleaks could not complete. Check Git history/configuration or reinstall the pinned CLI. Scanner output is withheld to protect credentials.")
                return 2
            rows = json.loads(report.read_text()) if report.exists() else []
            if result.returncode == 10 and not rows:
                print("ERROR: Gitleaks reported a finding without a readable report; the check fails closed.")
                return 2
            for row in rows:
                findings.add((row["File"], row["StartLine"], row["RuleID"]))
    if findings:
        print("ERROR: Possible secrets found. Values and source excerpts are withheld. Remove them; revoke/rotate any real credential, including credentials later deleted from Git.")
        for path, line, rule in sorted(findings):
            print(json.dumps({"file": path, "line": line, "rule": rule}))
        return 1
    print("PASS: No detected secrets in fetched history/staged changes, or forbidden tracked/historical file names.")
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=ROOT)
    parser.add_argument("--gitleaks", default="gitleaks")
    args = parser.parse_args()
    try:
        return scan(args.repo.resolve(), args.gitleaks)
    except (OSError, subprocess.SubprocessError, ValueError, KeyError):
        print("ERROR: Security checks could not complete; no clean result is claimed. Check Git, scanner configuration and permissions. Raw output is withheld.")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
