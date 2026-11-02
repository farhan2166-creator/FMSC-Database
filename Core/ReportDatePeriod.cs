namespace FMSC_SMS.Core;

/// <summary>Relative periods are resolved when applied/run, never persisted as yesterday's dates.</summary>
internal static class ReportDatePeriod
{
    internal const string Fixed = "Fixed dates";
    internal static readonly string[] Choices = { Fixed,"All dates","Today","This month","Previous month","This calendar year" };
    internal static (DateTime? From,DateTime? To) Resolve(string period,DateTime today)
    {
        today=today.Date;
        DateTime month=new(today.Year,today.Month,1);
        return period switch {
            "All dates" => (null,null), "Today" => (today,today),
            "This month" => (month,month.AddMonths(1).AddDays(-1)),
            "Previous month" => (month.AddMonths(-1),month.AddDays(-1)),
            "This calendar year" => (new DateTime(today.Year,1,1),new DateTime(today.Year,12,31)),
            _ => throw new ArgumentException("Choose a supported relative date period.",nameof(period))
        };
    }
}
