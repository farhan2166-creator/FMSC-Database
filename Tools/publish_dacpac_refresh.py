"""Trusted default-branch publisher. Never execute PR code with write credentials."""
import argparse
import base64
import json
import os
from pathlib import Path
import re
import urllib.error
import urllib.request

from refresh_dacpac import ROOT, SYNC_PATHS, Refusal, git, verify

WORKFLOWS = ("fmsc-desktop-ci.yml", "fmsc-security.yml", "fmsc-lifecycle-sql-ci.yml")


class GitHub:
    def __init__(self, token): self.token = token
    def __call__(self, method, path, data=None):
        request = urllib.request.Request("https://api.github.com" + path,
            data=json.dumps(data).encode() if data is not None else None, method=method,
            headers={"Authorization": "Bearer " + self.token, "Accept": "application/vnd.github+json",
                     "X-GitHub-Api-Version": "2022-11-28", "Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                body = response.read(); return json.loads(body) if body else {}
        except urllib.error.HTTPError as ex:
            if ex.code == 404 and method == "GET" and "/git/ref/heads/automation/dacpac-refresh-" in path: return None
            # Never print response bodies, credentials, payloads or private source.
            raise Refusal("GITHUB_HTTP_" + str(ex.code)) from None


def publish(project, anchor, source, repository, actor, event, ref, api):
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository) or not re.fullmatch(r"[A-Za-z0-9_\[\]-]{1,100}", actor):
        raise Refusal("INVALID_GITHUB_IDENTITY")
    if not re.fullmatch(r"[a-f0-9]{40}", source) or source != anchor:
        raise Refusal("DEFAULT_BRANCH_APPROVAL_ANCHOR_REQUIRED")
    base = "/repos/" + repository
    info = api("GET", base)
    default = info["default_branch"]
    if not info.get("private") or event not in ("push", "workflow_dispatch") or ref != "refs/heads/" + default:
        raise Refusal("UNTRUSTED_EVENT_OR_REF")
    permission = api("GET", base + "/collaborators/" + actor + "/permission")["permission"]
    if permission not in ("admin", "maintain", "write"):
        raise Refusal("TRUSTED_MAINTAINER_REQUIRED")
    if git(project, "rev-parse", "HEAD").decode().strip() != source:
        raise Refusal("CHECKOUT_SOURCE_DIFFERS")
    current = api("GET", base + "/git/ref/heads/" + default)["object"]["sha"]
    if current != source: raise Refusal("DEFAULT_BRANCH_MOVED_RETRY_ON_LATEST")
    report, updates = verify(project, anchor)
    if report["result"] == "REVIEW_REQUIRED": raise Refusal("REFRESH_REQUIRES_REVIEW")
    tracked = git(project, "ls-tree", source, "--", "FMSC.dacpac").decode().split()
    if len(tracked) < 3 or tracked[0] != "100644" or tracked[2] != report["sourceGitBlob"]:
        raise Refusal("CHECKOUT_CANDIDATE_DIFFERS_FROM_TRUSTED_COMMIT")
    if not updates: return {"result": "ALIGNED", "pullRequest": None}
    if not set(updates).issubset(SYNC_PATHS): raise Refusal("UNEXPECTED_PR_PATH")
    branch = "automation/dacpac-refresh-" + source[:12]
    existing = api("GET", base + "/git/ref/heads/" + branch)
    if existing is not None:
        commit_sha = existing["object"]["sha"]
        compare = api("GET", base + "/compare/" + source + "..." + commit_sha)
        if compare.get("ahead_by") != 1 or compare.get("behind_by") != 0 or {f["filename"] for f in compare["files"]} != set(updates):
            raise Refusal("EXISTING_BRANCH_IS_NOT_OWNED_ALIGNMENT")
        for name, payload in updates.items():
            blob = api("GET", base + "/contents/" + name + "?ref=" + branch)
            if base64.b64decode(blob["content"]) != payload: raise Refusal("EXISTING_ALIGNMENT_PAYLOAD_DIFFERS")
    else:
        commit = api("GET", base + "/git/commits/" + source)
        entries = []
        for name, payload in sorted(updates.items()):
            blob = api("POST", base + "/git/blobs", {"encoding": "base64", "content": base64.b64encode(payload).decode()})
            entries.append({"path": name, "mode": "100644", "type": "blob", "sha": blob["sha"]})
        tree = api("POST", base + "/git/trees", {"base_tree": commit["tree"]["sha"], "tree": entries})
        new = api("POST", base + "/git/commits", {"message": "Synchronize verified metadata-only DACPAC refresh", "parents": [source], "tree": tree["sha"]})
        api("POST", base + "/git/refs", {"ref": "refs/heads/" + branch, "sha": new["sha"]})
    if api("GET", base + "/git/ref/heads/" + default)["object"]["sha"] != source:
        raise Refusal("DEFAULT_BRANCH_MOVED_BEFORE_PR")
    owner = repository.split("/")[0]
    matches = api("GET", base + "/pulls?state=open&base=" + default + "&head=" + owner + ":" + branch)
    if matches:
        pr = matches[0]
    else:
        body = ("Verified metadata-only DACPAC refresh. Exact full model, all " + str(report["moduleCount"]) +
                " SQL fingerprints, exported data, structural/whole-rupee/recovery contracts and required schema version are unchanged. "
                "Only approved artifact/provenance records and, when necessary, the data-free schema fixture are synchronized.\n\n"
                "Source commit: `" + source + "`\nPrevious package: `" + report["previousPackageSha256"] + "`\nCandidate package: `" + report["candidatePackageSha256"] +
                "`\nSchema-only fixture regenerated: " + str(report["fixtureRegenerated"]) +
                "\n\nDesktop, security and isolated Level 1 workflows are dispatched on this branch because GITHUB_TOKEN-created PRs do not trigger ordinary PR workflows. "
                "Review the completed runs before merging. No automatic merge, schema/data update or installer change is authorized.")
        pr = api("POST", base + "/pulls", {"base": default, "head": branch, "title": "Synchronize verified DACPAC metadata refresh", "body": body})
    # Explicit dispatch is essential for GITHUB_TOKEN-created PRs. It is limited
    # to the existing reviewed workflows and this generated alignment branch.
    for workflow in WORKFLOWS:
        api("POST", base + "/actions/workflows/" + workflow + "/dispatches", {"ref": branch})
    return {"result": "PR_PREPARED", "pullRequest": pr["html_url"], "branch": branch, "dispatchedWorkflows": list(WORKFLOWS)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, default=ROOT)
    parser.add_argument("--approval-ref", required=True)
    args = parser.parse_args()
    try:
        result = publish(args.project, args.approval_ref, os.environ["GITHUB_SHA"], os.environ["GITHUB_REPOSITORY"],
                         os.environ["GITHUB_TRIGGERING_ACTOR"], os.environ["GITHUB_EVENT_NAME"], os.environ["GITHUB_REF"], GitHub(os.environ["GH_TOKEN"]))
    except (Refusal, KeyError, OSError, ValueError) as ex:
        code = str(ex) if isinstance(ex, Refusal) else "PUBLISHER_CONFIGURATION_OR_CONNECTION_FAILED"
        print("DACPAC PR publication refused: " + code)
        print("For HTTP 403, check repository Settings > Actions > General > Allow GitHub Actions to create and approve pull requests. No token or visibility change is automatic.")
        return 1
    print(json.dumps(result))
    return 0


if __name__ == "__main__": raise SystemExit(main())
