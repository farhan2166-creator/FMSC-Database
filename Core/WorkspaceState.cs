using System.Globalization;
using System.Runtime.CompilerServices;

namespace FMSC_SMS.Core;

internal static class WorkspaceState
{
    private sealed class Marker
    {
        public string Key = string.Empty;
    }
    private static readonly ConditionalWeakTable<Control, Marker> Markers = new();
    public static T Remember<T>(T control, string key) where T : Control
    {
        if (control is null)
            throw new ArgumentNullException(nameof(control));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A workspace-state key is required.", nameof(key));
        Markers.Remove(control);
        Markers.Add(control, new Marker { Key = key });
        return control;
    }
    public static Dictionary<string, string> Capture(Control root)
    {
        var state = new Dictionary<string, string>(StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Control control in DescendantsAndSelf(root))
        {
            if (!Markers.TryGetValue(control, out Marker? marker))
                continue;
            string key = OccurrenceKey(marker.Key, occurrences);
            string? value = CaptureValue(control);
            if (value is not null)
                state[key] = value;
        }
        return state;
    }
    public static void Restore(Control root, IReadOnlyDictionary<string, string> state)
    {
        if (state.Count == 0)
            return;
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Control control in DescendantsAndSelf(root))
        {
            if (!Markers.TryGetValue(control, out Marker? marker))
                continue;
            string key = OccurrenceKey(marker.Key, occurrences);
            if (!state.TryGetValue(key, out string? value))
                continue;
            RestoreValue(control, value);
        }
    }
    private static string OccurrenceKey(string key, Dictionary<string, int> occurrences)
    {
        occurrences.TryGetValue(key, out int occurrence);
        occurrences[key] = occurrence + 1;
        return occurrence == 0 ? key : $"{key}#{occurrence}";
    }
    private static string? CaptureValue(Control control) => control switch {
        LookupBox lookup => "lookup|" + (Convert.ToString(lookup.SelectedValue, CultureInfo.InvariantCulture) ?? string.Empty),
        ModernComboBox combo when combo.DataSource is not null => "bound|" + (Convert.ToString(combo.SelectedValue, CultureInfo.InvariantCulture) ?? string.Empty),
        ModernComboBox combo => "index|" + combo.SelectedIndex.ToString(CultureInfo.InvariantCulture),
        ModernTextBox text => "text|" + text.Text,
        StudentPicker student => "text|" + (student.SelectedId > 0 ? AdmissionNumbers.ForId(student.SelectedId) : student.Text),
        ModernDatePicker date when date.ShowCheckBox => "optionaldate|" +
            date.Value.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + (date.Checked ? "1" : "0"),
        ModernDatePicker date => "date|" + date.Value.Ticks.ToString(CultureInfo.InvariantCulture),
        CheckBox check => "check|" + (check.Checked ? "1" : "0"),
        _ => null
    };
    private static void RestoreValue(Control control, string value)
    {
        int separator = value.IndexOf('|');
        if (separator < 0)
            return;
        string kind = value[..separator];
        string payload = value[(separator + 1)..];
        switch (control)
        {
            case LookupBox lookup when kind == "lookup":
                RestoreBoundValue(lookup, payload);
                break;
            case ModernComboBox combo when kind == "bound":
                RestoreBoundValue(combo, payload);
                break;
            case ModernComboBox combo when kind == "index" &&
                                                 int.TryParse(payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index):
                if (index >= -1 && index < combo.Items.Count)
                    combo.SelectedIndex = index;
                break;
            case ModernTextBox text when kind == "text":
                text.Text = payload;
                break;
            case StudentPicker student when kind == "text":
                student.Text = payload;
                break;
            case ModernDatePicker date when kind == "optionaldate":
                string[] dateParts = payload.Split('|');
                if (dateParts.Length == 2 && long.TryParse(dateParts[0], out long optionalTicks) &&
                    optionalTicks >= date.MinDate.Ticks && optionalTicks <= DateTimePicker.MaximumDateTime.Ticks)
                {
                    date.Value = new DateTime(optionalTicks);
                    date.Checked = dateParts[1] == "1";
                }
                break;
            case ModernDatePicker date when kind == "date" &&
                                                  long.TryParse(payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks):
                try
                {
                    date.Value = new DateTime(ticks);
                }
                catch (ArgumentOutOfRangeException fallbackException13)
                {
                    AppExceptionHandler.LogNonFatal(fallbackException13, "WorkspaceState: ArgumentOutOfRangeException recovery");
                }
                break;
            case CheckBox check when kind == "check":
                check.Checked = payload == "1";
                break;
        }
    }
    private static void RestoreBoundValue(ModernComboBox combo, string persistedValue)
    {
        if (TryRestoreBoundValue(combo, persistedValue))
            return;
        if (combo is LookupBox lookup)
        {
            EventHandler? lookupHandler = null;
            lookupHandler = (_, _) => {
                if (lookup.IsDisposed || TryRestoreBoundValue(lookup, persistedValue))
                    lookup.LookupBound -= lookupHandler;
            };
            lookup.LookupBound += lookupHandler;
            lookup.Disposed += (_, _) => lookup.LookupBound -= lookupHandler;
            return;
        }
        EventHandler? handler = null;
        handler = (_, _) => {
            if (combo.IsDisposed || TryRestoreBoundValue(combo, persistedValue))
                combo.DataSourceChanged -= handler;
        };
        combo.DataSourceChanged += handler;
        combo.Disposed += (_, _) => combo.DataSourceChanged -= handler;
    }
    private static bool TryRestoreBoundValue(ModernComboBox combo, string persistedValue)
    {
        if (combo.DataSource is null || string.IsNullOrWhiteSpace(combo.ValueMember))
            return false;
        object? exactValue = null;
        bool found = false;
        if (combo.DataSource is DataTable table && table.Columns.Contains(combo.ValueMember))
        {
            foreach (DataRow row in table.Rows)
            {
                object candidate = row[combo.ValueMember];
                if (!string.Equals(Convert.ToString(candidate, CultureInfo.InvariantCulture), persistedValue, StringComparison.Ordinal))
                    continue;
                exactValue = candidate;
                found = true;
                break;
            }
        }
        else if (combo.DataSource is DataView view &&
                 view.Table is DataTable viewTable &&
                 viewTable.Columns.Contains(combo.ValueMember))
        {
            foreach (DataRowView row in view)
            {
                object candidate = row[combo.ValueMember];
                if (!string.Equals(Convert.ToString(candidate, CultureInfo.InvariantCulture), persistedValue, StringComparison.Ordinal))
                    continue;
                exactValue = candidate;
                found = true;
                break;
            }
        }
        if (found)
            combo.SelectedValue = exactValue;
        else if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
        return true;
    }
    private static IEnumerable<Control> DescendantsAndSelf(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
        {
            foreach (Control descendant in DescendantsAndSelf(child))
                yield return descendant;
        }
    }
}
