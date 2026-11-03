namespace FMSC_SMS.Core;

/// <summary>Shared report filter sizing: fit each input to its cell and wrap rows when space is limited.</summary>
internal static class ReportFilterLayout
{
    internal static void ConstrainField(TableLayoutPanel field, Control input)
    {
        field.ColumnStyles.Clear();
        field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        input.MinimumSize = new Size(0, input.MinimumSize.Height);
        foreach (var caption in field.Controls.OfType<Label>()) caption.AutoEllipsis = true;
    }

    internal static void Fit(TableLayoutPanel table, int rowHeight, int maximumColumns = int.MaxValue)
    {
        var fields = table.Controls.Cast<Control>().ToArray();
        bool arranging = false;
        void Arrange()
        {
            if (arranging || table.IsDisposed) return;
            int minimumWidth = UiDpi.Metric(table, AppScale.Scale(150));
            var shape = ReportFilterArrangement.For(fields.Length,
                Math.Min(table.ClientSize.Width - table.Padding.Horizontal,
                    maximumColumns == int.MaxValue ? int.MaxValue : maximumColumns * minimumWidth), minimumWidth);
            if (table.ColumnCount == shape.Columns && table.RowCount == shape.Rows) return;
            arranging = true;
            table.SuspendLayout();
            try
            {
                table.RowCount = Math.Max(table.RowCount, shape.Rows);
                table.ColumnCount = shape.Columns;
                table.ColumnStyles.Clear();
                for (int i = 0; i < shape.Columns; i++)
                    table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / shape.Columns));
                for (int i = 0; i < fields.Length; i++)
                    table.SetCellPosition(fields[i], new TableLayoutPanelCellPosition(i % shape.Columns, i / shape.Columns));
                table.RowCount = shape.Rows;
                table.RowStyles.Clear();
                for (int i = 0; i < shape.Rows; i++) table.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
                table.Height = rowHeight * shape.Rows + table.Padding.Vertical;
            }
            finally { table.ResumeLayout(true); arranging = false; }
        }
        table.SizeChanged += (_, _) => Arrange();
        table.HandleCreated += (_, _) => Arrange();
        table.DpiChangedAfterParent += (_, _) => Arrange();
    }
}
