param([string]$LauncherPath = (Join-Path $PSScriptRoot '../obj/FmscLauncher/Release/FMSC.Launcher.exe'))
$ErrorActionPreference = 'Stop'
$bytes = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($LauncherPath))
$pe = [BitConverter]::ToInt32($bytes,0x3c)
if ([BitConverter]::ToUInt16($bytes,$pe+24+68) -ne 2) { throw 'Launcher must use the Windows GUI subsystem, not a console subsystem.' }
$root = Join-Path ([IO.Path]::GetTempPath()) ('fmsc-windowless-' + [Guid]::NewGuid().ToString('N'))
$directory = Join-Path $root 'launcher with spaces'
$process = $null
try {
    $null = New-Item -ItemType Directory -Path $directory
    $exe = Join-Path $directory 'FMSC.Launcher.exe'
    Copy-Item -LiteralPath $LauncherPath -Destination $exe
    # Inert replacement for the recovery script: observe only the child console.
    # No installed app, credentials, recovery storage or SQL is accessed.
    @'
$ErrorActionPreference='Stop'
$env:PSModulePath=[IO.Path]::Combine($PSHOME,'Modules')
try {
    Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class ConsoleProbe { [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow(); }'
    [IO.File]::WriteAllText([IO.Path]::Combine($PSScriptRoot,'console.txt'),[ConsoleProbe]::GetConsoleWindow().ToInt64().ToString())
} catch { [IO.File]::WriteAllText([IO.Path]::Combine($PSScriptRoot,'error.txt'),$_.Exception.Message) }
'@ | Set-Content -LiteralPath (Join-Path $directory 'Launch-FMSC.ps1') -Encoding UTF8
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $exe; $start.UseShellExecute = $false
    $process = [Diagnostics.Process]::Start($start)
    if (-not $process.WaitForExit(10000)) { $process.Kill(); throw 'GUI bootstrap did not exit promptly.' }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $marker = Join-Path $directory 'console.txt'; $failure = Join-Path $directory 'error.txt'
    while (-not (Test-Path -LiteralPath $marker)) {
        if (Test-Path -LiteralPath $failure) { throw (Get-Content -LiteralPath $failure -Raw) }
        if ($watch.Elapsed.TotalSeconds -gt 20) { throw 'Windowless child did not report its console state.' }
        Start-Sleep -Milliseconds 50
    }
    if ((Get-Content -LiteralPath $marker -Raw).Trim() -ne '0') { throw 'PowerShell allocated a console window.' }
    Write-Output 'PASS: GUI bootstrap exits promptly; real Windows PowerShell child has no console window, including a launcher path with spaces. No installed FMSC or SQL launched.'
} finally {
    if ($process) { if (-not $process.HasExited) { $process.Kill() }; $process.Dispose() }
    $absolute = [IO.Path]::GetFullPath($root)
    if (-not $absolute.StartsWith([IO.Path]::GetTempPath(),[StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $absolute) -notmatch '^fmsc-windowless-[a-f0-9]{32}$') { throw 'Unsafe launcher fixture cleanup.' }
    Remove-Item -LiteralPath $absolute -Recurse -Force -ErrorAction SilentlyContinue
}
