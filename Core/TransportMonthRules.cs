namespace FMSC_SMS.Core;

internal static class TransportMonthRules
{
    internal static DateTime First(DateTime date)=>new(date.Year,date.Month,1);
    internal static DateTime CutoffMonth(DateTime today,int? cutoff)=>
        cutoff is >=1 and <=28&&today.Day>=cutoff?First(today).AddMonths(1):First(today);

    internal static DateTime[] Months(DataSet context,int feeTypeId,bool start)
    {
        var summary=context.Tables[0].Rows[0];
        DateTime minimum=new[]{Convert.ToDateTime(summary["CutoffMonth"]),Convert.ToDateTime(summary["BillingStartMonth"]),Convert.ToDateTime(summary["YearStartMonth"])}.Max();
        var paid=context.Tables[3].AsEnumerable().Where(row=>Convert.ToBoolean(row["HasPayment"])&&(start||Convert.ToInt32(row["FeeTypeId"])==feeTypeId));
        foreach(var row in paid)minimum=new[]{minimum,First(Convert.ToDateTime(row["BillingMonth"])).AddMonths(1)}.Max();
        if(start)
            foreach(var row in context.Tables[1].AsEnumerable().Where(row=>row.IsNull("CancelledAt")&&!row.IsNull("EffectiveTo")))
                minimum=new[]{minimum,First(Convert.ToDateTime(row["EffectiveTo"])).AddMonths(1)}.Max();
        DateTime maximum=Convert.ToDateTime(summary["YearEndMonth"]);if(!start)maximum=maximum.AddMonths(1);
        var months=new List<DateTime>();
        for(var month=minimum;month<=maximum;month=month.AddMonths(1))months.Add(month);
        return months.ToArray();
    }
}
