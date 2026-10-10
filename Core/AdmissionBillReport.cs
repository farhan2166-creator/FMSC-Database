namespace FMSC_SMS.Core;

internal static class AdmissionBillReport
{
    internal sealed record Line(string FeeName, string Period, decimal Amount, decimal Discount);

    internal static void Show(Form? owner, InstitutionProfile.PrintIdentity institution,
        string studentName, int admissionId, string classSection, IReadOnlyList<Line> lines, bool showDiscount,
        string campusName = "", string fatherName = "")
    {
        var data = ReceiptReportData.Create();
        decimal discount = showDiscount ? lines.Sum(line => line.Discount) : 0m;
        decimal total = lines.Sum(line => line.Amount);
        foreach (var line in lines)
            data.Rows.Add(1, institution.Name, "PAYMENT BILL", "STUDENT COPY",
                "ADM-" + AdmissionNumbers.ForId(admissionId), studentName,
                AdmissionNumbers.ForId(admissionId), classSection,
                DateTime.Now.ToString("dd-MMM-yyyy hh:mm tt"), "",
                line.FeeName,
                line.Amount, discount, total, 0m, "", "", discount > 0m, false,
                institution.ContactNumber, institution.Address, campusName, fatherName, line.Period);
        ReceiptReportData.GroupPeriods(data);
        string path = Path.Combine(AppContext.BaseDirectory, "Reports", "AdmissionBill.rdlc");
        RdlcReportPreview.Show(owner, "Admission Bill", path, "FeeReceiptData", data,
            fitPageWidth: false, reportDefinition: ReceiptReportLayout.Definition(path, data));
    }
}
