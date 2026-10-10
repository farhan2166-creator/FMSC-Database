using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Administration;
/// <summary>Performs the one-time legacy billing cleanup while preserving each enrollment audit trail.</summary>
public sealed class MigrationBillingCleanupPage : V2Page
{
    private readonly ModernDatePicker goLiveDate = HumanUi.Date();
    private readonly ModernTextBox reason =
        HumanUi.TextBox(false, 500);
    private readonly DataGridView grid =
        HumanUi.Grid(false);
    private readonly Label statusBanner = new() {
        Dock = DockStyle.Top,
        Height = AppScale.Scale(40),
        TextAlign = ContentAlignment.MiddleLeft,
        Font = AppTheme.NormalFont,
        Padding = new Padding(12, 0, 12, 0)
    };
    private readonly Panel settingsFieldsHost;
    private ModernButton? runButton;
    /* The Refresh action is the retry path when the run is NOT in flight, which is why it was left ungated. */
    private ModernButton? refreshButton;
    private bool alreadyCompleted;
    private bool isRunning;
    /* Run Cleanup writes against the status and candidate data LoadStatus reads. V2Page.SetActionButtonsEnabled is private to V2Page, so the button is gated here. */
    private bool statusLoaded;
    private int candidateCount;
    public MigrationBillingCleanupPage()
        : base(
            PageTitles.BillingStartSetup,
            "Set when recurring fees begin for imported students. This setup can only be run once.")
    {
        refreshButton = AddAction(UiText.Actions.Refresh, (_, _) => LoadStatus());
        runButton =
            AddBusyAction("Run Cleanup", RunAsync, "Danger");
        /* nothing has been read yet, so the cleanup action starts closed; the deferred LoadStatus below opens it only on success. */
        RefreshRunButtonEnabled();
        var reviewCard =
            HumanUi.Card(
                "Students Affected",
                "Active students with no billing start date or existing charges. Review the list; this action cannot be undone here.");
        grid.Dock = DockStyle.Fill;
        reviewCard.Content.Controls.Add(
            grid);
        var settingsCard =
            HumanUi.Card(
                "Billing Start Date",
                "Recurring fees begin on this date for the listed students. One-time fees stay the same.");
        settingsFieldsHost =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        ((TableLayoutPanel)settingsFieldsHost).ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        ((TableLayoutPanel)settingsFieldsHost).ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        ((TableLayoutPanel)settingsFieldsHost).RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        ((TableLayoutPanel)settingsFieldsHost).Controls.Add(
            HumanUi.Field("Billing Start Date", goLiveDate, true),
            0,
            0);
        ((TableLayoutPanel)settingsFieldsHost).Controls.Add(
            HumanUi.Field("Reason", reason, true),
            1,
            0);
        settingsCard.Content.Controls.Add(
            settingsFieldsHost);
        settingsCard.Height =
            AppMetrics.FieldCardHeight(1);
        var root =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, statusBanner.Height));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(
            new RowStyle(SizeType.AutoSize));
        root.Controls.Add(statusBanner, 0, 0);
        root.Controls.Add(reviewCard, 0, 1);
        root.Controls.Add(settingsCard, 0, 2);
        SetContent(
            root,
            "Run once after reviewing the students and start date. Use Student Record Checks for later corrections.");
        Defer(LoadStatus);
    }
    /// <summary>The page's own rule for Run Cleanup - a successfully read status, candidate data to act on, and no completed run behind it.</summary>
    private void RefreshRunButtonEnabled()
    {
        if (runButton is null)
            return;
        runButton.Enabled =
            statusLoaded &&
            !alreadyCompleted &&
            candidateCount > 0;
    }
    private void LoadStatus()
    {
        /* A refresh while the destructive run is in flight would repaint the pre-run candidates and re-arm the action over it. */
        if (isRunning)
            return;
        /* close Run Cleanup for the read; only a read that reaches the candidate list re-opens it. */
        statusLoaded = false;
        candidateCount = 0;
        RefreshRunButtonEnabled();
        try
        {
            var status =
                DatabaseHelper.QueryUser(
                    "GetMigrationBillingCleanupStatus");
            if (status.Rows.Count == 0)
            {
                Warn(
                    "School profile has not been set up yet. Complete School Information first.");
                return;
            }
            DataRow row =
                status.Rows[0];
            /* The status itself was read: what Run Cleanup may do with it is the page's own rule, applied through RefreshRunButtonEnabled below. */
            statusLoaded = true;
            alreadyCompleted =
                row["MigrationBillingCleanupCompletedAt"] != DBNull.Value;
            if (alreadyCompleted)
            {
                DateTime completedAt =
                    Convert.ToDateTime(
                        row["MigrationBillingCleanupCompletedAt"]);
                string byUser =
                    row["CompletedByUsername"] == DBNull.Value
                        ? "an unknown user"
                        : Convert.ToString(row["CompletedByUsername"]) ?? "an unknown user";
                int count =
                    row["MigrationBillingCleanupEnrollmentCount"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(row["MigrationBillingCleanupEnrollmentCount"]);
                statusBanner.Text =
                    $"Already run on {completedAt:dd-MMM-yyyy HH:mm} by {byUser} - {count} enrollment(s) updated. This cannot be run again.";
                statusBanner.ForeColor = AppTheme.Text;
                statusBanner.BackColor = AppTheme.SurfaceSecondary;
                grid.DataSource = null;
                goLiveDate.Enabled = false;
                reason.Enabled = false;
                /* The run is already behind us, so the page's own rule keeps the action closed. */
                RefreshRunButtonEnabled();
                SetStatus(
                    "Migration billing cleanup already completed.");
                return;
            }
            candidateCount =
                row["CandidateCount"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(row["CandidateCount"]);
            statusBanner.Text =
                candidateCount == 0
                    ? "No students currently need this - nothing to clean up."
                    : $"{candidateCount} active student(s) currently have no billing start date set. Review the list below, then run once.";
            statusBanner.ForeColor = AppTheme.Text;
            statusBanner.BackColor = AppTheme.Surface;
            goLiveDate.Enabled = true;
            reason.Enabled = true;
            /* The candidate list is the data Run Cleanup acts on, so the page's own rule needs a preview that actually loaded. */
            if (!LoadPreview())
                statusLoaded = false;
            RefreshRunButtonEnabled();
            SetStatus(
                "Ready.");
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    private bool LoadPreview()
    {
        try
        {
            grid.DataSource =
                DatabaseHelper.QueryUser(
                    "PreviewMigrationBillingCleanup");
            HumanUi.ApplyGridStyle(
                grid,
                AppTheme.Primary);
            return true;
        }
        catch (Exception ex)
        {
            Fail(ex);
            return false;
        }
    }
    private async Task RunAsync()
    {
        if (isRunning || alreadyCompleted)
            return;
        if (!ValidateRequiredInputs())
            return;
        if (grid.Rows.Count == 0)
        {
            Warn(
                "There is nothing to run - no students currently need this.");
            return;
        }
        if (string.IsNullOrWhiteSpace(reason.Text))
        {
            Warn(
                "Enter a reason for setting the billing start date.");
            return;
        }
        /* The count the operator agreed to, captured before the run, so the result can be reported against it instead of as a bare figure. */
        int previewedCount = grid.Rows.Count;
        if (!Confirm(
            $"This will set the billing start date to {goLiveDate.Value:dd-MMM-yyyy} for {previewedCount} student(s), and cannot be run again afterward. Continue?",
            "Run Billing Start Setup",
            defaultYes: false))
        {
            return;
        }
        DateTime effectiveGoLiveDate = goLiveDate.Value;
        string reasonText = reason.Text.Trim();
        /* The page owns both actions until the run settles. */
        bool refreshWasEnabled =
            refreshButton?.Enabled ?? false;
        if (refreshButton is not null)
            refreshButton.Enabled = false;
        if (runButton is not null)
            runButton.Enabled = false;
        isRunning = true;
        SetStatus(
            "Setting billing start dates…");
        try
        {
            var result = await Task.Run(() =>
                DatabaseHelper.QueryUser(
                    "RunMigrationBillingCleanup",
                    DatabaseHelper.Date("@GoLiveDate", effectiveGoLiveDate),
                    DatabaseHelper.P("@Reason", reasonText)));
            /* The operator may have left this page while the cleanup ran. */
            if (IsDisposed)
                return;
            int processed =
                result.Rows.Count > 0 && result.Rows[0]["EnrollmentsProcessed"] != DBNull.Value
                    ? Convert.ToInt32(result.Rows[0]["EnrollmentsProcessed"])
                    : 0;
            /* report the server's own count against what was listed, and do not present a shortfall as a full success. */
            Info(
                "Billing start setup finished.\n\n" +
                $"Students listed: {previewedCount:N0}\n" +
                $"Enrollment records updated: {processed:N0}\n\n" +
                (processed < previewedCount
                    ? "The student and enrollment totals differ. Check the billing start dates " +
                      "in Student Record Checks before continuing.\n\n"
                    : "") +
                "This page is now locked and cannot be run again.");
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                Fail(ex);
        }
        finally
        {
            isRunning = false;
            /* The page may be gone; only the flag above still means anything. */
            if (!IsDisposed)
            {
                if (refreshButton is not null && !refreshButton.IsDisposed)
                    refreshButton.Enabled = refreshWasEnabled;
                RefreshRunButtonEnabled();
                SetStatus(string.Empty);
            }
        }
        /* reloaded here, AFTER isRunning was cleared, so the page ends the run showing the state the server is actually in rather than the state it was in when the run… */
        if (IsDisposed)
            return;
        LoadStatus();
    }
}
