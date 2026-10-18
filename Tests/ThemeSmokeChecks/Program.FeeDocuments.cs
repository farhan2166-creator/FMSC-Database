using System.Data;
using FMSC_SMS.Core;
using Microsoft.Reporting.WinForms;
using System.Xml.Linq;

internal static partial class Program
{
    private static void CheckFeeDocuments()
    {
        var school=new InstitutionProfile.PrintIdentity("Fictional Meadow School","0300-1234567","Synthetic School Road");
        string folder=Path.GetFullPath("artifacts/fee-documents");Directory.CreateDirectory(folder);
        string template=Path.Combine(AppContext.BaseDirectory,"Reports","FeePaymentReceipt.rdlc");
        int Render(string name,DataTable data)
        {
            bool bill=Convert.ToString(data.Rows[0]["DocumentKind"])=="Bill";
            using var billRows=bill&&data.Columns.Contains("FamilyRowKind")?AdmissionBillReport.FamilyBillData(data):null;
            if(billRows is not null)data=billRows;
            string file=data.Columns.Contains("FamilyRowKind")?(bill?"FamilyBill.rdlc":"FamilyReceipt.rdlc"):(bill?"StudentBill.rdlc":"StudentReceipt.rdlc");
            string path=Path.Combine(AppContext.BaseDirectory,"Reports",file);
            Check(File.Exists(path),"Each document loads its own RDLC file");
            string definition=ReceiptReportLayout.SizeToContent(path,data);
            var xml=XDocument.Parse(definition);XNamespace ns=xml.Root!.Name.Namespace;
            Check(xml.Descendants(ns+"TablixMember").Where(member=>member.Element(ns+"Group")?.Attribute("Name")?.Value=="ReceiptPageGroup").Count()==1,"One document group is retained");
            if(bill)
            {
                foreach(string cell in new[]{"PeriodHeader","FeeHeader","AmountHeader","FeePeriod","FeeDescription","LineAmount"})
                    Check(xml.Descendants(ns+"Textbox").Single(box=>(string?)box.Attribute("Name")==cell)
                        .Element(ns+"Style")?.Element(ns+"Border")?.Element(ns+"Style")?.Value=="Solid","Bill fee cells have a visible table grid");
                if(billRows is not null)
                    Check(xml.Descendants(ns+"Group").Any(group=>(string?)group.Attribute("Name")=="BillStudentGroup"),"Family bills group real fee rows under separate student headings");
            }
            using var report=new LocalReport();report.LoadReportDefinition(new StringReader(definition));
            report.DataSources.Add(new ReportDataSource("FeeReceiptData",data));
            byte[] pdf=report.Render("PDF");File.WriteAllBytes(Path.Combine(folder,name+".pdf"),pdf);
            using var parsed=PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(pdf),PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            byte[] png=report.Render("IMAGE","<DeviceInfo><OutputFormat>PNG</OutputFormat><DpiX>130</DpiX><DpiY>130</DpiY></DeviceInfo>");
            File.WriteAllBytes(Path.Combine(folder,name+".png"),png);
            Check(pdf.Length>1000 && png.Length>1000,"Established RDLC renders a real PDF and PNG");
            return parsed.PageCount;
        }
        using var header=new DataTable();
        foreach(string name in new[]{"ReceiptNumber","PaymentMethod","ReceiptStatus"})header.Columns.Add(name);
        header.Columns.Add("TransactionDate",typeof(DateTime));
        foreach(string name in new[]{"CashReceived","CashApplied","CreditApplied","DiscountApplied","BalanceRemaining","CreditRemaining"})header.Columns.Add(name,typeof(decimal));
        var h=header.Rows.Add("FMSC-2026-0100","Account Credit","",new DateTime(2026,10,19,10,15,0),0m,0m,8700m,0m,0m,50m);
        using var students=new DataTable();
        students.Columns.Add("AdmissionId",typeof(int));foreach(string name in new[]{"AdmissionNumber","StudentName","FatherName"})students.Columns.Add(name);
        foreach(string name in new[]{"CashApplied","CreditApplied","BalanceRemaining"})students.Columns.Add(name,typeof(decimal));
        students.Rows.Add(1,"1001","Ali Example","Example Parent",0m,8700m,0m);
        using var lines=new DataTable();lines.Columns.Add("AdmissionId",typeof(int));
        foreach(string name in new[]{"FeeTypeName","ClassName","SectionName","CampusName"})lines.Columns.Add(name);
        lines.Columns.Add("FeePeriod",typeof(DateTime));foreach(string name in new[]{"AmountBefore","Cash","Credit"})lines.Columns.Add(name,typeof(decimal));
        for(int month=11;month<=13;month++)
        {
            var date=new DateTime(2026,11,1).AddMonths(month-11);
            lines.Rows.Add(1,"Tuition","Class 1","A","Main Campus",date,400m,0m,400m);
            lines.Rows.Add(1,"Transport","Class 1","A","Main Campus",date,2500m,0m,2500m);
        }
        using(var credit=CollectionReceiptReport.Build(school,h,students,lines))
        {
            Check(Convert.ToDecimal(credit.Rows[0]["TotalPaid"])==8700m && Convert.ToDecimal(credit.Rows[0]["CashReceived"])==0m
                && Convert.ToDecimal(credit.Rows[0]["CreditRemaining"])==50m,"Credit-only receipt distinguishes settlement, new money and remaining credit");
            Check(Render("individual-credit-receipt",credit)==1,"A six-line credit settlement prints on one receipt page");
            using var legacy=credit.Copy();foreach(DataRow row in legacy.Rows) row["ShowSettlement"]=false;
            Check(Render("legacy-individual-receipt",legacy)==1,"Original cash receipt fields remain printable through the RDLC template");
        }
        h["CashReceived"]=1000m;h["CashApplied"]=1000m;h["CreditApplied"]=7700m;
        students.Rows[0]["CashApplied"]=1000m;students.Rows[0]["CreditApplied"]=7700m;
        lines.Rows[1]["Cash"]=1000m;lines.Rows[1]["Credit"]=1500m;
        using(var mixed=CollectionReceiptReport.Build(school,h,students,lines))Check(Render("individual-mixed-receipt",mixed)==1,"Mixed receipt uses the same layout");
        students.Rows.Add(2,"1002","Sara Example","Example Parent",300m,100m,600m);
        lines.Rows.Add(2,"Tuition","Class 2","B","Main Campus",new DateTime(2026,11,1),1000m,300m,100m);
        h["CashReceived"]=1300m;h["CashApplied"]=1300m;h["CreditApplied"]=7800m;h["BalanceRemaining"]=600m;
        using(var family=CollectionReceiptReport.Build(school,h,students,lines))
        {
            Check(family.AsEnumerable().Select(row=>row.Field<int>("ReceiptPageKey")).Distinct().Single()==1,"All children share one family document key");
            Check(family.AsEnumerable().Count(row=>row.Field<string>("FamilyRowKind")=="Student")==2,"Family receipt identifies both children");
            Check(Render("family-mixed-receipt",family)==1,"Family receipt has no forced page per child");
            using var legacyFamily=family.Copy();foreach(DataRow row in legacyFamily.Rows) row["ShowSettlement"]=false;
            Check(Render("legacy-family-receipt",legacyFamily)==1,"Legacy family receipts use the same combined RDLC layout");
        }
        using var charges=new DataTable();foreach(string name in new[]{"Reference","StudentName","FeeTypeName"})charges.Columns.Add(name);
        charges.Columns.Add("BillingMonth",typeof(DateTime));charges.Columns.Add("DiscountAmount",typeof(decimal));charges.Columns.Add("Due",typeof(decimal));
        charges.Rows.Add("1001","Ali Example","Tuition",new DateTime(2026,11,1),0m,400m);
        using(var bill=AdmissionBillReport.BuildFamilyData(school.Name,school.ContactNumber,school.Address,"Example Parent",charges))
        {
            string definition=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Reports","StudentBill.rdlc"));
            Check(definition.Contains("TOTAL DUE") && definition.Contains("Bill reference") && !definition.Contains("TOTAL SETTLED"),"Bills contain bill-specific titles and totals");
            using var studentBill=AdmissionBillReport.StudentBillData(bill);
            Check(Render("individual-bill",studentBill)==1,"Individual bill uses the established receipt design");
        }
        charges.Rows.Add("1002","Sara Example","Tuition",new DateTime(2026,11,1),0m,500m);
        charges.Rows.Add("1001","Ali Example","Transport",new DateTime(2026,12,1),0m,600m);
        charges.Rows.Add("1002","Sara Example","Admission",DBNull.Value,0m,200m);
        using(var familyBill=AdmissionBillReport.BuildFamilyData(school.Name,school.ContactNumber,school.Address,"Example Parent",charges))
            Check(Render("family-bill",familyBill)==1,"Family bill is one combined document in the same design");
    }
}
