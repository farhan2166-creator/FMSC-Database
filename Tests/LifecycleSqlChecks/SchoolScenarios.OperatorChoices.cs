using Microsoft.Data.SqlClient;

internal sealed partial class SchoolScenarios
{
    private (int Student,int Enrollment) EarlierChild(string name)
    {
        var row=Sql.Proc("AdmitStudent",f.AdmissionArgs(name)).Rows[0];
        return(Convert.ToInt32(row["AdmissionId"]),Convert.ToInt32(row["EnrollmentId"]));
    }
    private void PostMonth(int enrollment,DateTime month)=>Sql.Proc("PostSelectedMonthlyFees",
        Sql.P("@UserId",f.Admin),Sql.P("@BillingMonth",month),Sql.P("@AcademicYearId",f.SourceYear),Sql.P("@Reason","Fictional agreed monthly bill"),
        Sql.TvpParameter("@Selection","PostingSelectionType",Tvp.PostingSelections(new[]{(enrollment,f.Tuition,month)})));
    private long Charge(int enrollment,DateTime month)=>Convert.ToInt64(Sql.Scalar("SELECT BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=@E AND FeeTypeId=@F AND BillingMonth=@M AND CorrectionOfBillingChargeId IS NULL;",Sql.P("@E",enrollment),Sql.P("@F",f.Tuition),Sql.P("@M",month)));
    private int Assignment(int enrollment)=>Sql.Count("SELECT TOP(1) StudentFeeAssignmentId FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@E AND FeeTypeId=@F AND CancelledAt IS NULL ORDER BY StudentFeeAssignmentId;",Sql.P("@E",enrollment),Sql.P("@F",f.Tuition));
    private decimal ChainDue(long charge)=>Sql.Money("SELECT SUM(dbo.GetFeeChargeBalance(BillingChargeId)) FROM dbo.BillingCharges WHERE (BillingChargeId=@C OR CorrectionOfBillingChargeId=@C) AND Status<>N'Cancelled';",Sql.P("@C",charge));
    private DataSet ReviewConcession(params SqlParameter[] args)
    {
        using var connection=DatabaseHelper.GetConnection();connection.Open();
        using var command=new SqlCommand("SaveStudentFeeDiscount",connection){CommandType=CommandType.StoredProcedure};command.Parameters.AddRange(args);
        var result=new DataSet();using var adapter=new SqlDataAdapter(command);adapter.Fill(result);return result;
    }
    private SqlParameter[] ConcessionArgs(int assignment,DateTime from,DateTime through,decimal discount,bool bills,bool preview,string? snapshot=null,int? user=null)=>new[]{
        Sql.P("@UserId",user??f.Admin),Sql.P("@StudentFeeAssignmentId",assignment),Sql.P("@DiscountAmount",discount),Sql.P("@ConcessionLabel",discount>0?"Scholarship":null),
        Sql.P("@EffectiveFrom",from),Sql.P("@EffectiveThrough",through),Sql.P("@ApplyToBills",bills),Sql.P("@PaidReductionAction","Credit"),Sql.P("@Reason","Fictional school-authorized concession"),
        Sql.P("@Preview",preview),Sql.P("@ExpectedSnapshot",snapshot)};
    private static string Snapshot(DataSet data)=>Convert.ToString(data.Tables[0].Rows[0]["Snapshot"])!;
    private Task AdmissionConcessionChoice()
    {
        foreach(bool immediate in new[]{true,false})
        {
            var args=f.AdmissionArgs("Admission concession choice "+immediate).Concat(new[]{Sql.P("@MonthlyDiscountFrom",new DateTime(f.AdmissionDate.Year,4,1).AddMonths(immediate?0:1))}).ToArray();
            args.Single(p=>p.ParameterName=="@AdmissionDiscounts").Value=Tvp.AdmissionFeeDiscounts(new[]{(f.Tuition,1000m)});
            int enrollment=Convert.ToInt32(Sql.Proc("AdmitStudent",args).Rows[0]["EnrollmentId"]);
            r.Check(Due(Tuition(enrollment))==(immediate?4000:5000),"The school explicitly chooses admission-month or following-month concessions for the opening bill.");
        }
        return Task.CompletedTask;
    }
    private async Task ConcessionChoices()
    {
        var child=EarlierChild("Temporary scholarship example");int id=Assignment(child.Enrollment);
        DateTime may=f.MonthlyDate,june=may.AddMonths(1),july=june.AddMonths(1);
        // A pre-agreed future rate is fixture context; the concession must retain it.
        Sql.Query("""
            UPDATE dbo.StudentFeeAssignments SET EffectiveTo=@May WHERE StudentFeeAssignmentId=@Id;
            INSERT dbo.StudentFeeAssignments(EnrollmentId,AdmissionId,FeeTypeId,Amount,DiscountAmount,Frequency,EffectiveFrom)
            VALUES(@E,@S,@F,6000,0,N'Monthly',@June);
            """,Sql.P("@May",may),Sql.P("@June",june),Sql.P("@Id",id),Sql.P("@E",child.Enrollment),Sql.P("@S",child.Student),Sql.P("@F",f.Tuition));
        PostMonth(child.Enrollment,may);PostMonth(child.Enrollment,june);
        long first=Charge(child.Enrollment,may),second=Charge(child.Enrollment,june);
        await Pay(child.Student,child.Enrollment,4500,(first,4500));
        using var review=ReviewConcession(ConcessionArgs(id,may,june,1000,true,true));
        r.Check(review.Tables.Count==3&&Convert.ToDecimal(review.Tables[0].Rows[0]["Difference"])==-2000&&Convert.ToDecimal(review.Tables[0].Rows[0]["ReleasedPaidAmount"])==500,
            "Concession review shows both bill reductions and only the paid difference to release.");
        var args=ConcessionArgs(id,may,june,1000,true,false,Snapshot(review));
        var intent=new MutationIntent(Guid.NewGuid(),MutationKind.FeeConcession,f.Admin,await RecoverableMutationWriter.GetScopeAsync(),
            RecoverableMutationWriter.PayloadHash(MutationKind.FeeConcession,f.Admin,args),"Fictional concession recovery",DateTime.UtcNow);
        var outcome=await RecoverableMutationWriter.SaveAsync(intent,args,t=>{t.Commit();throw new IOException("Synthetic lost acknowledgement");});
        r.Check(outcome.Outcome==MutationOutcome.Unconfirmed,"Lost concession acknowledgement remains unresolved until checked.");
        var recovered=await RecoverableMutationWriter.CheckAsync(intent);
        r.Check(recovered?.Amount==-2000&&recovered.ReleasedPaidAmount==500&&recovered.EffectiveThrough==june,
            "Read-only recovery finds the exact saved period and financial result.");
        int corrections=Sql.Count("SELECT COUNT(*) FROM dbo.FeeBillCorrections;");
        r.Check((await RecoverableMutationWriter.SaveAsync(intent,args)).Outcome==MutationOutcome.Saved&&Sql.Count("SELECT COUNT(*) FROM dbo.FeeBillCorrections;")==corrections,
            "Exact concession retry returns its saved result without another correction or credit.");
        r.Check(ChainDue(first)==0&&ChainDue(second)==5000&&Credit(child.Student)==500,
            "Partial payment consumes unpaid dues first and releases only 500 as credit; the other bill remains 5000 due.");
        r.Check(Sql.Count("SELECT COUNT(*) FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@E AND FeeTypeId=@F AND EffectiveFrom=@July AND EffectiveTo IS NULL AND Amount=6000 AND DiscountAmount=0;",Sql.P("@E",child.Enrollment),Sql.P("@F",f.Tuition),Sql.P("@July",july))==1,
            "After the temporary concession, the previously agreed 6000 rate and zero concession resume automatically.");
        r.Check(Due(Tuition(child.Enrollment))==5000&&Sql.Money("SELECT SUM(TotalAmount) FROM dbo.FeePayments WHERE AdmissionId=@S AND IsCreditApplication=0;",Sql.P("@S",child.Student))==4500,
            "Earlier tuition and cash received are preserved.");
        using var remove=ReviewConcession(ConcessionArgs(id,may,june,0,true,true));
        Sql.Proc("SaveStudentFeeDiscount",ConcessionArgs(id,may,june,0,true,false,Snapshot(remove)));
        r.Check(Sql.Money("SELECT SUM(NetAmount) FROM dbo.BillingCharges WHERE (BillingChargeId=@C OR CorrectionOfBillingChargeId=@C) AND Status<>N'Cancelled';",Sql.P("@C",first))==5000,
            "Removing a concession restores the agreed liability through an explicit correction chain.");
    }
    private async Task ConcessionRefusals()
    {
        var child=EarlierChild("Concession stale and rollback example");int id=Assignment(child.Enrollment);
        DateTime month=new(f.AdmissionDate.Year,f.AdmissionDate.Month,1);long bill=Tuition(child.Enrollment);
        using var review=ReviewConcession(ConcessionArgs(id,month,month,1000,true,true));
        await Pay(child.Student,child.Enrollment,1000,(bill,1000));
        await r.Refused("A payment after concession review prevents saving a stale financial decision",()=>Task.Run(()=>Sql.Proc("SaveStudentFeeDiscount",ConcessionArgs(id,month,month,1000,true,false,Snapshot(review)))),53104);
        int before=Sql.Count("SELECT COUNT(*) FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@E;",Sql.P("@E",child.Enrollment));
        using var current=ReviewConcession(ConcessionArgs(id,month,month,1000,true,true));
        Sql.Query("CREATE TRIGGER dbo.FMSC_OperatorConcessionRefusal ON dbo.FeeBillCorrections AFTER INSERT AS THROW 51044,'Synthetic concession refusal',1;");
        try{await r.Refused("A correction failure rolls back concession schedule changes and financial changes together",()=>Task.Run(()=>Sql.Proc("SaveStudentFeeDiscount",ConcessionArgs(id,month,month,1000,true,false,Snapshot(current)))),51044);}
        finally{Sql.Query("DROP TRIGGER dbo.FMSC_OperatorConcessionRefusal;");}
        r.Check(Sql.Count("SELECT COUNT(*) FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@E;",Sql.P("@E",child.Enrollment))==before&&Due(bill)==4000,
            "Failed concession leaves the exact original schedule and unpaid balance.");
        await r.Refused("An operator without fee-adjustment authority cannot preview an override",()=>Task.Run(()=>ReviewConcession(ConcessionArgs(id,month,month,1000,true,true,user:f.Restricted))),50003);
        using var assignmentOnly=ReviewConcession(ConcessionArgs(id,month,month,1000,false,true));
        Sql.Proc("SaveStudentFeeDiscount",ConcessionArgs(id,month,month,1000,false,false,Snapshot(assignmentOnly)));
        r.Check(Due(bill)==4000&&Credit(child.Student)==0,"The explicit leave-issued-bills-unchanged choice creates no waiver, payment or credit.");
    }
    private async Task LeavingChoices()
    {
        foreach(string policy in new[]{"KeepIssued","SchoolCutoff","FromMonth"})
        {
            var child=EarlierChild("Leaving policy example "+policy);DateTime may=f.MonthlyDate,june=may.AddMonths(1);
            PostMonth(child.Enrollment,may);PostMonth(child.Enrollment,june);long juneBill=Charge(child.Enrollment,june);
            Rate(juneBill,6000);await Pay(child.Student,child.Enrollment,5500,(juneBill,5000),(Convert.ToInt64(Sql.Scalar("SELECT BillingChargeId FROM dbo.BillingCharges WHERE CorrectionOfBillingChargeId=@C;",Sql.P("@C",juneBill))),500));
            SqlParameter[] Args(bool preview,string? snapshot=null)=>new[]{Sql.P("@UserId",f.Admin),Sql.P("@AdmissionId",child.Student),Sql.P("@LeavingDate",may.AddDays(4)),Sql.P("@Reason","Fictional reviewed departure"),
                Sql.P("@MonthlyFeePolicy",policy),Sql.P("@ReverseFromMonth",policy=="FromMonth"?june:null),Sql.P("@PaidReductionAction","Credit"),Sql.P("@Preview",preview),Sql.P("@ExpectedSnapshot",snapshot)};
            var plan=Sql.Proc("CloseStudentAdmission",Args(true)).Rows[0];
            r.Check(Convert.ToDecimal(plan["FutureAmount"])==(policy=="KeepIssued"?0:policy=="FromMonth"?500:5500)&&Convert.ToDecimal(plan["PaidReturnAmount"])==(policy=="KeepIssued"?0:5500),
                "Leaving preview reconciles original bills, rate differences and payments for the chosen policy.");
            Sql.Proc("CloseStudentAdmission",Args(false,Convert.ToString(plan["Snapshot"])));
            r.Check(Convert.ToString(Sql.Scalar("SELECT Status FROM dbo.Enrollments WHERE EnrollmentId=@E;",Sql.P("@E",child.Enrollment)))=="Left"&&
                ChainDue(juneBill)==(policy=="KeepIssued"?500:0)&&Credit(child.Student)==(policy=="KeepIssued"?0:5500),
                "Departure stops the placement while preserving or reconciling issued fees exactly as selected.");
            r.Check(ChainDue(Charge(child.Enrollment,may))==(policy=="SchoolCutoff"?0:5000),"Keeping May or waiving from June preserves the agreed May bill.");
            await r.Refused("A repeated departure cannot issue the same credit twice",()=>Task.Run(()=>Sql.Proc("CloseStudentAdmission",Args(false,Convert.ToString(plan["Snapshot"])))),59712);
        }
    }
}
