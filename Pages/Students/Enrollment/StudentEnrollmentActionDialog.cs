using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Students.Enrollment;

/// <summary>Individual dialogs share lifecycle validation and persistence.</summary>
internal abstract class StudentEnrollmentActionDialog : ApplicationWindowForm
{
    protected StudentEnrollmentActionDialog(int admissionId, string studentName, string operation, bool readmission = false)
    {
        Text = operation;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        Size = new Size(AppScale.Scale(readmission ? 780 : 700), AppScale.Scale(readmission ? 700 : 460));
        MinimumSize = Size;
        var workflow = new StudentTransferReadmissionPage(initialAdmissionId: admissionId,
            initialStudentDisplay: studentName, initialOperation: operation) { Dock = DockStyle.Fill };
        Controls.Add(workflow);
        FormClosing += (_, e) => { if (workflow.IsExecuting) e.Cancel = true; };
        workflow.OperationCompleted += (_, _) => BeginInvoke((MethodInvoker)(() => {
            DialogResult = DialogResult.OK;
            Close();
        }));
    }

    internal static StudentEnrollmentActionDialog Create(string operation, int admissionId, string studentName) => operation switch {
        "Transfer Section" => new TransferSectionDialog(admissionId, studentName),
        "Transfer Class" => new TransferClassDialog(admissionId, studentName),
        "Transfer Campus" => new TransferCampusDialog(admissionId, studentName),
        "Re-Admit" => new StudentReadmissionDialog(admissionId, studentName),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };
}

internal sealed class TransferSectionDialog(int admissionId, string studentName)
    : StudentEnrollmentActionDialog(admissionId, studentName, "Transfer Section");

internal sealed class TransferClassDialog(int admissionId, string studentName)
    : StudentEnrollmentActionDialog(admissionId, studentName, "Transfer Class");

internal sealed class TransferCampusDialog(int admissionId, string studentName)
    : StudentEnrollmentActionDialog(admissionId, studentName, "Transfer Campus");

internal sealed class StudentReadmissionDialog(int admissionId, string studentName)
    : StudentEnrollmentActionDialog(admissionId, studentName, "Re-Admit", readmission: true);
