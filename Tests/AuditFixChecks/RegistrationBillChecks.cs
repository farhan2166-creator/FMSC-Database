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
        return checks;
    }
}
