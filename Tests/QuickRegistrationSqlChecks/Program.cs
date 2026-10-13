using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Admissions;
using FMSC_SMS.Pages.Students.Maintenance;
using Microsoft.Data.SqlClient;

// The name guard prevents running writes against the school database accidentally.
string connectionText=Environment.GetEnvironmentVariable("FMSC_AUDIT_CONNECTION")??throw new Exception("Set FMSC_AUDIT_CONNECTION to an explicitly disposable restored database.");
var builder=new SqlConnectionStringBuilder(connectionText);
bool localSqlAuth=!builder.IntegratedSecurity && builder.DataSource.StartsWith("127.0.0.1,",StringComparison.Ordinal) && builder.UserID=="sa";
if(!builder.InitialCatalog.StartsWith("FMSC_AuditChecks_",StringComparison.Ordinal)||(!builder.IntegratedSecurity&&!localSqlAuth))
    throw new Exception("Use a disposable database named FMSC_AuditChecks_<suffix>, with integrated security or an isolated loopback SQL Server test instance. No production catalog is accepted.");
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
Assert(Convert.ToString(Query("SELECT TOP(1) VersionNumber FROM dbo.DatabaseVersion ORDER BY VersionId DESC").Rows[0][0])=="3","migration 3 installed on clone");
using(var compatible=DatabaseHelper.GetConnection()){
    compatible.Open();DatabaseCompatibility.Validate(compatible);
    Assert(true,"production startup/restore compatibility contract accepts migrated schema");
}
try{
    Query("""
        DECLARE @Check sysname=(SELECT TOP(1) name FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.LegacyAdmissionSetup') AND is_system_named=1);
        IF @Check IS NULL THROW 59991,'Missing generated setup safeguard',1;
        DECLARE @Sql nvarchar(max)=N'ALTER TABLE dbo.LegacyAdmissionSetup DROP CONSTRAINT '+QUOTENAME(@Check);
        EXEC sys.sp_executesql @Sql;
        ALTER TABLE dbo.LegacyAdmissionSetup WITH CHECK ADD CHECK ([SetupId]>=(1));
        """);
    try{
        using var weakened=DatabaseHelper.GetConnection();weakened.Open();DatabaseCompatibility.Validate(weakened);
        throw new Exception("Weakened anonymous check passed startup validation");
    }catch(DatabaseCompatibilityException){Console.WriteLine("PASS: startup rejects weakened generated check expression");checks++;}
}finally{
    Query("""
        DECLARE @Check sysname=(SELECT TOP(1) name FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.LegacyAdmissionSetup') AND is_system_named=1);
        IF @Check IS NOT NULL BEGIN
            DECLARE @Sql nvarchar(max)=N'ALTER TABLE dbo.LegacyAdmissionSetup DROP CONSTRAINT '+QUOTENAME(@Check);
            EXEC sys.sp_executesql @Sql;
        END;
        ALTER TABLE dbo.LegacyAdmissionSetup WITH CHECK ADD CHECK ([SetupId]=(1));
        """);
}
using(var restored=DatabaseHelper.GetConnection()){
    restored.Open();DatabaseCompatibility.Validate(restored);
    Assert(true,"restored exact generated safeguard passes startup validation");
}
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
    DatabaseHelper.Int("@UserId",-999), // Writer must replace this with the current actor once.
    DatabaseHelper.Bit("@QuickRegistration",true),DatabaseHelper.P("@AdmissionNumber",null),DatabaseHelper.P("@FamilyStudentId",null),
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
var listed=Query(AdmissionReviewSql.Query,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Bit("@Pending",true));
Assert(listed.AsEnumerable().Any(r=>Convert.ToInt32(r["AdmissionId"])==admission),"production pending list includes eligible QR");
var roleFixture=Query("""
    IF NOT EXISTS(SELECT 1 FROM dbo.Permissions WHERE PermissionCode=N'Students.Admit')
        INSERT dbo.Permissions(PermissionCode,PermissionName) VALUES(N'Students.Admit',N'Admit students');
    IF NOT EXISTS(SELECT 1 FROM dbo.Permissions WHERE PermissionCode=N'Students.View')
        INSERT dbo.Permissions(PermissionCode,PermissionName) VALUES(N'Students.View',N'View students');
    INSERT dbo.Roles(RoleName) VALUES(@Name);
    DECLARE @Role int=SCOPE_IDENTITY();
    INSERT dbo.Users(Username,RoleId) VALUES(@Name,@Role);
    DECLARE @User int=SCOPE_IDENTITY();
    INSERT dbo.UserRoles(UserId,RoleId) VALUES(@User,@Role);
    INSERT dbo.RolePermissions(RoleId,PermissionId) SELECT @Role,PermissionId FROM dbo.Permissions WHERE PermissionCode=N'Students.Admit';
    SELECT @User UserId,@Role RoleId;
    """,DatabaseHelper.P("@Name","audit-limited-"+Guid.NewGuid().ToString("N"))).Rows[0];
int limitedUser=Convert.ToInt32(roleFixture["UserId"]),limitedRole=Convert.ToInt32(roleFixture["RoleId"]);
try{
    Query(AdmissionReviewSql.Query,DatabaseHelper.Int("@UserId",limitedUser),DatabaseHelper.Bit("@Pending",true));
    throw new Exception("Admit alone unexpectedly authorized the student list");
}catch(SqlException e) when(e.Number==50003){Console.WriteLine("PASS: SQL retains Students.View requirement for admission-only role");checks++;}
Assert(!QuickRegistrationAccess.CanUse(p=>p=="Students.Admit"),"admission-only role is not offered unusable QR workflow");
Query("INSERT dbo.RolePermissions(RoleId,PermissionId) SELECT @Role,PermissionId FROM dbo.Permissions WHERE PermissionCode=N'Students.View'",DatabaseHelper.Int("@Role",limitedRole));
var rights=Query("SELECT P.PermissionCode FROM dbo.RolePermissions RP JOIN dbo.Permissions P ON P.PermissionId=RP.PermissionId WHERE RP.RoleId=@Role",DatabaseHelper.Int("@Role",limitedRole))
    .AsEnumerable().Select(r=>Convert.ToString(r[0])!).ToHashSet(StringComparer.OrdinalIgnoreCase);
Assert(QuickRegistrationAccess.CanUse(rights.Contains),"role with actual Admit and View permissions can use QR");
var limitedList=Query(AdmissionReviewSql.Query,DatabaseHelper.Int("@UserId",limitedUser),DatabaseHelper.Bit("@Pending",true));
Assert(limitedList.AsEnumerable().Any(r=>Convert.ToInt32(r["AdmissionId"])==admission),"limited role with View can query pending QR");
// Make a non-QR Pending fixture inside a transaction, test the production query,
// then roll it back so the valid registration can exercise the remaining saves.
using(var c=DatabaseHelper.GetConnection()){
    c.Open();using var transaction=c.BeginTransaction();
    using(var change=new SqlCommand("UPDATE dbo.Students SET QuickReference=NULL WHERE AdmissionId=@Id",c,transaction)){
        change.Parameters.Add(DatabaseHelper.Int("@Id",admission));change.ExecuteNonQuery();
    }
    using var query=new SqlCommand(AdmissionReviewSql.Query,c,transaction);
    query.Parameters.AddRange(new[]{DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Bit("@Pending",true)});
    var nonQr=new DataTable();using(var adapter=new SqlDataAdapter(query))adapter.Fill(nonQr);
    Assert(!nonQr.AsEnumerable().Any(r=>Convert.ToInt32(r["AdmissionId"])==admission),"production pending list excludes non-QR Pending fixture");
    transaction.Rollback();
}
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
async Task<int> NewChild(int? sibling=null)
{
    var current=Rates();var parameters=AdmissionArgs(current);
    parameters.Single(p=>p.ParameterName=="@FamilyStudentId").Value=(object?)sibling??DBNull.Value;
    var result=await QuickRegistrationWriter.SaveNewAsync(placement,QuickRegistrationQuote.Create(current),parameters);
    return Convert.ToInt32(result.Tables.Cast<DataTable>().Last(t=>t.Columns.Contains("AdmissionId")&&t.Rows.Count>0).Rows[0]["AdmissionId"]);
}
int anchor=await NewChild();
var selectable=Query("EXEC dbo.GetStudents @UserId=@UserId,@CampusId=0,@AcademicYearId=0,@IncludeFormerStudents=1;",DatabaseHelper.Int("@UserId",CurrentUser.UserId));
Assert(selectable.AsEnumerable().Any(r=>Convert.ToInt32(r["AdmissionId"])==anchor&&Convert.ToString(r["Status"])=="Pending"&&Convert.ToString(r["AdmissionNumber"])!.StartsWith("QR-")),
    "parent picker query includes first-time pending children with QR references before confirmation");
var single=await QuickRegistrationWriter.LoadAsync(admission);
Assert((int)single.Details.Rows[0]["FamilyMemberId"]==0&&(int)single.Details.Rows[0]["FamilyMemberCount"]==0,"standalone QR has no invented family membership");
string FinancialState()=>Convert.ToString(Query("""
    SELECT CONCAT((SELECT S.* FROM dbo.Students S WHERE AdmissionId=@Id FOR JSON PATH,INCLUDE_NULL_VALUES),
        (SELECT E.* FROM dbo.Enrollments E WHERE AdmissionId=@Id ORDER BY EnrollmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
        (SELECT A.* FROM dbo.StudentFeeAssignments A WHERE AdmissionId=@Id ORDER BY StudentFeeAssignmentId FOR JSON PATH,INCLUDE_NULL_VALUES),
        (SELECT B.* FROM dbo.BillingCharges B WHERE AdmissionId=@Id ORDER BY BillingChargeId FOR JSON PATH,INCLUDE_NULL_VALUES),
        (SELECT P.* FROM dbo.FeePayments P WHERE AdmissionId=@Id ORDER BY FeePaymentId FOR JSON PATH,INCLUDE_NULL_VALUES));
    """,DatabaseHelper.Int("@Id",admission)).Rows[0][0])!;
string financialBefore=FinancialState();
await QuickRegistrationWriter.ChangeFamilyAsync(admission,single.Snapshot,anchor);
var linked=await QuickRegistrationWriter.LoadAsync(admission);
Assert((int)linked.Details.Rows[0]["FamilyMemberId"]>0&&(int)linked.Details.Rows[0]["FamilyMemberCount"]==2,"saved pending QR can link an existing parent");
Assert(FinancialState()==financialBefore,"linking retains all student details, enrollments, fees, charges and payments");
await Refused(()=>QuickRegistrationWriter.ChangeFamilyAsync(admission,single.Snapshot,null),"stale screen cannot unlink a new family membership");
await Refused(()=>QuickRegistrationWriter.SavePendingAsync(admission,single.Snapshot,Edits(single,"Stale family edit","03007654321")),"family changes invalidate pending student edits");
int third=await NewChild(anchor);
var withThird=await QuickRegistrationWriter.LoadAsync(admission);
Assert((int)withThird.Details.Rows[0]["FamilyMemberCount"]==3,"link during new QR joins the same existing family");
await Refused(()=>QuickRegistrationWriter.ChangeFamilyAsync(admission,linked.Snapshot,null),"another child's family change invalidates unlink snapshot");
int otherAnchor=await NewChild();int otherChild=await NewChild(otherAnchor);
try{await QuickRegistrationWriter.ChangeFamilyAsync(admission,withThird.Snapshot,otherAnchor);throw new Exception("Different families were merged");}
catch(SqlException e) when(e.Number==56643){Assert(true,"cross-family merge is refused by the existing SQL safeguard");}
Assert((await QuickRegistrationWriter.LoadAsync(admission)).Snapshot==withThird.Snapshot,"refused cross-family link rolls back without membership changes");
int actor=CurrentUser.UserId;
try{
    CurrentUser.UserId=limitedUser;
    try{await QuickRegistrationWriter.ChangeFamilyAsync(admission,withThird.Snapshot,null);throw new Exception("Admission/view role unlinked a parent");}
    catch(SqlException e) when(e.Number==50003){Assert(true,"parent unlink requires Students.Administer in SQL");}
}finally{CurrentUser.UserId=actor;}
await QuickRegistrationWriter.ChangeFamilyAsync(admission,withThird.Snapshot,null);
var unlinked=await QuickRegistrationWriter.LoadAsync(admission);
Assert((int)unlinked.Details.Rows[0]["FamilyMemberId"]==0&&(int)unlinked.Details.Rows[0]["FamilyMemberCount"]==0,"unlink removes only this child's membership");
Assert((int)(await QuickRegistrationWriter.LoadAsync(anchor)).Details.Rows[0]["FamilyMemberCount"]==2,"unlink retains the other family members");
Assert(FinancialState()==financialBefore,"unlink retains student and financial history exactly");
await QuickRegistrationWriter.ChangeFamilyAsync(admission,unlinked.Snapshot,anchor);
Assert((int)(await QuickRegistrationWriter.LoadAsync(admission)).Details.Rows[0]["FamilyMemberCount"]==3,"a saved QR can relink after unlinking");
Query("UPDATE dbo.Students SET Gender=N'Male',PlaceOfBirth=N'Audit city' WHERE AdmissionId=@Id; EXEC dbo.CompleteQuickAdmission @UserId,@Id;",DatabaseHelper.Int("@Id",admission),DatabaseHelper.Int("@UserId",CurrentUser.UserId));
var active=await QuickRegistrationWriter.LoadAsync(admission);
await QuickRegistrationWriter.ChangeFamilyAsync(admission,active.Snapshot,null);
var activeUnlinked=await QuickRegistrationWriter.LoadAsync(admission);
await QuickRegistrationWriter.ChangeFamilyAsync(admission,activeUnlinked.Snapshot,anchor);
Assert((int)(await QuickRegistrationWriter.LoadAsync(admission)).Details.Rows[0]["FamilyMemberCount"]==3,"link and unlink remain available after admission confirmation");
Query("UPDATE dbo.Students SET Status=N'Cancelled' WHERE AdmissionId=@Id",DatabaseHelper.Int("@Id",admission));
var cancelled=await QuickRegistrationWriter.LoadAsync(admission);
try{await QuickRegistrationWriter.ChangeFamilyAsync(admission,cancelled.Snapshot,null);throw new Exception("Cancelled QR was unlinked");}
catch(InvalidOperationException e) when(e.Message.Contains("pending or active")){Assert(true,"cancelled students cannot change parent links from Quick Registration");}
Console.WriteLine($"PASS: {checks} production SQL checks. Fixture remains only in the disposable clone; drop that clone after inspection.");
