using System.Globalization;

namespace FMSC_SMS.Core;

// Print presentation only. Every selected source field is retained; related
// identity fields share a cell so amounts and references have room to read.
internal static class GridReportColumns
{
    internal sealed record Column(string Caption,int[] Fields,bool Numeric,double Minimum,double Weight);

    internal static string Value(DataRow row,int index,string moneyFormat)
    {
        object value=row[index];
        return value switch {
            DBNull=>"",
            decimal money=>money.ToString(moneyFormat,CultureInfo.CurrentCulture),
            DateTime date=>date.ToString(row.Table.Columns[index].ColumnName.Contains("Month",StringComparison.OrdinalIgnoreCase)
                || row.Table.Columns[index].ColumnName.Contains("Period",StringComparison.OrdinalIgnoreCase)?"MMM yyyy":"dd-MM-yyyy",CultureInfo.CurrentCulture),
            _=>Convert.ToString(value,CultureInfo.CurrentCulture)??""
        };
    }
    private static string Key(string text)=>new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    internal static List<Column> Create(DataTable source,string moneyFormat)
    {
        int Find(params string[] names)=>source.Columns.Cast<DataColumn>().Where(c=>names.Any(name=>Key(name)==Key(c.ColumnName))).Select(c=>c.Ordinal).DefaultIfEmpty(-1).First();
        var groups=new Dictionary<int,(string Caption,int[] Fields)>();var consumed=new HashSet<int>();
        void Merge(string caption,int anchor,params int[] extra)
        {
            if(anchor<0||consumed.Contains(anchor))return;
            int[] fields=new[]{anchor}.Concat(extra.Where(i=>i>=0&&!consumed.Contains(i))).Distinct().ToArray();
            if(fields.Length<2)return;
            groups[anchor]=(caption,fields);foreach(int field in fields.Skip(1))consumed.Add(field);
        }
        Merge("Receipt / Date",Find("Receipt / Reference","Receipt No.","Receipt Number","Receipt"),Find("Date"));
        Merge("Student / Family",Find("Student / Family"),Find("Scope"));
        Merge("Campus\nClass / Section",Find("Campus","Campus Name"),Find("Class / Section","Class – Section","ClassSection","Class"));
        Merge("Method\nReceived By",Find("Payment Method","Method"),Find("Received By"),Find("Reference"));
        Merge("Oldest Due\nAging",Find("Oldest Due Date","Due Date"),Find("Aging"));
        Merge("Period\nCharged On",Find("Charge Month","Billing Month","Month"),Find("Charge Date"));
        // Very wide charge reports benefit from one student identification cell.
        if(source.Columns.Count-consumed.Count>9)
            Merge("Student / Admission",Find("Student","Student Name"),Find("Adm. / QR No.","Admission Number","Adm. No."));

        using var bitmap=new Bitmap(1,1);using var graphics=Graphics.FromImage(bitmap);
        using var bodyFont=new Font("Segoe UI",9f);using var headerFont=new Font("Segoe UI",8.5f,FontStyle.Bold);
        double Measure(string text,Font font)=>graphics.MeasureString(text,font,PointF.Empty,StringFormat.GenericTypographic).Width/graphics.DpiX;
        var result=new List<Column>();
        foreach(DataColumn field in source.Columns)
        {
            int index=field.Ordinal;if(consumed.Contains(index))continue;
            var (caption,fields)=groups.GetValueOrDefault(index,(field.ColumnName,new[]{index}));
            caption=caption switch {
                "Money Received"=>"Money\nReceived", "Gross Received"=>"Gross\nReceived",
                "Credit Applied"=>"Credit\nApplied", "Adjusted Collection"=>"Adjusted\nCollection",
                "Open Charges (Net)"=>"Open Charges\n(Net)","Settled on Open Charges"=>"Settled on\nOpen Charges",
                _=>caption
            };
            bool numeric=fields.Length==1 && Type.GetTypeCode(field.DataType) is TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;
            double headerMinimum=caption.Split(new[]{' ','\n','/'},StringSplitOptions.RemoveEmptyEntries).Select(word=>Measure(word,headerFont)).DefaultIfEmpty(.3).Max()+.10;
            double contentMinimum=source.AsEnumerable().SelectMany(row=>fields.Select(i=>Value(row,i,moneyFormat)))
                .SelectMany(value=>numeric?new[]{value}:value.Split(new[]{' ','\r','\n'},StringSplitOptions.RemoveEmptyEntries))
                .Distinct().Select(value=>Measure(value,bodyFont)).DefaultIfEmpty(.3).Max()+.10;
            bool identity=caption.Contains("Student",StringComparison.OrdinalIgnoreCase)||caption.Contains("Father",StringComparison.OrdinalIgnoreCase)||caption.Contains("Name",StringComparison.OrdinalIgnoreCase);
            bool receipt=caption.StartsWith("Receipt",StringComparison.OrdinalIgnoreCase);
            bool placement=caption.StartsWith("Campus",StringComparison.OrdinalIgnoreCase);
            double minimum=Math.Max(headerMinimum,Math.Min(contentMinimum,numeric?1.65:identity?2.1:receipt?1.8:1.4));
            minimum=Math.Max(minimum,numeric?.65:identity?1.5:receipt?1.35:placement?1.2:.85);
            double weight=numeric?1:identity?2.2:receipt?1.8:placement?1.65:1.2;
            result.Add(new(caption,fields,numeric,minimum,weight));
        }
        return result;
    }
    internal static List<int[]> Bands(IReadOnlyList<Column> columns,double width)
    {
        if(columns.Sum(c=>c.Minimum)<=width)return new(){Enumerable.Range(0,columns.Count).ToArray()};
        int[] identity=Enumerable.Range(0,Math.Min(2,columns.Count)).Where(i=>!columns[i].Numeric).ToArray();
        if(identity.Length==0)identity=new[]{0};
        var remaining=new Queue<int>(Enumerable.Range(0,columns.Count).Except(identity));var result=new List<int[]>();
        if(remaining.Count==0)return new(){identity};
        while(remaining.Count>0)
        {
            var band=identity.ToList();double used=identity.Sum(i=>columns[i].Minimum);
            do{int next=remaining.Dequeue();band.Add(next);used+=columns[next].Minimum;}
            while(remaining.Count>0&&used+columns[remaining.Peek()].Minimum<=width);
            result.Add(band.ToArray());
        }
        return result;
    }
}
