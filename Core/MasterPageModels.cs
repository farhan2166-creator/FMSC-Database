namespace FMSC_SMS.Core;

public sealed record FieldSpec(
    string Key,
    string Caption,
    FieldKind Kind = FieldKind.Text,
    int Width = 220,
    bool Required = false,
    bool Multiline = false,
    string? LookupProcedure = null,
    string LookupIdColumn = "Id",
    string LookupTextColumn = "Name",
    int MaxLength = 1000,
    IReadOnlyList<string>? Options = null,
    bool LookupAllowCustomValue = false,
    string? LookupParentField = null,
    string LookupParentIdColumn = "Id",
    string LookupParentParameter = "@Id",
    Func<Form>? ManageDialogFactory = null);

public interface IManagedLookupDialog
{
    int SelectedId { get; }
    string? SelectedParentValue { get; }
}

public enum FieldKind
{
    Text,
    Integer,
    Decimal,
    Date,
    Boolean,
    ComboInt,
    ComboString,
    Password,
    Time
}

internal sealed class MasterEditorChangeTracker
{
    private readonly Func<Dictionary<string, object?>> _readValues;
    private Dictionary<string, object?>? _baseline;

    internal MasterEditorChangeTracker(Func<Dictionary<string, object?>> readValues) => _readValues = readValues;

    internal bool HasChanges
    {
        get
        {
            if (_baseline is null)
                return false;
            Dictionary<string, object?> current;
            try { current = _readValues(); }
            catch (Exception nonFatalException13)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException13, "MasterPageModels: MasterEditorChangeTracker fallback");
                return false;
            }
            foreach ((string key, object? value) in current)
            {
                if (!_baseline.TryGetValue(key, out object? previous))
                {
                    if (IsUnchosenLookup(key, value))
                        continue;
                    return true;
                }
                if (!Equals(previous, value))
                    return true;
            }
            return _baseline.Any(pair =>
                !current.ContainsKey(pair.Key) && !IsUnchosenLookup(pair.Key, pair.Value));
        }
    }

    internal void Capture()
    {
        try { _baseline = _readValues(); }
        catch (Exception nonFatalException14)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException14, "MasterPageModels: Capture fallback");
            _baseline = null;
        }
    }

    private static bool IsUnchosenLookup(string key, object? value) =>
        key.EndsWith("Id", StringComparison.Ordinal) && value is not null &&
        int.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int id) && id == 0;
}

/// <summary>Coordinates record reloads and exposes a generation for related background reads.</summary>
internal sealed class MasterReloadSession : IDisposable
{
    internal sealed record Ticket(CancellationToken Token, int Generation);
    private CancellationTokenSource? _cancellation;
    internal int Generation { get; private set; }

    internal Ticket Begin()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        return new Ticket(_cancellation.Token, ++Generation);
    }

    internal bool IsCurrent(Ticket ticket) =>
        !ticket.Token.IsCancellationRequested && ticket.Generation == Generation;

    internal void Cancel()
    {
        ++Generation;
        _cancellation?.Cancel();
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        ++Generation;
    }
}
