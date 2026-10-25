namespace FMSC_SMS.Core;

// Navigation only. SQL and existing dialogs remain authoritative for every write.
internal sealed record AdmissionWorkflowActions(bool Setup,bool CompleteDraft,bool History,bool Hold,bool Resume,
    bool Departure,bool Readmit,bool Transfer,bool ReviewLeft,bool Enabled)
{
    internal static AdmissionWorkflowActions For(int admissionId,string status,bool pendingRegistration,bool legacySetup,
        int pendingPlacement,bool held,bool busy,bool administrator,bool enrollmentManager)
    {
        bool saved=admissionId>0 && !pendingRegistration;
        bool active=string.Equals(status,"Active",StringComparison.OrdinalIgnoreCase);
        bool left=string.Equals(status,"Left",StringComparison.OrdinalIgnoreCase);
        bool settled=saved && !legacySetup && pendingPlacement==0;
        return new(saved && legacySetup && !held && administrator,saved && !legacySetup && pendingPlacement>0 && !held && administrator,
            saved && administrator,settled && active && !held && administrator,saved && held && administrator,
            settled && active && !held && administrator,settled && !active && !held && administrator,
            settled && active && !held && enrollmentManager,saved && left && !held && administrator,
            !busy && pendingPlacement>=0);
    }
}
