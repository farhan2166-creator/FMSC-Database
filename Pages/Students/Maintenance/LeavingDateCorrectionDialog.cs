using System.Text.Json;
using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Students.Maintenance;

/// <summary>Coordinates related departure dates using the established lifecycle repair contract.</summary>
internal sealed class LeavingDateCorrectionDialog : ApplicationWindowForm
{
    private readonly ModernDatePicker date = HumanUi.Date();
    private readonly ModernTextBox reason = HumanUi.TextBox(true, 500);
    private readonly Label outcome = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly CheckBox keepFinances = new() { Text = "Keep existing bills and payments at their recorded dates and amounts", AutoSize = true };
    private readonly CheckBox allowLegacy = new() { Text = "Acknowledge existing overlaps or dates outside admission / academic-year boundaries", AutoSize = true };
    private readonly ModernButton review = new() { Text = "Review Correction", ThemeRole = "Secondary" };
    private readonly ModernButton apply = new() { Text = "Apply Correction", ThemeRole = "Warning", Enabled = false };
    private readonly ModernButton cancel = new() { Text = UiText.Actions.Cancel, DialogResult = DialogResult.Cancel };
    private readonly DataGridView records = HumanUi.Grid();
    private bool reviewed;
    private bool busy;
    internal string ResultMessage { get; private set; } = "";

    internal LeavingDateCorrectionDialog(int admissionId, int enrollmentId, DataSet source, DateTime? proposedDate, string initialReason)
    {
        var student = source.Tables[0].Rows[0];
        var enrollments = source.Tables[1];
        var target = enrollments.AsEnumerable().Single(row => Convert.ToInt32(row["EnrollmentId"]) == enrollmentId);
        var linkedCertificates = source.Tables[2].AsEnumerable()
            .Where(row => row["EnrollmentId"] != DBNull.Value && Convert.ToInt32(row["EnrollmentId"]) == enrollmentId).ToArray();
        bool updateWithdrawal = Convert.ToString(student["Status"]) == "Left" && !enrollments.AsEnumerable().Any(row =>
            Convert.ToDateTime(row["EnrollmentDate"]) > Convert.ToDateTime(target["EnrollmentDate"]) ||
            (Convert.ToDateTime(row["EnrollmentDate"]) == Convert.ToDateTime(target["EnrollmentDate"]) && Convert.ToInt32(row["EnrollmentId"]) > enrollmentId));
        bool hasFinancialHistory = enrollments.AsEnumerable().Any(row => Convert.ToInt32(row["Charges"]) > 0);
        Text = "Correct Leaving Date";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(AppScale.Scale(900), AppScale.Scale(660));
        MinimumSize = new Size(AppScale.Scale(720), AppScale.Scale(540));
        date.Value = proposedDate ?? (target["LeavingDate"] is DateTime previous ? previous : DateTime.Today);
        reason.Text = initialReason;
        keepFinances.Visible = hasFinancialHistory;
        var guidance = new Label { Dock = DockStyle.Fill, AutoSize = true,
            Text = $"{student["AdmissionNumber"]} — {student["StudentName"]}\nEnrollment {enrollmentId}: {target["Placement"]} — {target["YearName"]}.\nOne date updates this departure and its linked certificates, plus the withdrawal date when this is the student's latest departure." };
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        fields.Controls.Add(HumanUi.Field("Correct leaving date", date), 0, 0);
        fields.Controls.Add(HumanUi.Field("Reason", reason, true), 1, 0);
        var flags = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        flags.Controls.Add(keepFinances); flags.Controls.Add(allowLegacy);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight * 2));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(guidance, 0, 0); root.Controls.Add(fields, 0, 1); root.Controls.Add(records, 0, 2);
        root.Controls.Add(flags, 0, 3); root.Controls.Add(outcome, 0, 4); root.Controls.Add(HumanUi.ButtonRow(review, apply, cancel), 0, 5);
        Controls.Add(root); CancelButton = cancel;
        void ResetReview()
        {
            reviewed = false; apply.Enabled = false; records.DataSource = null;
            outcome.Text = "Review the related date changes before applying. Bills, payments, status and original history will stay unchanged.";
        }
        void SetBusy(bool value)
        {
            busy = value; date.Enabled = reason.Enabled = review.Enabled = cancel.Enabled = flags.Enabled = !value;
            apply.Enabled = !value && reviewed;
        }
        static string ShowDate(object value) => value is DateTime d ? d.ToString("dd MMM yyyy") : "Not recorded";
        date.ValueChanged += (_, _) => ResetReview(); reason.TextChanged += (_, _) => ResetReview();
        keepFinances.CheckedChanged += (_, _) => ResetReview(); allowLegacy.CheckedChanged += (_, _) => ResetReview();
        review.Click += (_, _) => {
            if (busy) return;
            ResetReview();
            if (string.IsNullOrWhiteSpace(reason.Text)) { outcome.Text = "Enter a reason for this correction."; return; }
            if (date.Value.Date > DateTime.Today || date.Value.Date < Convert.ToDateTime(target["EnrollmentDate"]).Date)
            { outcome.Text = "Leaving date must not be in the future or before placement start date."; return; }
            if (hasFinancialHistory && !keepFinances.Checked)
            { outcome.Text = "This student has financial history. Acknowledge that existing bills and payments will stay unchanged before reviewing."; return; }
            var changes = new DataTable();
            changes.Columns.Add("Record"); changes.Columns.Add("Current Date"); changes.Columns.Add("Corrected Date");
            changes.Rows.Add($"Placement {enrollmentId}", ShowDate(target["LeavingDate"]), date.Value.ToString("dd MMM yyyy"));
            foreach (var certificate in linkedCertificates)
                changes.Rows.Add($"Certificate {certificate["CertificateNumber"]}", ShowDate(certificate["LeaveDate"]), date.Value.ToString("dd MMM yyyy"));
            if (updateWithdrawal)
                changes.Rows.Add("Student withdrawal", ShowDate(student["WithdrawalDate"]), date.Value.ToString("dd MMM yyyy"));
            records.DataSource = changes;
            outcome.Text = "Apply will save these related date changes together. The database will validate every placement before saving.\n" +
                $"Existing bills on this placement: {target["Charges"]}; outstanding: Rs. {Convert.ToDecimal(target["Outstanding"]):0.##}. These amounts will not change.\n" +
                "No bills will be created or cancelled. Certificate creation/issuance dates and original history stay unchanged." +
                (updateWithdrawal ? "" : "\nThe student's current withdrawal date and overall status stay unchanged.");
            reviewed = true; apply.Enabled = true;
        };
        apply.Click += async (_, _) => {
            if (busy || !reviewed) return;
            SetBusy(true);
            try
            {
                var result = await DatabaseHelper.QueryUserAsync("RepairLifecycleDates",
                    DatabaseHelper.Int("@AdmissionId", admissionId), DatabaseHelper.P("@ExpectedSnapshot", Convert.ToString(student["Snapshot"])),
                    DatabaseHelper.P("@Changes", JsonSerializer.Serialize(enrollments.AsEnumerable().Select(row => new {
                        EnrollmentId = Convert.ToInt32(row["EnrollmentId"]), EnrollmentDate = Convert.ToDateTime(row["EnrollmentDate"]).Date,
                        LeavingDate = Convert.ToInt32(row["EnrollmentId"]) == enrollmentId ? date.Value.Date : row["LeavingDate"] is DateTime left ? (DateTime?)left.Date : null,
                        BillingStartDate = row["BillingStartDate"] is DateTime start ? (DateTime?)start.Date : null
                    }))), DatabaseHelper.P("@Reason", reason.Text.Trim()),
                    DatabaseHelper.Bit("@AllowLegacyExceptions", allowLegacy.Checked), DatabaseHelper.Bit("@KeepFinancialHistory", keepFinances.Checked),
                    DatabaseHelper.P("@CertificateChanges", JsonSerializer.Serialize(linkedCertificates.Select(row => new {
                        LeaveRecordId = Convert.ToInt32(row["LeaveRecordId"]), LeaveDate = date.Value.Date
                    }))), DatabaseHelper.Bit("@UpdateWithdrawal", updateWithdrawal),
                    DatabaseHelper.P("@NewWithdrawalDate", updateWithdrawal ? date.Value.Date : (object)DBNull.Value));
                ResultMessage = $"Leaving date correction saved: {result.Rows[0]["EnrollmentsChanged"]} placement(s), {result.Rows[0]["CertificatesUpdated"]} certificate(s). Bills and payments unchanged.";
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex) { reviewed = false; FMSC_SMS.Core.OperationFeedback.Set(outcome,DbMessage.UserMessage(ex) + " Close and reopen the correction if records changed.",true); }
            finally { if (!IsDisposed) SetBusy(false); }
        };
        FormClosing += (_, e) => { if (busy && DialogResult != DialogResult.OK) e.Cancel = true; };
        ResetReview();
    }
}
