namespace FMSC_SMS.Core;
/// <summary>Small shared mechanics used by the certificate screens; certificate-specific business rules stay on their pages.</summary>
internal static class CertificateUi
{
    internal static bool ConfirmCertificateSelection(IWin32Window? owner, DataGridView grid,
        string idColumn, int currentId, int requestedId, bool hasEdits)
    {
        if (!hasEdits) return true;
        if (requestedId == currentId) return false;
        if (ModernMessageDialog.Confirm(owner,
            "Discard the unsaved certificate changes and select another certificate?",
            UiText.Dialogs.UnsavedChanges)) return true;
        if (grid.Columns.Contains(idColumn))
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow || !int.TryParse(Convert.ToString(row.Cells[idColumn].Value), out int id) || id != currentId) continue;
                grid.ClearSelection();
                var cell = row.Cells.Cast<DataGridViewCell>().FirstOrDefault(c => c.OwningColumn.Visible);
                if (cell is not null) grid.CurrentCell = cell;
                row.Selected = true;
                break;
            }
        }
        return false;
    }

    public static Panel Field(string caption, Control control, bool required = false, bool fillHeight = true)
    {
        Panel field = HumanUi.Field(caption, control, required, fillHeight);
        if (control is ModernTextBox { Multiline: false } || control is ModernComboBox ||
            control is ModernDatePicker || control is StudentPicker || control is NumericUpDown)
        {
            control.Dock = DockStyle.None;
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            control.Height = AppMetrics.InputControlHeight;
            control.Margin = new Padding(0);
        }
        return field;
    }

    public static int GetInt(DataRow row, string column)
    {
        if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value)
            return 0;
        return int.TryParse(Convert.ToString(row[column]), out int value) ? value : 0;
    }
    public static decimal GetDecimal(DataRow row, string column)
    {
        if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value)
            return 0m;
        return decimal.TryParse(Convert.ToString(row[column]), out decimal value) ? value : 0m;
    }
    public static string GetString(DataRow row, string column)
    {
        if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value)
            return string.Empty;
        return Convert.ToString(row[column])?.Trim() ?? string.Empty;
    }
    public static DateTime? GetDate(DataRow row, string column)
    {
        if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value)
            return null;
        if (row[column] is DateTime date)
            return date;
        return DateTime.TryParse(Convert.ToString(row[column]), out DateTime parsed) ? parsed : null;
    }
    public static void ShowReport(
        IWin32Window? owner,
        string title,
        string reportPath,
        string dataSourceName,
        DataTable data,
        IReadOnlyList<string>? provisionalSubjects = null)
    {
        string? definition = null;
        if (!CertificateLetterhead.UsesPreprintedPad(reportPath))
        {
            DataTable profile = DatabaseHelper.QueryUser("GetSchoolProfile");
            definition = CertificateLetterhead.Definition(reportPath, profile.Rows.Count > 0 ? profile.Rows[0] : null,
                data.Rows.Count > 0 ? data.Rows[0] : null, provisionalSubjects);
        }
        RdlcReportPreview.Show(
            owner,
            title,
            reportPath,
            dataSourceName,
            data,
            reportDefinition: definition,
            enableExternalImages: definition is not null);
    }
}
