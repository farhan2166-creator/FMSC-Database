using System.Data;
using System.Reflection;
using FMSC_SMS;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Security;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using FMSC_SMS.Pages.Finance.Collection;
using Microsoft.Reporting.WinForms;

internal static partial class Program
{
    private static async Task CheckFeeAccountSqlAsync()
    {
        await SeedFeeAccountAsync();
        int admin=Convert.ToInt32((await DatabaseHelper.QuerySqlAsync("SELECT UserId FROM dbo.Users WHERE Username=N'level2_admin';")).Rows[0][0]);
        CurrentUser.SignIn(admin,"Fictional account owner","Administrator",false,Array.Empty<string>());
        using var originals=await DatabaseHelper.QuerySqlAsync("""
            SELECT P.FeePaymentId,P.AdmissionId,P.EnrollmentId,R.ReceiptNumber FROM dbo.CollectionReceipts R
            JOIN dbo.CollectionReceiptPayments L ON L.CollectionReceiptId=R.CollectionReceiptId
            JOIN dbo.FeePayments P ON P.FeePaymentId=L.FeePaymentId
            WHERE R.SnapshotVersion>=2 AND P.TotalAmount>=400 AND P.Status=N'Completed' AND P.IsCreditApplication=0
            ORDER BY P.FeePaymentId;
            """);
        Check(originals.Rows.Count>=2,"Fictional family receipt has two original cash payments");
        int first=Convert.ToInt32(originals.Rows[0]["AdmissionId"]),second=Convert.ToInt32(originals.Rows[1]["AdmissionId"]);
        string receipt=Convert.ToString(originals.Rows[0]["ReceiptNumber"])!;
        async Task<string> SnapshotHash()=>Convert.ToString((await DatabaseHelper.QuerySqlAsync("SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',SnapshotJson),2) Hash FROM dbo.CollectionReceipts WHERE ReceiptNumber=@Receipt;",DatabaseHelper.P("@Receipt",receipt))).Rows[0][0])!;
        string before=await SnapshotHash();
        var account=await StudentFeeAccountData.ReadAsync(first);
        Check(account.Outstanding>0&&account.Credit==0&&account.Receipts.Rows.Count>0,"Actual account reader retains partial-payment outstanding and separate zero credit");
        using(var linked=await StudentFeeAccountData.ReadReceiptPaymentsAsync(receipt))
            Check(linked.AsEnumerable().Select(row=>StudentFeeAccountData.Number(row,"AccountAdmissionId")).Distinct().Count()>=2,"Original saved family links supply both children's correction context");
        await DatabaseHelper.QueryUserAsync("ProcessStudentCredit",DatabaseHelper.Int("@AdmissionId",first),DatabaseHelper.P("@Action","Deposit"),DatabaseHelper.P("@Amount",150m),DatabaseHelper.P("@RequestId",Guid.NewGuid()),DatabaseHelper.P("@Reason","Fictional account credit"),DatabaseHelper.P("@TargetAdmissionId",null));
        var credit=await StudentFeeAccountData.ReadAsync(first);
        Check(credit.Credit==150m&&credit.Outstanding==account.Outstanding&&credit.CreditEntries!.Rows.Count>0,"Deposit appears as credit history without reducing outstanding dues");
        string scope=await RecoverableMutationWriter.GetScopeAsync();
        SqlParameter[] Args(DataRow original,string operation,decimal amount)=>new[]{
            DatabaseHelper.Int("@AdmissionId",Convert.ToInt32(original["AdmissionId"])),DatabaseHelper.Int("@EnrollmentId",Convert.ToInt32(original["EnrollmentId"])),
            DatabaseHelper.P("@TransactionType",operation),DatabaseHelper.P("@Amount",amount),DatabaseHelper.Date("@TransactionDate",DateTime.Today),
            DatabaseHelper.P("@PaymentMethod",null),DatabaseHelper.P("@ReferenceNumber",null),DatabaseHelper.P("@Remarks","Fictional account regression"),
            DatabaseHelper.Int("@FeePaymentId",Convert.ToInt32(original["FeePaymentId"])),new SqlParameter("@BillingChargeId",SqlDbType.BigInt){Value=DBNull.Value},
            new SqlParameter("@Allocations",SqlDbType.Structured){TypeName="dbo.FeePaymentAllocationType",Value=Tvp.FeeAllocations(Array.Empty<(long,decimal)>())}};
        MutationIntent Intent(SqlParameter[] parameters)=>new(Guid.NewGuid(),MutationKind.FeeAdjustment,admin,scope,RecoverableMutationWriter.PayloadHash(MutationKind.FeeAdjustment,admin,parameters),"Fictional account regression",DateTime.UtcNow);
        var reduction=Args(originals.Rows[0],"Correction",500m);
        var reduced=await RecoverableMutationWriter.SaveAsync(Intent(reduction),reduction);
        Check(reduced.Outcome==MutationOutcome.Saved,"Existing recoverable writer records a payment reduction");
        var refund=Args(originals.Rows[0],"Refund",100m);var refundIntent=Intent(refund);
        var lost=await RecoverableMutationWriter.SaveAsync(refundIntent,refund,t=>{t.Commit();throw new IOException("Injected lost acknowledgement");});
        Check(lost.Outcome==MutationOutcome.Unconfirmed,"Committed refund with lost response stays explicitly unconfirmed");
        var verified=await RecoverableMutationWriter.CheckAsync(refundIntent);
        Check(verified is not null,"Existing recovery reader resolves the exact committed refund");
        var replay=await RecoverableMutationWriter.SaveAsync(refundIntent,refund);
        Check(replay.Outcome==MutationOutcome.Saved&&replay.Result!.RecordId==verified!.RecordId,"Repeating the same request returns its existing result");
        var reverse=Args(originals.Rows[1],"Reversal",400m);
        Check((await RecoverableMutationWriter.SaveAsync(Intent(reverse),reverse)).Outcome==MutationOutcome.Saved,"Existing writer reverses the other child's untouched payment");
        using(var changes=await StudentFeeAccountData.ReadReceiptPaymentsAsync(receipt))
        {
            Check(changes.AsEnumerable().Any(row=>StudentFeeAccountData.Text(row,"PaymentMethod")=="Refund")&&changes.AsEnumerable().Any(row=>StudentFeeAccountData.Text(row,"PaymentMethod")=="Correction"),"Receipt later-changes view returns linked refund and correction records");
            var original=changes.AsEnumerable().Single(row=>StudentFeeAccountData.Number(row,"FeePaymentId")==Convert.ToInt32(originals.Rows[1]["FeePaymentId"]));
            Check(StudentFeeAccountData.Eligibility(original).Actions.Length==0,"Reversed receipt payment has no further correction action");
            Check(changes.AsEnumerable().Count(row=>StudentFeeAccountData.Text(row,"PaymentMethod")=="Refund")==1,"Lost-response recovery does not duplicate the refund");
        }
        Check(before==await SnapshotHash(),"Corrections and credit leave the original receipt JSON byte-for-byte unchanged");
        using(var saved=await DatabaseHelper.QueryUserSetAsync("GetCollectionReceipt",DatabaseHelper.P("@ReceiptNumber",receipt)))
        {
            using var printable=FeeReceiptDocuments.BuildData(new InstitutionProfile.PrintIdentity("Fictional school","",""),saved.Tables[0].Rows[0],saved.Tables[1],saved.Tables[2]);
            Check(printable.Rows.Count>0,"Original saved receipt remains printable after financial corrections");
            using var prepared=FamilyFeeDocumentPresentation.Receipt(printable);
            using var report=new LocalReport();
            string template=Path.Combine(AppContext.BaseDirectory,"Reports",FamilyReceiptReport.TemplateFile);
            report.LoadReportDefinition(new StringReader(FeeDocumentLayout.SizeToContent(template,prepared)));
            report.DataSources.Add(new ReportDataSource(FamilyReceiptReport.DataSetName,prepared));
            byte[] pdf=report.Render("PDF");Check(pdf.Length>1000,"Original family receipt renders to PDF after refund and reversal");
            var target=typeof(ApplicationWindowForm).Assembly.GetType("FMSC_SMS.Core.Level2Target")!;
            string root=(string)target.GetProperty("Root",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
            Directory.CreateDirectory(Path.Combine(root,"counter-evidence"));File.WriteAllBytes(Path.Combine(root,"counter-evidence","account-original-receipt.pdf"),pdf);
        }
        using(var due=await DatabaseHelper.QuerySqlAsync("SELECT TOP(1) BillingChargeId,EnrollmentId,dbo.GetFeeChargeBalance(BillingChargeId) Due FROM dbo.BillingCharges WHERE AdmissionId=@Id AND Status<>N'Cancelled' AND dbo.GetFeeChargeBalance(BillingChargeId)>100 ORDER BY BillingChargeId;",DatabaseHelper.Int("@Id",first)))
        {
            long charge=Convert.ToInt64(due.Rows[0]["BillingChargeId"]);int enrollment=Convert.ToInt32(due.Rows[0]["EnrollmentId"]);decimal balance=Convert.ToDecimal(due.Rows[0]["Due"]);
            decimal outstanding=await FeeReceiptQueries.QueryCurrentBalanceAsync(first);
            var creditEdit=new CollectionDiscountWriter.CreditEdit(charge,first,enrollment,50m,balance,150m);
            var creditLine=new CollectionDiscountWriter.ReceiptLine(charge,first,enrollment,balance,0,0,50m);
            var draft=new CollectionDiscountWriter.ReceiptDraft(first,new[]{first},DateTime.Today,"Account Credit","Fictional credit application",0,0,new[]{creditLine});
            using var application=await CollectionDiscountWriter.CollectWithReceiptAsync(null,Array.Empty<CollectionDiscountWriter.Edit>(),new[]{creditEdit},DateTime.Today,"Fictional credit application",draft);
            var afterCredit=await StudentFeeAccountData.ReadAsync(first);
            Check(afterCredit.Credit==100m&&afterCredit.Outstanding==outstanding-50m,"Existing credit writer reduces dues and credit by the applied amount without recording cash");
            var creditPayment=afterCredit.Payments.AsEnumerable().First(row=>row["IsCreditEntry"] is true);
            Check(StudentFeeAccountData.Eligibility(creditPayment).Actions.Length==0,"Actual credit application cannot use cash correction actions");
        }
        using var entries=await DatabaseHelper.QueryUserSetAsync("GetFeeStatementTransactions",DatabaseHelper.Int("@AdmissionId",first),DatabaseHelper.Date("@FromDate",null),DatabaseHelper.Date("@ToDate",null),DatabaseHelper.Bit("@IncludeOpeningBalance",true));
        Check(entries.Tables.Count==2&&entries.Tables[0].AsEnumerable().Any(row=>Convert.ToString(row["PaymentMethod"])=="Refund"),"Unchanged statement reader includes the saved refund transaction");
        CurrentUser.SignIn(int.MaxValue,"No access","Operator",false,Array.Empty<string>());
        bool denied=false;try{await StudentFeeAccountData.ReadAsync(first);}catch(SqlException ex) when(ex.Number==50003){denied=true;}
        Check(denied,"Account reader independently enforces SQL permissions");
    }

    private static async Task SeedFeeAccountAsync()
    {
        var target=typeof(ApplicationWindowForm).Assembly.GetType("FMSC_SMS.Core.Level2Target")
            ??throw new InvalidOperationException("Account SQL tests require the owned Level2 binary.");
        target.GetMethod("ValidateSchema",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null);
        string root=(string)target.GetProperty("Root",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"credentials.json")));
        int Ref(string key)=>fixture.RootElement.GetProperty(key).GetInt32();
        int admin=Convert.ToInt32((await DatabaseHelper.QuerySqlAsync("SELECT UserId FROM dbo.Users WHERE Username=N'level2_admin';")).Rows[0][0]);
        CurrentUser.SignIn(admin,"Fictional account fixture","Administrator",false,Array.Empty<string>());
        Check(Convert.ToInt32((await DatabaseHelper.QuerySqlAsync("SELECT COUNT(*) FROM dbo.Students;")).Rows[0][0])==0,"Account fixture starts with no school records");
        int placement=Convert.ToInt32((await DatabaseHelper.QuerySqlAsync("SELECT ClassSectionId FROM dbo.CampusClassSections WHERE CampusId=@Campus AND ClassId=@Class AND SectionId=@Section;",DatabaseHelper.Int("@Campus",Ref("campus")),DatabaseHelper.Int("@Class",Ref("sourceClass")),DatabaseHelper.Int("@Section",Ref("section")))).Rows[0][0]);
        async Task<int> Admit(string name,int? family=null)
        {
            await DatabaseHelper.QueryUserAsync("AdmitStudent",
                DatabaseHelper.Date("@AdmissionDate",DateTime.Today),DatabaseHelper.Int("@AdmissionCampusId",Ref("campus")),DatabaseHelper.Int("@AcademicYearId",Ref("nextYear")),DatabaseHelper.Int("@ClassSectionId",placement),
                DatabaseHelper.P("@StudentName",name),DatabaseHelper.P("@FatherName","Fictional Account Parent"),DatabaseHelper.Date("@DateOfBirth",DateTime.Today.AddYears(-10)),
                DatabaseHelper.P("@PlaceOfBirth","Example City"),DatabaseHelper.P("@Gender","Male"),DatabaseHelper.P("@BFormNumber",null),DatabaseHelper.P("@Religion",null),DatabaseHelper.P("@BloodGroup",null),
                DatabaseHelper.P("@ContactNumber",null),DatabaseHelper.P("@AlternateContactNumber",null),DatabaseHelper.P("@Address","Fictional address"),DatabaseHelper.P("@PreviousSchool",null),DatabaseHelper.P("@FatherOccupation",null),DatabaseHelper.P("@Disability","No"),DatabaseHelper.Bit("@UsesTransport",false),
                new SqlParameter("@SelectedFeeTypeIds",SqlDbType.Structured){TypeName="dbo.FeeTypeIdList",Value=Tvp.FeeTypeIds(Array.Empty<int>())},
                new SqlParameter("@AdmissionDiscounts",SqlDbType.Structured){TypeName="dbo.AdmissionFeeDiscountType",Value=Tvp.AdmissionFeeDiscounts(Array.Empty<(int,decimal)>())},
                DatabaseHelper.Bit("@QuickRegistration",false),DatabaseHelper.P("@FamilyStudentId",family));
            return Convert.ToInt32((await DatabaseHelper.QuerySqlAsync("SELECT AdmissionId FROM dbo.Students WHERE StudentName=@Name;",DatabaseHelper.P("@Name",name))).Rows[0][0]);
        }
        int first=await Admit("Account Example One"),second=await Admit("Account Example Two",first);
        using var charges=await DatabaseHelper.QuerySqlAsync("""
            SELECT B.*,dbo.GetFeeChargeBalance(B.BillingChargeId) CurrentDue FROM dbo.BillingCharges B
            WHERE B.AdmissionId IN(@First,@Second) AND B.Status<>N'Cancelled' AND B.DiscountAmount=0 AND dbo.GetFeeChargeBalance(B.BillingChargeId)>1000
            ORDER BY B.AdmissionId,B.BillingChargeId;
            """,DatabaseHelper.Int("@First",first),DatabaseHelper.Int("@Second",second));
        var rows=new[]{first,second}.Select(id=>charges.AsEnumerable().First(row=>Convert.ToInt32(row["AdmissionId"])==id)).ToArray();
        var lines=rows.Select((r,i)=>new CollectionDiscountWriter.ReceiptLine(Convert.ToInt64(r["BillingChargeId"]),Convert.ToInt32(r["AdmissionId"]),Convert.ToInt32(r["EnrollmentId"]),Convert.ToDecimal(r["CurrentDue"])+Convert.ToDecimal(r["DiscountAmount"]),i==0?50m:0m,i==0?600m:400m,0m)).ToArray();
        var edits=new[]{new CollectionDiscountWriter.Edit(lines[0].ChargeId,first,lines[0].EnrollmentId,Convert.ToDecimal(rows[0]["DiscountAmount"]),50m,Convert.ToDecimal(rows[0]["CurrentDue"]))};
        var draft=new CollectionDiscountWriter.ReceiptDraft(first,new[]{first,second},DateTime.Today,"Cash","Fictional account receipt",1000m,50m,lines);
        using var result=await CollectionDiscountWriter.CollectWithReceiptAsync("ProcessFamilyFeeTransaction",edits,Array.Empty<CollectionDiscountWriter.CreditEdit>(),DateTime.Today,"Fictional account collection",draft,
            DatabaseHelper.P("@ReceivedAmount",1000m),DatabaseHelper.Int("@CreditAdmissionId",first),DatabaseHelper.P("@CreditRequestId",Guid.NewGuid()),DatabaseHelper.P("@IncludedStudentsJson",JsonSerializer.Serialize(new[]{first,second})),
            DatabaseHelper.P("@TransactionType","Collection"),DatabaseHelper.Date("@TransactionDate",DateTime.Today),DatabaseHelper.P("@PaymentMethod","Cash"),DatabaseHelper.P("@ReferenceNumber",null),DatabaseHelper.P("@Remarks","Fictional account receipt"),
            new SqlParameter("@Allocations",SqlDbType.Structured){TypeName="dbo.FamilyFeeAllocationType",Value=Tvp.FamilyAllocations(lines.Select(line=>(line.AdmissionId,line.EnrollmentId,(IEnumerable<(long,decimal)>)(line.Discount>0?new[]{(line.ChargeId,line.Cash),(line.ChargeId,-line.Discount)}:new[]{(line.ChargeId,line.Cash)}))))});
        Check(result.Rows.Count>0,"Established family writer creates the two-sibling receipt with a concession and partial payment");
    }
}
