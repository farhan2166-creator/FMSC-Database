namespace FMSC_SMS.Core;

/// <summary>A dashboard reference card for all application-wide keyboard shortcuts.</summary>
public sealed class HotKeysPage : V2Page
{
    public HotKeysPage()
        : base("Hot Keys", "Keyboard shortcuts for faster data entry")
    {
        var table = new DataTable();
        table.Columns.Add("Action", typeof(string));
        table.Columns.Add("Key", typeof(string));
        table.Rows.Add(AppHotKeys.QuickNavigationAction, AppHotKeys.QuickNavigationKey);
        foreach(var shortcut in AppHotKeys.DashboardNavigation)table.Rows.Add(shortcut.Caption,shortcut.Hint);
        foreach (AppHotKeys.ActionShortcut shortcut in AppHotKeys.PageActions)
            table.Rows.Add(shortcut.Action, shortcut.DisplayKey);
        foreach ((string action, string key) in AppHotKeys.TabActions)
            table.Rows.Add(action, key);

        Grid.AutoGenerateColumns = false;
        Grid.Columns.Clear();
        Grid.Columns.Add(new DataGridViewTextBoxColumn {
            Name = "Action",
            HeaderText = "Action",
            DataPropertyName = "Action",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            FillWeight = 70
        });
        Grid.Columns.Add(new DataGridViewTextBoxColumn {
            Name = "Key",
            HeaderText = "Key",
            DataPropertyName = "Key",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            FillWeight = 30
        });
        Grid.DataSource = table;
        SetContent(
            Grid,
            "Hold Ctrl to see shortcuts, or hover a button. Underlined letters identify assigned letter keys. Ctrl+F focuses search; dashboard shortcuts open a page or expand a navigation group. Actions require a visible, enabled button.");
    }
}
