namespace FMSC_SMS.Core;

internal sealed record SavedQuickRegistration(int Id, string Reference);
internal enum QuickRegistrationWriteOutcome { Saved, Rejected, Unconfirmed }
internal sealed record QuickRegistrationWriteResult(QuickRegistrationWriteOutcome Outcome, SavedQuickRegistration? Student=null, Exception? Error=null);
internal interface IQuickRegistrationTransaction
{
    bool SubmissionStarted { get; }
    void Commit();
    void Rollback();
}

internal static class QuickRegistrationSaveCoordinator
{
    internal static SavedQuickRegistration ReadResponse(DataSet response)
    {
        var tables=response.Tables.Cast<DataTable>().Where(t=>t.Columns.Contains("AdmissionId")&&t.Columns.Contains("QuickReference")).ToArray();
        if(tables.Length!=1||tables[0].Rows.Count!=1||tables[0].Rows[0]["AdmissionId"] is not int id||id<=0||
            tables[0].Rows[0]["QuickReference"] is not string reference||string.IsNullOrWhiteSpace(reference))
            throw new InvalidDataException("The registration save did not return one valid student identity.");
        return new(id,reference);
    }
    internal static QuickRegistrationWriteResult Execute(IQuickRegistrationTransaction transaction,Func<SavedQuickRegistration> write)
    {
        bool commitAttempted=false;SavedQuickRegistration? saved=null;
        try
        {
            saved=write();
            if(saved.Id<=0||string.IsNullOrWhiteSpace(saved.Reference))throw new InvalidDataException("The saved registration identity is invalid.");
            commitAttempted=true;transaction.Commit();return new(QuickRegistrationWriteOutcome.Saved,saved);
        }
        catch(Exception ex)
        {
            bool rolledBack=false;
            try{transaction.Rollback();rolledBack=true;}catch{ /* The request marker must resolve an unconfirmed rollback. */ }
            return new(commitAttempted||transaction.SubmissionStarted&&!rolledBack?QuickRegistrationWriteOutcome.Unconfirmed:QuickRegistrationWriteOutcome.Rejected,saved,ex);
        }
    }
}
internal sealed class QuickRegistrationOutcomeUnknownException(Exception inner)
    : Exception("The registration may already have been saved. Use Check Save Outcome before registering this child again.",inner);
