namespace FMSC_SMS.Core;

/// <summary>One page's latest read request. Never use this to cancel financial writes.</summary>
internal sealed class LatestReadScope : IDisposable
{
    internal readonly record struct Request(int Generation, CancellationToken Token);
    private CancellationTokenSource? current;
    private int generation;
    private bool disposed;

    internal Request Begin()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Cancel();
        current = new CancellationTokenSource();
        return new Request(generation, current.Token);
    }

    internal bool IsCurrent(Request request) =>
        !disposed && current is not null && request.Generation == generation && !request.Token.IsCancellationRequested;

    internal void Cancel()
    {
        generation++;
        current?.Cancel();
        current?.Dispose();
        current = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Cancel();
    }
}
