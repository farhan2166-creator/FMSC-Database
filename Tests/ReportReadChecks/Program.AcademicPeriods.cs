using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckAcademicPeriods()
    {
        foreach(int months in new[]{1,3,6,12})
        foreach(var (start,end) in new[]{(new DateTime(2026,4,1),new DateTime(2027,3,31)),
            (new DateTime(2023,11,15),new DateTime(2024,7,18)),(new DateTime(2024,2,29),new DateTime(2024,2,29)),
            (new DateTime(2026,4,15),new DateTime(2027,5,5))})
        {
            var ranges=ReportAcademicPeriod.Ranges(start,end,months);
            Check(ranges[0].From==start&&ranges[^1].To==end,"Month periods cover the exact academic-year boundaries");
            Check(ranges.All(r=>r.From>=start&&r.To<=end&&r.From<=r.To),"No period escapes the academic year, including single-day years");
            for(int i=1;i<ranges.Count;i++)Check(ranges[i-1].To.AddDays(1)==ranges[i].From,"Academic periods have no gaps or overlaps");
        }
        var quarters=ReportAcademicPeriod.Ranges(new DateTime(2026,4,1),new DateTime(2027,3,31),3);
        Check(quarters.Count==4&&quarters[0].To==new DateTime(2026,6,30)&&quarters[3].From==new DateTime(2027,1,1),"Quarters follow academic-year months rather than today's rolling range");
        var leap=ReportAcademicPeriod.Ranges(new DateTime(2024,2,1),new DateTime(2024,3,31),1);
        Check(leap[0].To==new DateTime(2024,2,29),"February retains leap day");
        foreach(int invalid in new[]{0,2,5,13})
        {
            bool refused=false;try { ReportAcademicPeriod.Ranges(DateTime.Today,DateTime.Today,invalid); }catch(ArgumentException){refused=true;}
            Check(refused,"Unsupported period lengths are rejected");
        }
        bool reversed=false;try { ReportAcademicPeriod.Ranges(new DateTime(2026,5,1),new DateTime(2026,4,1),3); }catch(ArgumentException){reversed=true;}
        Check(reversed,"A reversed academic year cannot generate periods");
    }
}
