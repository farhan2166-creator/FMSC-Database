namespace FMSC_SMS.Core;

/// <summary>Read reports without blocking the caller or flooding SQL with parallel connections.</summary>
internal static class SequentialReadBatch
{
    internal static async Task ReadAsync<T>(IEnumerable<int> ids,Func<int,Task<T>> read,
        Action<int,T> append,CancellationToken cancellation=default)
    {
        foreach(int id in ids)
        {
            cancellation.ThrowIfCancellationRequested();
            T result=await read(id).WaitAsync(cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            // Table assembly belongs off the UI thread even when a read completes synchronously.
            await Task.Run(()=>{cancellation.ThrowIfCancellationRequested();append(id,result);},cancellation).ConfigureAwait(false);
        }
    }
}
