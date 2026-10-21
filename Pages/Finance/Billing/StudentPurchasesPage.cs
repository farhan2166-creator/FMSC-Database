using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Finance.Billing;

public sealed class StudentPurchasesPage : V2Page, IBusyPage, IUnsavedChanges
{
    private readonly StudentPicker student = new();
    private readonly LookupBox enrollment = new();
    private readonly AdditionalFeeEntry entry = new();
    private readonly DataGridView recent = HumanUi.Grid(false);
    private readonly ModernButton clear;
    private int selectionVersion;
    private bool binding;
    private int loadedEnrollmentId;
    public bool IsBusy => entry.IsPosting;
    public string BusyMessage => "Wait for this purchase to finish saving before leaving.";
    public bool HasUnsavedChanges => entry.HasDraft;
    public string UnsavedChangesMessage => "This purchase has not been added. Discard it and leave?";

    public StudentPurchasesPage() : base(PageTitles.StudentPurchases,
        "Add purchases and event charges for collection with student fees.")
    {
        clear = AddAction("Clear Student", (_, _) => {
            if (CanChangeStudent()) ClearStudent();
        }, "Secondary");
        var refresh = AddBusyAction(UiText.Actions.Refresh, RefreshAsync, "Secondary");
        RelocateFromActions(clear, refresh);
        var identity = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
        };
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        identity.Controls.Add(HumanUi.Field("Student / Admission Number", student, true), 0, 0);
        identity.Controls.Add(HumanUi.Field("Class / Placement", enrollment, true), 1, 0);
        clear.Margin = Padding.Empty;
        refresh.Margin = new Padding(AppMetrics.SpacingSm, 0, 0, 0);
        var tools = new FlowLayoutPanel {
            AutoSize = false, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Height = AppMetrics.ButtonHeight,
            Width = clear.Width + refresh.Width + AppMetrics.SpacingSm,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        tools.Controls.Add(clear);
        tools.Controls.Add(refresh);
        var studentPanel = HumanUi.Card("Student", "Choose a student and their active placement.",
            accent: AppTheme.ModuleAccent("Finance"), accessory: tools);
        studentPanel.Content.Controls.Add(identity);
        var history = HumanUi.Card("Recent Charges", "Latest purchases, balances and payment status.",
            accent: AppTheme.ModuleAccent("Finance"));
        history.Margin = new Padding(AppMetrics.SpacingMd, 0, 0, 0);
        recent.AutoGenerateColumns = false;
        recent.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        foreach (var (name, caption, weight) in new[] {
            ("ChargeDate", "Date", 120), ("FeeTypeName", "Fee Type", 150), ("Item", "Remarks", 180),
            ("NetAmount", "Net (Rs.)", 85), ("Balance", "Balance (Rs.)", 100),
            ("Status", "Status", 95)
        })
        {
            recent.Columns.Add(new DataGridViewTextBoxColumn {
                Name = name, HeaderText = caption, DataPropertyName = name, FillWeight = weight,
                DefaultCellStyle = new DataGridViewCellStyle {
                    Format = name == "ChargeDate" ? "dd-MMM-yyyy" : "#,0.##",
                    Alignment = name is "NetAmount" or "Balance"
                        ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft
                }
            });
        }
        recent.Columns["ChargeDate"].MinimumWidth =
            TextRenderer.MeasureText("30-Sep-2026", AppTheme.NormalFont).Width + 2 * AppMetrics.SpacingSm;
        HumanUi.ApplyGridStyle(recent, AppTheme.Primary);
        history.Content.Controls.Add(recent);
        var body = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        var entryHost = new Panel {
            Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty
        };
        entry.Dock = DockStyle.Top;
        entry.BackColor = AppTheme.Surface;
        entry.Height = AdditionalFeeEntry.PreferredHeight;
        entryHost.Controls.Add(entry);
        var purchasePanel = HumanUi.Card("Purchase Details", "Enter the item, amount and any discount.",
            accent: AppTheme.ModuleAccent("Finance"));
        entryHost.BackColor = AppTheme.Surface;
        purchasePanel.Content.Controls.Add(entryHost);
        body.Controls.Add(purchasePanel, 0, 0);
        body.Controls.Add(history, 1, 0);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,
            studentPanel.Header.Height + AppMetrics.FieldRowHeight + AppMetrics.CardContentVerticalPadding + AppMetrics.SpacingSm));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.TextHeight(AppTheme.NormalFont) + AppMetrics.SpacingMd));
        studentPanel.Margin = new Padding(0, 0, 0, AppMetrics.SpacingSm);
        root.Controls.Add(studentPanel, 0, 0);
        root.Controls.Add(body, 0, 1);
        StatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(StatusLabel, 0, 2);
        SetContent(root);
        TrackStudentSearchStatus(student);
        student.SelectionChangeGuard = _ => CanChangeStudent();
        student.SelectionCleared += (_, _) => ClearStudent(false);
        student.UserSelectionChanged += async (_, _) => await LoadStudentAsync();
        enrollment.UserSelectionChanged += async (_, _) => {
            if (binding) return;
            if (!CanChangeStudent()) { enrollment.SelectedValue = loadedEnrollmentId; return; }
            await LoadEnrollmentAsync(++selectionVersion);
        };
        entry.PostingChanged += (_, _) => {
            student.Enabled = enrollment.Enabled = clear.Enabled = !entry.IsPosting;
        };
        entry.ChargePosted += async (_, _) => {
            SetStatus("Purchase added. It is available for collection with the student's fees.");
            await LoadRecentAsync(enrollment.SelectedId, selectionVersion);
        };
        FormFlow.OrderTableFields(identity);
        SetStatus("No student selected.");
    }

    private bool CanChangeStudent() => !entry.IsPosting && (!entry.HasDraft
        || Confirm("This purchase has not been added. Discard it and change student?", PageTitles.StudentPurchases));

    private void ClearStudent(bool clearPicker = true)
    {
        if (entry.IsPosting) return;
        ++selectionVersion;
        loadedEnrollmentId = 0;
        binding = true;
        try { enrollment.ClearLookup(); }
        finally { binding = false; }
        entry.ClearStudent();
        recent.DataSource = null;
        if (clearPicker) { student.Clear(); student.Focus(); }
        SetStatus("No student selected.");
    }

    private async Task LoadStudentAsync()
    {
        int admissionId = student.SelectedId;
        ClearStudent(false);
        if (admissionId <= 0) return;
        int version = selectionVersion;
        try
        {
            var all = await DatabaseHelper.QueryUserAsync("GetStudentEnrollments",
                DatabaseHelper.Int("@AdmissionId", admissionId));
            if (IsDisposed || version != selectionVersion) return;
            var active = all.Clone();
            active.Columns.Add("Display", typeof(string));
            foreach (DataRow row in all.Rows)
            {
                if (!string.Equals(Convert.ToString(row["Status"]), "Active", StringComparison.OrdinalIgnoreCase)) continue;
                var added = active.NewRow();
                foreach (DataColumn column in all.Columns) added[column.ColumnName] = row[column];
                added["Display"] = $"{row["ClassName"]} - {row["SectionName"]} ({row["YearName"]})";
                active.Rows.Add(added);
            }
            binding = true;
            try
            {
                enrollment.Bind(active, "EnrollmentId", "Display");
                if (active.Rows.Count > 0) enrollment.SelectedValue = active.Rows[0]["EnrollmentId"];
            }
            finally { binding = false; }
            if (active.Rows.Count == 0) { SetStatus("This student has no active placement."); return; }
            await LoadEnrollmentAsync(version);
            if (!IsDisposed && version == selectionVersion && student.ContainsFocus) entry.FocusFee();
        }
        catch (Exception ex) { if (!IsDisposed && version == selectionVersion) Fail(ex); }
    }

    private async Task LoadEnrollmentAsync(int version)
    {
        int id = enrollment.SelectedId;
        loadedEnrollmentId = id;
        entry.ClearStudent();
        recent.DataSource = null;
        if (id <= 0) return;
        await entry.LoadStudentAsync(id);
        if (IsDisposed || version != selectionVersion) return;
        SetStatus(entry.PostButton.Enabled ? "Ready." : "This placement is not ready for additional charges.");
        await LoadRecentAsync(id, version);
    }

    private async Task LoadRecentAsync(int id, int version)
    {
        if (id <= 0) return;
        try
        {
            int userId = FMSC_SMS.Pages.Security.CurrentUser.UserId;
            var rows = await Task.Run(() => DatabaseHelper.QuerySql("""
                EXEC dbo.RequirePermission @UserId, N'Fees.View';
                SELECT TOP (30) BC.ChargeDate, BC.Remarks AS Item,
                    BC.GrossAmount - BC.DiscountAmount AS NetAmount,
                    dbo.GetFeeChargeBalance(BC.BillingChargeId) AS Balance, BC.Status,
                    FT.FeeTypeName, BC.GrossAmount, BC.DiscountAmount
                FROM dbo.BillingCharges BC JOIN dbo.FeeTypes FT ON FT.FeeTypeId = BC.FeeTypeId
                WHERE BC.EnrollmentId = @EnrollmentId AND BC.ChargeKind = N'Manual'
                ORDER BY BC.ChargeDate DESC, BC.BillingChargeId DESC;
                """, DatabaseHelper.Int("@UserId", userId), DatabaseHelper.Int("@EnrollmentId", id)));
            if (IsDisposed || version != selectionVersion || id != enrollment.SelectedId) return;
            recent.DataSource = rows;
            foreach (DataGridViewRow row in recent.Rows)
                if (row.DataBoundItem is DataRowView item)
                    row.Cells["Item"].ToolTipText = $"{item["FeeTypeName"]}: {item["Item"]}\nAmount: {Convert.ToDecimal(item["GrossAmount"]):#,0.##}\nDiscount: {Convert.ToDecimal(item["DiscountAmount"]):#,0.##}";
        }
        catch (Exception ex)
        {
            if (IsDisposed || version != selectionVersion) return;
            recent.DataSource = null;
            Fail(ex);
        }
    }

    private Task RefreshAsync() => entry.IsPosting || enrollment.SelectedId <= 0
        ? Task.CompletedTask : LoadRecentAsync(enrollment.SelectedId, selectionVersion);
}
