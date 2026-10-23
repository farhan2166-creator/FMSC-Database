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
    private readonly bool groupMode;
    private Action? arrangeStatement;
    private ModernButton? familyGroupButton;
    private readonly Label statementContext = new() {
        Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
        Font = AppTheme.ButtonFont, ForeColor = AppTheme.Text,
        Text = "Select a student to view their account."
    };
    private ModernButton? annualReceiptsButton;
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
        : this(initialize,readStatement,false) { }
    internal StudentAccountStatementPage(bool initialize,Func<int,DateTime?,DateTime?,Task<(DataTable Transactions,decimal OpeningBalance)>>? readStatement,bool groupMode)
        : base(groupMode?PageTitles.FamilyGroupStatements:PageTitles.StudentAccountStatement,
            groupMode?"Choose students or families, then view their account statements together.":"See what a student was charged, what was paid and what remains due.")
    {
        this.groupMode=groupMode;
        this.readStatement=readStatement??LoadStatementDataAsync;
        FinanceHelp.Attach(this,openingBalance,"The amount already due before the selected statement period. It is carried into this statement, not newly charged.","Rs. 500 was unpaid before October. The October statement starts with Rs. 500 opening dues.");
        FinanceHelp.Attach(this,closingBalance,"The amount still due after the statement's charges, payments and adjustments. This report does not change the student's account.","Opening dues Rs. 500 plus fees Rs. 400 minus payment Rs. 300 leaves Rs. 600 due.");
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
            if (groupMode) { OnFiltersChanged(); await SearchStudents(); }
            else
            {
                SelectSingleStudent();
                await LoadStatement();
            }
        };
        studentSearch.SelectionCleared += (_, _) => {
            if (!groupMode) ClearSingleStudent();
        };
        studentSearch.KeyDown +=
            async (_, e) => {
                if (e.KeyCode != Keys.Enter || e.Handled)
                    return;
                e.SuppressKeyPress = true;
                e.Handled = true;
                /* SearchStudents returns Task, so it must be awaited. */
                if (groupMode) await SearchStudents();
                else if (studentSearch.SelectedId > 0) { SelectSingleStudent(); await LoadStatement(); }
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
        /* Statement scope includes checked students, date range and student-search filters. */
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
        if(initialize&&groupMode)Defer(BindContextLookups);
        ConfigureStudentGrid();
        Grid.DataBindingComplete += (_, _) => { ConfigureStatementGrid(); FormatStatementAmounts(); };
        BuildStatementLayout();
        ClearForm();
    }
    private void BuildStatementLayout()
    {
        var filters = HumanUi.Card(groupMode?"Find Students / Families":"Student Account",
            groupMode?"Placement filters find students. Tick students or Include Family below, then view their statements for the chosen dates.":"Leave dates unchecked for the complete account.");
        int headerHeight=filters.Header.Height;
        var filterGrid = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty
        };
        foreach (float width in groupMode?new[] { 30f, 30f, 20f, 20f }:new[] { 25f, 25f, 25f, 25f })
            filterGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        int fieldHeight = Math.Max(AppMetrics.FieldRowHeight,
            AppMetrics.TextHeight(AppTheme.FieldCaptionFont) + AppMetrics.InputControlHeight
            + AppMetrics.SpacingSm + Math.Max(studentSearch.Margin.Vertical, from.Margin.Vertical));
        filterGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        filterGrid.Controls.Add(HumanUi.Field(groupMode?"Find a student (optional)":"Student", studentSearch), 0, 0);
        filterGrid.SetColumnSpan(studentSearch.Parent!, 2);
        filterGrid.Controls.Add(HumanUi.Field("From (optional)", from), 2, 0);
        filterGrid.Controls.Add(HumanUi.Field("To (optional)", to), 3, 0);
        foreach (TableLayoutPanel field in filterGrid.Controls.OfType<TableLayoutPanel>())
            field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var groupFilters = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty, Visible = groupMode
        };
        foreach (var item in new[] { ("Campus", campus), ("Placement Year", year), ("Class", cls), ("Section", section) })
        {
            groupFilters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            groupFilters.Controls.Add(HumanUi.Field(item.Item1, item.Item2), groupFilters.Controls.Count, 0);
        }
        var find = CreateFilterButton("Find Students", AppTheme.Primary);
        find.Click += async (_, _) => await SearchStudents();
        generateStatementButton = CreateFilterButton("View Statement", AppTheme.Primary);
        generateStatementButton.Click += async (_, _) => await LoadStatement();
        var clear = CreateFilterButton("Clear", AppTheme.MutedText);
        if(!groupMode)clear.MinimumSize=new Size(AppScale.Scale(90),AppMetrics.ButtonHeight);
        clear.Click += (_, _) => { ClearForm(); if(groupMode)_ = ReloadSections(); };
        SetToolTip(clear, "Clear this statement's selection, dates and displayed results.");
        var actions = new FlowLayoutPanel {
            Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty, Padding = Padding.Empty
        };
        foreach (var button in new[] { generateStatementButton, clear })
        {
            AppMetrics.FitButtonWidth(button, minWidth: button==clear&&!groupMode?AppScale.Scale(90):110);
            actions.Controls.Add(button);
        }
        var modeBar = new FlowLayoutPanel {
            Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty, Padding = Padding.Empty
        };
        if(groupMode)modeBar.Controls.Add(find);
        else
        {
            familyGroupButton=CreateFilterButton("Family / Group Statements…",AppTheme.MutedText);
            familyGroupButton.Click+=(_,_)=>OpenFamilyGroupStatements();
            printAmountsDueButton=CreateFilterButton("Outstanding Bills…",AppTheme.MutedText);
            printAmountsDueButton.Click+=async(_,_)=>await PrintDueReport();
            SetToolTip(printAmountsDueButton,"Open current unpaid bills for this student. Statement dates do not limit these bills.");
            annualReceiptsButton=CreateFilterButton("Annual Receipts…",AppTheme.MutedText);
            annualReceiptsButton.Click+=async(_,_)=>await OpenAnnualReceipts();
            SetToolTip(annualReceiptsButton,"Open the separate annual receipts report for this student and choose its academic year.");
            foreach(var button in new[]{familyGroupButton,printAmountsDueButton,annualReceiptsButton})
            {
                AppMetrics.FitButtonWidth(button,minWidth:110);
                button.Margin=new Padding(4,0,4,0);modeBar.Controls.Add(button);
            }
            filters.Header.Controls.Add(modeBar);
            filters.Header.Controls.SetChildIndex(modeBar,filters.Header.Controls.Count-1);
        }
        var filterBody = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty
        };
        filterBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filterBody.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldHeight));
        var groupFilterRow = new RowStyle(SizeType.Absolute, 0);
        filterBody.RowStyles.Add(groupFilterRow);
        filterBody.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
        var actionsRow = new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight);
        filterBody.RowStyles.Add(actionsRow);
        filterBody.Controls.Add(filterGrid, 0, 0);
        filterBody.Controls.Add(groupFilters, 0, 1);
        if(groupMode)filterBody.Controls.Add(modeBar,0,2);
        filterBody.Controls.Add(actions,0,3);
        filters.Content.Controls.Add(filterBody);

        var studentCard = HumanUi.Card("Choose Students / Families", "Tick students or Include Family. Selections can span searches.");
        var selectAll = CreateFilterButton("Select All Results", AppTheme.MutedText);
        var deselect = CreateFilterButton("Clear Selection", AppTheme.MutedText);
        selectAll.Click += (_, _) => { SelectAllStudents(); OnFiltersChanged(); };
        deselect.Click += (_, _) => { ClearStudentSelection(); OnFiltersChanged(); };
        var selectionBody = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty
        };
        selectionBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selectionBody.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.TextHeight(AppTheme.NormalFont) + 8));
        selectionBody.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
        selectionBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        selectionBody.RowStyles.Add(new RowStyle(SizeType.Absolute,groupMode?AppMetrics.ButtonRowHeight:0));
        selectionBody.Controls.Add(selectionSummary, 0, 0);
        var selectionActions = HumanUi.ButtonRow(selectAll, deselect);
        selectionActions.Dock = DockStyle.Fill;
        selectionActions.Margin = Padding.Empty;
        selectionBody.Controls.Add(selectionActions, 0, 1);
        selectionBody.Controls.Add(students, 0, 2);
        if(groupMode)selectionBody.Controls.Add(actions,0,3);
        studentCard.Content.Controls.Add(selectionBody);
        var statement = HumanUi.Card("Account Details", "Fee charges with allocated payments and remaining amounts.", accessory: exportButton);
        statement.Header.Controls.SetChildIndex(exportButton, statement.Header.Controls.Count - 1);
        statement.Content.Controls.Add(Grid);
        var summary = BuildSummary();
        var root = new TableLayoutPanel {
            ColumnCount = 1, RowCount = 6, Margin = Padding.Empty, Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var filterRow = new RowStyle(SizeType.Absolute);
        var selectionRow = new RowStyle(SizeType.Absolute, 0);
        root.RowStyles.Add(filterRow);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.TextHeight(AppTheme.ButtonFont) + 12));
        root.RowStyles.Add(selectionRow);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, SummaryCardRowHeight()));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.TextHeight(AppTheme.NormalFont) * 2 + 8));
        root.Controls.Add(filters, 0, 0);
        root.Controls.Add(statementContext, 0, 1);
        root.Controls.Add(studentCard, 0, 2);
        root.Controls.Add(summary, 0, 3);
        root.Controls.Add(statement, 0, 4);
        root.Controls.Add(StatusLabel, 0, 5);
        var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        viewport.Controls.Add(root);
        int actionWidthNeeded=actions.Controls.Cast<Control>().Sum(control=>control.Width+control.Margin.Horizontal);
        if(!groupMode)
        {
            filterGrid.ColumnCount=5;
            filterGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,0));
        }
        bool arranging = false;
        arrangeStatement = () => {
            if (arranging) return;
            arranging = true;
            try
            {
                root.SuspendLayout();
                groupFilterRow.Height = groupMode ? fieldHeight : 0;
                studentCard.Visible = groupMode;
                int width = Math.Max(AppScale.Scale(720), viewport.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
                int actionWidth = width - filters.Padding.Horizontal - filters.Content.Padding.Horizontal;
                bool inlineActions=!groupMode&&actionWidth>=AppScale.Scale(880);
                if(!groupMode)
                {
                    int relatedWidth=modeBar.Controls.Cast<Control>().Sum(control=>control.Width+control.Margin.Horizontal);
                    bool stackReports=actionWidth<relatedWidth+AppScale.Scale(250);
                    filters.Header.Height=headerHeight+(stackReports?AppMetrics.ButtonRowHeight:0);
                    modeBar.Dock=stackReports?DockStyle.Bottom:DockStyle.Right;
                    modeBar.Width=relatedWidth;modeBar.Height=stackReports?AppMetrics.ButtonRowHeight:headerHeight;
                    filterGrid.ColumnStyles[4].Width=inlineActions?actionWidthNeeded:0;
                    if(inlineActions)
                    {
                        if(actions.Parent!=filterGrid)filterGrid.Controls.Add(actions,4,0);
                        actions.Padding=new Padding(0,Math.Max(16,AppMetrics.TextHeight(AppTheme.FieldCaptionFont))-3,0,0);
                    }
                    else
                    {
                        if(actions.Parent!=filterBody)filterBody.Controls.Add(actions,0,3);
                        actions.Padding=Padding.Empty;
                    }
                }
                int NeededHeight(FlowLayoutPanel bar)
                {
                    int rows=1,usedWidth=0;
                    foreach(Control control in bar.Controls)
                    {
                        int required=control.Width+control.Margin.Horizontal;
                        if(usedWidth>0&&usedWidth+required>actionWidth){rows++;usedWidth=0;}
                        usedWidth+=required;
                    }
                    return AppMetrics.ButtonRowHeight*rows;
                }
                filterBody.RowStyles[2].Height=groupMode?NeededHeight(modeBar):0;
                actionsRow.Height=groupMode||inlineActions?0:NeededHeight(actions);
                selectionBody.RowStyles[3].Height=groupMode?NeededHeight(actions):0;
                filterRow.Height = filters.Padding.Vertical + filters.Header.Height + filters.Content.Padding.Vertical
                    + fieldHeight * (groupMode ? 2 : 1) + filterBody.RowStyles[2].Height + actionsRow.Height;
                selectionRow.Height = groupMode ? studentCard.Padding.Vertical + studentCard.Header.Height
                    + studentCard.Content.Padding.Vertical + AppMetrics.TextHeight(AppTheme.NormalFont) + 8
                    + AppMetrics.ButtonRowHeight + NeededHeight(actions) + students.ColumnHeadersHeight + AppMetrics.GridRowHeight * 4 : 0;
                int fixedHeight = root.RowStyles.Cast<RowStyle>().Where(r => r.SizeType == SizeType.Absolute).Sum(r => (int)r.Height);
                int statementHeight = statement.Padding.Vertical + statement.Header.Height + statement.Content.Padding.Vertical
                    + Grid.ColumnHeadersHeight + AppMetrics.GridRowHeight * 5 + SystemInformation.HorizontalScrollBarHeight;
                root.Size = new Size(width,
                    Math.Max(viewport.ClientSize.Height, fixedHeight + statementHeight));
                root.ResumeLayout(true);
            }
            finally { arranging = false; }
        };
        viewport.SizeChanged += (_, _) => arrangeStatement();
        SetContent(viewport);
        arrangeStatement();
    }
    private void SelectSingleStudent()
    {
        statementReads.Cancel();
        Grid.DataSource = null;
        openingBalance.Text = chargesTotal.Text = paymentsTotal.Text = closingBalance.Text = "—";
        _appliedFilterSignature = string.Empty;
        SetReportActionsEnabled(false);
        manuallySelectedAdmissionIds.Clear();
        familySelections.Clear();
        students.Rows.Clear();
        int id = studentSearch.SelectedId;
        if (id > 0)
        {
            manuallySelectedAdmissionIds.Add(id);
            students.Rows.Add(true, false, id, studentSearch.Text, "", "");
        }
        statementContext.Text = id > 0 ? studentSearch.Text : "Select a student to view their account.";
        OnFiltersChanged();
    }
    private void ClearSingleStudent()
    {
        SelectSingleStudent();
        SetStatus("Select a student to view their charges, payments and balance.");
    }
    private Task OpenAnnualReceipts()
    {
        if(IsBusy||IsDisposed||!CurrentUser.HasPermission("Fees.Statement.View"))return Task.CompletedTask;
        var ids=GetSelectedAdmissionIds();
        if(ids.Count!=1)return Task.CompletedTask;
        try
        {
            using var dialog=CreateRelatedReportDialog(PageTitles.AnnualFeeReceipts,new AnnualFeeReceiptsPage(ids[0]));
            dialog.ShowDialog(FindForm());
        }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
        return Task.CompletedTask;
    }
    public static StudentAccountStatementPage CreateFamilyGroupStatements(int admissionId=0)
    {
        var page=new StudentAccountStatementPage(true,null,true);
        if(admissionId>0)page.Defer(()=>page.PreloadAdmissionIdAsync(admissionId));
        return page;
    }
    private void OpenFamilyGroupStatements()
    {
        if(IsBusy||IsDisposed||!CurrentUser.HasPermission("Fees.Statement.View"))return;
        try
        {
            var page=CreateFamilyGroupStatements(studentSearch.SelectedId);
            page.from.Value=from.Value;page.from.Checked=from.Checked;
            page.to.Value=to.Value;page.to.Checked=to.Checked;
            using var dialog=CreateRelatedReportDialog(PageTitles.FamilyGroupStatements,page);
            dialog.ShowDialog(FindForm());
        }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
    }
    private static ApplicationWindowForm CreateRelatedReportDialog(string title,Control page)
    {
        var dialog=new ApplicationWindowForm { Text=title,StartPosition=FormStartPosition.CenterParent,
            ClientSize=new Size(AppScale.Scale(1180),AppScale.Scale(760)),
            MinimumSize=new Size(AppScale.Scale(820),AppScale.Scale(560)),ShowInTaskbar=false,MinimizeBox=false };
        page.Dock=DockStyle.Fill;dialog.Controls.Add(page);ModalPageCloseGuard.Attach(dialog,page);
        return dialog;
    }
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
            var selected = students.Rows.Cast<DataGridViewRow>().FirstOrDefault(row => GetRowAdmissionId(row) == admissionId);
            if (selected is null)
            {
                ClearSingleStudent();
                SetStatus("This student could not be found. Choose a student before viewing their account.", PageStatusKind.Warning);
                return;
            }
            studentSearch.SetStudent(admissionId, Convert.ToString(selected.Cells["StudentName"].Value) ?? AdmissionNumbers.ForId(admissionId));
            statementContext.Text = groupMode?$"Group statement · {GetSelectedAdmissionIds().Count:N0} student(s) included":studentSearch.Text;
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
    /* Include search scope, checked students and family expansion in the statement signature. */
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
            SetStatus("Filters or student changed. Use View Statement again before exporting.", true);
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
    // Return Task so preload callers can await the student search.
    private async Task SearchStudents()
    {
        try
        {
            int version = ++studentSearchVersion;
            string search =
                studentSearch.SelectedId > 0
                    ? AdmissionNumbers.ForId(studentSearch.SelectedId)
                    : studentSearch.Text.Trim();
            /* An admission-number search is independent of campus/year/class/section filters. */
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
    /// <summary>Deduplicate sections returned once per campus/class placement.</summary>
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
        SetStatus("Filter changed. Use Find Students to load students.");
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
        if (printAmountsDueButton is not null) printAmountsDueButton.Enabled = hasStudents && !IsBusy && CurrentUser.HasPermission("Fees.View");
        if(familyGroupButton is not null)familyGroupButton.Enabled=!IsBusy&&CurrentUser.HasPermission("Fees.Statement.View");
        if (annualReceiptsButton is not null) annualReceiptsButton.Enabled = GetSelectedAdmissionIds().Count == 1
            && !IsBusy && CurrentUser.HasPermission("Fees.Statement.View");
        if (groupMode) statementContext.Text = $"Group statement · {GetSelectedAdmissionIds().Count:N0} student(s) included";
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
            SummaryCard("Opening Balance", openingBalance), 0, 0);
        table.Controls.Add(
            SummaryCard("Charges in Period", chargesTotal), 1, 0);
        table.Controls.Add(
            SummaryCard("Payments in Period", paymentsTotal), 2, 0);
        table.Controls.Add(
            SummaryCard("Period Closing Balance", closingBalance), 3, 0);
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
            UiHelper.SetGridEmptyMessage(Grid, "No account entries for the selected students and dates.");
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
        return value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
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
                .Format = "N0";
        }
    }
    /* One print at a time, and the page must still be alive when the institution name comes back. */
    private bool printingDueReport;
    private async Task PrintDueReport()
    {
        if(IsBusy || IsDisposed || !CurrentUser.HasPermission("Fees.View"))return;
        var ids=GetSelectedAdmissionIds();
        if(ids.Count==0){Warn("Select at least one student first.");return;}
        string signature=FilterSignature();
        printingDueReport=true;UpdateSelectionActions();
        try
        {
            using var busy=BeginBusy("Preparing outstanding bills…",this);
            await OutstandingBillsPreview.ShowAsync(FindForm(),ids,()=>!IsDisposed && signature==FilterSignature(),Warn);
        }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
        finally{printingDueReport=false;if(!IsDisposed)UpdateSelectionActions();}
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
            "—";
        chargesTotal.Text =
            "—";
        paymentsTotal.Text =
            "—";
        closingBalance.Text =
            "—";
        selectionSummary.Text =
            "No students included";
        /* The statement was cleared, so there is nothing current to export until View Statement runs again. */
        _appliedFilterSignature =
            string.Empty;
        SetReportActionsEnabled(false);
        UpdateSelectionActions();
        statementContext.Text = groupMode ? "Find students, then tick those to include." : "Select a student to view their account.";
        UiHelper.SetGridPlaceholderMessage(Grid, groupMode ? "Find students, then select those to include." : "Select a student to view their account.");
        UiHelper.SetGridEmptyMessage(Grid, groupMode ? "Find students, then select those to include." : "Select a student to view their account.");
        SetStatus(groupMode ? "All selections cleared. Use Find Students, then tick students or Include Family."
            : "Select a student above. The complete account is shown unless you choose dates.");
    }
}
