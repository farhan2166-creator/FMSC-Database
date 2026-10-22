using FMSC_SMS.Core;
using System.Data;

internal static partial class Program
{
    private static void CheckBillLineIdentity()
    {
        using var charges=new DataTable();
        foreach(string column in new[]{"Reference","StudentName","FeeTypeName"})charges.Columns.Add(column);
        charges.Columns.Add("BillingMonth",typeof(DateTime));charges.Columns.Add("DiscountAmount",typeof(decimal));charges.Columns.Add("Due",typeof(decimal));
        charges.Columns.Add("BillingChargeId",typeof(long));charges.Columns.Add("AdmissionId",typeof(int));
        charges.Rows.Add("A-41","Same name","Annual",DateTime.Today,0m,100m,11L,41);
        charges.Rows.Add("A-41","Same name","Books",DateTime.Today,0m,200m,12L,41);
        charges.Rows.Add("A-42","Same name","Annual",DateTime.Today,0m,300m,13L,42);
        charges.Rows.Add("A-42","Same name","Waived plan",DateTime.Today,100m,0m,-7L,42);
        using var family=FamilyBillReport.BuildData("School","","","Parent",charges);
        Check(family.Rows.Count==4&&family.AsEnumerable().Select(r=>r.Field<long>("BillChargeId")).OrderBy(x=>x).SequenceEqual(new[]{-7L,11L,12L,13L}),
            "Family bill retains one source identity per printed fee, including virtual waiver evidence");
        Check(family.AsEnumerable().All(r=>r.Field<int>("BillAdmissionId")== (r.Field<long>("BillChargeId") is 11 or 12?41:42)),
            "Children with identical names retain their own bill line ownership");
        using var student=StudentBillReport.BuildData("School","","","Parent",charges);
        Check(student.AsEnumerable().Select(r=>r.Field<long>("BillChargeId")).SequenceEqual(family.AsEnumerable().Select(r=>r.Field<long>("BillChargeId"))),
            "Flattening grouped data for the student template preserves the charge identities");
        using var direct=StudentBillReport.BuildData("School","","","Student","A-41","Class",
            new[]{new StudentBillReport.Line("Annual","Oct 2026",100m,0m,11L,41)},true,"","Parent","11");
        Check(direct.Rows[0].Field<long>("BillChargeId")==11L&&direct.Rows[0].Field<int>("BillAdmissionId")==41,
            "Admission and charge-printer bill lines carry the same source identities");
        using var restored=FeeBillDocuments.ReadSnapshot(FeeBillDocuments.Snapshot(family));
        Check(restored.AsEnumerable().Select(r=>r.Field<long>("BillChargeId")).SequenceEqual(family.AsEnumerable().Select(r=>r.Field<long>("BillChargeId"))),
            "Saved XML preserves per-line identities for issue-time validation");
        using var legacy=direct.Copy();legacy.Columns.Remove("BillChargeId");legacy.Columns.Remove("BillAdmissionId");
        using var saved=new DataTable();saved.Columns.Add("BillNumber");saved.Columns.Add("IssuedAt",typeof(DateTime));saved.Columns.Add("SnapshotXml");
        saved.Rows.Add("B-2026-00001",DateTime.Today,FeeBillDocuments.Snapshot(legacy));
        using var oldBill=FeeBillDocuments.ReadSavedData(saved.Rows[0]);
        Check(oldBill.Rows.Count==1&&oldBill.Rows[0].Field<decimal>("Amount")==100m&&oldBill.Rows[0].Field<string>("ReceiptNumber")=="B-2026-00001",
            "Historical snapshots without line identities remain readable under their original bill number");
    }
}
