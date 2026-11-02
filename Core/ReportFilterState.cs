using System.Globalization;

namespace FMSC_SMS.Core;

/// <summary>Explicit report controls only. No result rows, student selections or transaction drafts.</summary>
internal static class ReportFilterState
{
    internal static Dictionary<string,string> Capture(IEnumerable<(string Key,Control Control)> fields)
    {
        var state=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach (var (key,control) in fields)
            state[key]=control switch {
                LookupBox lookup => "id:"+lookup.SelectedId.ToString(CultureInfo.InvariantCulture),
                ModernComboBox combo => "choice:"+combo.Text,
                ModernDatePicker date => $"date:{date.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)}:{(date.ShowCheckBox && !date.Checked ? "0" : "1")}",
                CheckBox check => "check:"+(check.Checked ? "1" : "0"),
                ModernTextBox text => "text:"+text.Text,
                _ => throw new InvalidOperationException("Unsupported saved report filter: "+key)
            };
        return state;
    }

    // Ordered restoration supports dependencies: report view before grouping, campus before class.
    // The caller restores its captured previous state if any choice is no longer available.
    internal static void Restore(IEnumerable<(string Key,Control Control)> fields,IReadOnlyDictionary<string,string> state)
    {
        foreach (var (key,control) in fields)
        {
            if (!state.TryGetValue(key,out string? value)) continue; // Forward-compatible additions keep current defaults.
            int separator=value.IndexOf(':');
            if (separator<0) throw new ReportViewValidationException("Invalid saved filter: "+key);
            string kind=value[..separator],payload=value[(separator+1)..];
            switch (control)
            {
                case LookupBox lookup when kind=="id":
                    if (!int.TryParse(payload,NumberStyles.None,CultureInfo.InvariantCulture,out int id) || lookup.DataSource is null)
                        throw new ReportViewValidationException("Load the report filters before applying this view.");
                    bool found=lookup.Items.Cast<object>().Any(item => item is DataRowView row
                        && Convert.ToString(row[lookup.ValueMember],CultureInfo.InvariantCulture)==payload);
                    if (!found) throw new ReportViewValidationException($"The saved {key} is no longer available. Choose an available value and save the view again.");
                    lookup.SelectedValue=id;
                    break;
                case ModernComboBox combo when kind=="choice":
                    object? choice=combo.Items.Cast<object>().FirstOrDefault(item => Convert.ToString(item)==payload);
                    if (choice is null) throw new ReportViewValidationException($"The saved {key} option is unavailable for this report or your permissions.");
                    combo.SelectedItem=choice;
                    break;
                case ModernDatePicker date when kind=="date":
                    var parts=payload.Split(':');
                    if (parts.Length!=2 || !DateTime.TryParseExact(parts[0],"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day)
                        || day<date.MinDate.Date || day>date.MaxDate.Date || parts[1] is not ("0" or "1"))
                        throw new ReportViewValidationException("The saved date is outside the allowed range: "+key);
                    date.Value=day;
                    if (date.ShowCheckBox) date.Checked=parts[1]=="1";
                    break;
                case CheckBox check when kind=="check" && payload is "0" or "1": check.Checked=payload=="1"; break;
                case ModernTextBox text when kind=="text": text.Text=payload; break;
                default: throw new ReportViewValidationException("The saved filter is incompatible: "+key);
            }
        }
    }
}
