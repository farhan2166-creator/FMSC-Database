using System.Data;
using System.Xml.Linq;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckFeeDocumentConsistency(InstitutionProfile.PrintIdentity school,Func<string,DataTable,int> render)
    {
        foreach(string kind in new[]{"Bill","Receipt"})
        {
            var student=XDocument.Load(Path.Combine(AppContext.BaseDirectory,"Reports","Student"+kind+".rdlc"));
            var family=XDocument.Load(Path.Combine(AppContext.BaseDirectory,"Reports","Family"+kind+".rdlc"));
            XNamespace ns=student.Root!.Name.Namespace;
            XElement Box(XDocument doc,string name)=>doc.Descendants(ns+"Textbox").Single(box=>(string?)box.Attribute("Name")==name);
            string[] Order(XDocument doc)=>doc.Descendants(ns+"Textbox").Select(box=>(string)box.Attribute("Name")!).ToArray();
            foreach(var doc in new[]{student,family})
            {
                var order=Order(doc);
                Check(Array.IndexOf(order,"ReceiptNumber")<Array.IndexOf(order,"ReceiptHeading")
                    &&Array.IndexOf(order,"FatherName")<Array.IndexOf(order,"ChildCampus")
                    &&Array.IndexOf(order,"ChildCampus")<Array.IndexOf(order,"ClassSection"),
                    "Individual and family documents share reference, title and identity order");
                Check(!order.Contains("PaymentMethodLabel"),"Payment method shares the reference row on both formats");
                var rows=doc.Descendants(ns+"TablixBody").First().Element(ns+"TablixRows")!.Elements(ns+"TablixRow").ToArray();
                var members=doc.Descendants(ns+"TablixRowHierarchy").First().Descendants(ns+"TablixMember")
                    .Where(member=>member.Element(ns+"TablixMembers") is null).ToArray();
                foreach(var (name,field) in new[]{("ChildCampus","CampusName"),("ClassSection","ClassSection")})
                {
                    int index=Array.FindIndex(rows,row=>row.Descendants(ns+"Textbox").Any(box=>(string?)box.Attribute("Name")==name));
                    string prefix=ReferenceEquals(doc,family)?"Child":"";
                    Check(members[index].Element(ns+"Visibility")!.Value.Contains("Fields!"+prefix+field+".Value"),
                        "Campus and class rows each collapse according to their own value");
                }
            }
            foreach(string name in new[]{"ReceiptNumber","PeriodHeader","FeeHeader","AmountHeader","FeePeriod","FeeDescription","LineAmount"})
                Check(XNode.DeepEquals(Box(student,name).Element(ns+"Style"),Box(family,name).Element(ns+"Style")),
                    "Individual and family columns use identical padding, alignment and rules");
        }
        using var header=new DataTable();
        foreach(string field in new[]{"ReceiptNumber","PaymentMethod","ReceiptStatus"})header.Columns.Add(field);
        header.Columns.Add("TransactionDate",typeof(DateTime));
        foreach(string field in new[]{"CashReceived","CashApplied","CreditApplied","DiscountApplied","BalanceRemaining","CreditRemaining"})header.Columns.Add(field,typeof(decimal));
        var h=header.Rows.Add("FR-2026-00021","Cash + credit","",new DateTime(2026,10,27,9,5,0),2000m,2000m,450m,500m,550m,0m);
        using var students=new DataTable();students.Columns.Add("AdmissionId",typeof(int));
        foreach(string field in new[]{"AdmissionNumber","StudentName","FatherName"})students.Columns.Add(field);
        foreach(string field in new[]{"CashApplied","CreditApplied","DiscountApplied","BalanceRemaining","CreditRemaining"})students.Columns.Add(field,typeof(decimal));
        students.Rows.Add(542,"542","Kausar","Jan Akbar",1800m,0m,200m,0m,0m);
        students.Rows.Add(249,"249","Muhammad Nouman","Jan Akbar",200m,450m,300m,550m,0m);
        using var lines=new DataTable();lines.Columns.Add("AdmissionId",typeof(int));
        foreach(string field in new[]{"FeeTypeName","ClassName","SectionName","CampusName"})lines.Columns.Add(field);
        lines.Columns.Add("FeePeriod",typeof(DateTime));
        foreach(string field in new[]{"AmountBefore","Cash","Credit","Discount"})lines.Columns.Add(field,typeof(decimal));
        lines.Rows.Add(542,"Examination Fee","Four","F","Campus Boys",new DateTime(2026,10,1),2000m,1800m,0m,200m);
        lines.Rows.Add(249,"Activity Fee","Nine","F","Campus Boys",new DateTime(2026,10,1),1500m,200m,450m,300m);
        using var source=FeeReceiptDocuments.BuildData(school,h,students,lines);
        using(var display=FamilyFeeDocumentPresentation.Receipt(source))
        {
            var child=display.AsEnumerable().First(row=>row.Field<string>("ChildAdmissionNumberText")=="249");
            Check(child.Field<string>("ChildReceiptDeductionCaption")=="Less: Discount / Waived"
                &&child.Field<decimal>("ChildReceiptDeductionAmount")==300m&&child.Field<decimal>("ChildReceiptNetPayable")==1200m
                &&child.Field<bool>("ChildShowReceiptCredit")
                &&child.Field<decimal>("ChildCashReceived")==200m&&child.Field<decimal>("ChildBalanceRemaining")==550m,
                "The family example shows 1500 less 300 concession as 1200 payable, then 450 credit and 200 money leave 550 due");
            Check(child.Field<decimal>("GroupReceiptDeductionAmount")==500m&&child.Field<decimal>("GroupReceiptNetPayable")==3000m
                &&child.Field<decimal>("GroupReceived")==2000m&&child.Field<decimal>("GroupDue")==550m,
                "The group summary keeps fees payable separate from credit, actual cash and outstanding balance");
            Check(child.Field<decimal>("ChildDiscountApplied")==300m&&child.Field<decimal>("ChildPayable")==1200m
                &&child.Field<decimal>("ChildCreditApplied")==450m&&child.Field<decimal>("GroupDiscount")==500m,
                "Separating printed payment sources preserves all original accounting values");
        }
        Check(!source.Columns.Contains("ChildReceiptDeductionAmount"),"Print preparation leaves the original receipt data unchanged");
        Check(render("aligned-family-example",source)==1,"The supplied mixed-credit family example renders on one continuous slip");
        using var oneStudent=students.Clone();oneStudent.ImportRow(students.Rows[1]);
        using var oneLine=lines.Clone();oneLine.ImportRow(lines.Rows[1]);
        h["ReceiptNumber"]="R-2026-00007";h["CashReceived"]=200m;h["CashApplied"]=200m;h["DiscountApplied"]=300m;
        using(var receipt=FeeReceiptDocuments.BuildData(school,h,oneStudent,oneLine))
        {
            using var display=FeeReceiptPresentation.Prepare(receipt);var first=display.Rows[0];
            Check(first.Field<decimal>("ReceiptDeductionAmount")==300m&&first.Field<decimal>("ReceiptNetPayable")==1200m
                &&first.Field<decimal>("CashReceived")==200m&&first.Field<decimal>("BalanceRemaining")==550m,
                "Printing that same child individually has exactly the same deductions, payable, cash and due");
            Check(render("aligned-individual-example",receipt)==1,"The matching individual receipt renders with the family layout");
        }
        foreach(var (discount,credit,settlement,caption) in new[]{(0m,450m,true,"Less: Discount / Waived"),(300m,0m,true,"Less: Discount / Waived"),(0m,0m,true,"Less: Discount / Waived"),(300m,450m,false,"Less: Discount / Waived")})
        {
            using var data=FeeDocumentData.Create();var row=data.NewRow();
            row["DiscountApplied"]=discount;row["CreditApplied"]=credit;row["Payable"]=1200m;row["ShowSettlement"]=settlement;data.Rows.Add(row);
            using var display=FeeReceiptPresentation.Prepare(data);
            Check(display.Rows[0].Field<string>("ReceiptDeductionCaption")==caption
                &&display.Rows[0].Field<bool>("ShowReceiptDeduction")== (discount>0m)
                &&display.Rows[0].Field<decimal>("ReceiptNetPayable")==1200m
                &&display.Rows[0].Field<bool>("ShowReceiptCredit")== (settlement&&credit>0m),
                "Deduction captions and visibility follow the actual concession and applied credit, respecting legacy settlements");
        }
    }
}
