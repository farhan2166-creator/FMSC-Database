using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Admissions;
using Microsoft.Data.SqlClient;

// The name guard prevents running writes against the school database accidentally.
string connectionText=Environment.GetEnvironmentVariable("FMSC_AUDIT_CONNECTION")??throw new Exception("Set FMSC_AUDIT_CONNECTION to an explicitly disposable restored database.");
var builder=new SqlConnectionStringBuilder(connectionText);
if(!builder.InitialCatalog.StartsWith("FMSC_AuditChecks_",StringComparison.Ordinal)||!builder.IntegratedSecurity)
    throw new Exception("Use integrated security and a disposable database named FMSC_AuditChecks_<suffix>. No production catalog is accepted.");
DatabaseHelper.ConnectionString=connectionText;
DataTable Query(string sql,params SqlParameter[] parameters)
{
    using var c=DatabaseHelper.GetConnection();c.Open();using var q=new SqlCommand(sql,c){CommandTimeout=120};q.Parameters.AddRange(parameters);
    var t=new DataTable();using var a=new SqlDataAdapter(q);a.Fill(t);return t;
}
int checks=0;
void Assert(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS: "+label);checks++;}
async Task Refused(Func<Task> action,string label)
{try{await action();}catch(InvalidOperationException e) when(e.Message.Contains("changed",StringComparison.OrdinalIgnoreCase)){Console.WriteLine("PASS: "+label);checks++;return;}throw new Exception("Expected stale-state refusal: "+label);}
Assert(Convert.ToString(Query("SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC").Rows[0][0])=="2","migration 2 installed on clone");
CurrentUser.UserId=Convert.ToInt32(Query("SELECT TOP(1) U.UserId FROM dbo.Users U WHERE EXISTS(SELECT 1 FROM dbo.Roles R WHERE R.RoleId=U.RoleId AND R.RoleName=N'Administrator') OR EXISTS(SELECT 1 FROM dbo.UserRoles UR JOIN dbo.Roles R ON R.RoleId=UR.RoleId WHERE UR.UserId=U.UserId AND R.RoleName=N'Administrator')").Rows[0][0]);
var contexts=Query("""
    SELECT TOP(1) Y.AcademicYearId,P.ClassSectionId,P.CampusId FROM dbo.AcademicYears Y CROSS JOIN dbo.CampusClassSections P
    WHERE CONVERT(date,GETDATE()) BETWEEN Y.StartDate AND Y.EndDate
      AND EXISTS(SELECT 1 FROM dbo.FeeConfigurations C WHERE C.CampusId=P.CampusId AND C.ClassId=P.ClassId)
      AND NOT EXISTS(SELECT 1 FROM dbo.FeeTypes F WHERE F.IsMandatory=1 AND NOT EXISTS
        (SELECT 1 FROM dbo.FeeConfigurations C WHERE C.CampusId=P.CampusId AND C.ClassId=P.ClassId AND C.FeeTypeId=F.FeeTypeId));
    """);
if(contexts.Rows.Count==0)throw new Exception("Clone needs a current academic year, class placement, and complete mandatory fee configurations.");
var context=contexts.Rows[0];int placement=Convert.ToInt32(context["ClassSectionId"]);
DataTable Rates()=>Query(QuickRegistrationWriter.FeeQuery,DatabaseHelper.Int("@Placement",placement));
SqlParameter[] AdmissionArgs(DataTable rates)=>new[]{
    DatabaseHelper.Bit("@QuickRegistration",true),DatabaseHelper.P("@AdmissionNumber",null),DatabaseHelper.Int("@FamilyAnchorAdmissionId",0),
    DatabaseHelper.Date("@AdmissionDate",DateTime.Today),DatabaseHelper.Int("@AdmissionCampusId",Convert.ToInt32(context["CampusId"])),
    DatabaseHelper.Int("@AcademicYearId",Convert.ToInt32(context["AcademicYearId"])),DatabaseHelper.Int("@ClassSectionId",placement),
    DatabaseHelper.P("@StudentName","Audit QR "+Guid.NewGuid().ToString("N")),DatabaseHelper.P("@FatherName","Audit parent"),
    DatabaseHelper.P("@ContactNumber","03001234567"),DatabaseHelper.P("@Address","Disposable audit fixture"),DatabaseHelper.P("@Disability","No"),
    DatabaseHelper.Bit("@UsesTransport",false),
    new SqlParameter("@SelectedFeeTypeIds",SqlDbType.Structured){TypeName="dbo.FeeTypeIdList",Value=Tvp.FeeTypeIds(Array.Empty<int>())},
    new SqlParameter("@AdmissionDiscounts",SqlDbType.Structured){TypeName="dbo.AdmissionFeeDiscountType",Value=Tvp.AdmissionFeeDiscounts(Array.Empty<(int,decimal)>())},
    DatabaseHelper.P("@DateOfBirth",null),DatabaseHelper.P("@PlaceOfBirth",null),DatabaseHelper.P("@Gender",null),DatabaseHelper.P("@BFormNumber",null),
    DatabaseHelper.P("@Religion",null),DatabaseHelper.P("@BloodGroup",null),DatabaseHelper.P("@AlternateContactNumber",null),
    DatabaseHelper.P("@PreviousSchool",null),DatabaseHelper.P("@FatherOccupation",null)
};
var rates=Rates();string quote=QuickRegistrationQuote.Create(rates);
int before=Convert.ToInt32(Query("SELECT COUNT(*) FROM dbo.Students").Rows[0][0]);
var rate=Query("SELECT TOP(1) C.FeeConfigurationId,C.Amount FROM dbo.FeeConfigurations C JOIN dbo.CampusClassSections P ON P.CampusId=C.CampusId AND P.ClassId=C.ClassId WHERE P.ClassSectionId=@Placement ORDER BY C.FeeConfigurationId",DatabaseHelper.Int("@Placement",placement)).Rows[0];
int rateId=Convert.ToInt32(rate["FeeConfigurationId"]);
// Use the production quotation query and isolation level to prove that an edit
// on a second connection cannot slip through between comparison and save.
using(var locked=DatabaseHelper.GetConnection()){
    locked.Open();using var transaction=locked.BeginTransaction(IsolationLevel.Serializable);
    using var preview=new SqlCommand(QuickRegistrationWriter.FeeQuery,locked,transaction);
    preview.Parameters.Add(DatabaseHelper.Int("@Placement",placement));
    var lockedRates=new DataTable();using(var adapter=new SqlDataAdapter(preview))adapter.Fill(lockedRates);
    try{
        Query("SET LOCK_TIMEOUT 250; UPDATE dbo.FeeConfigurations SET Amount=Amount+1 WHERE FeeConfigurationId=@Id",DatabaseHelper.Int("@Id",rateId));
        throw new Exception("Competing fee edit bypassed preview locks");
    }catch(SqlException e) when(e.Number==1222){Console.WriteLine("PASS: serializable quotation blocks competing rate update");checks++;}
    transaction.Rollback();
}
Query("UPDATE dbo.FeeConfigurations SET Amount=Amount+1 WHERE FeeConfigurationId=@Id",DatabaseHelper.Int("@Id",rateId));
await Refused(async()=>{await QuickRegistrationWriter.SaveNewAsync(placement,quote,AdmissionArgs(rates));},"stale fee quote rejected by production writer");
Assert(Convert.ToInt32(Query("SELECT COUNT(*) FROM dbo.Students").Rows[0][0])==before,"stale quote creates no student");
Query("UPDATE dbo.FeeConfigurations SET Amount=@Amount WHERE FeeConfigurationId=@Id",DatabaseHelper.P("@Amount",rate["Amount"]),DatabaseHelper.Int("@Id",rateId));
rates=Rates();var results=await QuickRegistrationWriter.SaveNewAsync(placement,QuickRegistrationQuote.Create(rates),AdmissionArgs(rates));
int admission=Convert.ToInt32(results.Tables.Cast<DataTable>().Last(t=>t.Columns.Contains("AdmissionId")&&t.Columns.Contains("QuickReference")&&t.Rows.Count>0).Rows[0]["AdmissionId"]);
var a=await QuickRegistrationWriter.LoadAsync(admission);var b=await QuickRegistrationWriter.LoadAsync(admission);
SqlParameter[] Edits(QuickRegistrationWriter.Registration loaded,string name,string phone)
{
    var row=loaded.Details.Rows[0];var args=new List<SqlParameter>{DatabaseHelper.Int("@AdmissionId",admission),DatabaseHelper.P("@StudentName",name),DatabaseHelper.P("@FatherName",row["FatherName"]),DatabaseHelper.P("@ContactNumber",phone),DatabaseHelper.P("@Address",row["Address"])};
    foreach(string key in new[]{"DateOfBirth","Gender","BFormNumber","Religion","BloodGroup","AlternateContactNumber","PreviousSchool","PlaceOfBirth","FatherOccupation","Disability","BiseEnrolmentNumber","CasteTribe"})args.Add(DatabaseHelper.P("@"+key,row[key]));
    args.Add(new SqlParameter("@Discounts",SqlDbType.Structured){TypeName="dbo.StudentFeeDiscountType",Value=Tvp.StudentFeeDiscounts(Array.Empty<(int,decimal)>())});return args.ToArray();
}
await QuickRegistrationWriter.SavePendingAsync(admission,b.Snapshot,Edits(b,"Clerk B saved","03007654321"));
await Refused(async()=>{await QuickRegistrationWriter.SavePendingAsync(admission,a.Snapshot,Edits(a,"Clerk A stale","03001234567"));},"second clerk cannot overwrite newer basics");
var saved=await QuickRegistrationWriter.LoadAsync(admission);
Assert(Convert.ToString(saved.Details.Rows[0]["ContactNumber"])=="03007654321","newer phone retained after rejected stale save");
if(saved.Fees.Rows.Count>0){
    int assignment=Convert.ToInt32(saved.Fees.Rows[0]["StudentFeeAssignmentId"]);
    Query("UPDATE dbo.StudentFeeAssignments SET Amount=Amount+1 WHERE StudentFeeAssignmentId=@Id",DatabaseHelper.Int("@Id",assignment));
    await Refused(async()=>{await QuickRegistrationWriter.SavePendingAsync(admission,saved.Snapshot,Edits(saved,"Stale fee edit","03007654321"));},"assignment changes invalidate student snapshot");
}
try{Query("UPDATE dbo.FeeConfigurations SET Amount=1000.50 WHERE FeeConfigurationId=@Id",DatabaseHelper.Int("@Id",rateId));throw new Exception("Fraction accepted by SQL");}
catch(SqlException e) when(e.Number==547){Console.WriteLine("PASS: SQL rejects fractional rate without rounding");checks++;}
Console.WriteLine($"PASS: {checks} production SQL checks. Fixture remains only in the disposable clone; drop that clone after inspection.");
