using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Students.Enrollment;

internal sealed class ConfirmReturnAdmissionDialog : ApplicationWindowForm
{
    internal ConfirmReturnAdmissionDialog(int admissionId):this(admissionId,true) { }
    internal ConfirmReturnAdmissionDialog(int admissionId,bool initialize)
    {
        Text = "Confirm Return";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = true;
        ClientSize = new Size(AppScale.Scale(960), AppScale.Scale(720));
        MinimumSize = new Size(AppScale.Scale(820), AppScale.Scale(640));
        var workflow = new ConfirmReturnAdmissionPage(null,admissionId,true,initialize) { Dock = DockStyle.Fill };
        Controls.Add(workflow);
        bool completed=false;
        ModalPageCloseGuard.Attach(this,workflow,()=>completed);
        workflow.ReturnConfirmed += (_, _) => {completed=true;BeginInvoke((MethodInvoker)(() => { DialogResult = DialogResult.OK; Close(); }));};
    }
}
