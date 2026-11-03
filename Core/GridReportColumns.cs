using System.Globalization;

namespace FMSC_SMS.Core;

// Print presentation only. Every selected source field is retained; related
// identity fields share a cell so amounts and references have room to read.
internal static class GridReportColumns
{
    internal enum Kind { Standard, Student, Placement, Admission, Leaving, ClassChange, StatusChange, Due, Recipient, Certificate, Collection, Recorded, Family }
    internal sealed record Column(string Caption,int[] Fields,bool Numeric,double Minimum,double Weight,Kind Presentation=Kind.Standard);

    internal static string Alignment(DataTable source,Column column)
    {
        if(column.Numeric || column.Fields.All(i=>source.Columns[i].DataType==typeof(DateTime)))return "Center";
        string key=Key(column.Caption);
        return key.StartsWith("receipt") || key.StartsWith("adm") || key.StartsWith("reference")
            || key.StartsWith("campus") || key.StartsWith("method") || key.StartsWith("period")
            || key.StartsWith("oldestdue")
            || key is "class" or "section" or "classsection" or "year" or "academicyear"
                or "month" or "billingmonth" or "chargemonth" or "feeperiod" or "status"
                or "aging" or "agingbucket" or "collection" or "paymentmethod"
                or "certificateno" or "certificatenumber" or "studentstaffno" or "studentno" or "staffno"
                or "date" or "certificatedate" or "recordedon" or "collectedon" or "dateofbirth"
            ? "Center" : "Left";
    }

    internal static string Value(DataRow row,int index,string moneyFormat)
    {
        object value=row[index];
        return value switch {
            DBNull=>"",
            decimal money=>money.ToString(moneyFormat,CultureInfo.CurrentCulture),
            DateTime date=>date.ToString(row.Table.Columns[index].ColumnName.Contains("Month",StringComparison.OrdinalIgnoreCase)
                || row.Table.Columns[index].ColumnName.Contains("Period",StringComparison.OrdinalIgnoreCase)?"MMM yyyy":"dd-MM-yyyy",CultureInfo.CurrentCulture),
            _=>RecordTextPolicy.DisplayText(row.Table.Columns[index].ColumnName,Convert.ToString(value,CultureInfo.CurrentCulture)??"")
        };
    }
    private static string Key(string text)=>new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    internal static string DisplayValue(DataRow row,Column column,string moneyFormat)
    {
        bool Is(int index,params string[] names)=>ReportStudentIdentity.Matches(row.Table.Columns[index],names);
        int Find(params string[] names)=>column.Fields.FirstOrDefault(index=>Is(index,names),-1);
        string ValueAt(int index)=>Value(row,index,moneyFormat);
        string Label(int index,string label)=>ValueAt(index) is {Length:>0} value?label+value:"";
        string Lines(IEnumerable<string> values)=>string.Join("\n",values.Where(value=>value.Length>0).Distinct());
        if(column.Presentation==Kind.Student || column.Caption=="Student / Admission")
        {
            int student=Find("Student","StudentName"),admission=Find("Adm. / QR No.","AdmissionNumber","Adm. No.","AdmissionNumberText"),father=Find("Father","FatherName"),gender=Find("Gender");
            if(student>=0)return Lines(new[]{ReportStudentIdentity.FormatCell(row,admission,student,father,gender)}.Concat(
                column.Fields.Where(index=>index!=student&&index!=admission&&index!=father).Select(index=>
                    Is(index,"DateOfBirth","DOB")?Label(index,"DOB: "):Is(index,"StudentStatus")?Label(index,"Status: "):
                    Is(index,"Gender")?Label(index,"Gender: "):ValueAt(index))));
        }
        if(column.Presentation==Kind.Recipient)
        {
            int name=Find("Name","Recipient","Recipient Name"),reference=Find("Student / Staff No.","Student No.","Staff No.");
            return Lines(new[]{ReportStudentIdentity.Format(reference<0?"":ValueAt(reference),ValueAt(name),"","")}
                .Concat(column.Fields.Where(index=>index!=name&&index!=reference).Select(ValueAt)));
        }
        return Lines(column.Fields.Select(index=>column.Presentation switch {
            Kind.Admission=>Is(index,"AdmissionDate")?Label(index,"Date: "):Label(index,"Class: "),
            Kind.Leaving=>Is(index,"WithdrawalDate","LeavingDate","LeftDate")?Label(index,"Date: "):Label(index,"Class: "),
            Kind.ClassChange=>Is(index,"OldClassSection","From Class","OldClass")?Label(index,"From: "):Label(index,"To: "),
            Kind.StatusChange=>Is(index,"OldStatus")?Label(index,"From: "):Label(index,"To: "),
            Kind.Placement when Is(index,"EnrollmentStatus","Class Record Status")=>Label(index,"Class status: "),
            Kind.Due when Is(index,"Days Overdue")=>ValueAt(index) is {Length:>0} days?days+" days overdue":"",
            Kind.Certificate when Is(index,"Certificate Date")=>Label(index,"Date: "),
            Kind.Certificate when Is(index,"Issue Type")=>Label(index,"Issue: "),
            Kind.Collection when Is(index,"Collected On","CollectedAt")=>Label(index,"On: "),
            Kind.Collection when Is(index,"Collected By")=>Label(index,"By: "),
            Kind.Recorded when Is(index,"Recorded By")=>Label(index,"By: "),
            Kind.Family when Is(index,"Members")=>Label(index,"Members: "),
            _=>ValueAt(index)
        }));
    }
    internal static List<Column> Create(DataTable source,string moneyFormat)
    {
        int Find(params string[] names)=>source.Columns.Cast<DataColumn>().Where(c=>ReportStudentIdentity.Matches(c,names)).Select(c=>c.Ordinal).DefaultIfEmpty(-1).First();
        int Original(string name)=>source.Columns.Cast<DataColumn>().Where(c=>Key(Convert.ToString(c.ExtendedProperties[ReportStudentIdentity.SourceColumnKey])??c.ColumnName)==Key(name)).Select(c=>c.Ordinal).DefaultIfEmpty(-1).First();
        var groups=new Dictionary<int,(string Caption,int[] Fields,Kind Presentation)>();var consumed=new HashSet<int>();
        void MergeAs(Kind presentation,string caption,int anchor,params int[] extra)
        {
            if(anchor<0||consumed.Contains(anchor))return;
            int[] fields=new[]{anchor}.Concat(extra.Where(i=>i>=0&&!consumed.Contains(i))).Distinct().ToArray();
            if(fields.Length<2)return;
            groups[anchor]=(caption,fields,presentation);foreach(int field in fields.Skip(1))consumed.Add(field);
        }
        void Merge(string caption,int anchor,params int[] extra)=>MergeAs(Kind.Standard,caption,anchor,extra);
        MergeAs(Kind.Student,"Student / Admission",Find("Student","StudentName"),Find("Adm. / QR No.","AdmissionNumber","Adm. No.","AdmissionNumberText"),
            Find("Father","FatherName"),Find("DateOfBirth","DOB"),Find("Gender"),Original("StudentStatus"));
        MergeAs(Kind.Recipient,"Recipient",Find("Name","Recipient","Recipient Name"),Find("Student / Staff No.","Student No.","Staff No."),Find("Recipient Type"));
        Merge("Receipt / Date",Find("Receipt / Reference","Receipt No.","Receipt Number","Receipt"),Find("Date"));
        Merge("Student / Family",Find("Student / Family"),Find("Scope"));
        int campus=Find("Campus","CampusName"),klass=Find("Class / Section","Class – Section","ClassSection","ClassName","Class"),year=Find("Academic Year","YearName");
        int placement=campus>=0?campus:klass>=0?klass:year;
        string placementCaption=string.Join("\n",new[]{campus>=0?"Campus":"",klass>=0?"Class / Section":"",year>=0?"Academic Year":""}.Where(text=>text.Length>0));
        MergeAs(Kind.Placement,placementCaption,placement,klass,Find("ClassName"),Find("SectionName","Section"),year,Find("EnrollmentStatus"),Find("ProgramType","Program"));
        MergeAs(Kind.Admission,"Admission",Find("AdmissionDate"),Find("Class Admitted To","AdmissionClassSection"));
        MergeAs(Kind.Leaving,"Leaving",Find("WithdrawalDate","LeavingDate","LeftDate"),Find("Class Left","LeavingClassSection"));
        MergeAs(Kind.ClassChange,"Class Change",Find("OldClassSection","From Class"),Find("NewClassSection","To Class"));
        MergeAs(Kind.StatusChange,"Status Change",Find("OldStatus"),Find("NewStatus"));
        Merge("Contact",Find("ContactNumber","Contact","Phone"),Find("Email"));
        MergeAs(Kind.Family,"Family / Members",Find("Family","Family Code","Family / Student"),Find("Members"));
        MergeAs(Kind.Certificate,"Certificate",Find("Certificate No.","CertificateNumber"),Find("Certificate Type","CertificateType"),Find("Certificate Date"),Find("Entry"),Find("Issue Type"));
        MergeAs(Kind.Recorded,"Recorded",Find("Recorded On"),Find("Recorded By"));
        MergeAs(Kind.Collection,"Collection",Find("Collection","CollectionStatus"),Find("Collected On","CollectedAt"),Find("Collected By"));
        Merge("Method\nReceived By",Find("Payment Method","Method"),Find("Received By"),Find("Reference"));
        MergeAs(Kind.Due,"Oldest Due\nAging",Find("Oldest Due Date","Due Date"),Find("Aging"),Find("Days Overdue"));
        Merge("Period\nCharged On",Find("Charge Month","Billing Month","Month"),Find("Charge Date"));

        using var bitmap=new Bitmap(1,1);using var graphics=Graphics.FromImage(bitmap);
        using var bodyFont=new Font("Segoe UI",9f);using var headerFont=new Font("Segoe UI",8.5f,FontStyle.Bold);
        double Measure(string text,Font font)=>graphics.MeasureString(text,font,PointF.Empty,StringFormat.GenericTypographic).Width/graphics.DpiX;
        var result=new List<Column>();
        foreach(DataColumn field in source.Columns)
        {
            int index=field.Ordinal;if(consumed.Contains(index))continue;
            var (caption,fields,presentation)=groups.GetValueOrDefault(index,(field.ColumnName,new[]{index},Kind.Standard));
            caption=caption switch {
                "Money Received" or "Payment Received"=>"Payment\nReceived", "Gross Received"=>"Gross\nReceived",
                "Credit Applied"=>"Credit\nApplied", "Adjusted Collection"=>"Adjusted\nCollection",
                "Open Charges (Net)"=>"Open Charges\n(Net)","Settled on Open Charges"=>"Settled on\nOpen Charges",
                _=>caption
            };
            bool numeric=fields.Length==1 && Type.GetTypeCode(field.DataType) is TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;
            double headerMinimum=caption.Split(new[]{' ','\n','/'},StringSplitOptions.RemoveEmptyEntries).Select(word=>Measure(word,headerFont)).DefaultIfEmpty(.3).Max()+.10;
            headerMinimum=Math.Max(headerMinimum,Math.Min(1.25,caption.Split('\n').Select(line=>Measure(line,headerFont)+.15).Max()));
            var renderedColumn=new Column(caption,fields,numeric,0,0,presentation);
            double contentMinimum=source.AsEnumerable().Select(row=>DisplayValue(row,renderedColumn,moneyFormat))
                .SelectMany(value=>numeric?new[]{value}:value.Split(new[]{' ','\r','\n'},StringSplitOptions.RemoveEmptyEntries))
                .Distinct().Select(value=>Measure(value,bodyFont)).DefaultIfEmpty(.3).Max()+.10;
            bool identity=caption.Contains("Student",StringComparison.OrdinalIgnoreCase)||caption.Contains("Father",StringComparison.OrdinalIgnoreCase)||caption.Contains("Name",StringComparison.OrdinalIgnoreCase);
            bool receipt=caption.StartsWith("Receipt",StringComparison.OrdinalIgnoreCase);
            bool isPlacement=presentation==Kind.Placement||caption.StartsWith("Campus",StringComparison.OrdinalIgnoreCase);
            double minimum=Math.Max(headerMinimum,Math.Min(contentMinimum,numeric?1.65:identity?2.1:receipt?1.8:1.4));
            // Keep a labelled date (and recorded time) together. Measuring only
            // individual words lets the year wrap onto an otherwise empty line.
            if(presentation is Kind.Admission or Kind.Leaving or Kind.Recorded or Kind.Collection or Kind.Certificate)
            {
                double dateMinimum=source.AsEnumerable().SelectMany(row=>DisplayValue(row,renderedColumn,moneyFormat).Split('\n'))
                    .Where(line=>line.StartsWith("Date: ")||line.StartsWith("On: ")||
                        (presentation==Kind.Recorded && line.Length>0 && char.IsDigit(line[0])))
                    .Select(line=>Measure(line,bodyFont)+.15).DefaultIfEmpty(0).Max();
                minimum=Math.Max(minimum,Math.Min(dateMinimum,2.2));
            }
            minimum=Math.Max(minimum,numeric?.65:identity?1.5:receipt?1.35:isPlacement?1.2:.85);
            double weight=numeric?1:identity?2.2:receipt?1.8:isPlacement?1.65:1.2;
            result.Add(new(caption,fields,numeric,minimum,weight,presentation));
        }
        int Priority(Column column)
        {
            string key=Key(column.Caption);
            if(column.Numeric)return Type.GetTypeCode(source.Columns[column.Fields[0]].DataType) is TypeCode.Decimal or TypeCode.Double or TypeCode.Single?100:95;
            if(key.StartsWith("receipt")||key.StartsWith("certificate"))return 10;
            if(column.Presentation is Kind.Student or Kind.Recipient || key.StartsWith("student")||key.StartsWith("family"))return 20;
            if(column.Presentation==Kind.Placement||key.StartsWith("campus")||key is "class" or "classsection" or "academicyear")return 30;
            if(column.Presentation==Kind.Admission||key=="admissiondate")return 40;
            if(column.Presentation==Kind.Leaving||key=="leavingdate")return 45;
            if(column.Presentation is Kind.ClassChange or Kind.StatusChange || key=="change")return 50;
            if(key is "contact" or "contactnumber" or "email" or "address")return 60;
            if(key.StartsWith("period")||key is "date" or "recordedon" or "feetype")return 70;
            if(column.Presentation==Kind.Due)return 80;
            if(key is "remarks" or "reason" or "details")return 110;
            return column.Numeric?100:90;
        }
        return result.OrderBy(Priority).ToList();
    }
    internal static List<int[]> Bands(IReadOnlyList<Column> columns,double width)
    {
        if(columns.Sum(c=>c.Minimum)<=width)return new(){Enumerable.Range(0,columns.Count).ToArray()};
        // Operator-selected order may put money before the student's reference.
        // Repeat identity wherever it appears, without adding unselected fields.
        int[] identity=Enumerable.Range(0,columns.Count).Where(i=>!columns[i].Numeric &&
            (columns[i].Presentation is Kind.Student or Kind.Recipient
             || columns[i].Caption.Contains("Receipt",StringComparison.OrdinalIgnoreCase)
             || columns[i].Caption.Contains("Admission Number",StringComparison.OrdinalIgnoreCase)
             || columns[i].Caption.Contains("Adm.",StringComparison.OrdinalIgnoreCase)
             || columns[i].Caption.Contains("Student",StringComparison.OrdinalIgnoreCase)))
            .Take(2).ToArray();
        if(identity.Length==0)identity=Enumerable.Range(0,columns.Count).Where(i=>!columns[i].Numeric).Take(2).ToArray();
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
