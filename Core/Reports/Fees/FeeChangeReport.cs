namespace FMSC_SMS.Core;

// A fee-change acknowledgement has its own paper. Normal bills and payment
// receipts retain their established templates and settlement calculations.
internal static class FeeChangeReport
{
    internal const string TemplateFile="FeeChanges.rdlc";
    internal const string DataSetName="FeeChangesData";
    internal static bool IsAcknowledgement(DataRow header)=>Convert.ToString(header["PaymentMethod"])=="Fee changes";

    internal const string PreviousPlansQuery="""
        EXEC dbo.RequirePermission @UserId,N'Fees.View';
        SELECT A.*
        FROM dbo.CollectionReceipts CurrentNote
        CROSS APPLY OPENJSON(CurrentNote.SnapshotJson,'$.Students') WITH(AdmissionId int) S
        CROSS APPLY(SELECT TOP(1) R.SnapshotJson FROM dbo.CollectionReceipts R
          WHERE R.VisitSequence<CurrentNote.VisitSequence AND JSON_QUERY(R.SnapshotJson,'$.Agreements') IS NOT NULL
            AND EXISTS(SELECT 1 FROM OPENJSON(R.SnapshotJson,'$.Students') WITH(AdmissionId int) Child WHERE Child.AdmissionId=S.AdmissionId)
          ORDER BY R.VisitSequence DESC) PreviousNote
        CROSS APPLY OPENJSON(PreviousNote.SnapshotJson,'$.Agreements') WITH(AdmissionId int,EnrollmentId int,
          FeeTypeName nvarchar(200),Amount decimal(12,2),DiscountAmount decimal(12,2),Frequency nvarchar(30),
          EffectiveFrom date,EffectiveTo date,CancelledAt datetime2(7),ConcessionLabel nvarchar(30)) A
        WHERE CurrentNote.CollectionReceiptId=@ReceiptId AND A.AdmissionId=S.AdmissionId;
        """;
    internal static Task<DataTable> ReadPreviousPlansAsync(DataRow header)=>DatabaseHelper.QuerySqlAsync(PreviousPlansQuery,
        DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.P("@ReceiptId",header["CollectionReceiptId"]));

    internal static void Show(Form? owner,DataTable data)
    {
        string path=Path.Combine(AppContext.BaseDirectory,"Reports",TemplateFile);
        RdlcReportPreview.Show(owner,"Fee Change Note",path,DataSetName,data,
            reportDefinition:FeeDocumentLayout.SizeToContent(path,data));
    }

    internal static DataTable BuildData(InstitutionProfile.PrintIdentity institution,DataRow header,
        DataTable students,DataTable lines,DataTable? agreements=null,DataTable? previousPlans=null)
    {
        if(!IsAcknowledgement(header)||Money(header,"CashReceived")!=0m||Money(header,"CashApplied")!=0m||Money(header,"CreditApplied")!=0m)
            throw new InvalidOperationException("A fee-change note must be a saved zero-payment acknowledgement.");
        if(students.Rows.Count==0)throw new InvalidOperationException("The saved fee-change note has no students.");
        decimal outstanding=Money(header,"BalanceRemaining");
        if(students.AsEnumerable().Sum(row=>Money(row,"BalanceRemaining"))!=outstanding)
            throw new InvalidOperationException("The saved student balances do not match this fee-change note.");
        var data=FeeDocumentData.Create();
        foreach(string name in new[]{"ChildCampusName","ChangeHeading","ChangeNote","ChangeLabel","ChangeAmountText"})data.Columns.Add(name,typeof(string));
        foreach(string name in new[]{"ChangeStudentKey","ChangeKey"})data.Columns.Add(name,typeof(int));
        foreach(string name in new[]{"ChangeStrong","ShowGroupSummary"})data.Columns.Add(name,typeof(bool));
        foreach(string name in new[]{"ChildOutstanding","GroupOutstanding"})data.Columns.Add(name,typeof(decimal));
        int studentKey=0,changeKey=0;
        foreach(DataRow student in FeeStudentOrder.Rows(students,"AdmissionNumber"))
        {
            studentKey++;
            int id=Convert.ToInt32(student["AdmissionId"]);
            var fees=lines.AsEnumerable().Where(row=>Convert.ToInt32(row["AdmissionId"])==id).ToArray();
            decimal changedDue=0m;int sections=0;var changedFees=new HashSet<DataRow>();
            string One(IEnumerable<string> values)=>string.Join(" / ",values.Where(value=>value.Length>0).Distinct());
            string campus=One(fees.Select(row=>Text(row,"CampusName")));
            string placement=One(fees.Select(row=>Text(row,"ClassName")+(Text(row,"SectionName").Length>0?" - "+Text(row,"SectionName"):"")));
            void Section(string heading,IEnumerable<(string Label,decimal? Amount,bool Strong)> entries,string note="")
            {
                changeKey++;sections++;
                foreach(var entry in entries)
                {
                    var row=data.NewRow();
                    row["ReceiptPageKey"]=1;row["InstitutionName"]=institution.Name;
                    row["SchoolAddress"]=institution.Address;row["ContactNumber"]=institution.ContactNumber;
                    row["CampusName"]=FeeDocumentData.CampusHeading(lines);row["ChildCampusName"]=campus;
                    row["DocumentTitle"]="FEE CHANGE NOTE";row["CopyLabel"]="PARENT COPY";
                    row["ReceiptNumber"]=header["ReceiptNumber"];
                    row["PaymentDateText"]=Convert.ToDateTime(header["TransactionDate"]).ToString("dd-MM-yyyy hh:mm tt");
                    row["AdmissionNumberText"]=student["AdmissionNumber"];row["StudentName"]=student["StudentName"];
                    row["FatherName"]=student["FatherName"];row["ClassSection"]=placement;
                    row["ChangeStudentKey"]=studentKey;row["ChangeKey"]=changeKey;
                    row["ChangeHeading"]=heading;row["ChangeNote"]=note;row["ChangeLabel"]=entry.Label;
                    row["ChangeAmountText"]=entry.Amount?.ToString("N0")??"";row["ChangeStrong"]=entry.Strong;
                    row["ChildOutstanding"]=Money(student,"BalanceRemaining");row["GroupOutstanding"]=outstanding;
                    row["ShowGroupSummary"]=students.Rows.Count>1;row["PaymentMethod"]="Fee changes";
                    data.Rows.Add(row);
                }
            }
            foreach(var fee in fees.OrderBy(row=>row["FeePeriod"]).ThenBy(row=>Money(row,"FeeDisplayOrder")))
            {
                bool visit=HasValue(fee,"BaseAmount");
                decimal discount=Money(fee,"Discount"),rate=Money(fee,"RateAdjustment"),withdrawn=Money(fee,"DiscountReversed"),settlement=Money(fee,"SettlementAdjustment");
                bool cancelled=Text(fee,"FeeStatus")=="Cancelled"||Text(fee,"ConcessionKind")=="Cancelled";
                // Unchanged unpaid fees belong in the outstanding section, not
                // under a heading claiming that their terms have just changed.
                if(!cancelled&&discount==0m&&rate==0m&&withdrawn==0m&&settlement==0m)continue;
                changedFees.Add(fee);
                decimal before=visit?Money(fee,"BaseAmount"):HasValue(fee,"GrossAmount")?Money(fee,"GrossAmount"):Money(fee,"AmountBefore");
                if(!visit)settlement=-Money(fee,"PreviouslyPaid");
                decimal after=HasValue(fee,"BalanceAfter")?Money(fee,"BalanceAfter"):before+rate+withdrawn+settlement-discount;
                string heading=FeeDocumentData.FeeName(Text(fee,"FeeTypeName"))+" · "+Convert.ToDateTime(fee["FeePeriod"]).ToString("MMM-yyyy");
                var entries=new List<(string,decimal?,bool)>();
                if(cancelled)
                {
                    if(before==0m)before=Money(fee,"CancelledAmount");
                    entries.Add(("Dues before",before,false));entries.Add(("Charge cancelled",-before,false));
                    after=0m;
                }
                else
                {
                    bool carried=HasValue(fee,"IsCarriedForward")&&Convert.ToBoolean(fee["IsCarriedForward"]);
                    entries.Add((carried?"Dues before":"Fee amount",before,false));
                    if(rate!=0m)entries.Add((rate<0m?"Fee reduced":"Fee increased",rate,false));
                    if(withdrawn!=0m)entries.Add(("Discount removed",withdrawn,false));
                    if(settlement!=0m)entries.Add((settlement<0m?"Earlier payments / credit":"Earlier payment adjusted",settlement,false));
                    if(discount!=0m)entries.Add((Text(fee,"ConcessionKind") is "Waived" or "Free"||after==0m?"Waived":"Discount given",-discount,false));
                    if(before+rate+withdrawn+settlement-discount!=after)
                        throw new InvalidOperationException("The saved fee change does not reconcile. Reload the note.");
                }
                entries.Add(("Dues now",after,true));changedDue+=after;
                string reason=Text(fee,"ChangeReason");
                if(reason==FeeConcessionActions.WaiverReason)reason="Remaining dues waived by the school.";
                Section(heading,entries,reason.Length>0?"Reason: "+reason:"");
            }
            decimal other=Money(student,"BalanceRemaining")-changedDue;
            if(other<0m)throw new InvalidOperationException("The saved fee changes exceed this student's recorded balance.");
            if(other>0m)
            {
                var unpaid=fees.Where(fee=>!changedFees.Contains(fee)&&Money(fee,"BalanceAfter")>0m).ToArray();
                decimal listed=unpaid.Sum(fee=>Money(fee,"BalanceAfter"));
                if(listed>other)throw new InvalidOperationException("The saved unpaid fees do not match this note's balance.");
                var entries=unpaid.Select(fee=>(FeeDocumentData.FeeName(Text(fee,"FeeTypeName"))+" · "+Convert.ToDateTime(fee["FeePeriod"]).ToString("MMM-yyyy"),(decimal?)Money(fee,"BalanceAfter"),false)).ToList();
                if(listed<other)entries.Add(("Other outstanding",other-listed,false));
                Section("OTHER UNPAID FEES",entries,"These fees were not changed by this note.");
            }
            if(agreements is not null)
            {
                var previous=previousPlans?.AsEnumerable().Where(row=>Convert.ToInt32(row["AdmissionId"])==id).ToList()??new List<DataRow>();
                foreach(var plan in agreements.AsEnumerable().Where(row=>Convert.ToInt32(row["AdmissionId"])==id))
                {
                    // Compare saved snapshots, never today's plan. Unchanged
                    // plans do not crowd a note about a single posted waiver.
                    int same=previous.FindIndex(old=>SamePlan(old,plan));
                    if(same>=0){previous.RemoveAt(same);continue;}
                    bool ended=HasValue(plan,"CancelledAt");
                    decimal amount=Money(plan,"Amount"),discount=Money(plan,"DiscountAmount");
                    string Period(string name)=>HasValue(plan,name)?Convert.ToDateTime(plan[name]).ToString("MMM-yyyy"):"not specified";
                    string dates=$"Effective: {Period("EffectiveFrom")}"+(HasValue(plan,"EffectiveTo")?" to "+Period("EffectiveTo"):"");
                    string frequency=Text(plan,"Frequency");
                    string note=ended?$"Ended on {Convert.ToDateTime(plan["CancelledAt"]):dd-MM-yyyy}.":dates+". "+frequency+". Plan amounts are separate from posted dues.";
                    var entries=new List<(string,decimal?,bool)>{("Fee amount",amount,false)};
                    if(discount>0m)entries.Add(("Discount / waived",-discount,false));
                    entries.Add((ended?"Former plan amount":"Plan net amount",amount-discount,true));
                    Section((ended?"ENDED FEE PLAN: ":"SAVED FEE PLAN: ")+FeeDocumentData.FeeName(Text(plan,"FeeTypeName")),entries,note);
                }
            }
            if(sections==0)Section("SAVED FEE REVIEW",new[]{("No new posted fee changes",(decimal?)null,false)});
        }
        return data;
    }

    private static bool HasValue(DataRow row,string name)=>row.Table.Columns.Contains(name)&&!row.IsNull(name);
    private static decimal Money(DataRow row,string name)=>HasValue(row,name)?Convert.ToDecimal(row[name]):0m;
    private static string Text(DataRow row,string name)=>HasValue(row,name)?Convert.ToString(row[name])??"":"";
    private static bool SamePlan(DataRow left,DataRow right)=>new[]{"AdmissionId","EnrollmentId","FeeTypeName","Amount","DiscountAmount","Frequency","EffectiveFrom","EffectiveTo","CancelledAt","ConcessionLabel"}
        .All(name=>!HasValue(left,name)&&!HasValue(right,name)||HasValue(left,name)&&HasValue(right,name)&&Equals(left[name],right[name]));
}
