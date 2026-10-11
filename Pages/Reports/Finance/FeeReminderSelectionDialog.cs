using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Reports.Finance;

internal sealed class FeeReminderSelectionDialog : ApplicationWindowForm
{
    private readonly DataTable roster;
    private readonly DataGridView students = HumanUi.Grid(editable: true);
    private readonly ModernTextBox message = HumanUi.TextBox(true, 500);
    private readonly ModernComboBox paper = HumanUi.Combo();
    private readonly ModernDatePicker requestedDate = new() {
        Format = DateTimePickerFormat.Custom, CustomFormat = "dd-MMM-yyyy",
        Value = DateTime.Today, ShowCheckBox = true, Checked = false
    };
    internal IReadOnlySet<int> SelectedStudents => roster.AsEnumerable().Where(row => row.Field<bool>("Include"))
        .Select(row => row.Field<int>("AdmissionId")).ToHashSet();
    internal string Message => message.Text.Trim();
    internal DateTime? RequestedDate => requestedDate.Checked ? requestedDate.Value.Date : null;
    internal bool PrintOnA4 => FeeReminderPrintLayout.UseA4(paper.SelectedIndex, SelectedStudents.Count);

    internal FeeReminderSelectionDialog(DataTable balances, string scope)
    {
        roster = FeeReminderReportData.CreateRoster(balances);
        paper.Items.AddRange(new object[] { "Automatic (up to 10: thermal)", "Thermal — separate reminders", "A4 — three columns" });
        paper.SelectedIndex = 0;
        Text = "Fee Reminders"; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        Size = new Size(AppScale.Scale(950), AppScale.Scale(650)); MinimumSize = new Size(760, 520);
        BackColor = AppTheme.PageBackground;
        students.AutoGenerateColumns = false;
        students.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", DataPropertyName = "Include", HeaderText = "Print", Width = AppScale.Scale(60) });
        foreach (var (field, caption, width) in new[] { ("AdmissionNumber", "Admission Number", 130),
            ("StudentName", "Student", 220), ("FatherName", "Father", 190), ("Outstanding", "Outstanding", 130) })
            students.Columns.Add(new DataGridViewTextBoxColumn { Name = field, DataPropertyName = field,
                HeaderText = caption, Width = AppScale.Scale(width), ReadOnly = true });
        students.Columns["StudentName"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        students.Columns["Outstanding"].DefaultCellStyle.Format = "N2";
        students.Columns["Outstanding"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        students.DataSource = roster;
        var count = new Label { Dock = DockStyle.Fill, AutoSize = true, Font = AppTheme.NormalFont };
        var preview = new ModernButton { Text = "Print Preview", ThemeRole = "Primary" };
        var cancel = new ModernButton { Text = UiText.Actions.Cancel, ThemeRole = "Secondary", DialogResult = DialogResult.Cancel };
        var all = new ModernButton { Text = "Select All", ThemeRole = "Secondary" };
        var none = new ModernButton { Text = "Clear Selection", ThemeRole = "Secondary" };
        bool selectingAll = false;
        void UpdateSelection()
        {
            int selected = SelectedStudents.Count;
            string format = PrintOnA4 ? "A4, three columns" : "Thermal, separate reminders";
            count.Text = $"{selected} of {roster.Rows.Count} students selected. {format}. Balances refresh before preview.";
            preview.Enabled = selected > 0;
        }
        void CommitSelection()
        {
            students.EndEdit();
            if (BindingContext is not null) (BindingContext[roster] as CurrencyManager)?.EndCurrentEdit();
        }
        void SelectAll(bool include)
        {
            CommitSelection();
            selectingAll = true;
            try { foreach (DataRow row in roster.Rows) row["Include"] = include; }
            finally { selectingAll = false; }
            UpdateSelection();
        }
        all.Click += (_, _) => SelectAll(true); none.Click += (_, _) => SelectAll(false);
        paper.SelectedIndexChanged += (_, _) => UpdateSelection();
        students.CurrentCellDirtyStateChanged += (_, _) => { if (students.IsCurrentCellDirty) students.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        students.CellValueChanged += (_, _) => { if (!selectingAll) UpdateSelection(); };
        preview.Click += (_, _) => { CommitSelection(); if (SelectedStudents.Count > 0) DialogResult = DialogResult.OK; };
        var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AppScale.Scale(270)));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AppScale.Scale(200)));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        options.Controls.Add(HumanUi.Field("Paper format", paper), 0, 0);
        options.Controls.Add(HumanUi.Field("Pay by (optional)", requestedDate), 1, 0);
        options.Controls.Add(HumanUi.Field("Message (optional)", message), 2, 0);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(AppMetrics.SpacingMd) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.TextHeight(AppTheme.NormalFont) + 12));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight * 2));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
        root.Controls.Add(new Label { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 0, 0, AppMetrics.SpacingSm), Font = AppTheme.NormalFont,
            Text = scope + "\nChoose students and paper format. Automatic uses A4 above 10 students. Printing does not change charges or due dates." }, 0, 0);
        root.Controls.Add(HumanUi.ButtonRow(all, none), 0, 1); root.Controls.Add(students, 0, 2);
        root.Controls.Add(count, 0, 3); root.Controls.Add(options, 0, 4); root.Controls.Add(HumanUi.ButtonRow(preview, cancel), 0, 5);
        Controls.Add(root); CancelButton = cancel;
        FormFlow.OrderTableFields(root); UpdateSelection();
    }
}
