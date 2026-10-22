namespace FMSC_SMS.Core;

/// <summary>Single source of truth for the keyboard shortcuts shown and handled by the UI.</summary>
internal static class AppHotKeys
{
    internal static string PlainCaption(string caption)=>caption.Replace("&&","&");
    internal sealed record ActionShortcut(
        string Action,
        string DisplayKey,
        Keys KeyData,
        string Description,
        params string[] ButtonPrefixes);

    internal static readonly ActionShortcut[] PageActions =
    {
        new("Save", "Ctrl+S", Keys.Control | Keys.S, "Save changes in the active editor.", "Save"),
        new("Print", "Ctrl+P", Keys.Control | Keys.P, "Print or preview the current record or report.", "Print"),
        new("New record", "Ctrl+N", Keys.Control | Keys.N, "Start a new record using the page’s New action.", "New", "Clear / New"),
        new("Find / Search", "Ctrl+F", Keys.Control | Keys.F, "Focus the page search, or run its Find action.", "Find", "Search"),
        new("Export", "Ctrl+E", Keys.Control | Keys.E, "Export the current records or report.", "Export"),
        new("Refresh / Reload", "F5", Keys.F5, "Reload current data and refresh lookup caches.", "Refresh", "Reload"),
        new("Load", "Ctrl+L", Keys.Control | Keys.L, "Load data using the current selection.", "Load"),
        new("View", "Ctrl+O", Keys.Control | Keys.O, "Open the selected record or view.", "View"),
        new("Add", "Insert", Keys.Insert, "Add an item to the current list.", "Add"),
        new("Edit", "F2", Keys.F2, "Edit the selected record.", "Edit"),
        new("Clear", "Alt+C", Keys.Alt | Keys.C, "Reset the current editor using its Clear action.", "Clear"),
        new("Run", "Ctrl+R", Keys.Control | Keys.R, "Run the current operation.", "Run"),
        new("Generate", "Ctrl+G", Keys.Control | Keys.G, "Generate results from the current options.", "Generate"),
        new("Scan", "F8", Keys.F8, "Scan using the page’s Scan action.", "Scan"),
        new("Complete workflow", "Ctrl+Enter", Keys.Control | Keys.Enter, "Apply, confirm, collect or process; normal validation still applies.",
            "Apply", "Confirm", "Execute", "Post", "Process", "Queue", "Collect"),
        new("Delete / Remove", "Alt+Delete", Keys.Alt | Keys.Delete, "Delete the selected item; normal confirmation still applies.", "Delete", "Remove")
    };

    internal const string QuickNavigationAction = "Quick navigation";
    internal const string QuickNavigationKey = "Ctrl+K";
    internal static readonly (string Caption,Keys Keys,string Hint)[] DashboardNavigation = {
        ("Dashboard",Keys.Alt|Keys.D1,"Alt+1"),
        ("Daily Work",Keys.Alt|Keys.D2,"Alt+2"),
        ("Student Management",Keys.Alt|Keys.D3,"Alt+3"),
        ("Staff",Keys.Alt|Keys.D5,"Alt+5"),
        ("Reports",Keys.Alt|Keys.D6,"Alt+6"),
        ("School Setup",Keys.Alt|Keys.D7,"Alt+7"),
        (PageTitles.BackupRestore,Keys.Alt|Keys.D8,"Alt+8")
    };

    internal static readonly (string Action, string Key)[] TabActions =
    {
        ("Next tab", "Ctrl+Tab / Ctrl+PageDown"),
        ("Previous tab", "Ctrl+PageUp"),
        ("Open tab 1–9", "Ctrl+1 … Ctrl+9")
    };

    internal static bool IsValidShortcut(Keys keys)
    {
        Keys modifiers = keys & Keys.Modifiers;
        Keys code = keys & Keys.KeyCode;
        return code is not (Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu) &&
            modifiers is Keys.None or Keys.Control or Keys.Alt or Keys.Shift;
    }

    internal static bool TrySwitchTab(Control root, Keys keyData)
    {
        TabControl? tabs = Descendants(root)
            .OfType<TabControl>()
            .Where(tab => tab.Visible && tab.Enabled && tab.TabPages.Count > 0)
            .OrderByDescending(tab => tab.ContainsFocus)
            .ThenByDescending(Depth)
            .FirstOrDefault();
        if (tabs is null)
            return false;

        if (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.PageDown))
            return MoveTab(tabs, 1);
        if (keyData == (Keys.Control | Keys.PageUp))
            return MoveTab(tabs, -1);

        Keys keyCode = keyData & Keys.KeyCode;
        Keys modifiers = keyData & Keys.Modifiers;
        if (modifiers == Keys.Control && keyCode >= Keys.D1 && keyCode <= Keys.D9)
        {
            int index = keyCode - Keys.D1;
            return index < tabs.TabPages.Count && SelectTab(tabs, index);
        }
        return false;
    }

    private static int Depth(Control control)
    {
        int depth = 0;
        for (Control? parent = control.Parent; parent is not null; parent = parent.Parent) depth++;
        return depth;
    }

    private static bool MoveTab(TabControl tabs, int direction)
    {
        for (int offset = 1; offset <= tabs.TabPages.Count; offset++)
        {
            int index = (tabs.SelectedIndex + direction * offset + tabs.TabPages.Count) % tabs.TabPages.Count;
            if (tabs.TabPages[index].Enabled) return SelectTab(tabs, index);
        }
        return false;
    }

    private static bool SelectTab(TabControl tabs, int index)
    {
        if (index < 0 || index >= tabs.TabPages.Count || !tabs.TabPages[index].Enabled)
            return false;
        tabs.SelectedIndex = index;
        tabs.Focus();
        return true;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child))
                yield return descendant;
        }
    }
}
