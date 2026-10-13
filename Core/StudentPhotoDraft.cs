namespace FMSC_SMS.Core;

internal enum PhotoSaveOutcome { NoPendingPhoto, Saved, Failed, Superseded, Busy }
internal sealed record PhotoSaveResult(PhotoSaveOutcome Outcome, Exception? Error = null);

/// <summary>Prepared bytes survive failed saves and belong to one immutable selection.</summary>
internal sealed class StudentPhotoDraft
{
    internal sealed record Selection(byte[] Data, string FileName, bool SaveFailed = false);
    internal Selection? Current { get; private set; }
    internal bool IsSaving { get; private set; }
    internal void Stage(byte[] data, string fileName)
    {
        StudentPhotoInput.ValidateStoredData(data);
        Current = new Selection(data, fileName);
    }
    internal void Discard() => Current = null;

    internal async Task<PhotoSaveResult> SaveAsync(int admissionId,
        Func<int, byte[], string?, string?, Task> write, Func<bool> isCurrent)
    {
        if (IsSaving) return new(PhotoSaveOutcome.Busy);
        var selection = Current;
        if (selection is null) return new(PhotoSaveOutcome.NoPendingPhoto);
        if (admissionId <= 0) throw new ArgumentOutOfRangeException(nameof(admissionId));
        if (!isCurrent()) return new(PhotoSaveOutcome.Superseded);
        IsSaving = true;
        try
        {
            StudentPhotoInput.ValidateStoredData(selection.Data);
            await write(admissionId, selection.Data, selection.FileName, "image/jpeg");
            if (!isCurrent() || !ReferenceEquals(Current, selection)) return new(PhotoSaveOutcome.Superseded);
            Current = null;
            return new(PhotoSaveOutcome.Saved);
        }
        catch (Exception ex)
        {
            if (!isCurrent() || !ReferenceEquals(Current, selection)) return new(PhotoSaveOutcome.Superseded, ex);
            Current = selection with { SaveFailed = true };
            return new(PhotoSaveOutcome.Failed, ex);
        }
        finally { IsSaving = false; }
    }
}
