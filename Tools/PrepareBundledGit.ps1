# Pinned MinGit for app distribution. No network installation on school PCs.
param([Parameter(Mandatory=$true)][string]$DestinationDirectory)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$version='2.56.0.4'
$tag='v2.56.0.windows.4'
$name="MinGit-$version-64-bit.zip"
$expected='E3BF78351FE777FBA47DDEE5950082F8A98BB0F02604FDC7BA3EE2CB68FFA877'
$url="https://github.com/git-for-windows/git/releases/download/$tag/$name"
$DestinationDirectory=[IO.Path]::GetFullPath($DestinationDirectory)
if (Test-Path -LiteralPath $DestinationDirectory) {
    throw "Refusing to overwrite existing transport output: $DestinationDirectory"
}
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('fmsc-mingit-'+[Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $temporary -Force | Out-Null
    $zip=Join-Path $temporary $name
    Invoke-WebRequest -Uri $url -OutFile $zip -MaximumRedirection 5
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $expected) {
        throw 'Pinned MinGit SHA-256 mismatch; refusing to publish.'
    }
    Expand-Archive -LiteralPath $zip -DestinationPath $DestinationDirectory
    $git=Join-Path $DestinationDirectory 'cmd/git.exe'
    $ssh=Join-Path $DestinationDirectory 'usr/bin/ssh.exe'
    foreach ($file in @($git,$ssh)) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or
            (Get-Item -LiteralPath $file).Length -le 0) {
            throw "Verified MinGit archive missing component: $file"
        }
    }
    # Include Git's redistribution license, not merely a web reference.
    $copying=Join-Path $DestinationDirectory 'COPYING-GIT.txt'
    Invoke-WebRequest -Uri "https://raw.githubusercontent.com/git-for-windows/git/$tag/COPYING" -OutFile $copying -MaximumRedirection 5
    if ((Get-Item -LiteralPath $copying).Length -lt 5000 -or
        -not (Select-String -LiteralPath $copying -Pattern 'GNU GENERAL PUBLIC LICENSE' -Quiet)) {
        throw 'Required Git redistribution license could not be verified.'
    }
    & $git --version
    if ($LASTEXITCODE -ne 0) { throw 'Bundled Git could not run.' }
    & $ssh -V 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Bundled SSH could not run.' }
    @(
        'FMSC bundled update transport: Git for Windows MinGit 2.56.0(4) x64'
        'Original archive is redistributed without modifying its components.'
        "Distribution: $url"
        "Archive SHA-256: $expected"
        'Source: https://github.com/git-for-windows/git/tree/v2.56.0.windows.4'
        'Bundled license text: COPYING-GIT.txt (GPLv2)'
        'Upstream license: https://github.com/git-for-windows/git/blob/v2.56.0.windows.4/COPYING'
        'OpenSSH and other included components retain their respective licenses.'
        'See original sources for the corresponding component license notices.'
    ) | Set-Content -LiteralPath (Join-Path $DestinationDirectory 'THIRD_PARTY_NOTICES.txt') -Encoding UTF8
    Write-Host "Pinned MinGit $version including SSH verified."
} catch {
    if (Test-Path -LiteralPath $DestinationDirectory) {
        Remove-Item -LiteralPath $DestinationDirectory -Recurse -Force
    }
    throw
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
