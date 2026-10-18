using FMSC_SMS.Core;

internal static class RegistrationBillChecks
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        using var charges=new DataTable();
        foreach(var (column,type) in new[]{("Reference",typeof(string)),("StudentName",typeof(string)),("FeeTypeName",typeof(string)),
            ("BillingMonth",typeof(DateTime)),("DiscountAmount",typeof(decimal)),("Due",typeof(decimal))})charges.Columns.Add(column,type);
        charges.Rows.Add("QR-41","Ali","Tuition",new DateTime(2026,10,1),100m,900m);
        charges.Rows.Add("QR-41","Ali","Admission",DBNull.Value,0m,200m);
        using var single=AdmissionBillReport.BuildFamilyData("School","Phone","Address","Parent",charges);
        Check(single.AsEnumerable().All(r=>r.Field<string>("DocumentTitle")=="STUDENT PAYMENT BILL"&&r.Field<string>("StudentName")=="Ali"),"One child with several fees has a student bill headed with the child's name");
        Check(single.AsEnumerable().All(r=>r.Field<string>("ReceiptNumber")=="BILL-QR-41"&&r.Field<string>("FatherName")=="Parent"),"Single-child identity and parent details remain clear");
        Check(single.AsEnumerable().Count(r=>r.Field<string>("FamilyRowKind")=="Student")==1,"One child's fees cannot create multiple student headings");
        Check(single.AsEnumerable().Where(r=>r.Field<string>("FamilyRowKind")=="Fee").Sum(r=>r.Field<decimal>("Amount"))==1100m,"All single-child charges remain in the bill");
        Check(single.AsEnumerable().All(r=>r.Field<decimal>("TotalPaid")==1100m&&r.Field<decimal>("DiscountApplied")==100m),"Student bill totals and discounts stay exact");
        charges.Rows.Add("QR-42","Ali","Tuition",new DateTime(2026,10,1),0m,1000m);
        using var family=AdmissionBillReport.BuildFamilyData("School","Phone","Address","Parent",charges);
        Check(family.AsEnumerable().All(r=>r.Field<string>("DocumentTitle")=="FAMILY PAYMENT BILL"&&r.Field<string>("StudentName")=="Parent"),"Several child references use the family title and parent heading");
        Check(family.AsEnumerable().Count(r=>r.Field<string>("FamilyRowKind")=="Student")==2,"Same-name children keep separate reference-based headings");
        Check(family.AsEnumerable().Where(r=>r.Field<string>("FamilyRowKind")=="Fee").Sum(r=>r.Field<decimal>("Amount"))==2100m&&charges.Rows.Count==3,"Family bill retains every charge and leaves its source unchanged");
        using var table=AdmissionBillReport.FamilyBillData(family);
        Check(table.Rows.Count==3&&table.AsEnumerable().Sum(row=>row.Field<decimal>("Amount"))==2100m,"The bill table contains actual fee rows only, with no duplicated heading amounts");
        Check(table.AsEnumerable().Select(row=>row.Field<string>("BillStudentKey")).Distinct().Count()==2,"Same-name children retain distinct table groups by reference");
        Check(table.AsEnumerable().All(row=>!string.IsNullOrWhiteSpace(row.Field<string>("FeePeriod"))&&row.Field<string>("FeeDescription")==row.Field<string>("FeeDescription")!.Trim()),"Each table row has its own period and fee without indentation");
        using var members=new DataTable();
        members.Columns.Add("AdmissionId",typeof(int));members.Columns.Add("StudentName");members.Columns.Add("Status");
        members.Columns.Add("EnrollmentStatus");members.Columns.Add("BillingReady",typeof(bool));members.Columns.Add("PlacementConfirmedAt",typeof(DateTime));
        members.Rows.Add(41,"Same Name","Active","Active",false,new DateTime(2026,4,1));
        members.Rows.Add(42,"Same Name","Active","Active",true,new DateTime(2026,4,1));
        members.Rows.Add(43,"New Child","Pending","Active",false,DBNull.Value);
        Check(FamilyBillingData.Readiness(members.Rows[0])=="Awaiting Return","An unready confirmed promoted placement requires an explicit return");
        Check(FamilyBillingData.Readiness(members.Rows[1])=="Ready for Billing","Confirmed return readiness is shown independently of outstanding balance");
        Check(FamilyBillingData.Readiness(members.Rows[2])=="Pending Registration","A linked new child does not become a returning/admitted student");
        Check(FamilyBillingData.SelectMembers(members,null).SequenceEqual(new[]{41,42,43}),"Registration bill includes the whole actual family");
        Check(FamilyBillingData.SelectMembers(members,new[]{42,43,42}).SequenceEqual(new[]{42,43}),"Counter bill honors selected distinct children");
        Check(FamilyBillingData.IncompleteNotice(members,new[]{42,43})=="","Unchecked returning children do not change selected billing scope");
        Check(FamilyBillingData.IncompleteNotice(members,new[]{41,43}).Contains("Awaiting Return")&&FamilyBillingData.IncompleteNotice(members,new[]{41}).Contains("older dues"),"Mixed family billing warns about missing new-year charges and includes older posted dues explicitly");
        foreach(var ids in new[]{Array.Empty<int>(),new[]{0},new[]{99},new[]{41,99}}){
            bool refused=false;try{FamilyBillingData.SelectMembers(members,ids);}catch(InvalidOperationException){refused=true;}
            Check(refused,"Empty, invalid, stale or unrelated family selection is refused");
        }
        members.Rows[0]["PlacementConfirmedAt"]=DBNull.Value;
        Check(!FamilyBillingData.AwaitsReturn(members.Rows[0])&&FamilyBillingData.Readiness(members.Rows[0])=="Review Placement","Unconfirmed/legacy placement cannot be passed off as promoted return");
        members.Rows[1]["Status"]="Left";
        Check(FamilyBillingData.Readiness(members.Rows[1])=="Left"&&!FamilyBillingData.AwaitsReturn(members.Rows[1]),"A departed child is not automatically returned");
        return checks;
    }
}
