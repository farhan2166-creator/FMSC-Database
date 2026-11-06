namespace FMSC_SMS.Core;

// Navigation only. SQL and existing dialogs remain authoritative for every write.
internal sealed record AdmissionWorkflowActions(bool Setup,bool CompleteDraft,bool History,bool Hold,bool Resume,
    bool Departure,bool Readmit,bool Transfer,bool ReviewLeft,bool Enabled)
{
    internal static bool CanActivateLegacy(string? status) =>
        string.Equals(status?.Trim(),"Active",StringComparison.OrdinalIgnoreCase);

    // Left covers Admission departure, leaving certificates and final-class completion.
    // Withdrawn is a legacy synonym; current workflows record Left.
    internal static bool CanReadmit(string? status,bool officialAdmission,bool activeEnrollment=false,bool held=false) =>
        officialAdmission && !activeEnrollment && !held &&
        (string.Equals(status?.Trim(),"Left",StringComparison.OrdinalIgnoreCase) ||
         string.Equals(status?.Trim(),"Withdrawn",StringComparison.OrdinalIgnoreCase) ||
         string.Equals(status?.Trim(),"Inactive",StringComparison.OrdinalIgnoreCase));

    internal static AdmissionWorkflowActions For(int admissionId,string status,bool pendingRegistration,bool legacySetup,
        int pendingPlacement,bool held,bool busy,bool administrator,bool enrollmentManager,bool currentPlacement=true,bool officialAdmission=true)
    {
        bool saved=admissionId>0 && !pendingRegistration;
        bool active=CanActivateLegacy(status);
        bool left=string.Equals(status,"Left",StringComparison.OrdinalIgnoreCase);
        bool settled=saved && !legacySetup && pendingPlacement==0;
        return new(saved && active && legacySetup && !held && administrator,saved && !legacySetup && pendingPlacement>0 && !held && administrator,
            saved && administrator,settled && active && !held && administrator && currentPlacement,saved && held && administrator && currentPlacement,
            settled && active && !held && administrator,saved && pendingPlacement==0 && administrator && CanReadmit(status,officialAdmission,held:held),
            settled && active && !held && enrollmentManager,saved && left && !held && administrator && currentPlacement,
            !busy && pendingPlacement>=0);
    }
}
