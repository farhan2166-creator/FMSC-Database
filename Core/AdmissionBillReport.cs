namespace FMSC_SMS.Core;

internal static class AdmissionBillReport
{
    internal sealed record Line(string FeeName, string Period, decimal Amount, decimal Discount);

    internal static void ShowFamily(Form? owner, InstitutionProfile.PrintIdentity institution, string parent, DataTable charges)
    {
        var data=BuildFamilyData(institution.Name,institution.ContactNumber,institution.Address,parent,charges);
        string path=Path.Combine(AppContext.BaseDirectory,"Reports","AdmissionBill.rdlc");
        RdlcReportPreview.Show(owner,"Family Bill",path,"FeeReceiptData",data,fitPageWidth:false,reportDefinition:ReceiptReportLayout.Definition(path,data,splitFeePeriod:false));
    }
    internal static DataTable BuildFamilyData(string institutionName,string contactNumber,string address,string parent,DataTable charges)
    {
        var data=ReceiptReportData.Create();
        data.Columns.Add("FamilyRowKind",typeof(string));
        data.Columns.Add("FamilyAmountText",typeof(string));
        decimal total=charges.AsEnumerable().Sum(r=>Convert.ToDecimal(r["Due"]));
        decimal discount=charges.AsEnumerable().Sum(r=>Convert.ToDecimal(r["DiscountAmount"]));
        string references=string.Join(", ",charges.AsEnumerable().Select(r=>Convert.ToString(r["Reference"])).Distinct());
        void AddLine(string description,decimal amount,string kind){
            var line=data.Rows.Add(1,institutionName,"FAMILY PAYMENT BILL","PARENT COPY","FAMILY",parent,references,"",
                DateTime.Now.ToString("dd-MMM-yyyy hh:mm tt"),"",
                description,amount,discount,total,0m,"","",discount>0m,false,
                contactNumber,address,"",parent,"");
            line["FamilyRowKind"]=kind;line["FamilyAmountText"]=kind=="Fee"?amount.ToString("N0"):"";
        }
        // Group by the reference, never by name: siblings can share a name.
        foreach(var student in charges.AsEnumerable().GroupBy(r=>Convert.ToString(r["Reference"]))){
            AddLine($"{student.First()["StudentName"]} · {student.Key}",0m,"Student");
            foreach(var month in student.GroupBy(r=>r["BillingMonth"] is DateTime date?new DateTime(date.Year,date.Month,1):new DateTime(1,1,1)).OrderBy(g=>g.Key)){
                AddLine("  "+(month.Key.Year==1?"One-time fees":month.Key.ToString("MMM yyyy")),0m,"Month");
                foreach(var fee in month.OrderBy(r=>charges.Columns.Contains("FeeDisplayOrder")?Convert.ToInt32(r["FeeDisplayOrder"]):0).ThenBy(r=>Convert.ToString(r["FeeTypeName"])))
                    AddLine("    "+Convert.ToString(fee["FeeTypeName"]),Convert.ToDecimal(fee["Due"]),"Fee");
            }
        }
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
