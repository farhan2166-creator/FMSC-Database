namespace FMSC_SMS.Core;

// Print-only adjustments for the financial registers reviewed for density.
// Keep the existing orientation, selected fields, values and raw exports.
internal static class ManagementReportPrintLayout
{
    internal static bool Applies(string title)=>title.StartsWith("Outstanding & Aging",StringComparison.Ordinal)
        ||title.StartsWith("Billing & Discounts",StringComparison.Ordinal)
        ||title.StartsWith("Monthly Fee Reconciliation",StringComparison.Ordinal);

    internal static void Apply(List<GridReportColumns.Column> columns,double width,string title)
    {
        double spare=Math.Max(0,width-columns.Sum(column=>column.Minimum));
        // Short fee names should remain on one line before allocating spare
        // width to identities; otherwise every tuition row becomes twice as tall.
        if(title.StartsWith("Billing & Discounts",StringComparison.Ordinal))
            for(int i=0;i<columns.Count;i++)if(columns[i].Caption=="Fee Type")
            {
                double extra=Math.Min(spare,Math.Max(0,1.25-columns[i].Minimum));
                columns[i]=columns[i] with {Minimum=columns[i].Minimum+extra};spare-=extra;
            }
        for(int i=0;i<columns.Count;i++)
        {
            var column=columns[i];
            bool identity=column.Presentation==GridReportColumns.Kind.Student||column.Caption.StartsWith("Student",StringComparison.Ordinal);
            if(identity)
            {
                double extra=Math.Min(spare,Math.Max(0,(title.StartsWith("Outstanding",StringComparison.Ordinal)?3.1:2.6)-column.Minimum));
                columns[i]=column with {Minimum=column.Minimum+extra,Weight=4.5};spare-=extra;
            }
        }
        if(title.StartsWith("Monthly Fee Reconciliation",StringComparison.Ordinal))
        {
            int month=columns.FindIndex(column=>column.Caption is "Billing Month" or "Month");
            int campus=columns.FindIndex(column=>column.Caption.StartsWith("Campus",StringComparison.Ordinal));
            if(month>campus&&campus>=0){var column=columns[month];columns.RemoveAt(month);columns.Insert(campus,column);}
        }
    }
}
