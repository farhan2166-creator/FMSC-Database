param([string]$Server='(localdb)\MSSQLLocalDB',[string]$Database='FMSC_DB')
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '../Pages/Finance/Collection/OutstandingBillSelection.cs'))
$query=[regex]::Match($source,'(?s)Query\s*=\s*"""(.*?)""";').Groups[1].Value
if([string]::IsNullOrWhiteSpace($query)){throw 'Bill-selection SQL not found'}
$connection=[Data.SqlClient.SqlConnection]::new("Server=$Server;Database=$Database;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5")
$connection.Open()
try {
 $command=$connection.CreateCommand()
 $command.CommandText="SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId;"
 $user=[int]$command.ExecuteScalar();$command.Dispose()
 function Read-Selection([int]$Campus=0,[int]$Year=0,[int]$Class=0,[int]$Section=0) {
  $command=$connection.CreateCommand();$command.CommandText=$query
  foreach($p in @{UserId=$user;CampusId=$Campus;AcademicYearId=$Year;ClassId=$Class;SectionId=$Section}.GetEnumerator()){$null=$command.Parameters.AddWithValue('@'+$p.Key,$p.Value)}
  $table=[Data.DataTable]::new();$adapter=[Data.SqlClient.SqlDataAdapter]::new($command)
  try{$null=$adapter.Fill($table);return ,$table}finally{$adapter.Dispose();$command.Dispose()}
 }
 $all=Read-Selection
 $command=$connection.CreateCommand();$command.CommandText="SELECT COALESCE(SUM(dbo.GetFeeChargeBalance(BillingChargeId)),0) FROM dbo.BillingCharges WHERE Status<>N'Cancelled';"
 $expected=[decimal]$command.ExecuteScalar();$command.Dispose()
 $actual=[decimal](($all.Rows|Measure-Object Outstanding -Sum).Sum)
 if($actual-ne$expected){throw 'Selection does not match authoritative balances'}
 if(@($all.Rows|Group-Object AdmissionId|Where-Object Count -gt 1).Count){throw 'Duplicate students in selection'}
 foreach($row in $all.Rows){if([string]::IsNullOrWhiteSpace([string]$row['AdmissionNumber'])){throw 'Missing admission/QR reference'}}
 $checks=3
 foreach($scope in $all.Rows|Group-Object CampusId,AcademicYearId,ClassId,SectionId) {
  $row=$scope.Group[0]
  $filtered=Read-Selection -Campus ([int]$row['CampusId']) -Year ([int]$row['AcademicYearId']) -Class ([int]$row['ClassId']) -Section ([int]$row['SectionId'])
  if((($filtered.Rows|ForEach-Object{$_['AdmissionId']}) -join ',')-ne(($scope.Group|ForEach-Object{$_['AdmissionId']}) -join ',')){throw 'Placement filter differs from the full selection'}
  $checks++;$filtered.Dispose()
 }
 Write-Output "PASS: $checks read-only SQL checks; $($all.Rows.Count) unique owing students, Rs. $($actual.ToString('N0')), with matching placement filters. No bills issued."
 $all.Dispose()
}finally{$connection.Dispose()}
