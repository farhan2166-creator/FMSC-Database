namespace FMSC_SMS.Core;

internal static partial class AdmissionBillReport
{
    internal sealed record Line(string FeeName, string Period, decimal Amount, decimal Discount);

    internal static async Task ShowFamily(Form? owner, InstitutionProfile.PrintIdentity institution, string parent, DataTable charges,
        int primaryAdmission,int[] students)
    {
        using var grouped=BuildFamilyData(institution.Name,institution.ContactNumber,institution.Address,parent,charges);
        bool family=charges.AsEnumerable().Select(row=>Convert.ToString(row["Reference"])).Distinct().Count()>1;
        using var data=family?FamilyBillData(grouped):StudentBillData(grouped);
        await IssuedBillReport.ShowAsync(owner,data,primaryAdmission,students);
    }
    internal static async Task Show(Form? owner, InstitutionProfile.PrintIdentity institution,
        string studentName, int admissionId, string classSection, IReadOnlyList<Line> lines, bool showDiscount,
        string campusName = "", string fatherName = "", string sourceKey = "")
    {
        using var data = ReceiptReportData.Create();
        string reference=AdmissionNumbers.ForId(admissionId);
        decimal discount = showDiscount ? lines.Sum(line => line.Discount) : 0m;
        decimal total = lines.Sum(line => line.Amount);
        foreach (var line in lines)
            data.Rows.Add(1, institution.Name, "STUDENT FEE BILL", "PARENT COPY",
                "", studentName,
                reference, classSection,
                DateTime.Now.ToString("dd-MMM-yyyy hh:mm tt"), "",
                line.FeeName,
                line.Amount, discount, total, 0m, "", "", discount > 0m, false,
                institution.ContactNumber, institution.Address, campusName, fatherName, ReceiptReportData.FormatPeriod(line.Period));
        foreach(DataRow row in data.Rows) {row["DocumentKind"]="Bill";row["BillSourceKey"]=sourceKey;}
        await IssuedBillReport.ShowAsync(owner,data,admissionId,new[]{admissionId});
    }
}
