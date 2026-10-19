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
        int Render(string name,DataTable data,string? templateName=null,bool annual=false)
        {
            bool bill=Convert.ToString(data.Rows[0]["DocumentKind"])=="Bill";
            using var familyRows=!bill&&data.Columns.Contains("FamilyRowKind")?CollectionReceiptReport.FamilyReceiptData(data):null;
            if(familyRows is not null)data=familyRows;
            using var billRows=bill&&data.Columns.Contains("FamilyRowKind")?AdmissionBillReport.FamilyBillData(data):null;
            if(billRows is not null)data=billRows;
            if(bill) foreach(DataRow row in data.Rows)row["ReceiptNumber"]=data.Columns.Contains("FamilyRowKind")?"FB-2026-00001":"B-2026-00001";
            string file=data.Columns.Contains("FamilyRowKind")?(bill?"FamilyBill.rdlc":"FamilyReceipt.rdlc"):(bill?"StudentBill.rdlc":"StudentReceipt.rdlc");
            file=templateName??file;
            string path=Path.Combine(AppContext.BaseDirectory,"Reports",file);
            Check(File.Exists(path),"Each document loads its own RDLC file");
            string definition=annual?ReceiptReportLayout.Definition(path,data,annualSheet:true):ReceiptReportLayout.SizeToContent(path,data);
            var xml=XDocument.Parse(definition);XNamespace ns=xml.Root!.Name.Namespace;
            Check(xml.Descendants(ns+"TablixMember").Where(member=>member.Element(ns+"Group")?.Attribute("Name")?.Value=="ReceiptPageGroup").Count()==1,"One document group is retained");
            // The operator's finalized individual receipt is the visual reference:
            // shaded header with a top rule, and borderless fee rows.
            foreach(string cell in new[]{"FeeHeader","AmountHeader"})
                Check(xml.Descendants(ns+"Textbox").Single(box=>(string?)box.Attribute("Name")==cell)
                    .Element(ns+"Style")?.Element(ns+"TopBorder")?.Element(ns+"Style")?.Value=="Solid","Fee tables retain the finalized receipt header rule");
            Check(xml.Descendants(ns+"Textbox").Any(box=>(string?)box.Attribute("Name")=="PeriodHeader")
                && xml.Descendants(ns+"Textbox").Single(box=>(string?)box.Attribute("Name")=="AmountHeader")
                    .Descendants(ns+"Value").First().Value=="Rs.","Every bill and receipt uses the approved Month / Fee / Rs. table");
            foreach(string cell in new[]{"FeeDescription","LineAmount"})
                Check(!xml.Descendants(ns+"Textbox").Single(box=>(string?)box.Attribute("Name")==cell)
                    .Element(ns+"Style")!.Elements().Where(element=>element.Name.LocalName.EndsWith("Border"))
                    .Any(border=>border.Element(ns+"Style")?.Value=="Solid"),"Fee detail rows remain borderless");
            if(bill && file=="FamilyBill.rdlc")
            {
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
        void CheckCashOnly(string name,DataTable source)
        {
            using var familyRows=source.Columns.Contains("FamilyRowKind")?CollectionReceiptReport.FamilyReceiptData(source):null;
            if(familyRows is not null)source=familyRows;
            using var cash=source.Copy();
            foreach(DataRow row in cash.Rows)
            {
                row["CreditApplied"]=0m;row["CreditRemaining"]=0m;row["ShowCreditRemaining"]=false;
                row["CashReceived"]=row["TotalPaid"];row["PaymentMethod"]="Cash";
            }
            string file=source.Columns.Contains("FamilyRowKind")?"FamilyReceipt.rdlc":"StudentReceipt.rdlc";
            string path=Path.Combine(AppContext.BaseDirectory,"Reports",file);
            XNamespace ns="http://schemas.microsoft.com/sqlserver/reporting/2008/01/reportdefinition";
            var full=XDocument.Parse(ReceiptReportLayout.SizeToContent(path,source));
            var compact=XDocument.Parse(ReceiptReportLayout.SizeToContent(path,cash));
            double Height(XDocument document)=>double.Parse(document.Descendants(ns+"PageHeight").Single().Value.Replace("in",""),System.Globalization.CultureInfo.InvariantCulture);
            Check(Height(compact)<Height(full),"Zero credit rows collapse completely, including their paper space");
            var table=compact.Descendants(ns+"Tablix").First();
            var rows=table.Descendants(ns+"TablixRow").ToArray();
            var members=table.Element(ns+"TablixRowHierarchy")!.Descendants(ns+"TablixMember")
                .Where(member=>member.Element(ns+"TablixMembers") is null).ToArray();
            foreach(string textbox in new[]{"TotalPaidCreditApplied","TotalPaidCreditRemaining"})
            {
                int index=Array.FindIndex(rows,row=>row.Descendants(ns+"Textbox").Any(box=>(string?)box.Attribute("Name")==textbox));
                string field=textbox=="TotalPaidCreditApplied"?"CreditApplied":"ShowCreditRemaining";
                Check(members[index].Element(ns+"Visibility")!.Element(ns+"Hidden")!.Value.Contains(field),"Credit visibility is applied to the complete table row");
            }
            Check(Render(name,cash)==1,"A cash-only receipt renders with credit rows collapsed");
            foreach(var (applied,remaining,label) in new[]{(0m,50m,"remaining-only"),(100m,0m,"fully-used"),(100m,50m,"used-and-remaining")})
            {
                foreach(DataRow row in cash.Rows)
                {
                    row["CreditApplied"]=applied;row["CreditRemaining"]=remaining;
                    row["ShowCreditRemaining"]=applied!=0m||remaining!=0m;
                    row["CashReceived"]=Convert.ToDecimal(row["TotalPaid"])-applied;
                }
                var visible=XDocument.Parse(ReceiptReportLayout.SizeToContent(path,cash));
                Check(applied==0m?Height(visible)>Height(compact)&&Height(visible)<Height(full):Height(visible)==Height(full),
                    "Remaining-only credit occupies one row; credit used occupies both rows even when exhausted");
                Check(Render(name+"-"+label,cash)==1,"Credit visibility scenario prints on one page");
            }
        }
        using var header=new DataTable();
        foreach(string name in new[]{"ReceiptNumber","PaymentMethod","ReceiptStatus"})header.Columns.Add(name);
        header.Columns.Add("TransactionDate",typeof(DateTime));
        foreach(string name in new[]{"CashReceived","CashApplied","CreditApplied","DiscountApplied","BalanceRemaining","CreditRemaining"})header.Columns.Add(name,typeof(decimal));
        var h=header.Rows.Add("R-2026-00001","Account Credit","",new DateTime(2026,10,19,10,15,0),0m,0m,8700m,0m,0m,50m);
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
            CheckCashOnly("individual-cash-only-receipt",credit);
            using var legacy=credit.Copy();foreach(DataRow row in legacy.Rows) {row["ShowSettlement"]=false;row["ReceiptNumber"]="FMSC-2026-0100";}
            Check(Render("legacy-individual-receipt",legacy)==1,"Original cash receipt fields remain printable through the RDLC template");
            Check(Render("annual-receipt-template",legacy,"FeePaymentReceipt.rdlc")==1,"The editable annual receipt template prints separate month and fee columns directly");
            Check(Render("annual-receipt-sheet",legacy,"FeePaymentReceipt.rdlc",annual:true)==1,"Annual printing retains adjustment details without duplicating the fee columns");
        }
        h["CashReceived"]=1000m;h["CashApplied"]=1000m;h["CreditApplied"]=7700m;
        students.Rows[0]["CashApplied"]=1000m;students.Rows[0]["CreditApplied"]=7700m;
        lines.Rows[1]["Cash"]=1000m;lines.Rows[1]["Credit"]=1500m;
        using(var mixed=CollectionReceiptReport.Build(school,h,students,lines))Check(Render("individual-mixed-receipt",mixed)==1,"Mixed receipt uses the same layout");
        students.Rows.Add(2,"1002","Sara Example","Example Parent",300m,100m,600m);
        lines.Rows.Add(2,"Tuition","Class 2","B","Main Campus",new DateTime(2026,11,1),1000m,300m,100m);
        h["CashReceived"]=1300m;h["CashApplied"]=1300m;h["CreditApplied"]=7800m;h["BalanceRemaining"]=600m;
        h["ReceiptNumber"]="FR-2026-00001";
        using(var family=CollectionReceiptReport.Build(school,h,students,lines))
        {
            Check(family.AsEnumerable().Select(row=>row.Field<int>("ReceiptPageKey")).Distinct().Single()==1,"All children share one family document key");
            Check(family.AsEnumerable().Count(row=>row.Field<string>("FamilyRowKind")=="Student")==2,"Family receipt identifies both children");
            using(var displayed=CollectionReceiptReport.FamilyReceiptData(family))
            {
                Check(displayed.Rows.Count==lines.Rows.Count && displayed.AsEnumerable().Sum(row=>row.Field<decimal>("Amount"))==9100m,
                    "Family layout keeps every fee and settlement amount without summing heading or subtotal rows");
                Check(displayed.AsEnumerable().Select(row=>row.Field<int>("ReceiptStudentKey")).Distinct().Count()==2
                    && displayed.AsEnumerable().Count(row=>!string.IsNullOrWhiteSpace(row.Field<string>("FeePeriod")))==4,
                    "Family months appear once per month per child under separate student groups");
                Check(displayed.Rows[^1].Field<decimal>("StudentOutstanding")==600m
                    && displayed.Rows[0].Field<decimal>("StudentSettled")==8700m,"Per-child settlement and outstanding values remain unchanged");
            }
            Check(Render("family-mixed-receipt",family)==1,"Family receipt has no forced page per child");
            CheckCashOnly("family-cash-only-receipt",family);
            using var legacyFamily=family.Copy();foreach(DataRow row in legacyFamily.Rows) row["ShowSettlement"]=false;
            Check(Render("legacy-family-receipt",legacyFamily)==1,"Legacy family receipts use the same combined RDLC layout");
        }
        using var charges=new DataTable();foreach(string name in new[]{"Reference","StudentName","FeeTypeName"})charges.Columns.Add(name);
        charges.Columns.Add("BillingMonth",typeof(DateTime));charges.Columns.Add("DiscountAmount",typeof(decimal));charges.Columns.Add("Due",typeof(decimal));
        charges.Rows.Add("1001","Ali Example","Tuition",new DateTime(2026,11,1),0m,400m);
        charges.Rows.Add("1001","Ali Example","Transport",new DateTime(2026,11,1),0m,600m);
        charges.Rows.Add("1001","Ali Example","Tuition",new DateTime(2026,12,1),0m,400m);
        using(var bill=AdmissionBillReport.BuildFamilyData(school.Name,school.ContactNumber,school.Address,"Example Parent",charges))
        {
            using var original=AdmissionBillReport.StudentBillData(bill);
            string snapshot=IssuedBillReport.Snapshot(original);
            using var restored=IssuedBillReport.ReadSnapshot(snapshot);
            Check(restored.Rows.Count==original.Rows.Count && restored.AsEnumerable().Sum(row=>row.Field<decimal>("Amount"))==1400m,
                "Saved bill snapshots round-trip the complete report schema and every amount");
            foreach(DataRow row in original.Rows) {row["ReceiptNumber"]="B-2026-00001";row["PaymentDateText"]="different print date";}
            Check(IssuedBillReport.Snapshot(original)==snapshot,"Reprint date and number do not create a new issued bill");
            foreach(DataRow row in original.Rows)row["BillSourceKey"]="new posted charge";
            Check(IssuedBillReport.Snapshot(original)!=snapshot,"Replacement charges cannot reuse an older bill merely because amounts match");
            File.WriteAllText(Path.Combine(folder,"bill-snapshot.xml"),snapshot);
            string definition=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Reports","StudentBill.rdlc"));
            Check(definition.Contains("TOTAL DUE") && definition.Contains("Bill No.") && !definition.Contains("TOTAL SETTLED"),"Bills contain bill-specific titles and totals");
            using var studentBill=AdmissionBillReport.StudentBillData(bill);
            Check(Render("individual-bill",studentBill)==1,"Individual bill uses the established receipt design");
            Check(Render("admission-bill-template",studentBill,"AdmissionBill.rdlc")==1,"The older admission template also prints month and fee columns directly");
        }
        charges.Rows.Add("1002","Sara Example","Tuition",new DateTime(2026,11,1),0m,500m);
        charges.Rows.Add("1001","Ali Example","Transport",new DateTime(2026,12,1),0m,600m);
        charges.Rows.Add("1002","Sara Example","Admission",DBNull.Value,0m,200m);
        using(var familyBill=AdmissionBillReport.BuildFamilyData(school.Name,school.ContactNumber,school.Address,"Example Parent",charges))
            Check(Render("family-bill",familyBill)==1,"Family bill is one combined document in the same design");
    }
}
