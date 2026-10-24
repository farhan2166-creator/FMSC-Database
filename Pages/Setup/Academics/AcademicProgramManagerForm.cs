using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Setup.Academics;
public sealed class AcademicProgramManagerForm : ApplicationWindowForm, IManagedLookupDialog
{
    private readonly ModernComboBox existing = HumanUi.Combo();
    private readonly ModernComboBox programType = HumanUi.Combo();
    /* UX - what is missing is marked on the fields, all at once, not shown in message boxes. */
    private readonly FormValidator validator = new();
    private readonly ModernTextBox code = HumanUi.TextBox(false, 30);
    private readonly ModernTextBox name = HumanUi.TextBox(false, 150);
    private readonly ModernButton add;
    private readonly ModernButton save;
    private readonly ModernButton cancel;
    public int SelectedId { get; private set; }
    public string? SelectedParentValue { get; private set; }
    /* Prevent a queued click from repeating a completed save. */
    private bool savedSuccessfully;
    public AcademicProgramManagerForm()
    {
        Text = "Manage Programs";
        RecordTextPolicy.WireInput(name, "@ProgramName");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(AppScale.Scale(560), AppScale.Scale(420));
        BackColor = AppTheme.PageBackground;
        Font = AppTheme.NormalFont;
        var title = new Label {
            Text = "Programs",
            Dock = DockStyle.Top,
            Height = Math.Max(
                30,
                AppMetrics.TextHeight(AppTheme.PageTitleFont) + 8),
            Font = AppTheme.PageTitleFont,
            ForeColor = AppTheme.Text,
            Padding = new Padding(0, 2, 0, 0),
            TabStop = false
        };
        var subtitle = new Label {
            Text = "Define a new program or edit an existing program.",
            Dock = DockStyle.Top,
            Height = Math.Max(
                24,
                AppMetrics.TextHeight(AppTheme.NormalFont) + 8),
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText,
            TabStop = false
        };
        programType.Items.AddRange(
            new object[] {
                "School",
                "College"
            });
        existing.TabIndex = 0;
        programType.TabIndex = 1;
        code.TabIndex = 2;
        name.TabIndex = 3;
        foreach (Control input in new Control[] { existing, programType, code, name })
        {
            input.Dock = DockStyle.None;
            input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            input.Height = AppMetrics.InputControlHeight;
        }
        add = new ModernButton {
            Text = UiText.Actions.New,
            Width = AppScale.Scale(105),
            Height = AppMetrics.ButtonHeight,
            ThemeRole = "Secondary",
            TabIndex = 6
        };
        save = new ModernButton {
            Text = "Save / Update",
            Width = AppScale.Scale(125),
            Height = AppMetrics.ButtonHeight,
            ThemeRole = "Success",
            TabIndex = 7
        };
        cancel = new ModernButton {
            Text = UiText.Actions.Close,
            Width = AppScale.Scale(105),
            Height = AppMetrics.ButtonHeight,
            ThemeRole = "Secondary",
            TabIndex = 8
        };
        /* Cancel closes immediately when no save is running. */
        cancel.Click += (_, _) => {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        var fields = new TableLayoutPanel {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(0, 8, 0, 8),
            TabStop = false
        };
        fields.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 30));
        fields.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 70));
        int fieldRowHeight =
            Math.Max(
                42,
                AppMetrics.InputControlHeight + 4);
        for (int i = 0; i < 4; i++)
        {
            fields.RowStyles.Add(
                new RowStyle(SizeType.Absolute, fieldRowHeight));
        }
        fields.Height = fields.Padding.Vertical + (4 * fieldRowHeight);
        fields.Margin = Padding.Empty;
        fields.Controls.Add(LabelFor("Existing Program"), 0, 0);
        fields.Controls.Add(existing, 1, 0);
        fields.Controls.Add(LabelFor("Program Type"), 0, 1);
        fields.Controls.Add(programType, 1, 1);
        fields.Controls.Add(LabelFor("Code"), 0, 2);
        fields.Controls.Add(code, 1, 2);
        fields.Controls.Add(LabelFor("Program Name"), 0, 3);
        fields.Controls.Add(name, 1, 3);
        existing.SelectedIndexChanged +=
            Existing_SelectedIndexChanged;
        add.Click += (_, _) =>
            BeginNewProgram();
        save.Click += (_, _) =>
            SaveProgram();
        var buttons =
            HumanUi.ButtonRow(add, save, cancel);
        buttons.Dock = DockStyle.Top;
        buttons.TabStop = false;
        int buttonRowHeight = buttons.GetPreferredSize(Size.Empty).Height + buttons.Margin.Vertical;
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(22),
            TabStop = false
        };
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, title.Height));
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, subtitle.Height));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(
            new RowStyle(SizeType.AutoSize));
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(subtitle, 0, 1);
        root.Controls.Add(fields, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);
        int requiredClientHeight =
            root.Padding.Vertical +
            title.Height +
            subtitle.Height +
            fields.Height +
            title.Margin.Vertical +
            subtitle.Margin.Vertical +
            buttonRowHeight;
        ClientSize =
            new Size(
                AppScale.Scale(560),
                Math.Max(AppScale.Scale(420), requiredClientHeight));
        AcceptButton = save;
        CancelButton = cancel;
        FormFlow.EnterMovesToNext(this);
        Disposed += (_, _) => validator.Dispose();
        TabStop = true;
        LoadPrograms();
        BeginNewProgram();
    }
    private static Label LabelFor(string text) => new() {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.Text,
        Padding = new Padding(0, 0, 8, 0),
        TabStop = false
    };
    private void LoadPrograms()
    {
        var dt = DatabaseHelper.QueryUser(
            "GetAcademicPrograms");
        suppressProgramSelection = true;
        try
        {
            existing.DisplayMember =
                "ProgramName";
            existing.ValueMember =
                "AcademicProgramId";
            existing.DataSource =
                dt;
            existing.SelectedIndex =
                -1;
        }
        finally
        {
            suppressProgramSelection = false;
        }
    }
    private void LoadSelected()
    {
        if (existing.SelectedIndex < 0 ||
            existing.SelectedItem is not DataRowView row)
        {
            return;
        }
        string type =
            Convert.ToString(
                row["ProgramType"]) ?? string.Empty;
        int typeIndex =
            programType.Items.IndexOf(type);
        programType.SelectedIndex =
            typeIndex >= 0
                ? typeIndex
                : -1;
        code.Text =
            Convert.ToString(
                row["ProgramCode"]) ?? string.Empty;
        name.Text =
            Convert.ToString(
                row["ProgramName"]) ?? string.Empty;
        SelectedId =
            Convert.ToInt32(row["AcademicProgramId"]);
    }
    private void BeginNewProgram()
    {
        // New explicitly discards the small program editor.
        existing.SelectedIndexChanged -=
            Existing_SelectedIndexChanged;
        try
        {
            existing.SelectedIndex = -1;
        }
        finally
        {
            existing.SelectedIndexChanged +=
                Existing_SelectedIndexChanged;
        }
        SelectedId = 0;
        SelectedParentValue = null;
        programType.SelectedIndex = -1;
        code.Clear();
        name.Clear();
        programType.Focus();
    }
    /* Set while LoadPrograms binds the list: that bind raises Existing_SelectedIndexChanged programmatically and must not prompt. */
    private bool suppressProgramSelection;
    private void Existing_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (suppressProgramSelection)
            return;
        LoadSelected();
    }
    private void SaveProgram()
    {
        /* UX - a click/Enter that was already queued when the save finished must not save twice. */
        if (savedSuccessfully)
            return;
        validator.Begin();
        validator.Required(programType, UiText.Validation.SelectOne);
        validator.Required(name, UiText.Validation.Required);
        RecordTextPolicy.ValidateInputs(validator, this);
        if (!validator.Finish())
            return;
        string type =
            Convert.ToString(
                programType.SelectedItem) ?? string.Empty;
        try
        {
            int id =
                SelectedId;
            DatabaseHelper.Execute(
                "SaveAcademicProgram",
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@AcademicProgramId",
                    id),
                DatabaseHelper.P(
                    "@ProgramType",
                    type),
                DatabaseHelper.P("@ProgramCode", string.IsNullOrWhiteSpace(code.Text)
                        ? null
                        : code.Text.Trim()),
                DatabaseHelper.P(
                    "@ProgramName",
                    name.Text.Trim()),
                DatabaseHelper.Int(
                    "@DisplayOrder",
                    0));
            if (id == 0)
            {
                var dt =
                    DatabaseHelper.QueryUser(
                        "GetAcademicPrograms");
                var match =
                    dt.AsEnumerable()
                        .Where(r =>
                            string.Equals(
                                Convert.ToString(
                                    r["ProgramName"]),
                                name.Text.Trim(),
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(
                                Convert.ToString(r["ProgramType"]), type, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(r =>
                            Convert.ToInt32(r["AcademicProgramId"]))
                        .FirstOrDefault();
                if (match != null)
                {
                    SelectedId =
                        Convert.ToInt32(match["AcademicProgramId"]);
                }
            }
            SelectedParentValue =
                type;
            savedSuccessfully = true;
            DialogResult =
                DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            ModernMessageDialog.Show(this, DbMessage.UserMessage(ex), "Program", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            /* UX - a refusal is usually a duplicate name: put the cursor there. */
            name.Focus();
        }
    }
}

