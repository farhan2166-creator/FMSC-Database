using System.Globalization;
using System.Xml.Linq;

namespace FMSC_SMS.Core;

internal static class GridReportPreview
{
    internal sealed record Report(string Definition, DataTable Data);
    internal static Report Build(string institution,string title,string subtitle,DataTable source,string printedBy,string moneyFormat="#,##0.##")
    {
        if(source.Columns.Count==0)throw new ArgumentException("Choose at least one report column.",nameof(source));
        XNamespace ns="http://schemas.microsoft.com/sqlserver/reporting/2008/01/reportdefinition";
        XElement E(string name,params object[] content)=>new(ns+name,content);
        string Inch(double value)=>value.ToString("0.####",CultureInfo.InvariantCulture)+"in";
        var columns=GridReportColumns.Create(source,moneyFormat);
        bool landscape=columns.Count>6||columns.Sum(column=>column.Minimum)>7.25;
        double width=landscape?10.67:7.25;
        var bands=GridReportColumns.Bands(columns,width);
        var data=new DataTable("ReportData");
        for(int i=0;i<columns.Count;i++)data.Columns.Add("C"+i);
        var receiptLayout = CollectionReceiptGrouping.Layout(source);
        if (receiptLayout is not null)
        {
            data.Columns.Add("GroupKey"); data.Columns.Add("GroupLabel");
            for (int i = 0; i < columns.Count; i++) if (columns[i].Numeric) data.Columns.Add("N" + i, typeof(decimal));
        }
        for (int rowIndex = 0; rowIndex < source.Rows.Count; rowIndex++)
        {
            if (receiptLayout is not null && receiptLayout[rowIndex].Kind != "Detail") continue;
            DataRow row = source.Rows[rowIndex], printed = data.NewRow();
            for (int i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                printed["C" + i] = string.Join("\n", column.Fields.Select(index => GridReportColumns.Value(row, index, moneyFormat)).Where(value => value.Length > 0));
                if (receiptLayout is not null && column.Numeric)
                    printed["N" + i] = row.IsNull(column.Fields[0]) ? 0m : Convert.ToDecimal(row[column.Fields[0]]);
            }
            if (receiptLayout is not null)
            {
                printed["GroupKey"] = receiptLayout[rowIndex].GroupKey;
                printed["GroupLabel"] = receiptLayout[rowIndex].Label;
            }
            data.Rows.Add(printed);
        }

        XElement Text(string name,string value,bool bold=false,string align="Left",string size="9pt",string color="#182C40")=>
            E("Textbox",new XAttribute("Name",name),E("CanGrow",true),E("CanShrink",true),
                E("Paragraphs",E("Paragraph",E("TextRuns",E("TextRun",E("Value",value),
                    E("Style",E("FontFamily","Segoe UI"),E("FontSize",size),E("FontWeight",bold?"Bold":"Normal"),E("Color",color)))),E("Style",E("TextAlign",align)))),
                E("Style",E("VerticalAlign","Middle"),E("PaddingLeft","4pt"),E("PaddingRight","4pt"),E("PaddingTop","3pt"),E("PaddingBottom","3pt")));
        XElement Position(XElement text,double left,double top,double textWidth,double height)
        {text.Add(E("Top",Inch(top)),E("Left",Inch(left)),E("Height",Inch(height)),E("Width",Inch(textWidth)));return text;}
        var items=new List<XElement>();
        for(int bandIndex=0;bandIndex<bands.Count;bandIndex++)
        {
            int[] indices=bands[bandIndex];double minimum=indices.Sum(i=>columns[i].Minimum),weight=indices.Sum(i=>columns[i].Weight);
            double[] widths=indices.Select(i=>minimum<=width?columns[i].Minimum+(width-minimum)*columns[i].Weight/weight:width*columns[i].Minimum/minimum).ToArray();
            var headings=new List<XElement>();var details=new List<XElement>();
            foreach(int index in indices)
            {
                var column=columns[index];string alignment=column.Numeric?"Right":"Left";
                var heading=Text($"Heading{bandIndex}_{index}",column.Caption,true,alignment,"8.5pt");
                heading.Element(ns+"Style")!.Add(E("BackgroundColor","#EAF0F5"),E("BottomBorder",E("Style","Solid"),E("Width","0.8pt"),E("Color","#486780")));
                headings.Add(E("TablixCell",E("CellContents",heading)));
                var detail=Text($"Value{bandIndex}_{index}",$"=Fields!C{index}.Value",false,alignment);
                detail.Descendants(ns+"FontWeight").First().Value="=IIf(Left(Fields!C0.Value, 5) = \"TOTAL\" Or Left(Fields!C0.Value, 3) = \"NET\", \"Bold\", \"Normal\")";
                detail.Element(ns+"Style")!.Add(E("BottomBorder",E("Style","Solid"),E("Width","0.25pt"),E("Color","#DCE4EC")),
                    E("BackgroundColor","=IIf(RowNumber(Nothing) Mod 2 = 0, \"#F5F7FA\", \"White\")"));
                details.Add(E("TablixCell",E("CellContents",detail)));
            }
            var table=E("Tablix",new XAttribute("Name","ReportRows"+bandIndex),
                E("TablixBody",E("TablixColumns",widths.Select(w=>E("TablixColumn",E("Width",Inch(w))))),
                    E("TablixRows",E("TablixRow",E("Height","0.42in"),E("TablixCells",headings)),E("TablixRow",E("Height","0.26in"),E("TablixCells",details)))),
                E("TablixColumnHierarchy",E("TablixMembers",indices.Select(_=>E("TablixMember")))),
                E("TablixRowHierarchy",E("TablixMembers",E("TablixMember",E("KeepWithGroup","After"),E("RepeatOnNewPage",true)),
                    E("TablixMember",E("Group",new XAttribute("Name","DetailRows"+bandIndex)),E("KeepTogether",true)))),
                E("DataSetName","ReportData"),E("Top",bands.Count>1?"0.25in":"0in"),E("Left","0in"),E("Height","0.68in"),E("Width",Inch(width)),E("Style"));
            if (receiptLayout is not null) CollectionReceiptRdlc.Apply(table, indices, columns.Select(column => column.Numeric).ToArray(), bandIndex);
            if(bands.Count==1)items.Add(table);
            else
            {
                var note=Position(Text("Section"+bandIndex,$"Details {bandIndex+1} of {bands.Count} - student/reference columns repeat for matching records",false,"Left","8pt","#536578"),0,0,width,.23);
                var rectangle=E("Rectangle",new XAttribute("Name","SectionBlock"+bandIndex),E("ReportItems",note,table),
                    E("KeepTogether",false),E("Top",Inch(bandIndex*.95)),E("Left","0in"),E("Height","0.93in"),E("Width",Inch(width)),E("Style"));
                if(bandIndex>0)rectangle.Add(E("PageBreak",E("BreakLocation","Start")));
                items.Add(rectangle);
            }
        }
        var summaryKeys=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"Receipts","Money Received","Gross Received","Refunds","Corrections","Credit Applied","Adjusted Collection","Net Collection","Students Owing","Total Outstanding","Overdue","90+ Days","Gross Billed","Discounts","Net Billed","Charges","Students","Rows shown","Outstanding shown","Students holding credit","Credit held","Assignments"};
        var metrics=new List<(string Label,string Value)>();var filters=new List<string>();
        foreach(string part in System.Text.RegularExpressions.Regex.Split(subtitle.Trim(),@"\s{3,}"))
        {
            int colon=part.IndexOf(':');string key=colon>=0?part[..colon].Trim():"";
            if(summaryKeys.Contains(key))metrics.Add((key,part[(colon+1)..].Trim()));
            else if(part.Length>0)filters.Add(part.Replace("Period: Any start to Any end","Period: All dates",StringComparison.OrdinalIgnoreCase));
        }
        string scope=string.Join("   |   ",filters);
        using var bitmap=new Bitmap(1,1);using var graphics=Graphics.FromImage(bitmap);
        double TextHeight(string text,float points)
        {
            if(text.Length==0)return 0;
            using var font=new Font("Segoe UI",points);
            var measured=graphics.MeasureString(text,font,new SizeF((float)(width*graphics.DpiX)-8,10000),StringFormat.GenericTypographic);
            return Math.Max(points/72*1.3,measured.Height/graphics.DpiY)+.07;
        }
        double top=0;
        var headerItems=new List<XElement>();
        double schoolHeight=TextHeight(institution,14);
        headerItems.Add(Position(Text("Institution",institution,true,"Left","14pt","#25465E"),0,top,width,schoolHeight));top+=schoolHeight+.02;
        double titleHeight=TextHeight(title,12);
        headerItems.Add(Position(Text("ReportTitle",title,true,"Left","12pt"),0,top,width,titleHeight));top+=titleHeight+.03;
        if(scope.Length>0){double scopeHeight=TextHeight(scope,8.5f);headerItems.Add(Position(Text("Filters",scope,false,"Left","8.5pt","#536578"),0,top,width,scopeHeight));top+=scopeHeight+.08;}
        if(metrics.Count>0)
        {
            int count=Math.Min(metrics.Count,landscape?6:3);double metricWidth=width/count;
            for(int index=0;index<metrics.Count;index++)
            {
                double x=(index%count)*metricWidth,y=top+(index/count)*.53;
                var label=Position(Text("MetricLabel"+index,metrics[index].Label,false,"Left","8pt","#536578"),x,y,metricWidth,.24);
                var value=Position(Text("MetricValue"+index,metrics[index].Value,true,"Left","11pt"),x,y+.24,metricWidth,.26);
                label.Element(ns+"Style")!.Add(E("BackgroundColor","#F0F4F8"));value.Element(ns+"Style")!.Add(E("BackgroundColor","#F0F4F8"));
                headerItems.Add(label);headerItems.Add(value);
            }
            top+=Math.Ceiling(metrics.Count/(double)count)*.53+.08;
        }
        string safeBy=printedBy.Replace("\"","\"\"").Replace("\r"," ").Replace("\n"," ");
        var footerItems=new[]{
            Position(Text("Stamp","=\"Printed \" & Format(Now(), \"dd-MMM-yyyy HH:mm\") & \"  |  Printed by: "+safeBy+"\"",false,"Left","7.5pt","#536578"),0,0,width*.75,.27),
            Position(Text("PageNumber","=\"Page \" & Globals!PageNumber & \" of \" & Globals!TotalPages",false,"Right","7.5pt","#536578"),width*.75,0,width*.25,.27)};
        var document=new XDocument(E("Report",
            E("DataSources",E("DataSource",new XAttribute("Name","Source"),E("ConnectionProperties",E("DataProvider","System.Data.DataSet"),E("ConnectString","")))),
            E("DataSets",E("DataSet",new XAttribute("Name","ReportData"),E("Query",E("DataSourceName","Source"),E("CommandText","")),
                E("Fields",data.Columns.Cast<DataColumn>().Select(column=>E("Field",new XAttribute("Name",column.ColumnName),E("DataField",column.ColumnName)))))),
            E("Body",E("ReportItems",items),E("Height",Inch(bands.Count==1?.68:bands.Count*.95)),E("Style")),E("Width",Inch(width)),
            E("Page",E("PageHeader",E("Height",Inch(top)),E("PrintOnFirstPage",true),E("PrintOnLastPage",true),E("ReportItems",headerItems),E("Style")),
                E("PageFooter",E("Height","0.30in"),E("PrintOnFirstPage",true),E("PrintOnLastPage",true),E("ReportItems",footerItems),E("Style")),
                E("PageHeight",landscape?"8.27in":"11.69in"),E("PageWidth",landscape?"11.69in":"8.27in"),
                E("LeftMargin","0.5in"),E("RightMargin","0.5in"),E("TopMargin","0.35in"),E("BottomMargin","0.35in"),E("Style")),E("ConsumeContainerWhitespace",true)));
        return new(document.ToString(),data);
    }
    internal static void Show(IWin32Window? owner, string institution, string title, string subtitle, DataTable source, string printedBy, string moneyFormat = "#,##0.##")
    {
        var report = Build(institution, title, subtitle, source, printedBy, moneyFormat);
        RdlcReportPreview.Show(owner, title, "", "ReportData", report.Data, reportDefinition: report.Definition);
    }
}
