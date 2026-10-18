using Microsoft.Data.SqlClient;

// Runs only after the caller provisions and validates a disposable synthetic fixture.
internal static class CollectionCreditChecks
{
    internal static async Task Run(Fixture f, Report report)
    {
        var student = Sql.Proc("AdmitStudent", f.AdmissionArgs("Credit settlement example", current:true)).Rows[0];
        int admission=Convert.ToInt32(student["AdmissionId"]), enrollment=Convert.ToInt32(student["EnrollmentId"]);
        var sibling = Sql.Proc("AdmitStudent", f.AdmissionArgs("Sibling settlement example", admission, current:true)).Rows[0];
        int siblingId=Convert.ToInt32(sibling["AdmissionId"]), siblingEnrollment=Convert.ToInt32(sibling["EnrollmentId"]);
        decimal Credit(int id) => Sql.Money("SELECT COALESCE(SUM(Amount),0) FROM dbo.StudentCreditEntries WHERE AdmissionId=@Id",Sql.P("@Id",id));
        decimal Due(long bill) => Sql.Money("SELECT dbo.GetFeeChargeBalance(@Bill)",Sql.P("@Bill",bill));
        void Deposit(int id, decimal amount) => Sql.Proc("ProcessStudentCredit", Sql.P("@UserId",f.Admin),
            Sql.P("@AdmissionId",id),Sql.P("@Action","Deposit"),Sql.P("@Amount",amount),
            Sql.P("@RequestId",Guid.NewGuid()),Sql.P("@Reason","Synthetic credit check"));
        long Bill(int ownerEnrollment,int fee) => Convert.ToInt64(Sql.Scalar("SELECT TOP(1) BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=@E AND FeeTypeId=@F ORDER BY BillingChargeId",Sql.P("@E",ownerEnrollment),Sql.P("@F",fee)));
        CollectionDiscountWriter.CreditEdit Plan(long bill,int owner,int ownerEnrollment,decimal amount) => new(bill,owner,ownerEnrollment,amount,Due(bill),Credit(owner));
        Task<DataTable> Save(string? procedure,CollectionDiscountWriter.CreditEdit[] credits,params SqlParameter[] args) =>
            CollectionDiscountWriter.CollectWithCreditAsync(procedure,Array.Empty<CollectionDiscountWriter.Edit>(),credits,DateTime.Today,"Synthetic credit settlement",args);
        SqlParameter[] Cash(long bill,decimal received,decimal discount=0) => new[] {
            Sql.P("@AdmissionId",admission),Sql.P("@EnrollmentId",enrollment),Sql.P("@TransactionType","Collection"),
            Sql.P("@Amount",received),Sql.P("@TransactionDate",DateTime.Today),Sql.P("@PaymentMethod","Cash"),
            new SqlParameter("@FeePaymentId",SqlDbType.Int) {Value=DBNull.Value},new SqlParameter("@BillingChargeId",SqlDbType.BigInt) {Value=DBNull.Value},
            Sql.P("@ReferenceNumber",""),Sql.P("@Remarks","Synthetic mixed settlement"),
            Sql.TvpParameter("@Allocations","FeePaymentAllocationType",Tvp.FeeAllocations(discount>0 ? new[] {(bill,received),(bill,-discount)} : new[] {(bill,received)}))
        };
        long tuition=Bill(enrollment,f.Tuition), annual=Bill(enrollment,f.AdmissionFee), siblingAnnual=Bill(siblingEnrollment,f.AdmissionFee);
        Deposit(admission,8750); Deposit(siblingId,1000);
        decimal beforeCredit=Credit(admission); decimal beforeDue=Due(tuition);
        await report.Refused("Changed credit is refused before settlement",()=>Save(null,new[] {Plan(tuition,admission,enrollment,100) with { ExpectedCredit=beforeCredit+1 }}),53104);
        report.Check(Credit(admission)==beforeCredit && Due(tuition)==beforeDue,"Stale credit preview changes neither credit nor bills");
        await report.Refused("Sibling credit cannot settle another child's bill",()=>Save(null,new[] {Plan(tuition,siblingId,siblingEnrollment,100)}),53104);
        report.Check(Credit(siblingId)==1000 && Due(tuition)==beforeDue,"Sibling ownership refusal preserves both accounts");
        int payments=Sql.Count("SELECT COUNT(*) FROM dbo.FeePayments");
        bool refused=false;
        try { await Save("ProcessFeeTransaction",new[] {Plan(tuition,admission,enrollment,1000)},Cash(tuition,10000)); }
        catch(SqlException) { refused=true; }
        report.Check(refused && Credit(admission)==beforeCredit && Due(tuition)==beforeDue && Sql.Count("SELECT COUNT(*) FROM dbo.FeePayments")==payments,
            "A rejected cash payment rolls back its preceding credit application and ledger entries");
        await Save("ProcessFeeTransaction",new[] {Plan(tuition,admission,enrollment,1000)},Cash(tuition,3500,500));
        report.Check(Due(tuition)==0 && Credit(admission)==7750,"Cash 3500, credit 1000 and discount 500 settle a 5000 bill exactly");
        report.Check(Sql.Money("SELECT SUM(TotalAmount) FROM dbo.FeePayments WHERE AdmissionId=@Id AND IsCreditApplication=0 AND FeePaymentId NOT IN(SELECT FeePaymentId FROM dbo.StudentCreditEntries WHERE FeePaymentId IS NOT NULL)",Sql.P("@Id",admission))==3500,
            "Using credit and discount does not inflate collected cash");
        var family=await Save("ProcessFamilyFeeTransaction",new[] {Plan(annual,admission,enrollment,500),Plan(siblingAnnual,siblingId,siblingEnrollment,500)},
            Sql.P("@TransactionType","Collection"),Sql.P("@TransactionDate",DateTime.Today),Sql.P("@PaymentMethod","Cash"),Sql.P("@ReferenceNumber",""),Sql.P("@Remarks","Synthetic family settlement"),
            Sql.P("@ReceivedAmount",3000),Sql.P("@CreditAdmissionId",admission),Sql.P("@CreditRequestId",Guid.NewGuid()),Sql.P("@IncludedStudentsJson",System.Text.Json.JsonSerializer.Serialize(new[] {admission,siblingId})),
            Sql.TvpParameter("@Allocations","FamilyFeeAllocationType",Tvp.FamilyAllocations(new[] {(admission,enrollment,(IEnumerable<(long,decimal)>)new[] {(annual,1500m)}),(siblingId,siblingEnrollment,(IEnumerable<(long,decimal)>)new[] {(siblingAnnual,1500m)})})));
        report.Check(Due(annual)==0 && Due(siblingAnnual)==0 && Credit(siblingId)==500,"Family save uses each child's own credit and settles both bills");
        report.Check(family.Rows.Count==2 && family.AsEnumerable().Select(row=>Convert.ToString(row["ReceiptNumber"])).Distinct().Count()==1,"Family cash still has one shared receipt; credit remains a separate noncash movement");
        var month=DateTime.Today.AddMonths(1); month=new DateTime(month.Year,month.Month,1);
        if (month.Year==DateTime.Today.Year)
        {
            Sql.Proc("PostSelectedMonthlyFees",Sql.P("@UserId",f.Admin),Sql.P("@AcademicYearId",f.NextYear),Sql.P("@BillingMonth",month),
                Sql.TvpParameter("@Selection","PostingSelectionType",Tvp.PostingSelections(new[] {(enrollment,f.Tuition,month)})),Sql.P("@Reason","Synthetic advance bill"));
            long future=Convert.ToInt64(Sql.Scalar("SELECT BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=@E AND FeeTypeId=@F AND BillingMonth=@M",Sql.P("@E",enrollment),Sql.P("@F",f.Tuition),Sql.P("@M",month)));
            decimal expectedRemaining = Credit(admission)-5000;
            await Save(null,new[] {Plan(future,admission,enrollment,5000)});
            report.Check(Due(future)==0 && Credit(admission)==expectedRemaining,"Credit-only save settles a posted future bill and preserves the exact remainder");
        }
        report.Check(CollectionRecoveryJournal.ReadPending() is null,"Confirmed and refused transactions clear the recovery intent");
    }
}