namespace FMSC_SMS.Core;

public enum PageStatusKind
{
    Info,
    Success,
    Warning,
    Error,
    Busy
}

/// <summary>A page that is in the middle of work the operator must not leave.</summary>
public interface IBusyPage
{
    bool IsBusy { get; }
    string BusyMessage { get; }
}

/// <summary>Allows a page or workspace to retain operator context while navigating.</summary>
internal interface IWorkspaceStateHost
{
    Dictionary<string, string> CaptureWorkspaceState();
    void RestoreWorkspaceState(IReadOnlyDictionary<string, string> state);
}
