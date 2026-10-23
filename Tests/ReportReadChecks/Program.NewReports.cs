using FMSC_SMS.Pages.Reports.Finance;
using FMSC_SMS.Pages.Reports.Certificates;

internal static partial class Program
{
    private static void CheckNewReports()
    {
        var source=new DataTable();source.Columns.Add("BillingChargeId",typeof(long));source.Columns.Add("BillingMonth",typeof(DateTime));
        foreach(string name in new[] { "CampusId","ClassId","SectionId","FeeTypeId" }) source.Columns.Add(name,typeof(int));
        foreach(string name in new[] { "Campus","Class","Section","Fee Type" }) source.Columns.Add(name);
        foreach(string name in new[] { "GrossAmount","DiscountAmount","NetAmount","Money Allocated","Credit Applied","Outstanding","Excess Settlement" }) source.Columns.Add(name,typeof(decimal));
        source.Rows.Add(1L,new DateTime(2026,10,1),1,1,1,1,"Boys","One","A","Tuition",1000m,100m,900m,850m,100m,0m,50m);
        source.Rows.Add(2L,new DateTime(2026,10,15),1,1,2,1,"Boys","One","B","Tuition",500m,0m,500m,50m,100m,350m,0m);
        // Identical names in different campuses/IDs must never silently merge.
        source.Rows.Add(3L,new DateTime(2026,11,1),2,1,1,1,"Boys","One","A","Tuition",200m,0m,200m,0m,0m,200m,0m);
        source.Rows.Add(4L,new DateTime(2026,10,1),2,1,1,2,"Boys","One","A","Other",700m,0m,700m,0m,0m,700m,0m);
        foreach(string grouping in MonthlyFeeReconciliationData.Groupings)
        {
            var (rows,totals)=MonthlyFeeReconciliationData.Create(source,grouping);
            Check(rows.AsEnumerable().Sum(r=>r.Field<int>("Charges"))==4,grouping+": charges counted once across billing months");
            Check(rows.AsEnumerable().Sum(r=>r.Field<decimal>("Net Fees"))==2300m,grouping+": net billing reconciles across all groups");
            Check(rows.AsEnumerable().All(r=>r.Field<decimal>("Net Fees")+r.Field<decimal>("Excess Settlement")
                ==r.Field<decimal>("Money Allocated")+r.Field<decimal>("Credit Applied")+r.Field<decimal>("Outstanding")),grouping+": balances reconcile including excess settlement");
            Check((decimal)totals.Rows[0]["Settled"]==1100m && (decimal)totals.Rows[0]["Outstanding"]==1250m,grouping+": header totals match all rows");
            Check(!rows.Columns.Contains("BillingChargeId")&&!rows.Columns.Contains("CampusId"),grouping+": internal IDs are excluded from output");
        }
        Check(MonthlyFeeReconciliationData.Create(source,"Whole School").Rows.Rows.Count==2,"Whole school has one row per billing month, normalizing mid-month values");
        Check(MonthlyFeeReconciliationData.Create(source,"Class").Rows.Rows.Count==3,"Same class names in different campuses remain separate");
        var empty=MonthlyFeeReconciliationData.Create(source.Clone(),"Campus");
        Check(empty.Rows.Rows.Count==0 && (decimal)empty.Totals.Rows[0]["Net Fees"]==0m,"Empty reconciliation retains a valid output schema and zero totals");
        var register=new DataTable();register.Columns.Add("Entry");register.Columns.Add("Collection");
        register.Rows.Add("Issue logged","Not Collected");register.Rows.Add("Issue logged","Collected");
        register.Rows.Add("Issue logged","Not Collected");register.Rows.Add("Saved record only","Not tracked");
        var counts=CertificateIssueRegisterData.Totals(register);
        Check((int)counts.Rows[0]["Logged issues"]==3 && (int)counts.Rows[0]["Saved records only"]==1 && (int)counts.Rows[0]["Awaiting collection"]==2,
            "Certificate counts distinguish issue entries, duplicates, saved records and collection");
        Check((int)CertificateIssueRegisterData.Totals(register.Clone()).Rows[0]["Logged issues"]==0,"Empty certificate register totals");
        string general=CertificateIssueRegisterData.BuildQuery(true,false),leaving=CertificateIssueRegisterData.BuildQuery(false,true);
        Check(!general.Contains("dbo.LeaveCertificates") && general.Contains("N'Certificates.Create'"),"General register query never reads leaving sources without their permission");
        Check(!leaving.Contains("dbo.BankAccountOpeningCertificates") && leaving.Contains("N'Students.Leave'"),"Leaving-only register query never reads general or staff certificate sources");
        Check(CertificateIssueRegisterData.BuildQuery(true,true).Contains("UNION ALL"),"Repeated certificate issues are retained rather than deduplicated");
        bool denied=false;try { CertificateIssueRegisterData.BuildQuery(false,false); } catch(UnauthorizedAccessException) { denied=true; }
        Check(denied,"No-source certificate access fails before querying");
        Check(general.Contains("CHARINDEX(@Search")&&!general.Contains("LIKE @Search"),"Certificate search treats percent and underscore as literal text");
        var dates=new DataTable();foreach(string column in new[]{"Recorded On","Certificate Date","Collected On"}) dates.Columns.Add(column,typeof(DateTime));
        dates.Rows.Add(new DateTime(2026,10,23,14,35,0),new DateTime(2026,10,21),DBNull.Value);
        var display=CertificateIssueRegisterData.Display(dates);
        Check((string)display.Rows[0]["Recorded On"]==new DateTime(2026,10,23,14,35,0).ToString("dd-MMM-yyyy HH:mm"),"Certificate issue time survives grid, CSV, PDF and RDLC projection");
        Check((string)display.Rows[0]["Certificate Date"]==new DateTime(2026,10,21).ToString("dd-MMM-yyyy") && display.Rows[0]["Collected On"]==DBNull.Value,"Certificate date and uncollected blank stay distinct");
    }
}
