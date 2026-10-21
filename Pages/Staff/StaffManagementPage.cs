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
        var overview = new StaffEmploymentOverview();
        staff.AddEmploymentOverview(overview);
        overview.CanOpenEditor = () => {
            if (staff.IsBusy) return false;
            if (!staff.HasUnsavedChanges) return true;
            ModernMessageDialog.ShowWarning(this,
                "Save or clear your changes to staff details before editing employment.");
            return false;
        };
        overview.EmploymentChanged += staff.RefreshStaffRecords;
        var context = new Label {
            Dock = DockStyle.Fill, AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = AppTheme.ButtonFont, ForeColor = AppTheme.MutedText,
            Text = "Save a new staff member or choose one from Staff Records to manage employment."
        };
        staff.StaffSelected += async selection => {
            context.Text = $"Employment for: {selection.StaffName} · Staff No. {selection.StaffNumber}";
            context.ForeColor = AppTheme.Text;
            await overview.SelectStaffAsync(selection);
        };
        staff.StaffSaved += async selection => {
            context.Text = $"Saved staff: {selection.StaffName} · Staff No. {selection.StaffNumber}.";
            context.ForeColor = AppTheme.Text;
            await overview.SelectStaffAsync(selection);
        };
        staff.StaffSelectionCleared += async () => {
            context.Text = "Save a new staff member or choose one from Staff Records to manage employment.";
            context.ForeColor = AppTheme.MutedText;
            await overview.SelectStaffAsync(null);
        };
        var toolbar = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1,
            BackColor = AppTheme.Surface,
            Padding = new Padding(UiDesignSystem.Spacing.Lg, UiDesignSystem.Spacing.Sm,
                UiDesignSystem.Spacing.Lg, UiDesignSystem.Spacing.Sm),
            Margin = Padding.Empty
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        toolbar.Controls.Add(context, 0, 0);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,
            AppMetrics.ButtonRowHeight + UiDesignSystem.Spacing.Sm * 2));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(staff, 0, 1);
        Controls.Add(root);
    }
}
