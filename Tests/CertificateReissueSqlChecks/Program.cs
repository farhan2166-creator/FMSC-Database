using Microsoft.Data.SqlClient;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;

string connection=Environment.GetEnvironmentVariable("FMSC_CERTIFICATE_TEST_CONNECTION")??throw new Exception("Owned fixture connection required.");
string owner=Environment.GetEnvironmentVariable("FMSC_CERTIFICATE_TEST_OWNER")??"";
var builder=new SqlConnectionStringBuilder(connection);
if(!Guid.TryParseExact(owner,"N",out _)||builder.InitialCatalog!="FMSC_CertReissueChecks_"+owner)throw new Exception("Unsafe fixture identity.");
DatabaseHelper.ConnectionString=connection;
DataTable Query(string sql,params SqlParameter[] parameters)
{
    using var c=new SqlConnection(connection);c.Open();
    using var guard=new SqlCommand("SELECT CONVERT(nvarchar(32),value) FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_CertificateChecksOwner';",c);
    if(Convert.ToString(guard.ExecuteScalar())!=owner)throw new Exception("Wrong fixture owner.");
    using var q=new SqlCommand(sql,c){CommandTimeout=45};q.Parameters.AddRange(parameters);
    var data=new DataTable();using var adapter=new SqlDataAdapter(q);adapter.Fill(data);return data;
}
int Number(string sql)=>Convert.ToInt32(Query(sql).Rows[0][0]);
int checks=0;
void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
int admin=Number("SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator' ORDER BY U.UserId;");
CurrentUser.UserId=admin;
using(var c=new SqlConnection(connection)){c.Open();DatabaseCompatibility.Validate(c);checks++;}
string suffix=Guid.NewGuid().ToString("N");
int admission=Convert.ToInt32(Query("INSERT dbo.Students(AdmissionNumber,RegistrationNumber,AdmissionDate,AdmissionCampusId,StudentName,FatherName,DateOfBirth,Status,LegacySetupPending) VALUES(@Number,@Registration,CONVERT(date,GETDATE()),(SELECT MIN(CampusId) FROM dbo.Campuses),N'Certificate Test Student',N'Certificate Test Parent','2001-01-02',N'Active',0);SELECT CONVERT(int,SCOPE_IDENTITY());",
    DatabaseHelper.P("@Number","8"+DateTime.UtcNow.Ticks),DatabaseHelper.P("@Registration","CERT-"+suffix)).Rows[0][0]);
DateTime oldDob=new(2001,1,2),newDob=new(2002,3,4),today=DateTime.Today;
int regular=Convert.ToInt32(Query("EXEC dbo.CreateRegularStudentCertificate @UserId=@User,@AdmissionId=@Admission,@CertificateDate=@Date,@ClassName=N'Ten';",
    DatabaseHelper.Int("@User",admin),DatabaseHelper.Int("@Admission",admission),DatabaseHelper.Date("@Date",today)).Rows[0][0]);
int firstRegular=Number($"SELECT MIN(RegularStudentCertificateIssueId) FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateId={regular};");
DataTable PrintRegular(int issue)=>Query("EXEC dbo.GetRegularStudentCertificatePrintData @UserId=@User,@RegularStudentCertificateId=@Certificate,@RegularStudentCertificateIssueId=@Issue;",
    DatabaseHelper.Int("@User",admin),DatabaseHelper.Int("@Certificate",regular),DatabaseHelper.Int("@Issue",issue));
Check(firstRegular>0&&PrintRegular(firstRegular).Rows[0].Field<DateTime>("DateOfBirth")==oldDob,"Initial regular copy has its original DOB snapshot.");
bool legacyEditRefused=false;
try{Query("EXEC dbo.UpdateRegularStudentCertificate @UserId=@User,@RegularStudentCertificateId=@Certificate,@CertificateDate=@Date,@ClassName=N'Changed class';",
    DatabaseHelper.Int("@User",admin),DatabaseHelper.Int("@Certificate",regular),DatabaseHelper.Date("@Date",today));}catch(SqlException ex)when(ex.Number==58534){legacyEditRefused=true;}
Check(legacyEditRefused&&PrintRegular(firstRegular).Rows[0].Field<string>("ClassName")=="Ten","The legacy edit endpoint cannot overwrite a saved certificate copy.");
Query($"UPDATE dbo.Students SET DateOfBirth='2002-03-04' WHERE AdmissionId={admission};");
Check(PrintRegular(firstRegular).Rows[0].Field<DateTime>("DateOfBirth")==oldDob,"Correcting the school DOB does not change an earlier regular print.");
SqlParameter[] Args(int certificate,int latest,DateTime dob)=>new[]{DatabaseHelper.Int("@CertificateRecordId",certificate),DatabaseHelper.Int("@ExpectedLatestIssueId",latest),DatabaseHelper.Date("@ExpectedDateOfBirth",dob),DatabaseHelper.Date("@CertificateDate",today),DatabaseHelper.P("@ExpectedSourceHash",Query($"SELECT dbo.CertificateAdmissionFingerprint({admission});").Rows[0][0])};
async Task<MutationIntent> Intent(MutationKind kind,SqlParameter[] args)=>new(Guid.NewGuid(),kind,admin,await RecoverableMutationWriter.GetScopeAsync(),RecoverableMutationWriter.PayloadHash(kind,admin,args),"Disposable certificate reissue check",DateTime.UtcNow);
var regularArgs=Args(regular,firstRegular,newDob);var regularIntent=await Intent(MutationKind.RegularCertificateReissue,regularArgs);
var saved=await RecoverableMutationWriter.SaveAsync(regularIntent,regularArgs);
Check(saved.Outcome==MutationOutcome.Saved&&saved.Result?.DateOfBirth==newDob,"Regular reissue saves the reviewed corrected DOB.");
int secondRegular=saved.Result!.RecordId;
Check(PrintRegular(firstRegular).Rows[0].Field<DateTime>("DateOfBirth")==oldDob&&PrintRegular(secondRegular).Rows[0].Field<DateTime>("DateOfBirth")==newDob,"Old and new regular copies print their own DOB values.");
Check(Query("EXEC dbo.GetRegularStudentCertificatePrintData @UserId=@User,@RegularStudentCertificateId=@Certificate;",DatabaseHelper.Int("@User",admin),DatabaseHelper.Int("@Certificate",regular)).Rows[0].Field<DateTime>("DateOfBirth")==oldDob,
    "Legacy regular print calls retain the original copy rather than silently choosing a corrected one.");
Check(Number($"SELECT COUNT(*) FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateId={regular};")==2,"One regular reissue adds exactly one copy.");
var replay=await RecoverableMutationWriter.SaveAsync(regularIntent,regularArgs);
Check(replay.Outcome==MutationOutcome.Saved&&replay.Result!.RecordId==secondRegular&&Number($"SELECT COUNT(*) FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateId={regular};")==2,"Repeating an identical request returns its saved issue without duplication.");
Check((await RecoverableMutationWriter.CheckAsync(regularIntent))!.RecordId==secondRegular,"Outcome checking recovers the exact saved regular issue.");
var stale=await RecoverableMutationWriter.SaveAsync(await Intent(MutationKind.RegularCertificateReissue,regularArgs),regularArgs);
Check(stale.Outcome==MutationOutcome.Rejected,"A second request reviewed against an older regular issue is refused.");
var currentArgs=Args(regular,secondRegular,newDob);Query($"UPDATE dbo.Students SET DateOfBirth='2003-05-06' WHERE AdmissionId={admission};");
var changed=await RecoverableMutationWriter.SaveAsync(await Intent(MutationKind.RegularCertificateReissue,currentArgs),currentArgs);
Check(changed.Outcome==MutationOutcome.Rejected&&Number($"SELECT COUNT(*) FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateId={regular};")==2,"A DOB changed after review is refused without adding a copy.");
Query($"UPDATE dbo.Students SET DateOfBirth='2001-01-02' WHERE AdmissionId={admission};");
var createdBirth=Query("EXEC dbo.CreateDateOfBirthCertificate @UserId=@User,@AdmissionId=@Admission,@CertificateDate=@Date;",DatabaseHelper.Int("@User",admin),DatabaseHelper.Int("@Admission",admission),DatabaseHelper.Date("@Date",today));
int birth=Convert.ToInt32(createdBirth.Rows[0]["CertificateRecordId"]),firstBirth=Convert.ToInt32(createdBirth.Rows[0]["DateOfBirthCertificateIssueId"]);
DataTable PrintBirth(int issue)=>Query("EXEC dbo.GetDateOfBirthCertificatePrintData @UserId=@User,@CertificateRecordId=@Certificate,@DateOfBirthCertificateIssueId=@Issue;",
    DatabaseHelper.Int("@User",admin),DatabaseHelper.Int("@Certificate",birth),DatabaseHelper.Int("@Issue",issue));
Check(PrintBirth(firstBirth).Rows[0].Field<DateTime>("DateOfBirth")==oldDob,"Initial DOB issuance automatically receives a complete snapshot.");
Query($"UPDATE dbo.Students SET DateOfBirth='2002-03-04' WHERE AdmissionId={admission};");
Check(PrintBirth(firstBirth).Rows[0].Field<DateTime>("DateOfBirth")==oldDob,"A school DOB correction does not change an earlier DOB certificate print.");
var birthArgs=Args(birth,firstBirth,newDob);
var uncollected=await RecoverableMutationWriter.SaveAsync(await Intent(MutationKind.BirthCertificateReissue,birthArgs),birthArgs);
Check(uncollected.Outcome==MutationOutcome.Rejected,"An uncollected DOB certificate cannot be reissued.");
Query("EXEC dbo.MarkDateOfBirthCertificateCollected @UserId=@User,@DateOfBirthCertificateIssueId=@Issue,@CollectedBy=N'Test Collector';",DatabaseHelper.Int("@User",admin),DatabaseHelper.Int("@Issue",firstBirth));
var birthIntent=await Intent(MutationKind.BirthCertificateReissue,birthArgs);
var reissuedBirth=await RecoverableMutationWriter.SaveAsync(birthIntent,birthArgs);
Check(reissuedBirth.Outcome==MutationOutcome.Saved&&reissuedBirth.Result?.DateOfBirth==newDob,"Collected DOB certificate reissues with the corrected reviewed DOB.");
int secondBirth=reissuedBirth.Result!.RecordId;
Check(PrintBirth(firstBirth).Rows[0].Field<DateTime>("DateOfBirth")==oldDob&&PrintBirth(secondBirth).Rows[0].Field<DateTime>("DateOfBirth")==newDob,"Both DOB copies retain independently printable personal details.");
Check(Query("EXEC dbo.GetDateOfBirthCertificatePrintData @UserId=@User,@CertificateRecordId=@Certificate;",DatabaseHelper.Int("@User",admin),DatabaseHelper.Int("@Certificate",birth)).Rows[0].Field<DateTime>("DateOfBirth")==oldDob,
    "Legacy DOB print calls retain the original saved birth date.");
Check(Number($"SELECT COUNT(*) FROM dbo.DateOfBirthCertificateIssues WHERE CertificateRecordId={birth} AND CollectedAt IS NULL;")==1,"The new DOB issue starts uncollected without changing the prior collection.");
var birthReplay=await RecoverableMutationWriter.SaveAsync(birthIntent,birthArgs);
Check(birthReplay.Result!.RecordId==secondBirth&&Number($"SELECT COUNT(*) FROM dbo.DateOfBirthCertificateIssues WHERE CertificateRecordId={birth};")==2,"A replayed DOB request never creates another issue.");
Check((await RecoverableMutationWriter.CheckAsync(birthIntent))!.RecordId==secondBirth,"DOB recovery checks return the exact committed copy.");
Check(PrintBirth(int.MaxValue).Rows.Count==0&&PrintRegular(int.MaxValue).Rows.Count==0,"A foreign or nonexistent issue ID cannot print another certificate's details.");
Query($"UPDATE dbo.Students SET DateOfBirth=NULL WHERE AdmissionId={admission};");
var missingArgs=Args(regular,secondRegular,newDob);
Check((await RecoverableMutationWriter.SaveAsync(await Intent(MutationKind.RegularCertificateReissue,missingArgs),missingArgs)).Outcome==MutationOutcome.Rejected,"Missing current DOB refuses a regular reissue.");
Check(PrintRegular(firstRegular).Rows.Count==1&&PrintBirth(firstBirth).Rows.Count==1,"Missing current school DOB does not prevent reprinting saved snapshots.");
Check(Number($"SELECT COUNT(*) FROM dbo.MutationRequests WHERE RequestId IN('{regularIntent.Id}','{birthIntent.Id}');")==2,"Successful reissues commit durable recovery records.");
Query($"UPDATE dbo.Students SET DateOfBirth='2002-03-04',StudentName=N'Changed School Name' WHERE AdmissionId={admission};");
var concurrentArgs=Args(regular,secondRegular,newDob);
var intents=new[]{await Intent(MutationKind.RegularCertificateReissue,concurrentArgs),await Intent(MutationKind.RegularCertificateReissue,concurrentArgs)};
var concurrent=await Task.WhenAll(intents.Select(intent=>RecoverableMutationWriter.SaveAsync(intent,concurrentArgs)));
Check(concurrent.Count(r=>r.Outcome==MutationOutcome.Saved)==1&&concurrent.Count(r=>r.Outcome==MutationOutcome.Rejected)==1,
    "Two concurrent regular reissues reviewed against the same copy cannot both commit.");
int thirdRegular=concurrent.Single(r=>r.Outcome==MutationOutcome.Saved).Result!.RecordId;
Check(PrintRegular(thirdRegular).Rows[0].Field<string>("StudentName")=="Changed School Name"&&PrintRegular(firstRegular).Rows[0].Field<string>("StudentName")=="Certificate Test Student","A reissue uses fresh Admission personal details while earlier copies keep their original details.");
var lostArgs=Args(regular,thirdRegular,newDob);var lostIntent=await Intent(MutationKind.RegularCertificateReissue,lostArgs);
var lost=await RecoverableMutationWriter.SaveAsync(lostIntent,lostArgs,t=>{t.Commit();throw new IOException("Simulated lost commit reply");});
Check(lost.Outcome==MutationOutcome.Unconfirmed,"A lost commit reply is reported as uncertain rather than retried.");
var checkedLost=await RecoverableMutationWriter.CheckAsync(lostIntent);
Check(checkedLost is not null&&PrintRegular(checkedLost.RecordId).Rows[0].Field<DateTime>("DateOfBirth")==newDob,"Read-only recovery finds the exact issue after a lost commit reply.");
int copies=Number($"SELECT COUNT(*) FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateId={regular};");
Check((await RecoverableMutationWriter.SaveAsync(lostIntent,lostArgs)).Result!.RecordId==checkedLost!.RecordId&&Number($"SELECT COUNT(*) FROM dbo.RegularStudentCertificateIssues WHERE RegularStudentCertificateId={regular};")==copies,
    "Recovery followed by replay still cannot create a duplicate copy.");
int deniedUser=Convert.ToInt32(Query("INSERT dbo.Roles(RoleName) VALUES(@Role);DECLARE @RoleId int=CONVERT(int,SCOPE_IDENTITY());INSERT dbo.Users(Username,RoleId) VALUES(@User,@RoleId);SELECT CONVERT(int,SCOPE_IDENTITY());",
    DatabaseHelper.P("@Role","CT_NoGrants_"+suffix),DatabaseHelper.P("@User","certificate_test_"+suffix)).Rows[0][0]);
bool printDenied=false;
try{Query("EXEC dbo.GetRegularStudentCertificatePrintData @UserId=@User,@RegularStudentCertificateId=@Certificate,@RegularStudentCertificateIssueId=@Issue;",
    DatabaseHelper.Int("@User",deniedUser),DatabaseHelper.Int("@Certificate",regular),DatabaseHelper.Int("@Issue",firstRegular));}catch(SqlException ex)when(ex.Number==50003){printDenied=true;}
Check(printDenied,"Saved-copy reprints retain their permission checks.");
var deniedArgs=Args(regular,checkedLost.RecordId,newDob);
var deniedIntent=new MutationIntent(Guid.NewGuid(),MutationKind.RegularCertificateReissue,deniedUser,await RecoverableMutationWriter.GetScopeAsync(),
    RecoverableMutationWriter.PayloadHash(MutationKind.RegularCertificateReissue,deniedUser,deniedArgs),"Denied fixture request",DateTime.UtcNow);
CurrentUser.UserId=deniedUser;
Check((await RecoverableMutationWriter.SaveAsync(deniedIntent,deniedArgs)).Outcome==MutationOutcome.Rejected,"An operator without certificate permission cannot reissue.");
CurrentUser.UserId=admin;
await CharacterCertificateChecks.Run(connection,owner,admin,deniedUser);
LeaveCertificateChecks.Run(connection,owner,admin);
var register=Query(FMSC_SMS.Pages.Reports.Certificates.CertificateIssueRegisterData.BuildQuery(true,true),DatabaseHelper.Int("@UserId",admin),
    DatabaseHelper.P("@FromDate",null),DatabaseHelper.P("@ToDate",null),DatabaseHelper.P("@CertificateType",null),DatabaseHelper.P("@RecipientType",null),
    DatabaseHelper.P("@EntryKind",null),DatabaseHelper.P("@CollectionStatus",null),DatabaseHelper.P("@Search",""));
Check(register.AsEnumerable().Any(row=>row.Field<string>("Recipient")=="Fresh Admission Name"),"Certificate register uses the saved character issue's fresh Admission snapshot.");
Check(register.AsEnumerable().Any(row=>row.Field<string>("Recipient")=="Changed School Name"),"Certificate register includes regular reissues and their saved personal details.");
Check(register.AsEnumerable().Any(row=>row.Field<string>("Recipient")=="Fresh Leaving Name"),"Certificate register uses school/college leaving issue snapshots.");
Console.WriteLine($"PASS: {checks} isolated SQL snapshot, corrected-DOB, stale review, collection, request replay and recovery checks.");
