using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Students.Dialogs;
public sealed class PossibleSiblingSearchForm : ApplicationWindowForm
{
    private readonly int currentAdmissionId;
    private readonly int userId;
    private readonly ModernTextBox searchBox =
        HumanUi.TextBox(false, 300);
    private readonly ModernButton searchButton = new() {
        Text = "Find",
        Width = AppScale.Scale(110),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = UiDesignSystem.Actions.Primary
    };
    private readonly ModernButton addButton = new() {
        Text = "Add as Sibling",
        Width = AppScale.Scale(155),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = UiDesignSystem.Actions.Success
    };
    private readonly ModernButton closeButton = new() {
        Text = UiText.Actions.Close,
        Width = AppScale.Scale(110),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = UiDesignSystem.Actions.Secondary
    };
    private readonly DataGridView resultsGrid =
        HumanUi.Grid(false);
    private readonly Label status = new() {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.MutedText,
        AutoEllipsis = true,
        Margin = new Padding(0)
    };
    private readonly Label selectedStudent =
        CreateDetailLabel();
    private readonly Label selectedFather =
        CreateDetailLabel();
    private readonly Label selectedAdmissionId =
        CreateDetailLabel();
    private readonly Label selectedRegistration =
        CreateDetailLabel();
    private readonly Label selectedClass =
        CreateDetailLabel();
    private readonly Label selectedSection =
        CreateDetailLabel();
    private readonly Label selectedStatus =
        CreateDetailLabel();
    private readonly HashSet<int> currentFamilyIds = new();
    private int? currentFamilyId;
    public bool AddedSibling { get; private set; }
    public PossibleSiblingSearchForm(IWin32Window? owner, int admissionId, string fatherName)
    {
        currentAdmissionId = admissionId;
        userId = CurrentUser.UserId;
        Text = "Find Possible Sibling";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Width = AppScale.Scale(900, AppScale.ControlSizeScale);
        Height = AppScale.Scale(610, AppScale.ControlSizeScale);
        BackColor = AppTheme.PageBackground;
        ForeColor = AppTheme.Text;
        Font = AppTheme.NormalFont;
        searchBox.PlaceholderText =
            "Admission Number, student name or father name";
        searchBox.Text = fatherName;
        ConfigureGrid();
        searchButton.Click += (_, _) => Search();
        addButton.Click += (_, _) =>
            AddSelectedSibling();
        closeButton.Click += (_, _) =>
            Close();
        resultsGrid.SelectionChanged += (_, _) =>
            ShowSelectedDetails();
        resultsGrid.CellDoubleClick += (_, e) => {
            if (e.RowIndex >= 0)
                AddSelectedSibling();
        };
        var searchCard = BuildSearchCard();
        var resultsCard = BuildResultsCard();
        var selectedCard = BuildSelectedCard();
        var actionRow = HumanUi.ButtonRow(addButton, closeButton);
        actionRow.Margin = new Padding(
            0,
            AppScale.Scale(4, AppScale.ControlSizeScale), 0, 0);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(
                AppMetrics.PagePadding,
                AppMetrics.PagePadding,
                AppMetrics.PagePadding,
                AppMetrics.PagePadding),
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                AppScale.Scale(145, AppScale.ControlSizeScale)));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                AppScale.Scale(118, AppScale.ControlSizeScale)));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                AppMetrics.ButtonRowHeight +
                AppScale.Scale(8, AppScale.ControlSizeScale)));
        root.Controls.Add(searchCard, 0, 0);
        root.Controls.Add(resultsCard, 0, 1);
        root.Controls.Add(selectedCard, 0, 2);
        root.Controls.Add(actionRow, 0, 3);
        Controls.Add(root);
        CancelButton = closeButton;
        addButton.Enabled = false;
        status.Text =
            "Enter a search value, or use the father's name already entered on the admission.";
        ClearSelectedDetails();
        Shown += (_, _) =>
            Search();
    }
    /* UX - Enter did nothing anywhere on this dialog. The grid consumes Enter itself, so this is handled in ProcessCmdKey, which runs before it. */
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter && ActiveControl is not ButtonBase and not ComboBox { DroppedDown: true })
        {
            if (resultsGrid.ContainsFocus)
            {
                if (addButton.Enabled)
                    AddSelectedSibling();
                return true;
            }
            if (searchButton.Enabled)
                Search();
            return true;
        }
        if (keyData == Keys.Down && ActiveControl is TextBox && resultsGrid.Rows.Count > 0)
        {
            resultsGrid.Focus();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private Control BuildSearchCard()
    {
        var card = HumanUi.Card(
            "Find Possible Sibling",
            "Search by student name, father name or Admission Number.",
            AppTheme.ModuleAccent("Students"));
        var searchRoot = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        searchRoot.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));
        searchRoot.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                AppScale.Scale(120, AppScale.ControlSizeScale)));
        searchRoot.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        var searchField = HumanUi.Field("Search", searchBox);
        searchField.Margin = new Padding(
            0,
            0,
            AppScale.Scale(10, AppScale.ControlSizeScale), 0);
        searchRoot.Controls.Add(searchField, 0, 0);
        searchButton.Dock = DockStyle.Bottom;
        searchRoot.Controls.Add(searchButton, 1, 0);
        card.Content.Controls.Add(searchRoot);
        return card;
    }
    private Control BuildResultsCard()
    {
        var card = HumanUi.Card("Possible Students", string.Empty, AppTheme.ModuleAccent("Students"));
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(resultsGrid, 0, 0);
        card.Content.Controls.Add(root);
        return card;
    }
    private Control BuildSelectedCard()
    {
        var card = HumanUi.Card(
            "Selected Student",
            "Verify the student details before adding the sibling relationship.",
            AppTheme.ModuleAccent("Students"));
        var details = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        /* Admission Number first: it is what identifies a student. The registration number is only a reporting label and follows the names. */
        details.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 10));
        details.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 22));
        details.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 16));
        details.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 16));
        details.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 12));
        details.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 12));
        details.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 12));
        details.Controls.Add(
            BuildDetailBlock("Admission Number", selectedAdmissionId), 0, 0);
        details.Controls.Add(
            BuildDetailBlock("Student", selectedStudent), 1, 0);
        details.Controls.Add(
            BuildDetailBlock("Father", selectedFather), 2, 0);
        details.Controls.Add(
            BuildDetailBlock("Registration", selectedRegistration), 3, 0);
        details.Controls.Add(
            BuildDetailBlock("Class", selectedClass), 4, 0);
        details.Controls.Add(
            BuildDetailBlock("Section", selectedSection), 5, 0);
        details.Controls.Add(
            BuildDetailBlock("Status", selectedStatus), 6, 0);
        card.Content.Controls.Add(details);
        return card;
    }
    private static Control BuildDetailBlock(string caption, Label value)
    {
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(
                0,
                0,
                AppScale.Scale(10, AppScale.ControlSizeScale), 0), Padding = new Padding(0)
        };
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                AppScale.Scale(22, AppScale.ControlSizeScale)));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        var captionLabel = new Label {
            Dock = DockStyle.Fill,
            Text = caption,
            Font = AppTheme.ButtonFont,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        };
        value.Dock = DockStyle.Fill;
        value.TextAlign =
            ContentAlignment.MiddleLeft;
        value.Margin = new Padding(0);
        root.Controls.Add(captionLabel, 0, 0);
        root.Controls.Add(value, 0, 1);
        return root;
    }
    private static Label CreateDetailLabel()
    {
        return new Label {
            Dock = DockStyle.Fill,
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
    }
    private void ConfigureGrid()
    {
        resultsGrid.Dock = DockStyle.Fill;
        resultsGrid.AutoGenerateColumns = true;
        resultsGrid.AllowUserToAddRows = false;
        resultsGrid.AllowUserToDeleteRows = false;
        resultsGrid.ReadOnly = true;
        resultsGrid.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;
        resultsGrid.MultiSelect = false;
        resultsGrid.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.None;
        resultsGrid.AutoSizeRowsMode =
            DataGridViewAutoSizeRowsMode.None;
        resultsGrid.ColumnHeadersDefaultCellStyle.WrapMode =
            DataGridViewTriState.False;
        resultsGrid.ColumnHeadersHeight =
            AppMetrics.GridHeaderHeight;
        resultsGrid.RowTemplate.Height =
            AppMetrics.GridRowHeight;
        resultsGrid.Margin = new Padding(0);
    }
    private void LoadCurrentFamilyIds()
    {
        currentFamilyIds.Clear();
        currentFamilyId = null;
        try
        {
            var family = DatabaseHelper.QueryUser(
                "GetStudentFamily",
                DatabaseHelper.Int("@AdmissionId", currentAdmissionId));
            if (!family.Columns.Contains("AdmissionId"))
            {
                return;
            }
            if (family.Columns.Contains("FamilyId") &&
                family.Rows.Count > 0 &&
                family.Rows[0]["FamilyId"] != DBNull.Value)
            {
                currentFamilyId = Convert.ToInt32(family.Rows[0]["FamilyId"]);
            }
            foreach (DataRow row in family.Rows)
            {
                int id =
                    row["AdmissionId"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(row["AdmissionId"]);
                if (id > 0)
                    currentFamilyIds.Add(id);
            }
        }
        catch (Exception ex)
        {
            /* this was an empty catch. The query builds the set of admissions already in this student's family, which is what stops the sibling search suggesting someone already in the family. */
            AppExceptionHandler.LogNonFatal(
                ex,
                "Loading the current student's existing family for the sibling search - already-linked members may be suggested as new siblings");
        }
    }
    private static Dictionary<int, (int? FamilyId, int MemberCount)> LoadFamilyMembership(IEnumerable<int> admissionIds)
    {
        var result = new Dictionary<int, (int? FamilyId, int MemberCount)>();
        var ids = admissionIds.ToList();
        if (ids.Count == 0)
            return result;
        try
        {
            var tvp = new SqlParameter(
                "@AdmissionIds",
                SqlDbType.Structured) {
                TypeName = "dbo.AdmissionIdList",
                Value = Tvp.AdmissionIds(ids)
            };
            var raw = DatabaseHelper.QueryUser("GetFamilyMembershipSummary", tvp);
            foreach (DataRow row in raw.Rows)
            {
                int admissionId = Convert.ToInt32(row["AdmissionId"]);
                int? familyId = row["FamilyId"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(row["FamilyId"]);
                int memberCount = row["FamilyMemberCount"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(row["FamilyMemberCount"]);
                result[admissionId] = (familyId, memberCount);
            }
        }
        catch (Exception nonFatalException71)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException71, "PossibleSiblingSearchForm: LoadFamilyMembership fallback");
            // Non-critical; the results grid simply won't show family membership if this lookup fails, and AddStudentSibling still enforces its own cross-family safety check.
        }
        return result;
    }
    private void Search()
    {
        try
        {
            string text =
                searchBox.Text.Trim();
            if (text.Length < 2 &&
                !AdmissionNumbers.TryNormalize(text, out _))
            {
                resultsGrid.DataSource = null;
                ClearSelectedDetails();
                status.Text =
                    "Enter at least 2 characters for a student/father name, or an Admission Number such as 1 or 1a.";
                return;
            }
            LoadCurrentFamilyIds();
            var raw = DatabaseHelper.QueryUser(
                "GetStudents",
                DatabaseHelper.Int(
                    "@CampusId",
                    0),
                DatabaseHelper.Int(
                    "@AcademicYearId",
                    0),
                DatabaseHelper.P(
                    "@Search",
                    null),
                DatabaseHelper.Bit("@IncludeFormerStudents", true));
            List<DataRow> matches = HumanUi.FindStudentMatches(raw, text);
            var display = new DataTable();
            display.Columns.Add("Student Name", typeof(string));
            display.Columns.Add("Father Name", typeof(string));
            display.Columns.Add(UiText.Fields.RegistrationNumber, typeof(string));
            display.Columns.Add("Class", typeof(string));
            display.Columns.Add("Section", typeof(string));
            display.Columns.Add("Status", typeof(string));
            display.Columns.Add("AdmissionId", typeof(int));
            display.Columns.Add("Family", typeof(string));
            display.Columns.Add("FamilyId", typeof(int));
            var candidateIds = new List<int>();
            foreach (DataRow row in matches)
            {
                int id =
                    row["AdmissionId"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(row["AdmissionId"]);
                if (id <= 0 ||
                    id == currentAdmissionId ||
                    currentFamilyIds.Contains(id))
                {
                    continue;
                }
                candidateIds.Add(id);
                display.Rows.Add(
                    HumanUi.StudentDisplay(
                        id,
                        GetText(row, "StudentName"),
                        GetText(row, "FatherName"),
                        GetText(row, "Gender"),
                        GetText(row, "RegistrationNumber"),
                        GetText(row, "ClassName"),
                        GetText(row, "SectionName")),
                    GetText(
                        row,
                        "FatherName"),
                    GetText(
                        row,
                        "RegistrationNumber"),
                    GetText(
                        row,
                        "ClassName"),
                    GetText(
                        row,
                        "SectionName"),
                    GetText(row, "Status"), id, string.Empty, DBNull.Value);
            }
            var familyMembership = LoadFamilyMembership(candidateIds);
            foreach (DataRow row in display.Rows)
            {
                int id = Convert.ToInt32(row["AdmissionId"]);
                if (!familyMembership.TryGetValue(id, out var membership) ||
                    membership.FamilyId is null)
                {
                    continue;
                }
                row["Family"] =
                    $"Already in a family ({membership.MemberCount:N0} member(s))";
                row["FamilyId"] = membership.FamilyId.Value;
            }
            resultsGrid.DataSource =
                display;
            if (resultsGrid.Columns.Contains("AdmissionId"))
            {
                resultsGrid.Columns[
                    "AdmissionId"].Visible = false;
            }
            if (resultsGrid.Columns.Contains("FamilyId"))
            {
                resultsGrid.Columns[
                    "FamilyId"].Visible = false;
            }
            if (resultsGrid.Columns.Contains(UiText.Fields.RegistrationNumber))
            {
                resultsGrid.Columns[
                    UiText.Fields.RegistrationNumber].Visible = false;
            }
            ConfigureGridColumns();
            ClearSelectedDetails();
            resultsGrid.ClearSelection();
            status.Text =
                display.Rows.Count > 0
                    ? $"{display.Rows.Count} possible student(s) found. Select one to verify."
                    : "No matching students were found. Try the student name, father name, or Admission Number.";
            if (display.Rows.Count > 0)
            {
                resultsGrid.Rows[0].Selected =
                    true;
                ShowSelectedDetails();
            }
        }
        catch (Exception ex)
        {
            resultsGrid.DataSource = null;
            ClearSelectedDetails();
            status.Text =
                "Unable to search for possible siblings right now.";
            ModernMessageDialog.Show(this, DbMessage.UserMessage(ex), "Sibling Search", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private void ConfigureGridColumns()
    {
        SetColumnWeight("Student Name", 180);
        SetColumnWeight("Father Name", 175);
        SetColumnWeight("Class", 95);
        SetColumnWeight("Section", 90);
        SetColumnWeight("Status", 95);
        SetColumnWeight("Family", 170);
    }
    private void SetColumnWeight(string columnName, float weight)
    {
        if (!resultsGrid.Columns.Contains(columnName))
        {
            return;
        }
        resultsGrid.Columns[
            columnName].FillWeight = weight;
        resultsGrid.Columns[
            columnName]
            .DefaultCellStyle.Alignment =
            DataGridViewContentAlignment.MiddleLeft;
    }
    private void ShowSelectedDetails()
    {
        if (resultsGrid.CurrentRow is null ||
            !resultsGrid.Columns.Contains("AdmissionId"))
        {
            ClearSelectedDetails();
            return;
        }
        var row =
            resultsGrid.CurrentRow;
        object? rawId =
            row.Cells[
                "AdmissionId"].Value;
        int id =
            rawId is null || rawId == DBNull.Value
                ? 0
                : Convert.ToInt32(rawId);
        if (id <= 0)
        {
            ClearSelectedDetails();
            return;
        }
        selectedAdmissionId.Text = AdmissionNumbers.ForId(id);
        selectedStudent.Text =
            Convert.ToString(
                row.Cells[
                    "Student Name"].Value)
            ?? string.Empty;
        selectedFather.Text =
            Convert.ToString(
                row.Cells[
                    "Father Name"].Value)
            ?? string.Empty;
        selectedRegistration.Text =
            Convert.ToString(
                row.Cells[
                    UiText.Fields.RegistrationNumber].Value)
            ?? string.Empty;
        selectedClass.Text =
            Convert.ToString(
                row.Cells[
                    "Class"].Value)
            ?? string.Empty;
        selectedSection.Text =
            Convert.ToString(
                row.Cells[
                    "Section"].Value)
            ?? string.Empty;
        selectedStatus.Text =
            Convert.ToString(
                row.Cells[
                    "Status"].Value)
            ?? string.Empty;
        addButton.Enabled = true;
    }
    private void ClearSelectedDetails()
    {
        selectedAdmissionId.Text = "—";
        selectedStudent.Text = "—";
        selectedFather.Text = "—";
        selectedRegistration.Text = "—";
        selectedClass.Text = "—";
        selectedSection.Text = "—";
        selectedStatus.Text = "—";
        addButton.Enabled = false;
    }
    private void AddSelectedSibling()
    {
        try
        {
            if (resultsGrid.CurrentRow is null ||
                !resultsGrid.Columns.Contains("AdmissionId"))
            {
                /* UX - nothing was attempted, so this is said in the form's own status line. */
                status.Text = "Select the correct student from the list first.";
                return;
            }
            int siblingId =
                Convert.ToInt32(
                    resultsGrid.CurrentRow
                        .Cells[
                            "AdmissionId"].Value);
            if (siblingId <= 0 ||
                siblingId == currentAdmissionId)
            {
                status.Text = "Select a different, valid student.";
                return;
            }
            string studentName =
                selectedStudent.Text;
            int? siblingFamilyId = null;
            object? siblingFamilyIdValue =
                resultsGrid.CurrentRow.Cells[
                    "FamilyId"].Value;
            if (siblingFamilyIdValue is not null &&
                siblingFamilyIdValue != DBNull.Value)
            {
                siblingFamilyId =
                    Convert.ToInt32(siblingFamilyIdValue);
            }
            string familyWarning = string.Empty;
            if (siblingFamilyId is not null)
            {
                string memberCountText =
                    Convert.ToString(
                        resultsGrid.CurrentRow.Cells[
                            "Family"].Value)
                    ?? string.Empty;
                familyWarning = currentFamilyId is not null &&
                    currentFamilyId != siblingFamilyId
                        ? $"⚠ WARNING: {studentName} already belongs to a different, existing family ({memberCountText}). " +
                          "The system will not allow linking two students who each already belong to different families — " +
                          "double-check this is the right student before continuing.\n\n"
                        : $"⚠ WARNING: {studentName} is already {memberCountText.ToLowerInvariant()}. " +
                          "Continuing will add this student's current family into the relationship being created here. " +
                          "Make sure this is not a same-name coincidence before continuing.\n\n";
            }
            var confirm =
                ModernMessageDialog.Show(
                    this,
                    familyWarning +
                    $"Add {studentName} as a sibling?",
                    "Confirm Sibling",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
            if (confirm !=
                DialogResult.Yes)
            {
                return;
            }
            DatabaseHelper.Execute(
                "AddStudentSibling",
                DatabaseHelper.P(
                    "@UserId",
                    userId),
                DatabaseHelper.Int(
                    "@AdmissionId",
                    currentAdmissionId),
                DatabaseHelper.Int(
                    "@SiblingAdmissionId",
                    siblingId),
                DatabaseHelper.P("@RelationshipType", "Sibling"));
            AddedSibling = true;
            LoadCurrentFamilyIds();
            Search();
            /* UX - a completed add is reported in the status line, after the refreshed search result, rather than in a message box the operator has to dismiss. */
            status.Text = $"{studentName} has been added as a sibling.";
        }
        catch (Exception ex)
        {
            ModernMessageDialog.Show(this, DbMessage.UserMessage(ex), "Sibling Search", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private static string GetText(DataRow row, string columnName)
    {
        return row.Table.Columns.Contains(
                   columnName) &&
               row[columnName] != DBNull.Value
            ? Convert.ToString(row[columnName])
              ?? string.Empty
            : string.Empty;
    }
}

