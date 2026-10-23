namespace FMSC_SMS.Core;
/// <summary>Small shared mechanics used by the certificate screens; certificate-specific business rules stay on their pages.</summary>
internal static class CertificateUi
{
    internal const string FieldGridTag="CertificateFieldGrid";
    internal static bool ConfirmCertificateSelection(IWin32Window? owner, DataGridView grid,
        string idColumn, int currentId, int requestedId, bool hasEdits,Func<bool>? confirmDiscard=null)
    {
        if (!hasEdits) return true;
        if (requestedId == currentId) return false;
        if(confirmDiscard?.Invoke()??ModernMessageDialog.Confirm(owner,
            "Discard the unsaved certificate changes and select another certificate?",
            UiText.Dialogs.UnsavedChanges))return true;
        if(currentId<=0)
        {
            // Stay in a new draft: there is no saved row to restore.
            grid.ClearSelection();grid.CurrentCell=null;return false;
        }
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
        Size requestedMaximum=control.MaximumSize;
        var field = (TableLayoutPanel)HumanUi.Field(caption, control, required, fillHeight);
        field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        if (control is ModernTextBox { Multiline: false } || control is ModernComboBox ||
            control is ModernDatePicker || control is StudentPicker || control is NumericUpDown)
        {
            // The certificate's field grid owns widths; keep each input directly
            // below its caption instead of vertically centring it in spare space.
            control.MaximumSize=requestedMaximum;
            control.Height = AppMetrics.InputControlHeight;
            control.Dock = DockStyle.Top;
            control.Margin = new Padding(0);
        }
        return field;
    }

    internal static void CompactFields(HumanCard details,TableLayoutPanel fields,TableLayoutPanel root,
        float[] wideWidths,(int Column,int Row,int Span)[] widePositions,int minimumWidth=1200)
    {
        fields.Tag=FieldGridTag;
        var controls=fields.Controls.Cast<Control>().ToArray();
        var original=controls.Select(control=>(Column:fields.GetColumn(control),Row:fields.GetRow(control),Span:fields.GetColumnSpan(control))).ToArray();
        var originalWidths=fields.ColumnStyles.Cast<ColumnStyle>().Select(style=>style.Width).ToArray();
        bool? wideLayout=null;
        void Arrange()
        {
            bool wide=details.Content.ClientSize.Width>=AppScale.Scale(minimumWidth);
            if(wideLayout==wide)return;
            wideLayout=wide;
            var positions=wide?widePositions:original;
            var widths=wide?wideWidths:originalWidths;
            fields.SuspendLayout();root.SuspendLayout();
            try
            {
                foreach(Control control in controls)fields.SetColumnSpan(control,1);
                fields.ColumnCount=widths.Length;fields.RowCount=positions.Max(position=>position.Row)+1;
                fields.ColumnStyles.Clear();fields.RowStyles.Clear();
                foreach(float width in widths)fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,width));
                for(int row=0;row<fields.RowCount;row++)fields.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.FieldRowHeight));
                for(int index=0;index<controls.Length;index++)
                {
                    var position=positions[index];
                    fields.SetCellPosition(controls[index],new TableLayoutPanelCellPosition(position.Column,position.Row));
                    fields.SetColumnSpan(controls[index],position.Span);
                    controls[index].TabIndex=position.Row*fields.ColumnCount+position.Column;
                }
                root.RowStyles[0].Height=AppMetrics.FieldCardHeight(fields.RowCount)+AppMetrics.ButtonRowHeight;
            }
            finally{fields.ResumeLayout(true);root.ResumeLayout(true);}
        }
        details.Content.SizeChanged+=(_,_)=>Arrange();
        Arrange();
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
