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
3. Install Git and `git-filter-repo`; example: `py -m pip install --upgrade git-filter-repo`.
4. Run from a working directory with sufficient disk space:

   ```powershell
   .\Tools\PrepareDacpacHistoryCleanup.ps1 -OutputDirectory C:\Temp\fmsc-dacpac-review.git
   ```

5. Review `DACPAC_CLEANUP_REVIEW.txt`. The script makes an isolated
   `--mirror` clone, removes only `fmsc.dacpac` **in that local copy**,
   verifies no reachable history retains that path, and compares the mode,
   blob ID and path of every other file on both branch tips. It **does not**
   push or delete GitHub content.

The script deliberately fails if the branch set differs from the observed
`main` and `ops/automatic-actions-run-retention-20261024`, or if tags
appear. Once this PR is merged, remove the cleanup PR's short-lived head
branch **after verifying it is no longer needed**, before attempting the
preview. Investigate any branch or tag changes rather than bypassing guards.

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
