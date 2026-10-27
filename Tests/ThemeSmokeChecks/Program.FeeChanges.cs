using System.Data;
using System.Xml.Linq;
using Microsoft.Reporting.WinForms;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckFeeChangeDocuments()
    {
        var school=new InstitutionProfile.PrintIdentity("Fahim Model School & College","0333-9164636, 0312-9164636","Sarband Peshawar");
        using var header=new DataTable();
        foreach(string name in new[]{"ReceiptNumber","PaymentMethod"})header.Columns.Add(name);
        header.Columns.Add("TransactionDate",typeof(DateTime));header.Columns.Add("SnapshotVersion",typeof(int));
        foreach(string name in new[]{"CashReceived","CashApplied","CreditApplied","BalanceRemaining"})header.Columns.Add(name,typeof(decimal));
        var h=header.Rows.Add("R-2026-00013","Fee changes",new DateTime(2026,10,27,9,40,2),3,0m,0m,0m,0m);
        using var students=new DataTable();students.Columns.Add("AdmissionId",typeof(int));
        foreach(string name in new[]{"AdmissionNumber","StudentName","FatherName"})students.Columns.Add(name);
        students.Columns.Add("BalanceRemaining",typeof(decimal));
        students.Rows.Add(542,"542","Kausar","Jan Akbar",0m);
        using var lines=new DataTable();lines.Columns.Add("AdmissionId",typeof(int));lines.Columns.Add("ChargeId",typeof(long));
        foreach(string name in new[]{"FeeTypeName","ClassName","SectionName","CampusName","ConcessionKind","ChangeReason","FeeStatus"})lines.Columns.Add(name);
        lines.Columns.Add("FeePeriod",typeof(DateTime));lines.Columns.Add("IsCarriedForward",typeof(bool));
        foreach(string name in new[]{"BaseAmount","GrossAmount","AmountBefore","Discount","RateAdjustment","DiscountReversed","SettlementAdjustment","BalanceAfter","CancelledAmount"})lines.Columns.Add(name,typeof(decimal));
        void Fee(long id,string name,decimal before,decimal discount,decimal after,string kind="",decimal rate=0m,decimal withdrawn=0m,decimal settlement=0m,string reason="",int child=542)
            =>lines.Rows.Add(child,id,name,child==542?"Four":"Nine","F",child==542?"Campus Boys":"Campus Girls",kind,reason,"Paid",new DateTime(2026,10,1),true,
                before,before+rate+withdrawn+settlement,before+rate+withdrawn+settlement,discount,rate,withdrawn,settlement,after,0m);
        string folder=Path.GetFullPath("artifacts/fee-changes");Directory.CreateDirectory(folder);
        void Render(string name,DataTable data)
        {
            FeeDocumentParents.Apply(data,new Dictionary<string,string>{{"542","Female"},{"249","Male"}});
            string path=Path.Combine(AppContext.BaseDirectory,"Reports",FeeChangeReport.TemplateFile);
            string definition=FeeDocumentLayout.SizeToContent(path,data);
            var xml=XDocument.Parse(definition);XNamespace ns=xml.Root!.Name.Namespace;
            Check(!xml.Descendants(ns+"Value").Any(value=>value.Value.Contains("Payment received")||value.Value.Contains("Net payable")),"Fee-change notes do not borrow payment-receipt settlement labels");
            Check(xml.Descendants(ns+"Value").Any(value=>value.Value=="No payment was received."),"The note clearly states that no payment was received");
            Check(!xml.Descendants(ns+"PageBreak").Any(),"A family note remains one continuous printout");
            using var report=new LocalReport();report.LoadReportDefinition(new StringReader(definition));
            report.DataSources.Add(new ReportDataSource(FeeChangeReport.DataSetName,data));
            byte[] pdf=report.Render("PDF");File.WriteAllBytes(Path.Combine(folder,name+".pdf"),pdf);
            byte[] png=report.Render("IMAGE","<DeviceInfo><OutputFormat>PNG</OutputFormat><DpiX>160</DpiX><DpiY>160</DpiY></DeviceInfo>");
            File.WriteAllBytes(Path.Combine(folder,name+".png"),png);
            using var parsed=PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(pdf),PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            Check(parsed.PageCount==1&&png.Length>1000,"Fee changes render as one complete thermal PDF/PNG");
            string csv=System.Text.Encoding.UTF8.GetString(report.Render("CSV"));File.WriteAllText(Path.Combine(folder,name+".csv"),csv);
            Check(csv.Contains("Dues now")||csv.Contains("Plan net amount")||csv.Contains("Charge cancelled")||csv.Contains("No new posted fee changes"),"The reporting engine renders the change explanations");
        }
        Fee(135,"Other Fee",300m,300m,0m,"Waived",reason:FeeConcessionActions.WaiverReason);
        using(var data=FeeChangeReport.BuildData(school,h,students,lines))
        {
            Check(data.Rows.Count==3&&data.Rows[1].Field<string>("ChangeLabel")=="Waived"&&data.Rows[1].Field<string>("ChangeAmountText")==(-300m).ToString("N0"),"A fully waived fee prints before, waiver and zero after");
            Check(data.Rows[0].Field<string>("ChangeNote")=="Reason: Remaining dues waived by the school.","The internal waiver reason is rendered in parent-friendly language");
            Render("individual-waiver-only",data);
        }
        h["BalanceRemaining"]=500m;students.Rows[0]["BalanceRemaining"]=500m;
        Fee(136,"Other Fee",500m,0m,500m);
        using(var data=FeeChangeReport.BuildData(school,h,students,lines))
        {
            Check(data.AsEnumerable().Select(row=>row.Field<string>("ChangeHeading")).Distinct().Count()==2
                &&data.AsEnumerable().Any(row=>row.Field<string>("ChangeHeading")=="OTHER UNPAID FEES"&&row.Field<string>("ChangeAmountText")==500m.ToString("N0")),
                "Unchanged debt is shown separately rather than presented as another fee change");
            Check(data.AsEnumerable().All(row=>row.Field<decimal>("ChildOutstanding")==500m),"The note keeps its saved outstanding balance");
            Render("individual-waiver-and-other-dues",data);
        }
        lines.Rows.Clear();h["BalanceRemaining"]=students.Rows[0]["BalanceRemaining"]=400m;
        Fee(137,"Library Fee",600m,200m,400m,"Discount",reason:"Approved library concession");
        using(var data=FeeChangeReport.BuildData(school,h,students,lines))Render("individual-discount",data);
        lines.Rows.Clear();h["BalanceRemaining"]=students.Rows[0]["BalanceRemaining"]=600m;
        Fee(137,"Library Fee",400m,0m,600m,withdrawn:200m,reason:"Temporary concession ended");
        using(var data=FeeChangeReport.BuildData(school,h,students,lines))
        {
            Check(data.AsEnumerable().Any(row=>row.Field<string>("ChangeLabel")=="Discount removed"&&row.Field<string>("ChangeAmountText")==200m.ToString("N0")),"A withdrawn discount is an increase in dues, not a negative waiver");
            Render("individual-discount-removed",data);
        }
        lines.Rows.Clear();h["BalanceRemaining"]=students.Rows[0]["BalanceRemaining"]=0m;
        Fee(138,"Activity Fee",300m,200m,0m,"Waived",rate:-100m,reason:"Fee corrected and remaining dues waived");
        Fee(139,"Transport Fee",0m,0m,0m,rate:-50m,settlement:50m,reason:"Paid fee reduced; settlement released");
        using(var data=FeeChangeReport.BuildData(school,h,students,lines))
        {
            Check(data.AsEnumerable().Any(row=>row.Field<string>("ChangeAmountText")==(-50m).ToString("N0"))
                &&data.AsEnumerable().Any(row=>row.Field<string>("ChangeAmountText")==50m.ToString("N0")),"Offsetting changes remain visible without inventing a payment");
            Render("individual-combined-corrections",data);
        }
        lines.Rows.Clear();Fee(140,"Books Fee",300m,0m,0m,rate:-300m,reason:"Duplicate charge cancelled");lines.Rows[0]["FeeStatus"]="Cancelled";
        using(var data=FeeChangeReport.BuildData(school,h,students,lines))Render("individual-charge-cancelled",data);
        lines.Rows.Clear();
        using var plans=new DataTable();plans.Columns.Add("AdmissionId",typeof(int));plans.Columns.Add("EnrollmentId",typeof(int));
        foreach(string name in new[]{"FeeTypeName","Frequency","ConcessionLabel"})plans.Columns.Add(name);
        foreach(string name in new[]{"Amount","DiscountAmount"})plans.Columns.Add(name,typeof(decimal));
        foreach(string name in new[]{"EffectiveFrom","EffectiveTo","CancelledAt"})plans.Columns.Add(name,typeof(DateTime));
        plans.Rows.Add(542,21,"Tuition Fee","Monthly","Scholarship",1600m,400m,new DateTime(2027,1,1),new DateTime(2027,3,1),DBNull.Value);
        using(var data=FeeChangeReport.BuildData(school,h,students,lines,plans))
        {
            Check(data.AsEnumerable().Any(row=>row.Field<string>("ChangeLabel")=="Plan net amount"&&row.Field<string>("ChangeAmountText")==1200m.ToString("N0"))
                &&data.Rows[0].Field<decimal>("ChildOutstanding")==0m,"Future fee-plan figures are separate from posted outstanding dues");
            Render("individual-future-plan",data);
        }
        using(var old=plans.Copy())
        using(var data=FeeChangeReport.BuildData(school,h,students,lines,plans,old))
        {
            Check(data.Rows.Count==1&&data.Rows[0].Field<string>("ChangeLabel")=="No new posted fee changes","An unchanged saved plan is not repeatedly printed as a new change");
            Render("individual-no-new-changes",data);
        }
        students.Rows.Add(249,"249","Muhammad Nouman","Jan Akbar",400m);
        h["BalanceRemaining"]=400m;
        Fee(141,"Library Fee",600m,600m,0m,"Waived",reason:"School-approved waiver");
        Fee(142,"Activity Fee",600m,200m,400m,"Discount",reason:"Approved concession",child:249);
        using(var data=FeeChangeReport.BuildData(school,h,students,lines))
        {
            Check(data.AsEnumerable().Select(row=>row.Field<int>("ChangeStudentKey")).Distinct().Count()==2
                &&data.AsEnumerable().All(row=>row.Field<decimal>("GroupOutstanding")==400m),"Family notes retain each child's changes and one saved family outstanding total");
            Render("family-fee-changes",data);
        }
        lines.Rows[0]["FeeTypeName"]="Examination and practical assessment materials fee";
        lines.Rows[0]["ChangeReason"]=string.Join(" ",Enumerable.Repeat("Approved concession for examination materials, reviewed by the school office.",5));
        using(var data=FeeChangeReport.BuildData(school,h,students,lines))Render("family-long-fee-and-reason",data);
        lines.Rows.Clear();
        for(int index=0;index<30;index++)
            Fee(200+index,"Examination and practical assessment materials fee "+index,600m,600m,0m,"Waived",
                reason:string.Join(" ",Enumerable.Repeat("Approved concession for examination materials, reviewed by the school office.",index%5+1)));
        Fee(300,"Library Fee",600m,200m,400m,"Discount",reason:"Approved concession",child:249);
        using(var data=FeeChangeReport.BuildData(school,h,students,lines))Render("family-many-wrapped-fee-changes",data);
        h["CashReceived"]=1m;bool refused=false;
        try{using var invalid=FeeChangeReport.BuildData(school,h,students,lines);}catch(InvalidOperationException){refused=true;}
        Check(refused,"An actual payment cannot be presented as a fee-change note");
        h["CashReceived"]=0m;
        lines.Rows[0]["BalanceAfter"]=1m;refused=false;
        try{using var invalid=FeeChangeReport.BuildData(school,h,students,lines);}catch(InvalidOperationException){refused=true;}
        Check(refused,"A saved before/change/after mismatch is rejected rather than printed");
    }
}
