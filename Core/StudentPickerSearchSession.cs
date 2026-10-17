namespace FMSC_SMS.Core;

/// <summary>Owns the asynchronous search generation so stale student-list loads cannot update a picker.</summary>
internal sealed class StudentPickerSearchSession
{
    private int _generation;

    internal void Cancel() => _generation++;

    internal void Begin(
        DataTable? fixedSource,
        string initialTerm,
        bool listAll,
        Func<string> readCurrentTerm,
        Func<bool> ownerDisposed,
        Action<DataTable, string> apply,
        Action<Exception> fail,
        Action? loading = null,
        Func<Task<DataTable>>? loadSource = null)
    {
        int generation = ++_generation;
        Task<DataTable> load;
        try
        {
            load = fixedSource is not null
                ? Task.FromResult(fixedSource)
                : (loadSource ?? StudentSearchCache.GetAllAsync)();
        }
        catch (Exception ex)
        {
            fail(ex);
            return;
        }
        if (load.IsCompletedSuccessfully)
        {
            apply(load.Result, initialTerm);
            return;
        }
        loading?.Invoke();
        _ = CompleteAsync(load, generation, listAll, readCurrentTerm, ownerDisposed, apply, fail);
    }

    private async Task CompleteAsync(
        Task<DataTable> load,
        int generation,
        bool listAll,
        Func<string> readCurrentTerm,
        Func<bool> ownerDisposed,
        Action<DataTable, string> apply,
        Action<Exception> fail)
    {
        try
        {
            DataTable all = await load;
            if (ownerDisposed() || generation != _generation)
                return;
            string term = listAll ? string.Empty : readCurrentTerm();
            if (listAll || term.Length > 0)
                apply(all, term);
        }
        catch (Exception ex)
        {
            if (!ownerDisposed() && generation == _generation)
                fail(ex);
        }
    }
}
