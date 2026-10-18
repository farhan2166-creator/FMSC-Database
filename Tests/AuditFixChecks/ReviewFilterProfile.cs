using FMSC_SMS.Core;
using System.Data;
using System.Diagnostics;

internal static class ReviewFilterProfile
{
    // Synthetic UI-side data only: this does not benchmark SQL balance aggregation,
    // WinForms painting or a school's actual workload. No timing threshold is asserted.
    internal static void Run()
    {
        foreach(int size in new[]{100,2000,10000}){
            var table=new DataTable{CaseSensitive=false,Locale=System.Globalization.CultureInfo.InvariantCulture};
            foreach(string c in new[]{"Reference","StudentName","FatherName","ContactNumber","Status","Verification"})table.Columns.Add(c,typeof(string));
            for(int i=0;i<size;i++)table.Rows.Add($"QR-{i}",$"Student {i}",$"Parent {i}",$"030{i:D8}","Pending","Needs Review");
            using var view=new DataView(table);
            var terms=new[]{"s","st","student","student 1","student 123","missing",""};
            int[] expected=terms.Select(t=>table.AsEnumerable().Count(r=>new[]{"Reference","StudentName","FatherName","ContactNumber"}.Any(c=>Convert.ToString(r[c])!.Contains(t,StringComparison.OrdinalIgnoreCase)))).ToArray();
            // Warm the expression evaluator before measuring this path.
            view.RowFilter=AdmissionReviewFilter.Create(true,false,false,"warmup");
            var watch=Stopwatch.StartNew();long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<terms.Length;i++){
                view.RowFilter=AdmissionReviewFilter.Create(true,false,false,terms[i]);
                if(view.Count!=expected[i])throw new Exception("Synthetic filter count changed");
            }
            long allocated=GC.GetAllocatedBytesForCurrentThread()-before;watch.Stop();
            Console.WriteLine($"PROFILE: {size} cached rows, {terms.Length} filter changes, {watch.Elapsed.TotalMilliseconds:F1} ms total, {allocated/1024:F0} KiB allocated; source rows retained.");
        }
    }
}
