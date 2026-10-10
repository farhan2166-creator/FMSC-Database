namespace FMSC_SMS.Core;

/// <summary>Caches the student list briefly while student pickers filter as the operator types.</summary>
public static class StudentSearchCache
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);
    private static readonly object Sync = new();
    private static int _generation;
    private static DataTable? _all;
    private static DateTime _loadedAtUtc;
    private static Task<DataTable>? _loading;

    public static void Invalidate()
    {
        AdmissionNumbers.Invalidate();
        lock (Sync)
        {
            _generation++;
            _all = null;
            _loading = null;
        }
    }

    public static void Warm()
    {
        try
        {
            _ = GetAllAsync().ContinueWith(
                task => { _ = task.Exception; },
                TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (Exception nonFatalException20)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException20, "StudentSearchCache: Warm fallback");
        }
    }

    public static Task<DataTable> GetAllAsync()
    {
        lock (Sync)
        {
            if (_all is not null && DateTime.UtcNow - _loadedAtUtc < MaxAge)
                return Task.FromResult(_all);
            if (_loading is not null && !_loading.IsCompleted)
                return _loading;
            _loading = LoadAsync(_generation);
            return _loading;
        }
    }

    private static async Task<DataTable> LoadAsync(int generation)
    {
        DataTable table = await DatabaseHelper.QueryUserAsync(
            "GetStudents",
            DatabaseHelper.Int("@CampusId", 0),
            DatabaseHelper.Int("@AcademicYearId", 0),
            DatabaseHelper.P("@Search", null),
            DatabaseHelper.Bit("@IncludeFormerStudents", true));
        lock (Sync)
        {
            if (generation == _generation)
            {
                _all = table;
                _loadedAtUtc = DateTime.UtcNow;
                return table;
            }
        }
        // A save invalidated this read while it was in flight. Its callers also
        // need the refreshed list, not just the next picker that opens.
        return await GetAllAsync();
    }
}
