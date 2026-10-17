using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Staff;

internal sealed class StaffEmploymentDialog : ApplicationWindowForm
{
    public event Action? EmploymentChanged;

    internal StaffEmploymentDialog(StaffProfilePage.Selection staff, int? employmentId = null)
    {
        Text = $"Employment — {staff.StaffName} · Staff No. {staff.StaffNumber}";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Size = new Size(AppScale.Scale(820), AppScale.Scale(650));
        MinimumSize = new Size(AppScale.Scale(650), AppScale.Scale(500));
        BackColor = AppTheme.Surface;
        var jobs = new StaffEmploymentPage(() => staff.Id) { Dock = DockStyle.Fill };
        if (employmentId is int id) jobs.EditOnLoad(id);
        jobs.PrepareForEmbedding(keepActions: true);
        jobs.EmploymentChanged += () => EmploymentChanged?.Invoke();
        Controls.Add(jobs);
        FormClosing += (_, e) => {
            if (jobs.IsBusy) { e.Cancel = true; return; }
            if (jobs.HasUnsavedChanges && !ModernMessageDialog.Confirm(this,
                jobs.UnsavedChangesMessage, UiText.Dialogs.UnsavedChanges))
                e.Cancel = true;
        };
    }
}
