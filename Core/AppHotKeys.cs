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
        new("Save", "Ctrl+S", Keys.Control | Keys.S, "Save changes, including Add Fee / Add Charge, in the active editor.", "Save"),
        new("Print", "Ctrl+P", Keys.Control | Keys.P, "Print or preview the current record, report or selected outstanding bills.", "Print", "Preview Selected Bills"),
        new("New record", "Ctrl+N", Keys.Control | Keys.N, "Start a new record using the page’s New action.", "New", "Clear / New"),
        new("Find / Search", "Ctrl+F", Keys.Control | Keys.F, "Focus the page search, or run its Find action.", "Find", "Search"),
        new("Export", "Alt+E", Keys.Alt | Keys.E, "Export the current records or report.", "Export"),
        new("Refresh / Reload", "Ctrl+R", Keys.Control | Keys.R, "Reload current data and refresh lookup caches.", "Refresh", "Reload"),
        new("Load", "Ctrl+L", Keys.Control | Keys.L, "Load data using the current selection.", "Load"),
        new("View", "Ctrl+O", Keys.Control | Keys.O, "Open the selected record or view.", "View", "Open"),
        new("Add item", "Alt+A", Keys.Alt | Keys.A, "Add a draft item or open an Add dialog, including Additional Charge; use Ctrl+S to save it.", "Add", "Additional Charge"),
        new("Edit", "Ctrl+E", Keys.Control | Keys.E, "Edit the selected record.", "Edit"),
        new("Clear", "Alt+C", Keys.Alt | Keys.C, "Reset the current editor using its Clear action.", "Clear"),
        new("Run", "Alt+R", Keys.Alt | Keys.R, "Run the current operation.", "Run"),
        new("Generate", "Ctrl+G", Keys.Control | Keys.G, "Generate results from the current options.", "Generate"),
        new("Scan", "Alt+S", Keys.Alt | Keys.S, "Scan using the page’s Scan action.", "Scan"),
        new("Complete workflow", "Ctrl+Enter", Keys.Control | Keys.Enter, "Apply, confirm, collect or process; normal validation still applies.",
            "Apply", "Confirm", "Execute", "Post", "Process", "Queue", "Collect", "Mark Collected"),
        new("Delete / Remove", "Alt+Delete", Keys.Alt | Keys.Delete, "Delete the selected item; normal confirmation still applies.", "Delete", "Remove"),
        new("Save & New", "Alt+W", Keys.Alt | Keys.W, "Save the current entry and start the next one using the page action.", "Save & New", "Save && New"),
        new("Next student", "Alt+N", Keys.Alt | Keys.N, "Use Next Student; normal draft checks still apply.", "Next Student"),
        new("Use full amount", "Alt+U", Keys.Alt | Keys.U, "Fill the payment amount from the current balance, discount and advance; does not collect payment.", "Use Full Amount"),
        new("Payment history", "Alt+H", Keys.Alt | Keys.H, "Open recent payments for the current student.", "Recent Payments", "Payment History"),
        new("Print receipt", "Alt+P", Keys.Alt | Keys.P, "Print the available receipt. In report previews, Alt+P opens page setup.", "Print Receipt", "Reprint Receipt"),
        new("Print student bill", "Ctrl+B", Keys.Control | Keys.B, "Print the selected student's bill.", "Print Student Bill"),
        new("Print family bill", "Alt+B", Keys.Alt | Keys.B, "Print the family bill for the selected children.", "Print Family Bill", "Print Selected Bill"),
        new("Advance balance", "Alt+V", Keys.Alt | Keys.V, "Open the student's advance balance.", "Advance Balance"),
        new("Student fees menu", "Alt+F", Keys.Alt | Keys.F, "Open Student Fees for account, future-fee and related actions.", "Student Fees"),
        new("Review fee terms", "Alt+T", Keys.Alt | Keys.T, "Review the current student's fee terms.", "Review Fee Terms"),
        new("Review admission", "Alt+D", Keys.Alt | Keys.D, "Review the selected admission or open Admissions for the current student.", "Review Admission", "Open Admissions"),
        new("Pending registrations", "Alt+G", Keys.Alt | Keys.G, "Open pending registrations.", "Pending Registrations"),
        new("Manage photo", "Alt+M", Keys.Alt | Keys.M, "Open photo management for the current student.", "Manage Photo"),
        new("Issue certificate again", "Alt+I", Keys.Alt | Keys.I, "Start Issue Again for the selected collected certificate; normal checks still apply.", "Issue Again"),
        new("Report columns", "Alt+O", Keys.Alt | Keys.O, "Choose the report's visible columns and their order.", "Columns"),
        new("Rename saved view", "Ctrl+M", Keys.Control | Keys.M, "Rename the selected saved report view.", "Rename View"),
        new("Reset report filters", "Ctrl+T", Keys.Control | Keys.T, "Use Reset Filters; keep Alt+C for the separate Clear Filters action.", "Reset Filters"),
        new("Check save outcome", "Alt+K", Keys.Alt | Keys.K, "Check an uncertain save using the existing recovery action.",
            "Check Save Outcome", "Check Saved Admission", "Check Saved Changes", "Check Previous Save"),
        new("Check registration save", "Ctrl+J", Keys.Control | Keys.J, "Check a pending registration save, separately from admission recovery.", "Check Registration Save"),
        new("Admit sibling", "Alt+L", Keys.Alt | Keys.L, "Open sibling admission; leave Alt+A available for adding a fee.", "Admit Sibling"),
        new("Class / section A4 report", "Alt+Y", Keys.Alt | Keys.Y, "Open the separate class summary or class/section outstanding A4 report. Ctrl+P retains the current report's print action.",
            "Class Summary A4", "Class / Section A4")
    };

    internal const string QuickNavigationAction = "Find records";
    internal const string QuickNavigationKey = "Ctrl+K";
    internal static readonly (string Caption,Keys Keys,string Hint)[] DashboardNavigation = {
        ("Dashboard",Keys.Alt|Keys.D1,"Alt+1"),
        ("Daily Work",Keys.Alt|Keys.D2,"Alt+2"),
        ("Fees",Keys.Alt|Keys.D3,"Alt+3"),
        ("Students",Keys.Alt|Keys.D4,"Alt+4"),
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
