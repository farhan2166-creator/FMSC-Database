using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Reports;
internal static class ReportUi
{
    private sealed record ChoiceFilter(int DefaultIndex);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ModernComboBox, ChoiceFilter> ChoiceFilters = new();
    /// <summary>Opt an actual filter into counting/resetting. Report views and
    /// grouping choices stay intact unless explicitly registered as filters.</summary>
    public static void RegisterChoiceFilter(ModernComboBox choice, int defaultIndex = 0)
    {
        ChoiceFilters.Remove(choice);
        ChoiceFilters.Add(choice, new ChoiceFilter(defaultIndex));
    }
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
        HumanUi.ApplyFieldInputLayout(control);
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
            Height = rowHeight + 8,
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
        var fieldPanels = table.Controls.Cast<Control>().ToArray();
        bool arranging = false;
        void ArrangeFields()
        {
            if (arranging || table.IsDisposed) return;
            var arrangement = ReportFilterArrangement.For(fields.Length,
                table.ClientSize.Width - table.Padding.Horizontal, AppScale.Scale(150));
            if (table.ColumnCount == arrangement.Columns && table.RowCount == arrangement.Rows) return;
            arranging = true;
            table.SuspendLayout();
            try
            {
                table.RowCount = Math.Max(table.RowCount, arrangement.Rows);
                table.ColumnCount = arrangement.Columns;
                table.ColumnStyles.Clear();
                for (int i = 0; i < arrangement.Columns; i++)
                    table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / arrangement.Columns));
                for (int i = 0; i < fieldPanels.Length; i++)
                    table.SetCellPosition(fieldPanels[i], new TableLayoutPanelCellPosition(
                        i % arrangement.Columns, i / arrangement.Columns));
                table.RowCount = arrangement.Rows;
                table.RowStyles.Clear();
                for (int i = 0; i < arrangement.Rows; i++)
                    table.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
                table.Height = rowHeight * arrangement.Rows + table.Padding.Vertical;
            }
            finally { table.ResumeLayout(true); arranging = false; }
        }
        table.SizeChanged += (_, _) => ArrangeFields();
        table.HandleCreated += (_, _) => ArrangeFields();
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
    public static Control CompactLayout(Control filters, DataGridView grid, string resultTitle, Action filtersCleared, params Control[] actionButtons)
        => BuildLayout(filters, grid, resultTitle, true, actionButtons, filtersCleared);
    private static Control BuildLayout(Control filters, DataGridView grid, string resultTitle, bool compact, Control[] actionButtons, Action? filtersCleared = null)
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
        var filterCard = HumanUi.Card("Report Filters", "Choose the scope below, then run the report. All and unchecked optional dates include every value.", accessory: accessory);
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
                else if (control is ModernComboBox choice && ChoiceFilters.TryGetValue(choice, out var filter) &&
                         choice.SelectedIndex >= 0 && choice.SelectedIndex != filter.DefaultIndex)
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
                else if (control is ModernComboBox choice && ChoiceFilters.TryGetValue(choice, out var filter))
                    choice.SelectedIndex = filter.DefaultIndex < choice.Items.Count ? filter.DefaultIndex : -1;
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
            filtersCleared?.Invoke();
        };
        foreach (Control control in Descendants(filters))
        {
            if (control is LookupBox lookup)
                lookup.SelectedIndexChanged += (_, _) => RefreshFilterState();
            else if (control is ModernComboBox choice && ChoiceFilters.TryGetValue(choice, out _))
                choice.SelectedIndexChanged += (_, _) => RefreshFilterState();
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
            compactBody.RowStyles.Add(new RowStyle(SizeType.Absolute, filters.Height));
            filters.Margin = Padding.Empty;
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
                    // Calculate wrapping from the buttons themselves. A flow panel's
                    // preferred height can retain a narrow startup width constraint.
                    int bodyHeight = filters.Height;
                    compactBody.RowStyles[0].Height = bodyHeight;
                    if (compactButtons is not null)
                    {
                        var flow = compactButtons.Controls.OfType<FlowLayoutPanel>().Single();
                        // The inner panel can still have its startup width while the
                        // table is laying out. Use the owning row's available width
                        // so that startup wrapping does not leave a tall empty card.
                        int width = Math.Max(1, root.ClientSize.Width - root.Padding.Horizontal -
                            filterCard.Margin.Horizontal - filterCard.Padding.Horizontal -
                            filterCard.Content.Padding.Horizontal - compactBody.Padding.Horizontal -
                            compactButtons.Margin.Horizontal - compactButtons.Padding.Horizontal);
                        int buttonsHeight = WrappedButtonHeight(flow, width, !root.Visible) +
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
            bool fitQueued = false;
            void QueueFit()
            {
                if (fitQueued || !root.IsHandleCreated || root.IsDisposed) return;
                fitQueued = true;
                root.BeginInvoke(new Action(() => {
                    fitQueued = false;
                    if (root.IsDisposed) return;
                    FitFilterCard();
                    int[] heights = root.GetRowHeights();
                    if (heights.Length > 0 && heights[0] != (int)root.RowStyles[0].Height)
                        root.PerformLayout();
                }));
            }
            compactBody.SizeChanged += (_, _) => QueueFit();
            compactBody.Layout += (_, _) => QueueFit();
            filters.SizeChanged += (_, _) => QueueFit();
            filterCard.Content.SizeChanged += (_, _) => QueueFit();
            filterCard.Header.SizeChanged += (_, _) => QueueFit();
            if (compactButtons is not null)
                compactButtons.Layout += (_, _) => QueueFit();
            root.SizeChanged += (_, _) => QueueFit();
            root.HandleCreated += (_, _) => QueueFit();
            root.VisibleChanged += (_, _) => QueueFit();
            FitFilterCard();
        }
        return root;
    }
    private static int WrappedButtonHeight(FlowLayoutPanel flow, int availableWidth, bool includeHidden)
    {
        int width = Math.Max(1, availableWidth - flow.Padding.Horizontal);
        int usedWidth = 0, rowHeight = 0, totalHeight = 0;
        foreach (Control button in flow.Controls)
        {
            if (!includeHidden && !button.Visible) continue;
            int buttonWidth = button.Width + button.Margin.Horizontal;
            int buttonHeight = button.Height + button.Margin.Vertical;
            if (usedWidth > 0 && usedWidth + buttonWidth > width)
            {
                totalHeight += rowHeight;
                usedWidth = rowHeight = 0;
            }
            usedWidth += buttonWidth;
            rowHeight = Math.Max(rowHeight, buttonHeight);
            if (flow.GetFlowBreak(button))
            {
                totalHeight += rowHeight;
                usedWidth = rowHeight = 0;
            }
        }
        return totalHeight + rowHeight + flow.Padding.Vertical;
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
