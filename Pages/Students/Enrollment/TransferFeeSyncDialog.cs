using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Students.Enrollment;
/// <summary>Shown by StudentTransferReadmissionPage BEFORE a Transfer Class / Transfer Campus is executed, whenever the destination class/campus's fee configuration differs from what the student is currently assigned.</summary>
public sealed class TransferFeeSyncDialog : ApplicationWindowForm
{
    public enum FeeDecision
    {
        Cancel,
        Keep,
        NextMonth,
        CurrentMonth,
        CustomMonth
    }
    private sealed class ComparisonRow
    {
        public required string FeeTypeName;
        public int DisplayOrder;
        public int FeeTypeId;
        public int CurrentAssignmentId;
        public bool Changes;
        public decimal? CurrentAmount;
        public decimal? DestAmount;
        public string? Frequency;
    }
    internal sealed record ComparisonData(DataTable FeeTypes,DataTable Current,DataTable Destination,DateTime CutoffDate);
    private DateTime cutoffMonth;
    public FeeDecision Decision { get; private set; } = FeeDecision.Cancel;
    /// <summary>True when at least one fee type differs between the
    /// student's current assignments and the destination's configuration
    /// The caller should skip showing this dialog entirely when false.</summary>
    public bool HasFeeDifference { get; }
    public bool CanApplyDestinationFees { get; private set; }
    private string missingDestinationFees = string.Empty;
    private DataRow[] existingPeriods = Array.Empty<DataRow>();
    private readonly RadioButton customMonthOption = new() { Text="New fees from selected month",AutoSize=true };
    private readonly DateTimePicker effectiveMonth = new() { Format=DateTimePickerFormat.Custom,CustomFormat="MMM yyyy",ShowUpDown=true,Width=130 };
    private readonly CheckBox replaceSchedule = new() { Text="Replace unbilled scheduled rates / waivers from that month",AutoSize=true };
    public DateTime? EffectiveMonth => customMonthOption.Checked ? new DateTime(effectiveMonth.Value.Year,effectiveMonth.Value.Month,1) : null;
    public bool ReplaceScheduledFees => !keepOption.Checked && replaceSchedule.Checked;
    private readonly ModernComboBox paidReduction = HumanUi.Combo();
    public string PaidReductionAction => paidReduction.SelectedIndex == 1 ? "Refund" : "Credit";
    private readonly List<ComparisonRow> rows = new();
    private readonly DataGridView grid = HumanUi.Grid(false);
    private readonly RadioButton keepOption = new() {
        Text = "Keep current fees",
        AutoSize = true,
        Checked = true
    };
    private readonly RadioButton nextMonthOption = new() {
        Text = "New fees from next month",
        AutoSize = true
    };
    private readonly RadioButton currentMonthOption = new() {
        Text = "New fees from this month (unbilled fees only)",
        AutoSize = true
    };
    public TransferFeeSyncDialog(
        string studentDisplay,
        string currentPlacementLabel,
        string destinationPlacementLabel,
        DateTime transferDate,
        int enrollmentId,
        int destinationCampusId,
        int destinationClassId)
        :this(studentDisplay,currentPlacementLabel,destinationPlacementLabel,transferDate,enrollmentId,destinationCampusId,destinationClassId,null) { }

    internal TransferFeeSyncDialog(string studentDisplay,string currentPlacementLabel,string destinationPlacementLabel,
        DateTime transferDate,int enrollmentId,int destinationCampusId,int destinationClassId,ComparisonData? comparison)
    {
        Text = "Fees After Transfer";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(AppScale.Scale(880), AppScale.Scale(660));
        ClientSize = new Size(AppScale.Scale(1040), AppScale.Scale(760));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        LoadComparison(enrollmentId, destinationCampusId, destinationClassId,comparison);
        paidReduction.Items.AddRange(new object[] { "Keep excess payment as advance", "Refund the excess payment" });
        paidReduction.SelectedIndex = 0;
        FinanceHelp.Attach(this,paidReduction,FinanceHelp.Topic.PaidReduction);
        FinanceHelp.Attach(this,keepOption,FinanceHelp.Topic.TransferFees);
        FinanceHelp.Attach(this,currentMonthOption,FinanceHelp.Topic.TransferFees);
        FinanceHelp.Attach(this,nextMonthOption,FinanceHelp.Topic.TransferFees);
        FinanceHelp.Attach(this,customMonthOption,FinanceHelp.Topic.TransferFees);
        FinanceHelp.Attach(this,replaceSchedule,"Replace future rates or full discounts that have not yet been billed, starting in the chosen month.","An unbilled November discount may be replaced by the new class terms. Check the review before continuing.");
        var policy = comparison is null ? DatabaseHelper.QuerySql("SELECT dbo.GetMonthlyBillingStartDate(@Date) StartDate;", DatabaseHelper.Date("@Date", transferDate)) : null;
        DateTime cutoffDate = comparison?.CutoffDate ?? Convert.ToDateTime(policy!.Rows[0]["StartDate"]);
        cutoffMonth=new DateTime(cutoffDate.Year,cutoffDate.Month,1);
        currentMonthOption.Text = $"Use School Profile cutoff: new fees from {cutoffDate:MMMM yyyy}";

        effectiveMonth.MinDate = cutoffMonth;
        DateTime lastBilled = existingPeriods.Select(row => PeriodDate(row,"LastBilledMonth") ?? effectiveMonth.MinDate.AddMonths(-1)).DefaultIfEmpty(effectiveMonth.MinDate.AddMonths(-1)).Max();
        effectiveMonth.Value = lastBilled >= effectiveMonth.MinDate ? lastBilled.AddMonths(1) : effectiveMonth.MinDate;
        HasFeeDifference = rows.Any(r => r.Changes);
        if (CanApplyDestinationFees) { keepOption.Checked = false; currentMonthOption.Checked = true; }
        BuildLayout(studentDisplay, currentPlacementLabel, destinationPlacementLabel, transferDate);
    }
    private void LoadComparison(int enrollmentId, int destinationCampusId, int destinationClassId,ComparisonData? comparison)
    {
        var feeTypes = comparison?.FeeTypes ?? DatabaseHelper.QueryUser("GetFeeTypes");
        var displayOrder = feeTypes.AsEnumerable().ToDictionary(
            row => Convert.ToInt32(row["FeeTypeId"]),
            row => Convert.ToInt32(row["DisplayOrder"]));
        // "Current" = the enrollment's currently-OPEN assignments (same EffectiveTo IS NULL definition used everywhere else in this system).
        var current = comparison?.Current ?? DatabaseHelper.QueryUser("GetStudentFeeAssignments", DatabaseHelper.Int("@EnrollmentId", enrollmentId), DatabaseHelper.Bit("@IncludeCancelled", true));
        var cancelledFees = current.AsEnumerable()
            .Where(r => r["EffectiveTo"] == DBNull.Value && r["CancelledAt"] != DBNull.Value)
            .Select(r => Convert.ToInt32(r["FeeTypeId"])).ToHashSet();
        var eligibleTypes=feeTypes.AsEnumerable().Where(type=>
            string.Equals(Convert.ToString(type["FeeTypeCode"]),"TUITION",StringComparison.OrdinalIgnoreCase)
            || (Convert.ToBoolean(type["IsTransportLinked"]) && current.AsEnumerable().Any(a=>
                Convert.ToInt32(a["FeeTypeId"])==Convert.ToInt32(type["FeeTypeId"]) && a["CancelledAt"]==DBNull.Value
                && a["EffectiveTo"]==DBNull.Value && Convert.ToString(a["Frequency"])=="Monthly")))
            .Select(type=>Convert.ToInt32(type["FeeTypeId"])).ToHashSet();
        var liveCurrent = current.AsEnumerable().Where(r => r["CancelledAt"] == DBNull.Value
            && Convert.ToString(r["Frequency"])=="Monthly" && eligibleTypes.Contains(Convert.ToInt32(r["FeeTypeId"]))).ToArray();
        existingPeriods = liveCurrent;
        var currentByFeeType = liveCurrent
            .Where(r => r["EffectiveTo"] == DBNull.Value)
            .GroupBy(r => Convert.ToInt32(r["FeeTypeId"]))
            .Select(group => group.OrderByDescending(r => Convert.ToInt32(r["StudentFeeAssignmentId"])).First())
            .ToDictionary(
                r => Convert.ToInt32(r["FeeTypeId"]),
                r => (
                    Name: Convert.ToString(r["FeeTypeName"]) ?? string.Empty,
                    Amount: Convert.ToDecimal(r["StandardAmount"]),
                    Frequency: Convert.ToString(r["Frequency"]) ?? string.Empty,
                    Id: Convert.ToInt32(r["StudentFeeAssignmentId"])));
        // Destination fees match the configuration used by the transfer procedure.
        var destination = comparison?.Destination ?? DatabaseHelper.QueryUser(
            "GetFeeConfigurations",
            DatabaseHelper.Int("@CampusId", destinationCampusId),
            DatabaseHelper.Int("@ClassId", destinationClassId));
        var activeDestination = destination.AsEnumerable().Where(r=>Convert.ToString(r["Frequency"])=="Monthly" && eligibleTypes.Contains(Convert.ToInt32(r["FeeTypeId"]))).ToArray();
        var destByFeeType = activeDestination.Where(r =>
            !cancelledFees.Contains(Convert.ToInt32(r["FeeTypeId"])) &&
            (Convert.ToString(r["Frequency"]) == "Monthly") &&
            (string.Equals(Convert.ToString(r["FeeTypeCode"]),"TUITION",StringComparison.OrdinalIgnoreCase) ||
             (Convert.ToBoolean(r["IsTransportLinked"]) && currentByFeeType.ContainsKey(Convert.ToInt32(r["FeeTypeId"])))))
            .ToDictionary(
                r => Convert.ToInt32(r["FeeTypeId"]),
                r => (
                    Name: Convert.ToString(r["FeeTypeName"]) ?? string.Empty,
                    Amount: Convert.ToDecimal(r["Amount"]),
                    Frequency: Convert.ToString(r["Frequency"]) ?? string.Empty));
        var missingRequired = liveCurrent
            .Where(r => r["EffectiveTo"] == DBNull.Value && Convert.ToBoolean(r["IsMandatory"]))
            .Where(r => !activeDestination.Any(d => Convert.ToInt32(d["FeeTypeId"]) == Convert.ToInt32(r["FeeTypeId"])))
            .Select(r => Convert.ToString(r["FeeTypeName"]) ?? "Required fee")
            .Distinct().ToArray();
        CanApplyDestinationFees = activeDestination.Length > 0 && missingRequired.Length == 0;
        missingDestinationFees = activeDestination.Length == 0
            ? "No destination fees are configured."
            : "Missing required destination fees: " + string.Join(", ", missingRequired) + ".";
        foreach (int feeTypeId in currentByFeeType.Keys.Union(destByFeeType.Keys))
        {
            bool hasCurrent = currentByFeeType.TryGetValue(feeTypeId, out var c);
            bool hasDest = destByFeeType.TryGetValue(feeTypeId, out var d);
            rows.Add(new ComparisonRow {
                FeeTypeName = hasDest ? d.Name : c.Name,
                DisplayOrder = displayOrder.GetValueOrDefault(feeTypeId,int.MaxValue),
                FeeTypeId = feeTypeId,
                CurrentAssignmentId = hasCurrent ? c.Id : 0,
                Changes = !hasCurrent || !hasDest || c.Amount!=d.Amount || c.Frequency!=d.Frequency,
                CurrentAmount = hasCurrent ? c.Amount : null,
                DestAmount = hasDest ? d.Amount : null,
                Frequency = hasDest ? d.Frequency : c.Frequency
            });
        }
    }
    private void BuildLayout(string studentDisplay, string currentPlacementLabel, string destinationPlacementLabel, DateTime transferDate)
    {
        var title = new Label {
            Text = "Fees after transfer",
            Dock = DockStyle.Top,
            Height = Math.Max(30, AppMetrics.TextHeight(AppTheme.PageTitleFont) + 8),
            Font = AppTheme.PageTitleFont,
            ForeColor = AppTheme.Text
        };
        var summary = new TableLayoutPanel {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 2,
            Margin = new Padding(3, AppMetrics.SpacingXs, 3, AppMetrics.SpacingSm)
        };
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var details = new[] {
            (Label: "Student", Value: studentDisplay),
            (Label: "Current", Value: currentPlacementLabel),
            (Label: "Destination", Value: destinationPlacementLabel),
            (Label: "Date", Value: transferDate.ToString("dd-MM-yyyy"))
        };
        var detailValues = new List<Label>();
        for (int row = 0; row < details.Length; row++)
        {
            if(row%2==0)summary.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            summary.Controls.Add(new Label {
                Text = details[row].Label,
                AutoSize = true,
                Font = AppTheme.BodyBoldFont,
                ForeColor = AppTheme.Text,
                Margin = new Padding(0, 2, AppMetrics.SpacingMd, 2)
            }, (row%2)*2, row/2);
            var value = new Label {
                Text = details[row].Value,
                AutoSize = true,
                Font = AppTheme.NormalFont,
                ForeColor = AppTheme.Text,
                Margin = new Padding(0, 2, 0, 2)
            };
            detailValues.Add(value);
            summary.Controls.Add(value, (row%2)*2+1, row/2);
        }
        var scopeNote=new Label {
            Text="Same-year transfer: tuition and existing transport only. Admission, books and other fees stay unchanged.",
            AutoSize=true,Dock=DockStyle.Top,Font=AppTheme.NormalFont,ForeColor=AppTheme.MutedText,
            Margin=new Padding(0,AppMetrics.SpacingXs,0,AppMetrics.SpacingSm)
        };
        var note = new Label {
            Name="TransferFeeStatus",
            Text = "Review the new rates below. Issued monthly bills receive only the required difference; paid reductions follow your credit/refund choice.",
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText
        };
        if (!CanApplyDestinationFees)
        {
            note.Text = missingDestinationFees + " Choose Keep current fees to continue the transfer, or cancel and complete Fee Setup first.";
            note.AutoSize = true;
            nextMonthOption.Enabled = false;
            currentMonthOption.Enabled = false;
            customMonthOption.Enabled = false;
        }
        grid.Dock = DockStyle.Fill;
        grid.AutoGenerateColumns = false;
        grid.Columns.Add("FeeTypeName", UiText.Fees.FeeType);
        grid.Columns.Add("Current", "Current fees");
        grid.Columns.Add("New", "New fees");
        grid.Columns.Add("Change", "Change");
        grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
        grid.Columns["FeeTypeName"].FillWeight=34;grid.Columns["FeeTypeName"].MinimumWidth=190;
        grid.Columns["Current"].FillWeight=18;grid.Columns["Current"].MinimumWidth=120;
        grid.Columns["New"].FillWeight=22;grid.Columns["New"].MinimumWidth=140;
        grid.Columns["Change"].FillWeight=26;grid.Columns["Change"].MinimumWidth=170;
        grid.RowTemplate.Height=Math.Max(AppScale.Scale(34),AppMetrics.InputControlHeight);
        foreach (var name in new[] { "Current", "New", "Change" })
        {
            grid.Columns[name].DefaultCellStyle = new DataGridViewCellStyle {
                Format = "0.##",
                Alignment = DataGridViewContentAlignment.MiddleRight
            };
        }
        HumanUi.ApplyGridStyle(grid, AppTheme.Primary);
        var confirmButton = new ModernButton {
            Text = "Confirm Transfer",Width=AppScale.Scale(150),Height=AppMetrics.ButtonHeight,ThemeRole="Success"
        };
        string DefaultNote() => "New tuition / transport rates may adjust issued monthly bills. Paid reductions use your advance/refund choice.";
        void RefreshComparison()
        {
        bool keep = keepOption.Checked;
        if (keep && replaceSchedule.Checked) replaceSchedule.Checked = false;
        DateTime effectiveMonth = new DateTime(transferDate.Year,transferDate.Month,1)
            .AddMonths(nextMonthOption.Checked ? 1 : 0);
        if(currentMonthOption.Checked)effectiveMonth=cutoffMonth;
        if (customMonthOption.Checked) effectiveMonth = EffectiveMonth!.Value;
        this.effectiveMonth.Enabled = customMonthOption.Checked;
        replaceSchedule.Enabled = !keep;
        string problem = keep ? string.Empty : ValidateChoice(effectiveMonth);
        confirmButton.Enabled = keep || problem.Length==0;
        note.Text = problem.Length>0 ? problem : CanApplyDestinationFees ? DefaultNote()
            : missingDestinationFees + " Keep current fees or complete Fee Setup first.";
        OperationFeedback.Apply(note,problem.Length>0 || !CanApplyDestinationFees);
        grid.Columns["New"].HeaderText = keep ? "After transfer" : $"From {effectiveMonth:MMM yyyy}";
        grid.Rows.Clear();
        foreach (var row in rows.OrderBy(r => r.DisplayOrder).ThenBy(r => r.FeeTypeName))
        {
            decimal? selectedAmount = keep ? row.CurrentAmount : row.DestAmount;
            string currentText = row.CurrentAmount is decimal ca
                ? ca.ToString("0.##")
                : "—";
            string destText = selectedAmount is decimal da
                ? da.ToString("0.##")
                : "Not included";
            string changeText;
            if (keep)
                changeText = "No change";
            else if (row.CurrentAmount is null)
                changeText = "New fee";
            else if (row.DestAmount is null)
                changeText = "Stops with new fees";
            else
            {
                decimal diff = row.DestAmount.Value - row.CurrentAmount.Value;
                changeText = diff == 0
                    ? "No change"
                    : (diff > 0 ? "+" : "") + diff.ToString("0.##");
            }
            int index = grid.Rows.Add(row.FeeTypeName, currentText, destText, changeText);
            bool isDifferent = row.CurrentAmount != selectedAmount;
            if (isDifferent)
            {
                grid.Rows[index].DefaultCellStyle.Font =
                    AppTheme.BodyBoldFont;
            }
        }
        grid.ClearSelection();
        }
        foreach (var option in new[] { keepOption,nextMonthOption,currentMonthOption,customMonthOption })
            option.CheckedChanged += (_, _) => { if (option.Checked) RefreshComparison(); };
        effectiveMonth.ValueChanged += (_, _) => RefreshComparison();
        replaceSchedule.CheckedChanged += (_, _) => RefreshComparison();
        RefreshComparison();
        var choiceLabel = new Label {
            Text = "Apply fees",
            Dock = DockStyle.Top,
            Height = Math.Max(22, AppMetrics.TextHeight(AppTheme.NormalFont) + 6),
            Font = AppTheme.BodyBoldFont,
            ForeColor = AppTheme.Text,
            Margin = new Padding(0, 10, 0, 4)
        };
        var choicePanel = new FlowLayoutPanel {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            Height = AppScale.Scale(90, AppScale.ControlSizeScale)
        };
        keepOption.Margin = new Padding(0, 2, 0, 2);
        nextMonthOption.Margin = new Padding(0, 2, 0, 2);
        currentMonthOption.Margin = new Padding(0, 2, 0, 2);
        choicePanel.Controls.Add(keepOption);
        choicePanel.Controls.Add(nextMonthOption);
        choicePanel.Controls.Add(currentMonthOption);
        var customRow = new FlowLayoutPanel {AutoSize=true,WrapContents=false,Margin=Padding.Empty};
        customRow.Controls.Add(customMonthOption);
        customRow.Controls.Add(effectiveMonth);
        choicePanel.Controls.Add(customRow);
        choicePanel.Controls.Add(replaceSchedule);
        var choices=new TableLayoutPanel {Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,64));
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,36));
        choices.Controls.Add(choicePanel,0,0);
        var paidPanel=new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,RowCount=2,Margin=new Padding(AppMetrics.SpacingMd,0,0,0)};
        paidPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var paidField=HumanUi.Field("If a reduced fee was already paid",paidReduction);
        paidField.Height=AppMetrics.FieldRowHeight;
        paidPanel.Controls.Add(paidField,0,0);
        var paidNote=new Label {Text="Choose how to return any excess payment after the reviewed rate reduction.",AutoSize=true,Dock=DockStyle.Top,
            Font=AppTheme.NormalFont,ForeColor=AppTheme.MutedText};
        paidPanel.Controls.Add(paidNote,0,1);
        choices.Controls.Add(paidPanel,1,0);
        // Radio buttons need one shared parent to remain mutually exclusive.
        customMonthOption.CheckedChanged += (_, _) => {
            if(customMonthOption.Checked) {
                keepOption.Checked=false; nextMonthOption.Checked=false; currentMonthOption.Checked=false;
                RefreshComparison();
            }
        };
        foreach(var option in new[]{keepOption,nextMonthOption,currentMonthOption})
            option.CheckedChanged += (_, _) => {if(option.Checked) {customMonthOption.Checked=false; RefreshComparison();}};
        var cancelButton = new ModernButton {
            Text = UiText.Actions.Cancel,
            Width = AppScale.Scale(105),
            Height = AppMetrics.ButtonHeight,
            ThemeRole = "Secondary",
            DialogResult = DialogResult.Cancel
        };
        confirmButton.Click += (_, _) => {
            if (!CanApplyDestinationFees && (nextMonthOption.Checked || currentMonthOption.Checked))
            {
                ModernMessageDialog.Show(this, missingDestinationFees + " Keep existing fees or cancel and complete Fee Setup first.",
                    "Destination Fees Incomplete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Decision =
                keepOption.Checked ? FeeDecision.Keep :
                nextMonthOption.Checked ? FeeDecision.NextMonth :
                currentMonthOption.Checked ? FeeDecision.CurrentMonth :
                customMonthOption.Checked ? FeeDecision.CustomMonth :
                FeeDecision.Keep;
            DialogResult = DialogResult.OK;
        };
        var buttons = HumanUi.ButtonRow(cancelButton, confirmButton);
        buttons.Dock = DockStyle.Bottom;
        buttons.Margin = new Padding(0, 10, 0, 0);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(16)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(summary, 0, 1);
        root.Controls.Add(scopeNote, 0, 2);
        root.Controls.Add(grid, 0, 3);
        root.Controls.Add(note, 0, 4);
        root.Controls.Add(choiceLabel, 0, 5);
        root.Controls.Add(choices, 0, 6);
        root.Controls.Add(buttons, 0, 7);
        void FitText()
        {
            int width = Math.Max(1, root.ClientSize.Width - root.Padding.Horizontal - summary.Margin.Horizontal);
            summary.MaximumSize = new Size(width, 0);
            note.MaximumSize = new Size(width, 0);
            scopeNote.MaximumSize=new Size(width,0);
            paidNote.MaximumSize=new Size(Math.Max(1,paidPanel.Width-paidPanel.Margin.Horizontal),0);
            int captionWidth = summary.Controls.OfType<Label>()
                .Where(label => summary.GetColumn(label) == 0)
                .Max(label => label.PreferredWidth + label.Margin.Horizontal);
            foreach (var value in detailValues)
                value.MaximumSize = new Size(Math.Max(1, width/2 - captionWidth-AppMetrics.SpacingMd), 0);
        }
        root.SizeChanged += (_, _) => FitText();
        Controls.Add(root);
        FitText();
        // Adding radio buttons to their shared parent can reset the initial choice.
        if(CanApplyDestinationFees){keepOption.Checked=false;currentMonthOption.Checked=true;}
        else keepOption.Checked=true;
        RefreshComparison();
        /* UX - the decision on this dialog is the choice below the grid, not the grid. */
        AcceptButton = confirmButton;
        CancelButton = cancelButton;
        ActiveControl = CanApplyDestinationFees ? currentMonthOption : keepOption;
    }
    private static DateTime? PeriodDate(DataRow row,string column) => row.Table.Columns.Contains(column) && row[column]!=DBNull.Value
        ? Convert.ToDateTime(row[column]) : null;
    private string ValidateChoice(DateTime target)
    {
        if(target<cutoffMonth)return $"The school cutoff permits new rates from {cutoffMonth:MMMM yyyy}. Choose that month or a later one.";
        if(!CanApplyDestinationFees) return missingDestinationFees + " Keep current fees or complete Fee Setup first.";
        foreach(var period in existingPeriods)
        {
            if(PeriodDate(period,"EffectiveFrom") is DateTime start && PeriodDate(period,"EffectiveTo") is DateTime end && start>end)
                return $"{CellFeeName(period)}: the period starts {start:MMM yyyy} but ends {end:MMM yyyy}. Review Fee Changes first, or keep current fees. The checkbox only replaces valid unbilled schedules.";
            if(existingPeriods.Any(other => !ReferenceEquals(period,other)
                && Convert.ToInt32(period["FeeTypeId"])==Convert.ToInt32(other["FeeTypeId"])
                && (PeriodDate(period,"EffectiveFrom")??DateTime.MinValue)<=(PeriodDate(other,"EffectiveTo")??DateTime.MaxValue)
                && (PeriodDate(other,"EffectiveFrom")??DateTime.MinValue)<=(PeriodDate(period,"EffectiveTo")??DateTime.MaxValue)))
                return $"{CellFeeName(period)} has overlapping fee periods. Review Fee Changes first, or keep current fees. The checkbox cannot resolve conflicting existing periods.";
        }
        foreach(var comparison in rows)
        {
            var periods=existingPeriods.Where(row=>Convert.ToInt32(row["FeeTypeId"])==comparison.FeeTypeId).ToArray();
            bool changed=comparison.Changes || (replaceSchedule.Checked && periods.Any(row=>
                Convert.ToInt32(row["StudentFeeAssignmentId"])!=comparison.CurrentAssignmentId
                && (PeriodDate(row,"EffectiveTo")??DateTime.MaxValue)>=target));
            if(!changed) continue;
            DateTime? billed=periods.Select(row=>PeriodDate(row,"LastBilledMonth")).Max();
            if(billed>=target && comparison.Frequency != "Monthly") return $"{comparison.FeeTypeName} is billed through {billed:MMM yyyy}. Choose a later month or keep current fees.";
            if(!replaceSchedule.Checked && periods.Any(row=>
                Convert.ToInt32(row["StudentFeeAssignmentId"])==comparison.CurrentAssignmentId
                ? PeriodDate(row,"EffectiveFrom")>=target
                : (PeriodDate(row,"EffectiveTo")??DateTime.MaxValue)>=target))
                return $"{comparison.FeeTypeName} has a scheduled rate or waiver. Keep it, or explicitly select Replace scheduled rates / waivers.";
        }
        return string.Empty;
    }
    private static string CellFeeName(DataRow row) => Convert.ToString(row["FeeTypeName"]) ?? "Fee";
}

