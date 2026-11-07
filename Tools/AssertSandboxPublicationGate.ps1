# This gate runs only in trusted FMSC-Source workflows. Never use private
# Sandbox credentials here. It reads a least-privilege commit status written
# by Sandbox only after BOTH real SQL / Windows installer matrices succeed.
param([string]$SourceSha=$env:GITHUB_SHA)
$ErrorActionPreference='Stop'
if ($env:GITHUB_REPOSITORY -cne 'farhan2166-creator/FMSC-Source' -or
    $env:GITHUB_REF -cne 'refs/heads/main' -or
    $SourceSha -cnotmatch '^[a-f0-9]{40}$') {
    throw 'Sandbox publication attestation is only valid for an exact FMSC-Source main revision.'
}
if (-not $env:GITHUB_TOKEN) { throw 'Required GitHub commit-status read credential is missing.' }
$api='https://api.github.com/repos/farhan2166-creator/FMSC-Source'
$headers=@{
  Authorization="Bearer $env:GITHUB_TOKEN"
  Accept='application/vnd.github+json'
  'X-GitHub-Api-Version'='2022-11-28'
}
$main=Invoke-RestMethod -Uri "$api/branches/main" -Headers $headers -Method Get
if ($main.commit.sha -cne $SourceSha) {
    throw 'main moved since this run began. An attestation for a stale revision cannot publish.'
}
$contexts=@(Invoke-RestMethod -Uri "$api/commits/$SourceSha/statuses?per_page=100" -Headers $headers -Method Get)
$context='fmsc/sandbox-mdf-ldf/v1'
# GitHub returns newest first. Do not skip a newer failure to find an older
# successful status, even when it was posted by the legitimate attestor.
$latest=@($contexts | Where-Object { $_.context -ceq $context } | Select-Object -First 1)
if ($latest.Count -ne 1) { throw 'Missing private Sandbox real-installer acceptance attestation.' }
$record=$latest[0]
if ($record.state -cne 'success' -or
    $record.creator.login -cne 'farhan2166-creator' -or
    $record.description -cne 'MDF/LDF v3+v7 SQL+installer PASS (fixture 5a8c5130)' -or
    $record.target_url -cnotmatch '^https://github[.]com/farhan2166-creator/FMSC-Sandbox/actions/runs/[0-9]+$') {
    throw 'The latest Sandbox acceptance status was unsuccessful, unauthorized or invalid.'
}
Write-Host "Verified trusted Sandbox SQL + installer acceptance for exact Source SHA $SourceSha"
