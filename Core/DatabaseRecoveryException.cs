namespace FMSC_SMS.Core;

internal sealed class DatabaseRecoveryException : InvalidOperationException
{
    internal string Reason { get; }
    internal string Details { get; }
    internal DatabaseRecoveryException(string reason, string message, string details = "") : base(message)
    { Reason = reason; Details = details; }

    // Command output must survive both logging and the connection-details panel.
    public override string ToString() => base.ToString() + $"\nRecovery reason: {Reason}" +
        (string.IsNullOrWhiteSpace(Details) ? "" : "\n" + Details);
}
