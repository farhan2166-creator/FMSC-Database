using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Finance.StudentAccounts;
/// <summary>One enrollment's recurring rates and transport start/stop actions. Purchases are managed in Fee Collection.</summary>
public sealed class StudentFeeAssignmentsPanel : UserControl
{
    private readonly DataGridView grid = HumanUi.Grid(false);
    private DateTime ReferenceMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DataTable assignments = new();
    private readonly ModernButton reviewButton = new() { Text = "Review Pending Fees", ThemeRole = "Primary", Height = AppMetrics.ButtonHeight };
    public bool IsBusy { get; private set; }
    public bool ReviewCompleted { get; private set; }
    public bool NeedsReview { get; private set; }
    /* The fee row menu: the same three fee-level operations the button row below offers, hung off the row itself. */
    private readonly ContextMenuStrip rowMenu = new StyledContextMenu();
    private readonly TextBox hint = new() {
        Dock = DockStyle.Fill,
        AutoSize = false,
        Font = AppTheme.SmallFont,
        ForeColor = AppTheme.MutedText,
        Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
        ScrollBars = ScrollBars.Vertical, BackColor = AppTheme.Surface, TabStop = false
    };
    private readonly ModernButton addButton = new() {
        Text = "Add Fee",
        Width = AppScale.Scale(110),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Success"
    };
    private readonly ModernButton discontinueButton = new() {
        Text = "Stop Transport",
        Width = AppScale.Scale(110),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Warning",
        Enabled = false
    };
    private readonly ModernButton startTransportButton = new() {
        Text = "Start / Restart Transport",
        Width = AppScale.Scale(210),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Success",
        Enabled = false
    };
    private readonly ModernButton issuedBillButton=new(){Text="Review Issued Bill",ThemeRole="Secondary",Enabled=false};
    private readonly ModernButton discountButton = new() {
        Text = "Review Fee Terms",
        Width = AppScale.Scale(160),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Secondary",
        Enabled = false
    };
    private int enrollmentId;
    private int admissionId;
    private bool hasOpenTransport;
    /// <summary>True if any change (add / discontinue / amount change / discount / remove) was actually applied.</summary>
    public bool ChangesMade { get; private set; }
    /// <summary>Raised once per applied change, after <see cref="ChangesMade"/> is set. A host that shows something derived from these fees listens here instead of polling ChangesMade after the fact.</summary>
    public event EventHandler? Changed;
    public StudentFeeAssignmentsPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.Surface;
        Font = AppTheme.NormalFont;
        BuildLayout();
        reviewButton.Click += async (_, _) => await ReviewPendingFeesAsync();
        grid.SelectionChanged += (_, _) => UpdateButtonStates();
        addButton.Click += (_, _) => AddFee();
        discontinueButton.Click += (_, _) => Discontinue();
        startTransportButton.Click += (_, _) => StartTransport();
        discountButton.Click += (_, _) => ChangeDiscount();
        issuedBillButton.Click+=(_,_)=>ReviewIssuedBill();
        ShowNoStudent();
    }
    private void BuildLayout()
    {
        grid.Dock = DockStyle.Fill;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.Columns.Add("FeeTypeName", UiText.Fees.FeeType);
        grid.Columns.Add("StandardAmount", UiText.Fees.Amount);
        // StudentFeeAssignments.DiscountAmount (and SaveStudentFeeDiscount's @DiscountAmount) is a rupee value only - there is no percentage discount mode anywhere in the assignment workflow.
        grid.Columns.Add("DiscountAmount", UiText.Fees.Discount);
        grid.Columns.Add("NetAmount", UiText.Fees.NetAmount);
        grid.Columns.Add("Frequency", UiText.Fees.Frequency);
        grid.Columns.Add("EffectiveFrom", "From Month");
        grid.Columns.Add("EffectiveTo", "Through Month");
        grid.Columns.Add("Status", "Rate Status");
        grid.Columns.Add("PostedThrough", "Last Posted");
        grid.Columns.Add("StudentFeeAssignmentId", "Id");
        grid.Columns["StudentFeeAssignmentId"].Visible = false;
        grid.Columns.Add("FeeTypeId", "FeeTypeId");
        grid.Columns["FeeTypeId"].Visible = false;
        grid.Columns.Add("IsOpen", "IsOpen");
        grid.Columns["IsOpen"].Visible = false;
        grid.Columns.Add("LastBilledMonth", "LastBilledMonth");
        grid.Columns["LastBilledMonth"].Visible = false;
        grid.Columns.Add("IsTransportLinked", "Transport");
        grid.Columns["IsTransportLinked"].Visible = false;
        grid.Columns.Add("ConfiguredOrder", "ConfiguredOrder");
        grid.Columns["ConfiguredOrder"].Visible = false;
        grid.Columns.Add("ConcessionLabel", "Concession");
        grid.Columns["ConcessionLabel"].DisplayIndex = 3;
        foreach (var name in new[] { "StandardAmount", "DiscountAmount", "NetAmount" })
        {
            grid.Columns[name].DefaultCellStyle = new DataGridViewCellStyle {
                Format = "0.##",
                Alignment = DataGridViewContentAlignment.MiddleCenter
            };
            grid.Columns[name].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        grid.Columns["FeeTypeName"].FillWeight = 150;
        grid.Columns["StandardAmount"].FillWeight = 80;
        grid.Columns["DiscountAmount"].FillWeight = 115;
        grid.Columns["NetAmount"].FillWeight = 70;
        grid.Columns["Frequency"].FillWeight = 105;
        grid.Columns["EffectiveFrom"].FillWeight = 110;
        grid.Columns["EffectiveTo"].FillWeight = 160;
        grid.Columns["Status"].FillWeight = 110;
        grid.Columns["PostedThrough"].FillWeight = 110;
        HumanUi.ApplyGridStyle(grid, AppTheme.ModuleAccent("Finance"));
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (column.Visible) column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        }
        var actions = new[] { addButton, discontinueButton, startTransportButton, discountButton, issuedBillButton, reviewButton };
        var buttons = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = actions.Length, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (int index = 0; index < actions.Length; index++) {
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / actions.Length));
            actions[index].Dock = DockStyle.Fill;
            actions[index].MinimumSize = Size.Empty;
            actions[index].AutoSize = false;
            actions[index].Margin = new Padding(AppScale.Scale(3), AppScale.Scale(4), AppScale.Scale(3), AppScale.Scale(4));
            buttons.Controls.Add(actions[index], index, 0);
        }
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.Scale(48)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
        root.Controls.Add(grid, 0, 0);
        root.Controls.Add(hint, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        grid.ContextMenuStrip = rowMenu;
        /* A right-click over a row and the keyboard's Menu key / Shift+F10 on the current row both arrive here. */
        rowMenu.Opening += (_, e) => {
            Point pointer = grid.PointToClient(Cursor.Position);
            var hit = grid.HitTest(pointer.X, pointer.Y);
            int rowIndex = hit.RowIndex >= 0
                ? hit.RowIndex
                : grid.CurrentRow?.Index ?? -1;
            DataGridViewRow? row =
                rowIndex >= 0 && rowIndex < grid.Rows.Count
                    ? grid.Rows[rowIndex]
                    : null;
            e.Cancel = !BuildRowMenu(row);
            if (e.Cancel || row is null)
                return;
            grid.ClearSelection();
            row.Selected = true;
            grid.CurrentCell = row.Cells["FeeTypeName"];
        };
    }
    /// <summary>Points the panel at one enrollment and loads its fees.</summary>
    public void SetStudent(int enrollmentId, int admissionId)
    {
        this.enrollmentId = enrollmentId;
        this.admissionId = admissionId;
        if (enrollmentId <= 0)
        {
            ShowNoStudent();
            return;
        }
        LoadGrid(chooseLatestPostedMonth: true);
    }
    /// <summary>Re-reads the list for the enrollment already set. Used by a host whose tab was hidden while the selection changed underneath it.</summary>
    public void Reload()
    {
        if (enrollmentId <= 0)
        {
            ShowNoStudent();
            return;
        }
        LoadGrid();
    }
    private void ShowNoStudent()
    {
        grid.Rows.Clear();
        addButton.Enabled = false;
        hasOpenTransport=false;
        UpdateButtonStates();
        hint.Text = "Select a student to see the fees assigned to them.";
    }
    private bool enrollmentHasLeft;
    private void LoadGrid(bool chooseLatestPostedMonth = false)
    {
        try
        {
            var placement=DatabaseHelper.QuerySql("""
                EXEC dbo.RequirePermission @UserId,N'Students.View';
                SELECT Status FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId;
                """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@EnrollmentId",enrollmentId));
            enrollmentHasLeft=placement.Rows.Count>0 && Convert.ToString(placement.Rows[0]["Status"]) is not ("Active" or "Pending");
            var dt = DatabaseHelper.QueryUser(
                "GetStudentFeeAssignments",
                DatabaseHelper.Int("@EnrollmentId", enrollmentId),DatabaseHelper.Bit("@IncludeCancelled",enrollmentHasLeft));
            assignments = dt;
            hasOpenTransport=dt.AsEnumerable().Any(row => Convert.ToBoolean(row["IsTransportLinked"]) && row["EffectiveTo"]==DBNull.Value);
            grid.Rows.Clear();
            DateTime selectedMonth = ReferenceMonth;
            if (chooseLatestPostedMonth)
            {
                DateTime month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
                if (dt.Columns.Contains("LastBilledMonth"))
                    foreach (DataRow assignment in dt.Rows)
                        if (assignment["LastBilledMonth"] != DBNull.Value &&
                            Convert.ToDateTime(assignment["LastBilledMonth"]) > month)
                            month = Convert.ToDateTime(assignment["LastBilledMonth"]);
                selectedMonth = month;
            }
            ReferenceMonth = FeeAssignmentPresentation.PostingMonth(dt);
            foreach (DataRow row in dt.Rows)
            {
                object effectiveFrom = row.Table.Columns.Contains("EffectiveFrom") ? row["EffectiveFrom"] : DBNull.Value;
                object effectiveTo = row.Table.Columns.Contains("EffectiveTo") ? row["EffectiveTo"] : DBNull.Value;
                object lastBilledMonth = row.Table.Columns.Contains("LastBilledMonth") ? row["LastBilledMonth"] : DBNull.Value;
                bool isOpen = effectiveTo == DBNull.Value;
                int index = grid.Rows.Add(
                    Convert.ToString(row["FeeTypeName"]),
                    row["StandardAmount"],
                    row["DiscountAmount"],
                    row["NetAmount"],
                    Convert.ToString(row["Frequency"]),
                    effectiveFrom == DBNull.Value ? "From start" : FormatMonth(effectiveFrom),
                    effectiveTo == DBNull.Value ? "Ongoing" : FormatMonth(effectiveTo),
                    AssignmentStatus(effectiveFrom, effectiveTo, ReferenceMonth),
                    lastBilledMonth == DBNull.Value ? "Not posted" : FormatMonth(lastBilledMonth),
                    Convert.ToInt32(row["StudentFeeAssignmentId"]),
                    Convert.ToInt32(row["FeeTypeId"]),
                    isOpen,
                    lastBilledMonth,
                    row.Table.Columns.Contains("IsTransportLinked") && Convert.ToBoolean(row["IsTransportLinked"]),
                    grid.Rows.Count);
                grid.Rows[index].Cells["EffectiveFrom"].Tag = effectiveFrom == DBNull.Value
                    ? null : Convert.ToDateTime(effectiveFrom);
                grid.Rows[index].Cells["ConcessionLabel"].Value = row.Table.Columns.Contains("ConcessionLabel") ? row["ConcessionLabel"] : "";
                grid.Rows[index].Cells["EffectiveTo"].Tag = effectiveTo == DBNull.Value
                    ? null : Convert.ToDateTime(effectiveTo);
                if (Convert.ToString(grid.Rows[index].Cells["Status"].Value) == "Historical")
                {
                    grid.Rows[index].DefaultCellStyle.ForeColor = AppTheme.MutedText;
                }
            }
            addButton.Enabled = true;
            UpdateMonthView();
            if (!enrollmentHasLeft && CurrentUser.HasPermission("Fees.Collect")) {
                int pending = FeeAssignmentPosting.PendingThrough(enrollmentId, ReferenceMonth).Rows.Count;
                NeedsReview = NeedsReview || pending > 0;
                hint.Text += pending > 0 ? $"  |  {pending} pending through {ReferenceMonth:MMM yyyy} — Review Pending Fees." : "  |  Posting up to date.";
            }
        }
        catch (Exception ex)
        {
            addButton.Enabled = false;
            UpdateButtonStates();
            ModernMessageDialog.Show(
                this,
                DbMessage.UserMessage(ex),
                "Fee Assignments",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
    private static string AssignmentStatus(object from, object to, DateTime today)
    {
        // Assignment boundaries are inclusive billing months, not day-level expiry dates.
        DateTime month = new(today.Year, today.Month, 1);
        if (to != DBNull.Value && to != null && Convert.ToDateTime(to) < month)
            return "Historical";
        if (from != DBNull.Value && from != null && Convert.ToDateTime(from) > month)
            return "Scheduled";
        return "Active";
    }
    private void SetMonthOptions(DataTable assignments, DateTime selectedMonth)
    {
        ReferenceMonth = selectedMonth;
    }
    private void UpdateMonthView()
    {
        if (enrollmentId <= 0) return;
        foreach (DataGridViewRow row in grid.Rows)
        {
            string status = AssignmentStatus(row.Cells["EffectiveFrom"].Tag!,
                row.Cells["EffectiveTo"].Tag!, ReferenceMonth);
            row.Cells["Status"].Value = status;
            row.DefaultCellStyle.ForeColor = status == "Historical" ? AppTheme.MutedText : AppTheme.Text;
            row.Cells["Status"].ToolTipText = $"Rate status for {ReferenceMonth:MMMM-yyyy}. Posting future fees does not change the effective dates.";
            row.Cells["PostedThrough"].ToolTipText = "Latest charge month recorded for this rate version. See the student ledger for posted amounts and payment status.";
        }
        grid.Sort(new FeePeriodComparer(ReferenceMonth));
        MarkOverlappingPeriods();
        if(enrollmentHasLeft)
            foreach(DataGridViewRow row in grid.Rows) {
                row.Cells["Status"].Value="Historical";
                row.DefaultCellStyle.ForeColor=AppTheme.MutedText;
                row.Cells["Status"].ToolTipText="Placement has ended. Preserved fee schedules do not authorize posting after the leave month.";
            }
        UpdateButtonStates();
        int active = grid.Rows.Cast<DataGridViewRow>()
            .Count(r => Convert.ToString(r.Cells["Status"].Value) == "Active");
        hint.Text = grid.Rows.Count == 0 ? "No fees are assigned to this placement yet."
            : grid.Rows.Cast<DataGridViewRow>().Any(r => r.Tag is true)
                ? "Some fee periods overlap. Review these before making further changes."
                : FeeAssignmentPresentation.CompactSummary(assignments, ReferenceMonth);
        if(enrollmentHasLeft) hint.Text="Student has left. These are preserved fee records. Existing charges and balances remain in Fee Collection.";
        reviewButton.Enabled = !enrollmentHasLeft && !IsBusy && enrollmentId > 0 && CurrentUser.HasPermission("Fees.Collect");
    }
    private sealed class FeePeriodComparer(DateTime month) : System.Collections.IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not DataGridViewRow left || y is not DataGridViewRow right) return 0;
            int group = Rank(left).CompareTo(Rank(right));
            // The query returns fee types in their configured display order, then rate periods.
            return group != 0 ? group : Convert.ToInt32(left.Cells["ConfiguredOrder"].Value)
                .CompareTo(Convert.ToInt32(right.Cells["ConfiguredOrder"].Value));
        }
        private int Rank(DataGridViewRow row) => AssignmentStatus(
            row.Cells["EffectiveFrom"].Tag!, row.Cells["EffectiveTo"].Tag!, month) switch {
                "Active" => 0,
                "Scheduled" => 1,
                _ => 2
            };
    }
    private static bool HasEnded(DataGridViewRow row) =>
        AssignmentStatus(row.Cells["EffectiveFrom"].Tag!, row.Cells["EffectiveTo"].Tag!, DateTime.Today) == "Historical";
    private static string FormatMonth(object value)
    {
        if (value == null || value == DBNull.Value)
            return string.Empty;
        return DateTime.TryParse(Convert.ToString(value), out DateTime parsed)
            ? parsed.ToString("MMM-yyyy")
            : Convert.ToString(value) ?? string.Empty;
    }
    private DataGridViewRow? SelectedRow =>
        grid.SelectedRows.Count > 0 ? grid.SelectedRows[0] : null;
    private void MarkOverlappingPeriods()
    {
        var rows = grid.Rows.Cast<DataGridViewRow>().ToArray();
        foreach (var row in rows)
        {
            DateTime from = row.Cells["EffectiveFrom"].Tag is DateTime start ? start : DateTime.MinValue;
            DateTime to = row.Cells["EffectiveTo"].Tag is DateTime end ? end : DateTime.MaxValue;
            bool overlaps = Convert.ToString(row.Cells["Frequency"].Value)=="Monthly" && rows.Any(other => other != row &&
                Equals(other.Cells["FeeTypeId"].Value, row.Cells["FeeTypeId"].Value) &&
                (other.Cells["EffectiveFrom"].Tag is DateTime s ? s : DateTime.MinValue) <= to &&
                (other.Cells["EffectiveTo"].Tag is DateTime e ? e : DateTime.MaxValue) >= from);
            row.Tag = overlaps;
            if (overlaps) row.Cells["Status"].Value = "Needs Review";
        }
    }
    private static bool IsTransport(DataGridViewRow row) =>
        row.Cells["IsTransportLinked"].Value is bool value && value;
    private void UpdateButtonStates()
    {
        var row = SelectedRow;
        issuedBillButton.Enabled=row is not null&&CurrentUser.HasPermission("Fees.Adjust");
        if(enrollmentHasLeft) {
            addButton.Enabled=discontinueButton.Enabled=startTransportButton.Enabled=reviewButton.Enabled=false;
            discountButton.Enabled=row is not null&&CurrentUser.HasPermission("Fees.Adjust");
            return;
        }
        bool isOpenRowSelected = row != null && row.Tag is not true && row.Cells["IsOpen"].Value is bool b && b;
        discontinueButton.Enabled = isOpenRowSelected && IsTransport(row!);
        discountButton.Enabled = row != null && row.Tag is not true && CurrentUser.HasPermission("Fees.Adjust");
        startTransportButton.Enabled = enrollmentId>0 && addButton.Enabled && !hasOpenTransport;
    }
    /// <summary>Fills the fee row menu and says whether it should open at all. A historical row therefore opens the menu with everything greyed out rather than pretending the actions apply.</summary>
    private bool BuildRowMenu(DataGridViewRow? row)
    {
        rowMenu.Items.Clear();
        if (row is null)
            return false;
        var header =
            rowMenu.Items.Add(
                $"{Convert.ToString(row.Cells["FeeTypeName"].Value)}  |  "
                + $"Rs. {Convert.ToDecimal(row.Cells["NetAmount"].Value):N0}");
        header.Enabled = false;
        rowMenu.Items.Add(
            new ToolStripSeparator());
        bool isOpenRowSelected =
            row.Tag is not true && row.Cells["IsOpen"].Value is bool open && open;
        var discount =
            rowMenu.Items.Add(
                "Review Fee Terms...");
        discount.Enabled = (enrollmentHasLeft||row.Tag is not true)&&CurrentUser.HasPermission("Fees.Adjust");
        discount.Click += (_, _) => ChangeDiscount();
        var issued=rowMenu.Items.Add("Review an Issued Bill...");issued.Enabled=CurrentUser.HasPermission("Fees.Adjust");issued.Click+=(_,_)=>ReviewIssuedBill();
        if(IsTransport(row))
        {
            var stop=rowMenu.Items.Add("Stop Transport...");
            stop.Enabled=!enrollmentHasLeft&&isOpenRowSelected;
            stop.Click += (_, _) => Discontinue();
        }
        return true;
    }
    private void AnnounceChange()
    {
        ChangesMade = true;
        NeedsReview = true;
        ReviewCompleted = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private Task ReviewPendingFeesAsync()
    {
        if (enrollmentHasLeft || IsBusy || enrollmentId <= 0 || !CurrentUser.HasPermission("Fees.Collect")) return Task.CompletedTask;
        IsBusy = true;
        try {
            using var review = new PendingStudentFeesDialog(enrollmentId);
            review.ShowDialog(FindForm());
            ReviewCompleted = review.ReviewCompleted;
            ChangesMade = ChangesMade || review.PostingMade || review.ReviewCompleted;
            LoadGrid();
            NeedsReview = !review.ReviewCompleted;
        }
        finally {
            IsBusy = false;
            reviewButton.Enabled = !enrollmentHasLeft && CurrentUser.HasPermission("Fees.Collect") && enrollmentId > 0;
        }
        return Task.CompletedTask;
    }
    private void AddFee()
    {
        if (enrollmentId <= 0)
            return;
        using var form = new AddFeeAssignmentForm(enrollmentId);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            AnnounceChange();
            LoadGrid();
        }
    }
    private void StartTransport()
    {
        if(enrollmentId<=0 || hasOpenTransport) return;
        using var form=new AddFeeAssignmentForm(enrollmentId,transportOnly:true);
        if(form.ShowDialog(this)==DialogResult.OK)
        {
            AnnounceChange();
            LoadGrid();
        }
    }
    private void Discontinue()
    {
        var row = SelectedRow;
        if (row == null || row.Tag is true || !IsTransport(row)) return;
        int id = Convert.ToInt32(row.Cells["StudentFeeAssignmentId"].Value);
        string feeTypeName = Convert.ToString(row.Cells["FeeTypeName"].Value) ?? "";
        DateTime? starts = row.Cells["EffectiveFrom"].Tag is DateTime date ? date : null;
        using var form = new DiscontinueFeeAssignmentForm(id, feeTypeName, starts);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            AnnounceChange();
            LoadGrid();
        }
    }
    private void ChangeDiscount()
    {
        var row=SelectedRow;
        if(row is null||!CurrentUser.HasPermission("Fees.Adjust"))return;
        if(Convert.ToString(row.Cells["Frequency"].Value)=="As Needed"){ReviewIssuedBill();return;}
        try
        {
            int id=Convert.ToInt32(row.Cells["StudentFeeAssignmentId"].Value);
            using var form=new ChangeFeeDiscountForm(id,Convert.ToString(row.Cells["FeeTypeName"].Value)??"Fee",
                Convert.ToDecimal(row.Cells["StandardAmount"].Value),Convert.ToDecimal(row.Cells["DiscountAmount"].Value));
            if(form.ShowDialog(this)==DialogResult.OK){AnnounceChange();LoadGrid();}
        }
        catch(Exception ex){ModernMessageDialog.Show(FindForm(),DbMessage.UserMessage(ex),"Review Fee Terms",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }
    private void ReviewIssuedBill()
    {
        var row=SelectedRow;
        if(row is null||!CurrentUser.HasPermission("Fees.Adjust"))return;
        try
        {
            using var data=FeeDiscountPeriod.IssuedBills(Convert.ToInt32(row.Cells["StudentFeeAssignmentId"].Value));
            using var picker=new IssuedBillPicker(data,Convert.ToString(row.Cells["FeeTypeName"].Value)??"Fee");
            if(picker.ShowDialog(this)!=DialogResult.OK)return;
            var context=FeeDiscountPeriod.BillContext(picker.SelectedBillId);
            using var form=new ChangeFeeDiscountForm(Convert.ToInt32(context["StudentFeeAssignmentId"]),Convert.ToString(context["FeeTypeName"])??"Fee",
                Convert.ToDecimal(context["StandardAmount"]),Convert.ToDecimal(context["DiscountAmount"]),context);
            form.ShowDialog(this);
            if(form.Changed){AnnounceChange();LoadGrid();}
        }
        catch(Exception ex){ModernMessageDialog.Show(FindForm(),DbMessage.UserMessage(ex),"Review Issued Bill",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }
    /// <summary>The panel is added to whoever hosts it, so the ids it was last pointed at are the only state worth reporting.</summary>
    public bool HasStudent => enrollmentId > 0 && admissionId > 0;
}
