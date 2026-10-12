param([string]$ProjectRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$resolvedRoot=[IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')
$manifestPath=Join-Path $resolvedRoot 'Docs\FROZEN_BASELINE.json'
$manifest=Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$failures=[Collections.Generic.List[string]]::new()
foreach($file in @($manifest.SourceFiles)+@($manifest.DatabaseFiles)+@($manifest.ApplicationFiles)){
 $target=[IO.Path]::GetFullPath((Join-Path $resolvedRoot $file.Path))
 if(!$target.StartsWith($resolvedRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Baseline file escapes the project directory.'}
 if(!(Test-Path -LiteralPath $target -PathType Leaf)){$failures.Add("Missing: $($file.Path)");continue}
 if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.Sha256){$failures.Add("Changed: $($file.Path)")}
}
$expected=@($manifest.SourceFiles | ForEach-Object {$_.Path})
foreach($file in Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File -Force){
 $relative=$file.FullName.Substring($resolvedRoot.Length+1).Replace('\','/')
 if($relative -match '^(\.git|\.vs|bin|obj|Docs|Database)/' -or $relative -eq 'FMSC_SMS.csproj.user'){continue}
 if($relative -notin $expected){$failures.Add("Added since freeze: $relative")}
}
if($failures.Count){throw ("Frozen baseline changed:`r`n"+($failures -join "`r`n"))}
Write-Output "Frozen application $($manifest.ApplicationVersion) / database $($manifest.DatabaseVersion): all fingerprints match."
