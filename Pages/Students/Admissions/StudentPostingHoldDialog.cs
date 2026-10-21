using System.Text.Json;
using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Pages.Students.Admissions;

/// <summary>
/// Owner decision, 21 October 2026: hold stops posting, not collection or admission.
/// Placement start remains class history. Resume billing and restoration are explicit
/// school decisions; a certificate does not itself prohibit lifting a hold.
/// </summary>
internal sealed class StudentPostingHoldDialog : ApplicationWindowForm
{
    private readonly int admission;
    private readonly bool resume, adoptLeft;
    private readonly Func<string, SqlParameter[], Task<DataTable>> query;
    private readonly Func<Task<string>> scope;
    private readonly string? recoveryPath;
    private readonly ModernDatePicker date = HumanUi.Date(), month = HumanUi.Date();
    private readonly ModernComboBox policy = HumanUi.Combo(), payment = HumanUi.Combo(), placement = HumanUi.Combo();
    private readonly ModernTextBox reason = HumanUi.TextBox(true, 500), impact = HumanUi.TextBox(true, 0);
    private readonly CheckBox credit = new() { Text = "Apply available advance to fees", AutoSize = true };
    private readonly CheckBox annual = new() { Text = "Include this placement's unbilled annual fees", AutoSize = true };
    private readonly DataGridView bills = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = AppTheme.Surface };
    private readonly ModernButton review = new() { Text = "Review Decision", ThemeRole = "Secondary" },
        save = new() { Text = "Save", ThemeRole = "Success", Enabled = false },
        check = new() { Text = "Check Save Outcome", ThemeRole = "Secondary", Visible = false };
    private readonly TableLayoutPanel body = new() { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, Padding = new Padding(16) };
    private DataRow? plan;
    private bool busy, loading = true;
    private int generation;
    private MutationIntent? pending;
    internal bool Changed { get; private set; }
    private MutationKind Kind => resume ? MutationKind.StudentResume : MutationKind.StudentHold;

    internal StudentPostingHoldDialog(int admission, string student, bool resume, bool adoptLeft = false,
        Func<string, SqlParameter[], Task<DataTable>>? query = null, Func<Task<string>>? scope = null, string? recoveryPath = null)
    {
        this.admission = admission; this.resume = resume; this.adoptLeft = adoptLeft;
        this.query = query ?? ((procedure, args) => DatabaseHelper.QueryUserAsync(procedure, args));
        this.scope = scope ?? RecoverableMutationWriter.GetScopeAsync; this.recoveryPath = recoveryPath;
        Text = resume ? "Lift Hold / Resume" : adoptLeft ? "Review Left Record as Temporary Hold" : "Put on Hold";
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        ClientSize = new Size(AppScale.Scale(940), AppScale.Scale(780)); MinimumSize = new Size(780, 660);
        save.Text = resume ? "Save & Resume" : "Save Hold";
        impact.ReadOnly = true; impact.Dock = DockStyle.Fill; impact.ScrollBars = ScrollBars.Vertical;
        month.Format = DateTimePickerFormat.Custom; month.CustomFormat = "MMMM yyyy";
        month.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        policy.Items.AddRange(new object[] { "Use the school cutoff", "Keep all issued monthly bills", "Waive from a selected month" });
        policy.SelectedIndex = 0;
        payment.Items.AddRange(new object[] { "Keep released payment as advance", "Record the released payment as refunded" });
        payment.SelectedIndex = 0;
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Add(new Label { Text = student, AutoSize = true, Font = AppTheme.BodyBoldFont }, SizeType.AutoSize);
        Add(new Label { AutoSize = true, MaximumSize = new Size(AppScale.Scale(880), 0), Text = resume
            ? "Choose the return date and first billing month. Tick only the waived fees to restore. Existing refunds remain recorded."
            : adoptLeft
                ? "Only this reviewed Left record will become a temporary posting hold. Its identity, certificate and financial history remain."
                : "Stop new fee posting until you lift the hold. Existing dues can still be collected; admission and placement start dates stay recorded." }, SizeType.AutoSize);
        if (resume)
        {
            var dates = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty };
            dates.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); dates.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            dates.Controls.Add(HumanUi.Field("Return / resume date", date, true), 0, 0);
            dates.Controls.Add(HumanUi.Field("First month to charge", month, true), 1, 0);
            Add(dates, SizeType.Absolute, AppMetrics.FieldRowHeight);
        }
        else Add(HumanUi.Field("Hold effective date", date, true), SizeType.Absolute, AppMetrics.FieldRowHeight);
        Add(HumanUi.Field("Reason", reason, true), SizeType.Absolute, AppMetrics.FieldRowHeight * 1.4f);
        if (resume)
        {
            Add(HumanUi.Field("Placement to resume", placement, true), SizeType.Absolute, AppMetrics.FieldRowHeight);
            placement.MaximumSize = Size.Empty;
            var choices = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty };
            choices.Controls.Add(credit); choices.Controls.Add(annual); Add(choices, SizeType.AutoSize);
            Add(new Label { Text = "Waived fees — tick to restore the charge (no payment is restored)", AutoSize = true }, SizeType.AutoSize);
            bills.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Restore", HeaderText = "Restore?", FillWeight = 16 });
            bills.Columns.Add(new DataGridViewTextBoxColumn { Name = "Fee", HeaderText = "Fee", ReadOnly = true, FillWeight = 40 });
            bills.Columns.Add(new DataGridViewTextBoxColumn { Name = "Month", HeaderText = "Month", ReadOnly = true, FillWeight = 22 });
            bills.Columns.Add(new DataGridViewTextBoxColumn { Name = "Amount", HeaderText = "Amount (Rs.)", ReadOnly = true, FillWeight = 22 });
            bills.CurrentCellDirtyStateChanged += (_, _) => { if (bills.IsCurrentCellDirty) bills.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            bills.CellValueChanged += (_, _) => Reset();
            Add(bills, SizeType.Absolute, AppScale.Scale(136));
        }
        else
        {
            Add(HumanUi.Field("Already issued monthly fees", policy), SizeType.Absolute, AppMetrics.FieldRowHeight);
            Add(HumanUi.Field("First month to waive", month), SizeType.Absolute, AppMetrics.FieldRowHeight);
            Add(HumanUi.Field("If the waived fee has already been paid", payment), SizeType.Absolute, AppMetrics.FieldRowHeight);
            month.Enabled = false;
            policy.SelectedIndexChanged += (_, _) => month.Enabled = policy.SelectedIndex == 2;
        }
        Add(impact, SizeType.Percent, 100);
        var close = new ModernButton { Text = "Close", DialogResult = DialogResult.Cancel, ThemeRole = "Secondary" };
        CancelButton = close; Add(HumanUi.ButtonRow(review, save, check, close), SizeType.AutoSize); Controls.Add(body);
        body.AutoScrollMinSize = new Size(0, AppScale.Scale(640));
        body.SizeChanged += (_, _) => {
            foreach (var label in body.Controls.OfType<Label>())
                label.MaximumSize = new Size(Math.Max(1, body.ClientSize.Width - body.Padding.Horizontal - label.Margin.Horizontal), 0);
        };
        FinanceHelp.Attach(this, month, resume
            ? "Choose when monthly billing resumes. Placement start is unchanged."
            : "This chooses which issued monthly fees are waived.", resume
            ? "Held in August, back in October: choose August to charge the absence, or October to leave the missing August–September fees uncharged. Tick earlier waived bills separately if you want them restored."
            : "Waiving an unpaid Rs. 400 bill removes Rs. 400 due. If it was paid, review the advance or refund choice before saving.");
        FinanceHelp.Attach(this, credit, "Use the student's existing advance; this receives no new cash.",
            "Rs. 500 due and Rs. 200 advance: choosing this settles Rs. 200 and leaves Rs. 300 due.");
        FinanceHelp.Attach(this, annual, "Include only annual fees that have never been issued for the selected placement. Previously reversed annual fees require the existing correction review.",
            "A return in a new academic year may need that placement's annual fee. Tick this to include it in the review. An annual fee already issued is not charged twice.");
        FinanceHelp.Attach(this, payment, FinanceHelp.Topic.PaidReduction);
        FinanceHelp.Attach(this, placement, "Use the same placement when applicable. For a new academic year, prepare its placement through the existing promotion or placement workflow, then select it here.",
            "An October return in the same class keeps its recorded placement start. A return in a later academic year uses that year's prepared placement.");
        foreach (var control in new Control[] { date, month, policy, payment, placement, credit, annual, reason })
        {
            if (control is ModernDatePicker picker) picker.ValueChanged += (_, _) => Reset();
            else if (control is ModernComboBox combo) combo.SelectedIndexChanged += (_, _) => Reset();
            else if (control is CheckBox box) box.CheckedChanged += (_, _) => Reset();
            else control.TextChanged += (_, _) => Reset();
        }
        review.Click += async (_, _) => await ReviewAsync(); save.Click += async (_, _) => await SaveAsync();
        check.Click += async (_, _) => await CheckAsync();
        FormClosing += (_, e) => { if (busy && !Changed) e.Cancel = true; };
        review.Enabled = false;
        Shown += async (_, _) => await LoadAsync();
    }

    private void Add(Control control, SizeType size, float height = 0)
    {
        int row = body.RowStyles.Count; body.RowCount = row + 1;
        body.RowStyles.Add(new RowStyle(size, height)); control.Dock = DockStyle.Fill; body.Controls.Add(control, 0, row);
    }
    private void Reset() { generation++; plan = null; save.Enabled = false; }
    private SqlParameter[] Args(bool preview, Guid request) => new[] {
        DatabaseHelper.Int("@AdmissionId", admission), DatabaseHelper.P("@Action", resume ? "Resume" : "Hold"),
        DatabaseHelper.Date("@EffectiveDate", date.Value.Date), DatabaseHelper.P("@Reason", reason.Text.Trim()),
        DatabaseHelper.P("@RequestId", request), DatabaseHelper.Bit("@Preview", preview),
        DatabaseHelper.P("@ExpectedSnapshot", preview ? null : Convert.ToString(plan?["Snapshot"])),
        DatabaseHelper.P("@MonthlyFeePolicy", policy.SelectedIndex switch { 1 => "KeepIssued", 2 => "FromMonth", _ => "SchoolCutoff" }),
        DatabaseHelper.P("@WaiveFromMonth", !resume && policy.SelectedIndex == 2 ? new DateTime(month.Value.Year, month.Value.Month, 1) : null),
        DatabaseHelper.P("@PaidReductionAction", payment.SelectedIndex == 1 ? "Refund" : "Credit"),
        DatabaseHelper.P("@BillingFromMonth", resume ? new DateTime(month.Value.Year, month.Value.Month, 1) : null),
        DatabaseHelper.P("@RestoreBills", JsonSerializer.Serialize(bills.Rows.Cast<DataGridViewRow>().Where(r => Equals(r.Cells["Restore"].Value, true)).Select(r => (long)r.Tag!).OrderBy(x => x))),
        DatabaseHelper.Bit("@ApplyAvailableCredit", resume && credit.Checked),
        DatabaseHelper.P("@TargetEnrollmentId", resume && placement.SelectedValue is int id ? id : null), DatabaseHelper.Bit("@AdoptLeft", adoptLeft),
        DatabaseHelper.Bit("@ChargeAnnualFees", resume && annual.Checked) };
    private async Task LoadAsync()
    {
        busy = true; body.Enabled = false;
        try
        {
            var context = await query("GetStudentPostingHoldContext", new[] { DatabaseHelper.Int("@AdmissionId", admission) });
            var targets = context.Clone();
            foreach (DataRow row in context.Rows)
                if (Convert.ToString(row["EnrollmentStatus"]) is "Active" or "Left") targets.ImportRow(row);
            placement.DisplayMember = "Placement"; placement.ValueMember = "EnrollmentId"; placement.DataSource = targets;
            if (resume)
            {
                var waived = await query("GetStudentPostingHoldBills", new[] { DatabaseHelper.Int("@AdmissionId", admission) });
                foreach (DataRow row in waived.Rows)
                {
                    int index = bills.Rows.Add(false, row["FeeTypeName"], Convert.ToDateTime(row["BillingMonth"]).ToString("MMM yyyy"), Money(Convert.ToDecimal(row["RestoreAmount"])));
                    bills.Rows[index].Tag = Convert.ToInt64(row["BillingChargeId"]);
                    bills.Rows[index].Cells["Restore"].ReadOnly = !Convert.ToBoolean(row["CanRestore"]);
                    if (!Convert.ToBoolean(row["CanRestore"])) bills.Rows[index].Cells["Restore"].ToolTipText = "This fee has changed since the hold. Review it in the existing fee-correction screen.";
                }
            }
            pending = MutationRecoveryJournal.ReadPending(MutationKind.StudentHold, JournalPath(MutationKind.StudentHold))
                ?? MutationRecoveryJournal.ReadPending(MutationKind.StudentResume, JournalPath(MutationKind.StudentResume));
            if (pending is not null) impact.Text = $"A previous decision needs checking: {pending.Summary}. Use Check Save Outcome before saving again.";
            else impact.Text = "Choose the school's decision, then review the amounts before saving.";
        }
        catch (Exception ex) { AppExceptionHandler.LogNonFatal(ex, "Loading student posting hold context"); impact.Text = DbMessage.UserMessage(ex); }
        finally { busy = false; loading = false; body.Enabled = true; UpdatePending(); }
    }
    private void UpdatePending()
    {
        check.Visible = pending is not null; review.Enabled = !loading && pending is null;
        if (pending is not null) save.Enabled = false;
    }
    private string? JournalPath(MutationKind kind) => recoveryPath is null ? null : recoveryPath + "." + kind;
    private static string Money(decimal value) => value.ToString(WholeRupees.IsWhole(value) ? "N0" : "N2");
    private async Task ReviewAsync()
    {
        if (busy || loading || pending is not null) return;
        Reset(); int reviewed = generation; busy = true; body.Enabled = false;
        try
        {
            var data = await query("SaveStudentPostingDecision", Args(true, Guid.NewGuid()));
            if (reviewed != generation || IsDisposed) return;
            if (data.Rows.Count != 1) throw new InvalidDataException("The review could not be verified. Reload the student.");
            plan = data.Rows[0];
            string Amount(string name) => Money(Convert.ToDecimal(plan[name]));
            impact.Text = $"Current outstanding: Rs. {Amount("Outstanding")}\r\n" + (resume
                ? $"Waived fees selected for restoration: Rs. {Amount("Restored")}\r\nNew fees to charge: Rs. {Amount("NewCharges")}\r\nAvailable advance: Rs. {Amount("AvailableCredit")}\r\nAdvance to apply: Rs. {Amount("CreditToApply")}\r\n"
                : $"Unpaid fees to waive: Rs. {Amount("Waived")}\r\nPaid amount released as {(payment.SelectedIndex == 1 ? "recorded refund" : "advance")}: Rs. {Amount("ReleasedPaid")}\r\n") +
                $"Outstanding after saving: Rs. {Amount("ResultingOutstanding")}\r\n\r\n" +
                (resume ? "No new admission number. Original receipts and recorded refunds remain.\r\n" : "New fee posting stops. Existing dues remain collectible.\r\n") + FeeLines(plan, "ChangedFees", resume ? "Restore" : "Waive") + FeeLines(plan, "AddedFees", "Charge");
            save.Enabled = true;
        }
        catch (Exception ex) { Reset(); AppExceptionHandler.LogNonFatal(ex, "Reviewing student posting hold decision"); impact.Text = DbMessage.UserMessage(ex); }
        finally { busy = false; body.Enabled = true; }
    }
    private static string FeeLines(DataRow row, string column, string verb)
    {
        // An empty SQL FOR JSON scalar subquery can arrive as DBNull, rather
        // than "[]". No fee rows is a valid review, including every Hold's
        // empty AddedFees list; it must not prevent reviewing the decision.
        string? fees = row.IsNull(column) ? null : Convert.ToString(row[column]);
        if (string.IsNullOrWhiteSpace(fees)) return string.Empty;
        using var document = JsonDocument.Parse(fees);
        return string.Join("", document.RootElement.EnumerateArray().Select(f => $"{verb}: {f.GetProperty("FeeTypeName").GetString()} / {DateTime.Parse(f.GetProperty("BillingMonth").GetString()!):MMM yyyy} / Rs. {Money(f.GetProperty("Amount").GetDecimal())}\r\n"));
    }
    private async Task SaveAsync()
    {
        if (busy || plan is null || pending is not null) return;
        busy = true; body.Enabled = false;
        try
        {
            string databaseScope = await scope();
            using (var journal = MutationRecoveryJournal.Begin(Kind, Pages.Security.CurrentUser.UserId, databaseScope,
                RecoverableMutationWriter.PayloadHash(Kind, Pages.Security.CurrentUser.UserId, Args(false, Guid.Empty)), $"Student ID {admission}: {(resume ? "resume" : "hold")}", JournalPath(Kind))) pending = journal.Intent;
            var result = await query("SaveStudentPostingDecision", Args(false, pending.Id));
            Complete(result.Rows.Count == 1 ? Convert.ToString(result.Rows[0]["ResultJson"]) : null);
        }
        catch (Exception ex)
        {
            AppExceptionHandler.LogNonFatal(ex, "Saving student posting hold decision");
            Reset(); impact.Text = (pending is null ? "The decision was not submitted.\r\n" : "Saving was not confirmed. Use Check Save Outcome before repeating this decision.\r\n") + DbMessage.UserMessage(ex);
        }
        finally { busy = false; if (!IsDisposed) { body.Enabled = true; UpdatePending(); } }
    }
    private void Complete(string? result)
    {
        using var json = JsonDocument.Parse(result ?? "{}");
        if (json.RootElement.GetProperty("AdmissionId").GetInt32() != admission ||
            pending is null || json.RootElement.GetProperty("RequestId").GetGuid() != pending.Id ||
            json.RootElement.GetProperty("HoldId").GetInt32() <= 0 ||
            json.RootElement.GetProperty("Action").GetString() != (pending.Kind == MutationKind.StudentResume ? "Resume" : "Hold"))
            throw new InvalidDataException("The saved decision identity could not be verified. Check its outcome.");
        MutationRecoveryJournal.ClearVerified(pending!, JournalPath(pending!.Kind)); pending = null;
        Changed = true; StudentSearchCache.Invalidate(); DialogResult = DialogResult.OK; Close();
    }
    private async Task CheckAsync()
    {
        if (busy || pending is null) return;
        busy = true; body.Enabled = false;
        try
        {
            if (pending.UserId != Pages.Security.CurrentUser.UserId || pending.Scope != await scope())
                throw new InvalidOperationException("Reconnect to the original database and operator account to check this decision.");
            var data = await query("GetStudentPostingDecisionOutcome", new[] { DatabaseHelper.Int("@AdmissionId", admission), DatabaseHelper.P("@RequestId", pending.Id) });
            if (data.Rows.Count == 1) Complete(Convert.ToString(data.Rows[0]["ResultJson"]));
            else { MutationRecoveryJournal.ClearVerified(pending, JournalPath(pending.Kind)); pending = null; Reset(); impact.Text = "The database confirms that this decision was not saved. Review again before saving."; }
        }
        catch (Exception ex) { AppExceptionHandler.LogNonFatal(ex, "Checking student posting decision outcome"); impact.Text = DbMessage.UserMessage(ex); }
        finally { busy = false; if (!IsDisposed) { body.Enabled = true; UpdatePending(); } }
    }
}
