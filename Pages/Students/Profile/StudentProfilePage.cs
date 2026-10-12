using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Admissions;
using FMSC_SMS.Pages.Students.Enrollment;
using FMSC_SMS.Pages.Students.Maintenance;
namespace FMSC_SMS.Pages.Students.Profile;
/// <summary>A read-only aggregator: pick a student once and see identity, current placement, outstanding balance, family and lifecycle history in one place, with jump-links into the existing pages that actually edit each area.</summary>
public sealed class StudentProfilePage : V2Page
{
    private readonly Action<Func<Control>, string, string>? navigate;
    private readonly StudentPicker student = new();
    private readonly ModernButton feeHistoryButton = new() { Text="Fee Change History", Enabled=false };
    private bool loadingFeeHistory;
    private readonly PictureBox photo = new() {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = AppTheme.SurfaceSecondary,
        BorderStyle = BorderStyle.FixedSingle
    };
    private readonly Label nameLabel = new() {
        Dock = DockStyle.Top,
        Height = AppScale.Scale(26),
        Font = AppTheme.HeadingFont,
        ForeColor = AppTheme.HeadingText,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };
    private readonly Label placementLabel = new() {
        Dock = DockStyle.Top,
        Height = AppScale.Scale(20),
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.MutedText,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };
    private readonly Label registrationLabel = new() {
        Dock = DockStyle.Top,
        Height = AppScale.Scale(20),
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.MutedText,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };
    private readonly Label contactLabel = new() {
        Dock = DockStyle.Top,
        Height = AppScale.Scale(20),
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.MutedText,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };
    private readonly Label balanceLabel = new() {
        Dock = DockStyle.Top,
        Height = AppScale.Scale(24),
        Font = AppTheme.FontSmallBold,
        ForeColor = AppTheme.MutedText,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };
    private readonly DataGridView contextGrid = HumanUi.Grid();
    private readonly DataGridView familyGrid = HumanUi.Grid();
    private readonly DataGridView timelineGrid = HumanUi.Grid();
    private readonly Label familyEmptyLabel = new() {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.MutedText,
        Text = "This student does not currently belong to a family.",
        Visible = false
    };
    public StudentProfilePage(Action<Func<Control>, string, string>? navigate = null)
        : base(
            "Student Profile",
            "View a student’s details, family, balance and history.")
    {
        UiHelper.DisposePictureImageOnDispose(photo);
        this.navigate = navigate;
        student.UserSelectionChanged += async (_, _) => await LoadProfileAsync();
        student.UserSelectionChanged += (_, _) => UpdateFeeHistoryAvailability();
        /* Editing the box away from the student on screen drops that student's profile, so nothing is shown under a name that is no longer in the box. */
        student.SelectionCleared += (_, _) => {
            ClearProfile();
            UpdateFeeHistoryAvailability();
            SetStatus("Select a student to view their profile.");
        };
        student.SearchFailed += ex => Fail(ex);
        StudentSearchCache.Warm();
        UiHelper.SetGridPlaceholderMessage(contextGrid, UiText.EmptyGrid.SelectStudent);
        UiHelper.SetGridPlaceholderMessage(familyGrid, UiText.EmptyGrid.SelectStudent);
        UiHelper.SetGridPlaceholderMessage(timelineGrid, UiText.EmptyGrid.SelectStudent);
        var selector = HumanUi.Card("Student", "Type the student's Admission Number or name, then click the student in the list.");
        selector.Content.Controls.Add(HumanUi.Field("Student", student, true));
        var header = BuildHeaderCard();
        var sections = BuildSectionsGrid();
        var footer = BuildQuickActionsFooter();
        /* The finder and the identity card share one row (finder left, photo and name right), so the vertical space the finder used to take goes to the three sections below. */
        var topRow = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        topRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        selector.Margin = new Padding(0, 0, AppScale.Scale(4), 0);
        header.Margin = new Padding(AppScale.Scale(4), 0, 0, 0);
        topRow.Controls.Add(selector, 0, 0);
        topRow.Controls.Add(header, 1, 0);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            Math.Max(AppMetrics.FieldCardHeight(1),
                header.Header.Height + header.Padding.Vertical + header.Content.Padding.Vertical +
                IdentityContentHeight)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            Math.Max(AppScale.Scale(96), 2 * (AppTheme.CompactButtonHeight + UiDesignSystem.Spacing.Sm) + UiDesignSystem.Spacing.Md)));
        root.Controls.Add(topRow, 0, 0);
        root.Controls.Add(sections, 0, 1);
        root.Controls.Add(footer, 0, 2);
        SetContent(root);
        ClearProfile();
    }
    private int IdentityContentHeight => new[] { nameLabel, placementLabel, contactLabel, balanceLabel }
        .Sum(label => Math.Max(label.Height, AppMetrics.TextHeight(label.Font) + AppScale.Scale(4)));
    private HumanCard BuildHeaderCard()
    {
        var card = HumanUi.Card("Identity & Standing", "Pulled from the student's current admission record.");
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AppScale.Scale(96)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var photoHost = new Panel {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, UiDesignSystem.Spacing.Md, 0)
        };
        photoHost.Controls.Add(photo);
        var textHost = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0)
        };
        textHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var label in new[] { nameLabel, placementLabel, contactLabel, balanceLabel })
        {
            label.Margin = Padding.Empty;
            textHost.RowStyles.Add(new RowStyle(SizeType.Absolute,
                Math.Max(label.Height, AppMetrics.TextHeight(label.Font) + AppScale.Scale(4))));
        }
        /* Identified by Admission Number (the first part of the name line). */
        textHost.Controls.Add(nameLabel, 0, 0);
        textHost.Controls.Add(placementLabel, 0, 1);
        textHost.Controls.Add(contactLabel, 0, 2);
        textHost.Controls.Add(balanceLabel, 0, 3);
        layout.Controls.Add(photoHost, 0, 0);
        layout.Controls.Add(textHost, 1, 0);
        card.Content.Controls.Add(layout);
        return card;
    }
    private Control BuildSectionsGrid()
    {
        var grid = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        // TOP LEFT: Family / Siblings
        var familyCard = HumanUi.Card("Family / Siblings", "Other admitted children linked to this student's family.");
        familyCard.Margin = new Padding(0, AppScale.Scale(4), AppScale.Scale(4), AppScale.Scale(4));
        var familyHost = new Panel {
            Dock = DockStyle.Fill
        };
        familyHost.Controls.Add(familyGrid);
        familyHost.Controls.Add(familyEmptyLabel);
        familyEmptyLabel.BringToFront();
        familyCard.Content.Controls.Add(familyHost);
        // TOP RIGHT: Lifecycle Timeline
        var timelineCard = HumanUi.Card("Lifecycle Timeline", "Admission, transfers, promotions and status history.");
        timelineCard.Margin = new Padding(AppScale.Scale(4), AppScale.Scale(4), 0, AppScale.Scale(4));
        timelineCard.Content.Controls.Add(timelineGrid);
        var feeHistoryActions=HumanUi.ButtonRow(feeHistoryButton);
        feeHistoryActions.Dock=DockStyle.Bottom;
        timelineCard.Content.Controls.Add(feeHistoryActions);
        // BOTTOM: Enrollment & Academic Context Full width
        var contextCard = HumanUi.Card("Academic Placement", "Current campus, class, section and status.");
        contextCard.Margin = new Padding(0, AppScale.Scale(4), 0, 0);
        contextCard.Content.Controls.Add(contextGrid);
        // Layout
        grid.Controls.Add(familyCard, 0, 0);
        grid.Controls.Add(timelineCard, 1, 0);
        grid.Controls.Add(contextCard, 0, 1);
        // Make Enrollment & Academic Context span both columns.
        grid.SetColumnSpan(contextCard, 2);
        return grid;
    }
    /* The six jump buttons were one long strip that scrolled sideways on a narrow window. */
    private Control BuildQuickActionsFooter()
    {
        var table = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, UiDesignSystem.Spacing.Sm, 0, 0),
            Padding = new Padding(0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AppScale.Scale(150)));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        FlowLayoutPanel Row() => new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        Label GroupLabel(string text) => new Label {
            Text = text,
            Dock = DockStyle.Fill,
            Font = UiDesignSystem.Typography.Caption,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false
        };
        var recordRow = Row();
        recordRow.Controls.Add(
            JumpButton("Open Admissions", id => new StudentAdmissionPage(navigate, preselectAdmissionId: id), title: PageTitles.Admissions));
        recordRow.Controls.Add(
            JumpButton("Open Family / Siblings", id => new StudentFamilyPage(id)));
        recordRow.Controls.Add(
            JumpButton("Open Photo", id => new StudentPhotoPage(id), title: "Student Photo"));
        var lifecycleRow = Row();
        lifecycleRow.Controls.Add(
            JumpButton("Open " + PageTitles.StudentRecordChecks, id => new StudentRecordChecksPage(id)));
        lifecycleRow.Controls.Add(
            JumpButton(
                "Open " + PageTitles.AnnualFeeReceipts,
                id => new FMSC_SMS.Pages.Reports.Finance.AnnualFeeReceiptsPage(id),
                module: "Finance"));
        feeHistoryButton.Height=AppTheme.CompactButtonHeight;
        feeHistoryButton.Margin=new Padding(0,0,UiDesignSystem.Spacing.Sm,0);
        AppMetrics.FitButtonWidth(feeHistoryButton,minWidth:150);
        feeHistoryButton.SetOutlineColors(AppTheme.InputBorder,AppTheme.Primary,AppTheme.Surface);
        feeHistoryButton.Click += async (_, _) => {
            if (loadingFeeHistory) return;
            loadingFeeHistory=true;
            UpdateFeeHistoryAvailability();
            try { await FMSC_SMS.Pages.Reports.Finance.StudentFeeChangeHistory.ShowAsync(this,() => student.SelectedId); }
            catch(Exception ex) { if(!IsDisposed) Fail(ex); }
            finally { loadingFeeHistory=false; if(!IsDisposed) UpdateFeeHistoryAvailability(); }
        };
        table.Controls.Add(GroupLabel("Student record"), 0, 0);
        table.Controls.Add(recordRow, 1, 0);
        table.Controls.Add(GroupLabel("Lifecycle & fees"), 0, 1);
        table.Controls.Add(lifecycleRow, 1, 1);
        return table;
    }
    private void UpdateFeeHistoryAvailability() => feeHistoryButton.Enabled=!loadingFeeHistory && student.SelectedId>0
        && CurrentUser.HasPermission("Fees.View") && CurrentUser.HasPermission("Fees.Statement.View");
    private ModernButton JumpButton(string text, Func<int, Control> factory, string module = "Students", string? title = null)
    {
        var button = new ModernButton {
            Text = text,
            Height = AppTheme.CompactButtonHeight,
            Margin = new Padding(
                0,
                0,
                UiDesignSystem.Spacing.Sm,
                0),
            Tag = UiDesignSystem.Actions.Secondary
        };
        AppMetrics.FitButtonWidth(button, minWidth: 150);
        button.SetOutlineColors(AppTheme.InputBorder, AppTheme.Primary, AppTheme.Surface);
        button.Click += (_, _) => {
            int admissionId = student.SelectedId;
            Func<Control> openPage = () => factory(admissionId);
            if (navigate is not null)
            {
                navigate(openPage, module, title ?? text.Replace("Open ", string.Empty));
                return;
            }
            using var dialog = new ApplicationWindowForm {
                Text = text,
                StartPosition = FormStartPosition.CenterParent,
                Width = AppScale.Scale(1200),
                Height = AppScale.Scale(760),
                MinimumSize = new Size(
                    AppScale.Scale(980),
                    AppScale.Scale(620)),
                BackColor = AppTheme.PageBackground
            };
            var control = openPage();
            control.Dock = DockStyle.Fill;
            dialog.Controls.Add(control);
            dialog.ShowDialog(FindForm());
        };
        return button;
    }
    private async Task LoadProfileAsync()
    {
        try
        {
            if (student.SelectedId <= 0)
            {
                Warn(UiText.Prompts.SelectStudent);
                return;
            }
            int admissionId = student.SelectedId;
            /* Item 14 - everything on this page belongs to the student being replaced. */
            ClearProfile();
            SetStatus("Loading student profile...");
            var contextTask = DatabaseHelper.QueryUserAsync(
                "GetStudentDetails",
                DatabaseHelper.Int("@AdmissionId", admissionId));
            var photoTask = Task.Run(() =>
                DatabaseHelper.QueryUser(
                    "GetStudentPhoto",
                    DatabaseHelper.Int("@AdmissionId", admissionId)));
            var familyLookupTask = Task.Run(() =>
                DatabaseHelper.QueryUser(
                    "GetStudentFamily",
                    DatabaseHelper.Int("@AdmissionId", admissionId)));
            var timelineTask = Task.Run(() =>
                DatabaseHelper.QueryUser(
                    "GetStudentLifecycleTimeline",
                    DatabaseHelper.Int("@AdmissionId", admissionId)));
            await Task.WhenAll(contextTask, photoTask, familyLookupTask, timelineTask);
            if (IsDisposed ||
                student.SelectedId != admissionId)
            {
                return;
            }
            RenderIdentity(await contextTask, admissionId);
            RenderPhoto(await photoTask);
            BindContextGrid(await contextTask);
            BindMiniGrid(timelineGrid, await timelineTask);
            ConfigureTimelineGrid();
            RenderFamily(await familyLookupTask, admissionId);
            await LoadBalanceAsync(admissionId);
            if (IsDisposed ||
                student.SelectedId != admissionId)
            {
                return;
            }
            SetStatus("Profile loaded.");
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                Fail(ex);
        }
    }
    private void RenderIdentity(DataTable students, int admissionId)
    {
        DataRow? row = students
            .AsEnumerable()
            .FirstOrDefault(r =>
                r["AdmissionId"] != DBNull.Value &&
                Convert.ToInt32(r["AdmissionId"]) == admissionId);
        if (row is null)
        {
            nameLabel.Text = "Student not found.";
            placementLabel.Text = string.Empty;
            registrationLabel.Text = string.Empty;
            contactLabel.Text = string.Empty;
            return;
        }
        string name = CellText(row, "StudentName");
        string father = CellText(row, "FatherName");
        string className = CellText(row, "ClassName");
        string sectionName = CellText(row, "SectionName");
        string status = CellText(row, "Status");
        string registration = CellText(row, "RegistrationNumber");
        string contact = CellText(row, "ContactNumber");
        nameLabel.Text =
            string.IsNullOrWhiteSpace(father)
                ? $"{AdmissionNumbers.ForId(admissionId)} - {name}"
                : $"{AdmissionNumbers.ForId(admissionId)} - {name} (Father: {father})";
        string placement =
            string.IsNullOrWhiteSpace(className)
                ? "Not currently enrolled"
                : string.IsNullOrWhiteSpace(sectionName)
                    ? className
                    : $"{className} - {sectionName}";
        placementLabel.Text =
            string.IsNullOrWhiteSpace(status)
                ? placement
                : $"{placement} - {status}";
        registrationLabel.Text =
            string.IsNullOrWhiteSpace(registration)
                ? string.Empty
                : $"Registration No.: {registration}";
        contactLabel.Text =
            string.IsNullOrWhiteSpace(contact)
                ? string.Empty
                : $"Contact: {contact}";
    }
    private void RenderPhoto(DataTable photoTable)
    {
        UiHelper.ReplacePictureImage(photo, null);
        if (photoTable.Rows.Count == 0 ||
            !photoTable.Columns.Contains("PhotoData"))
        {
            return;
        }
        try
        {
            var bytes =
                (byte[])photoTable.Rows[0]["PhotoData"];
            using var stream =
                new MemoryStream(bytes);
            using var loaded =
                Image.FromStream(stream);
            photo.Image =
                new Bitmap(loaded);
        }
        catch (Exception nonFatalException76)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException76, "StudentProfilePage: RenderPhoto fallback");
            // A corrupt or unreadable photo shouldn't block the rest of the profile.
        }
    }
    private void RenderFamily(DataTable familyLookup, int admissionId)
    {
        if (familyLookup.Rows.Count == 0 ||
            !familyLookup.Columns.Contains("FamilyId"))
        {
            familyEmptyLabel.Text = "This student does not currently belong to a family.";
            familyGrid.DataSource = null;
            familyGrid.Visible = false;
            familyEmptyLabel.Visible = true;
            return;
        }
        // The family procedure returns every member, including the selected child.
        var members = familyLookup.Clone();
        foreach (DataRow member in familyLookup.Rows)
            if (member["AdmissionId"] != DBNull.Value && Convert.ToInt32(member["AdmissionId"]) != admissionId)
                members.ImportRow(member);
        if (IsDisposed ||
            student.SelectedId != admissionId)
        {
            return;
        }
        if (members.Rows.Count == 0)
        {
            familyEmptyLabel.Text = "No other admitted children in this family.";
            familyGrid.DataSource = null;
            familyGrid.Visible = false;
            familyEmptyLabel.Visible = true;
            return;
        }
        familyEmptyLabel.Visible = false;
        familyGrid.Visible = true;
        string[] visible = { "AdmissionNumber", "StudentName", "ClassSection", "CampusName", "Status" };
        if (!members.Columns.Contains("ClassSection"))
        {
            members.Columns.Add("ClassSection", typeof(string));
            foreach (DataRow member in members.Rows)
                member["ClassSection"] = PlacementText(member);
        }
        BindMiniGrid(familyGrid, members);
        int order = 0;
        foreach (string name in visible)
            if (familyGrid.Columns[name] is { } column)
            {
                column.DisplayIndex = order++;
                column.HeaderText = name switch {
                    "AdmissionNumber" => "Adm. No.", "StudentName" => "Student",
                    "ClassSection" => "Class", "CampusName" => "Campus", _ => "Status"
                };
            }
        foreach (DataGridViewColumn column in familyGrid.Columns)
            column.Visible = visible.Contains(column.DataPropertyName);
        familyGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
    }
    private async Task LoadBalanceAsync(int admissionId)
    {
        // Business rule (V20 audit F-04): everyone with Students.View may see the
        // profile balance, including users without Reports.View. Use the dedicated
        // Students.View procedure; do not weaken permissions on unrelated reports.
        // See Docs/AUDIT_DECISIONS.md.
        try
        {
            object? value =
                await DatabaseHelper.ScalarUserAsync(
                    "GetStudentProfileOutstandingBalance",
                    DatabaseHelper.Int("@AdmissionId", admissionId));
            if (IsDisposed ||
                student.SelectedId != admissionId)
            {
                return;
            }
            decimal balance =
                value is null ||
                value == DBNull.Value
                    ? 0
                    : Convert.ToDecimal(value);
            balanceLabel.Text =
                $"Outstanding Balance: Rs. {balance:N0}";
            balanceLabel.ForeColor =
                balance > 0
                    ? AppTheme.Danger
                    : AppTheme.Success;
        }
        catch (Exception ex)
            when (ex is Microsoft.Data.SqlClient.SqlException sql &&
                  sql.Number is 2812 or 208)
        {
            balanceLabel.Text =
                "Outstanding Balance: unavailable. Contact the administrator.";
            balanceLabel.ForeColor =
                AppTheme.MutedText;
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                Fail(ex);
        }
    }
    /* The identity card above already shows name, father, registration number and contact. */
    private static readonly string[] ContextColumns =
    {
        "AcademicYearName", "YearName", "CurrentCampusName", "ClassSection",
        "EnrollmentStatus", "EnrollmentDate", "TransportText",
        "AdmissionDate", "AdmissionCampusName", "DateOfBirth", "Gender"
    };
    private void BindContextGrid(DataTable table)
    {
        var display = table.Copy();
        display.Columns.Add("ClassSection", typeof(string));
        display.Columns.Add("TransportText", typeof(string));
        foreach (DataRow row in display.Rows)
        {
            row["ClassSection"] = PlacementText(row);
            row["TransportText"] = display.Columns.Contains("UsesTransport") && row["UsesTransport"] != DBNull.Value
                && Convert.ToBoolean(row["UsesTransport"]) ? "Yes" : "No";
        }
        BindMiniGrid(contextGrid, display);
        int order = 0;
        foreach (string name in ContextColumns)
        {
            DataGridViewColumn? column = contextGrid.Columns[name];
            if (column is not null)
                column.DisplayIndex = order++;
        }
        foreach (DataGridViewColumn column in contextGrid.Columns)
            column.Visible = Array.IndexOf(ContextColumns, column.DataPropertyName) >= 0;
        SetHeader("CurrentCampusName", "Campus");
        SetHeader("AdmissionCampusName", "Admission Campus");
        SetHeader("EnrollmentStatus", "Status");
        SetHeader("TransportText", "Transport");
        SetHeader("ClassSection", "Class");
        SetHeader("YearName", "Academic Year");
    }
    private static string PlacementText(DataRow row)
    {
        string className = CellText(row, "ClassName");
        string section = CellText(row, "SectionName");
        return string.IsNullOrWhiteSpace(section) ? className
            : string.IsNullOrWhiteSpace(className) ? section : $"{className} - {section}";
    }
    private void SetHeader(string column, string text)
    {
        DataGridViewColumn? found = contextGrid.Columns[column];
        if (found is not null)
            found.HeaderText = text;
    }
    private static void BindMiniGrid(DataGridView grid, DataTable table)
    {
        grid.DataSource = null;
        grid.DataSource = table;
        grid.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.None;
    }
    private void ConfigureTimelineGrid()
    {
        timelineGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        timelineGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        timelineGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        timelineGrid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.TopLeft;
        timelineGrid.ScrollBars = ScrollBars.Both;
        foreach (DataGridViewColumn column in timelineGrid.Columns)
        {
            // Keep dates and event labels compact, leaving room for the full description.
            column.MinimumWidth = AppScale.Scale(40);
            column.FillWeight = column.DataPropertyName switch
            {
                "EventDate" => 18,
                "EventType" => 25,
                "Description" => 57,
                _ => 30
            };
        }
    }
    private static string CellText(
        DataRow row,
        string columnName) =>
        row.Table.Columns.Contains(columnName) &&
        row[columnName] != DBNull.Value
            ? Convert.ToString(
                row[columnName]) ?? string.Empty
            : string.Empty;
    private void ClearProfile()
    {
        nameLabel.Text =
            "Select a student to see their full profile.";
        placementLabel.Text = string.Empty;
        registrationLabel.Text = string.Empty;
        contactLabel.Text = string.Empty;
        balanceLabel.Text = string.Empty;
        UiHelper.ReplacePictureImage(photo, null);
        contextGrid.DataSource = null;
        familyGrid.DataSource = null;
        familyGrid.Visible = true;
        familyEmptyLabel.Visible = false;
        timelineGrid.DataSource = null;
        SetStatus("Select a student to see their full profile.");
    }
}

