using System.Globalization;
using System.Text.RegularExpressions;
namespace FMSC_SMS.Core;
public static class HumanUi
{
    public static HumanCard Card(
        string title,
        string? subtitle = null,
        Color? accent = null,
        Control? accessory = null) =>
        new(title, subtitle, accent, accessory);
    /* The student pickers filter an in-memory list (StudentSearchCache), so there is no database round trip to wait out. */
    public const int SearchDebounceMs = 120;
    public static System.Windows.Forms.Timer CreateSearchDebounceTimer(Action onSearch, int intervalMs = SearchDebounceMs)
    {
        var timer = new System.Windows.Forms.Timer {
            Interval = intervalMs
        };
        timer.Tick += (_, _) => {
            timer.Stop();
            onSearch();
        };
        return timer;
    }
    public static string StudentDisplay(
        int admissionId,
        string? studentName,
        string? fatherName,
        string? gender = null,
        string? registrationNumber = null,
        string? className = null,
        string? sectionName = null)
    {
        string name = (studentName ?? string.Empty).Trim();
        string father = (fatherName ?? string.Empty).Trim();
        string registration = (registrationNumber ?? string.Empty).Trim();
        string cls = (className ?? string.Empty).Trim();
        string section = (sectionName ?? string.Empty).Trim();
        string relation = gender?.Trim() switch {
            string value when value.Equals("Male", StringComparison.OrdinalIgnoreCase) => "s/o",
            string value when value.Equals("Female", StringComparison.OrdinalIgnoreCase) => "d/o",
            _ => "child of"
        };
        string display =
            string.IsNullOrWhiteSpace(father)
                ? $"{AdmissionNumbers.ForId(admissionId)} - {name}"
                : $"{AdmissionNumbers.ForId(admissionId)} - {name} {relation} {father}";
        /* The registration number is a reporting label, not part of how an operator identifies a student, so it is no longer appended here. */
        _ = registration;
        // Disambiguates same-named siblings/twins (H1) in every quick-pick dropdown, not just the dedicated Student Search dialog.
        string classSection =
            string.IsNullOrWhiteSpace(cls)
                ? string.Empty
                : string.IsNullOrWhiteSpace(section)
                    ? cls
                    : $"{cls} - {section}";
        if (!string.IsNullOrWhiteSpace(classSection))
            display += $" ({classSection})";
        return display;
    }
    /// <summary>Returns matching source rows without cloning the source table.</summary>
    public static List<DataRow> FindStudentMatches(DataTable source, string? searchText, int maxRows = 0, Func<DataRow, bool>? additionalFilter = null)
        => StudentSearchMatcher.Find(source, searchText, maxRows, additionalFilter);
    /// <summary>Filters students into a DataTable for callers that require one.</summary>
    public static DataTable FilterStudentsByAdmissionIdOrName(DataTable source, string? searchText, int maxRows = 0)
    {
        List<DataRow> matches = FindStudentMatches(source, searchText, maxRows);
        DataTable result = source.Clone();
        foreach (DataRow row in matches)
            result.ImportRow(row);
        return result;
    }
    /// <summary>Position of the student whose admission id is exactly <paramref name="term"/> in a LookupBox filled from a student list (position 0 is the blank row), or -1.</summary>
    public static int IndexOfAdmissionId(LookupBox box, string term)
    {
        if (!AdmissionNumbers.TryNormalize(term, out string wanted))
            return -1;
        for (int i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is DataRowView view &&
                view.DataView.Table is { } table &&
                table.Columns.Contains("AdmissionNumber") &&
                view["AdmissionNumber"] != DBNull.Value &&
                string.Equals(Convert.ToString(view["AdmissionNumber"]), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }
    /// <summary>Presentation only: it holds no state and takes no part in any form's data.</summary>
    public static Label GroupHeading(string text)
    {
        var label = new Label {
            Text = text,
            Dock = DockStyle.Fill,
            Font = UiDesignSystem.Typography.Caption,
            ForeColor = AppTheme.Primary,
            TextAlign = ContentAlignment.BottomLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
            Margin = new Padding(0),
            Padding = new Padding(UiDesignSystem.Spacing.Xs, 0, 0, 2),
            /* Re-tinted with the theme's accent when the theme changes. */
            Tag = "ThemeAccentText"
        };
        label.Paint += (_, e) => {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawLine(pen, 0, label.Height - 1, label.Width, label.Height - 1);
        };
        return label;
    }
    /// <summary>Presentation only: dims the caption of a field made by <see cref="Field"/> so a record-keeping reference (BISE enrolment, registration number) reads as secondary.</summary>
    public static void MuteFieldCaption(Panel field)
    {
        foreach (Control control in field.Controls)
        {
            if (control is Label label &&
                Equals(label.Tag, UiHelper.FieldCaptionTag))
            {
                label.ForeColor = AppTheme.MutedText;
                label.Tag = "ThemeMutedText";
                return;
            }
        }
    }
    /// <summary>Row height that fits <see cref="GroupHeading"/> with a little air above the fields.</summary>
    public static int GroupHeadingHeight =>
        Math.Max(16, AppMetrics.TextHeight(UiDesignSystem.Typography.Caption)) + UiDesignSystem.Spacing.Sm + 2;
    public static Panel Field(string caption, Control control, bool required = false, bool fillHeight = true)
    {
        var panel = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 0, UiDesignSystem.Spacing.Sm, UiDesignSystem.Spacing.Sm),
            Margin = new Padding(0)
        };
        int captionHeight = Math.Max(16, AppMetrics.TextHeight(UiDesignSystem.Typography.Caption));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, captionHeight));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label {
            Text = required ? caption + " *" : caption,
            Dock = DockStyle.Fill,
            Font = UiDesignSystem.Typography.Caption,
            ForeColor = AppTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            /* No bottom padding: the label is exactly one line tall, so padding under the text left less than a line and clipped the descenders (Campus, (optional)). */
            Padding = new Padding(UiDesignSystem.Spacing.Xs, 0, 0, 0),
            Margin = Padding.Empty,
            Tag = UiHelper.FieldCaptionTag
        }, 0, 0);
        ApplyFieldInputLayout(control, fillHeight);
        /* Keep required-state metadata on the input so shared validation can discover it. */
        UiAccessibility.Describe(
            control,
            string.IsNullOrWhiteSpace(control.AccessibleName)
                ? UiAccessibility.CleanCaption(caption)
                : null,
            required ? "Required" : null);
        panel.Controls.Add(control, 0, 1);
        return panel;
    }
    /// <summary>One sizing rule for field builders: the container owns spacing,
    /// single-line inputs retain their standard height, and multiline content may grow.</summary>
    internal static void ApplyFieldInputLayout(Control control, bool fillHeight = true)
    {
        control.Margin = Padding.Empty;
        // Single-line inputs (combo, date, numeric, one-line text) keep the standard input height. Only multiline text and non-input controls fill.
        bool singleLineInput =
            control is ModernComboBox ||
            control is ModernDatePicker ||
            control is StudentPicker ||
            control is ModernTextBox { Multiline: false };
        // These fixed-purpose fields should not grow to a monitor-wide entry
        // box. The cap follows the existing font, not a resolution breakpoint.
        if (control is ModernComboBox or ModernDatePicker or ModernNumericTextBox && control.MaximumSize.Width == 0)
            control.MaximumSize = new Size(AppMetrics.TextWidth(new string('M', 24), AppTheme.NormalFont,
                AppScale.Scale(40)), control.MaximumSize.Height);
        // Applying a width-only MaximumSize can reset a panel's specified
        // height to zero. Restore input height before docking and deriving its minimum.
        if (singleLineInput) control.Height = AppMetrics.InputControlHeight;
        if (fillHeight && !singleLineInput)
        {
            control.Dock = DockStyle.Fill;
        }
        else
        {
            control.MinimumSize = new Size(control.MinimumSize.Width,
                Math.Max(control.MinimumSize.Height, control.Height));
            control.Dock = DockStyle.Top;
        }
    }
    public static ModernTextBox TextBox(bool multiline = false, int maxLength = 1000) => new() {
        Multiline = multiline,
        ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
        MaxLength = maxLength,
        Height = multiline ? AppMetrics.MultilineInputHeight(78) : AppMetrics.InputControlHeight,
        Dock = DockStyle.Fill
    };
    public static TableLayoutPanel ButtonRow(params Control[] buttons)
        => BuildButtonRow(buttons, preserveOrder: false);
    internal static TableLayoutPanel ButtonRowInOrder(params Control[] buttons)
        => BuildButtonRow(buttons, preserveOrder: true);
    private static TableLayoutPanel BuildButtonRow(Control[] buttons, bool preserveOrder)
    {
        if (!preserveOrder) buttons = ActionButtonStyle.Ordered(buttons);
        var row = new TableLayoutPanel {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, UiDesignSystem.Spacing.Md, 0, UiDesignSystem.Spacing.Xs),
            Padding = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var flow = new FlowLayoutPanel {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Margin = new Padding(0)
        };
        int minimumButtonWidth = AppScale.Scale(110);
        int buttonWidth = buttons.Length == 0
            ? 0
            : buttons.Max(button => Math.Max(
                minimumButtonWidth,
                Math.Max(button.Width, AppMetrics.TextWidth(button.Text, button.Font, AppScale.Scale(32)))));
        foreach (Control button in buttons)
        {
            if (button is ModernButton)
                button.Height = Math.Max(AppMetrics.ButtonHeight, AppMetrics.TextHeight(button.Font) + AppScale.Scale(8));
            if (buttonWidth > 0)
                button.Width = buttonWidth;
            flow.Controls.Add(button);
        }
        if (!preserveOrder) ActionButtonStyle.Arrange(flow);
        else for (int i = 0; i < buttons.Length; i++) buttons[i].TabIndex = i;
        row.Controls.Add(flow, 1, 0);
        void FitButtons()
        {
            int available = Math.Max(1, row.ClientSize.Width - row.Padding.Horizontal);
            if (flow.MaximumSize.Width != available)
                flow.MaximumSize = new Size(available, 0);
            int natural = buttons.Where(button => button.Visible || !row.Visible).Sum(button => button.Width + button.Margin.Horizontal);
            int minimum = Math.Min(available, natural);
            if (flow.MinimumSize.Width != minimum) flow.MinimumSize = new Size(minimum, 0);
        }
        row.SizeChanged += (_, _) => FitButtons();
        row.VisibleChanged += (_, _) => FitButtons();
        foreach (Control button in buttons)
        {
            button.VisibleChanged += (_, _) => FitButtons();
            button.SizeChanged += (_, _) => FitButtons();
        }
        bool adjusting = false;
        float? baselineHeight = null;
        row.Layout += (_, _) => {
            if (adjusting || row.Parent?.Parent is not HumanCard card || card.Parent is not TableLayoutPanel root) return;
            int index = root.GetRow(card);
            if (index < 0 || index >= root.RowStyles.Count || root.RowStyles[index].SizeType != SizeType.Absolute) return;
            baselineHeight ??= root.RowStyles[index].Height;
            // Certificate editors can change their number of field rows when
            // resized. Do not restore the initial, taller editor height afterward.
            var certificateFields=card.Content.Controls.OfType<TableLayoutPanel>()
                .FirstOrDefault(table=>Equals(table.Tag,CertificateUi.FieldGridTag));
            float baseline=certificateFields is null?baselineHeight.Value:
                AppMetrics.FieldCardHeight(certificateFields.RowCount)+AppMetrics.ButtonRowHeight;
            float needed = baseline + Math.Max(0, row.Height + row.Margin.Vertical - AppMetrics.ButtonRowHeight);
            if (Math.Abs(root.RowStyles[index].Height - needed) < 1) return;
            adjusting = true;
            try { root.RowStyles[index].Height = needed; }
            finally { adjusting = false; }
        };
        return row;
    }
    public static ModernComboBox Combo() => new() {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Height = AppMetrics.InputControlHeight,
        Dock = DockStyle.None,
        Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
    };
    public static void BindCurrentAcademicYear(LookupBox combo, DataTable table)
    {
        combo.Enabled = false;
        combo.TabStop = false;
        DataTable currentYears = table.Clone();
        DataRow? current = table.Columns.Contains("IsCurrent")
            ? table.AsEnumerable().FirstOrDefault(row =>
                !row.IsNull("IsCurrent") && Convert.ToBoolean(row["IsCurrent"]))
            : null;
        if (current is not null) currentYears.ImportRow(current);
        combo.DataSource = null;
        combo.KeyColumn = "AcademicYearId";
        combo.DisplayMember = "YearName";
        combo.ValueMember = "AcademicYearId";
        combo.DataSource = currentYears;
        combo.SelectedIndex = currentYears.Rows.Count == 1 ? 0 : -1;
    }
    public static void SelectCurrentAcademicYear(ModernComboBox combo, DataTable table)
    {
        if (combo is null || table is null ||
            !table.Columns.Contains("AcademicYearId") ||
            !table.Columns.Contains("IsCurrent"))
        {
            combo!.SelectedIndex = -1; // Every caller passes a live combo; this keeps the existing behaviour exactly.
            return;
        }
        DataRow? currentYear = table.AsEnumerable()
            .FirstOrDefault(row =>
                row["AcademicYearId"] != DBNull.Value &&
                Convert.ToInt32(row["AcademicYearId"]) > 0 &&
                row["IsCurrent"] != DBNull.Value &&
                Convert.ToBoolean(row["IsCurrent"]));
        combo.SelectedValue = currentYear?["AcademicYearId"] ?? 0;
    }
    public static ModernNumericTextBox Number(int max = 100000000) => new() {
        Minimum = 0,
        Maximum = max,
        Height = AppMetrics.InputControlHeight,
        Dock = DockStyle.Fill,
        Font = AppTheme.NormalFont
    };
    public static ModernDatePicker Date() => new ModernDatePicker {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "dd-MMM-yyyy",
        Height = AppMetrics.InputControlHeight,
        Dock = DockStyle.Fill,
        Font = AppTheme.NormalFont
    };
    public static DataGridView Grid(bool editable = false)
    {
        var grid = new KeyboardDataGridView {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = !editable,
            RowHeadersVisible = false,
            BorderStyle = BorderStyle.None,
            BackgroundColor = AppTheme.Surface,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            EnableHeadersVisualStyles = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            ColumnHeadersHeight = AppMetrics.GridHeaderHeight,
            RowTemplate = { Height = AppMetrics.GridRowHeight }
        };
        ApplyGridStyle(grid, AppTheme.Primary);
        return grid;
    }
    public static void ApplyGridStyle(DataGridView grid, Color accent)
    {
        grid.BackgroundColor = AppTheme.Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = AppTheme.Border;
        grid.EnableHeadersVisualStyles = false;
        grid.RowHeadersVisible = false;
        grid.AllowUserToResizeRows = false;
        UiHelper.ApplyGridHeaderStyle(grid, accent);
        grid.ColumnHeadersDefaultCellStyle.Padding =
            new Padding(UiDpi.Metric(grid, AppMetrics.SpacingSm), 0, UiDpi.Metric(grid, AppMetrics.SpacingSm), 0);
        grid.DefaultCellStyle.Font = UiDpi.ForControl(grid, AppTheme.NormalFont);
        grid.DefaultCellStyle.ForeColor = AppTheme.Text;
        grid.DefaultCellStyle.BackColor = AppTheme.Surface;
        grid.DefaultCellStyle.SelectionBackColor = AppTheme.Tint(accent, 0.88f);
        grid.DefaultCellStyle.SelectionForeColor = AppTheme.Text;
        grid.DefaultCellStyle.Padding =
            new Padding(UiDpi.Metric(grid, AppMetrics.SpacingSm), 0, UiDpi.Metric(grid, AppMetrics.SpacingSm), 0);
        grid.AlternatingRowsDefaultCellStyle.BackColor = AppTheme.SurfaceSecondary;
        UiHelper.ApplyGridPresentation(grid);
    }
    public static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var s = Regex.Replace(value, "([a-z])([A-Z])", "$1 $2").Replace("_", " ");
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s);
    }
}
