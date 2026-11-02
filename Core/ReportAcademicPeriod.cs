using System.Globalization;

namespace FMSC_SMS.Core;

internal static class ReportAcademicPeriod
{
    internal static readonly string[] Choices={"1 month","3 months","6 months","12 months"};
    internal static int Months(string choice)=>choice switch {"1 month"=>1,"3 months"=>3,"6 months"=>6,"12 months"=>12,_=>0};
    internal sealed record Range(DateTime From,DateTime To)
    {
        public override string ToString()=>From.ToString("dd MMM yyyy",CultureInfo.CurrentCulture)+" – "+To.ToString("dd MMM yyyy",CultureInfo.CurrentCulture);
    }
    internal static IReadOnlyList<Range> Ranges(DateTime start,DateTime end,int months)
    {
        start=start.Date;end=end.Date;
        if(months is not (1 or 3 or 6 or 12)||end<start)throw new ArgumentException("Choose a valid academic year and month period.");
        var ranges=new List<Range>();
        var first=new DateTime(start.Year,start.Month,1);
        while(first<=end)
        {
            var next=first.AddMonths(months);
            ranges.Add(new Range(first<start?start:first,next.AddDays(-1)>end?end:next.AddDays(-1)));
            first=next;
        }
        return ranges;
    }
}
