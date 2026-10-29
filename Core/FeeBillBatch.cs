namespace FMSC_SMS.Core;

internal static class FeeBillBatch
{
    // Ownership of confirmed results passes to the caller even on partial failure.
    internal sealed record Result(List<DataTable> Saved,string? Error);
    internal static async Task<Result> PrepareAsync(int[][] groups,Func<bool> current,Func<int[],Task<DataTable>> issue)
    {
        var saved=new List<DataTable>();
        foreach(var ids in groups)
        {
            if(!current())break;
            try { saved.Add(await issue(ids)); }
            catch(Exception ex){return new(saved,ex.Message);}
        }
        return new(saved,null);
    }
}
