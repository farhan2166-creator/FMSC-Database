namespace FMSC_SMS.Core;
/// <summary>Small shared mechanics used by the certificate screens; certificate-specific business rules stay on their pages.</summary>
internal static class CertificateUi
{
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
