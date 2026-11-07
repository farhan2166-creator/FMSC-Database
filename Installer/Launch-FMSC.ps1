# Stable desktop entry point. This script and its helpers are installed in
# Administrators/SYSTEM-writable storage outside the replaceable app directory.
$ErrorActionPreference = 'Stop'
try {
    . ([IO.Path]::Combine($PSScriptRoot,'UpdateRecovery.ps1'))
    $root = Get-FmscRecoveryRoot
    if ([IO.Path]::GetFullPath($PSScriptRoot) -ine [IO.Path]::GetFullPath($root)) { throw 'Invalid FMSC launcher location.' }
    $record = Read-FmscRecoveryJournal $root
    $active = $null
    try {
        if ([Threading.Mutex]::TryOpenExisting('Global\FMSCInstallerActivation',[ref]$active)) {
            $held = $false
            try { $held = $active.WaitOne(0) } catch [Threading.AbandonedMutexException] { $held = $true }
            if (-not $held) { throw 'FMSC is being updated. Wait for installation to finish.' }
            $active.ReleaseMutex()
        }
    } finally { if ($active) { $active.Dispose() } }
    if ($record -and $record.Phase -notin @('Completed','Aborted')) {
        if ($record.UserSid -ne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value) {
            throw 'Sign in as the Windows user who started this update to recover it.'
        }
        $script = Join-Path $root 'Recovery.ps1'
        if ((Get-FileHash $script -Algorithm SHA256).Hash -ne $record.ScriptSha256 -or
            (Get-FileHash (Join-Path $root 'UpdateRecovery.ps1') -Algorithm SHA256).Hash -ne $record.HelperSha256) {
            throw 'Recovery files failed verification. Contact support; school data has not been restored or replaced.'
        }
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.FileName = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
        $start.Arguments = '-NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $script + '" -ResumeUpdate'
        $start.UseShellExecute = $true; $start.Verb = 'runas'
        $null = [Diagnostics.Process]::Start($start)
    } else {
        Start-Process -FilePath 'C:\FMSC School Management\FMSC_SMS.exe' -WorkingDirectory 'C:\FMSC School Management'
    }
} catch {
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.MessageBox]::Show($_.Exception.Message, 'FMSC - Update Recovery', 'OK', 'Warning') | Out-Null
}
