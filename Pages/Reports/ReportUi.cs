using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Reports;
internal static class ReportUi
{
    /// <summary>UX - the shared "From must not be after To" check for the dated reports, shown on the To picker itself ("Earlier than From") instead of in a message box.</summary>
    public static bool ValidDateRange(FormValidator validator, ModernDatePicker from, ModernDatePicker to)
    {
        validator.Begin();
        bool Valid() => (from.ShowCheckBox && !from.Checked) ||
            (to.ShowCheckBox && !to.Checked) || from.Value.Date <= to.Value.Date;
        bool ok = Valid();
        validator.Check(to, ok, "Earlier than From", Valid);
        if (!ok)
        {
            EventHandler? clearWhenFixed = null;
            clearWhenFixed = (_, _) => {
                if (Valid())
                {
                    validator.Clear(to);
                    from.ValueChanged -= clearWhenFixed;
                }
            };
            from.ValueChanged += clearWhenFixed;
        }
        return validator.Finish();
    }
    public static DataTable SectionFilterList() =>
        DatabaseHelper.QueryUser("GetSections");
    public static async Task<DataTable> SectionFilterListAsync() =>
        await DatabaseHelper.QueryUserAsync("GetSections");
    public static Panel Field(string caption, Control control)
    {
        var panel = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 10, 6)
        };
        panel.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            Math.Max(16, AppMetrics.TextHeight(AppTheme.NormalFont))));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label {
            Text = caption,
            Dock = DockStyle.Fill,
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        control.Margin = new Padding(0);
        if (control is ModernComboBox ||
            control is ModernDatePicker ||
            control is ModernTextBox { Multiline: false })
        {
            control.Height = AppMetrics.InputControlHeight;
            control.Dock = DockStyle.Top;
        }
        panel.Controls.Add(control, 0, 1);
        return panel;
    }
    public static TableLayoutPanel Bar(params (string Caption, Control Control)[] fields)
    {
        int columns = Math.Max(1, fields.Length);
        int rowHeight =
            Math.Max(16, AppMetrics.TextHeight(AppTheme.NormalFont)) +
            AppMetrics.InputControlHeight + 6;
        var table = new TableLayoutPanel {
            Dock = DockStyle.Top,
            Height = rowHeight,
            ColumnCount = columns,
            RowCount = 1,
            Padding = new Padding(0, 4, 0, 4)
        };
        for (int i = 0; i < columns; i++)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
        for (int i = 0; i < fields.Length; i++)
        {
            WorkspaceState.Remember(fields[i].Control, $"ReportFilter:{i}:{fields[i].Caption}");
            var fieldControl = Field(fields[i].Caption, fields[i].Control);
            fieldControl.Margin = new Padding(0);
            table.Controls.Add(fieldControl, i, 0);
        }
        return table;
    }
    public static void BindSimple(LookupBox box, DataTable table, string id, string text)
    {
        box.Bind(table, id, text);
    }
    /// <summary>The vertical chrome a filter card adds around its content - padding, the card header and the gaps.</summary>
    public static int FilterCardChromeHeight =>
        10 + 10 + AppMetrics.CardHeaderHeight() + 6 + 10;
    public static Control Layout(Control filters, DataGridView grid, string resultTitle = "Report Results", params Control[] actionButtons)
        => BuildLayout(filters, grid, resultTitle, true, actionButtons);
    public static Control CompactLayout(Control filters, DataGridView grid, string resultTitle, params Control[] actionButtons)
        => BuildLayout(filters, grid, resultTitle, true, actionButtons);
    private static Control BuildLayout(Control filters, DataGridView grid, string resultTitle, bool compact, params Control[] actionButtons)
    {
        var clear = new ModernButton {
            Text = "Clear Filters",
            Image = UiHelper.CreateGlyphIcon("↺", AppTheme.MutedText, 16),
            ImageAlign = ContentAlignment.MiddleLeft,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Width = AppScale.Scale(128, AppScale.ControlSizeScale),
            Height = AppMetrics.ButtonHeight,
            ThemeRole = "Outline"
        };
        clear.SetOutlineColors(AppTheme.Border, AppTheme.Text, AppTheme.Surface);
        var active = new Label {
            AutoSize = true,
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(8, 0, 0, 0)
        };
        var accessory = new FlowLayoutPanel {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent
        };
        accessory.Controls.Add(active);
        accessory.Controls.Add(clear);
        var filterCard = HumanUi.Card("Report Filters", "Run with no filters or any combination. Leave a filter at All or an optional date unchecked to ignore it.", accessory: accessory);
        filterCard.Content.Controls.Add(filters);
        /* each date picker's value as the page built it. Captured here, at construction, because this is the one moment those defaults are on screen. */
        var dateBaselines = Descendants(filters)
            .OfType<ModernDatePicker>()
            .ToDictionary(picker => picker, picker => picker.Value.Date);
        void RefreshFilterState()
        {
            int count = 0;
            foreach (Control control in Descendants(filters))
            {
                if (control is LookupBox lookup && lookup.SelectedId > 0)
                    count++;
                else if (control is ModernTextBox textBox && !string.IsNullOrWhiteSpace(textBox.Text))
                    count++;
                else if (control is CheckBox checkBox && checkBox.Checked)
                    count++;
                else if (control is ModernDatePicker picker &&
                         (picker.ShowCheckBox ? picker.Checked :
                         dateBaselines.TryGetValue(picker, out var baseline) && picker.Value.Date != baseline))
                {
                    /* The counter used to ignore the date range entirely, so "No optional filters" could sit above a report that was still scoped to last month. */
                    count++;
                }
            }
            active.Text = count == 0 ? "No optional filters" : $"{count} filter{(count == 1 ? "" : "s")} active";
        }
        clear.Click += (_, _) => {
            foreach (Control control in Descendants(filters))
            {
                if (control is LookupBox lookup)
                    lookup.SelectedIndex = 0;
                else if (control is ModernTextBox textBox)
                    textBox.Text = string.Empty;
                else if (control is CheckBox checkBox)
                    checkBox.Checked = false;
                /* The date range goes back to the range the page opened with. */
                else if (control is ModernDatePicker picker &&
                         dateBaselines.TryGetValue(picker, out var baseline))
                {
                    picker.Value = baseline;
                    if (picker.ShowCheckBox) picker.Checked = false;
                }
            }
            RefreshFilterState();
        };
        foreach (Control control in Descendants(filters))
        {
            if (control is LookupBox lookup)
                lookup.SelectedIndexChanged += (_, _) => RefreshFilterState();
            else if (control is ModernTextBox textBox)
                textBox.TextChanged += (_, _) => RefreshFilterState();
            else if (control is CheckBox checkBox)
                checkBox.CheckedChanged += (_, _) => RefreshFilterState();
            else if (control is ModernDatePicker picker)
                picker.ValueChanged += (_, _) => RefreshFilterState();
        }
        RefreshFilterState();
        int buttonRowHeight = 0;
        TableLayoutPanel? compactBody = null;
        TableLayoutPanel? compactButtons = null;
        if (compact)
        {
            filterCard.Content.Controls.Remove(filters);
            compactBody = new TableLayoutPanel {
                Dock = DockStyle.Top, AutoSize = false,
                ColumnCount = 1, RowCount = actionButtons.Length > 0 ? 2 : 1,
                Margin = Padding.Empty, Padding = Padding.Empty
            };
            compactBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            compactBody.RowStyles.Add(new RowStyle(SizeType.Absolute, filters.Height + filters.Padding.Vertical));
            filters.Margin = Padding.Empty;
            filters.Height += filters.Padding.Vertical;
            compactBody.Controls.Add(filters, 0, 0);
            filterCard.Content.Controls.Add(compactBody);
        }
        if (actionButtons.Length > 0)
        {
            var buttonRow = HumanUi.ButtonRow(actionButtons);
            buttonRow.Dock = DockStyle.Top;
            if (compactBody is not null)
            {
                compactButtons = buttonRow;
                buttonRow.AutoSize = false;
                buttonRow.Dock = DockStyle.Fill;
                buttonRow.Margin = new Padding(0, UiDesignSystem.Spacing.Xs, 0, 0);
                compactBody.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
                compactBody.Controls.Add(buttonRow, 0, 1);
            }
            else filterCard.Content.Controls.Add(buttonRow);
            buttonRowHeight = AppMetrics.ButtonRowHeight;
        }
        var resultCard = HumanUi.Card(resultTitle, "Review the generated report below.");
        resultCard.Content.Controls.Add(grid);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, filters.Height + FilterCardChromeHeight + buttonRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(filterCard, 0, 0);
        root.Controls.Add(resultCard, 0, 1);
        if (compactBody is not null)
        {
            bool fitting = false;
            void FitFilterCard()
            {
                if (fitting || root.IsDisposed) return;
                fitting = true;
                try
                {
                    // Measure the button flow at the available width instead of feeding
                    // the card's current height back into an autosized table.
                    int bodyHeight = filters.Height;
                    compactBody.RowStyles[0].Height = bodyHeight;
                    if (compactButtons is not null)
                    {
                        var flow = compactButtons.Controls.OfType<FlowLayoutPanel>().Single();
                        int width = Math.Max(1, filterCard.Content.ClientSize.Width -
                            compactButtons.Padding.Horizontal);
                        int buttonsHeight = flow.GetPreferredSize(new Size(width, 0)).Height +
                            flow.Margin.Vertical + compactButtons.Padding.Vertical;
                        compactBody.RowStyles[1].Height = buttonsHeight + compactButtons.Margin.Vertical;
                        bodyHeight += buttonsHeight + compactButtons.Margin.Vertical;
                    }
                    compactBody.Height = bodyHeight;
                    int height = filterCard.Padding.Vertical + filterCard.Header.Height +
                        filterCard.Content.Padding.Vertical + bodyHeight;
                    if (Math.Abs(root.RowStyles[0].Height - height) > 1)
                        root.RowStyles[0].Height = height;
                }
                finally { fitting = false; }
            }
            compactBody.SizeChanged += (_, _) => FitFilterCard();
            compactBody.Layout += (_, _) => FitFilterCard();
            filters.SizeChanged += (_, _) => FitFilterCard();
            filterCard.Content.SizeChanged += (_, _) => FitFilterCard();
            filterCard.Header.SizeChanged += (_, _) => FitFilterCard();
            if (compactButtons is not null)
                compactButtons.Layout += (_, _) => FitFilterCard();
            root.SizeChanged += (_, _) => FitFilterCard();
            FitFilterCard();
        }
        return root;
    }
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child))
                yield return descendant;
        }
    }
}
