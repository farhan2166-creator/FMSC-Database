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
    private SqlParameter[] TermsArgs(int assignment,DateTime from,DateTime through,decimal discount,bool preview,string? snapshot=null,
        decimal? rate=null,bool agreement=true,long? bill=null,bool full=false,string paidAction="Credit")
    {
        var args=ConcessionArgs(assignment,from,through,discount,true,preview,snapshot).ToList();
        args.Single(p=>p.ParameterName=="@PaidReductionAction").Value=paidAction;
        args.AddRange(new[]{Sql.P("@NewAmount",rate),Sql.P("@UpdateAgreement",agreement),Sql.P("@BillingChargeId",bill),Sql.P("@FullFeeWaiver",full)});
        return args.ToArray();
    }
    private DataTable SaveTerms(int assignment,DateTime from,DateTime through,decimal discount,decimal? rate=null,bool agreement=true,long? bill=null,bool full=false,string action="Credit")
    {
        using var review=ReviewConcession(TermsArgs(assignment,from,through,discount,true,rate:rate,agreement:agreement,bill:bill,full:full,paidAction:action));
        return Sql.Proc("SaveStudentFeeDiscount",TermsArgs(assignment,from,through,discount,false,Snapshot(review),rate,agreement,bill,full,action));
    }
    private async Task ReviewedRates()
    {
        var child=EarlierChild("Reviewed fee rate example");int id=Assignment(child.Enrollment);DateTime month=f.MonthlyDate;
        PostMonth(child.Enrollment,month);long bill=Charge(child.Enrollment,month);await Pay(child.Student,child.Enrollment,4500,(bill,4500));
        SaveTerms(id,month,month,500,4500);
        r.Check(ChainDue(bill)==0&&Credit(child.Student)==500,"A lower agreed rate and fixed concession consume unpaid dues before releasing the paid 500.");
        r.Check(Sql.Count("SELECT COUNT(*) FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@E AND FeeTypeId=@F AND EffectiveFrom=@From AND Amount=4500 AND DiscountAmount=500;",
            Sql.P("@E",child.Enrollment),Sql.P("@F",f.Tuition),Sql.P("@From",month))==1,"The reviewed rate applies to the selected agreement period.");
        SaveTerms(id,month,month,1000,5000);
        r.Check(Sql.Money("SELECT TOP(1) NewGross FROM dbo.FeeBillCorrections WHERE BillingChargeId=@C ORDER BY FeeBillCorrectionId DESC;",Sql.P("@C",bill))==5000,
            "A change of gross fee and concession with unchanged net bill still records the new fee basis.");
        SaveTerms(id,month,month,0);
        r.Check(Sql.Money("SELECT SUM(NetAmount) FROM dbo.BillingCharges WHERE BillingChargeId=@C OR CorrectionOfBillingChargeId=@C;",Sql.P("@C",bill))==5000&&ChainDue(bill)==1000&&Credit(child.Student)==500,
            "Later removal uses the reviewed 5000 fee basis and keeps paid credit until its use is explicitly chosen.");
        DateTime june=month.AddMonths(1);SaveTerms(id,june,june,5000,6000,full:false);
        r.Check(Sql.Money("SELECT DiscountAmount FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@E AND FeeTypeId=@F AND EffectiveFrom=@M;",Sql.P("@E",child.Enrollment),Sql.P("@F",f.Tuition),Sql.P("@M",june))==5000,
            "The operator can retain a fixed scholarship while increasing its fee, without an implicit full waiver.");
        SaveTerms(id,june,june,0,6000,full:true);
        r.Check(Sql.Money("SELECT DiscountAmount FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@E AND FeeTypeId=@F AND EffectiveFrom=@M;",Sql.P("@E",child.Enrollment),Sql.P("@F",f.Tuition),Sql.P("@M",june))==6000,
            "An explicit full concession follows the new agreed rate.");
    }
    private async Task AnnualAndPurchaseTerms()
    {
        var child=EarlierChild("Annual and purchase correction example");DateTime month=new(f.AdmissionDate.Year,f.AdmissionDate.Month,1);
        long annual=Convert.ToInt64(Sql.Scalar("SELECT BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=@E AND FeeTypeId=@F;",Sql.P("@E",child.Enrollment),Sql.P("@F",f.AdmissionFee)));
        int annualAssignment=Sql.Count("SELECT StudentFeeAssignmentId FROM dbo.BillingCharges WHERE BillingChargeId=@C;",Sql.P("@C",annual));
        await Pay(child.Student,child.Enrollment,2000,(annual,2000));
        SaveTerms(annualAssignment,month,month,0,1500,agreement:false,bill:annual,action:"Refund");
        r.Check(ChainDue(annual)==0&&Credit(child.Student)==0&&Sql.Money("SELECT SUM(TotalAmount) FROM dbo.FeePayments WHERE AdmissionId=@S AND PaymentMethod=N'Refund';",Sql.P("@S",child.Student))==-500,
            "An explicitly reviewed annual fee reduction issues only the paid 500 refund and retains the original receipt.");
        long Purchase()=>Convert.ToInt64(Sql.Proc("PostAdditionalFee",Sql.P("@UserId",f.Admin),Sql.P("@RequestId",Guid.NewGuid()),Sql.P("@FeeTypeId",f.Purchase),Sql.P("@ChargeDate",f.AdmissionDate),Sql.P("@Amount",1200m),Sql.P("@DiscountAmount",0m),Sql.P("@Reason","Fictional workbook"),Sql.TvpParameter("@Selection","ManualChargeSelectionType",Tvp.ManualChargeSelection(new[]{child.Enrollment}))).Rows[0]["BillingChargeId"]);
        long first=Purchase(),second=Purchase();int purchaseId=Sql.Count("SELECT StudentFeeAssignmentId FROM dbo.BillingCharges WHERE BillingChargeId=@C;",Sql.P("@C",first));
        await Pay(child.Student,child.Enrollment,500,(first,500));SaveTerms(purchaseId,month,month,200,1000,agreement:false,bill:first);
        r.Check(ChainDue(first)==300&&ChainDue(second)==1200&&Credit(child.Student)==0,"A purchase correction affects only the selected purchase and waives unpaid money first.");
        SaveTerms(purchaseId,month,month,0,1000,agreement:false,bill:first,full:true);
        r.Check(ChainDue(first)==0&&ChainDue(second)==1200&&Credit(child.Student)==500,"A full concession on one purchase returns its paid 500 as credit without changing another purchase.");
        r.Check(Sql.Count("SELECT COUNT(*) FROM dbo.FeePayments WHERE AdmissionId=@S AND TotalAmount=2000 AND IsCreditApplication=0;",Sql.P("@S",child.Student))==1,"The original annual receipt remains intact after financial correction.");
    }
    private async Task ClosedBillTerms()
    {
        var child=EarlierChild("Closed placement financial correction example");DateTime month=f.MonthlyDate.AddMonths(1);
        PostMonth(child.Enrollment,month);long bill=Charge(child.Enrollment,month);int id=Assignment(child.Enrollment);
        await Pay(child.Student,child.Enrollment,5000,(bill,5000));
        SqlParameter[] Close(bool preview,string? snapshot=null)=>new[]{Sql.P("@UserId",f.Admin),Sql.P("@AdmissionId",child.Student),Sql.P("@LeavingDate",f.MonthlyDate.AddDays(4)),Sql.P("@Reason","Fictional departure with retained bills"),Sql.P("@MonthlyFeePolicy","KeepIssued"),Sql.P("@Preview",preview),Sql.P("@ExpectedSnapshot",snapshot)};
        var plan=Sql.Proc("CloseStudentAdmission",Close(true));Sql.Proc("CloseStudentAdmission",Close(false,Convert.ToString(plan.Rows[0]["Snapshot"])));
        SaveTerms(id,month,month,0,4500,agreement:false,bill:bill);
        r.Check(ChainDue(bill)==0&&Credit(child.Student)==500,"A reviewed paid bill reduction remains possible after departure, including a retained future issued bill.");
        SaveTerms(id,month,month,0,6000,agreement:false,bill:bill);
        r.Check(ChainDue(bill)==1500&&Credit(child.Student)==500,"Increasing a closed-placement bill creates its explicit correction chain while preserving available credit.");
        SqlParameter[] CreditArgs(bool preview,string? snapshot=null)=>TermsArgs(id,month,month,0,preview,snapshot,6000,agreement:false,bill:bill).Concat(new[]{Sql.P("@ApplyAvailableCredit",true)}).ToArray();
        using(var review=ReviewConcession(CreditArgs(true)))
        {
            r.Check(Convert.ToDecimal(review.Tables[0].Rows[0]["CreditToUse"])==500&&Convert.ToDecimal(review.Tables[0].Rows[0]["RemainingCredit"])==0,"The review states exactly how much available credit will be used.");
            Sql.Proc("SaveStudentFeeDiscount",CreditArgs(false,Snapshot(review)));
        }
        r.Check(Credit(child.Student)==0&&Due(Tuition(child.Enrollment))==4500&&ChainDue(bill)==1500,"Explicit credit use settles the oldest tuition first while preserving the later reviewed bill's balance.");
        r.Check(Sql.Count("SELECT COUNT(*) FROM dbo.Enrollments WHERE EnrollmentId=@E AND Status=N'Left';",Sql.P("@E",child.Enrollment))==1&&Sql.Money("SELECT Amount FROM dbo.StudentFeeAssignments WHERE StudentFeeAssignmentId=@Id;",Sql.P("@Id",id))==5000,
            "Closed-bill corrections do not reopen the placement or rewrite its original fee agreement.");
        await r.Refused("A direct closed-history financial rewrite is still refused",()=>Task.Run(()=>Sql.Query("UPDATE dbo.BillingCharges SET DiscountAmount=DiscountAmount+1 WHERE BillingChargeId=@C;",Sql.P("@C",bill))),57102);
        await r.Refused("The legacy rate endpoint cannot bypass explicit closed-bill review",()=>Task.Run(()=>Rate(bill,4000)),54922);
        r.Check(Sql.Scalar("SELECT SESSION_CONTEXT(N'FMSC.BillCorrection');") is DBNull,"A completed correction leaves no reusable session correction scope.");
        using var failedReview=ReviewConcession(TermsArgs(id,month,month,0,true,rate:4000,agreement:false,bill:bill));
        Sql.Query("CREATE TRIGGER dbo.FMSC_ClosedCorrectionRefusal ON dbo.BillingCharges AFTER UPDATE AS IF EXISTS(SELECT 1 FROM inserted I JOIN deleted D ON D.BillingChargeId=I.BillingChargeId WHERE I.DiscountAmount<>D.DiscountAmount) THROW 51045,'Synthetic closed-bill refusal',1;");
        try
        {
            using var connection=DatabaseHelper.GetConnection();connection.Open();
            await r.Refused("A failure after entering closed-bill correction scope rolls back every financial change",()=>Task.Run(()=>{
                using var command=new SqlCommand("SaveStudentFeeDiscount",connection){CommandType=CommandType.StoredProcedure};
                command.Parameters.AddRange(TermsArgs(id,month,month,0,false,Snapshot(failedReview),4000,agreement:false,bill:bill));command.ExecuteNonQuery();}),51045);
            using var checkScope=new SqlCommand("SELECT CASE WHEN SESSION_CONTEXT(N'FMSC.BillCorrection') IS NULL AND @@TRANCOUNT=0 THEN 1 ELSE 0 END;",connection);
            r.Check(Convert.ToInt32(checkScope.ExecuteScalar())==1,"The same SQL session restores its correction scope and transaction after failure.");
            r.Check(ChainDue(bill)==1500&&Credit(child.Student)==0,"An aborted closed-bill correction leaves the exact prior dues and credit.");
        }
        finally{Sql.Query("DROP TRIGGER dbo.FMSC_ClosedCorrectionRefusal;");}
    }
    private Task PromotionConcessionChoices()
    {
        foreach(var choice in new[]{(Carry:false,Discount:1000m),(Carry:true,Discount:1000m),(Carry:true,Discount:5000m)})
        {
            var child=EarlierChild("Promotion concession example "+choice);int id=Assignment(child.Enrollment);DateTime last=new(f.AdmissionDate.Year,12,1);
            SaveTerms(id,last,last,choice.Discount,full:choice.Discount==5000);
            var decisions=new DataTable();decisions.Columns.Add("AdmissionId",typeof(int));decisions.Columns.Add("Decision");decisions.Rows.Add(child.Student,"PROMOTE");
            Sql.Proc("BulkPromoteStudents",Sql.P("@UserId",f.Admin),Sql.P("@SourceAcademicYearId",f.SourceYear),Sql.P("@DestinationAcademicYearId",f.NextYear),Sql.P("@SourceCampusId",f.Campus),Sql.P("@SourceClassId",f.Class6),Sql.P("@SourceSectionId",f.Section),Sql.P("@DestinationClassSectionId",f.Placement7),Sql.P("@EffectiveDate",f.PromotionDate),Sql.P("@Remarks","Fictional reviewed concession policy"),Sql.P("@CarryConcessions",choice.Carry),Sql.TvpParameter("@Decisions","BulkPromotionDecisionType",decisions));
            int next=Sql.Count("SELECT EnrollmentId FROM dbo.Enrollments WHERE AdmissionId=@S AND AcademicYearId=@Y;",Sql.P("@S",child.Student),Sql.P("@Y",f.NextYear));
            decimal expected=!choice.Carry?0:choice.Discount==5000?6000:1000;
            r.Check(Sql.Money("SELECT DiscountAmount FROM dbo.StudentFeeAssignments WHERE EnrollmentId=@E AND FeeTypeId=@F;",Sql.P("@E",next),Sql.P("@F",f.Tuition))==expected,
                "Promotion explicitly resets concessions or carries the final monthly agreement, including a full waiver at the destination rate.");
            r.Check(Sql.Count("SELECT COUNT(*) FROM dbo.BillingCharges WHERE EnrollmentId=@E;",Sql.P("@E",next))==0&&Sql.Count("SELECT COUNT(*) FROM dbo.Enrollments WHERE EnrollmentId=@E AND BillingReady=0;",Sql.P("@E",next))==1,
                "Carrying a concession never bills a student before return confirmation.");
            r.Check(Due(Tuition(child.Enrollment))==5000,"Prior-placement dues remain collectible after either promotion policy.");
        }
        return Task.CompletedTask;
    }

}
