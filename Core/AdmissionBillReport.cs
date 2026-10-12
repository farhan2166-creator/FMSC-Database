namespace FMSC_SMS.Core;

internal static class AdmissionBillReport
{
    internal sealed record Line(string FeeName, string Period, decimal Amount, decimal Discount);

    internal static void ShowFamily(Form? owner, InstitutionProfile.PrintIdentity institution, string parent, DataTable charges)
    {
        var data=BuildFamilyData(institution.Name,institution.ContactNumber,institution.Address,parent,charges);
        string path=Path.Combine(AppContext.BaseDirectory,"Reports","AdmissionBill.rdlc");
        RdlcReportPreview.Show(owner,"Family Bill",path,"FeeReceiptData",data,fitPageWidth:false,reportDefinition:ReceiptReportLayout.Definition(path,data));
    }
    internal static DataTable BuildFamilyData(string institutionName,string contactNumber,string address,string parent,DataTable charges)
    {
        var data=ReceiptReportData.Create();
        decimal total=charges.AsEnumerable().Sum(r=>Convert.ToDecimal(r["Due"]));
        decimal discount=charges.AsEnumerable().Sum(r=>Convert.ToDecimal(r["DiscountAmount"]));
        string references=string.Join(", ",charges.AsEnumerable().Select(r=>Convert.ToString(r["Reference"])).Distinct());
        foreach(DataRow r in charges.Rows)
            data.Rows.Add(1,institutionName,"FAMILY PAYMENT BILL","PARENT COPY","FAMILY",parent,references,"",
                DateTime.Now.ToString("dd-MMM-yyyy hh:mm tt"),"",
                $"{r["StudentName"]} · {r["Reference"]} · {r["FeeTypeName"]}",Convert.ToDecimal(r["Due"]),discount,total,0m,"","",discount>0m,false,
                contactNumber,address,"",parent,r["BillingMonth"] is DateTime month?month.ToString("MMM yyyy"):"");
        return data;
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
            data.Rows.Add(1, institution.Name, "PAYMENT BILL", "STUDENT COPY",
                (provisional?"BILL-":"ADM-") + reference, studentName,
                reference, classSection,
                DateTime.Now.ToString("dd-MMM-yyyy hh:mm tt"), "",
                line.FeeName,
                line.Amount, discount, total, 0m, "", "", discount > 0m, false,
                institution.ContactNumber, institution.Address, campusName, fatherName, line.Period);
        ReceiptReportData.GroupPeriods(data);
        string path = Path.Combine(AppContext.BaseDirectory, "Reports", "AdmissionBill.rdlc");
        RdlcReportPreview.Show(owner, provisional?"Registration Bill":"Admission Bill", path, "FeeReceiptData", data,
            fitPageWidth: false, reportDefinition: ReceiptReportLayout.Definition(path, data));
    }
}
