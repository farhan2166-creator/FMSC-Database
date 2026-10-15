"""Trusted default-branch publisher. Never execute PR code with write credentials."""
import argparse
import base64
import json
import os
from pathlib import Path
import re
import subprocess
import urllib.error
import urllib.request

from refresh_dacpac import ROOT, SYNC_PATHS, Refusal, git, verify, select_approval_anchor

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
            if ex.code == 404 and method == "GET" and any(prefix in path for prefix in ("/git/ref/heads/automation/dacpac-refresh-", "/git/ref/heads/automation/schema-upgrade-")): return None
            # Never print response bodies, credentials, payloads or private source.
            raise Refusal("GITHUB_HTTP_" + str(ex.code)) from None


def publish(project, anchor, source, repository, actor, event, ref, api):
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository) or not re.fullmatch(r"[A-Za-z0-9_\[\]-]{1,100}", actor):
        raise Refusal("INVALID_GITHUB_IDENTITY")
    if not re.fullmatch(r"[a-f0-9]{40}", source) or not re.fullmatch(r"[a-f0-9]{40}", anchor):
        raise Refusal("FULL_SOURCE_AND_APPROVAL_COMMITS_REQUIRED")
    git(project, "merge-base", "--is-ancestor", anchor, source)
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
    from schema_upgrade import baseline
    baseline(project, anchor)
    report, updates = verify(project, anchor)
    if report["result"] == "REVIEW_REQUIRED": raise Refusal("REFRESH_REQUIRES_REVIEW")
    tracked = git(project, "ls-tree", source, "--", "FMSC.dacpac").decode().split()
    if len(tracked) < 3 or tracked[0] != "100644" or tracked[2] != report["sourceGitBlob"]:
        raise Refusal("CHECKOUT_CANDIDATE_DIFFERS_FROM_TRUSTED_COMMIT")
    if not updates: return {"result": "ALIGNED", "pullRequest": None}
    if not set(updates).issubset(SYNC_PATHS): raise Refusal("UNEXPECTED_PR_PATH")
    branch = "automation/dacpac-refresh-" + source[:12]
    pr = create_review_pr(api, repository, source, branch, updates, "Synchronize verified DACPAC metadata refresh",
        "Verified metadata-only refresh. Exact model/module/exported payload matching passed; review the dispatched Desktop, Security and isolated SQL checks before merging. No automatic merge. Source: `" + source + "`.")
    # Explicit dispatch is essential for GITHUB_TOKEN-created PRs. It is limited
    # to the existing reviewed workflows and this generated alignment branch.
    for workflow in WORKFLOWS:
        api("POST", base + "/actions/workflows/" + workflow + "/dispatches", {"ref": branch})
    return {"result": "PR_PREPARED", "pullRequest": pr["html_url"], "branch": branch, "dispatchedWorkflows": list(WORKFLOWS)}


def create_review_pr(api, repository, source, branch, updates, title, body, draft=False):
    """Publish a single exact-content proposal; never overwrite an existing branch."""
    base = "/repos/" + repository
    default = api("GET", base)["default_branch"]
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
        new = api("POST", base + "/git/commits", {"message": title, "parents": [source], "tree": tree["sha"]})
        api("POST", base + "/git/refs", {"ref": "refs/heads/" + branch, "sha": new["sha"]})
    if api("GET", base + "/git/ref/heads/" + default)["object"]["sha"] != source:
        raise Refusal("DEFAULT_BRANCH_MOVED_BEFORE_PR")
    owner = repository.split("/")[0]
    matches = api("GET", base + "/pulls?state=open&base=" + default + "&head=" + owner + ":" + branch)
    return matches[0] if matches else api("POST", base + "/pulls", {"base": default, "head": branch, "title": title, "body": body, "draft": draft})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, default=ROOT)
    parser.add_argument("--approval-ref", help="Explicit reviewed anchor for local maintainers; Actions always computes its own pre-refresh anchor.")
    args = parser.parse_args()
    try:
        source = os.environ["GITHUB_SHA"]
        anchor = args.approval_ref or select_approval_anchor(args.project, source, os.environ["GITHUB_EVENT_NAME"], os.environ.get("GITHUB_EVENT_BEFORE", ""))
        result = publish(args.project, anchor, source, os.environ["GITHUB_REPOSITORY"],
                         os.environ["GITHUB_TRIGGERING_ACTOR"], os.environ["GITHUB_EVENT_NAME"], os.environ["GITHUB_REF"], GitHub(os.environ["GH_TOKEN"]))
    except (Refusal, KeyError, OSError, ValueError, subprocess.SubprocessError) as ex:
        code = str(ex) if isinstance(ex, Refusal) else "PUBLISHER_CONFIGURATION_OR_CONNECTION_FAILED"
        print("DACPAC PR publication refused: " + code)
        print("For HTTP 403, check repository Settings > Actions > General > Allow GitHub Actions to create and approve pull requests. No token or visibility change is automatic.")
        return 1
    print(json.dumps(result))
    return 0


if __name__ == "__main__": raise SystemExit(main())
