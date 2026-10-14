using System.Diagnostics;
using FMSC_SMS.Core;

internal static class StatementReadBatchChecks
{
    internal static async Task<int> RunAsync()
    {
        int checks=0;void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
        var first=new TaskCompletionSource<int>();int calls=0;var consumed=new List<int>();
        var pending=SequentialReadBatch.ReadAsync(new[]{1,2},id=>{calls++;return id==1?first.Task:Task.FromResult(id);},(_,value)=>consumed.Add(value));
        Check(!pending.IsCompleted&&calls==1&&consumed.Count==0,"A pending report read returns control before any table is published");
        first.SetResult(1);await pending;
        Check(consumed.SequenceEqual(new[]{1,2}),"Sequential report assembly preserves captured student order");
        using var cancel=new CancellationTokenSource();var delayed=new TaskCompletionSource<int>();calls=0;consumed.Clear();
        var cancelled=SequentialReadBatch.ReadAsync(new[]{1,2},_=>{calls++;return delayed.Task;},(_,value)=>consumed.Add(value),cancel.Token);
        cancel.Cancel();try{await cancelled;throw new Exception("Expected cancellation");}catch(OperationCanceledException){checks++;}
        Check(calls==1&&consumed.Count==0,"Superseding a report stops subsequent reads and prevents consuming the abandoned result");delayed.SetResult(1);
        consumed.Clear();calls=0;
        try{await SequentialReadBatch.ReadAsync(new[]{1,2,3},id=>{calls++;return id==2?Task.FromException<int>(new IOException("Fixture query refusal")):Task.FromResult(id);},(_,value)=>consumed.Add(value));throw new Exception("Expected read error");}
        catch(IOException){checks++;}
        Check(calls==2&&consumed.SequenceEqual(new[]{1}),"A failed student read stops the batch rather than inventing successful data");
        int active=0,maximum=0,heartbeats=0;consumed.Clear();var watch=Stopwatch.StartNew();
        var profile=SequentialReadBatch.ReadAsync(Enumerable.Range(1,32),async id=>{int now=Interlocked.Increment(ref active);maximum=Math.Max(maximum,now);await Task.Delay(8);Interlocked.Decrement(ref active);return id;},(_,value)=>consumed.Add(value));
        while(!profile.IsCompleted){heartbeats++;await Task.Delay(2);}await profile;watch.Stop();
        Check(maximum==1&&consumed.Count==32&&heartbeats>1,"Synthetic delayed reads keep the caller responsive with at most one SQL-style read in flight");
        Console.WriteLine($"PROFILE statement reads: 32 synthetic 8-ms queries, {watch.Elapsed.TotalMilliseconds:F0} ms, {heartbeats} caller heartbeats, max {maximum} concurrent read. Excludes actual SQL and WinForms.");
        return checks;
    }
}
