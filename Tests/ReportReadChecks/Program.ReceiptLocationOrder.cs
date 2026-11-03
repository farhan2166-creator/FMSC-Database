using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckReceiptLocationOrder()
    {
        foreach(string mode in new[]{"Campus","Class","Section"})
        {
            using var source=ReceiptRows().Clone();
            source.Rows.Add("FR-1","Boys, Girls","One – A\nTwo – B",100m,0m,0m,20m,100m);
            source.Rows.Add("FR-2","Girls, Boys","Two – B\nOne – A\nOne – A",0m,0m,30m,0m,-30m);
            CollectionReceiptGrouping.Apply(source,mode);
            var layout=CollectionReceiptGrouping.Layout(source)!;
            Check(layout.Count(row=>row.Kind=="Heading")==1,mode+": reordered saved children share one location group");
            Check(layout.Count(row=>row.Kind=="Subtotal")==1,mode+": identical locations have one subtotal");
            var subtotal=source.Rows[Array.FindIndex(layout,row=>row.Kind=="Subtotal")];
            var grand=source.Rows[Array.FindIndex(layout,row=>row.Kind=="GrandTotal")];
            foreach(var row in new[]{subtotal,grand})
                Check(row.Field<decimal>("Money Received")==100m&&row.Field<decimal>("Corrections")==30m
                    &&row.Field<decimal>("Credit Applied")==20m&&row.Field<decimal>("Adjusted Collection")==70m,
                    mode+": merged totals retain signed corrections and separate credit");
            using var details=CollectionReceiptGrouping.DetailsOnly(source);
            Check(details.Rows.Count==2&&details.Rows[0].Field<string>("Campus")=="Boys, Girls"
                &&details.Rows[1].Field<string>("Campus")=="Girls, Boys"
                &&details.Rows[1].Field<string>("Class / Section")=="Two – B\nOne – A\nOne – A",
                mode+": receipt detail fields retain their saved order and repeated classes");
            var first=CollectionReceiptGrouping.Page(source,0,1);var last=CollectionReceiptGrouping.Page(source,1,1);
            Check(first.TotalRows==2&&last.TotalRows==2&&CollectionReceiptGrouping.Layout(last.Rows)!.Count(row=>row.Kind=="Subtotal")==1,
                mode+": pagination carries the merged group subtotal to its final receipt");
        }
        foreach(string mode in new[]{"Class","Section"})
        {
            using var source=ReceiptRows().Clone();
            source.Rows.Add("FR-1","Boys","One – A\nTwo – B",100m,0m,0m,0m,100m);
            source.Rows.Add("FR-2","Boys","One – B\nTwo – A",100m,0m,0m,0m,100m);
            CollectionReceiptGrouping.Apply(source,mode);
            Check(CollectionReceiptGrouping.Layout(source)!.Count(row=>row.Kind=="Heading")== (mode=="Class"?1:2),
                mode+": grouping preserves class/section associations rather than combining unrelated section pairs");
        }
    }
}
