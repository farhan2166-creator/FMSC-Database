using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Administration;
public sealed class AuditLogsPage : V2Page
{
    /* Retains full audit values for 6 months; 0 keeps audit records indefinitely. */
    private const int AuditFullDetailMonths = 6;
    private const int AuditRetentionMonths = 0;
    /* Caps the audit trail at 10,000 rows. */
    private const int AuditMaxRows = 10000;
    readonly LookupBox targetUser = new();
    readonly LookupBox table = new();
    readonly ModernDatePicker from = HumanUi.Date();
    readonly ModernDatePicker to = HumanUi.Date();
    FormValidator? rangeValidator;
    /// <summary>Searches audit table, record, action, and before/after values by keyword.</summary>
    readonly ModernTextBox search = HumanUi.TextBox(false, 200);
    public AuditLogsPage()
        : base("Audit Logs", "Review critical changes to fees, access, student lifecycle and certificates.")
    {
        from.Value = DateTime.Today.AddDays(-30);
        to.Value = DateTime.Today;
        from.ShowCheckBox = to.ShowCheckBox = true;
        from.Checked = to.Checked = false;
        WorkspaceState.Remember(targetUser, "Audit:User");
        WorkspaceState.Remember(table, "Audit:Table");
        WorkspaceState.Remember(from, "Audit:From");
        WorkspaceState.Remember(to, "Audit:To");
        WorkspaceState.Remember(search, "Audit:Search");
        var runAudit = AddBusyAction("Run Audit", RunAsync, "Primary");
        var clear = AddAction("Clear", (_, _) => ClearForm(), "Secondary");
        RelocateFromActions(runAudit, clear);
        Defer(async () => {
            try
            {
                var usersTask = DatabaseHelper.QueryUserAsync("GetUsers");
                var tablesTask = DatabaseHelper.QueryUserAsync("GetAuditTableNames");
                await Task.WhenAll(usersTask, tablesTask);
                if (IsDisposed) return;
                targetUser.Bind(await usersTask, "UserId", "Username", "All");
                table.Bind(await tablesTask, "TableName", "TableName", "All");
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
            /* Housekeeping after the filters are ready, so a slow prune never delays the screen. */
            await PruneAuditTrailAsync();
        });
        var filters = HumanUi.Card("Audit Filters", "Find a change by user, table and date - or type a keyword: it is matched against the table, the record, the action and the values recorded with it (a name, an amount, a fee month). The values themselves are kept for 6 months, so older changes are found by table, record or action.");
        var fields = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Padding = new Padding(2, 4, 2, 0)
        };
        for (int i = 0; i < 4; i++)
        {
            fields.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 25f));
        }
        fields.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        fields.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        fields.Controls.Add(
            HumanUi.Field("Target User", targetUser), 0, 0);
        fields.Controls.Add(
            HumanUi.Field("Table", table), 1, 0);
        fields.Controls.Add(
            HumanUi.Field("From Date (optional)", from), 2, 0);
        fields.Controls.Add(
            HumanUi.Field("To Date (optional)", to), 3, 0);
        /* Across the full width, because a keyword is the one filter that needs room - a name, a receipt or an amount reads badly in a quarter-width box. */
        var searchField =
            HumanUi.Field("Search (keyword)", search);
        fields.Controls.Add(searchField, 0, 1);
        fields.SetColumnSpan(searchField, 4);
        filters.Content.Controls.Add(fields);
        var buttonRow = HumanUi.ButtonRow(runAudit, clear);
        buttonRow.Dock = DockStyle.Bottom;
        filters.Content.Controls.Add(buttonRow);
        var records = HumanUi.Card("Audit History", "Changed values for critical events. Older entries may contain full snapshots.");
        records.Content.Controls.Add(Grid);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(2)
                + AppMetrics.ButtonRowHeight));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(filters, 0, 0);
        root.Controls.Add(records, 0, 1);
        SetContent(root);
        ClearForm();
    }
    /* invalidates an in-flight audit query. Run Audit is not disabled while it runs, and nothing re-checked the filters it was started with. */
    private int auditRunVersion;
    /* The version counter is bumped only by Run Audit and Clear, so editing a filter while the query ran did NOT invalidate it and the result landed as though it… */
    private string AuditFilterSignature() =>
        $"{targetUser.SelectedId}|{table.SelectedIndex}|{Convert.ToString(table.SelectedValue)}|" +
        $"{from.Checked}:{from.Value.Date:yyyy-MM-dd}|{to.Checked}:{to.Value.Date:yyyy-MM-dd}|{search.Text.Trim()}";
    /// <summary>Two stages, and the destructive one is off: rows older than <see cref="AuditFullDetailMonths"/> lose their before/after images but keep who/what/when.</summary>
    private async Task PruneAuditTrailAsync()
    {
        if (!CurrentUser.HasPermission("System.Administration"))
            return;
        try
        {
            DataTable result =
                await DatabaseHelper.QueryUserAsync(
                    "PruneAuditTrail",
                    DatabaseHelper.Int("@ClearPayloadOlderThanMonths", AuditFullDetailMonths),
                    DatabaseHelper.Int("@DeleteOlderThanMonths", AuditRetentionMonths),
                    DatabaseHelper.Int("@MaxRows", AuditMaxRows));
            if (result.Rows.Count == 0)
                return;
            DataRow row = result.Rows[0];
            int cleared =
                row["PayloadsCleared"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(row["PayloadsCleared"]);
            int deleted =
                row["RowsDeleted"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(row["RowsDeleted"]);
            /* The column only exists once FMSC_FIX_085 is applied, and this read must not be the thing that fails when it is not. */
            int capped =
                result.Columns.Contains("RowsCapped") &&
                row["RowsCapped"] != DBNull.Value
                    ? Convert.ToInt32(row["RowsCapped"])
                    : 0;
            if (cleared == 0 && deleted == 0 && capped == 0)
                return;
            var tidy =
                new List<string>();
            if (capped > 0)
            {
                tidy.Add(
                    $"{capped:N0} old entr" + (capped == 1 ? "y was" : "ies were") +
                    $" dropped to hold the trail at {AuditMaxRows:N0}");
            }
            if (cleared > 0)
            {
                tidy.Add(
                    $"{cleared:N0} entr" + (cleared == 1 ? "y" : "ies") +
                    $" older than {AuditFullDetailMonths} months lost their before/after detail");
            }
            if (deleted > 0)
            {
                tidy.Add(
                    $"{deleted:N0} entr" + (deleted == 1 ? "y" : "ies") +
                    $" older than {AuditRetentionMonths} months were removed");
            }
            SetStatus($"Audit history tidied: {string.Join(", and ", tidy)}.");
        }
        catch (Exception ex)
        {
            /* Reported rather than swallowed: the usual cause is a database that has not had FMSC_FIX_084 applied, and then the trail simply keeps growing. */
            SetStatus($"The audit history could not be tidied: {DbMessage.UserMessage(ex)}", true);
        }
    }
    async Task RunAsync()
    {
        /* A range that runs backwards returns nothing at all, which reads as "there was no activity" rather than "these dates are the wrong way round". */
        /* UX - marked on the To picker instead of a status-line sentence; same rule. */
        if (!FMSC_SMS.Pages.Reports.ReportUi.ValidDateRange(rangeValidator ??= CreateValidator(), from, to))
            return;
        int version = ++auditRunVersion;
        string requestedFilters = AuditFilterSignature();
        try
        {
            string? tableName = null;
            if (table.SelectedIndex > 0 &&
                table.SelectedValue != null)
            {
                tableName =
                    table.SelectedValue
                        .ToString()
                        ?.Trim();
                if (string.IsNullOrWhiteSpace(tableName))
                    tableName = null;
            }
            int targetUserId =
                targetUser.SelectedId > 0
                    ? targetUser.SelectedId
                    : 0;
            DateTime? fromDate = from.Checked ? from.Value.Date : null;
            DateTime? toDate = to.Checked ? to.Value.Date
                    .AddDays(1)
                    .AddTicks(-1) : null;
            var rows =
                await DatabaseHelper.QueryUserAsync(
                    "GetAuditLogs",
                    DatabaseHelper.P(
                        "@TableName",
                        tableName),
                    DatabaseHelper.P(
                        "@RecordId",
                        null),
                    DatabaseHelper.Int(
                        "@TargetUserId",
                        targetUserId),
                    DatabaseHelper.P(
                        "@FromDate",
                        fromDate),
                    DatabaseHelper.P(
                        "@ToDate",
                        toDate),
                    /* Null rather than empty, so the procedure's own NULLIF has nothing to do and "no keyword" is one value rather than two. */
                    DatabaseHelper.P(
                        "@Search",
                        string.IsNullOrWhiteSpace(search.Text)
                            ? null
                            : search.Text.Trim()));
            /* The captured filters must still be the ones on screen, and no Clear may have happened while this ran. */
            if (IsDisposed ||
                version != auditRunVersion)
            {
                return;
            }
            /* The filters moved while this ran, so these rows answer a question the operator is no longer asking. */
            if (!string.Equals(requestedFilters, AuditFilterSignature(), StringComparison.Ordinal))
            {
                Grid.DataSource = null;
                SetStatus("Filters changed while the audit was running - click Run Audit again to see the history for the filters now selected.", true);
                return;
            }
            Bind(rows);
            ConfigureAuditGrid();
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                Fail(ex);
        }
    }
    void ConfigureAuditGrid()
    {
        if (Grid.Columns.Count == 0)
            return;
        Grid.DefaultCellStyle.WrapMode =
            DataGridViewTriState.True;
        Grid.ColumnHeadersDefaultCellStyle.WrapMode =
            DataGridViewTriState.True;
        Grid.AutoSizeRowsMode =
            DataGridViewAutoSizeRowsMode.AllCells;
        Grid.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.None;
        SetWidth("Username", 75);
        SetWidth("ActionDateTime", 180);
        SetWidth("TableName", 110);
        SetWidth("Action", 100);
        SetFill("OldValues", 1.0f);
        SetFill("NewValues", 1.0f);
        if (Grid.Columns.Contains("OldValues"))
        {
            Grid.Columns["OldValues"].MinimumWidth = AppScale.Scale(220);
        }
        if (Grid.Columns.Contains("NewValues"))
        {
            Grid.Columns["NewValues"].MinimumWidth = AppScale.Scale(220);
        }
    }
    void SetWidth(string columnName, int width)
    {
        if (!Grid.Columns.Contains(columnName))
            return;
        DataGridViewColumn column =
            Grid.Columns[columnName];
        if (width <= 0)
        {
            column.Visible = false;
            return;
        }
        column.Visible = true;
        column.AutoSizeMode =
            DataGridViewAutoSizeColumnMode.None;
        column.Width = width;
    }
    void SetFill(string columnName, float weight)
    {
        if (!Grid.Columns.Contains(columnName))
            return;
        DataGridViewColumn column =
            Grid.Columns[columnName];
        column.Visible = true;
        column.AutoSizeMode =
            DataGridViewAutoSizeColumnMode.None;
        column.FillWeight = weight;
    }
    void ClearForm()
    {
        /* A query already in flight belongs to the filters this clear is resetting, so it must not repaint the grid afterwards. */
        auditRunVersion++;
        targetUser.SelectedIndex = -1;
        table.SelectedIndex = -1;
        search.Clear();
        from.Value =
            DateTime.Today.AddDays(-30);
        to.Value =
            DateTime.Today;
        from.Checked = to.Checked = false;
        Grid.DataSource = null;
        SetStatus("Ready to review audit history.");
    }
}
