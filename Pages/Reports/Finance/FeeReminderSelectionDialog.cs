using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Reports.Finance;

internal sealed class FeeReminderSelectionDialog : ApplicationWindowForm
{
    private readonly DataTable roster;
    private readonly DataGridView students = HumanUi.Grid(editable: true);
    private readonly ModernTextBox message = HumanUi.TextBox(true, 500);
    private readonly ModernTextBox search = HumanUi.TextBox(false, 100);
    private readonly ModernComboBox paper = HumanUi.Combo();
    private readonly ModernDatePicker requestedDate = new() {
        Format = DateTimePickerFormat.Custom, CustomFormat = "dd-MMM-yyyy",
        Value = DateTime.Today, ShowCheckBox = true, Checked = false
    };
    internal IReadOnlySet<int> SelectedStudents => roster.AsEnumerable().Where(row => row.Field<bool>("Include"))
        .Select(row => row.Field<int>("AdmissionId")).ToHashSet();
    internal string Message => message.Text.Trim();
    internal DateTime? RequestedDate => requestedDate.Checked ? requestedDate.Value.Date : null;
    internal bool PrintOnA4 => paper.SelectedIndex == 2;

    internal FeeReminderSelectionDialog(DataTable balances, string scope)
    {
        roster = FeeReminderReportData.CreateRoster(balances);
        paper.Items.AddRange(new object[] { "Choose paper format", "Thermal — separate reminders", "A4 — three columns" });
        paper.SelectedIndex = 0;
        Text = "Fee Reminders"; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        ClientSize = new Size(AppScale.Scale(1000), AppScale.Scale(740));
        MinimumSize = new Size(AppScale.Scale(850), AppScale.Scale(700));
        MinimizeBox = false;
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        students.Dock = DockStyle.Fill;
        students.AutoGenerateColumns = false;
        students.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", DataPropertyName = "Include", HeaderText = "Print", Width = AppScale.Scale(60) });
        foreach (var (field, caption, width) in new[] { ("AdmissionNumber", "Adm. / QR No.", 130),
            ("StudentName", "Student", 220), ("FatherName", "Father", 190), ("Outstanding", "Outstanding (Rs.)", 150) })
            students.Columns.Add(new DataGridViewTextBoxColumn { Name = field, DataPropertyName = field,
                HeaderText = caption, Width = AppScale.Scale(width), ReadOnly = true });
        students.Columns["StudentName"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        students.Columns["Outstanding"].DefaultCellStyle.Format = "N0";
        students.Columns["Outstanding"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        students.DataSource = roster.DefaultView;
        search.PlaceholderText = "Student / father name / admission number";
        var count = new Label { Dock = DockStyle.Fill, AutoSize = true, Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText, Padding = new Padding(4, 6, 0, 0) };
        var preview = new ModernButton { Text = "Print Preview", ThemeRole = "Primary" };
        var cancel = new ModernButton { Text = UiText.Actions.Cancel, ThemeRole = "Secondary", DialogResult = DialogResult.Cancel };
        var all = new ModernButton { Text = "Select All", ThemeRole = "Secondary" };
        var none = new ModernButton { Text = "Clear Selection", ThemeRole = "Secondary" };
        var shown = new ModernButton { Text = "Select Shown", ThemeRole = "Secondary" };
        bool selectingAll = false;
        void UpdateSelection()
        {
            int selected = SelectedStudents.Count;
            count.Text = $"{selected} of {roster.Rows.Count} selected · {roster.DefaultView.Count} shown. Search keeps checked students selected.";
            preview.Enabled = selected > 0 && paper.SelectedIndex > 0;
            shown.Enabled = roster.DefaultView.Count > 0;
        }
        void CommitSelection()
        {
            students.EndEdit();
            if (BindingContext is not null) (BindingContext[roster.DefaultView] as CurrencyManager)?.EndCurrentEdit();
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
        shown.Click += (_, _) => {
            CommitSelection();
            selectingAll = true;
            try { foreach (DataRowView row in roster.DefaultView) row["Include"] = true; }
            finally { selectingAll = false; }
            UpdateSelection();
        };
        search.TextChanged += (_, _) => {
            CommitSelection();
            selectingAll = true;
            try { FeeReminderReportData.ApplySearch(roster, search.Text); }
            finally { selectingAll = false; }
            UpdateSelection();
        };
        paper.SelectedIndexChanged += (_, _) => UpdateSelection();
        students.CurrentCellDirtyStateChanged += (_, _) => { if (students.IsCurrentCellDirty) students.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        students.CellValueChanged += (_, _) => { if (!selectingAll) UpdateSelection(); };
        preview.Click += (_, _) => { CommitSelection(); if (SelectedStudents.Count > 0 && paper.SelectedIndex > 0) DialogResult = DialogResult.OK; };
        var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        options.Controls.Add(HumanUi.Field("Paper format", paper, true), 0, 0);
        options.Controls.Add(HumanUi.Field("Pay by (optional)", requestedDate), 1, 0);
        options.Controls.Add(HumanUi.Field("Message (optional)", message), 2, 0);
        var selection = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selection.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        selection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        selection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var selectionActions = HumanUi.ButtonRow(all, shown, none);
        selectionActions.Dock = DockStyle.Fill;
        selectionActions.Margin = new Padding(0, 0, 0, AppMetrics.SpacingSm);
        selection.Controls.Add(HumanUi.Field("Find Student", search), 0, 0);
        selection.Controls.Add(selectionActions, 0, 1);
        selection.Controls.Add(students, 0, 2);
        selection.Controls.Add(count, 0, 3);
        var studentCard = HumanUi.Card("Students", scope);
        studentCard.Content.Controls.Add(selection);
        studentCard.Margin = new Padding(0, 0, 0, AppMetrics.SpacingSm);
        var optionsCard = HumanUi.Card("Reminder Details", "Choose a paper format. Balances refresh before preview.");
        optionsCard.Content.Controls.Add(options);
        var actions = HumanUi.ButtonRow(preview, cancel);
        actions.Dock = DockStyle.Fill;
        // The grid handles its own scrolling; let this layout shrink with the
        // dialog instead of wrapping its initial size in a fixed-size viewport.
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true,
            ColumnCount = 1, RowCount = 3, Padding = new Padding(AppMetrics.SpacingMd) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.Scale(190)));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(studentCard, 0, 0);
        root.Controls.Add(optionsCard, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root); CancelButton = cancel;
        FormFlow.OrderTableFields(root); UpdateSelection();
    }
}
