namespace FMSC_SMS.Core;
public interface IUnsavedChanges
{
    bool HasUnsavedChanges { get; }
    string UnsavedChangesMessage { get; }
}
