param(
    [Parameter(Mandatory=$true)][string]$PublishDirectory
)
$ErrorActionPreference = 'Stop'
$publishRoot = (Get-Item -LiteralPath $PublishDirectory -ErrorAction Stop).FullName
if (-not (Test-Path -LiteralPath $publishRoot -PathType Container)) {
    throw "Publish directory is not a folder: $publishRoot"
}
$cultureNames = @([Globalization.CultureInfo]::GetCultures([Globalization.CultureTypes]::AllCultures).Name)
foreach ($folder in Get-ChildItem -LiteralPath $publishRoot -Directory -Force) {
    if ($folder.Name -notin $cultureNames) { continue }
    $culture = [Globalization.CultureInfo]::GetCultureInfo($folder.Name)
    if ($culture.TwoLetterISOLanguageName -in @('en', 'ur')) { continue }
    # Reused publish destinations can retain empty folders after satellite files
    # are excluded. Delete only empty, immediate culture folders; never recurse.
    if ([IO.Directory]::GetFileSystemEntries($folder.FullName).Length -eq 0) {
        [IO.Directory]::Delete($folder.FullName, $false)
        Write-Output "Removed empty published language folder: $($folder.Name)"
    }
}
