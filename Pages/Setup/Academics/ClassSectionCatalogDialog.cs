using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Setup.Academics;

internal enum SetupCatalog { Campuses, Classes, Sections }

internal sealed class ClassSectionCatalogDialog : ApplicationWindowForm, IBusyPage
{
    internal MasterPage Editor { get; }
    public bool IsBusy => Editor.IsBusy;
    public string BusyMessage => Editor.BusyMessage;

    internal ClassSectionCatalogDialog(SetupCatalog catalog)
    {
        Text = catalog switch {
            SetupCatalog.Campuses => "Manage Campuses",
            SetupCatalog.Classes => "Manage Classes",
            _ => "Manage Sections"
        };
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        ClientSize = new Size(AppScale.Scale(1100), AppScale.Scale(740));
        MinimumSize = new Size(AppScale.Scale(850), AppScale.Scale(600));
        Editor = catalog switch {
            SetupCatalog.Campuses => new CampusesPage(),
            SetupCatalog.Classes => new ClassesPage(false),
            _ => new SectionsPage(false)
        };
        Editor.PrepareForEmbedding(keepActions: true);
        Editor.HideRecordsChrome();
        Editor.Dock = DockStyle.Fill;
        var close = new ModernButton { Text = UiText.Actions.Close, ThemeRole = "Secondary", AutoSize = true };
        close.Click += (_, _) => Close();
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(Editor, 0, 0);
        var closeRow = HumanUi.ButtonRow(close);
        closeRow.Dock = DockStyle.Fill;
        root.Controls.Add(closeRow, 0, 1);
        Controls.Add(root);
        CancelButton = close;
        FormClosing += (_, e) => {
            if (!IsBusy) return; // Small setup drafts retain their prompt-free policy.
            e.Cancel = true;
            ModernMessageDialog.ShowWarning(this, BusyMessage, "FMSC");
        };
    }
}
