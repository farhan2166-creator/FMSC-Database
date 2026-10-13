param([string]$ProjectRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
# Retired: the old source-freeze manifest is no longer part of this repository.
# Do not manufacture a replacement manifest or turn a missing baseline into a pass.
# The standard publish path already uses VerifySchemaArtifact.ps1.
throw 'VerifyBaseline.ps1 is retired; no source-freeze verification was performed. Use Installer/VerifySchemaArtifact.ps1 -ProjectDirectory <project-root> for approved DACPAC verification, and the checks in Validation/AuditFixChecks.md for the release candidate.'
