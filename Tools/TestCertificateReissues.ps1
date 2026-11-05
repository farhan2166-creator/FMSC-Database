# Current certificate behavior and recovery checks on a fresh, owned baseline-1 fixture.
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'TestAutomaticBilling.ps1') -CertificateOnly
