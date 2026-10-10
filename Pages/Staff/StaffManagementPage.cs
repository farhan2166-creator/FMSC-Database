using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Staff;

public sealed class StaffManagementPage : UserControl, IUnsavedChanges, IBusyPage
{
    private readonly StaffProfilePage staff;
    public bool IsBusy => staff.IsBusy;
    public string BusyMessage => staff.BusyMessage;
    public bool HasUnsavedChanges => staff.HasUnsavedChanges;
    public string UnsavedChangesMessage => staff.UnsavedChangesMessage;

    public StaffManagementPage()
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.PageBackground;
        staff = new StaffProfilePage();
        staff.PrepareForEmbedding(keepActions: true);
        StaffProfilePage.Selection? selected = null;
        var context = new Label {
            Dock = DockStyle.Fill, AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = AppTheme.ButtonFont, ForeColor = AppTheme.MutedText,
            Text = "Save a new staff member or choose one from Staff Records to manage employment."
        };
        var employmentButton = new ModernButton {
            Text = "Employment", AutoSize = true, Dock = DockStyle.Fill,
            ThemeRole = "Secondary", Enabled = false,
            Margin = new Padding(UiDesignSystem.Spacing.Md, 0, 0, 0),
            Padding = new Padding(UiDesignSystem.Spacing.Md, 0, UiDesignSystem.Spacing.Md, 0)
        };
        employmentButton.ApplyTheme();
        staff.StaffSelected += selection => {
            selected = selection;
            context.Text = $"Employment for: {selection.StaffName} · Staff No. {selection.StaffNumber}";
            context.ForeColor = AppTheme.Text;
            employmentButton.Enabled = true;
        };
        employmentButton.Click += (_, _) => {
            if (selected is null || staff.IsBusy) return;
            if (staff.HasUnsavedChanges)
            {
                ModernMessageDialog.ShowWarning(this,
                    "Save or clear your changes to staff details before opening Employment.");
                return;
            }
            using var dialog = new StaffEmploymentDialog(selected);
            dialog.EmploymentChanged += staff.RefreshStaffRecords;
            dialog.ShowDialog(FindForm());
        };
        var toolbar = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = AppTheme.Surface,
            Padding = new Padding(UiDesignSystem.Spacing.Lg, UiDesignSystem.Spacing.Sm,
                UiDesignSystem.Spacing.Lg, UiDesignSystem.Spacing.Sm),
            Margin = Padding.Empty
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.Controls.Add(context, 0, 0);
        toolbar.Controls.Add(employmentButton, 1, 0);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,
            AppMetrics.ButtonRowHeight + UiDesignSystem.Spacing.Sm * 2));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(staff, 0, 1);
        Controls.Add(root);
    }
}
