using System.Globalization;

namespace FMSC_SMS.Core;

/// <summary>A calendar with explicit year and month navigation for distant dates.
/// Navigation stays local until a day is chosen or Use Date is pressed.</summary>
internal sealed class DateSelectionPanel : UserControl
{
    private readonly TextBox year;
    private readonly ComboBox month;
    private readonly MonthCalendar calendar;
    private readonly Label selectedDate;
    private bool updating;
    internal event Action<DateTime>? DateChosen;
    internal event Action? DateCleared;

    internal DateSelectionPanel(DateTime value, DateTime minimum, DateTime maximum, bool optional)
    {
        AutoScaleMode = AutoScaleMode.None;
        Font = AppTheme.NormalFont;
        BackColor = AppTheme.Surface;
        ForeColor = AppTheme.Text;
        Padding = new Padding(AppMetrics.SpacingMd);
        year = new TextBox {
            Text = value.Year.ToString(CultureInfo.CurrentCulture), MaxLength = 4,
            Dock = DockStyle.Fill, Font = AppTheme.NormalFont,
            AccessibleName = "Year",
            AccessibleDescription = "Type the year directly, for example 1985."
        };
        month = new ComboBox {
            DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill,
            Font = AppTheme.NormalFont, AccessibleName = "Month",
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = AppMetrics.TextHeight(AppTheme.NormalFont) + 4
        };
        month.DrawItem += (_, e) => {
            e.DrawBackground();
            if (e.Index >= 0)
                TextRenderer.DrawText(e.Graphics, Convert.ToString(month.Items[e.Index]), month.Font,
                    e.Bounds, e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        };
        month.Items.AddRange(Enumerable.Range(1, 12)
            .Select(m => (object)CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m)).ToArray());
        month.SelectedIndex = value.Month - 1;
        calendar = new MonthCalendar {
            MaxSelectionCount = 1, MinDate = minimum.Date, MaxDate = maximum.Date,
            ShowToday = false, ShowTodayCircle = true,
            Margin = Padding.Empty, Anchor = AnchorStyles.None,
            BackColor = AppTheme.Surface, ForeColor = AppTheme.Text,
            TitleBackColor = AppTheme.Primary, TitleForeColor = Color.White,
            AccessibleName = "Day calendar"
        };
        calendar.SetDate(value.Date);
        selectedDate = new Label {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            Font = AppTheme.ButtonFont, ForeColor = AppTheme.Text, Margin = Padding.Empty
        };
        UpdateSummary();
        var navigation = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty
        };
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.TextHeight(Font) + 4));
        navigation.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        navigation.Controls.Add(new Label { Text = "Year", Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
        navigation.Controls.Add(new Label { Text = "Month", Dock = DockStyle.Fill, Margin = Padding.Empty }, 1, 0);
        navigation.Controls.Add(year, 0, 1);
        navigation.Controls.Add(month, 1, 1);
        year.Margin = new Padding(0, 0, AppMetrics.SpacingSm, 0);
        month.Margin = Padding.Empty;

        var today = new ModernButton {
            Text = "Today", ThemeRole = "Secondary", Dock = DockStyle.Fill,
            Enabled = DateTime.Today >= minimum.Date && DateTime.Today <= maximum.Date
        };
        var useDate = new ModernButton { Text = "Use Date", ThemeRole = "Primary", Dock = DockStyle.Fill };
        var actions = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = optional ? 3 : 2, RowCount = 1, Margin = Padding.Empty
        };
        for (int i = 0; i < actions.ColumnCount; i++)
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / actions.ColumnCount));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        actions.Controls.Add(today, 0, 0);
        if (optional)
        {
            var clear = new ModernButton { Text = "Clear", ThemeRole = "Secondary", Dock = DockStyle.Fill };
            clear.Click += (_, _) => DateCleared?.Invoke();
            actions.Controls.Add(clear, 1, 0);
        }
        actions.Controls.Add(useDate, actions.ColumnCount - 1, 0);
        foreach (Control button in actions.Controls) button.Margin = new Padding(2);
        var hint = new Label {
            Text = "Type a year, choose a month, then pick a day. Enter applies; Esc cancels.",
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty
        };
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.TextHeight(Font) + AppMetrics.ControlHeight + 8));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, calendar.SingleMonthSize.Height + AppMetrics.SpacingSm));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.TextHeight(selectedDate.Font) + 8));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight + 4));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.TextHeight(hint.Font) * 2 + 4));
        layout.Controls.Add(navigation, 0, 0);
        layout.Controls.Add(calendar, 0, 1);
        layout.Controls.Add(selectedDate, 0, 2);
        layout.Controls.Add(actions, 0, 3);
        layout.Controls.Add(hint, 0, 4);
        Controls.Add(layout);
        Size = new Size(Math.Max(AppScale.Scale(360), calendar.SingleMonthSize.Width + Padding.Horizontal),
            (int)layout.RowStyles.Cast<RowStyle>().Sum(row => row.Height) + Padding.Vertical);

        year.TextChanged += (_, _) => {
            if (year.Text.Length == 4 && ValidYear(out _)) Navigate();
        };
        month.SelectedIndexChanged += (_, _) => Navigate();
        calendar.DateChanged += (_, _) => {
            if (updating) return;
            updating = true;
            try { year.Text = calendar.SelectionStart.Year.ToString(CultureInfo.CurrentCulture); month.SelectedIndex = calendar.SelectionStart.Month - 1; }
            finally { updating = false; }
            UpdateSummary();
        };
        calendar.DateSelected += (_, e) => DateChosen?.Invoke(e.Start.Date);
        useDate.Click += (_, _) => {
            if (!CommitYear()) return;
            Navigate();
            DateChosen?.Invoke(calendar.SelectionStart.Date);
        };
        today.Click += (_, _) => DateChosen?.Invoke(DateTime.Today);
    }
    private bool CommitYear()
    {
        if (!ValidYear(out _))
        {
            year.Focus(); year.Select(0, year.Text.Length);
            selectedDate.Text = $"Enter a year from {calendar.MinDate.Year} to {calendar.MaxDate.Year}.";
            return false;
        }
        return true;
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter && !month.DroppedDown)
        {
            if (CommitYear())
            {
                Navigate();
                DateChosen?.Invoke(calendar.SelectionStart.Date);
            }
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private bool ValidYear(out int entered) =>
        int.TryParse(year.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out entered)
        && entered >= calendar.MinDate.Year && entered <= calendar.MaxDate.Year;
    internal void FocusYear()
    {
        year.Focus(); year.SelectAll();
    }
    private void Navigate()
    {
        if (updating || month.SelectedIndex < 0 || !ValidYear(out int y)) return;
        int m = month.SelectedIndex + 1;
        DateTime date = new(y, m, Math.Min(calendar.SelectionStart.Day, DateTime.DaysInMonth(y, m)));
        date = date < calendar.MinDate ? calendar.MinDate : date > calendar.MaxDate ? calendar.MaxDate : date;
        updating = true;
        try {
            calendar.SetDate(date);
            year.Text = date.Year.ToString(CultureInfo.CurrentCulture);
            month.SelectedIndex = date.Month - 1;
        }
        finally { updating = false; }
        UpdateSummary();
    }
    private void UpdateSummary() => selectedDate.Text = calendar.SelectionStart.ToString("dd MMMM yyyy", CultureInfo.CurrentCulture);
}
