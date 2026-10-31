# Targeted root-DACPAC history cleanup (operator-controlled)

**Status: preparation only. This PR does not remove anything from GitHub history.**

## Why this is needed

The FMSC Security Checks job rejected an older root `fmsc.dacpac`
(`DACPAC requires private review: fmsc.dacpac`). Replacing the file in
the latest commit does not remove the historical Git blobs. The current
root package has a schema-only declaration, but an earlier DACPAC
contained exported data according to the repository owner.

## Scope and invariants

- Rewrite **only** the exact, root-level Git path `fmsc.dacpac`.
- Preserve `Tests/LifecycleSqlChecks/Baseline/schema.dacpac`.
- Preserve all C#, SQL, migrations, workflow YAML, security rules and
  other file content, paths, and permissions.
- Do not claim commit SHAs stay the same. Descendant commit IDs must change
  when an old commit is changed. Keep empty commits rather than dropping
  their original messages unnecessarily.
- Do not suppress the scanner, delete all CI history, or make application
  changes as part of the security cleanup.

## Run locally — preview only

1. Temporarily set the **public** repository to private if it contains
   actual school/student data. Previous exposure cannot be undone by
   changing visibility or rewriting branches.
2. Coordinate with collaborators. Preserve a **secure, offline backup**
   and your new independently verified schema-only DACPAC **outside** the
   cleanup directory. Do not upload an old data-bearing archive as an
   Actions artifact.
3. Use the installed Git for Windows and Windows PowerShell. No Python,
   `git-filter-repo`, package manager or additional installation is required.
4. Run from a working directory with sufficient disk space:

   ```powershell
   .\Tools\PrepareDacpacHistoryCleanup.ps1 -OutputDirectory C:\Temp\fmsc-dacpac-review.git
   ```

5. Review `DACPAC_CLEANUP_REVIEW.txt`. The script makes an isolated
   `--mirror` clone, removes only `fmsc.dacpac` **in that local copy**,
   verifies every rewritten commit lacks that root path, and compares all other
   files in every old/new commit pair. Commit messages, author/committer identities,
   dates and merge-parent order are also verified. The original clone remains
   untouched. It **does not**
   push or delete GitHub content.

The script uses Git's bundled `filter-branch` with a fixed index filter for the
single ASCII root path. Git cautions against general-purpose use of this older
command. This procedure therefore runs only in a fresh isolated bare mirror,
refuses unexpected refs/tags/grafts, keeps empty commits, and checks every commit
before removing the mirror's temporary backup refs. A failed check stops the
preview; never publish a failed or partially verified mirror. It does not claim
that old, unreachable objects or external copies have been physically erased.

`DACPAC_COMMIT_MAP.txt` records old/new commit IDs. `DACPAC_AFFECTED_PULL_REFS.txt`
and the first-changed commits in the review report support a GitHub Support request.
Original commit signatures cannot remain valid after rewriting history.

For a local synthetic regression test, run
`powershell -NoProfile -ExecutionPolicy Bypass -File Tools/TestDacpacHistoryCleanup.ps1`.
This creates only disposable repositories under `artifacts`, including a merge,
a DACPAC-only commit and the separate test fixture. It does not contact GitHub.

The script deliberately fails if the branch set differs from the reviewed
`main` branch, or if tags appear. GitHub's branch and tag inventory was checked
again on 30 October 2026: only `main` exists and there are no tags. The old
operations and preparation branches are no longer present remotely; stale
remote-tracking branches in an existing clone do not describe the current
GitHub inventory. Investigate any new branch or tag rather than bypassing guards.

## Before any remote rewrite

- Restore the **fresh** root DACPAC in rewritten `main` as a *new*
  commit; never bring back the old blob. Check allowed ZIP entries,
  `ContainsExportedData=false`, SQL model checks, and Gitleaks.
- Validate the SQL contract, help/report checks, installer dependencies,
  and unchanged business rules using disposable data. A schema-only flag
  does not prove that arbitrary text in the SQL model is non-sensitive.
- Independently confirm every rewritten tip preserves unrelated Git blobs
  and that the intended replacement DACPAC is the **only** desired tip
  difference. Obtain explicit approval for the planned force push.
- Use Git's `--force-with-lease` tied to the **recorded old branch heads**,
  and coordinate an atomic update of all affected writable branch refs.
  Do **not** use a blind `--force` or `--mirror` push, and never silently
  discard a newer commit.
- Contact GitHub Support for affected read-only `refs/pull/*` references
  and cached views. Previously observed: **65 pull request head refs**.
  Inspect other refs, forks, clones, releases, Actions artifacts/logs,
  and any already-downloaded copies; rewriting branches alone is not
  guaranteed to eliminate exposure.
- Re-clone or carefully realign collaborator clones after the rewrite.
  Run the actual GitHub security workflow and relevant regressions.
  Record exact commands, commit SHAs, failures and results before
  claiming completion.

See GitHub's official guide:
https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository

## Why this cannot be completed by merging a pull request

GitHub pull requests add commits to a branch; they do not replace old
commits or garbage-collect historical blobs. A PR that merely deletes
`fmsc.dacpac` would remove a file needed by tests while leaving the
sensitive historical content available. This PR is therefore intentionally
limited to a reviewable cleanup procedure and a **local-only preview**.
