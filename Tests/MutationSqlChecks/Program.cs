using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using Microsoft.Data.SqlClient;

string connectionText=Environment.GetEnvironmentVariable("FMSC_AUDIT_CONNECTION")??throw new Exception("Set the disposable audit connection.");
var builder=new SqlConnectionStringBuilder(connectionText);
if(!builder.InitialCatalog.StartsWith("FMSC_AuditChecks_",StringComparison.Ordinal)||!builder.DataSource.StartsWith("127.0.0.1,",StringComparison.Ordinal)||builder.UserID!="sa")throw new Exception("Only a disposable loopback audit clone is accepted.");
DatabaseHelper.ConnectionString=connectionText;
DataTable Query(string sql,params SqlParameter[] parameters){using var c=DatabaseHelper.GetConnection();c.Open();using var q=new SqlCommand(sql,c){CommandTimeout=120};q.Parameters.AddRange(parameters);var table=new DataTable();using var a=new SqlDataAdapter(q);a.Fill(table);return table;}
int checks=0;void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;Console.WriteLine("PASS: "+label);}
string Resource(string name){using var input=typeof(MutationKind).Assembly.GetManifestResourceStream(name)!;using var reader=new StreamReader(input);return reader.ReadToEnd();}
if(args.Contains("--compatibility")){using var c=DatabaseHelper.GetConnection();c.Open();DatabaseCompatibility.Validate(c);Console.WriteLine("PASS: uploaded schema 6 meets production startup and restore compatibility requirements.");return;}
if(args.Contains("--migration")){
    using var c=DatabaseHelper.GetConnection();c.Open();c.InfoMessage+=(_,e)=>Console.WriteLine(e.Message);
    using var q=new SqlCommand(Resource("Migration6Checks.sql"),c){CommandTimeout=120};q.Parameters.Add(new SqlParameter("@Migration",SqlDbType.NVarChar,-1){Value=Resource("Migration6.sql")});q.ExecuteNonQuery();
    DatabaseCompatibility.Validate(c);Check(true,"startup validates request table, typed columns, result foreign keys, uniqueness and safeguards");
}
CurrentUser.UserId=Convert.ToInt32(Query("SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator'").Rows[0][0]);
int admin=CurrentUser.UserId;
var original=Query("SELECT TOP(1) FeePaymentId,AdmissionId,EnrollmentId FROM dbo.FeePayments WHERE Remarks=N'Audit initial collection' AND Status=N'Completed' ORDER BY FeePaymentId DESC").Rows[0];
int payment=(int)original["FeePaymentId"],admission=(int)original["AdmissionId"],enrollment=(int)original["EnrollmentId"];
SqlParameter[] Refund(decimal amount)=>new[]{DatabaseHelper.Int("@AdmissionId",admission),DatabaseHelper.Int("@EnrollmentId",enrollment),DatabaseHelper.P("@TransactionType","Refund"),DatabaseHelper.P("@Amount",amount),DatabaseHelper.Date("@TransactionDate",DateTime.Today),DatabaseHelper.P("@PaymentMethod",null),DatabaseHelper.P("@ReferenceNumber",null),DatabaseHelper.P("@Remarks","Exact mutation request fixture"),DatabaseHelper.Int("@FeePaymentId",payment),new SqlParameter("@BillingChargeId",SqlDbType.BigInt){Value=DBNull.Value},new SqlParameter("@Allocations",SqlDbType.Structured){TypeName="dbo.FeePaymentAllocationType",Value=Tvp.FeeAllocations(Array.Empty<(long,decimal)>())}};
string scope=await RecoverableMutationWriter.GetScopeAsync();
MutationIntent Intent(MutationKind kind,SqlParameter[] parameters)=>new(Guid.NewGuid(),kind,CurrentUser.UserId,scope,RecoverableMutationWriter.PayloadHash(kind,CurrentUser.UserId,parameters),"Disposable exact operation",DateTime.UtcNow);
int Refunds()=>Convert.ToInt32(Query("SELECT COUNT(*) FROM dbo.FeePayments WHERE RelatedFeePaymentId=@Payment AND PaymentMethod=N'Refund' AND Status=N'Completed'",DatabaseHelper.Int("@Payment",payment)).Rows[0][0]);
if(args.Contains("--bank"))
{
    int staffId=Convert.ToInt32(Query("SELECT TOP(1) StaffId FROM dbo.Staff WHERE StaffName=N'FQA bank fixture' ORDER BY StaffId DESC").Rows[0][0]);
    SqlParameter[] Bank()=>new[]{DatabaseHelper.Int("@StaffId",staffId),DatabaseHelper.P("@Designation","Teacher"),DatabaseHelper.P("@BankName","Audit Bank"),DatabaseHelper.P("@BranchName","Audit Branch"),DatabaseHelper.P("@MonthlyIncome",10000m),DatabaseHelper.P("@Purpose","Payroll"),DatabaseHelper.Date("@CertificateDate",DateTime.Today)};
    int Certificates()=>Convert.ToInt32(Query("SELECT COUNT(*) FROM dbo.BankAccountOpeningCertificates WHERE StaffId=@Staff",DatabaseHelper.Int("@Staff",staffId)).Rows[0][0]);
    var bankArgs=Bank();var bankIntent=Intent(MutationKind.BankCertificateCreate,bankArgs);int count=Certificates();
    var lostBank=await RecoverableMutationWriter.SaveAsync(bankIntent,bankArgs,t=>{t.Commit();throw new IOException("Injected lost bank create acknowledgement");});
    Check(lostBank.Outcome==MutationOutcome.Unconfirmed&&Certificates()==count+1,"actual bank create commit with lost acknowledgement is unresolved with one certificate");
    var found=await RecoverableMutationWriter.CheckAsync(bankIntent);
    Check(found is not null&&found.Kind==MutationKind.BankCertificateCreate&&found.SubjectId==staffId&&found.RecordId>0,"read-only bank recovery returns the authoritative staff and certificate ID");
    Check(Query("SELECT StaffId FROM dbo.BankAccountOpeningCertificates WHERE BankAccountOpeningCertificateId=@ID",DatabaseHelper.Int("@ID",found!.RecordId)).Rows[0][0].Equals(staffId),"the recovered ID identifies that exact persisted staff certificate");
    var again=await RecoverableMutationWriter.SaveAsync(bankIntent,bankArgs);Check(again.Outcome==MutationOutcome.Saved&&again.Result==found&&Certificates()==count+1,"same bank request replay cannot create another certificate");
    var changed=Bank();changed.First(p=>p.ParameterName=="@BankName").Value="Another Bank";
    try{await RecoverableMutationWriter.SaveAsync(bankIntent,changed);throw new Exception("Changed bank request accepted");}catch(InvalidOperationException){Check(true,"changed bank details cannot reuse a completed request identity");}
    var concurrentArgs=Bank();var concurrent=Intent(MutationKind.BankCertificateCreate,concurrentArgs);count=Certificates();
    var both=await Task.WhenAll(RecoverableMutationWriter.SaveAsync(concurrent,concurrentArgs),RecoverableMutationWriter.SaveAsync(concurrent,concurrentArgs));
    Check(both.All(r=>r.Outcome==MutationOutcome.Saved)&&both[0].Result==both[1].Result&&Certificates()==count+1,"concurrent copies of a bank create request produce exactly one certificate");
    var brokenArgs=Bank();var broken=Intent(MutationKind.BankCertificateCreate,brokenArgs);count=Certificates();
    Query("CREATE TRIGGER dbo.FMSC_FailMutationMarker ON dbo.MutationRequests AFTER INSERT AS BEGIN SET NOCOUNT ON;THROW 57699,'Injected marker failure',1;END;");
    try{var failed=await RecoverableMutationWriter.SaveAsync(broken,brokenArgs);Check(failed.Outcome!=MutationOutcome.Saved,"bank marker failure cannot acknowledge successful creation");}
    finally{Query("DROP TRIGGER dbo.FMSC_FailMutationMarker");}
    Check(Certificates()==count&&await RecoverableMutationWriter.CheckAsync(broken) is null,"bank certificate and marker roll back together after marker failure");
    var optional=Bank();optional.First(p=>p.ParameterName=="@MonthlyIncome").Value=DBNull.Value;
    var optionalIntent=Intent(MutationKind.BankCertificateCreate,optional);var optionalSaved=await RecoverableMutationWriter.SaveAsync(optionalIntent,optional);
    Check(optionalSaved.Outcome==MutationOutcome.Saved&&optionalSaved.Result!.Amount==0m,"bank income remains optional through durable creation");
    int accountantId=Convert.ToInt32(Query("SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Accountant'").Rows[0][0]);CurrentUser.UserId=accountantId;
    try{await RecoverableMutationWriter.CheckAsync(bankIntent);throw new Exception("Unauthorized bank recovery accepted");}catch(SqlException e) when(e.Number==50003){Check(true,"bank recovery cannot bypass current certificate permissions");}
    var deniedBankArgs=Bank();var deniedIntent=Intent(MutationKind.BankCertificateCreate,deniedBankArgs);count=Certificates();var deniedBank=await RecoverableMutationWriter.SaveAsync(deniedIntent,deniedBankArgs);
    Check(deniedBank.Outcome==MutationOutcome.Rejected&&Certificates()==count,"Accountant retains its access and cannot acquire bank creation through recovery");CurrentUser.UserId=admin;
    Check(await RecoverableMutationWriter.CheckAsync(deniedIntent) is null,"Administrator can verify a refused bank request without creating it");
    Console.WriteLine($"PASS: {checks} native bank-certificate recovery checks.");return;
}
if(args.Contains("--adjustment-types"))
{
    foreach(string type in new[]{"Correction","Reversal"})
    {
        // Each type gets a fresh original collection: no dependence on a prior refund balance.
        var charge=Query("SELECT TOP(1) B.BillingChargeId FROM dbo.BillingCharges B WHERE B.AdmissionId=@Admission AND B.Status<>N'Cancelled' AND B.GrossAmount-B.DiscountAmount-COALESCE((SELECT SUM(A.Amount) FROM dbo.FeePaymentAllocations A JOIN dbo.FeePayments P ON P.FeePaymentId=A.FeePaymentId WHERE A.BillingChargeId=B.BillingChargeId AND P.Status=N'Completed'),0)>=100 ORDER BY B.BillingChargeId",DatabaseHelper.Int("@Admission",admission));
        var collect=Refund(100m);collect.First(p=>p.ParameterName=="@TransactionType").Value="Collection";collect.First(p=>p.ParameterName=="@FeePaymentId").Value=DBNull.Value;collect.First(p=>p.ParameterName=="@PaymentMethod").Value="Cash";collect.First(p=>p.ParameterName=="@Allocations").Value=Tvp.FeeAllocations(new[]{(Convert.ToInt64(charge.Rows[0][0]),100m)});
        Query("EXEC dbo.ProcessFeeTransaction @AdmissionId=@AdmissionId,@EnrollmentId=@EnrollmentId,@TransactionType=@TransactionType,@Amount=@Amount,@TransactionDate=@TransactionDate,@PaymentMethod=@PaymentMethod,@ReferenceNumber=@ReferenceNumber,@Remarks=@Remarks,@FeePaymentId=@FeePaymentId,@BillingChargeId=@BillingChargeId,@Allocations=@Allocations,@UserId=@UserId",collect.Concat(new[]{DatabaseHelper.Int("@UserId",admin)}).ToArray());
        payment=Convert.ToInt32(Query("SELECT TOP(1) FeePaymentId FROM dbo.FeePayments WHERE AdmissionId=@Admission AND PaymentMethod=N'Cash' AND Status=N'Completed' ORDER BY FeePaymentId DESC",DatabaseHelper.Int("@Admission",admission)).Rows[0][0]);
        var change=Refund(type=="Correction"?80m:100m);change.First(p=>p.ParameterName=="@TransactionType").Value=type;var changeIntent=Intent(MutationKind.FeeAdjustment,change);
        var unknown=await RecoverableMutationWriter.SaveAsync(changeIntent,change,t=>{t.Commit();throw new IOException("Injected lost acknowledgement");});Check(unknown.Outcome==MutationOutcome.Unconfirmed,type+" committed with lost acknowledgement remains unresolved");
        var actual=await RecoverableMutationWriter.CheckAsync(changeIntent);Check(actual is not null&&actual.TransactionType==type&&actual.OriginId==payment&&actual.RecordId>0,type+" recovery returns the authoritative saved identity");
        var repeated=await RecoverableMutationWriter.SaveAsync(changeIntent,change);Check(repeated.Outcome==MutationOutcome.Saved&&repeated.Result==actual,type+" replay returns the original result without rerunning SQL");
    }
    Console.WriteLine($"PASS: {checks} native correction/reversal recovery checks.");return;
}
var arguments=Refund(100m);var request=Intent(MutationKind.FeeAdjustment,arguments);int before=Refunds();
var lost=await RecoverableMutationWriter.SaveAsync(request,arguments,t=>{t.Commit();throw new IOException("Injected client lost commit acknowledgement");});
Check(lost.Outcome==MutationOutcome.Unconfirmed&&Refunds()==before+1,"actual committed refund with lost client acknowledgement remains unconfirmed, with one refund");
var recovered=await RecoverableMutationWriter.CheckAsync(request);
Check(recovered is not null&&recovered.TransactionType=="Refund"&&recovered.Amount==100m,"read-only exact request recovery finds the committed refund and receipt");
var replay=await RecoverableMutationWriter.SaveAsync(request,arguments);
Check(replay.Outcome==MutationOutcome.Saved&&replay.Result==recovered&&Refunds()==before+1,"replaying the same request cannot record another refund");
try{await RecoverableMutationWriter.SaveAsync(request,Refund(99m));throw new Exception("Modified request accepted");}catch(InvalidOperationException){Check(true,"changed payload cannot reuse a saved request identity");}
try{await RecoverableMutationWriter.CheckAsync(request with{Scope=new string('F',64)});throw new Exception("Foreign scope accepted");}catch(InvalidOperationException){Check(true,"a different database scope cannot finalize this recovery");}
var parallelArgs=Refund(50m);var parallel=Intent(MutationKind.FeeAdjustment,parallelArgs);before=Refunds();var copies=await Task.WhenAll(RecoverableMutationWriter.SaveAsync(parallel,parallelArgs),RecoverableMutationWriter.SaveAsync(parallel,parallelArgs));
Check(copies.All(x=>x.Outcome==MutationOutcome.Saved)&&copies[0].Result==copies[1].Result&&Refunds()==before+1,"concurrent copies of one refund request execute exactly one business write");
var failArgs=Refund(25m);var fail=Intent(MutationKind.FeeAdjustment,failArgs);before=Refunds();
Query("CREATE TRIGGER dbo.FMSC_FailMutationMarker ON dbo.MutationRequests AFTER INSERT AS BEGIN SET NOCOUNT ON;THROW 57699,'Injected marker failure',1;END;");
try{var failed=await RecoverableMutationWriter.SaveAsync(fail,failArgs);Check(failed.Outcome!=MutationOutcome.Saved,"marker failure cannot produce a successful adjustment acknowledgement");}
finally{Query("DROP TRIGGER dbo.FMSC_FailMutationMarker");}
Check(Refunds()==before&&await RecoverableMutationWriter.CheckAsync(fail) is null,"marker failure atomically rolls back the refund and its recovery record");
var nonexistent=Intent(MutationKind.FeeAdjustment,Refund(10m));Check(await RecoverableMutationWriter.CheckAsync(nonexistent) is null,"a settled absent exact request is verified without replaying financial SQL");
using(var c=DatabaseHelper.GetConnection()){
    c.Open();using var t=c.BeginTransaction();using var held=new SqlCommand("DECLARE @R int;EXEC @R=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction';IF @R<0 THROW 57698,'Lock fixture failed',1;",c,t);held.Parameters.Add(DatabaseHelper.P("@Resource","FMSC.Mutation."+nonexistent.Id.ToString("D")));held.ExecuteNonQuery();
    try{await RecoverableMutationWriter.CheckAsync(nonexistent);throw new Exception("In-flight absence accepted");}catch(SqlException e) when(e.Number==57610){Check(true,"an in-flight request stays unresolved rather than reporting not recorded");}t.Rollback();
}
var accountant=Query("SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Accountant'");
if(accountant.Rows.Count==0){var row=Query("DECLARE @Role int=(SELECT RoleId FROM dbo.Roles WHERE RoleName=N'Accountant');INSERT dbo.Users(Username,RoleId) VALUES(@Name,@Role);DECLARE @User int=SCOPE_IDENTITY();INSERT dbo.UserRoles(UserId,RoleId) VALUES(@User,@Role);SELECT @User",DatabaseHelper.P("@Name","MutationAccountant"+Guid.NewGuid().ToString("N")[..12]));CurrentUser.UserId=(int)row.Rows[0][0];}else CurrentUser.UserId=(int)accountant.Rows[0][0];
try{await RecoverableMutationWriter.CheckAsync(request);throw new Exception("Other operator recovery authorized");}catch(SqlException e) when(e.Number==50003){Check(true,"recovering another operator's request requires Administrator authority");}
var deniedArgs=Refund(10m);var deniedRequest=Intent(MutationKind.FeeAdjustment,deniedArgs);before=Refunds();var denied=await RecoverableMutationWriter.SaveAsync(deniedRequest,deniedArgs);
Check(denied.Outcome==MutationOutcome.Rejected&&Refunds()==before,"Accountant access is preserved and cannot acquire fee-adjustment authority through recovery");
CurrentUser.UserId=admin;
Check(await RecoverableMutationWriter.CheckAsync(deniedRequest) is null,"Administrator can verify a refused operator request as absent without performing it");
using(var compatible=DatabaseHelper.GetConnection()){compatible.Open();DatabaseCompatibility.Validate(compatible);Check(true,"current app compatibility accepts migrated schema 6");}
Console.WriteLine($"PASS: {checks} native request recovery checks on the disposable clone.");
