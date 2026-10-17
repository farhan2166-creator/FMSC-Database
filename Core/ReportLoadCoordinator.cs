namespace FMSC_SMS.Core;

/// <summary>Serializes report loads and rejects results from superseded page requests.</summary>
internal sealed class ReportLoadCoordinator : IDisposable
{
    internal sealed record Ticket(CancellationTokenSource Cancellation, int Generation)
    {
        // Capture before Begin disposes a superseded source; waiting consumers
        // must never retrieve Token from an already disposed source.
        internal CancellationToken Token { get; } = Cancellation.Token;
    }

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _current;
    private int _generation;
    private bool _disposed;
    private bool _pending;

    internal Ticket Begin()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _current?.Cancel();
        _current?.Dispose();
        _current = new CancellationTokenSource();
        _pending = true;
        return new Ticket(_current, ++_generation);
    }

    internal Task EnterAsync(Ticket ticket) => _gate.WaitAsync(ticket.Token);

    internal bool IsCurrent(Ticket ticket) =>
        !_disposed && !ticket.Cancellation.IsCancellationRequested &&
        ticket.Generation == _generation && ReferenceEquals(_current, ticket.Cancellation);

    internal void Exit()
    {
        try { _gate.Release(); }
        catch (ObjectDisposedException) { }
    }

    internal bool Cancel()
    {
        bool wasPending = _pending;
        _pending = false;
        _generation++;
        _current?.Cancel();
        return wasPending;
    }

    internal void Complete(Ticket ticket)
    {
        if (ReferenceEquals(_current, ticket.Cancellation) && ticket.Generation == _generation)
            _pending = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pending = false;
        _current?.Cancel();
        _current?.Dispose();
        // Cancelled waiters leave immediately. An active reader must still release
        // its gate in finally, so do not dispose the semaphore under that reader.
    }
}
