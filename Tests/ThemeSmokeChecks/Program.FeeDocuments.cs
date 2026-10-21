using System.Data;
using FMSC_SMS.Core;
using Microsoft.Reporting.WinForms;
using System.Xml.Linq;

internal static partial class Program
{
    private static void CheckFeeDocuments()
    {
        foreach(string retired in new[]{"AdmissionBill.rdlc","FeePaymentReceipt.rdlc"})
            Check(!File.Exists(Path.Combine(AppContext.BaseDirectory,"Reports",retired)),"Retired templates are absent from the built application");
        foreach(var (template,dataSet) in new[]{(StudentBillReport.TemplateFile,StudentBillReport.DataSetName),
            (FamilyBillReport.TemplateFile,FamilyBillReport.DataSetName),(StudentReceiptReport.TemplateFile,StudentReceiptReport.DataSetName),
            (FamilyReceiptReport.TemplateFile,FamilyReceiptReport.DataSetName)})
        {
            var definition=XDocument.Load(Path.Combine(AppContext.BaseDirectory,"Reports",template));
            XNamespace ns=definition.Root!.Name.Namespace;
            Check(definition.Descendants(ns+"DataSet").Single().Attribute("Name")!.Value==dataSet
                && dataSet==Path.GetFileNameWithoutExtension(template)+"Data","Each C# report binds the matching RDLC dataset");
        }
        var school=new InstitutionProfile.PrintIdentity("Fahim Model School & College","0300-1234567","Sarband Peshawar");
        string folder=Path.GetFullPath("artifacts/fee-documents");Directory.CreateDirectory(folder);
        int Render(string name,DataTable data,string? templateName=null,bool annual=false)
        {
            bool bill=Convert.ToString(data.Rows[0]["DocumentKind"])=="Bill";
            // Match StudentReceiptReport.Show: group months before sizing or rendering.
            using var individualRows=!bill&&!data.Columns.Contains("FamilyRowKind")?data.Copy():null;
            if(individualRows is not null)
            {
                FeeDocumentData.GroupPeriods(individualRows);
                data=individualRows;
                Check(data.AsEnumerable().Where(row=>!string.IsNullOrWhiteSpace(row.Field<string>("FeePeriod")))
                    .GroupBy(row=>(row.Field<int>("ReceiptPageKey"),row.Field<string>("FeePeriod"))).All(group=>group.Count()==1),
                    "Individual receipt samples print each month once per receipt, as the application preview does");
            }
            using var familyRows=!bill&&data.Columns.Contains("FamilyRowKind")?FamilyReceiptReport.PrepareData(data):null;
            if(familyRows is not null)data=familyRows;
            using var billRows=bill&&data.Columns.Contains("FamilyRowKind")?FamilyBillReport.PrepareData(data):null;
            if(billRows is not null)data=billRows;
            if(bill) foreach(DataRow row in data.Rows)row["ReceiptNumber"]=data.Columns.Contains("FamilyRowKind")?"FB-2026-00001":"B-2026-00001";
            string file=data.Columns.Contains("FamilyRowKind")?(bill?"FamilyBill.rdlc":"FamilyReceipt.rdlc"):(bill?"StudentBill.rdlc":"StudentReceipt.rdlc");
            file=templateName??file;
            string path=Path.Combine(AppContext.BaseDirectory,"Reports",file);
            Check(File.Exists(path),"Each document loads its own RDLC file");
            string definition=annual?FeeDocumentLayout.SizeToContent(path,data,annualSheet:true):FeeDocumentLayout.SizeToContent(path,data);
            var xml=XDocument.Parse(definition);XNamespace ns=xml.Root!.Name.Namespace;
            var receiptTable=xml.Descendants(ns+"Textbox").Single(box=>(string?)box.Attribute("Name")=="FamilyBatch")
                .Ancestors(ns+"Tablix").First();
            var printedRows=receiptTable.Element(ns+"TablixBody")!.Element(ns+"TablixRows")!.Elements(ns+"TablixRow").ToArray();
            var printedMembers=receiptTable.Element(ns+"TablixRowHierarchy")!.Descendants(ns+"TablixMember")
                .Where(member=>member.Element(ns+"TablixMembers") is null).ToArray();
            int familyDetail=Array.FindIndex(printedRows,row=>row.Descendants(ns+"Textbox")
                .Any(box=>(string?)box.Attribute("Name")=="FamilyBatch"));
            Check(printedMembers[familyDetail].Element(ns+"Visibility")?.Element(ns+"Hidden")?.Value=="true",
                "Bills and receipts suppress the redundant family student-count detail, including older snapshots");
            if(!bill)
            {
                string Caption(string name)=>xml.Descendants(ns+"Textbox").Single(box=>(string?)box.Attribute("Name")==name)
                    .Descendants(ns+"Value").First().Value;
                Check(new[]{"TotalLabelFeesCovered","TotalLabelCashReceived","BalanceLabel","TotalLabel"}
                    .All(name=>!Caption(name).StartsWith('=')),
                    "Receipt captions are editable plain text, separate from monetary formulas");
            }
            var address = xml.Descendants(ns+"Textbox").Single(box=>(string?)box.Attribute("Name")=="SchoolAddress");
            Check(address.Descendants(ns+"Value").First().Value.Contains("Fields!CampusName.Value")
                && !xml.Descendants(ns+"Textbox").Any(box=>(string?)box.Attribute("Name")=="CampusHeading"),
                "School address and campus share one header line");
            Check(xml.Descendants(ns+"Textbox").Single(box=>(string?)box.Attribute("Name")=="ReceiptHeading")
                .Descendants(ns+"FontSize").All(size=>size.Value=="6.25pt"),"The complete document title uses the smaller font");
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
            report.DataSources.Add(new ReportDataSource(xml.Descendants(ns+"DataSet").Single().Attribute("Name")!.Value,data));
            byte[] pdf=report.Render("PDF");File.WriteAllBytes(Path.Combine(folder,name+".pdf"),pdf);
            using var parsed=PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(pdf),PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            byte[] png=report.Render("IMAGE","<DeviceInfo><OutputFormat>PNG</OutputFormat><DpiX>130</DpiX><DpiY>130</DpiY></DeviceInfo>");
            File.WriteAllBytes(Path.Combine(folder,name+".png"),png);
            Check(pdf.Length>1000 && png.Length>1000,"Established RDLC renders a real PDF and PNG");
            return parsed.PageCount;
        }
        void CheckCashOnly(string name,DataTable source)
        {
            using var familyRows=source.Columns.Contains("FamilyRowKind")?FamilyReceiptReport.PrepareData(source):null;
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
            var full=XDocument.Parse(FeeDocumentLayout.SizeToContent(path,source));
            var compact=XDocument.Parse(FeeDocumentLayout.SizeToContent(path,cash));
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
                var visible=XDocument.Parse(FeeDocumentLayout.SizeToContent(path,cash));
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
            lines.Rows.Add(1,"Tuition","Class 1","A","Campus Boys",date,400m,0m,400m);
            lines.Rows.Add(1,"Transport","Class 1","A","Campus Boys",date,2500m,0m,2500m);
        }
        using(var credit=FeeReceiptDocuments.BuildData(school,h,students,lines))
        {
            Check(Convert.ToDecimal(credit.Rows[0]["TotalPaid"])==8700m && Convert.ToDecimal(credit.Rows[0]["CashReceived"])==0m
                && Convert.ToDecimal(credit.Rows[0]["CreditRemaining"])==50m,"Credit-only receipt distinguishes settlement, new money and remaining credit");
            Check(Render("individual-credit-receipt",credit)==1,"A six-line credit settlement prints on one receipt page");
            CheckCashOnly("individual-cash-only-receipt",credit);
            using var legacy=credit.Copy();foreach(DataRow row in legacy.Rows) {row["ShowSettlement"]=false;row["ReceiptNumber"]="FMSC-2026-0100";}
            Check(Render("legacy-individual-receipt",legacy)==1,"Original cash receipt fields remain printable through the RDLC template");
            Check(Render("annual-receipt-sheet",legacy,StudentReceiptReport.TemplateFile,annual:true)==1,"Annual printing uses StudentReceipt.rdlc with its native adjustment rows");
        }
        using(var partialHeader=header.Copy())
        using(var partialStudents=students.Copy())
        using(var partialLines=lines.Clone())
        {
            var payment=partialHeader.Rows[0];
            payment["CashReceived"]=200m;payment["CashApplied"]=200m;payment["CreditApplied"]=0m;
            payment["BalanceRemaining"]=2200m;payment["PaymentMethod"]="Cash";
            partialStudents.Rows[0]["CashApplied"]=200m;partialStudents.Rows[0]["CreditApplied"]=0m;
            partialStudents.Rows[0]["BalanceRemaining"]=2200m;
            partialLines.Rows.Add(1,"Transport Fee","Class 1","A","Campus Boys",new DateTime(2026,11,1),2400m,200m,0m);
            using var partial=FeeReceiptDocuments.BuildData(school,payment,partialStudents,partialLines);
            Check(partial.Rows[0].Field<decimal>("Amount")==2400m && partial.Rows[0].Field<decimal>("FeesCovered")==2400m
                && partial.Rows[0].Field<decimal>("CashReceived")==200m && partial.Rows[0].Field<decimal>("TotalPaid")==200m
                && partial.Rows[0].Field<decimal>("BalanceRemaining")==2200m,
                "A partial receipt shows the saved full fee, total, received and remaining balance independently");
            Check(Render("individual-partial-payment",partial)==1,"A partial payment receipt renders with its full charge amount");
        }
        // A family account can include debts beyond the fee lines on this
        // receipt. Keep these scopes visible rather than suggesting that the
        // account balance is simply this receipt's fee total minus its payment.
        using(var familyHeader=header.Copy())
        using(var familyStudents=students.Copy())
        using(var familyLines=lines.Clone())
        {
            var payment=familyHeader.Rows[0];
            payment["ReceiptNumber"]="FR-2026-00002";payment["PaymentMethod"]="Cash";
            payment["CashReceived"]=3000m;payment["CashApplied"]=3000m;payment["CreditApplied"]=0m;
            payment["BalanceRemaining"]=6500m;payment["CreditRemaining"]=0m;
            familyStudents.Rows[0]["CashApplied"]=3000m;familyStudents.Rows[0]["CreditApplied"]=0m;
            familyStudents.Rows[0]["BalanceRemaining"]=4300m;
            familyStudents.Rows.Add(2,"1002","Sara Example","Example Parent",0m,0m,2200m);
            familyLines.Rows.Add(1,"Tuition Fee","Class 1","A","Campus Boys",new DateTime(2026,10,1),400m,400m,0m);
            familyLines.Rows.Add(1,"Admission Fee","Class 1","A","Campus Boys",new DateTime(2026,10,1),4000m,2600m,0m);
            familyLines.Rows.Add(1,"Tuition Fee","Class 1","A","Campus Boys",new DateTime(2026,11,1),400m,0m,0m);
            using var family=FeeReceiptDocuments.BuildData(school,payment,familyStudents,familyLines);
            Check(family.Rows[0].Field<decimal>("FeesCovered")==4800m && family.Rows[0].Field<decimal>("CashReceived")==3000m
                && family.Rows[0].Field<decimal>("StudentOutstanding")==4300m && family.Rows[0].Field<decimal>("BalanceRemaining")==6500m,
                "Receipt fees, payment, child debt and family debt retain their separate financial scopes");
            Check(Render("family-partial-payment",family)==1,"A partial family payment fits with explicit student and family balance labels");
            string sourcePath=Path.Combine(AppContext.BaseDirectory,"Reports",FamilyReceiptReport.TemplateFile);
            var editable=XDocument.Load(sourcePath);XNamespace editableNs=editable.Root!.Name.Namespace;
            editable.Descendants(editableNs+"Textbox").Single(box=>(string?)box.Attribute("Name")=="DocumentDateFooter")
                .Descendants(editableNs+"Value").First().Value=new string('W',150)+" ";
            string editedPath=Path.Combine(folder,"edited-caption.rdlc");editable.Save(editedPath);
            decimal PaperHeight(string path)=>decimal.Parse(XDocument.Parse(FeeDocumentLayout.SizeToContent(path,family))
                .Descendants(editableNs+"PageHeight").Single().Value.Replace("in",""),System.Globalization.CultureInfo.InvariantCulture);
            Check(PaperHeight(editedPath)>PaperHeight(sourcePath),
                "Editing a literal date caption grows the receipt paper while its separate date formula remains intact");
            Check(Render("edited-caption-receipt",family,editedPath)==1,
                "An edited multi-run receipt caption renders without adding an unintended second page");
        }
        h["CashReceived"]=1000m;h["CashApplied"]=1000m;h["CreditApplied"]=7700m;
        students.Rows[0]["CashApplied"]=1000m;students.Rows[0]["CreditApplied"]=7700m;
        lines.Rows[1]["Cash"]=1000m;lines.Rows[1]["Credit"]=1500m;
        using(var mixed=FeeReceiptDocuments.BuildData(school,h,students,lines))Check(Render("individual-mixed-receipt",mixed)==1,"Mixed receipt uses the same layout");
        students.Rows.Add(2,"1002","Sara Example","Example Parent",300m,100m,600m);
        lines.Rows.Add(2,"Tuition","Class 2","B","Campus Boys",new DateTime(2026,11,1),1000m,300m,100m);
        h["CashReceived"]=1300m;h["CashApplied"]=1300m;h["CreditApplied"]=7800m;h["BalanceRemaining"]=600m;
        h["ReceiptNumber"]="FR-2026-00001";
        using(var family=FeeReceiptDocuments.BuildData(school,h,students,lines))
        {
            Check(family.AsEnumerable().All(row=>row.Field<string>("CampusName")=="Campus Boys"),
                "A family receipt retains the common campus in its header");
            using var mixedCampuses=lines.Copy();mixedCampuses.Rows[^1]["CampusName"]="Campus Girls";
            Check(FeeDocumentData.CampusHeading(mixedCampuses)=="Multiple campuses",
                "A family spanning campuses does not claim one child's campus for everyone");
            Check(family.AsEnumerable().Select(row=>row.Field<int>("ReceiptPageKey")).Distinct().Single()==1,"All children share one family document key");
            Check(family.AsEnumerable().Select(row=>row.Field<int>("ReceiptStudentKey")).Distinct().Count()==2,"Family receipt identifies both children");
            using(var displayed=FamilyReceiptReport.PrepareData(family))
            {
                Check(displayed.Rows.Count==lines.Rows.Count && displayed.AsEnumerable().Sum(row=>row.Field<decimal>("Amount"))==9700m,
                    "Family layout keeps full pre-payment fee amounts without summing heading or subtotal rows");
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
        charges.Columns.Add("BillingMonth",typeof(DateTime));charges.Columns.Add("DiscountAmount",typeof(decimal));charges.Columns.Add("Due",typeof(decimal));charges.Columns.Add("CampusName").DefaultValue="Campus Boys";
        charges.Rows.Add("1001","Ali Example","Tuition",new DateTime(2026,11,1),0m,400m);
        charges.Rows.Add("1001","Ali Example","Transport",new DateTime(2026,11,1),0m,600m);
        charges.Rows.Add("1001","Ali Example","Tuition",new DateTime(2026,12,1),0m,400m);
        using(var bill=FeeBillDataBuilder.BuildGrouped(school.Name,school.ContactNumber,school.Address,"Example Parent",charges))
        {
            Check(bill.AsEnumerable().All(row=>row.Field<string>("CampusName")=="Campus Boys"),
                "Grouped bills retain the campus supplied by the posted charge placement");
            using var original=StudentBillReport.FromGroupedData(bill);
            string snapshot=FeeBillDocuments.Snapshot(original);
            using var restored=FeeBillDocuments.ReadSnapshot(snapshot);
            Check(restored.Rows.Count==original.Rows.Count && restored.AsEnumerable().Sum(row=>row.Field<decimal>("Amount"))==1400m,
                "Saved bill snapshots round-trip the complete report schema and every amount");
            foreach(DataRow row in original.Rows) {row["ReceiptNumber"]="B-2026-00001";row["PaymentDateText"]="different print date";}
            Check(FeeBillDocuments.Snapshot(original)==snapshot,"Reprint date and number do not create a new issued bill");
            foreach(DataRow row in original.Rows)row["BillSourceKey"]="new posted charge";
            Check(FeeBillDocuments.Snapshot(original)!=snapshot,"Replacement charges cannot reuse an older bill merely because amounts match");
            File.WriteAllText(Path.Combine(folder,"bill-snapshot.xml"),snapshot);
            string definition=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Reports","StudentBill.rdlc"));
            Check(definition.Contains("TOTAL DUE") && definition.Contains("Bill No.") && !definition.Contains("TOTAL SETTLED"),"Bills contain bill-specific titles and totals");
            using var studentBill=StudentBillReport.FromGroupedData(bill);
            Check(Render("individual-bill",studentBill)==1,"Individual bill uses the established receipt design");
            Check(Render("individual-bill-a4",studentBill,annual:true)>=1,"A4 bill printing reuses the approved bill RDLC on a sheet");
        }
        charges.Rows.Add("1002","Sara Example","Tuition",new DateTime(2026,11,1),0m,500m);
        charges.Rows.Add("1001","Ali Example","Transport",new DateTime(2026,12,1),0m,600m);
        charges.Rows.Add("1002","Sara Example","Admission",DBNull.Value,0m,200m);
        using(var familyBill=FeeBillDataBuilder.BuildGrouped(school.Name,school.ContactNumber,school.Address,"Example Parent",charges))
            Check(Render("family-bill",familyBill)==1,"Family bill is one combined document in the same design");
    }
}
