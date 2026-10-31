# Clone the authorized school database; all fixture writes stay in the owned clone.
param([string]$Instance='(localdb)\MSSQLLocalDB')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$id=[guid]::NewGuid().ToString('N');$catalog='FMSC_RegistrationChecks_'+$id
$run=Join-Path $root ('artifacts/registration-workflow-checks/'+$id)
[void](New-Item -ItemType Directory -Path $run -Force)
$backup=(Join-Path $run 'fixture.bak').Replace("'","''")
$directory=$run.Replace("'","''")
$versionLines=@(& sqlcmd -S $Instance -E -d FMSC_DB -b -l 10 -h -1 -W -Q "SET NOCOUNT ON; SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC;")
if($LASTEXITCODE -ne 0){throw 'Could not read source schema version.'}
$version=($versionLines | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^\d+$' }) -join ''
if($version -notin @('63','64','65')){throw 'Registration tests require schema 63, 64 or 65.'}
# Upgrade only the disposable clone, in order; never rerun a source migration.
$migration=(@(
 if($version -eq '63'){[IO.File]::ReadAllText((Join-Path $root 'Migrations/64.sql'))}
 if($version -in @('63','64')){[IO.File]::ReadAllText((Join-Path $root 'Migrations/65.sql'))}
) -join "`r`n")
$header=@"
SET NOCOUNT ON; SET XACT_ABORT ON; SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
IF NOT EXISTS(SELECT 1 FROM sys.master_files WHERE database_id=DB_ID(N'FMSC_DB') AND file_id=1 AND physical_name=N'C:\ProgramData\FMSC_Database\FMSC_DB.mdf')
 THROW 51000,'Unexpected school database source.',1;
IF DB_ID(N'$catalog') IS NOT NULL THROW 51000,'Owned clone already exists.',1;
BACKUP DATABASE FMSC_DB TO DISK=N'$backup' WITH COPY_ONLY,CHECKSUM,INIT;
RESTORE VERIFYONLY FROM DISK=N'$backup' WITH CHECKSUM;
DECLARE @Moves nvarchar(max),@Restore nvarchar(max);
SELECT @Moves=STRING_AGG(CONVERT(nvarchar(max),N'MOVE N'''+REPLACE(name,N'''',N'''''')+N''' TO N''$directory/file-'+CONVERT(nvarchar(10),file_id)+CASE WHEN type=1 THEN N'.ldf''' ELSE N'.mdf''' END),N',')
FROM sys.master_files WHERE database_id=DB_ID(N'FMSC_DB') AND type IN(0,1);
SET @Restore=N'RESTORE DATABASE [$catalog] FROM DISK=N''$backup'' WITH '+@Moves+N',CHECKSUM,RECOVERY;';
EXEC(@Restore);
GO
USE [$catalog];
GO
BEGIN TRAN;
GO
"@
$script=Join-Path $run 'checks.sql'
[IO.File]::WriteAllText($script,$header+"`r`n"+$migration+"`r`nCOMMIT;`r`nGO`r`n"+[IO.File]::ReadAllText((Join-Path $root 'Validation/RegistrationWorkflowChecks.sql')))
try{
 & sqlcmd -S $Instance -E -d master -b -l 10 -t 180 -i $script 2>&1 | Tee-Object -FilePath (Join-Path $run 'result.txt')
 if($LASTEXITCODE -ne 0){throw 'Registration workflow checks failed in the owned clone.'}
}finally{
 if($catalog -notmatch '^FMSC_RegistrationChecks_[0-9a-f]{32}$'){throw 'Invalid clone identity; cleanup refused.'}
 & sqlcmd -S $Instance -E -d master -b -l 10 -t 60 -Q "IF DB_ID(N'$catalog') IS NOT NULL BEGIN ALTER DATABASE [$catalog] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$catalog]; END;"
 if($LASTEXITCODE -ne 0){throw 'Owned clone cleanup failed.'}
}
