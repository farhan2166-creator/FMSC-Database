using System.Data;
using System.Xml.Linq;
using Microsoft.Reporting.NETCore;
using FMSC_SMS.Core;
XNamespace ns="http://schemas.microsoft.com/sqlserver/reporting/2008/01/reportdefinition";
XElement E(string name,params object[] content)=>new(ns+name,content);
XElement Box(string name,string value)=>E("Textbox",new XAttribute("Name",name),E("CanGrow",true),E("Paragraphs",E("Paragraph",E("TextRuns",E("TextRun",E("Value",value),E("Style",E("FontFamily","Arial"),E("FontSize","9pt"),E("FontWeight","Normal")))),E("Style",E("TextAlign","Left")))),E("Style"));
XElement Row(string name,string first,string second)=>E("TablixRow",E("Height",".25in"),E("TablixCells",E("TablixCell",E("CellContents",Box(name+"0",first))),E("TablixCell",E("CellContents",Box(name+"1",second)))));
var table=E("Tablix",new XAttribute("Name","ReportRows0"),E("TablixBody",E("TablixColumns",E("TablixColumn",E("Width","3in")),E("TablixColumn",E("Width","2in"))),E("TablixRows",Row("Heading","Receipt","Amount"),Row("Detail","=Fields!C0.Value","=Fields!C1.Value"))),E("TablixColumnHierarchy",E("TablixMembers",E("TablixMember"),E("TablixMember"))),E("TablixRowHierarchy",E("TablixMembers",E("TablixMember",E("KeepWithGroup","After"),E("RepeatOnNewPage",true)),E("TablixMember",E("Group",new XAttribute("Name","DetailRows0")),E("KeepTogether",true)))),E("DataSetName","ReportData"),E("Top","0in"),E("Left","0in"),E("Height",".5in"),E("Width","5in"),E("Style"));
CollectionReceiptRdlc.Apply(table,new[]{0,1},new[]{false,true},0);
var data=new DataTable();foreach(string c in new[]{"C0","C1","GroupKey","GroupLabel"})data.Columns.Add(c);data.Columns.Add("N1",typeof(decimal));data.Rows.Add("R1","100","A","Campus Boys",100m);data.Rows.Add("R2","200","A","Campus Boys",200m);data.Rows.Add("R3","-30","B","Campus Girls",-30m);
var doc=new XDocument(E("Report",E("DataSources",E("DataSource",new XAttribute("Name","Source"),E("ConnectionProperties",E("DataProvider","System.Data.DataSet"),E("ConnectString","")))),E("DataSets",E("DataSet",new XAttribute("Name","ReportData"),E("Query",E("DataSourceName","Source"),E("CommandText","")),E("Fields",data.Columns.Cast<DataColumn>().Select(c=>E("Field",new XAttribute("Name",c.ColumnName),E("DataField",c.ColumnName)))))),E("Body",E("ReportItems",table),E("Height",".5in"),E("Style")),E("Width","5in"),E("Page",E("PageHeight","11.69in"),E("PageWidth","8.27in"),E("LeftMargin",".5in"),E("RightMargin",".5in"),E("TopMargin",".5in"),E("BottomMargin",".5in"),E("Style"))));
using var report=new LocalReport();report.LoadReportDefinition(new StringReader(doc.ToString()));report.DataSources.Add(new ReportDataSource("ReportData",data));
_ = report.GetParameters();
Console.WriteLine("PASS: grouped RDLC compiles in the reporting engine");
byte[] xml=report.Render("XML");
var output=XDocument.Load(new MemoryStream(xml));
var printed=output.Descendants("ReportRows0").Single();
if((string?)printed.Attribute("ReceiptTotal0_1_True")!="270")throw new Exception("Grand total must use each receipt once.");
var groups=printed.Descendants("ReceiptGroup0").ToArray();
if(groups.Length!=2 || (string?)groups[0].Attribute("ReceiptTotal0_1_False")!="300" || (string?)groups[1].Attribute("ReceiptTotal0_1_False")!="-30")
    throw new Exception("Group subtotals must preserve signed corrections.");
if(printed.Descendants("DetailRows0").Count()!=3)throw new Exception("All three original receipts must remain.");
Console.WriteLine("PASS: grouped report processes original receipts and subtotal/grand-total expressions to XML");

// Compile the actual edited financial definitions, rather than just generated
// table markup. XML rendering exercises expressions without Windows font APIs.
var checkout=new DirectoryInfo(AppContext.BaseDirectory);
while(checkout is not null && !File.Exists(Path.Combine(checkout.FullName,"FMSC_SMS.csproj")))checkout=checkout.Parent;
if(checkout is null)throw new Exception("Run financial RDLC checks from the repository checkout.");
foreach(string template in new[]{"StudentBill","FamilyBill","StudentReceipt","FamilyReceipt","FeeChanges","ClassOutstandingCharges"})
{
    string path=Path.Combine(checkout.FullName,"Pages","Reports","Templates",template+".rdlc");
    var definition=XDocument.Load(path);
    var dataset=definition.Descendants(ns+"DataSet").Single();
    using var sample=new DataTable();
    foreach(var field in dataset.Element(ns+"Fields")!.Elements(ns+"Field"))
    {
        string typeName=field.Elements().FirstOrDefault(e=>e.Name.LocalName=="TypeName")?.Value??"System.String";
        string name=(string)field.Attribute("Name")!;
        // These templates omit type hints. Supply real deduction magnitudes and
        // visibility flags so the changed expressions are evaluated, not hidden.
        Type type=name is "GroupDiscount" or "GroupReceiptDeductionAmount" or "GroupPreviouslySettled"?typeof(decimal)
            :name.StartsWith("Show",StringComparison.Ordinal)||name.StartsWith("ChildShow",StringComparison.Ordinal)?typeof(bool)
            :Type.GetType(typeName)??typeof(string);
        sample.Columns.Add(name,type);
    }
    var sampleRow=sample.NewRow();
    foreach(DataColumn column in sample.Columns)
        sampleRow[column]=column.DataType==typeof(int)?1:column.DataType==typeof(decimal)?100m
            :column.DataType==typeof(bool)?true:column.DataType==typeof(DateTime)?new DateTime(2026,11,2):"Example";
    foreach(string field in new[]{"RowType","LineType"})if(sample.Columns.Contains(field))sampleRow[field]="Fee";
    foreach(string field in new[]{"ReceiptPageKey","ReceiptStudentKey","BillStudentKey"})if(sample.Columns.Contains(field))sampleRow[field]=1;
    sample.Rows.Add(sampleRow);
    using var actual=new LocalReport();actual.LoadReportDefinition(new StringReader(definition.ToString()));
    actual.DataSources.Add(new ReportDataSource((string)dataset.Attribute("Name")!,sample));
    _=actual.GetParameters();
    var xmlBytes=actual.Render("XML");
    if(xmlBytes.Length==0 || System.Text.Encoding.UTF8.GetString(xmlBytes).Contains("#Error",StringComparison.Ordinal))
        throw new Exception(template+" did not process financial expressions successfully.");
    var rendered=XDocument.Load(new MemoryStream(xmlBytes));
    foreach(string box in template=="FamilyBill"?new[]{"GroupDiscountAmount","GroupPreviouslySettledAmount"}
        :template=="FamilyReceipt"?new[]{"GroupDiscountAmount"}:Array.Empty<string>())
    {
        var amounts=rendered.Descendants().Attributes().Where(attribute=>attribute.Name.LocalName==box).Select(attribute=>attribute.Value).ToArray();
        if(amounts.Length==0||amounts.Any(value=>value!="-100"))
            throw new Exception(template+" must print "+box+" as a negative deduction; actual: "+string.Join(",",amounts));
    }
    Console.WriteLine("PASS: "+template+" actual RDLC compiles and processes synthetic fields to XML");
}
