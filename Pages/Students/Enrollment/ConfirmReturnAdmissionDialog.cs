using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Students.Enrollment;

internal sealed class ConfirmReturnAdmissionDialog : ApplicationWindowForm
{
    internal ConfirmReturnAdmissionDialog(int admissionId)
    {
        Text = "Confirm Return";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false;
        Size = new Size(AppScale.Scale(760), AppScale.Scale(610));
        MinimumSize = Size;
        var workflow = new ConfirmReturnAdmissionPage(preselectAdmissionId: admissionId, compactDialog: true) { Dock = DockStyle.Fill };
        Controls.Add(workflow);
        bool completed=false;
        ModalPageCloseGuard.Attach(this,workflow,()=>completed);
        workflow.ReturnConfirmed += (_, _) => {completed=true;BeginInvoke((MethodInvoker)(() => { DialogResult = DialogResult.OK; Close(); }));};
    }
}
