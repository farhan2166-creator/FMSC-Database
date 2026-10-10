namespace FMSC_SMS.Core;

/// <summary>Serializes report loads and rejects results from superseded page requests.</summary>
internal sealed class ReportLoadCoordinator : IDisposable
{
    internal sealed record Ticket(CancellationTokenSource Cancellation, int Generation);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _current;
    private int _generation;
    private bool _disposed;

    internal Ticket Begin()
    {
        _current?.Cancel();
        _current?.Dispose();
        _current = new CancellationTokenSource();
        return new Ticket(_current, ++_generation);
    }

    internal Task EnterAsync() => _gate.WaitAsync();

    internal bool IsCurrent(Ticket ticket) =>
        !_disposed && !ticket.Cancellation.IsCancellationRequested &&
        ticket.Generation == _generation && ReferenceEquals(_current, ticket.Cancellation);

    internal void Exit()
    {
        try { _gate.Release(); }
        catch (ObjectDisposedException) { }
    }

    internal void Cancel()
    {
        _generation++;
        _current?.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _current?.Cancel();
        _current?.Dispose();
        /* A page can be disposed while an older load is waiting to enter this gate. The
         * waiter must be allowed to enter, observe IsCurrent == false, and leave cleanly.
         * Disposing SemaphoreSlim here made ordinary navigation surface an unhandled
         * ObjectDisposedException. The coordinator and gate are collected together once
         * those short-lived continuations release their references. */
    }
}
