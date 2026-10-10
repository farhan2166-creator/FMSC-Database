namespace FMSC_SMS.Core;

/// <summary>Single source of truth for the keyboard shortcuts shown and handled by the UI.</summary>
internal static class AppHotKeys
{
    internal static string PlainCaption(string caption)=>caption.Replace("&&","\0").Replace("&","").Replace("\0","&");
    internal sealed record ActionShortcut(
        string Action,
        string DisplayKey,
        Keys KeyData,
        params string[] ButtonPrefixes);

    internal static readonly ActionShortcut[] PageActions =
    {
        new("Save", "Ctrl+S", Keys.Control | Keys.S, "Save"),
        new("Print", "Ctrl+P", Keys.Control | Keys.P, "Print"),
        new("New record", "Ctrl+N", Keys.Control | Keys.N, "New", "Clear / New"),
        new("Find / Search", "Ctrl+F", Keys.Control | Keys.F, "Find", "Search"),
        new("Export", "Ctrl+E", Keys.Control | Keys.E, "Export"),
        new("Refresh / Reload", "F5", Keys.F5, "Refresh", "Reload"),
        new("Load", "Ctrl+L", Keys.Control | Keys.L, "Load"),
        new("View", "Ctrl+Shift+V", Keys.Control | Keys.Shift | Keys.V, "View"),
        new("Add", "Ctrl+Shift+A", Keys.Control | Keys.Shift | Keys.A, "Add"),
        new("Edit", "Ctrl+Shift+E", Keys.Control | Keys.Shift | Keys.E, "Edit"),
        new("Clear", "Ctrl+Shift+C", Keys.Control | Keys.Shift | Keys.C, "Clear"),
        new("Run", "Ctrl+R", Keys.Control | Keys.R, "Run"),
        new("Generate", "Ctrl+G", Keys.Control | Keys.G, "Generate"),
        new("Scan", "Ctrl+Shift+S", Keys.Control | Keys.Shift | Keys.S, "Scan"),
        new("Apply / Confirm / Execute / Post / Process / Queue / Collect", "Ctrl+Enter", Keys.Control | Keys.Enter,
            "Apply", "Confirm", "Execute", "Post", "Process", "Queue", "Collect"),
        new("Delete / Remove", "Ctrl+Delete", Keys.Control | Keys.Delete, "Delete", "Remove")
    };

    internal const string QuickNavigationAction = "Quick navigation";
    internal const string QuickNavigationKey = "Ctrl+K";
    internal static readonly (string Caption,Keys Keys,string Hint)[] DashboardNavigation = {
        ("Dashboard",Keys.Control|Keys.Alt|Keys.D,"Ctrl+Alt+D"),
        ("Daily Work",Keys.Control|Keys.Alt|Keys.W,"Ctrl+Alt+W"),
        ("Student Management",Keys.Control|Keys.Alt|Keys.T,"Ctrl+Alt+T"),
        ("Fees & Billing",Keys.Control|Keys.Alt|Keys.F,"Ctrl+Alt+F"),
        ("Staff",Keys.Control|Keys.Alt|Keys.S,"Ctrl+Alt+S"),
        ("Reports",Keys.Control|Keys.Alt|Keys.R,"Ctrl+Alt+R"),
        ("School Setup",Keys.Control|Keys.Alt|Keys.U,"Ctrl+Alt+U"),
        ("System Maintenance",Keys.Control|Keys.Alt|Keys.M,"Ctrl+Alt+M")
    };

    internal static readonly (string Action, string Key)[] TabActions =
    {
        ("Next tab", "Ctrl+Tab"),
        ("Previous tab", "Ctrl+Shift+Tab"),
        ("Open tab 1–9", "Ctrl+1 … Ctrl+9")
    };

    internal static bool TrySwitchTab(Control root, Keys keyData)
    {
        TabControl? tabs = Descendants(root)
            .OfType<TabControl>()
            .FirstOrDefault(tab => tab.Visible && tab.Enabled && tab.TabPages.Count > 0);
        if (tabs is null)
            return false;

        if (keyData == (Keys.Control | Keys.Tab))
            return SelectTab(tabs, (tabs.SelectedIndex + 1) % tabs.TabPages.Count);
        if (keyData == (Keys.Control | Keys.Shift | Keys.Tab))
            return SelectTab(tabs, (tabs.SelectedIndex - 1 + tabs.TabPages.Count) % tabs.TabPages.Count);

        Keys keyCode = keyData & Keys.KeyCode;
        Keys modifiers = keyData & Keys.Modifiers;
        if (modifiers == Keys.Control && keyCode >= Keys.D1 && keyCode <= Keys.D9)
        {
            int index = keyCode - Keys.D1;
            return index < tabs.TabPages.Count && SelectTab(tabs, index);
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
