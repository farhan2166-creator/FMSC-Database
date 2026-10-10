namespace FMSC_SMS.Core;

/// <summary>Searchable reference for application and context-specific shortcuts.</summary>
public sealed class HotKeysPage : V2Page
{
    public HotKeysPage()
        : base("Hot Keys", "Familiar shortcuts • one modifier at most • actions follow the active editor")
    {
        var table = new DataTable();
        table.Columns.Add("Action", typeof(string));
        table.Columns.Add("Key", typeof(string));
        table.Columns.Add("Description", typeof(string));
        void Add(string action, string key, string description) => table.Rows.Add(action, key, description);
        Add("Quick navigation", AppHotKeys.QuickNavigationKey, "Search for a page and press Enter to open it.");
        Add("Shortcut help", "F1", "Open this reference from the dashboard.");
        foreach (var shortcut in AppHotKeys.DashboardNavigation)
            Add(shortcut.Caption, shortcut.Hint, "Open this dashboard page or expand its navigation group.");
        foreach (var shortcut in AppHotKeys.PageActions)
            Add(shortcut.Action, shortcut.DisplayKey, shortcut.Description);
        foreach (var shortcut in AppHotKeys.TabActions)
            Add(shortcut.Action, shortcut.Key, "Switch within the active tab group; disabled tabs are skipped.");
        Add("Preview: page setup", "Alt+P", "Set paper size, orientation and margins.");
        Add("Preview: previous / next page", "Ctrl+Left / Ctrl+Right", "Move through the report preview.");
        Add("Preview: whole page", "Ctrl+0", "Fit the full report page in the preview.");
        Add("Preview: page width", "Ctrl+1", "Fit the report to the preview width.");
        Add("Close dialog / preview", "Esc", "Close through the normal unsaved-change checks.");
        Add("Copy / Cut / Paste", "Ctrl+C / Ctrl+X / Ctrl+V", "Use standard editing in the focused input.");
        Add("Select all / Undo", "Ctrl+A / Ctrl+Z", "Use standard editing where the focused input supports it.");
        Add("Next / previous field", "Tab / Shift+Tab", "Move keyboard focus between controls.");

        Grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        Grid.AutoGenerateColumns = false;
        Grid.Columns.Clear();
        foreach (var column in new[] {
            (Name: "Action", Weight: 27f, Minimum: 140),
            (Name: "Key", Weight: 25f, Minimum: 150),
            (Name: "Description", Weight: 48f, Minimum: 240)
        })
            Grid.Columns.Add(new DataGridViewTextBoxColumn {
                Name = column.Name, HeaderText = column.Name, DataPropertyName = column.Name,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = column.Weight, MinimumWidth = column.Minimum
            });
        Grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        Grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        Grid.DataSource = table;
        var search = new ModernTextBox {
            Dock = DockStyle.Fill, PlaceholderText = "Search shortcuts, actions or descriptions…",
            AccessibleName = "Search shortcuts", Name = "shortcutSearch"
        };
        search.TextChanged += (_, _) => {
            string query = search.Text.Trim();
            // Filter values directly: quotes and wildcard characters are literal input.
            foreach (DataRow row in table.Rows)
                row["Visible"] = new[] { "Action", "Key", "Description" }
                    .Any(column => row[column].ToString()!.Contains(query, StringComparison.OrdinalIgnoreCase));
        };
        table.Columns.Add("Visible", typeof(bool));
        foreach (DataRow row in table.Rows) row["Visible"] = true;
        table.DefaultView.RowFilter = "Visible = true";
        var content = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.Scale(44)));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(search, 0, 0);
        content.Controls.Add(Grid, 0, 1);
        SetContent(content,
            "Hold Ctrl or Alt to reveal available key badges. Page actions require a visible, enabled, unambiguous target. Preview shortcuts apply only inside a report preview; typing shortcuts stay with the focused input.");
    }
}
