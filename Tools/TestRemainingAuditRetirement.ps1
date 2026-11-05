# Schema-only fixture: never accepts a school database connection or backup.
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$owner=[guid]::NewGuid().ToString('N')
$catalog='FMSC_BillingChecks_'+$owner
$instance='(localdb)\'+$catalog
$run=Join-Path $root ('artifacts/remaining-audit-retirement/'+$owner)
New-Item -ItemType Directory -Path $run -Force | Out-Null
$script:checks=0
function Check([bool]$ok,[string]$message){if(-not $ok){throw "FAIL: $message"};$script:checks++;Write-Output "PASS: $message"}
function Connect {
 $c=[Data.SqlClient.SqlConnection]::new("Server=$instance;Database=$catalog;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=15")
 $c.Open();$q=$c.CreateCommand();$q.CommandText="SELECT CONVERT(nvarchar(32),value) FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_BillingChecksOwner'"
 if([string]$q.ExecuteScalar() -ne $owner){$c.Dispose();throw 'Fixture ownership mismatch.'};return $c
}
function Query([string]$sql){$c=Connect;try{$q=$c.CreateCommand();$q.CommandText=$sql;$q.CommandTimeout=90;$t=[Data.DataSet]::new();$a=[Data.SqlClient.SqlDataAdapter]::new($q);[void]$a.Fill($t);return ,$t}finally{$c.Dispose()}}
function Number([string]$sql){return (Query $sql).Tables[0].Rows[0][0]}
function Apply([bool]$commit){
 $c=Connect;$tx=$c.BeginTransaction()
 try{foreach($batch in [regex]::Split([IO.File]::ReadAllText((Join-Path $root 'Tests/LifecycleSqlChecks/Migrations/59.sql')),'(?im)^\s*GO\s*\r?$')){
  if([string]::IsNullOrWhiteSpace($batch)){continue};$q=$c.CreateCommand();$q.Transaction=$tx;$q.CommandText=$batch;$q.CommandTimeout=90;[void]$q.ExecuteNonQuery()
 };if($commit){$tx.Commit()}else{$tx.Rollback()}}catch{if($tx.Connection){$tx.Rollback()};throw}finally{$c.Dispose()}
}
function Refusal([string]$sql){try{[void](Query $sql);return 0}catch{$e=$_.Exception;while($e.InnerException){$e=$e.InnerException};if($e -is [Data.SqlClient.SqlException]){return $e.Number};throw}}
$created=$false
try {
 & SqlLocalDB create $catalog -s;if($LASTEXITCODE -ne 0){throw 'Cannot create private instance'};$created=$true
 $safe=$run.Replace("'","''")
 & sqlcmd -S $instance -d master -E -b -Q "CREATE DATABASE [$catalog] ON PRIMARY(NAME=N'$catalog',FILENAME=N'$safe\fixture.mdf') LOG ON(NAME=N'${catalog}_log',FILENAME=N'$safe\fixture_log.ldf'); EXEC [$catalog].sys.sp_addextendedproperty @name=N'FMSC_BillingChecksOwner',@value=N'$owner';"
 if($LASTEXITCODE -ne 0){throw 'Cannot create owned catalog'}
 $package=Join-Path $env:USERPROFILE '.nuget/packages/microsoft.sqlpackage/170.5.96/tools/net8.0/any/sqlpackage.dll'
 & dotnet $package '/Action:Publish' "/SourceFile:$root/fmsc.dacpac" "/TargetConnectionString:Server=$instance;Database=$catalog;Integrated Security=True;TrustServerCertificate=True" '/p:DropObjectsNotInSource=False' '/p:DropExtendedPropertiesNotInSource=False' '/p:ScriptDatabaseOptions=False' '/Quiet:True'
 if($LASTEXITCODE -ne 0){throw 'Cannot publish record-free schema'}
 [void](Query "INSERT dbo.DatabaseVersion(VersionNumber,AppliedOn,AppliedBy,Notes) VALUES(N'58',SYSDATETIME(),SUSER_SNAME(),N'Owned schema-only fixture');")
 [void](Query ([IO.File]::ReadAllText((Join-Path $root 'Validation/AutomaticBillingFixture.sql'))))
 [void](Query "INSERT dbo.FeeConfigurations(CampusId,ClassId,FeeTypeId,Amount,Frequency) VALUES(1,1,1,10000,N'Monthly');")
 [void](Query "DECLARE @O uniqueidentifier=NEWID();INSERT dbo.AuditOperations(OperationId,UserId,OperationType) VALUES(@O,1,N'FICTIONAL_EXISTING_HISTORY');EXEC dbo.WriteAuditLog @O,1,N'Students',N'1',N'UPDATE',NULL,N'{""Status"":""Active""}';")
 $originalLogs=Number 'SELECT COUNT(*) FROM dbo.AuditLogs'
 $originalOperations=Number 'SELECT COUNT(*) FROM dbo.AuditOperations'
 Check ($originalLogs -gt 0 -and $originalOperations -gt 0) 'Fixture contains historical generic audit records before migration.'
 $names=@([regex]::Matches([IO.File]::ReadAllText((Join-Path $root 'Tests/LifecycleSqlChecks/Migrations/59.sql')),'(?im)^CREATE OR ALTER PROCEDURE\s+([\w.\[\]]+)') | ForEach-Object{$_.Groups[1].Value.Replace('[','').Replace(']','')})
 Check ($names.Count -eq 19) 'All 19 reviewed procedures are included.'
 $signatures=@{};$refusals=@{};$invocations=@{}
 foreach($name in $names){
  $signatures[$name]=[string](Number "SELECT name,TYPE_NAME(user_type_id) TypeName,max_length,precision,scale,is_output FROM sys.parameters WHERE object_id=OBJECT_ID(N'$name') ORDER BY parameter_id FOR JSON PATH")
  $params=(Query "SELECT P.name,T.name TypeName,S.name SchemaName,T.is_table_type FROM sys.parameters P JOIN sys.types T ON T.user_type_id=P.user_type_id JOIN sys.schemas S ON S.schema_id=T.schema_id WHERE P.object_id=OBJECT_ID(N'$name') ORDER BY P.parameter_id").Tables[0]
  $declare='';$argsList=@();foreach($p in $params.Rows){if($p.is_table_type){$declare+="DECLARE $($p.name) [$($p.SchemaName)].[$($p.TypeName)];";$argsList+="$($p.name)=$($p.name)"}else{$value=if($p.name -eq '@UserId'){'-9999'}else{'NULL'};$argsList+="$($p.name)=$value"}}
  $sql=$declare+'EXEC '+$name+' '+($argsList -join ',')+';';$invocations[$name]=$sql;$refusals[$name]=Refusal $sql
  Check ($refusals[$name] -ne 0) "$name rejects an unauthorized/invalid request before migration."
 }
 # Fingerprint every existing row, including audit and recovery tables; only the version row may change.
 $fingerprint="DECLARE @sql nvarchar(max)=N''; SELECT @sql+=N'SELECT N'''+S.name+'.'+T.name+N''' TableName,CONVERT(varchar(64),HASHBYTES(''SHA2_256'',(SELECT * FROM '+QUOTENAME(S.name)+'.'+QUOTENAME(T.name)+N' FOR JSON PATH,INCLUDE_NULL_VALUES)),2) Fingerprint;'+CHAR(10) FROM sys.tables T JOIN sys.schemas S ON S.schema_id=T.schema_id WHERE T.name<>N'DatabaseVersion' ORDER BY S.name,T.name;EXEC sys.sp_executesql @sql;"
 $before=(Query $fingerprint).GetXml()
 $moduleSnapshot=[string](Number "SELECT SCHEMA_NAME(P.schema_id) SchemaName,P.name,M.definition FROM sys.procedures P JOIN sys.sql_modules M ON M.object_id=P.object_id ORDER BY P.schema_id,P.name FOR JSON PATH")
 Apply $false
 Check ((Number 'SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC') -eq '58') 'Rollback restores schema version.'
 Check ([string](Number "SELECT SCHEMA_NAME(P.schema_id) SchemaName,P.name,M.definition FROM sys.procedures P JOIN sys.sql_modules M ON M.object_id=P.object_id ORDER BY P.schema_id,P.name FOR JSON PATH") -ceq $moduleSnapshot) 'Rollback restores all procedure definitions.'
 Check ((Query $fingerprint).GetXml() -eq $before) 'Rolled-back migration preserves all fixture records.'
 Apply $true
 Check ((Number 'SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC') -eq '59') 'Migration reaches schema 59.'
 Check ((Query $fingerprint).GetXml() -eq $before) 'Committed migration preserves every existing business, audit and recovery row.'
 foreach($name in $names){
  Check ([string](Number "SELECT name,TYPE_NAME(user_type_id) TypeName,max_length,precision,scale,is_output FROM sys.parameters WHERE object_id=OBJECT_ID(N'$name') ORDER BY parameter_id FOR JSON PATH") -eq $signatures[$name]) "$name preserves its parameter contract."
  Check ((Refusal $invocations[$name]) -eq $refusals[$name]) "$name preserves permission/invalid-request rejection."
 }
 Check ((Number "SELECT COUNT(*) FROM sys.procedures P JOIN sys.sql_modules M ON M.object_id=P.object_id WHERE P.name<>N'WriteAuditLog' AND (M.definition LIKE N'%WriteAuditLog%' OR M.definition LIKE N'%INSERT%AuditOperations%')") -eq 0) 'No procedure calls the retiring generic audit writer.'
 [void](Query "EXEC dbo.SaveStudentDetails @UserId=1,@AdmissionId=1,@StudentName=N'Updated Fictional Student';")
 Check ((Number "SELECT COUNT(*) FROM dbo.Students WHERE AdmissionId=1 AND StudentName=N'Updated Fictional Student'") -eq 1) 'Student details still save.'
 [void](Query "INSERT dbo.Sections(SectionCode,SectionName) VALUES(N'B',N'Fictional Section B'); INSERT dbo.CampusClassSections(CampusId,ClassId,SectionId) VALUES(1,1,2); DECLARE @D date=CONVERT(date,GETDATE());EXEC dbo.TransferEnrollmentClassSection 1,1,2,@D,N'Fictional transfer';")
 Check ((Number "SELECT COUNT(*) FROM dbo.EnrollmentHistory WHERE EnrollmentId=1 AND ActionType=N'TRANSFER'") -eq 1) 'Section transfer retains operational enrollment history.'
 [void](Query "EXEC dbo.AddStudentFeeAssignment @UserId=1,@EnrollmentId=1,@FeeTypeId=1;")
 Check ((Number 'SELECT COUNT(*) FROM dbo.BillingCharges WHERE EnrollmentId=1 AND FeeTypeId=1') -gt 0) 'Fee assignment still posts opening charges.'
 [void](Query "DECLARE @Id bigint=(SELECT TOP(1) BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=1 AND FeeTypeId=1 ORDER BY BillingChargeId);EXEC dbo.ReverseBillingCharge 1,@Id,N'Fictional reversal';")
 Check ((Number "SELECT COUNT(*) FROM dbo.BillingCharges WHERE EnrollmentId=1 AND Status=N'Cancelled'") -gt 0) 'Charge reversal preserves the cancelled business record.'
 [void](Query "INSERT dbo.Roles(RoleName) VALUES(N'Accountant');EXEC dbo.SaveRolePermissions 1,2,N'[]';DECLARE @H varbinary(64)=CONVERT(varbinary(64),REPLICATE('h',64)),@S varbinary(32)=CONVERT(varbinary(32),REPLICATE('s',32));EXEC dbo.SaveUser @UserId=1,@Username=N'fictional_new_user',@RoleId=2,@PasswordHash=@H,@PasswordSalt=@S;")
 Check ((Number "SELECT COUNT(*) FROM dbo.Users WHERE Username=N'fictional_new_user'") -eq 1) 'Account and role-permission saves still succeed.'
 [void](Query "EXEC comm.SaveRolePermissions 1,2,N'[]';")
 Check ((Number "SELECT COUNT(*) FROM comm.Audit WHERE Operation=N'CommunicationGrantsSaved'") -eq 1) 'Communication permission save retains its separate operational history.'
 Check ((Number 'SELECT COUNT(*) FROM dbo.AuditLogs') -eq $originalLogs) 'Successful revised workflows create no generic audit logs and retain existing logs.'
 Check ((Number 'SELECT COUNT(*) FROM dbo.AuditOperations') -eq $originalOperations) 'Successful revised workflows create no generic audit operations and retain existing operations.'
 Check ((Number "SELECT COUNT(*) FROM sys.objects WHERE object_id IN(OBJECT_ID(N'dbo.AuditLogs'),OBJECT_ID(N'dbo.AuditOperations'),OBJECT_ID(N'dbo.AuditValueLeaves'),OBJECT_ID(N'dbo.GetAuditLogs'))") -eq 4) 'Historical audit readers remain intact.'
 Check ((Refusal ([IO.File]::ReadAllText((Join-Path $root 'Tests/LifecycleSqlChecks/Migrations/59.sql')).Split([string[]]@("`nGO"),[StringSplitOptions]::None)[0])) -eq 51000) 'Migration refuses execution without an outer transaction.'
 Write-Output "PASS: $script:checks remaining-audit-retirement checks."
} finally {
 if($created){& SqlLocalDB stop $catalog; & SqlLocalDB delete $catalog}
 # Retain generated files as ignored local evidence; never delete a computed directory recursively.
}
