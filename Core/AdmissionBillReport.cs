namespace FMSC_SMS.Core;

internal static partial class AdmissionBillReport
{
    internal sealed record Line(string FeeName, string Period, decimal Amount, decimal Discount);

    internal static void ShowFamily(Form? owner, InstitutionProfile.PrintIdentity institution, string parent, DataTable charges)
    {
        using var grouped=BuildFamilyData(institution.Name,institution.ContactNumber,institution.Address,parent,charges);
        bool family=charges.AsEnumerable().Select(row=>Convert.ToString(row["Reference"])).Distinct().Count()>1;
        using var data=family?FamilyBillData(grouped):StudentBillData(grouped);
        string path=Path.Combine(AppContext.BaseDirectory,"Reports",family?"FamilyBill.rdlc":"StudentBill.rdlc");
        RdlcReportPreview.Show(owner,family?"Family Bill":"Student Bill",path,"FeeReceiptData",data,
            reportDefinition:ReceiptReportLayout.SizeToContent(path,data));
    }
    internal static void Show(Form? owner, InstitutionProfile.PrintIdentity institution,
        string studentName, int admissionId, string classSection, IReadOnlyList<Line> lines, bool showDiscount,
        string campusName = "", string fatherName = "")
    {
        var data = ReceiptReportData.Create();
        string reference=AdmissionNumbers.ForId(admissionId);
        bool provisional=reference.StartsWith("QR-",StringComparison.OrdinalIgnoreCase);
        decimal discount = showDiscount ? lines.Sum(line => line.Discount) : 0m;
        decimal total = lines.Sum(line => line.Amount);
        foreach (var line in lines)
            data.Rows.Add(1, institution.Name, "STUDENT PAYMENT BILL", "PARENT COPY",
                "BILL-" + reference, studentName,
                reference, classSection,
                DateTime.Now.ToString("dd-MMM-yyyy hh:mm tt"), "",
                line.FeeName,
                line.Amount, discount, total, 0m, "", "", discount > 0m, false,
                institution.ContactNumber, institution.Address, campusName, fatherName, line.Period);
        foreach(DataRow row in data.Rows) row["DocumentKind"]="Bill";
        string path = Path.Combine(AppContext.BaseDirectory, "Reports", "StudentBill.rdlc");
        RdlcReportPreview.Show(owner, provisional?"Registration Bill":"Admission Bill", path, "FeeReceiptData", data,
            fitPageWidth: false, reportDefinition: ReceiptReportLayout.SizeToContent(path, data));
    }
}
