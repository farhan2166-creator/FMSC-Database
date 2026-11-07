# Read-only compatibility probe. No school database, schema or recovery files are changed.
param([Parameter(Mandatory=$true)][string]$SqlCmdPath,
    [string]$SqlInstance='(localdb)\MSSQLLocalDB')
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('fmsc-sqlcmd-probe-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $temporary
try {
    $wrapper=Join-Path $temporary 'migration wrapper.sql'
    $unicode="caf$([char]0x00e9) $([char]0x0627)$([char]0x0631)$([char]0x062f)$([char]0x0648)"
    $hex=([BitConverter]::ToString([Text.Encoding]::Unicode.GetBytes($unicode))).Replace('-','')
    $query="IF CONVERT(varbinary(max),N'$unicode')<>0x$hex THROW 51000,'Unicode input changed',1;`r`nPRINT 'FMSC_UTF8_OK';"
    foreach($scriptName in @('Installer/core.ps1','Migrations/Apply-Migrations.ps1')) {
        $tokens=$null;$errors=$null
        $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $project $scriptName),[ref]$tokens,[ref]$errors)
        if($errors.Count){throw "Parse errors: $scriptName"}
        $command=$ast.Find({param($node) $node -is [Management.Automation.Language.CommandAst] -and
            $node.Extent.Text -match 'SqlCmd.* -S .* -i '},$true)
        if(-not $command){throw "Migration command missing: $scriptName"}
        $arguments=@($command.CommandElements | Select-Object -Skip 1 | ForEach-Object {
            if($_ -is [Management.Automation.Language.VariableExpressionAst]) {
                switch($_.VariablePath.UserPath) {
                    SqlInstance {$SqlInstance}
                    DatabaseName {'master'}
                    wrapperPath {$wrapper}
                    default {throw 'Unexpected migration argument'}
                }
            } elseif($_ -is [Management.Automation.Language.CommandParameterAst]) {
                '-'+$_.ParameterName
            } else {$_.Value}
        })
        foreach($fail in @($false,$true)) {
            $sql=if($fail){":on error exit`r`nTHROW 51000,'FMSC_EXPECTED_ERROR',1;`r`nGO"}else{
                ":on error exit`r`n$query`r`nGO`r`nPRINT 'FMSC_GO_OK';`r`nGO"}
            [IO.File]::WriteAllText($wrapper,$sql,(New-Object Text.UTF8Encoding($false)))
            $start=New-Object Diagnostics.ProcessStartInfo
            $start.FileName=(Resolve-Path -LiteralPath $SqlCmdPath).Path
            $start.Arguments=($arguments|ForEach-Object {'"'+$_+'"'}) -join ' '
            $start.UseShellExecute=$false;$start.CreateNoWindow=$true
            $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
            $process=[Diagnostics.Process]::Start($start)
            try {
                $out=$process.StandardOutput.ReadToEndAsync();$err=$process.StandardError.ReadToEndAsync()
                if(-not $process.WaitForExit(30000)){$process.Kill();throw 'Read-only probe timed out'}
                $output=$out.GetAwaiter().GetResult()+$err.GetAwaiter().GetResult()
                if($fail) {
                    if($process.ExitCode -eq 0 -or $output -notmatch 'FMSC_EXPECTED_ERROR'){throw "SQL error handling failed: $output"}
                } elseif($process.ExitCode -ne 0 -or $output -notmatch 'FMSC_UTF8_OK' -or $output -notmatch 'FMSC_GO_OK') {
                    throw "UTF-8/input/GO probe failed: $output"
                }
            } finally {$process.Dispose()}
        }
        Write-Output "PASS: $scriptName migration arguments, UTF-8 Unicode, spaced input paths, GO and error exit."
    }
} finally {
    # Only files made by this probe; no recursive cleanup or database deletion.
    if($wrapper -and (Test-Path -LiteralPath $wrapper)){Remove-Item -LiteralPath $wrapper -Force}
    Remove-Item -LiteralPath $temporary
}
