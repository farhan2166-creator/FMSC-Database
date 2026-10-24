using FMSC_SMS.Pages.Security;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Core;

internal sealed class CertificateReissueException(string message,Exception? inner=null) : InvalidOperationException(message,inner);

/// <summary>A reviewed new copy, with the existing durable intent and outcome checks.</summary>
internal sealed class CertificateReissueFlow(MutationKind kind)
{
    internal static SqlParameter[]? ReviewLeave(IWin32Window? owner,int certificateId)
    {
        using var preview=DatabaseHelper.QueryUser("GetLeaveCertificateReissuePreview",DatabaseHelper.Int("@LeaveRecordId",certificateId));
        if(preview.Rows.Count!=1)throw new InvalidOperationException("The leave certificate could not be read. Refresh its history.");
        var row=preview.Rows[0];
        string Date(string column)=>row.IsNull(column)?"Not recorded":Convert.ToDateTime(row[column]).ToString("dd-MMM-yyyy");
        if(!ModernMessageDialog.Confirm(owner,
            $"Current Admission name: {row["CurrentStudentName"]}\nFather name: {row["CurrentFatherName"]}\nDOB: {Date("CurrentDateOfBirth")}\nAdmission date: {Date("CurrentAdmissionDate")}\n\nIssue a duplicate using these current Admission details? The original leaving date, class and leaving-episode details stay as saved. Earlier copies keep their original details.",
            "Issue Leaving Certificate Again",defaultYes:false))return null;
        return new[]{DatabaseHelper.Int("@ExpectedLatestIssueId",Convert.ToInt32(row["LatestIssueId"])),DatabaseHelper.P("@ExpectedSourceHash",row["SourceHash"])};
    }
    internal bool Blocked { get; private set; }
    internal MutationIntent? Pending { get; private set; }
    internal void Load()
    {
        Blocked=true;
        try {Pending=MutationRecoveryJournal.ReadPending(kind);}
        catch(Exception ex){throw new CertificateReissueException("The earlier reissue recovery record could not be read. Keep it for administrator review before issuing another copy.",ex);}
        Blocked=Pending is not null;
    }
    internal async Task<MutationResult?> CheckAsync()
    {
        Load();
        if(Pending is null)return null;
        var intent=Pending;
        var result=await RecoverableMutationWriter.CheckAsync(intent);
        MutationRecoveryJournal.ClearVerified(intent);Pending=null;Blocked=false;
        return result;
    }
    internal async Task<MutationResult?> IssueAsync(IWin32Window? owner,int certificateId)
    {
        Load();
        if(Blocked)throw new CertificateReissueException("Use Check Reissue before creating another copy. The earlier reissue still needs outcome checking.");
        using var preview=await DatabaseHelper.QueryUserAsync("GetCertificateReissuePreview",
            DatabaseHelper.P("@CertificateKind",kind==MutationKind.RegularCertificateReissue?"Regular":"DOB"),
            DatabaseHelper.Int("@CertificateRecordId",certificateId));
        if(preview.Rows.Count!=1)throw new CertificateReissueException("The certificate could not be read. Reload its history before reissuing.");
        var row=preview.Rows[0];
        if(row.IsNull("CurrentDateOfBirth"))throw new CertificateReissueException("Record the student's correct birth date in Admission before reissuing.");
        DateTime saved=Convert.ToDateTime(row["SavedDateOfBirth"]).Date,current=Convert.ToDateTime(row["CurrentDateOfBirth"]).Date,date=DateTime.Today;
        if(!ModernMessageDialog.Confirm(owner,
            $"Latest saved copy DOB: {saved:dd-MMM-yyyy}\r\nCurrent Admission DOB: {current:dd-MMM-yyyy}\r\nCurrent Admission name: {row["CurrentStudentName"]}\r\nFather name: {row["CurrentFatherName"]}\r\nNew certificate date: {date:dd-MMM-yyyy}\r\n\r\nIssue a new copy using these current Admission details? Earlier copies keep their original details. The saved certificate class, where applicable, stays the same.",
            "Issue Certificate Again",defaultYes:false))return null;
        var args=new[]{DatabaseHelper.Int("@CertificateRecordId",certificateId),DatabaseHelper.Int("@ExpectedLatestIssueId",Convert.ToInt32(row["LatestIssueId"])),
            DatabaseHelper.Date("@ExpectedDateOfBirth",current),DatabaseHelper.Date("@CertificateDate",date),DatabaseHelper.P("@ExpectedSourceHash",row["SourceHash"])};
        int user=CurrentUser.UserId;
        string scope=await RecoverableMutationWriter.GetScopeAsync();
        MutationWriteResult outcome;
        using(var journal=MutationRecoveryJournal.Begin(kind,user,scope,RecoverableMutationWriter.PayloadHash(kind,user,args),
            $"Certificate {certificateId}: reissue DOB {saved:yyyy-MM-dd} to {current:yyyy-MM-dd}"))
        {
            Pending=journal.Intent;Blocked=true;
            outcome=await RecoverableMutationWriter.SaveAsync(Pending,args);
        }
        if(outcome.Outcome==MutationOutcome.Unconfirmed)
            throw new CertificateReissueException("The reissue outcome is not yet confirmed. Use Check Reissue; do not issue another copy.",outcome.Error);
        MutationRecoveryJournal.ClearVerified(Pending!);Pending=null;Blocked=false;
        if(outcome.Outcome==MutationOutcome.Rejected)throw outcome.Error??new InvalidOperationException("The certificate was not reissued.");
        return outcome.Result??throw new InvalidDataException("The saved reissue identity is missing.");
    }
}
