namespace FMSC_SMS.Core;

internal static class CollectionCreditAllocation
{
    // Ordinary collection uses only the main student's advance, against that
    // student's charges. Selecting siblings never pools or transfers their money.
    // The fixed family counter has no main-student picker; retain its existing
    // per-child advance allocation, still without crossing account ownership.
    internal static Dictionary<long,decimal> Build(
        IEnumerable<(long ChargeId,int AdmissionId,decimal Due)> charges,
        IReadOnlyDictionary<int,decimal> balances,int? mainStudent)
    {
        var remaining=balances.Where(pair=>mainStudent is null||pair.Key==mainStudent)
            .ToDictionary(pair=>pair.Key,pair=>Math.Max(0m,pair.Value));
        var result=new Dictionary<long,decimal>();
        foreach(var charge in charges)
        {
            if(charge.ChargeId<=0||!remaining.TryGetValue(charge.AdmissionId,out decimal credit)||credit<=0m)continue;
            decimal take=Math.Min(Math.Max(0m,charge.Due),credit);
            if(take<=0m)continue;
            result[charge.ChargeId]=take;
            remaining[charge.AdmissionId]-=take;
        }
        return result;
    }
}
