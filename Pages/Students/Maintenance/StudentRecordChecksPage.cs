using System.Text;
using System.Text.Json;
using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Students.Maintenance;
public sealed class StudentRecordChecksPage : V2Page
{
    private readonly StudentPicker student = new();
    private string gridMode = "Findings";
    /* The three mode loads are async but were guarded only against the student changing. */
    private int gridModeVersion;
    private DataTable enrollmentsRaw = new();
    private DataTable findingsRaw = new();
    private readonly Label findingsSummary = new() {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        Padding = new Padding(0, 0, 8, 0),
        ForeColor = AppTheme.MutedText,
        AutoEllipsis = true,
        UseMnemonic = false
    };
    private readonly ModernButton modeFindings = new() {
        Text = "Issues",
        Dock = DockStyle.Fill,
        ThemeRole = "Primary",
        Margin = new Padding(0, 4, 4, 4)
    };
    private readonly ModernButton modeEnrollments = new() {
        Text = "Placements",
        Dock = DockStyle.Fill,
        ThemeRole = "TabInactive",
        Margin = new Padding(0, 4, 4, 4)
    };
    private readonly ModernButton modeTimeline = new() {
        Text = "History",
        Dock = DockStyle.Fill,
        ThemeRole = "TabInactive",
        Margin = new Padding(0, 4, 4, 4)
    };
    private readonly ModernComboBox statusCombo = HumanUi.Combo();
    private readonly ModernDatePicker withdrawalDate =
        HumanUi.Date();
    /* The reason boxes are two lines high now that they sit in a narrow side panel: the canned quick-fix reasons are longer than one line of it. */
    private readonly ModernTextBox statusReason =
        HumanUi.TextBox(true, 500);
    private readonly LookupBox classSection = new();
    private readonly LookupBox academicYear = new();
    private readonly ModernComboBox enrollmentStatusCombo = HumanUi.Combo();
    private readonly ModernDatePicker enrollmentDate = HumanUi.Date();
    private readonly ModernDatePicker leavingDate = HumanUi.Date();
    private readonly ModernDatePicker billingStartDate = HumanUi.Date();
    private readonly CheckBox allowLegacyDates = new() {Text="Allow dates outside year / overlapping history",AutoSize=true};
    private readonly ModernTextBox enrollmentReason =
        HumanUi.TextBox(true, 500);
    private int selectedEnrollmentId;
    private readonly Label enrollmentSelection = new() {AutoSize=true,Dock=DockStyle.Fill};
    private ModernButton saveEnrollmentAction=null!;
    private ModernButton addEnrollmentAction=null!;
    private ModernButton repairDatesAction=null!;
    private readonly ModernButton newEnrollmentAction=new() {Text="Add Missing Record",ThemeRole="Secondary"};
    private bool creatingEnrollment;
    /* One line of read-only context (registration no., placement, date of birth, phone) in place of four separate boxes: it says the same thing in a fraction of the height. */
    private const string NoStudentText =
        "No student selected - Scan checks every student in the school. " +
        "Pick a student to check and correct one.";
    private readonly Label studentSummary = new() {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 8, 0),
        BackColor = AppTheme.SurfaceSecondary,
        ForeColor = AppTheme.MutedText,
        Font = AppTheme.NormalFont,
        AutoEllipsis = true,
        UseMnemonic = false,
        Text = NoStudentText
    };
    /* Free-text filter over the rows on screen. */
    private readonly ModernTextBox rowFilter = new() {
        PlaceholderText = "Filter rows...",
        MaxLength = 60,
        Dock = DockStyle.Fill,
        Margin = new Padding(0, 4, 8, 4)
    };
    private bool rowFilterSyncing;
    /* Corrections side panel. */
    private enum CorrectionTab
    {
        Status,
        Enrollment
    }
    private readonly ModernButton correctionStatusTab = new() {
        Text = "Student Status",
        Dock = DockStyle.Fill,
        ThemeRole = "Primary",
        Margin = new Padding(0, 4, 4, 4)
    };
    private readonly ModernButton correctionEnrollmentTab = new() {
        Text = "Placement",
        Dock = DockStyle.Fill,
        ThemeRole = "TabInactive",
        Margin = new Padding(0, 4, 0, 4)
    };
    private readonly Label correctionTitle = new() {
        Text = "Correct Record",
        Dock = DockStyle.Fill,
        Font = UiDesignSystem.Typography.Action,
        ForeColor = AppTheme.HeadingText,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        UseMnemonic = false
    };
    private readonly Label correctionSubtitle = new() {
        Text = "No student selected",
        Dock = DockStyle.Fill,
        Font = UiDesignSystem.Typography.Body,
        ForeColor = AppTheme.MutedText,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        UseMnemonic = false
    };
    private TableLayoutPanel bodyLayout = null!;
    private RoundedPanel correctionPanel = null!;
    private Control statusPanel = null!;
    private Control enrollmentPanel = null!;
    private Control statusFooter = null!;
    private Control enrollmentFooter = null!;
    private int correctionPanelWidth;
    private int bodyGap;
    private bool CanCorrect => CurrentUser.HasPermission("Students.Administer");
    private readonly Action<Func<Control>, string, string>? navigate;
    public StudentRecordChecksPage(Action<Func<Control>, string, string>? navigate = null)
        : base(
            PageTitles.StudentRecordChecks,
            "Review discrepancies, then open Admission to correct the student record.")
    {
        this.navigate = navigate;
        /* all three of these write corrections, so each is guarded against a double submit. Scan and Export only read. */
        var scan = AddBusyAction("Scan", () => SetGridMode("Findings"), "Primary");
        var saveStatus = AddBusyAction("Save Status", SaveStatus, "Success");
        var saveEnrollment = saveEnrollmentAction = AddBusyAction("Save Placement", SaveEnrollment, "Success");
        var addEnrollment = addEnrollmentAction = AddBusyAction("Create Missing Record", AddEnrollment, "Success");
        var repairDates=repairDatesAction=AddBusyAction("Review Dates",()=>RepairDatesFromEditor(),"Secondary");
        newEnrollmentAction.Click+=(_,_)=>BeginMissingEnrollment();
        GateAction(repairDates,"Students.Administer");
        var export = AddExportAction("Student_Consistency_Findings");
        var clear = AddBusyAction(UiText.Actions.Clear, () => ClearForm(), "Secondary");
        /* this page is reachable with Students.View alone, but every one of these three writes goes to a procedure that requires Students.Administer (dbo.CorrectStudentStatus, dbo.CorrectEnrollmentStatus, dbo.AddEnrollment...). Scan/Export/Clear stay - they only read. */
        GateAction(saveStatus, "Students.Administer");
        GateAction(saveEnrollment, "Students.Administer");
        GateAction(addEnrollment, "Students.Administer");
        RelocateFromActions(saveStatus, saveEnrollment, addEnrollment, repairDates);
        /* The constructor cannot await and BuildStudentSearch is now async Task. The discard marks that as deliberate. */
        _ = BuildStudentSearch();
        BuildStatusEditor();
        BuildEnrollmentEditor();
        modeFindings.Click += async (_, _) => await SetGridMode("Findings");
        modeEnrollments.Click += async (_, _) => await SetGridMode("Enrollments");
        modeTimeline.Click += async (_, _) => await SetGridMode("Timeline");
        Grid.CurrentCellChanged += (_, _) => OnGridSelectionChanged();
        Grid.CellContentClick += (_, e) => OnGridCellContentClick(e);
        Grid.CellFormatting += (_, e) => OnGridCellFormatting(e);
        rowFilter.TextChanged += (_, _) => ApplyRowFilter();
        correctionStatusTab.Click += (_, _) => ShowCorrectionTab(CorrectionTab.Status);
        correctionEnrollmentTab.Click += async (_, _) => {
            ShowCorrectionTab(CorrectionTab.Enrollment);
            /* The form edits a row of the Enrollments list, so show the list with it. */
            if (gridMode != "Enrollments" && student.SelectedId > 0)
                await SetGridMode("Enrollments");
        };
        bodyGap = AppMetrics.SpacingMd;
        correctionPanelWidth = AppScale.Scale(460);
        /* Student bar: the picker plus one line of context, no card header. */
        var studentBar = new RoundedPanel {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            BorderColor = AppTheme.Border,
            ShowBorder = true,
            CornerRadius = AppTheme.CardRadius,
            Padding = new Padding(UiDesignSystem.Spacing.Md, UiDesignSystem.Spacing.Sm, UiDesignSystem.Spacing.Md, 0),
            Margin = new Padding(0, 0, 0, bodyGap)
        };
        studentBar.Controls.Add(BuildStudentRow());
        /* Results: mode tabs, filter and summary on one slim toolbar, then the grid. */
        var toolbar = BuildResultsToolbar();
        var resultsInner = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        resultsInner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        resultsInner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        resultsInner.Controls.Add(toolbar, 0, 0);
        resultsInner.Controls.Add(Grid, 0, 1);
        var resultsCard = new RoundedPanel {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            BorderColor = AppTheme.Border,
            ShowBorder = true,
            CornerRadius = AppTheme.CardRadius,
            Padding = new Padding(UiDesignSystem.Spacing.Sm),
            Margin = new Padding(0)
        };
        resultsCard.Controls.Add(resultsInner);
        correctionPanel = BuildCorrectionPanel(saveStatus, saveEnrollment, addEnrollment);
        correctionPanel.Enabled = CanCorrect;
        correctionPanel.Margin = new Padding(bodyGap, 0, 0, 0);
        bodyLayout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, correctionPanelWidth + bodyGap));
        bodyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bodyLayout.Controls.Add(resultsCard, 0, 0);
        bodyLayout.Controls.Add(correctionPanel, 1, 0);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight + AppMetrics.SpacingSm + bodyGap));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(studentBar, 0, 0);
        root.Controls.Add(bodyLayout, 0, 1);
        SetContent(root);
        // Admission owns corrections; this page remains a diagnostic/history view.
        correctionPanel.Visible = false;
        bodyLayout.ColumnStyles[1].Width = 0;
        bodyLayout.SetColumnSpan(resultsCard, 2);
        newEnrollmentAction.Visible = false;
        var openAdmission = AddAction("Open Admission", (_, _) => OpenAdmissionForCorrection(), "Primary");
        GateActionAny(openAdmission, "Students.Admit", "Students.Administer");
    }
    public StudentRecordChecksPage(int preloadAdmissionId) : this()
    {
        if (preloadAdmissionId > 0) Defer(async () => {
            var found = await StudentSearchCache.FindByAdmissionIdAsync(preloadAdmissionId);
            if (IsDisposed || student.SelectedId > 0 || !string.IsNullOrWhiteSpace(student.Text)) return;
            if (found is null) { SetStatus("This student could not be found. Search for a student to continue.", true); return; }
            student.SetStudent(found);
            await LoadStudentSnapshot();
        });
    }
    private Control BuildResultsToolbar()
    {
        var toolbar = new FlowLayoutPanel {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        foreach(var button in new[]{modeFindings,modeEnrollments,modeTimeline,newEnrollmentAction}) {
            button.Dock=DockStyle.None;
            button.Size=new Size(AppMetrics.TextWidth(button.Text,AppTheme.ButtonFont,44),AppMetrics.ButtonHeight);
            button.Margin=new Padding(0,4,4,4);
        }
        rowFilter.Dock=DockStyle.None;rowFilter.Size=new Size(AppScale.Scale(190),AppMetrics.InputControlHeight);
        findingsSummary.Dock=DockStyle.None;findingsSummary.AutoSize=true;findingsSummary.Margin=new Padding(4,10,0,4);
        toolbar.Controls.AddRange(new Control[]{modeFindings,modeEnrollments,modeTimeline,rowFilter,newEnrollmentAction,findingsSummary});
        return toolbar;
    }
    private RoundedPanel BuildCorrectionPanel(
        ModernButton saveStatus,
        ModernButton saveEnrollment,
        ModernButton addEnrollment)
    {
        var panel = new RoundedPanel {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            BorderColor = AppTheme.Border,
            ShowBorder = true,
            CornerRadius = AppTheme.CardRadius,
            Padding = new Padding(UiDesignSystem.Spacing.Md),
            Margin = new Padding(0)
        };
        int headerHeight = AppMetrics.ButtonHeight + AppMetrics.TextHeight(AppTheme.NormalFont) + 6;
        int tabsHeight = AppMetrics.ButtonHeight + 8;
        var header = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(correctionTitle, 0, 0);
        header.Controls.Add(correctionSubtitle, 0, 1);
        var tabs = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tabs.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tabs.Controls.Add(correctionStatusTab, 0, 0);
        tabs.Controls.Add(correctionEnrollmentTab, 1, 0);
        statusPanel = BuildStatusPanel(saveStatus);
        enrollmentPanel = BuildEnrollmentPanel(saveEnrollment, addEnrollment);
        enrollmentPanel.Visible = false;
        var content = new Panel {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        content.Controls.Add(enrollmentPanel);
        content.Controls.Add(statusPanel);
        statusFooter=CorrectionFooter(saveStatus);
        enrollmentFooter=CorrectionFooter(saveEnrollment,addEnrollment);
        enrollmentFooter.Visible=false;
        var footer=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};
        footer.Controls.Add(statusFooter);footer.Controls.Add(enrollmentFooter);
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, headerHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, tabsHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,AppMetrics.ButtonRowHeight));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(tabs, 0, 1);
        layout.Controls.Add(content, 0, 2);
        layout.Controls.Add(footer,0,3);
        panel.Controls.Add(layout);
        return panel;
    }
    private static Control CorrectionFooter(params ModernButton[] buttons)
    {
        var footer=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,
            WrapContents=false,Margin=Padding.Empty,Padding=new Padding(0,AppMetrics.SpacingSm,0,0)};
        foreach(var button in buttons) {
            button.Dock=DockStyle.None;
            button.Size=new Size(AppMetrics.TextWidth(button.Text,AppTheme.ButtonFont,44),AppMetrics.ButtonHeight);
            button.Margin=new Padding(4,0,0,0);footer.Controls.Add(button);
        }
        return footer;
    }
    private static Label CorrectionHint(string text) => new() {
        Text = text,
        Dock = DockStyle.Fill,
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.MutedText,
        Padding = new Padding(UiDesignSystem.Spacing.Xs, 2, UiDesignSystem.Spacing.Xs, 0),
        UseMnemonic = false
    };
    private static int CorrectionReasonRowHeight =>
        AppMetrics.FieldRowHeightFor(AppMetrics.MultilineInputHeight(58));
    private static TableLayoutPanel CorrectionForm(int rows)
    {
        var form = new TableLayoutPanel {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = rows,
            AutoSize = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        return form;
    }
    private Control BuildStatusPanel(ModernButton saveStatus)
    {
        int hintHeight = AppMetrics.TextHeight(AppTheme.NormalFont) * 3 + 8;
        int fieldHeight = AppMetrics.FieldRowHeight;
        int reasonHeight = CorrectionReasonRowHeight;
        var form = CorrectionForm(3);
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, hintHeight));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldHeight));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, reasonHeight));
        form.Height = hintHeight + fieldHeight + reasonHeight;
        var hint = CorrectionHint("Correct overall status here. For an existing departure, use Placement → Correct Leaving Date.");
        form.Controls.Add(hint, 0, 0);
        form.SetColumnSpan(hint, 2);
        form.Controls.Add(HumanUi.Field("Student Status", statusCombo, fillHeight: false), 0, 1);
        form.Controls.Add(HumanUi.Field("Withdrawal Date", withdrawalDate, fillHeight: false), 1, 1);
        var reason = HumanUi.Field(UiText.Fields.Reason, statusReason);
        form.Controls.Add(reason, 0, 2);
        form.SetColumnSpan(reason, 2);
        return form;
    }
    private Control BuildEnrollmentPanel(ModernButton saveEnrollment, ModernButton addEnrollment)
    {
        int hintHeight = AppMetrics.TextHeight(AppTheme.NormalFont) * 2 + 8;
        int fieldHeight = AppMetrics.FieldRowHeight;
        int reasonHeight = CorrectionReasonRowHeight;
        var form = CorrectionForm(7);
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, hintHeight));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldHeight));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldHeight));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldHeight));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldHeight));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.TextHeight(AppTheme.NormalFont)*3+12));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, reasonHeight));
        form.Height = hintHeight + 4 * fieldHeight + AppMetrics.TextHeight(AppTheme.NormalFont)*3+12 + reasonHeight;
        enrollmentSelection.Text="Edit the active placement, then Save Placement.\nSelect a Left record to correct its leaving date.";
        form.Controls.Add(enrollmentSelection, 0, 0);
        form.SetColumnSpan(enrollmentSelection, 2);
        var section = HumanUi.Field("Campus / Class / Section", classSection, fillHeight: false);
        form.Controls.Add(section, 0, 1);
        form.SetColumnSpan(section, 2);
        form.Controls.Add(HumanUi.Field("Academic Year", academicYear, fillHeight: false), 0, 2);
        form.Controls.Add(HumanUi.Field("Status", enrollmentStatusCombo, fillHeight: false), 1, 2);
        form.Controls.Add(HumanUi.Field("Start Date", enrollmentDate, fillHeight: false), 0, 3);
        form.Controls.Add(HumanUi.Field("Leave Date", leavingDate, fillHeight: false), 1, 3);
        var reason = HumanUi.Field(UiText.Fields.Reason, enrollmentReason);
        billingStartDate.ShowCheckBox=true;
        form.Controls.Add(HumanUi.Field("Billing Start",billingStartDate,fillHeight:false),0,4);
        form.SetColumnSpan(form.GetControlFromPosition(0,4)!,2);
        var options=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false};
        options.Controls.Add(allowLegacyDates);
        options.Controls.Add(new Label {Text="Saving reconciles unpaid bills through the current month. Payment history is protected.",AutoSize=true,MaximumSize=new Size(AppScale.Scale(390),0)});
        form.Controls.Add(options,0,5);form.SetColumnSpan(options,2);
        form.Controls.Add(reason, 0, 6);
        form.SetColumnSpan(reason, 2);
        UpdateEnrollmentActions();
        return form;
    }
    private void UpdateEnrollmentActions()
    {
        bool selected=selectedEnrollmentId>0 && !creatingEnrollment;
        var record=enrollmentsRaw.AsEnumerable().FirstOrDefault(r=>enrollmentsRaw.Columns.Contains("EnrollmentId") && Convert.ToInt32(r["EnrollmentId"])==selectedEnrollmentId);
        bool editable=creatingEnrollment || (selected && Convert.ToString(record?["Status"])=="Active");
        saveEnrollmentAction.Visible=!creatingEnrollment;saveEnrollmentAction.Enabled=selected && editable && CanCorrect;
        bool active=selected && Convert.ToString(record?["Status"])=="Active";
        repairDatesAction.Visible=selected && (active || Convert.ToString(record?["Status"])=="Left");
        repairDatesAction.Text=active?"Correct Placement Dates":"Correct Leaving Date";
        repairDatesAction.Enabled=selected && CanCorrect && (!active || CurrentUser.HasPermission("Fees.Adjust"));
        SetToolTip(repairDatesAction,active
            ? "Review placement dates and affected charges together. Requires student administration and fee-adjustment permissions."
            : "Review this departure, its linked certificates and applicable withdrawal date together.");
        foreach(Control control in new Control[]{enrollmentDate,leavingDate,billingStartDate,allowLegacyDates,classSection,enrollmentStatusCombo,enrollmentReason})control.Enabled=editable && CanCorrect;
        if(selected && !editable)enrollmentSelection.Text=$"Placement {selectedEnrollmentId} — {record?["Status"]}\n" +
            (Convert.ToString(record?["Status"])=="Left"?"Use Correct Leaving Date to review its departure.":"This historical record is view only.");
        addEnrollmentAction.Visible=creatingEnrollment;addEnrollmentAction.Enabled=creatingEnrollment && CanCorrect;
        newEnrollmentAction.Visible=false;newEnrollmentAction.Enabled=false;
        academicYear.Enabled=creatingEnrollment;
        billingStartDate.Enabled=!creatingEnrollment && editable && CanCorrect;
        allowLegacyDates.Enabled=!creatingEnrollment && editable && CanCorrect;
        if(!selected && !creatingEnrollment)enrollmentSelection.Text="Edit the active placement, then Save Placement.\nSelect a Left record to correct its leaving date.";
    }
    private void BeginMissingEnrollment()
    {
        if(student.SelectedId<=0 || !CanCorrect)return;
        creatingEnrollment=true;selectedEnrollmentId=0;
        Grid.CurrentCell=null;Grid.ClearSelection();
        classSection.SelectedIndex=-1;academicYear.SelectedIndex=-1;enrollmentStatusCombo.SelectedItem="Active";
        enrollmentDate.Value=DateTime.Today;leavingDate.Checked=false;billingStartDate.Checked=false;allowLegacyDates.Checked=false;enrollmentReason.Clear();
        enrollmentSelection.Text="Review existing records before adding a missing placement.\nNo fees or bills are created; configure billing start separately.";
        UpdateEnrollmentActions();OpenCorrections(CorrectionTab.Enrollment);
    }
    private async Task RepairDatesFromEditor()
    {
        if(selectedEnrollmentId<=0) {Warn("Select the enrollment whose dates need correction");return;}
        var record=enrollmentsRaw.AsEnumerable().FirstOrDefault(r=>Convert.ToInt32(r["EnrollmentId"])==selectedEnrollmentId);
        if(Convert.ToString(record?["Status"])=="Left") {await RepairDates(selectedEnrollmentId);return;}
        await ReconcileDatesFromEditor();
    }
    private void ShowCorrectionTab(CorrectionTab tab)
    {
        statusPanel.Visible = tab == CorrectionTab.Status;
        enrollmentPanel.Visible = tab == CorrectionTab.Enrollment;
        statusFooter.Visible=tab==CorrectionTab.Status;
        enrollmentFooter.Visible=tab==CorrectionTab.Enrollment;
        TabStrip.ApplyButtonTheme(correctionStatusTab, tab == CorrectionTab.Status);
        TabStrip.ApplyButtonTheme(correctionEnrollmentTab, tab == CorrectionTab.Enrollment);
    }
    private void UpdateCorrectionHeader()
    {
        correctionSubtitle.Text =
            student.SelectedId > 0 && !string.IsNullOrWhiteSpace(student.Text)
                ? student.Text
                : "No student selected";
    }
    private void OpenCorrections(CorrectionTab tab)
    {
        /* A view-only user has no panel to open; the Fix and row clicks still navigate. */
        if (!CanCorrect)
            return;
        ShowCorrectionTab(tab);
        UpdateCorrectionHeader();
    }
    private Task BuildStudentSearch()
    {
        student.PlaceholderText = StudentPicker.SearchPlaceholder;
        StudentSearchCache.Warm();
        student.SearchFailed += ex => Fail(ex);
        student.MatchesChanged += (_, _) => {
            if (student.Text.Trim().Length == 0 || student.SelectedId > 0)
                return;
            SetStatus(
                student.MatchCount == 0
                    ? "No student matches what is typed."
                    : student.MatchesCapped
                        ? "Many students match - keep typing to narrow the list."
                        : $"{student.MatchCount} student(s) found. Click one, or use Up/Down and Enter.");
        };
        /* The operator edited away from the student the page is holding, or emptied the box. */
        student.SelectionCleared += async (_, _) => await ClearForm(false);
        student.UserSelectionChanged += async (_, _) => await LoadStudentSnapshot();
        return Task.CompletedTask;
    }
    private void BuildStatusEditor()
    {
        statusCombo.Items.Clear();
        statusCombo.Items.AddRange(new object[] { "Active", "Left" });
        withdrawalDate.ShowCheckBox = true;
        withdrawalDate.Checked = false;
    }
    private void BuildEnrollmentEditor()
    {
        enrollmentStatusCombo.Items.Clear();
        enrollmentStatusCombo.Items.AddRange(new object[] { "Active", "Promoted", "Left" });
        leavingDate.ShowCheckBox = true;
        leavingDate.Checked = false;
        Defer(() => {
            try
            {
                var sections = DatabaseHelper.QueryUser(
                    "GetClassSections",
                    DatabaseHelper.Int("@CampusId", 0),
                    DatabaseHelper.Int("@ClassId", 0),
                    DatabaseHelper.Int("@SectionId", 0));
                var dt = new DataTable();
                dt.Columns.Add("ClassSectionId", typeof(int));
                dt.Columns.Add("Display", typeof(string));
                foreach (DataRow row in sections.Rows)
                {
                    dt.Rows.Add(Convert.ToInt32(row["ClassSectionId"]), $"{row["CampusName"]} — {row["ClassName"]} {row["SectionName"]}");
                }
                classSection.Bind(dt, "ClassSectionId", "Display");
                academicYear.Bind(DatabaseHelper.QueryUser("GetAcademicYears"), "AcademicYearId", "YearName");
            }
            catch (Exception ex) { Fail(ex); }
        });
    }
    private Control BuildStudentRow()
    {
        var studentRow = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        studentRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        studentRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        studentRow.Controls.Add(HumanUi.Field("Student (optional)", student, fillHeight: false), 0, 0);
        studentRow.Controls.Add(HumanUi.Field("Student details", studentSummary), 1, 0);
        return studentRow;
    }
    private void SetStudentSummary(string text, bool placeholder)
    {
        studentSummary.ForeColor = placeholder ? AppTheme.MutedText : AppTheme.Text;
        studentSummary.Text = text;
    }
    private async Task LoadStudentSnapshot()
    {
        selectedEnrollmentId = 0;
        creatingEnrollment=false;UpdateEnrollmentActions();
        /* (#10) - a student switch has to clear the ENROLLMENT editor as well as the status one. ClearForm already resets these five; this is the switch path. */
        enrollmentStatusCombo.SelectedIndex = -1;
        classSection.SelectedIndex = -1;
        academicYear.SelectedIndex = -1;
        leavingDate.Checked = false;
        enrollmentReason.Clear();
        UpdateCorrectionHeader();
        if (student.SelectedId <= 0)
        {
            statusCombo.SelectedIndex = -1;
            withdrawalDate.Checked = false;
            statusReason.Clear();
            ClearSnapshotDisplay();
            return;
        }
        try
        {
            int admissionId = student.SelectedId;
            var raw = await DatabaseHelper.QueryUserAsync("GetStudentDetails", DatabaseHelper.Int("@AdmissionId", admissionId));
            if (IsDisposed || student.SelectedId != admissionId) return;
            if (raw.Rows.Count > 0)
            {
                var row = raw.Rows[0];
                statusCombo.SelectedItem = Convert.ToString(row["Status"]);
                if (row["WithdrawalDate"] != DBNull.Value)
                {
                    withdrawalDate.Value = Convert.ToDateTime(row["WithdrawalDate"]);
                    withdrawalDate.Checked = true;
                }
                else
                {
                    withdrawalDate.Value = DateTime.Today;
                    withdrawalDate.Checked = false;
                }
            }
            UpdateStudentContextHeader(raw);
            statusReason.Clear();
        }
        catch (Exception ex) { ClearSnapshotDisplay(); Fail(ex); }
        if (!IsDisposed)
        {
            UpdateCorrectionHeader();
            await SetGridMode("Findings");
        }
    }
    private void UpdateStudentContextHeader(DataTable raw)
    {
        if (student.SelectedId <= 0)
        {
            ClearSnapshotDisplay();
            return;
        }
        try
        {
            int admissionId = student.SelectedId;
            if (IsDisposed) return;
            if (raw.Rows.Count == 0)
            {
                ClearSnapshotDisplay();
                return;
            }
            var row = raw.Rows[0];
            var parts = new List<string>();
            /* Admission Number first - it is what identifies the student; the registration number is only a reporting label. */
            parts.Add($"Admission Number {AdmissionNumbers.ForId(admissionId)}");
            parts.Add(
                row["ClassName"] != DBNull.Value
                    ? $"{Convert.ToString(row["CurrentCampusName"])} — " +
                      $"{Convert.ToString(row["ClassName"])} {Convert.ToString(row["SectionName"])}"
                    : "No current placement");
            if (row["DateOfBirth"] != DBNull.Value)
                parts.Add("DOB " + Convert.ToDateTime(row["DateOfBirth"]).ToString("dd-MMM-yyyy"));
            string phone = row["ContactNumber"] != DBNull.Value
                ? Convert.ToString(row["ContactNumber"]) ?? string.Empty
                : string.Empty;
            if (phone.Length > 0)
                parts.Add("Phone " + phone);
            SetStudentSummary(string.Join("   •   ", parts), false);
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void ClearSnapshotDisplay() => SetStudentSummary(NoStudentText, true);
    private async Task SetGridMode(string mode)
    {
        gridMode = mode;
        UpdateEnrollmentActions();
        if(Grid.Columns.Contains("ReviewFinding"))Grid.Columns["ReviewFinding"].Visible=mode=="Findings" && CanCorrect;
        Grid.ScrollBars=ScrollBars.Both;
        Grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.None;
        Grid.DefaultCellStyle.WrapMode=DataGridViewTriState.False;
        /* see gridModeVersion. The tab highlight is set here and the table is bound later, so the version is what keeps the two from disagreeing. */
        int modeVersion = ++gridModeVersion;
        TabStrip.ApplyButtonTheme(modeFindings, mode == "Findings");
        TabStrip.ApplyButtonTheme(modeEnrollments, mode == "Enrollments");
        TabStrip.ApplyButtonTheme(modeTimeline, mode == "Timeline");
        /* The enrollment form is the one that goes with the Enrollments list. */
        if (mode == "Enrollments")
            ShowCorrectionTab(CorrectionTab.Enrollment);
        switch (mode)
        {
            case "Enrollments":
                await LoadEnrollments(modeVersion);
                break;
            case "Timeline":
                await LoadTimeline(modeVersion);
                break;
            default:
                await Scan(modeVersion);
                break;
        }
    }
    private static void ApplyStudentDisplayToFindings(DataTable findings)
    {
        if (!findings.Columns.Contains("AdmissionId") ||
            !findings.Columns.Contains("StudentName"))
        {
            return;
        }
        bool hasFather = findings.Columns.Contains("FatherName");
        bool hasGender = findings.Columns.Contains("Gender");
        bool hasRegistration = findings.Columns.Contains("RegistrationNumber");
        bool hasClass = findings.Columns.Contains("ClassName");
        bool hasSection = findings.Columns.Contains("SectionName");
        foreach (DataRow row in findings.Rows)
        {
            int admissionId = Convert.ToInt32(row["AdmissionId"]);
            string name = Convert.ToString(row["StudentName"]) ?? string.Empty;
            string father = hasFather
                ? Convert.ToString(row["FatherName"]) ?? string.Empty
                : string.Empty;
            string gender = hasGender
                ? Convert.ToString(row["Gender"]) ?? string.Empty
                : string.Empty;
            string registration = hasRegistration
                ? Convert.ToString(row["RegistrationNumber"]) ?? string.Empty
                : string.Empty;
            // Disambiguates same-named siblings/twins in this findings grid the same way every other student picker in the app already does (HumanUi.StudentDisplay).
            string className = hasClass
                ? Convert.ToString(row["ClassName"]) ?? string.Empty
                : string.Empty;
            string sectionName = hasSection
                ? Convert.ToString(row["SectionName"]) ?? string.Empty
                : string.Empty;
            row["StudentName"] = HumanUi.StudentDisplay(admissionId, name, father, gender, registration, className, sectionName);
        }
    }
    private async Task Scan(int modeVersion)
    {
        try
        {
            int? admissionId = student.SelectedId > 0
                ? student.SelectedId
                : null;
            findingsRaw = await DatabaseHelper.QueryUserAsync(
                "CheckStudentConsistency",
                DatabaseHelper.NullableInt("@AdmissionId", admissionId));
            /* (#8) - a scan for ONE student must land only while that student is still selected, or its findings are shown as the current student's. */
            if (IsDisposed ||
                modeVersion != gridModeVersion ||
                (admissionId.HasValue &&
                 student.SelectedId != admissionId.Value))
            {
                return;
            }
            ApplyStudentDisplayToFindings(findingsRaw);
            UpdateFindingInstructions(findingsRaw);
            ResetRowFilter();
            Bind(findingsRaw);
            DecorateFindingsGrid();
            UpdateFindingsSummary(findingsRaw, admissionId is null);
            SetStatus(
                Grid.Rows.Count == 0
                    ? (admissionId is null
                        ? "No inconsistencies found across all students."
                        : "No inconsistencies found for this student.")
                    : $"{Grid.Rows.Count} issue(s) found.");
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void DecorateFindingsGrid()
    {
        if (!Grid.Columns.Contains("ReviewFinding"))
            Grid.Columns.Add(new DataGridViewButtonColumn {Name="ReviewFinding",HeaderText="",Text="Review",UseColumnTextForButtonValue=true});
        var visible=new[]{"ReviewFinding","StudentName","Severity","IssueDescription","SuggestedAction"};
        foreach(DataGridViewColumn column in Grid.Columns)column.Visible=visible.Contains(column.Name) && (column.Name!="ReviewFinding" || CanCorrect);
        Grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;
        Grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;
        Grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;
        Grid.ScrollBars=ScrollBars.Both;
        for(int i=0;i<visible.Length;i++)if(Grid.Columns.Contains(visible[i]))Grid.Columns[visible[i]].DisplayIndex=i;
        var review=Grid.Columns["ReviewFinding"];
        review.AutoSizeMode=DataGridViewAutoSizeColumnMode.None;review.Width=AppMetrics.TextWidth("Review",AppTheme.ButtonFont,24);
        if (Grid.Columns.Contains("Severity"))
        {
            var severity = Grid.Columns["Severity"]!;
            severity.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            severity.Width = AppScale.Scale(84);
        }
        if (Grid.Columns.Contains("StudentName"))
        {Grid.Columns["StudentName"]!.AutoSizeMode=DataGridViewAutoSizeColumnMode.None;Grid.Columns["StudentName"]!.FillWeight = 110;}
        if (Grid.Columns.Contains("IssueDescription"))
        {Grid.Columns["IssueDescription"]!.AutoSizeMode=DataGridViewAutoSizeColumnMode.None;Grid.Columns["IssueDescription"]!.FillWeight = 220;}
        if (Grid.Columns.Contains("SuggestedAction"))
        {Grid.Columns["SuggestedAction"]!.AutoSizeMode=DataGridViewAutoSizeColumnMode.None;Grid.Columns["SuggestedAction"]!.FillWeight = 220;}
    }
    private static void UpdateFindingInstructions(DataTable findings)
    {
        if(!findings.Columns.Contains("RuleCode") || !findings.Columns.Contains("SuggestedAction"))return;
        foreach(DataRow row in findings.Rows) {
            string rule=Convert.ToString(row["RuleCode"])??"";
            row["SuggestedAction"]=rule switch {
                "CERTIFICATE_ENROLLMENT_DATE_MISMATCH" or "WITHDRAWAL_ENROLLMENT_DATE_MISMATCH" => "Admission → Actions → Placement History / Corrections → Correct Billing / Leaving Dates",
                "LEAVING_BEFORE_ENROLLMENT" or "ENROLLMENT_DATE_OVERLAP" => "Open Admission to review placement dates and the appropriate correction",
                "ENROLLMENT_BEFORE_ADMISSION" => "Open Admission and compare First Admission with placement start date",
                "STU_ACTIVE_NO_ENROLLMENT" => "Admission → Actions → Placement History → Add verified missing placement",
                "LEAVECERT_STATUS_MISMATCH" => "Review History and Student Status. An older certificate may belong to a previous placement; confirm before changing the current status",
                _ => "Open Admission to review this discrepancy and choose the appropriate correction"
            };
        }
    }
    private void UpdateFindingsSummary(DataTable findings, bool scanningEveryone)
    {
        if (!scanningEveryone || findings.Rows.Count == 0)
        {
            findingsSummary.Text = string.Empty;
            return;
        }
        var rows = findings.AsEnumerable().ToList();
        int high = rows.Count(r => string.Equals(Convert.ToString(r["Severity"]), "High", StringComparison.OrdinalIgnoreCase));
        int medium = rows.Count(r => string.Equals(Convert.ToString(r["Severity"]), "Medium", StringComparison.OrdinalIgnoreCase));
        int low = rows.Count(r => string.Equals(Convert.ToString(r["Severity"]), "Low", StringComparison.OrdinalIgnoreCase));
        int studentsAffected = rows
            .Select(r => Convert.ToInt32(r["AdmissionId"]))
            .Distinct()
            .Count();
        /* Only the severities that occur, so a school with 520 Low findings reads "520 issues · 520 students · 520 Low" rather than listing two zeros. */
        var parts = new List<string> {
            $"{rows.Count} issue(s)",
            $"{studentsAffected} student(s)"
        };
        if (high > 0)
            parts.Add($"{high} High");
        if (medium > 0)
            parts.Add($"{medium} Medium");
        if (low > 0)
            parts.Add($"{low} Low");
        findingsSummary.Text = string.Join(" · ", parts);
    }
    private void OnGridCellFormatting(DataGridViewCellFormattingEventArgs e)
    {
        if (gridMode != "Findings")
            return;
        if (e.RowIndex < 0 || e.RowIndex >= Grid.Rows.Count)
            return;
        if (Grid.Rows[e.RowIndex].DataBoundItem is not DataRowView rowView ||
            !rowView.Row.Table.Columns.Contains("Severity"))
        {
            return;
        }
        e.CellStyle!.BackColor = Convert.ToString(rowView.Row["Severity"]) switch // DataGridView always supplies a non-null CellStyle on CellFormatting.
        {
            "High" => AppTheme.Tint(AppTheme.Danger, 0.90f),
            "Medium" => AppTheme.Tint(AppTheme.Warning, 0.90f),
            _ => Grid.DefaultCellStyle.BackColor
        };
    }
    private void OnGridCellContentClick(DataGridViewCellEventArgs e)
    {
        if (gridMode != "Findings")
            return;
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
            return;
        if (Grid.Columns[e.ColumnIndex].Name != "ReviewFinding")
            return;
        if (Grid.Rows[e.RowIndex].DataBoundItem is not DataRowView rowView)
            return;
        /* QuickFix is now async Task. */
        _ = QuickFix(rowView.Row);
    }
    /* Clicking an enrollment row loads it into the form (OnGridSelectionChanged) and brings the form up beside the list. */
    private void OnGridCellClick(DataGridViewCellEventArgs e)
    {
        if (gridMode != "Enrollments" || e.RowIndex < 0 || student.SelectedId <= 0)
            return;
        if (Grid.Rows[e.RowIndex].DataBoundItem is DataRowView rowView &&
            rowView.Row.Table.Columns.Contains("EnrollmentId") &&
            rowView.Row["EnrollmentId"] != DBNull.Value &&
            Convert.ToInt32(rowView.Row["EnrollmentId"]) != selectedEnrollmentId)
        {
            OnGridSelectionChanged();
        }
        OpenCorrections(CorrectionTab.Enrollment);
    }
    private async Task LoadEnrollments(int modeVersion)
    {
        enrollmentsRaw = new DataTable();
        findingsSummary.Text = string.Empty;
        if (student.SelectedId <= 0)
        {
            Grid.DataSource = null;
            Warn("Select a student to see their academic placement history.");
            return;
        }
        try
        {
            int admissionId = student.SelectedId;
            enrollmentsRaw = await DatabaseHelper.QueryUserAsync(
                "GetStudentEnrollmentsForLifecycle",
                DatabaseHelper.Int("@AdmissionId", admissionId));
            if (IsDisposed ||
                modeVersion != gridModeVersion ||
                student.SelectedId != admissionId)
            {
                return;
            }
            ResetRowFilter();
            var display=enrollmentsRaw.Copy();
            display.Columns.Add("ClassSectionDisplay",typeof(string));
            foreach(DataRow row in display.Rows)row["ClassSectionDisplay"]=$"{row["ClassName"]} - {row["SectionName"]}";
            Bind(display);
            var columns=new[]{("YearName","Year",80f),("CampusName","Campus",120f),("ClassSectionDisplay","Class - Section",130f),
                ("Status","Status",75f),("EnrollmentDate","Start",105f),("LeavingDate","Leave",105f)};
            foreach(DataGridViewColumn column in Grid.Columns)column.Visible=columns.Any(c=>c.Item1==column.Name);
            for(int i=0;i<columns.Length;i++) {
                var column=Grid.Columns[columns[i].Item1];column.HeaderText=columns[i].Item2;column.DisplayIndex=i;
                column.AutoSizeMode=DataGridViewAutoSizeColumnMode.None;column.FillWeight=columns[i].Item3;
            }
            Grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;
            Grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;
            Grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;
            int latest=enrollmentsRaw.AsEnumerable().OrderByDescending(r=>Convert.ToDateTime(r["EnrollmentDate"]))
                .ThenByDescending(r=>Convert.ToInt32(r["EnrollmentId"])).Select(r=>Convert.ToInt32(r["EnrollmentId"])).FirstOrDefault();
            if(latest>0)SelectEnrollmentRow(latest);
        }
        catch (Exception ex) { Fail(ex); }
    }
    private async Task LoadTimeline(int modeVersion)
    {
        findingsSummary.Text = string.Empty;
        if (student.SelectedId <= 0)
        {
            Grid.DataSource = null;
            Warn("Select a student to see their lifecycle timeline.");
            return;
        }
        try
        {
            int admissionId = student.SelectedId;
            var raw = await DatabaseHelper.QueryUserAsync("GetStudentLifecycleTimeline", DatabaseHelper.Int("@AdmissionId", admissionId));
            if (IsDisposed ||
                modeVersion != gridModeVersion ||
                student.SelectedId != admissionId)
            {
                return;
            }
            ResetRowFilter();
            Bind(raw);
            var historyColumns=new[]{("EventDate","Event Date",100f),("EventType","Event",160f),("Description","Details",360f),("CreatedAt","Recorded",140f)};
            foreach(DataGridViewColumn column in Grid.Columns)column.Visible=historyColumns.Any(c=>c.Item1==column.Name);
            Grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;
            Grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;
            Grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;
            Grid.ScrollBars=ScrollBars.Both;
            for(int i=0;i<historyColumns.Length;i++)if(Grid.Columns.Contains(historyColumns[i].Item1)) {
                var column=Grid.Columns[historyColumns[i].Item1];column.HeaderText=historyColumns[i].Item2;column.DisplayIndex=i;
                column.AutoSizeMode=DataGridViewAutoSizeColumnMode.None;column.FillWeight=historyColumns[i].Item3;
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    /* The filter box belongs to the table on screen. */
    private void ResetRowFilter()
    {
        if (rowFilter.Text.Length == 0)
            return;
        rowFilterSyncing = true;
        try
        {
            rowFilter.Text = string.Empty;
        }
        finally
        {
            rowFilterSyncing = false;
        }
    }
    private void ApplyRowFilter()
    {
        if (rowFilterSyncing || Grid.DataSource is not DataTable table)
            return;
        string text = rowFilter.Text.Trim();
        string filter = string.Empty;
        if (text.Length > 0)
        {
            string pattern = EscapeLikeValue(text);
            /* Only text columns that are actually visible: matching on a hidden column (rule code, father's name...) would keep rows that show nothing relevant. */
            var clauses = table.Columns
                .Cast<DataColumn>()
                .Where(c =>
                    c.DataType == typeof(string) &&
                    (!Grid.Columns.Contains(c.ColumnName) || Grid.Columns[c.ColumnName]!.Visible))
                .Select(c => $"[{c.ColumnName}] LIKE '%{pattern}%'")
                .ToList();
            filter = string.Join(" OR ", clauses);
        }
        /* Changing the filter re-selects a row; that must not reload the enrollment form from whichever row lands under the cursor. */
        rowFilterSyncing = true;
        try
        {
            table.DefaultView.RowFilter = filter;
        }
        catch (Exception ex) when (ex is EvaluateException or SyntaxErrorException or InvalidExpressionException)
        {
            table.DefaultView.RowFilter = string.Empty;
        }
        finally
        {
            rowFilterSyncing = false;
        }
        SetStatus(
            text.Length == 0
                ? $"{table.Rows.Count} row(s)."
                : $"{Grid.Rows.Count} of {table.Rows.Count} row(s) match \"{text}\".");
    }
    private static string EscapeLikeValue(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        foreach (char c in value)
        {
            switch (c)
            {
                case '\'':
                    sb.Append("''");
                    break;
                case '[':
                    sb.Append("[[]");
                    break;
                case ']':
                    sb.Append("[]]");
                    break;
                case '%':
                    sb.Append("[%]");
                    break;
                case '*':
                    sb.Append("[*]");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
    private void OnGridSelectionChanged()
    {
        if (gridMode != "Enrollments" || rowFilterSyncing)
            return;
        if (Grid.CurrentRow is null ||
            !enrollmentsRaw.Columns.Contains("EnrollmentId") ||
            !Grid.Columns.Contains("EnrollmentId") ||
            Grid.CurrentRow.Cells["EnrollmentId"].Value == DBNull.Value)
        {
            selectedEnrollmentId = 0;
            UpdateEnrollmentActions();
            return;
        }
        int enrollmentId = Convert.ToInt32(Grid.CurrentRow.Cells["EnrollmentId"].Value);
        DataRow? match = enrollmentsRaw.AsEnumerable()
            .FirstOrDefault(row =>
                Convert.ToInt32(row["EnrollmentId"]) == enrollmentId);
        if (match is null)
        {
            selectedEnrollmentId = 0;
            UpdateEnrollmentActions();
            return;
        }
        selectedEnrollmentId = enrollmentId;
        creatingEnrollment=false;
        enrollmentSelection.Text=$"Placement {enrollmentId} — {match["Status"]}\nEdit fields and reason, then Save Placement";
        allowLegacyDates.Checked=false;
        billingStartDate.Checked=match.Table.Columns.Contains("BillingStartDate") && match["BillingStartDate"]!=DBNull.Value;
        if(billingStartDate.Checked)billingStartDate.Value=Convert.ToDateTime(match["BillingStartDate"]);
        if(match.Table.Columns.Contains("AcademicYearId"))academicYear.SelectedValue=Convert.ToInt32(match["AcademicYearId"]);
        UpdateEnrollmentActions();
        enrollmentStatusCombo.SelectedItem = Convert.ToString(match["Status"]);
        enrollmentDate.Value = match["EnrollmentDate"] != DBNull.Value
            ? Convert.ToDateTime(match["EnrollmentDate"])
            : DateTime.Today;
        if (match["LeavingDate"] != DBNull.Value)
        {
            leavingDate.Value = Convert.ToDateTime(match["LeavingDate"]);
            leavingDate.Checked = true;
        }
        else
        {
            leavingDate.Value = DateTime.Today;
            leavingDate.Checked = false;
        }
        if (match.Table.Columns.Contains("ClassSectionId"))
            classSection.SelectedValue = Convert.ToInt32(match["ClassSectionId"]);
        enrollmentReason.Clear();
    }
    private Task QuickFix(DataRow finding)
    {
        OpenAdmissionForCorrection(Convert.ToInt32(finding["AdmissionId"]));
        return Task.CompletedTask;
    }
    private void OpenAdmissionForCorrection(int admissionId = 0)
    {
        if (admissionId <= 0) admissionId = student.SelectedId;
        if (admissionId <= 0) { Warn(UiText.Prompts.SelectStudent); return; }
        if (!CurrentUser.HasPermission("Students.Admit") && !CurrentUser.HasPermission("Students.Administer"))
        { Warn("Opening Admission requires admission or student administration permission."); return; }
        int selected = admissionId;
        if (navigate is not null)
            navigate(() => new FMSC_SMS.Pages.Students.Admissions.StudentAdmissionPage(navigate, selected), "Students", PageTitles.Admissions);
        else
        {
            using var host = new ApplicationWindowForm { Text = PageTitles.Admissions, WindowState = FormWindowState.Maximized, StartPosition = FormStartPosition.CenterParent };
            host.Controls.Add(new FMSC_SMS.Pages.Students.Admissions.StudentAdmissionPage(preselectAdmissionId: selected) { Dock = DockStyle.Fill });
            host.ShowDialog(FindForm());
        }
    }
    private async Task SelectStudentByAdmissionId(int admissionId)
    {
        DataTable all = await StudentSearchCache.GetAllAsync();
        DataRow? found = FindCachedStudent(all, admissionId);
        if (found is null)
        {
            /* Admitted since the list was read. */
            StudentSearchCache.Invalidate();
            all = await StudentSearchCache.GetAllAsync();
            found = FindCachedStudent(all, admissionId);
        }
        if (IsDisposed)
            return;
        if (found is not null)
            student.SetStudent(found);
        await LoadStudentSnapshot();
    }
    private static DataRow? FindCachedStudent(DataTable all, int admissionId)
    {
        if (!all.Columns.Contains("AdmissionId"))
            return null;
        foreach (DataRow row in all.Rows)
        {
            if (row["AdmissionId"] != DBNull.Value &&
                Convert.ToInt32(row["AdmissionId"]) == admissionId)
            {
                return row;
            }
        }
        return null;
    }
    private void SelectEnrollmentRow(int enrollmentId)
    {
        foreach (DataGridViewRow row in Grid.Rows)
        {
            if (row.DataBoundItem is not DataRowView rowView ||
                !rowView.Row.Table.Columns.Contains("EnrollmentId") ||
                Convert.ToInt32(rowView.Row["EnrollmentId"]) != enrollmentId)
            {
                continue;
            }
            Grid.ClearSelection();
            row.Selected = true;
            var visibleCell=row.Cells.Cast<DataGridViewCell>().FirstOrDefault(cell=>cell.Visible);
            if(visibleCell!=null)Grid.CurrentCell=visibleCell;
            return;
        }
    }
    private async Task RepairDates(int targetEnrollmentId=0,DateTime? proposedStart=null,DateTime? proposedLeave=null,bool clearLeave=false,string initialReason="",bool focused=false)
    {
        if(student.SelectedId<=0) {Warn(UiText.Prompts.SelectStudent);return;}
        int admissionId=student.SelectedId;
        try {
            var data=await DatabaseHelper.QueryUserSetAsync("PreviewLifecycleDateRepair",DatabaseHelper.Int("@AdmissionId",admissionId));
            if(student.SelectedId!=admissionId || IsDisposed)return;
            if(data.Tables[0].Rows.Count==0) {Warn("The student no longer exists.");return;}
            if(targetEnrollmentId==0 && proposedLeave.HasValue && data.Tables[1].Rows.Count>0)
                targetEnrollmentId=data.Tables[1].AsEnumerable().Where(r=>Convert.ToString(r["Status"])=="Left")
                    .OrderByDescending(r=>Convert.ToDateTime(r["EnrollmentDate"])).ThenByDescending(r=>Convert.ToInt32(r["EnrollmentId"]))
                    .Select(r=>Convert.ToInt32(r["EnrollmentId"])).FirstOrDefault();
            var target=data.Tables[1].AsEnumerable().FirstOrDefault(r=>Convert.ToInt32(r["EnrollmentId"])==targetEnrollmentId);
            if(target!=null && Convert.ToString(target["Status"])=="Left") {
                using var correction=new LeavingDateCorrectionDialog(admissionId,targetEnrollmentId,data,proposedLeave,initialReason);
                if(correction.ShowDialog(FindForm())!=DialogResult.OK)return;
                StudentSearchCache.Invalidate();await LoadStudentSnapshot();await SetGridMode("Enrollments");
                SelectEnrollmentRow(targetEnrollmentId);await ReportCorrectionAsync(admissionId,correction.ResultMessage);return;
            }
            if(target!=null && Convert.ToString(target["Status"])!="Active") {Warn("Select a Left placement to correct a departure, or an active placement to review its dates.");return;}
            using var dialog=new LifecycleDateRepairDialog(admissionId,data,targetEnrollmentId,proposedStart,proposedLeave,clearLeave,initialReason,focused);
            if(dialog.ShowDialog(FindForm())!=DialogResult.OK)return;
            StudentSearchCache.Invalidate();await LoadStudentSnapshot();await SetGridMode("Enrollments");
            if(targetEnrollmentId>0)SelectEnrollmentRow(targetEnrollmentId);
            await ReportCorrectionAsync(admissionId,"Selected date corrections saved.");
        } catch(Exception ex) {Fail(ex);}
    }
    private async Task ReportCorrectionAsync(int admissionId,string resultMessage)
    {
        if(IsDisposed || student.SelectedId!=admissionId)return;
        Info(resultMessage + " Checking for remaining issues…");
        try {
            var checkedFindings=await DatabaseHelper.QueryUserAsync("CheckStudentConsistency",DatabaseHelper.Int("@AdmissionId",admissionId));
            if(IsDisposed || student.SelectedId!=admissionId)return;
            findingsRaw=checkedFindings;
            if(gridMode=="Findings") {
                ApplyStudentDisplayToFindings(findingsRaw);UpdateFindingInstructions(findingsRaw);
                ResetRowFilter();Bind(findingsRaw);DecorateFindingsGrid();UpdateFindingsSummary(findingsRaw,false);
            }
            int remaining=checkedFindings.Rows.Count;
            Info(resultMessage + (remaining==0
                ? " No remaining issues found by the console checks."
                : $" {remaining} issue(s) remain. Open Issues to review them."));
        } catch(Exception ex) {
            if(!IsDisposed && student.SelectedId==admissionId)
                Warn(resultMessage + " Could not recheck remaining issues: " + DbMessage.UserMessage(ex));
        }
    }
    private async Task SaveStatus()
    {
        // BUSINESS RULE: this console is an intentional backdoor for correcting legacy records.
        // It may clear an unknown withdrawal date even when the corrected status is Left; normal
        // student workflows enforce their own chronology and must not copy this exception.
        try
        {
            if (student.SelectedId <= 0)
            {
                Warn(UiText.Prompts.SelectStudent);
                return;
            }
            string? status = statusCombo.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(status))
            {
                Warn("Select the corrected status.");
                return;
            }
            if (string.IsNullOrWhiteSpace(statusReason.Text))
            {
                Warn("Enter a reason for this correction.");
                return;
            }
            int correctedAdmissionId=student.SelectedId;
            var current=DatabaseHelper.QuerySql("""
                EXEC dbo.RequirePermission @UserId,N'Students.Administer';
                SELECT Status,WithdrawalDate FROM dbo.Students WHERE AdmissionId=@AdmissionId;
                """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@AdmissionId",correctedAdmissionId));
            if(current.Rows.Count==0) {Warn("The student no longer exists.");return;}
            string oldStatus=Convert.ToString(current.Rows[0]["Status"])??"";
            if(status=="Left" && withdrawalDate.Checked && oldStatus=="Left" &&
                (current.Rows[0]["WithdrawalDate"]==DBNull.Value || Convert.ToDateTime(current.Rows[0]["WithdrawalDate"]).Date!=withdrawalDate.Value.Date)) {
                await RepairDates(proposedLeave:withdrawalDate.Value.Date,initialReason:statusReason.Text.Trim());return;
            }
            var enrollmentReview=DatabaseHelper.QueryUser("GetStudentEnrollmentsForLifecycle",DatabaseHelper.Int("@AdmissionId",correctedAdmissionId));
            var active=enrollmentReview.AsEnumerable().Where(row=>Convert.ToString(row["Status"])=="Active")
                .OrderByDescending(row=>Convert.ToInt32(row["EnrollmentId"])).ToArray();
            if(status=="Active" && oldStatus=="Left" && active.Length==0) {
                Warn("This student has no active placement. Use Readmission to bring them back into a class; status repair cannot create that placement.");return;
            }
            object proposedWithdrawal=withdrawalDate.Checked?withdrawalDate.Value.Date:DBNull.Value;
            bool noChange=oldStatus==status && Equals(current.Rows[0]["WithdrawalDate"],proposedWithdrawal);
            string relatedEffect=noChange?"These values already match; no record changes are expected.":status=="Left" && active.Length>0
                ? $"The latest active placement ({active[0]["EnrollmentId"]}) will close as Left. Its leaving date will be " +
                    (withdrawalDate.Checked?withdrawalDate.Value.ToString("dd MMM yyyy"):"today because no withdrawal date is recorded") + "."
                : "Existing placement records will not be changed by this status correction.";
            if(!Confirm($"Student: {student.Text}\nStatus: {oldStatus} → {status}\n" +
                (withdrawalDate.Checked?$"Withdrawal date: {withdrawalDate.Value:dd MMM yyyy}\n":"Withdrawal date will be cleared.\n") +
                relatedEffect + "\n\nBills, payments and certificates stay unchanged. The database will check the correction before saving.\n" +
                $"Reason: {statusReason.Text.Trim()}\n\nApply this status correction?","Confirm Status Correction"))return;
            var statusResult=DatabaseHelper.Query(
                "CorrectStudentStatus",
                DatabaseHelper.Int("@UserId", CurrentUser.UserId),
                DatabaseHelper.Int("@AdmissionId", student.SelectedId),
                DatabaseHelper.P("@NewStatus", status),
                DatabaseHelper.P(
                    "@NewWithdrawalDate",
                    withdrawalDate.Checked ? withdrawalDate.Value.Date : (object)DBNull.Value),
                DatabaseHelper.Bit("@ClearWithdrawalDate", !withdrawalDate.Checked),
                DatabaseHelper.P("@Reason", statusReason.Text.Trim()));
            var saved=statusResult.Rows[0];
            string statusOutcome=Convert.ToBoolean(saved["Changed"])
                ? $"Student status corrected to {saved["Status"]}. " +
                    (saved["WithdrawalDate"] is DateTime savedDate?$"Withdrawal date: {savedDate:dd MMM yyyy}.":"Withdrawal date cleared.")
                : "Student status and withdrawal date already match. No changes were needed.";
            statusReason.Clear();
            await LoadStudentSnapshot();
            StudentSearchCache.Invalidate();
            await ReportCorrectionAsync(correctedAdmissionId,statusOutcome);
        }
        catch (Exception ex) { Fail(ex); }
    }
    private async Task ReconcileDatesFromEditor()
    {
        if(!CanCorrect || !CurrentUser.HasPermission("Fees.Adjust")) {
            Warn("Correcting placement dates and billing requires student administration and fee-adjustment permissions.");return;
        }
        int enrollmentId=selectedEnrollmentId,admissionId=student.SelectedId;
        var current=enrollmentsRaw.AsEnumerable().FirstOrDefault(row=>Convert.ToInt32(row["EnrollmentId"])==enrollmentId);
        if(current is null || Convert.ToString(current["Status"])!="Active") {Warn("Select an active placement to correct its start and billing dates.");return;}
        using var correction=new EnrollmentDateCorrectionDialog(student.Text,enrollmentId,current,enrollmentDate.Value.Date,
            billingStartDate.Checked?billingStartDate.Value.Date:null,enrollmentReason.Text.Trim(),allowLegacyDates.Checked);
        if(correction.ShowDialog(FindForm())!=DialogResult.OK)return;
        StudentSearchCache.Invalidate();
        if(IsDisposed || student.SelectedId!=admissionId)return;
        await LoadStudentSnapshot();await LoadEnrollments(gridModeVersion);
        SelectEnrollmentRow(enrollmentId);await ReportCorrectionAsync(admissionId,correction.ResultMessage);
    }
    private async Task SaveEnrollment()
    {
        int correctedAdmissionId=student.SelectedId;
        try
        {
            if (selectedEnrollmentId <= 0)
            {
                Warn(
                    "Select an academic placement row first, or use Add Missing Record " +
                    "to create a new one.");
                return;
            }
            if (string.IsNullOrWhiteSpace(enrollmentReason.Text))
            {
                Warn("Enter a reason for this correction.");
                return;
            }
            var original=enrollmentsRaw.AsEnumerable().FirstOrDefault(row=>Convert.ToInt32(row["EnrollmentId"])==selectedEnrollmentId);
            if(original==null || Convert.ToString(original["Status"])!="Active") {Warn("Previous placements are view only.");return;}
            bool statusOrPlacementChanged=Convert.ToString(original["Status"])!=Convert.ToString(enrollmentStatusCombo.SelectedItem) ||
                (classSection.SelectedId>0 && Convert.ToInt32(original["ClassSectionId"])!=classSection.SelectedId);
            if(!statusOrPlacementChanged) {
                if(leavingDate.Checked) {Warn("An active placement cannot have a leaving date. Choose its departure status first.");return;}
                await ReconcileDatesFromEditor();return;
            }
            if(Convert.ToDateTime(original["EnrollmentDate"]).Date!=enrollmentDate.Value.Date ||
                !Equals(original["BillingStartDate"],billingStartDate.Checked?(object)billingStartDate.Value.Date:DBNull.Value)) {
                Warn("Use Correct Placement Dates to save the date changes first, then save the status or placement correction.");return;
            }
            int correctedEnrollmentId=selectedEnrollmentId;
            string newStatus=(enrollmentStatusCombo.SelectedItem as string)??Convert.ToString(original["Status"])!;
            int newPlacementId=classSection.SelectedId>0?classSection.SelectedId:Convert.ToInt32(original["ClassSectionId"]);
            DateTime newStart=enrollmentDate.Value.Date;
            DateTime? newLeave=leavingDate.Checked?leavingDate.Value.Date:null;
            string newPlacement=classSection.Text,reason=enrollmentReason.Text.Trim();
            var currentRecords=await DatabaseHelper.QueryUserAsync("GetStudentEnrollmentsForLifecycle",DatabaseHelper.Int("@AdmissionId",correctedAdmissionId));
            if(IsDisposed || student.SelectedId!=correctedAdmissionId || selectedEnrollmentId!=correctedEnrollmentId)return;
            var current=currentRecords.AsEnumerable().FirstOrDefault(row=>Convert.ToInt32(row["EnrollmentId"])==correctedEnrollmentId);
            string[] reviewedFields={"Status","ClassSectionId","EnrollmentDate","LeavingDate","BillingStartDate"};
            if(current==null || reviewedFields.Any(field=>!Equals(current[field],original[field]))) {
                Warn("This placement changed since it was loaded. Refresh and review it before saving.");return;
            }
            if(newStatus=="Left" && !newLeave.HasValue) {Warn("Enter a leaving date before marking this placement Left.");return;}
            if(newLeave.HasValue && newLeave.Value<newStart) {Warn("Leaving date cannot be before placement start date.");return;}
            if(newStatus=="Promoted" && !currentRecords.AsEnumerable().Any(row=>Convert.ToInt32(row["EnrollmentId"])!=correctedEnrollmentId && Convert.ToString(row["Status"])=="Active")) {
                Warn("Use Promotion to create the destination placement before marking this record Promoted.");return;
            }
            string oldPlacement=$"{current["CampusName"]} / {current["ClassName"]} / {current["SectionName"]}";
            string oldLeave=current["LeavingDate"] is DateTime previousLeave?previousLeave.ToString("dd MMM yyyy"):"not recorded";
            string proposedLeave=newLeave.HasValue?newLeave.Value.ToString("dd MMM yyyy"):"not recorded";
            string studentEffect=newStatus=="Left"
                ? $"The student's overall status becomes Left and the withdrawal date becomes {newLeave:dd MMM yyyy}."
                : newStatus=="Active"
                    ? "If the student's overall status is not Active, it becomes Active and the withdrawal date is cleared."
                    : "The student's overall status and withdrawal date stay unchanged.";
            if(!Confirm($"Student: {student.Text}\nEnrollment: {correctedEnrollmentId} ({current["YearName"]})\n" +
                $"Status: {current["Status"]} → {newStatus}\nPlacement: {oldPlacement} → {newPlacement}\n" +
                $"Leaving date: {oldLeave} → {proposedLeave}\n" +
                $"\n{studentEffect}\n" +
                "Fee assignments, bills, payments and certificates stay unchanged. A placement correction does not apply new fee rates.\n" +
                $"Reason: {reason}\n\nSave this placement correction?","Confirm Placement Correction"))return;
            var saved=DatabaseHelper.Query(
                "CorrectEnrollmentStatus",
                DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@EnrollmentId",correctedEnrollmentId),
                DatabaseHelper.P("@NewStatus",newStatus),DatabaseHelper.P("@NewEnrollmentDate",newStart),
                DatabaseHelper.P("@NewLeavingDate",newLeave.HasValue?newLeave.Value:(object)DBNull.Value),
                DatabaseHelper.Bit("@ClearLeavingDate",!newLeave.HasValue),DatabaseHelper.Int("@NewClassSectionId",newPlacementId),
                DatabaseHelper.P("@Reason",reason));
            enrollmentReason.Clear();
            /* this refresh belongs to the mode that is on screen now, so it uses the current version: it binds unless the operator switches mode while it runs. */
            await LoadEnrollments(gridModeVersion);
            // CorrectEnrollmentStatus can now synchronize Students.Status/WithdrawalDate as a side effect of this save (e.g. closing the Active enrollment to Left, or reactivating one to Active).
            await LoadStudentSnapshot();
            StudentSearchCache.Invalidate();
            SelectEnrollmentRow(correctedEnrollmentId);
            await ReportCorrectionAsync(correctedAdmissionId,Convert.ToBoolean(saved.Rows[0]["Changed"])
                ? $"Placement {correctedEnrollmentId} corrected: status {saved.Rows[0]["Status"]}, placement {newPlacement}. Fee assignments and bills were not changed."
                : "No placement changes were needed.");
        }
        catch (Exception ex) { Fail(ex); }
    }
    private async Task AddEnrollment()
    {
        int correctedAdmissionId=student.SelectedId;
        try
        {
            if(!creatingEnrollment || selectedEnrollmentId>0) {Warn("Use Add Missing Record first to start a separate historical placement");return;}
            if (student.SelectedId <= 0)
            {
                Warn(UiText.Prompts.SelectStudent);
                return;
            }
            if (classSection.SelectedId <= 0)
            {
                Warn("Select a class section for the new academic placement.");
                return;
            }
            if (academicYear.SelectedId <= 0)
            {
                Warn("Select an academic year for the new academic placement.");
                return;
            }
            if (string.IsNullOrWhiteSpace(enrollmentReason.Text))
            {
                Warn("Enter a reason for this correction.");
                return;
            }
            string newStatus=(enrollmentStatusCombo.SelectedItem as string)??"Active";
            DateTime newStart=enrollmentDate.Value.Date;
            DateTime? newLeave=leavingDate.Checked?leavingDate.Value.Date:null;
            int newPlacementId=classSection.SelectedId,newYearId=academicYear.SelectedId;
            string newPlacementName=classSection.Text,newYearName=academicYear.Text,correctionReason=enrollmentReason.Text.Trim();
            var existingRecords=await DatabaseHelper.QueryUserAsync("GetStudentEnrollmentsForLifecycle",DatabaseHelper.Int("@AdmissionId",correctedAdmissionId));
            if(IsDisposed || student.SelectedId!=correctedAdmissionId)return;
            bool hasActive=existingRecords.AsEnumerable().Any(row=>Convert.ToString(row["Status"])=="Active");
            if(newStatus=="Active" && hasActive) {Warn("An active placement already exists. Select and correct it instead of creating another.");return;}
            if(newStatus=="Promoted" && !hasActive) {Warn("A Promoted record requires an existing active destination placement. Review the student's placements first.");return;}
            if(newStatus=="Left" && !newLeave.HasValue) {Warn("Enter the leaving date for the missing Left placement.");return;}
            if(newLeave.HasValue && newLeave.Value<newStart) {Warn("Leaving date cannot be before placement start date.");return;}
            var overlapping=existingRecords.AsEnumerable().Where(row=>
                Convert.ToDateTime(row["EnrollmentDate"])<=(newLeave??DateTime.MaxValue) &&
                newStart<=(row["LeavingDate"] is DateTime end?end:DateTime.MaxValue)).ToArray();
            string recordReview=overlapping.Length==0?"No overlapping placement dates were found.":
                $"Review carefully: {overlapping.Length} existing record(s) overlap these dates: " +
                string.Join(", ",overlapping.Take(6).Select(row=>$"{row["EnrollmentId"]} ({row["YearName"]}, {row["Status"]})")) +
                (overlapping.Length>6?" and more.":".") + " Do not create a duplicate record.";
            string statusEffect=newStatus=="Active"
                ? "If the student's overall status is not Active, creating this record sets it to Active and clears the withdrawal date."
                : "The student's overall status and withdrawal date stay unchanged.";
            if(!Confirm($"Student: {student.Text}\nYear: {newYearName}\nPlacement: {newPlacementName}\nStatus: {newStatus}\nStart: {newStart:dd MMM yyyy}\n" +
                (newLeave.HasValue?$"Leave: {newLeave.Value:dd MMM yyyy}\n":"Leave: not recorded\n") +
                $"\nExisting placements: {existingRecords.Rows.Count}. {recordReview}\n\n{statusEffect}\n" +
                "This creates the placement and its correction history only. No fee assignments or bills are created. Existing bills, payments and certificates stay unchanged.\n" +
                $"Reason: {correctionReason}\n\nCreate this missing placement?","Confirm Missing Placement"))return;
            var created=DatabaseHelper.Query(
                "CorrectEnrollmentStatus",
                DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@AdmissionId",correctedAdmissionId),
                DatabaseHelper.P("@NewStatus",newStatus),DatabaseHelper.P("@NewEnrollmentDate",newStart),
                DatabaseHelper.P("@NewLeavingDate",newLeave.HasValue?newLeave.Value:(object)DBNull.Value),
                DatabaseHelper.Bit("@ClearLeavingDate",!newLeave.HasValue),DatabaseHelper.Int("@NewClassSectionId",newPlacementId),
                DatabaseHelper.Int("@AcademicYearId",newYearId),DatabaseHelper.P("@Reason",correctionReason));
            int createdId=Convert.ToInt32(created.Rows[0]["EnrollmentId"]);
            creatingEnrollment=false;
            enrollmentReason.Clear();
            await SetGridMode("Enrollments");
            // Refresh the snapshot panel so it reflects that instead of the pre-correction status.
            await LoadStudentSnapshot();
            StudentSearchCache.Invalidate();
            SelectEnrollmentRow(createdId);
            await ReportCorrectionAsync(correctedAdmissionId,$"Missing placement {createdId} created for {newYearName}, status {created.Rows[0]["Status"]}. No fee assignments or bills were created.");
        }
        catch (Exception ex) { Fail(ex); }
    }
    private async Task ClearForm(bool clearStudentBox = true)
    {
        if (clearStudentBox)
            student.Clear();
        selectedEnrollmentId = 0;
        creatingEnrollment=false;UpdateEnrollmentActions();
        enrollmentsRaw = new DataTable();
        findingsRaw = new DataTable();
        findingsSummary.Text = string.Empty;
        ResetRowFilter();
        ClearSnapshotDisplay();
        statusCombo.SelectedIndex = -1;
        withdrawalDate.Checked = false;
        statusReason.Clear();
        enrollmentStatusCombo.SelectedIndex = -1;
        classSection.SelectedIndex = -1;
        academicYear.SelectedIndex = -1;
        leavingDate.Checked = false;
        enrollmentReason.Clear();
        Grid.DataSource = null;
        ShowCorrectionTab(CorrectionTab.Status);
        UpdateCorrectionHeader();
        await SetGridMode("Findings");
        SetStatus("Cleared.");
    }
}




