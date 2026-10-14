using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Reports.Finance;
public sealed class StudentAccountStatementPage : V2Page, IBusyPage
{
    private readonly LookupBox campus =
        new();
    private readonly LookupBox year =
        new();
    private readonly LookupBox cls =
        new();
    private readonly LookupBox section =
        new();
    private readonly StudentPicker studentSearch = new();
    private readonly ModernDatePicker from =
        new ModernDatePicker {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "dd-MMM-yyyy",
            Value = DateTime.Today.AddMonths(-1),
            ShowCheckBox = true,
            Checked = false,
            Dock = DockStyle.Fill
        };
    private readonly ModernDatePicker to =
        new ModernDatePicker {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "dd-MMM-yyyy",
            Value = DateTime.Today,
            ShowCheckBox = true,
            Checked = false,
            Dock = DockStyle.Fill
        };
    private readonly DataGridView students =
        HumanUi.Grid();
    private readonly Label openingBalance =
        SummaryValue(AppTheme.Success);
    private readonly Label chargesTotal =
        SummaryValue(AppTheme.Text);
    private readonly Label paymentsTotal =
        SummaryValue(AppTheme.Success);
    private readonly Label closingBalance =
        SummaryValue(AppTheme.Danger);
    private readonly Label selectionSummary =
        new() {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = AppTheme.MutedText,
            Text = "No students included"
        };
    private bool suppressFilterEvents;
    private bool suppressStudentEvents;
    private int studentSearchVersion;
    private int contextLoadVersion;
    private FormValidator? statementDateValidator;
    private readonly ModernButton exportButton;
    private ModernButton? generateStatementButton;
    private ModernButton? printAmountsDueButton;
    /* The filter values that produced the statement currently in the grid. Empty until a statement has actually been loaded for this page. */
    private string _appliedFilterSignature = string.Empty;
    private readonly HashSet<int> manuallySelectedAdmissionIds =
        new();
    private readonly Dictionary<int, HashSet<int>> familySelections =
        new();
    private readonly LatestReadScope statementReads=new();
    private readonly Func<int,DateTime?,DateTime?,Task<(DataTable Transactions,decimal OpeningBalance)>> readStatement;
    public bool IsBusy=>BusyOperationRunning||printingDueReport;
    public string BusyMessage=>"A financial statement or report is being prepared. Wait for the result before leaving.";
    public StudentAccountStatementPage() : this(true,null) { }
    internal StudentAccountStatementPage(bool initialize,Func<int,DateTime?,DateTime?,Task<(DataTable Transactions,decimal OpeningBalance)>>? readStatement)
        : base(PageTitles.StudentAccountStatement, "Select students to review or print their charges, payments and balances.")
    {
        this.readStatement=readStatement??LoadStatementDataAsync;
        Disposed+=(_,_)=>statementReads.Dispose();
        var export =
            AddExportAction("Student_Fee_Statement");
        exportButton = export;
        RelocateFromActions(export);
        WorkspaceState.Remember(campus, "FeeStatement:Campus");
        WorkspaceState.Remember(year, "FeeStatement:Year");
        WorkspaceState.Remember(cls, "FeeStatement:Class");
        WorkspaceState.Remember(section, "FeeStatement:Section");
        WorkspaceState.Remember(studentSearch, "FeeStatement:StudentSearch");
        WorkspaceState.Remember(from, "FeeStatement:From");
        WorkspaceState.Remember(to, "FeeStatement:To");
        studentSearch.PlaceholderText =
            StudentPicker.SearchPlaceholder;
        studentSearch.SearchFailed += ex => Fail(ex);
        studentSearch.UserSelectionChanged += async (_, _) => {
            OnFiltersChanged();
            await SearchStudents();
        };
        studentSearch.KeyDown +=
            async (_, e) => {
                if (e.KeyCode != Keys.Enter || e.Handled)
                    return;
                e.SuppressKeyPress = true;
                e.Handled = true;
                /* SearchStudents returns Task, so it must be awaited. */
                await SearchStudents();
            };
        campus.SelectedIndexChanged +=
            async (_, _) => {
                if (suppressFilterEvents)
                    return;
                /* The sections that exist depend on the campus as well as the class (dbo.GetClassSections), so a campus change re-narrows them too. */
                ClearDisplayedStudents();
                await ReloadSections();
            };
        year.SelectedIndexChanged +=
            (_, _) => {
                if (suppressFilterEvents)
                    return;
                ClearDisplayedStudents();
            };
        cls.SelectedIndexChanged +=
            async (_, _) => {
                if (suppressFilterEvents)
                    return;
                ClearDisplayedStudents();
                await ReloadSections();
            };
        section.SelectedIndexChanged +=
            (_, _) => {
                if (suppressFilterEvents)
                    return;
                ClearDisplayedStudents();
            };
        /* A displayed statement is produced from the checked students and the from/to dates, and the campus/year/class/section boxes plus the search text decide which students those are. */
        foreach (LookupBox filter in new[] { campus, year, cls, section })
            filter.SelectedIndexChanged += (_, _) => OnFiltersChanged();
        studentSearch.TextChanged +=
            (_, _) => {
                /* The search text is one of the criteria an in-flight search was run for, so changing it invalidates that run. */
                studentSearchVersion++;
                OnFiltersChanged();
            };
        from.ValueChanged +=
            (_, _) => OnFiltersChanged();
        to.ValueChanged +=
            (_, _) => OnFiltersChanged();
        if(initialize)Defer(BindContextLookups);
        ConfigureStudentGrid();
        var filters =
            HumanUi.Card("1. Find Students", "Use any combination of filters. Admission Number or name is usually the quickest search.");
        var filterGrid =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 2,
                Padding = new Padding(2),
                Margin = new Padding(0)
            };
        for (int i = 0; i < 4; i++)
        {
            filterGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        }
        // Field panels also need room for the input's own margins and their bottom padding.
        int fieldRowHeight = Math.Max(AppMetrics.FieldRowHeight,
            AppMetrics.TextHeight(AppTheme.FieldCaptionFont) + AppMetrics.InputControlHeight
            + AppMetrics.SpacingSm + Math.Max(studentSearch.Margin.Vertical, from.Margin.Vertical));
        filterGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldRowHeight));
        filterGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldRowHeight));
        filterGrid.Controls.Add(HumanUi.Field("Campus", campus), 0, 0);
        filterGrid.Controls.Add(HumanUi.Field("Academic Year", year), 1, 0);
        filterGrid.Controls.Add(HumanUi.Field("Class", cls), 2, 0);
        filterGrid.Controls.Add(HumanUi.Field("Section", section), 3, 0);
        filterGrid.Controls.Add(HumanUi.Field("Student Name or Admission Number", studentSearch), 0, 1);
        filterGrid.SetColumnSpan(studentSearch.Parent!, 2);
        filterGrid.Controls.Add(HumanUi.Field("Statement From (optional)", from), 2, 1);
        filterGrid.Controls.Add(HumanUi.Field("Statement To (optional)", to), 3, 1);
        // Field's default auto-sized column retains the native date picker's
        // preferred width. Let every field follow its allocated filter column.
        foreach (TableLayoutPanel field in filterGrid.Controls.OfType<TableLayoutPanel>())
            field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        var searchButton =
            CreateFilterButton("Find Students", AppTheme.Primary);
        var viewButton =
            CreateFilterButton("Generate Statement", AppTheme.Success);
        var dueReportButton =
            CreateFilterButton("Print Amounts Due", AppTheme.Primary);
        generateStatementButton = viewButton;
        printAmountsDueButton = dueReportButton;
        UpdateSelectionActions();
        var clearButton =
            CreateFilterButton("Start Over", AppTheme.MutedText);
        /* this is also the one place that takes off ticks the current list does not show, so the button says so rather than leaving the operator to hunt for them. */
        SetToolTip(
            clearButton,
            "Clears the filters, the search and the statement, and unticks every student - "
            + "including students ticked in an earlier search that this list does not show.");
        /* SearchStudents returns Task; await it so a failure surfaces through this handler rather than as an unobserved task exception. */
        searchButton.Click += async (_, _) => await SearchStudents();
        viewButton.Click += async (_, _) => await LoadStatement();
        dueReportButton.Click += async (_, _) => await PrintDueReport();
        clearButton.Click += (_, _) => {
            ClearForm();
            /* The section list was narrowed to the previous campus/class; with both cleared it must offer every section again. */
            _ = ReloadSections();
        };
        var buttonPanel =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Padding = new Padding(2, 4, 2, 0),
                Margin = new Padding(0)
            };
        for (int i = 0; i < 4; i++)
            buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        buttonPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        foreach (ModernButton button in new[]
                 {
                     searchButton,
                     viewButton,
                     dueReportButton,
                     clearButton
                 })
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(4, 2, 4, 2);
            button.MinimumSize = new Size(0, AppMetrics.ButtonHeight);
        }
        buttonPanel.Controls.Add(searchButton, 0, 0);
        buttonPanel.Controls.Add(viewButton, 1, 0);
        buttonPanel.Controls.Add(dueReportButton, 2, 0);
        buttonPanel.Controls.Add(clearButton, 3, 0);
        var filterBody =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
        filterBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        filterBody.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldRowHeight * 2 + filterGrid.Padding.Vertical));
        filterBody.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight + 8));
        filterBody.Controls.Add(filterGrid, 0, 0);
        filterBody.Controls.Add(buttonPanel, 0, 1);
        filters.Content.Controls.Add(filterBody);
        var studentCard =
            HumanUi.Card("2. Choose Students", "Tick Include, or Include Family for linked students.");
        var selectAllButton = CreateFilterButton("Select All Results", AppTheme.MutedText);
        var clearSelectionButton = CreateFilterButton("Clear Selection", AppTheme.MutedText);
        selectAllButton.Click += (_, _) => SelectAllStudents();
        clearSelectionButton.Click += (_, _) => ClearStudentSelection();
        var selectionBar = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        selectionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        selectionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        selectAllButton.Dock = DockStyle.Fill;
        clearSelectionButton.Dock = DockStyle.Fill;
        selectionBar.Controls.Add(selectAllButton, 0, 0);
        selectionBar.Controls.Add(clearSelectionButton, 1, 0);
        selectionSummary.AutoSize = false;
        selectionSummary.Margin = new Padding(4, 0, 4, 0);
        /* Use explicit rows rather than Dock.Top plus Dock.Fill. The latter depends on WinForms z-order and allowed the toolbar to consume/overlap nearly all of the student grid at shorter window heights. */
        var selectionBody = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        selectionBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        int selectionSummaryHeight = AppMetrics.TextHeight(AppTheme.NormalFont) + 6;
        selectionBody.RowStyles.Add(new RowStyle(SizeType.Absolute, selectionSummaryHeight));
        selectionBody.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight + 10));
        selectionBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        selectionBody.Controls.Add(selectionSummary, 0, 0);
        selectionBody.Controls.Add(selectionBar, 0, 1);
        selectionBody.Controls.Add(students, 0, 2);
        studentCard.Content.Controls.Add(selectionBody);
        var statement =
            HumanUi.Card("3. Review Statement", "Charges and allocated payments.", accessory: export);
        // Dock the accessory before the two header labels consume vertical space.
        statement.Header.Controls.SetChildIndex(export, statement.Header.Controls.Count - 1);
        statement.Content.Controls.Add(Grid);
        var work =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(0, 6, 0, 0),
                Margin = new Padding(0)
            };
        work.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        work.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        work.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        work.Controls.Add(studentCard, 0, 0);
        work.Controls.Add(statement, 1, 0);
        var summary = BuildSummary();
        var root =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
        int filterHeight = filters.Padding.Vertical + filters.Header.Height + filters.Content.Padding.Vertical
            + fieldRowHeight * 2 + filterGrid.Padding.Vertical + AppMetrics.ButtonHeight + 8;
        int summaryHeight = SummaryCardRowHeight();
        int statusHeight = AppMetrics.TextHeight(AppTheme.NormalFont) + 8;
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, filterHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, summaryHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, statusHeight));
        root.Controls.Add(filters, 0, 0);
        root.Controls.Add(work, 0, 1);
        root.Controls.Add(summary, 0, 2);
        StatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(StatusLabel, 0, 3);

        // A fill-docked table alone shrinks its percent row to zero. Keep a usable
        // grid height inside a scrolling viewport when the dialog is shorter.
        var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0) };
        root.Dock = DockStyle.None;
        viewport.Controls.Add(root);
        bool arranging = false;
        Size arrangedViewportSize = Size.Empty;
        void ArrangeStatement()
        {
            if (arranging || viewport.ClientSize == arrangedViewportSize)
                return;
            arranging = true;
            arrangedViewportSize = viewport.ClientSize;
            try
            {
                int availableWidth = viewport.ClientSize.Width;
                int studentWidth = students.Columns.Cast<DataGridViewColumn>()
                    .Where(column => column.Visible).Sum(column => column.MinimumWidth)
                    + studentCard.Padding.Horizontal + SystemInformation.VerticalScrollBarWidth + 8;
                bool stacked = availableWidth < studentWidth + AppScale.Scale(420) + AppMetrics.SpacingSm;
                int gridHeight = students.ColumnHeadersHeight + AppMetrics.GridRowHeight * 4
                    + SystemInformation.HorizontalScrollBarHeight + 4;
                int studentHeight = studentCard.Padding.Vertical + studentCard.Header.Height
                    + studentCard.Content.Padding.Vertical + selectionSummaryHeight
                    + AppMetrics.ButtonHeight + 10 + gridHeight;
                int statementHeight = statement.Padding.Vertical + statement.Header.Height
                    + statement.Content.Padding.Vertical + gridHeight;
                work.SuspendLayout();
                work.SetCellPosition(statement, new TableLayoutPanelCellPosition(stacked ? 0 : 1, stacked ? 1 : 0));
                work.ColumnCount = stacked ? 1 : 2;
                work.RowCount = stacked ? 2 : 1;
                work.ColumnStyles.Clear();
                work.RowStyles.Clear();
                work.ColumnStyles.Add(stacked
                    ? new ColumnStyle(SizeType.Percent, 100f)
                    : new ColumnStyle(SizeType.Absolute, Math.Max(studentWidth + AppMetrics.SpacingSm, availableWidth * 0.48f)));
                if (stacked)
                {
                    work.RowStyles.Add(new RowStyle(SizeType.Absolute, studentHeight + AppMetrics.SpacingSm));
                    work.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                }
                else
                {
                    work.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                    work.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                }
                studentCard.Margin = stacked
                    ? new Padding(0, 0, 0, AppMetrics.SpacingSm)
                    : new Padding(0, 0, AppMetrics.SpacingSm, 0);
                int workHeight = work.Padding.Vertical + studentHeight
                    + (stacked ? statementHeight + AppMetrics.SpacingSm : 0);
                root.Size = new Size(Math.Max(availableWidth, AppScale.Scale(720)),
                    Math.Max(viewport.ClientSize.Height, filterHeight + workHeight + summaryHeight + statusHeight));
                work.ResumeLayout(true);
            }
            finally
            {
                arranging = false;
            }
        }
        viewport.SizeChanged += (_, _) => ArrangeStatement();
        viewport.Layout += (_, _) => ArrangeStatement();
        SetContent(viewport);
        ArrangeStatement();
        ClearForm();
    }
    // This is additive only - the free-text search, campus/year/class/section filters, and multi-student/family statement logic below are completely unchanged.
    public StudentAccountStatementPage(int preloadAdmissionId)
        : this()
    {
        if (preloadAdmissionId > 0)
        {
            Defer(() => _ = PreloadAdmissionIdAsync(preloadAdmissionId));
        }
    }
    private async Task PreloadAdmissionIdAsync(int admissionId)
    {
        try
        {
            studentSearch.Text = AdmissionNumbers.ForId(admissionId);
            manuallySelectedAdmissionIds.Add(admissionId);
            await SearchStudents();
            if (IsDisposed)
                return;
            await LoadStatement();
        }
        catch (Exception ex) { if (!IsDisposed) Fail(ex); }
    }
    private static ModernButton CreateFilterButton(string text, Color color)
    {
        var button =
            new ModernButton {
                Text = text,
                Width = AppScale.Scale(145),
                MinimumSize = new Size(145, AppMetrics.ButtonHeight),
                Height = AppMetrics.ButtonHeight,
                Margin = new Padding(4, 3, 4, 3),
                Font = AppTheme.ButtonFont
            };
        if (color == AppTheme.Success)
            button.ThemeRole = "Success";
        else if (color == AppTheme.Warning)
            button.ThemeRole = "Warning";
        else if (color == AppTheme.Danger)
            button.ThemeRole = "Danger";
        else if (color == AppTheme.MutedText)
            button.ThemeRole = "Secondary";
        else
            button.ThemeRole = "Primary";
        return button;
    }
    private void ConfigureStudentGrid()
    {
        students.Columns.Clear();
        students.AutoGenerateColumns =
            false;
        students.Dock =
            DockStyle.Fill;
        students.ReadOnly =
            false;
        students.AllowUserToAddRows =
            false;
        students.AllowUserToDeleteRows =
            false;
        students.AllowUserToResizeRows =
            false;
        students.MultiSelect =
            false;
        students.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;
        students.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.None;
        students.ColumnHeadersHeight = AppMetrics.TextHeight(AppTheme.ButtonFont) * 2 + 12;
        students.Columns.Add(
            new DataGridViewCheckBoxColumn {
                Name = "Select",
                HeaderText = "Include",
                Width = AppMetrics.TextWidth("Include", AppTheme.ButtonFont, 24),
                MinimumWidth = AppMetrics.TextWidth("Include", AppTheme.ButtonFont, 24),
                AutoSizeMode =
                    DataGridViewAutoSizeColumnMode.None,
                ReadOnly = false,
                ThreeState = false
            });
        students.Columns.Add(
            new DataGridViewCheckBoxColumn {
                Name = "Family",
                HeaderText = "Include Family",
                Width = AppScale.Scale(105),
                MinimumWidth = AppScale.Scale(105),
                AutoSizeMode =
                    DataGridViewAutoSizeColumnMode.None,
                ReadOnly = false,
                ThreeState = false
            });
        students.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "AdmissionId",
                HeaderText = "ID",
                Visible = false
            });
        students.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "StudentName",
                HeaderText = "Student",
                FillWeight = 180,
                MinimumWidth = AppScale.Scale(180),
                ReadOnly = true
            });
        students.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "ClassName",
                HeaderText = "Class",
                FillWeight = 75,
                MinimumWidth = AppScale.Scale(75),
                ReadOnly = true
            });
        students.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "SectionName",
                HeaderText = "Section",
                FillWeight = 70,
                MinimumWidth = Math.Max(70, AppMetrics.TextWidth("Section", AppTheme.ButtonFont, 24)),
                ReadOnly = true
            });
        students.CellContentClick +=
            (_, e) => {
                if (e.RowIndex < 0)
                    return;
                if (e.ColumnIndex ==
                    students.Columns["Select"].Index)
                {
                    BeginInvoke(
                        new Action(
                            () =>
                                HandleSelectChanged(e.RowIndex)));
                    return;
                }
                if (e.ColumnIndex ==
                    students.Columns["Family"].Index)
                {
                    BeginInvoke(
                        new Action(
                            () =>
                                HandleFamilyChanged(e.RowIndex)));
                }
            };
        students.CurrentCellDirtyStateChanged +=
            (_, _) => {
                if (!students.IsCurrentCellDirty)
                    return;
                if (students.CurrentCell.OwningColumn
                    is DataGridViewCheckBoxColumn)
                {
                    students.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
    }
    private void HandleSelectChanged(int rowIndex)
    {
        if (suppressStudentEvents)
            return;
        if (rowIndex < 0 ||
            rowIndex >= students.Rows.Count)
        {
            return;
        }
        var row =
            students.Rows[rowIndex];
        if (row.IsNewRow)
            return;
        int admissionId =
            GetRowAdmissionId(row);
        if (admissionId <= 0)
            return;
        bool selected =
            Convert.ToBoolean(
                row.Cells["Select"]
                    .Value ?? false);
        if (selected)
        {
            manuallySelectedAdmissionIds.Add(admissionId);
            SetStatus("Student selected." + SelectionCountText());
            /* The checked set is part of what the displayed statement was built from, so it is checked for staleness here. */
            OnFiltersChanged();
            return;
        }
        manuallySelectedAdmissionIds.Remove(admissionId);
        if (familySelections.ContainsKey(admissionId))
        {
            familySelections.Remove(admissionId);
        }
        row.Cells["Family"].Value =
            false;
        if (IsSelectedThroughAnotherFamily(admissionId))
        {
            row.Cells["Select"].Value =
                true;
        }
        else
        {
            row.Cells["Select"].Value =
                false;
        }
        /* Unticking a student who carried a family tick drops that family from the included set. */
        RefreshSelectionCells();
        SetStatus("Student deselected." + SelectionCountText());
        /* as above: the checked set changed, so the statement is re-judged. */
        OnFiltersChanged();
    }
    private void HandleFamilyChanged(int rowIndex)
    {
        if (suppressStudentEvents)
            return;
        if (rowIndex < 0 ||
            rowIndex >= students.Rows.Count)
        {
            return;
        }
        var row =
            students.Rows[rowIndex];
        if (row.IsNewRow)
            return;
        int admissionId =
            GetRowAdmissionId(row);
        if (admissionId <= 0)
            return;
        bool familyChecked =
            Convert.ToBoolean(
                row.Cells["Family"]
                    .Value ?? false);
        if (!familyChecked)
        {
            familySelections.Remove(admissionId);
            row.Cells["Select"].Value =
                manuallySelectedAdmissionIds.Contains(
                    admissionId) ||
                IsSelectedThroughAnotherFamily(admissionId);
            RefreshSelectionCells();
            SetStatus("Family selection removed." + SelectionCountText());
            /* A family tick changes the checked set, which is part of what the displayed statement was built from. */
            OnFiltersChanged();
            return;
        }
        SelectFamilyForStudent(admissionId);
        /* SelectFamilyForStudent expands the tick into the whole family's checks, so the statement is re-judged once that has happened. */
        OnFiltersChanged();
    }
    /// <summary>Makes every row's Select / Family cell say what the included set actually is (manual ticks + family ticks).</summary>
    private void RefreshSelectionCells()
    {
        foreach (DataGridViewRow gridRow in students.Rows)
        {
            if (gridRow.IsNewRow)
                continue;
            int id = GetRowAdmissionId(gridRow);
            if (id <= 0)
                continue;
            gridRow.Cells["Select"].Value =
                manuallySelectedAdmissionIds.Contains(id) ||
                IsSelectedThroughAnyFamily(id);
            gridRow.Cells["Family"].Value =
                familySelections.ContainsKey(id);
        }
    }
    private bool IsSelectedThroughAnotherFamily(int admissionId)
    {
        foreach (var pair
                 in familySelections)
        {
            if (pair.Key == admissionId)
                continue;
            if (pair.Value.Contains(admissionId))
            {
                return true;
            }
        }
        return false;
    }
    private static int GetRowAdmissionId(DataGridViewRow row)
    {
        if (row.Cells["AdmissionId"]
                .Value == null ||
            row.Cells["AdmissionId"]
                .Value == DBNull.Value)
        {
            return 0;
        }
        return Convert.ToInt32(
            row.Cells["AdmissionId"]
                .Value);
    }
    /* everything that decides what the displayed statement contains: the search context and the free-text search pick the students, the checked set (with its family expansions) is what LoadStatement actually reads. */
    private string FilterSignature()
    {
        var admissionIds =
            GetSelectedAdmissionIds();
        admissionIds.Sort();
        return
            $"{campus.SelectedId}|{year.SelectedId}|{cls.SelectedId}|{section.SelectedId}|" +
            $"{studentSearch.Text.Trim()}|{string.Join(",", admissionIds)}|" +
            $"{from.Checked}:{from.Value.Date:yyyy-MM-dd}|{to.Checked}:{to.Value.Date:yyyy-MM-dd}";
    }
    private void SetReportActionsEnabled(bool enabled)
    {
        exportButton.Enabled = enabled;
    }
    /* report the displayed statement as stale the moment the selection or dates no longer match the values that produced it. */
    private void OnFiltersChanged()
    {
        if(BusyOperationRunning)statementReads.Cancel();
        UpdateSelectionActions();
        if (string.IsNullOrEmpty(_appliedFilterSignature))
            return;
        bool current = string.Equals(_appliedFilterSignature, FilterSignature(), StringComparison.Ordinal);
        SetReportActionsEnabled(current);
        if (current)
            SetStatus("Statement matches the current filters and student selection.");
        else
            SetStatus("Filters or student changed. Use Generate Statement again before exporting.", true);
    }
    private async Task BindContextLookups()
    {
        try
        {
            int version = ++contextLoadVersion;
            suppressFilterEvents = true;
            var campusesTask = DatabaseHelper.QueryUserAsync("GetCampuses");
            var yearsTask = DatabaseHelper.QueryUserAsync("GetAcademicYears");
            var classesTask = DatabaseHelper.QueryUserAsync("GetClasses");
            /* Sections come from the campus/class/section assignments that actually exist, not from the bare section list. */
            var sectionsTask = ReportClassSectionLookup.QueryAsync();
            await Task.WhenAll(campusesTask, yearsTask, classesTask, sectionsTask);
            if (version != contextLoadVersion || IsDisposed)
                return;
            campus.Bind(await campusesTask, "CampusId", "CampusName");
            year.Bind(await yearsTask, "AcademicYearId", "YearName");
            cls.Bind(await classesTask, "ClassId", "ClassName");
            section.Bind(DistinctSections(await sectionsTask), "SectionId", "SectionName");
            suppressFilterEvents = false;
            students.Rows.Clear();
        }
        catch (Exception ex)
        {
            suppressFilterEvents = false;
            Fail(ex);
        }
    }
    private async Task ReloadSections()
    {
        try
        {
            int version = ++contextLoadVersion;
            int previousSectionId = section.SelectedId;
            suppressFilterEvents = true;
            try
            {
                section.ClearLookup();
            }
            finally
            {
                suppressFilterEvents = false;
            }
            /* Read before the await: the filters the list is being built for. */
            int campusId = campus.SelectedId;
            int classId = cls.SelectedId;
            /* Only the sections that exist for the chosen campus and class. Only the Bind itself is suppressed, below. */
            var raw =
                DistinctSections(
                    await ReportClassSectionLookup.QueryAsync(campusId, classId));
            if (version != contextLoadVersion || IsDisposed)
                return;
            suppressFilterEvents = true;
            try
            {
                section.Bind(raw, "SectionId", "SectionName");
                if (previousSectionId > 0 &&
                    raw.AsEnumerable().Any(
                        r => r["SectionId"] != DBNull.Value &&
                             Convert.ToInt32(r["SectionId"]) == previousSectionId))
                {
                    section.SelectedValue =
                        previousSectionId;
                }
            }
            finally
            {
                suppressFilterEvents = false;
            }
            /* Bind resets the selection, which may have dropped a section the operator had chosen; the statement filters must be re-judged. */
            OnFiltersChanged();
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    // note: signature changed from "async void" to "async Task" (Rule 2 - a behavior-preserving refactor, not a logic change) solely so the new preload constructor below can await…
    private async Task SearchStudents()
    {
        try
        {
            int version = ++studentSearchVersion;
            string search =
                studentSearch.SelectedId > 0
                    ? AdmissionNumbers.ForId(studentSearch.SelectedId)
                    : studentSearch.Text.Trim();
            /* Admission Number is an independent filter (): a number in Student Search matches on Admission Number whatever Campus / Academic Year / Class / Section say. */
            bool byAdmissionId = AdmissionNumbers.TryNormalize(search, out string admissionNumber);
            if (byAdmissionId) search = admissionNumber;
            bool contextSet =
                campus.SelectedId > 0 ||
                year.SelectedId > 0 ||
                cls.SelectedId > 0 ||
                section.SelectedId > 0;
            SetStatus(UiText.Prompts.SearchingStudents);
            var raw =
                await DatabaseHelper.QueryUserAsync(
                    "GetStudents",
                    DatabaseHelper.Int(
                        "@CampusId",
                        byAdmissionId ? 0 : campus.SelectedId),
                    DatabaseHelper.Int(
                        "@AcademicYearId",
                        byAdmissionId ? 0 : year.SelectedId),
                    DatabaseHelper.P(
                        "@Search",
                        search.Length == 0
                            ? null
                            : search),
                    DatabaseHelper.Bit("@IncludeFormerStudents", true));
            if (version != studentSearchVersion || IsDisposed)
                return;
            List<DataRow> matchingRows =
                HumanUi.FindStudentMatches(raw, search);
            suppressStudentEvents = true;
            students.Rows.Clear();
            foreach (DataRow row
                     in matchingRows)
            {
                if (!byAdmissionId &&
                    cls.SelectedId > 0 &&
                    raw.Columns.Contains("ClassId") &&
                    (row["ClassId"] == DBNull.Value ||
                     Convert.ToInt32(row["ClassId"]) != cls.SelectedId))
                {
                    continue;
                }
                if (!byAdmissionId &&
                    section.SelectedId > 0 &&
                    raw.Columns.Contains("SectionId") &&
                    (row["SectionId"] == DBNull.Value ||
                     Convert.ToInt32(row["SectionId"]) != section.SelectedId))
                {
                    continue;
                }
                if (row["AdmissionId"] ==
                    DBNull.Value)
                {
                    continue;
                }
                int admissionId =
                    Convert.ToInt32(row["AdmissionId"]);
                bool selected =
                    manuallySelectedAdmissionIds.Contains(
                        admissionId) ||
                    IsSelectedThroughAnyFamily(admissionId);
                bool family =
                    familySelections.ContainsKey(admissionId);
                string studentDisplay =
                    HumanUi.StudentDisplay(admissionId, raw.Columns.Contains("StudentName")
                            ? Convert.ToString(row["StudentName"])
                            : string.Empty,
                        raw.Columns.Contains("FatherName")
                            ? Convert.ToString(row["FatherName"])
                            : string.Empty,
                        raw.Columns.Contains("Gender")
                            ? Convert.ToString(row["Gender"])
                            : null,
                        raw.Columns.Contains("RegistrationNumber")
                            ? Convert.ToString(row["RegistrationNumber"])
                            : null,
                        raw.Columns.Contains("ClassName")
                            ? Convert.ToString(row["ClassName"])
                            : null,
                        raw.Columns.Contains("SectionName")
                            ? Convert.ToString(row["SectionName"])
                            : null);
                int index =
                    students.Rows.Add(selected, family, admissionId, studentDisplay, raw.Columns.Contains("ClassName")
                            ? Convert.ToString(row["ClassName"])
                            : string.Empty,
                        raw.Columns.Contains("SectionName")
                            ? Convert.ToString(row["SectionName"])
                            : string.Empty);
                students.Rows[index].Tag =
                    admissionId;
            }
            suppressStudentEvents = false;
            if (students.Rows.Count == 0)
            {
                SetStatus("No students found for the selected filters.");
            }
            else
            {
                /* Ticks accumulate across searches on purpose (search a name, tick, search another). */
                SetStatus($"{students.Rows.Count} student(s) found." +
                    (byAdmissionId && contextSet
                        ? " Admission Number is searched across every campus, year, class and section."
                        : string.Empty) +
                    SelectionCountText());
            }
        }
        catch (Exception ex)
        {
            suppressStudentEvents = false;
            Fail(ex);
        }
    }
    private bool IsSelectedThroughAnyFamily(int admissionId)
    {
        foreach (var family
                 in familySelections.Values)
        {
            if (family.Contains(admissionId))
            {
                return true;
            }
        }
        return false;
    }
    /// <summary>GetClassSections returns one row per campus/class/section placement, so the same section repeats (once per class, once per campus). A section filter needs each section once.</summary>
    private static DataTable DistinctSections(DataTable classSections)
    {
        var result = new DataTable();
        result.Columns.Add("SectionId", typeof(int));
        result.Columns.Add("SectionName", typeof(string));
        result.Columns.Add("SectionDisplayOrder", typeof(int));
        if (!classSections.Columns.Contains("SectionId") ||
            !classSections.Columns.Contains("SectionName"))
        {
            return result;
        }
        var seen = new HashSet<int>();
        foreach (DataRow row in classSections.Rows)
        {
            if (row["SectionId"] == DBNull.Value)
                continue;
            int id = Convert.ToInt32(row["SectionId"]);
            if (seen.Add(id))
                result.Rows.Add(id, Convert.ToString(row["SectionName"]) ?? string.Empty,
                    classSections.Columns.Contains("SectionDisplayOrder") ? row["SectionDisplayOrder"] : int.MaxValue);
        }
        return LookupDisplayOrder.ForBinding(result, "SectionId");
    }
    private void ClearDisplayedStudents()
    {
        students.Rows.Clear();
        /* The checked-student sets are keyed to the rows just cleared. */
        manuallySelectedAdmissionIds.Clear();
        familySelections.Clear();
        studentSearchVersion++;
        SetStatus("Filter changed. Click Search Students to load students.");
    }
    private void SelectFamilyForStudent(int admissionId)
    {
        try
        {
            var family =
                DatabaseHelper.QueryUser(
                    "GetStudentFamily",
                    DatabaseHelper.Int(
                        "@UserId",
                        CurrentUser.UserId),
                    DatabaseHelper.Int("@AdmissionId", admissionId));
            if (family.Rows.Count == 0)
            {
                UncheckFamily(admissionId);
                Warn("No family members were found for this student.");
                return;
            }
            var familyIds =
                new HashSet<int>();
            foreach (DataRow row
                     in family.Rows)
            {
                if (!family.Columns.Contains("AdmissionId"))
                {
                    continue;
                }
                if (row["AdmissionId"] ==
                    DBNull.Value)
                {
                    continue;
                }
                int memberId =
                    Convert.ToInt32(row["AdmissionId"]);
                if (memberId > 0)
                {
                    familyIds.Add(memberId);
                }
            }
            if (familyIds.Count == 0)
            {
                UncheckFamily(admissionId);
                Warn("No family members were found.");
                return;
            }
            familySelections[
                admissionId] =
                familyIds;
            manuallySelectedAdmissionIds.Remove(admissionId);
            foreach (DataGridViewRow row
                     in students.Rows)
            {
                if (row.IsNewRow)
                    continue;
                int rowAdmissionId =
                    GetRowAdmissionId(row);
                if (!familyIds.Contains(rowAdmissionId))
                {
                    continue;
                }
                row.Cells["Select"].Value =
                    true;
                row.Cells["Family"].Value =
                    rowAdmissionId ==
                    admissionId;
            }
            SetStatus($"{familyIds.Count} family member(s) selected." + SelectionCountText());
        }
        catch (Exception ex)
        {
            familySelections.Remove(admissionId);
            UncheckFamily(admissionId);
            Fail(ex);
        }
    }
    private void UncheckFamily(int admissionId)
    {
        familySelections.Remove(admissionId);
        foreach (DataGridViewRow row
                 in students.Rows)
        {
            if (row.IsNewRow)
                continue;
            if (GetRowAdmissionId(row) !=
                admissionId)
            {
                continue;
            }
            row.Cells["Family"].Value =
                false;
            row.Cells["Select"].Value =
                manuallySelectedAdmissionIds.Contains(
                    admissionId) ||
                IsSelectedThroughAnyFamily(admissionId);
            break;
        }
    }
    private void SelectAllStudents()
    {
        foreach (DataGridViewRow row
                 in students.Rows)
        {
            if (row.IsNewRow)
                continue;
            int admissionId =
                GetRowAdmissionId(row);
            if (admissionId <= 0)
                continue;
            manuallySelectedAdmissionIds.Add(admissionId);
            row.Cells["Select"].Value =
                true;
        }
        SetStatus($"{students.Rows.Count} student(s) selected." + SelectionCountText());
    }
    private void ClearStudentSelection()
    {
        manuallySelectedAdmissionIds.Clear();
        familySelections.Clear();
        foreach (DataGridViewRow row
                 in students.Rows)
        {
            if (row.IsNewRow)
                continue;
            row.Cells["Select"].Value =
                false;
            row.Cells["Family"].Value =
                false;
        }
        SetStatus("Student and family selections cleared.");
        UpdateSelectionActions();
    }
    /// <summary>The ticks survive a new search on purpose (search one family, tick it, search the next).</summary>
    private string SelectionCountText()
    {
        UpdateSelectionActions();
        int includedTotal =
            GetSelectedAdmissionIds().Count;
        if (includedTotal == 0)
        {
            selectionSummary.Text = "No students included";
            return string.Empty;
        }
        int shownTicked = students.Rows
            .Cast<DataGridViewRow>()
            .Count(r => !r.IsNewRow &&
                         Convert.ToBoolean(r.Cells["Select"].Value ?? false));
        int hidden = Math.Max(0, includedTotal - shownTicked);
        selectionSummary.Text = hidden > 0
            ? $"{includedTotal:N0} students included • {hidden:N0} family member(s) outside these search results"
            : $"{includedTotal:N0} student(s) included";
        return hidden > 0
            ? $" {includedTotal:N0} student(s) ticked in total ({hidden:N0} not shown in the current list)."
            : $" {includedTotal:N0} student(s) ticked in total.";
    }
    private void UpdateSelectionActions()
    {
        bool hasStudents = GetSelectedAdmissionIds().Count > 0;
        if (generateStatementButton is not null) generateStatementButton.Enabled = hasStudents&&!BusyOperationRunning;
        if (printAmountsDueButton is not null) printAmountsDueButton.Enabled = hasStudents && !printingDueReport;
    }
    private List<int> GetSelectedAdmissionIds()
    {
        var result =
            new HashSet<int>();
        foreach (int id
                 in manuallySelectedAdmissionIds)
        {
            if (id > 0)
                result.Add(id);
        }
        foreach (HashSet<int> family
                 in familySelections.Values)
        {
            foreach (int id
                     in family)
            {
                if (id > 0)
                    result.Add(id);
            }
        }
        return result.ToList();
    }
    private static Label SummaryValue(Color color)
    {
        return new Label {
            Dock = DockStyle.Fill,
            Font = AppTheme.HeadingFont,
            ForeColor = color,
            TextAlign =
                ContentAlignment.MiddleCenter
        };
    }
    private Control BuildSummary()
    {
        var table =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Padding =
                    new Padding(0, 6, 0, 6),
                Margin =
                    new Padding(0)
            };
        for (int i = 0; i < 4; i++)
        {
            table.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 25f));
        }
        table.Controls.Add(
            SummaryCard("Balance Before Period", openingBalance), 0, 0);
        table.Controls.Add(
            SummaryCard("Charges in Period", chargesTotal), 1, 0);
        table.Controls.Add(
            SummaryCard("Payments in Period", paymentsTotal), 2, 0);
        table.Controls.Add(
            SummaryCard("Balance at To Date", closingBalance), 3, 0);
        return table;
    }
    private static Control SummaryCard(string title, Label value)
    {
        var card =
            new RoundedPanel {
                Dock = DockStyle.Fill,
                BackColor =
                    AppTheme.Surface,
                BorderColor =
                    AppTheme.Border,
                ShowBorder = true,
                CornerRadius =
                    AppTheme.CardRadius,
                Padding =
                    new Padding(8),
                Margin =
                    new Padding(0, 0, 8, 0)
            };
        var root =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding =
                    new Padding(0),
                Margin =
                    new Padding(0)
            };
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                Math.Max(
                    22,
                    AppMetrics.TextHeight(AppTheme.ButtonFont) + 4)));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(
            new Label {
                Text = title,
                Dock = DockStyle.Fill,
                Font =
                    AppTheme.ButtonFont,
                ForeColor =
                    AppTheme.MutedText,
                TextAlign =
                    ContentAlignment.MiddleCenter
            },
            0,
            0);
        root.Controls.Add(value, 0, 1);
        card.Controls.Add(root);
        return card;
    }
    private static int SummaryCardRowHeight()
    {
        return
            12 +
            16 +
            Math.Max(
                22,
                AppMetrics.TextHeight(
                    AppTheme.ButtonFont) + 4) +
            Math.Max(
                30,
                AppMetrics.TextHeight(
                    AppTheme.HeadingFont) + 12) +
            12;
    }
    // It now lives in dbo.GetFeeStatementTransactions -- a verbatim lift of the same query -- for consistency with the rest of the app.
    private static DataTable LoadCurrentFeeStatement(int admissionId, DateTime? fromDate, DateTime? toDate)
    {
        return DatabaseHelper.QueryUser(
            "GetFeeStatementTransactions",
            DatabaseHelper.Int("@AdmissionId", admissionId),
            DatabaseHelper.P("@FromDate", fromDate.HasValue ? fromDate.Value.Date : (object)DBNull.Value),
            DatabaseHelper.P("@ToDate", toDate.HasValue ? toDate.Value.Date : (object)DBNull.Value));
    }
    private static async Task<(DataTable Transactions, decimal OpeningBalance)> LoadStatementDataAsync(
        int admissionId, DateTime? fromDate, DateTime? toDate)
    {
        var data = await DatabaseHelper.QueryUserSetAsync(
            "GetFeeStatementTransactions",
            DatabaseHelper.Int("@AdmissionId", admissionId),
            DatabaseHelper.Date("@FromDate", fromDate),
            DatabaseHelper.Date("@ToDate", toDate),
            DatabaseHelper.Bit("@IncludeOpeningBalance", true));
        if (data.Tables.Count != 2 || data.Tables[1].Rows.Count != 1 ||
            data.Tables[1].Rows[0].IsNull("OpeningBalance"))
            throw new InvalidOperationException("The statement opening balance could not be verified.");
        return (data.Tables[0], Convert.ToDecimal(data.Tables[1].Rows[0]["OpeningBalance"]));
    }
    private async Task LoadStatement()
    {
        try{await RunBusyAsync("Generating statement…",LoadStatementCoreAsync,this);}
        finally{if(!IsDisposed)UpdateSelectionActions();}
    }
    private async Task LoadStatementCoreAsync()
    {
        var request=statementReads.Begin();
        // Every regeneration attempt invalidates the old financial snapshot,
        // including a failed attempt under exactly the same filters.
        _appliedFilterSignature=string.Empty;
        SetReportActionsEnabled(false);
        SetStatus("Generating statement. The previous snapshot is not current and cannot be exported.");
        try
        {
            if (!ReportUi.ValidDateRange(
                    statementDateValidator ??= CreateValidator(),
                    from,
                    to))
            {
                return;
            }
            var admissionIds =
                GetSelectedAdmissionIds();
            if (admissionIds.Count == 0)
            {
                Warn("Select at least one student first.");
                return;
            }
            var combined =
                CreateStatementTable();
            decimal combinedOpening = 0m;
            decimal combinedCharges = 0m;
            decimal combinedPayments = 0m;
            DateTime? fromDate=from.Checked?from.Value.Date:null,toDate=to.Checked?to.Value.Date:null;
            string signature=FilterSignature();
            var visibleNames=students.Rows.Cast<DataGridViewRow>().Where(row=>!row.IsNewRow)
                .GroupBy(GetRowAdmissionId).ToDictionary(group=>group.Key,group=>Convert.ToString(group.First().Cells["StudentName"].Value)??"");
            await SequentialReadBatch.ReadAsync(admissionIds,async id=>{
                var statement=await readStatement(id,fromDate,toDate).ConfigureAwait(false);
                request.Token.ThrowIfCancellationRequested();
                string name=visibleNames.GetValueOrDefault(id,"");
                if(string.IsNullOrWhiteSpace(name)&&statement.Transactions.Columns.Contains("StudentName")&&statement.Transactions.Rows.Count>0)
                    name=Convert.ToString(statement.Transactions.Rows[0]["StudentName"])??"";
                if(string.IsNullOrWhiteSpace(name))
                {
                    var details=await DatabaseHelper.QueryUserAsync("GetStudentDetails",DatabaseHelper.Int("@AdmissionId",id)).ConfigureAwait(false);
                    if(details.Rows.Count==0)throw new InvalidOperationException("A selected statement student could not be verified. Find students again.");
                    name=Convert.ToString(details.Rows[0]["StudentName"])??"";
                }
                return (statement.Transactions,statement.OpeningBalance,Name:name);
            },(id,statement)=>{
                AppendStudentRows(combined,statement.Transactions,statement.Name);
                combinedCharges+=SumDistinctCharges(statement.Transactions);
                combinedPayments+=SumPayments(statement.Transactions);
                combinedOpening+=statement.OpeningBalance;
            },request.Token);
            if(IsDisposed||!statementReads.IsCurrent(request)||signature!=FilterSignature())return;
            if (combined.Columns.Contains("StatementStudent") &&
                combined.Columns.Contains("TransactionDate"))
            {
                combined = await Task.Run(()=>{
                    combined.DefaultView.Sort="StatementStudent ASC, TransactionDate ASC, TransactionType ASC, TransactionId ASC";
                    return combined.DefaultView.ToTable();
                },request.Token);
                if(IsDisposed||!statementReads.IsCurrent(request)||signature!=FilterSignature())return;
            }
            Bind(combined);
            ConfigureStatementGrid();
            FormatStatementAmounts();
            openingBalance.Text =
                FormatAmount(combinedOpening);
            chargesTotal.Text =
                FormatAmount(combinedCharges);
            paymentsTotal.Text =
                FormatAmount(combinedPayments);
            closingBalance.Text =
                FormatAmount(
                    combinedOpening +
                    combinedCharges -
                    combinedPayments);
            SetStatus($"{admissionIds.Count} student(s) included in statement.");
            /* The statement now on screen was built from exactly these students and dates, so Export is enabled with it. */
            _appliedFilterSignature =
                FilterSignature();
            SetReportActionsEnabled(true);
        }
        catch(OperationCanceledException) when(!statementReads.IsCurrent(request)) { }
        catch (Exception ex)
        {
            if(IsDisposed||!statementReads.IsCurrent(request))return;
            Fail(ex);
            SetStatus("Statement could not be refreshed. The previous snapshot is not current; generate again before exporting. "+DbMessage.UserMessage(ex),PageStatusKind.Warning);
        }
    }
    private static DataTable CreateStatementTable()
    {
        return new DataTable();
    }
    private static void AppendStudentRows(DataTable combined, DataTable source, string studentName)
    {
        if (source.Rows.Count == 0)
            return;
        if (combined.Columns.Count == 0)
        {
            foreach (DataColumn column
                     in source.Columns)
            {
                combined.Columns.Add(column.ColumnName, column.DataType);
            }
            if (!combined.Columns.Contains("StatementStudent"))
            {
                combined.Columns.Add("StatementStudent", typeof(string));
            }
        }
        foreach (DataRow sourceRow
                 in source.Rows)
        {
            if (source.Columns.Contains("TransactionType") &&
                string.Equals(Convert.ToString(sourceRow["TransactionType"]), "PAYMENT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var target =
                combined.NewRow();
            foreach (DataColumn column
                     in source.Columns)
            {
                if (!combined.Columns.Contains(column.ColumnName))
                {
                    continue;
                }
                target[column.ColumnName] =
                    sourceRow[column.ColumnName];
            }
            target["StatementStudent"] =
                studentName;
            combined.Rows.Add(target);
        }
    }
    private string GetStudentName(int admissionId)
    {
        foreach (DataGridViewRow row
                 in students.Rows)
        {
            if (row.IsNewRow)
                continue;
            if (GetRowAdmissionId(row) !=
                admissionId)
            {
                continue;
            }
            // Convert.ToString(object) yields String.Empty, never null, for these cell values; the annotation is conservative.
            return Convert.ToString(
                row.Cells["StudentName"]
                    .Value)!;
        }
        try
        {
            var raw =
                DatabaseHelper.QueryUser(
                    "GetStudents",
                    DatabaseHelper.Int(
                        "@CampusId",
                        0),
                    DatabaseHelper.Int(
                        "@AcademicYearId",
                        0),
                    DatabaseHelper.P(
                        "@Search",
                        AdmissionNumbers.ForId(admissionId)),
                    DatabaseHelper.Bit("@IncludeFormerStudents", true));
            foreach (DataRow row
                     in raw.Rows)
            {
                if (!raw.Columns.Contains("AdmissionId"))
                {
                    continue;
                }
                if (row["AdmissionId"] ==
                    DBNull.Value)
                {
                    continue;
                }
                if (Convert.ToInt32(
                        row["AdmissionId"]) !=
                    admissionId)
                {
                    continue;
                }
                if (raw.Columns.Contains("StudentName"))
                {
                    // Convert.ToString(object) yields String.Empty, never null, for these cell values; the annotation is conservative.
                    return Convert.ToString(
                        row["StudentName"])!;
                }
            }
        }
        catch (Exception nonFatalException33)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException33, "StudentAccountStatementPage: GetStudentName fallback");
        }
        return AdmissionNumbers.ForId(admissionId);
    }
    private static decimal SumPayments(DataTable table)
    {
        if (!table.Columns.Contains("PaidAmount"))
        {
            return 0m;
        }
        return table.AsEnumerable()
            .Where(
                row =>
                    !table.Columns.Contains(
                        "TransactionType") ||
                    string.Equals(
                        Convert.ToString(row["TransactionType"]), "PAYMENT", StringComparison.OrdinalIgnoreCase))
            .Sum(
                row =>
                    row["PaidAmount"] ==
                    DBNull.Value
                        ? 0m
                        : Convert.ToDecimal(row["PaidAmount"]));
    }
    private static decimal SumDistinctCharges(DataTable table)
    {
        if (!table.Columns.Contains("DebitAmount"))
        {
            return 0m;
        }
        IEnumerable<DataRow> chargeRows =
            table.AsEnumerable();
        if (table.Columns.Contains("TransactionType"))
        {
            chargeRows =
                chargeRows.Where(
                    row =>
                        string.Equals(
                            Convert.ToString(row["TransactionType"]), "CHARGE", StringComparison.OrdinalIgnoreCase));
        }
        if (!table.Columns.Contains("TransactionId"))
        {
            return chargeRows.Sum(
                row =>
                    row["DebitAmount"] ==
                    DBNull.Value
                        ? 0m
                        : Convert.ToDecimal(row["DebitAmount"]));
        }
        return chargeRows
            .GroupBy(
                row =>
                    Convert.ToInt64(row["TransactionId"]))
            .Sum(
                group =>
                    group.First()["DebitAmount"] ==
                    DBNull.Value
                        ? 0m
                        : Convert.ToDecimal(group.First()["DebitAmount"]));
    }
    private static string FormatAmount(decimal value)
    {
        return value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }
    private void ConfigureStatementGrid()
    {
        Grid.AutoGenerateColumns = true;
        Grid.AllowUserToAddRows = false;
        Grid.AllowUserToDeleteRows = false;
        Grid.ReadOnly = true;
        Grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        Grid.MultiSelect = false;
        Grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        string[] hiddenColumns =
        {
            "TransactionId",
            "AdmissionId",
            "TransactionType",
            "EnrollmentId",
            "CreditAmount",
            "Remarks",
            "PaymentMethod",
            "ReceiptNumber"
        };
        foreach (string name in hiddenColumns)
        {
            if (Grid.Columns.Contains(name))
                Grid.Columns[name].Visible = false;
        }
        SetStatementColumn("StatementStudent", "Student", 170, true);
        SetStatementColumn("TransactionDate", "Date", 88, true);
        SetStatementColumn("FeeTypeName", UiText.Fees.FeeType, 135, true);
        SetStatementColumn("BillingMonth", "Month", 90, true);
        SetStatementColumn("DebitAmount", "Charge", 78, true);
        SetStatementColumn("PaidAmount", "Paid", 70, true);
        SetStatementColumn("Balance", "Due", 70, true);
        if (Grid.Columns.Contains("TransactionDate"))
            Grid.Columns["TransactionDate"].DefaultCellStyle.Format = "dd-MMM-yyyy";
        if (Grid.Columns.Contains("BillingMonth"))
            Grid.Columns["BillingMonth"].DefaultCellStyle.Format = "MMM-yyyy";
        if (Grid.Columns.Contains("DebitAmount"))
            Grid.Columns["DebitAmount"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        if (Grid.Columns.Contains("PaidAmount"))
            Grid.Columns["PaidAmount"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        if (Grid.Columns.Contains("Balance"))
            Grid.Columns["Balance"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        int index = 0;
        foreach (string name in new[]
                 {
                     "StatementStudent",
                     "TransactionDate",
                     "FeeTypeName",
                     "BillingMonth",
                     "DebitAmount",
                     "PaidAmount",
                     "Balance"
                 })
        {
            if (Grid.Columns.Contains(name) && Grid.Columns[name].Visible)
                Grid.Columns[name].DisplayIndex = index++;
        }
    }
    private void SetStatementColumn(string name, string header, int minimumWidth, bool visible = true)
    {
        if (!Grid.Columns.Contains(name))
            return;
        var column = Grid.Columns[name];
        column.Visible = visible;
        column.HeaderText = header;
        column.MinimumWidth = minimumWidth;
    }
    private void FormatStatementAmounts()
    {
        string[] amountColumns =
        {
            "DebitAmount",
            "CreditAmount",
            "PaidAmount",
            "Balance",
            "Amount",
            "OpeningBalance",
            "ClosingBalance"
        };
        foreach (string columnName
                 in amountColumns)
        {
            if (!Grid.Columns.Contains(columnName))
            {
                continue;
            }
            Grid.Columns[columnName]
                .DefaultCellStyle
                .Format = "0";
        }
    }
    /* One print at a time, and the page must still be alive when the institution name comes back. */
    private bool printingDueReport;
    private async Task PrintDueReport()
    {
        /* this method awaited the institution name with no re-entrancy guard and no post-await disposal check, while every other async method in this file re-checks. */
        if (printingDueReport || IsDisposed)
            return;
        printingDueReport = true;
        UpdateSelectionActions();
        try
        {
            if (from.Checked && to.Checked && to.Value.Date < from.Value.Date)
            {
                Warn("To date cannot be earlier than From date.");
                return;
            }
            var admissionIds = GetSelectedAdmissionIds();
            if (admissionIds.Count == 0)
            {
                Warn("Select at least one student first.");
                return;
            }
            var reportRows = new List<DueReportRow>();
            foreach (int admissionId in admissionIds)
            {
                var table =
                    LoadCurrentFeeStatement(admissionId, null, to.Checked ? to.Value.Date : null);
                AddDueReportRows(reportRows, table, admissionId);
            }
            if (reportRows.Count == 0)
            {
                Warn("There are no outstanding fees for the selected student(s).");
                return;
            }
            /* The statement printed a hard-coded institution name. */
            string institutionName =
                await Core.InstitutionProfile.GetNameAsync();
            if (IsDisposed)
                return;
            if (string.IsNullOrWhiteSpace(institutionName))
            {
                Alert(
                    "The school profile could not be read, so nothing was printed. " +
                    "Check School Profile, then try again.");
                return;
            }
            PrintThermalDueReport(institutionName, reportRows);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            printingDueReport = false;
            UpdateSelectionActions();
        }
    }
    private sealed class DueReportRow
    {
        public string StudentName { get; init; } =
            string.Empty;
        public string FeeName { get; init; } =
            string.Empty;
        public string BillingMonth { get; init; } =
            string.Empty;
        public decimal Amount { get; init; }
        public decimal Paid { get; init; }
        public decimal Due { get; init; }
    }
    private void AddDueReportRows(List<DueReportRow> result, DataTable table, int admissionId)
    {
        string studentName=GetStudentName(admissionId);
        if (!table.Columns.Contains("TransactionType"))
            return;
        IEnumerable<DataRow> chargeRows =
        table.AsEnumerable()
            .Where(row =>
                string.Equals(Convert.ToString(row["TransactionType"]), "CHARGE", StringComparison.OrdinalIgnoreCase));
        if (table.Columns.Contains("TransactionId"))
        {
            chargeRows =
                chargeRows
                    .GroupBy(row =>
                        row["TransactionId"] == DBNull.Value
                            ? 0L
                            : Convert.ToInt64(row["TransactionId"]))
                    .Where(group => group.Key != 0)
                    .Select(group => group.First());
        }
        foreach (DataRow row in chargeRows)
        {
            decimal charge =
                GetDecimal(row, "DebitAmount");
            decimal paid =
                GetDecimal(row, "PaidAmount");
            decimal due =
                Math.Max(0m, charge - paid);
            if (due <= 0m)
                continue;
            string feeName =
                table.Columns.Contains("FeeTypeName")
                    ? Convert.ToString(row["FeeTypeName"]) ?? "Fee"
                    : "Fee";
            string billingMonth =
                table.Columns.Contains("BillingMonth")
                    ? FormatBillingMonth(row["BillingMonth"])
                    : string.Empty;
            result.Add(
                new DueReportRow {
                    StudentName = studentName,
                    FeeName = string.IsNullOrWhiteSpace(feeName)
                        ? "Fee"
                        : feeName,
                    BillingMonth = billingMonth,
                    Amount = charge,
                    Paid = paid,
                    Due = due
                });
        }
    }
    private sealed class DueCharge
    {
        public DataRow Row { get; init; } = null!;
        public decimal Amount { get; init; }
        public DateTime Date { get; init; }
    }
    private static DateTime GetTransactionDate(DataRow row)
    {
        string[] columns =
        {
            "TransactionDate",
            "PaymentDate",
            "ChargeDate",
            "CreatedAt"
        };
        foreach (string column in columns)
        {
            if (!row.Table.Columns.Contains(column))
                continue;
            if (row[column] == DBNull.Value)
                continue;
            if (DateTime.TryParse(
                    Convert.ToString(row[column]), out DateTime value))
            {
                return value;
            }
        }
        return DateTime.MaxValue;
    }
    private static decimal GetDecimal(DataRow row, string column)
    {
        if (!row.Table.Columns.Contains(column))
            return 0m;
        if (row[column] == DBNull.Value)
            return 0m;
        return Convert.ToDecimal(row[column]);
    }
    private static string FormatBillingMonth(object value)
    {
        if (value == null ||
            value == DBNull.Value)
        {
            return string.Empty;
        }
        if (DateTime.TryParse(Convert.ToString(value), out DateTime date))
        {
            return date.ToString("MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }
        return Convert.ToString(value)!; // Null and DBNull were both rejected above, so this never yields null.
    }
    private void PrintThermalDueReport(string institutionName, List<DueReportRow> rows)
    {
        const int paperWidthHundredthsInch = 228;
        const int margin = 5;
        int studentCount =
            rows
                .GroupBy(r => r.StudentName)
                .Count();
        int feeCount =
            rows.Count;
        int lineCount = 4;
        lineCount += studentCount * 2;
        lineCount += feeCount;
        lineCount += 3;
        int paperHeightHundredthsInch =
            Math.Max(170, 30 + (lineCount * 13));
        using var document =
            new System.Drawing.Printing.PrintDocument();
        document.DefaultPageSettings.PaperSize =
            new System.Drawing.Printing.PaperSize("Thermal 58mm", paperWidthHundredthsInch, paperHeightHundredthsInch);
        document.DefaultPageSettings.Margins =
            new System.Drawing.Printing.Margins(margin, margin, margin, margin);
        document.PrintPage +=
            (_, e) => {
                using var titleFont =
                    new Font("Arial", 9, FontStyle.Bold);
                using var normalFont =
                    new Font("Arial", 7.5f, FontStyle.Regular);
                using var boldFont =
                    new Font("Arial", 7.5f, FontStyle.Bold);
                // WinForms supplies Graphics on every PrintPage event; it is never null here.
                Graphics graphics =
                    e.Graphics!;
                float left =
                    e.MarginBounds.Left;
                float width =
                    e.MarginBounds.Width;
                float y =
                    e.MarginBounds.Top;
                float lineHeight =
                    12f;
                DrawCentered(graphics, institutionName, boldFont, left, width, ref y);
                DrawCentered(graphics, "FEE DUE", titleFont, left, width, ref y);
                DrawCentered(
                    graphics,
                    DateTime.Now.ToString("dd-MMM-yyyy hh:mm tt"), normalFont, left, width, ref y);
                DrawSeparator(graphics, normalFont, left, width, ref y);
                decimal grandTotal = 0m;
                foreach (var group in rows.GroupBy(row => row.StudentName))
                {
                    DrawWrappedText(graphics, group.Key, boldFont, Brushes.Black, left, width, ref y, lineHeight);
                    foreach (DueReportRow row in group)
                    {
                        string feeText =
                            row.FeeName;
                        if (!string.IsNullOrWhiteSpace(row.BillingMonth))
                        {
                            feeText +=
                                " " +
                                row.BillingMonth;
                        }
                        DrawReceiptLine(graphics, feeText, FormatAmount(row.Due), normalFont, left, width, ref y, lineHeight);
                        grandTotal +=
                            row.Due;
                    }
                    y += 2;
                }
                DrawSeparator(graphics, normalFont, left, width, ref y);
                DrawReceiptLine(graphics, "TOTAL DUE", FormatAmount(grandTotal), boldFont, left, width, ref y, lineHeight);
                y += 3;
                DrawCentered(graphics, "Thank you", normalFont, left, width, ref y);
                e.HasMorePages =
                    false;
            };
        using var preview =
            new PrintPreviewDialog {
                Document = document,
                Width = AppScale.Scale(600),
                Height = AppScale.Scale(700),
                StartPosition =
                    FormStartPosition.CenterScreen
            };
        preview.ShowDialog();
    }
    private static void DrawReceiptLine(
        Graphics graphics,
        string leftText,
        string rightText,
        Font font,
        float x,
        float width,
        ref float y,
        float lineHeight)
    {
        float rightWidth =
            graphics.MeasureString(
                rightText,
                font).Width;
        float rightX =
            x +
            width -
            rightWidth;
        float availableWidth =
            Math.Max(
                30,
                rightX -
                x -
                4);
        string display =
            TrimToWidth(graphics, leftText, font, availableWidth);
        graphics.DrawString(display, font, Brushes.Black, x, y);
        graphics.DrawString(rightText, font, Brushes.Black, rightX, y);
        y += lineHeight;
    }
    private static void DrawCentered(Graphics graphics, string text, Font font, float x, float width, ref float y)
    {
        SizeF size =
            graphics.MeasureString(text, font);
        graphics.DrawString(
            text,
            font,
            Brushes.Black,
            x +
            Math.Max(0, (width - size.Width) / 2), y);
        y += size.Height + 2;
    }
    private static void DrawSeparator(Graphics graphics, Font font, float x, float width, ref float y)
    {
        string separator =
            "--------------------------------";
        separator =
            TrimToWidth(graphics, separator, font, width);
        graphics.DrawString(separator, font, Brushes.Black, x, y);
        y += 13;
    }
    private static void DrawWrappedText(
        Graphics graphics,
        string text,
        Font font,
        Brush brush,
        float x,
        float width,
        ref float y,
        float lineHeight)
    {
        string value =
            string.IsNullOrWhiteSpace(text)
                ? "Student"
                : text.Trim();
        foreach (string part
                 in WrapText(graphics, value, font, width))
        {
            graphics.DrawString(part, font, brush, x, y);
            y += lineHeight;
        }
    }
    private static IEnumerable<string> WrapText(Graphics graphics, string text, Font font, float width)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield return string.Empty;
            yield break;
        }
        string[] words =
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string line =
            string.Empty;
        foreach (string word in words)
        {
            string candidate =
                string.IsNullOrEmpty(line)
                    ? word
                    : line + " " + word;
            if (graphics.MeasureString(candidate, font).Width <= width)
            {
                line = candidate;
                continue;
            }
            if (!string.IsNullOrEmpty(line))
            {
                yield return line;
                line = word;
            }
            else
            {
                yield return
                    TrimToWidth(graphics, word, font, width);
                line = string.Empty;
            }
        }
        if (!string.IsNullOrEmpty(line))
            yield return line;
    }
    private static string TrimToWidth(Graphics graphics, string text, Font font, float width)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        if (graphics.MeasureString(text, font).Width <= width)
        {
            return text;
        }
        const string suffix = "...";
        string result = text;
        while (result.Length > 1 &&
               graphics.MeasureString(result + suffix, font).Width > width)
        {
            result =
                result.Substring(0, result.Length - 1);
        }
        return result + suffix;
    }
    private void ClearForm()
    {
        statementReads.Cancel();
        suppressFilterEvents = true;
        campus.SelectedIndex = -1;
        year.SelectedIndex = -1;
        cls.SelectedIndex = -1;
        section.SelectedIndex = -1;
        suppressFilterEvents = false;
        studentSearchVersion++;
        studentSearch.Clear();
        manuallySelectedAdmissionIds.Clear();
        familySelections.Clear();
        suppressStudentEvents = true;
        students.Rows.Clear();
        suppressStudentEvents = false;
        Grid.DataSource = null;
        try
        {
            Grid.Rows.Clear();
        }
        catch (Exception nonFatalException34)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException34, "StudentAccountStatementPage: ClearForm fallback");
        }
        from.Value =
            DateTime.Today.AddMonths(-1);
        to.Value =
            DateTime.Today;
        from.Checked = to.Checked = false;
        openingBalance.Text =
            "0";
        chargesTotal.Text =
            "0";
        paymentsTotal.Text =
            "0";
        closingBalance.Text =
            "0";
        selectionSummary.Text =
            "No students included";
        /* The statement was cleared, so there is nothing current to export until View Statement runs again. */
        _appliedFilterSignature =
            string.Empty;
        SetReportActionsEnabled(false);
        UpdateSelectionActions();
        SetStatus(
            "Form cleared - every ticked student, including ticks carried over from earlier searches, "
            + "has been cleared. Click Search Students for all students, or use filters to narrow the results.");
    }
}
