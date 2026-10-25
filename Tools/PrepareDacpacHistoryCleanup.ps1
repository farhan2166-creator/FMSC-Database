#Requires -Version 5.1
<#
.SYNOPSIS
    PREVIEW ONLY: remove the root fmsc.dacpac from a separate mirror clone.
.DESCRIPTION
    Does not push, delete remote refs, or modify an existing local checkout.
    Excludes only the exact Git path fmsc.dacpac. Preserves empty commits
    to avoid unnecessarily dropping the surrounding development record.
    All changed commit IDs and GitHub PR caches must be handled separately.
.EXAMPLE
    .\Tools\PrepareDacpacHistoryCleanup.ps1 -OutputDirectory C:\Temp\fmsc-dacpac-review.git
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = 'https://github.com/farhan2166-creator/FMSC-Database.git'
$pathToRemove = 'fmsc.dacpac'
$expectedBranches = @(
    'refs/heads/main',
    'refs/heads/ops/automatic-actions-run-retention-20261024'
)

$destination = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $destination) {
    throw "Output path already exists; will not overwrite: $destination"
}
& git --version
if ($LASTEXITCODE -ne 0) { throw 'Git is not available.' }
& git filter-repo --version
if ($LASTEXITCODE -ne 0) { throw 'git-filter-repo is required.' }

# GitHub is never modified by this script.
& git clone --mirror $repository $destination
if ($LASTEXITCODE -ne 0) { throw 'Mirror clone failed. Nothing was pushed.' }

Push-Location -LiteralPath $destination
try {
    $branches = @(& git for-each-ref --format='%(refname)' refs/heads)
    if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate branches.' }
    $difference = @(Compare-Object -ReferenceObject $expectedBranches -DifferenceObject $branches)
    if ($difference.Count -ne 0) {
        throw 'Unexpected branch inventory. Review branches before cleanup; no remote changes.'
    }
    $tags = @(& git for-each-ref --format='%(refname)' refs/tags)
    if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate tags.' }
    if ($tags.Count -ne 0) {
        throw 'Tags were found and need separate review before rewriting; no remote changes.'
    }

    $originalTips = @{}
    $otherFiles = @{}
    foreach ($ref in $expectedBranches) {
        $originalTips[$ref] = (& git rev-parse $ref).Trim()
        if ($LASTEXITCODE -ne 0) { throw "Cannot resolve $ref" }
        # ls-tree includes the mode and blob SHA, so this detects any changes
        # to other tracked file contents, names, and file modes at branch tips.
        $otherFiles[$ref] = @(
            & git ls-tree -r --full-tree $ref |
                Where-Object { $_ -cnotmatch '\tfmsc\.dacpac$' } |
                Sort-Object
        )
        if ($LASTEXITCODE -ne 0) { throw "Cannot list $ref" }
    }

    & git filter-repo --sensitive-data-removal --invert-paths --path $pathToRemove --prune-empty never --prune-degenerate never
    if ($LASTEXITCODE -ne 0) { throw 'Local rewrite failed; nothing was pushed.' }

    $remaining = @(& git log --all --format='%H' -- $pathToRemove)
    if ($LASTEXITCODE -ne 0 -or $remaining.Count -ne 0) {
        throw 'FAIL: historical root DACPAC is still reachable; nothing was pushed.'
    }
    foreach ($ref in $expectedBranches) {
        $newFiles = @(
            & git ls-tree -r --full-tree $ref |
                Where-Object { $_ -cnotmatch '\tfmsc\.dacpac$' } |
                Sort-Object
        )
        if ($LASTEXITCODE -ne 0) { throw "Cannot inspect rewritten $ref" }
        if (@(Compare-Object -ReferenceObject $otherFiles[$ref] -DifferenceObject $newFiles -CaseSensitive).Count -ne 0) {
            throw "FAIL: unrelated tracked file changed on $ref; nothing was pushed."
        }
    }

    $report = @(
        'LOCAL REVIEW ONLY — NO REMOTE CHANGES'
        'Repository: ' + $repository
        'Removed historical path: ' + $pathToRemove
        ''
        'Verified: the root DACPAC does not exist in reachable local Git history.'
        'Verified: other tracked file paths/modes/blob IDs match at both branch tips.'
        'NOT verified: privacy clearance of earlier releases, logs, PRs, forks or clones.'
        'NOT verified: fresh replacement DACPAC or application regression tests.'
        ''
        'IMPORTANT: The root fmsc.dacpac is also absent from rewritten main.'
        'DO NOT push until a fresh, validated schema-only file is restored.'
        'DO NOT push until you have backup, PR-ref cleanup and collaborator plan.'
        'Never blindly push --mirror; GitHub pull request refs are server managed.'
        ''
        'Original branch tips (needed for force-with-lease checks):'
    )
    foreach ($ref in $expectedBranches) {
        $report += ($ref + '  ' + $originalTips[$ref])
    }
    $reportPath = Join-Path $destination 'DACPAC_CLEANUP_REVIEW.txt'
    $report | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Host "PASS — local DACPAC history filtering and tip-file verification."
    Write-Host "Review: $reportPath"
    Write-Host "No remote GitHub refs were changed."
}
finally {
    Pop-Location
}
