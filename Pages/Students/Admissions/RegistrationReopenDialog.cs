using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Students.Admissions;

internal sealed class RegistrationReopenDialog : ApplicationWindowForm
{
    private readonly ModernComboBox year = HumanUi.Combo();
    private readonly PlacementPicker placement = new();
    private readonly ModernDatePicker joining = new() { Value = DateTime.Today };
    private readonly ModernTextBox reason = HumanUi.TextBox(false, 250);
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Font = AppTheme.NormalFont };
    private readonly ModernButton review = new() { Text = "Review Reopening", AutoSize = true }, apply = new() { Text = "Reopen Registration", AutoSize = true, Enabled = false, ThemeRole = "Primary" };
    private readonly Guid request = Guid.NewGuid();
    private string? snapshot;
    private bool busy, ready, unknown;
    internal RegistrationReopenDialog(int admission, string studentName, bool initialize = true)
    {
        Text = "Reopen Registration — " + studentName; StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(820, 350); MinimumSize = new Size(740, 330);
        MaximizeBox = MinimizeBox = false;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight * 2));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); fields.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        fields.Controls.Add(HumanUi.Field("Academic year", year, true), 0, 0);
        fields.Controls.Add(HumanUi.Field("Campus / Class / Section", placement, true), 1, 0);
        fields.Controls.Add(HumanUi.Field("New joining date", joining, true), 0, 1);
        fields.Controls.Add(HumanUi.Field("Reason", reason, true), 1, 1);
        foreach (Control control in new Control[] { year, placement, joining, reason }) control.MaximumSize = Size.Empty;
        root.Controls.Add(fields, 0, 0); root.Controls.Add(status, 0, 1);
        var close = new ModernButton { Text = "Close", AutoSize = true };
        var actions = HumanUi.ButtonRow(review, apply, close); actions.Dock = DockStyle.Fill;
        root.Controls.Add(actions, 0, 2); Controls.Add(root);
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        close.Click += (_, _) => Close();
        void InvalidateReview() { snapshot = null; apply.Enabled = false; }
        year.SelectedIndexChanged += (_, _) => InvalidateReview(); placement.SelectedIndexChanged += (_, _) => InvalidateReview();
        joining.ValueChanged += (_, _) => InvalidateReview(); reason.TextChanged += (_, _) => InvalidateReview();
        async Task Run(bool preview)
        {
            if (busy || !ready || unknown || !preview && snapshot is null) return;
            if (year.SelectedValue is null || placement.SelectedId <= 0 || string.IsNullOrWhiteSpace(reason.Text) || reason.Text.Trim().Length > 250)
            { status.Text = "Choose the academic year and placement, and enter a reopening reason of up to 250 characters."; return; }
            busy = true; review.Enabled = apply.Enabled = year.Enabled = placement.Enabled = joining.Enabled = reason.Enabled = false;
            bool saved = false, submitted = false;
            try
            {
                // New reopening targets must be active at both review and apply.
                // This is a C# selection rule; historical SQL placements remain valid.
                await ActiveClassSectionPolicy.EnsureActiveAsync(placement.SelectedId);
                submitted = true;
                using var result = await DatabaseHelper.QueryUserAsync("ReopenPendingRegistration",
                    DatabaseHelper.Int("@AdmissionId", admission), DatabaseHelper.Int("@AcademicYearId", Convert.ToInt32(year.SelectedValue)),
                    DatabaseHelper.Int("@ClassSectionId", Convert.ToInt32(placement.SelectedValue)), DatabaseHelper.Date("@JoiningDate", joining.Value.Date),
                    DatabaseHelper.P("@Reason", reason.Text.Trim()), DatabaseHelper.P("@RequestId", request),
                    DatabaseHelper.Bit("@Preview", preview), DatabaseHelper.P("@ExpectedSnapshot", snapshot));
                if (Convert.ToBoolean(result.Rows[0]["Saved"])) { saved = true; }
                else
                {
                    snapshot = Convert.ToString(result.Rows[0]["Snapshot"]);
                    status.Text = $"Existing outstanding: Rs. {Convert.ToDecimal(result.Rows[0]["Outstanding"]):N0}. Reopen creates a new pending placement. The cancelled placement, bills, payments and family links remain recorded. No old fees are restored. Review fees in Admission before confirming.";
                }
            }
            catch (Exception ex)
            {
                snapshot = null;
                if (!preview && submitted) unknown = true;
                status.Text = DbMessage.UserMessage(ex) + (preview || !submitted ? "" : " Close this window and reload the student to check the saved outcome before trying again.");
            }
            finally
            {
                busy = false;
                review.Enabled = year.Enabled = placement.Enabled = joining.Enabled = reason.Enabled = !unknown;
                apply.Enabled = !unknown && snapshot is not null;
            }
            if (saved) { DialogResult = DialogResult.OK; Close(); }
        }
        review.Click += async (_, _) => await Run(true); apply.Click += async (_, _) => await Run(false);
        if (initialize) Shown += async (_, _) => {
            review.Enabled = false; busy = true;
            try
            {
                var years = await DatabaseHelper.QueryUserAsync("GetAcademicYears");
                year.DisplayMember = "YearName"; year.ValueMember = "AcademicYearId"; year.DataSource = years;
                var current = years.AsEnumerable().FirstOrDefault(row => Convert.ToBoolean(row["IsCurrent"]));
                if (current is not null) year.SelectedValue = current["AcademicYearId"];
                var places = await DatabaseHelper.QueryUserAsync("GetClassSections", DatabaseHelper.Int("@CampusId", 0), DatabaseHelper.Int("@ClassId", 0), DatabaseHelper.Int("@SectionId", 0));
                placement.Bind(await ActiveClassSectionPolicy.OnlyActiveAsync(places));
                ready = true;
                status.Text = "Select the new placement and joining date. Review reopening first; old cancelled records and finances remain unchanged.";
            }
            catch (Exception ex) { status.Text = DbMessage.UserMessage(ex); }
            finally { busy = false; review.Enabled = ready; }
        };
    }
}
