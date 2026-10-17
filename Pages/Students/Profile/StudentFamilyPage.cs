using FMSC_SMS.Core;
using FMSC_SMS.Pages.Students.Dialogs;
using FMSC_SMS.Pages.Students.Enrollment;
namespace FMSC_SMS.Pages.Students.Profile;
public sealed class StudentFamilyPage : V2Page, IBusyPage
{
    // Lists students who already belong to a family.
    private readonly StudentPicker student = new();
    /* Only students who already belong to a family are offered (GetStudentsWithFamily). */
    private HashSet<int> familyAdmissionIds = new();
    private readonly TableLayoutPanel headHost = new() {
        Dock = DockStyle.Top,
        ColumnCount = 3,
        RowCount = 1,
        Margin = new Padding(0),
        Padding = new Padding(0)
    };
    private readonly FlowLayoutPanel membersFlow = new() {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        WrapContents = true,
        FlowDirection = FlowDirection.LeftToRight,
        Margin = new Padding(0),
        Padding = new Padding(0)
    };
    private readonly Label emptyStateLabel = new() {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.MutedText,
        Text = "Select a student to view their family.",
        AutoEllipsis = true
    };
    // Displayed admission IDs; the first is the highest-class family member.
    private readonly List<int> currentMemberAdmissionIds = new();
    private string currentHeadFatherName = string.Empty;
    private FamilyMemberCard? selectedCard;
    private int _familyLoadVersion;
    private readonly Func<int, Task<DataTable>> readMembers;
    private readonly Func<int, Task<bool>> confirmReturn;
    private ModernButton? confirmReturnButton;
    public StudentFamilyPage() : this(0) { }
    public StudentFamilyPage(int preloadAdmissionId) : this(preloadAdmissionId,true) { }
    internal StudentFamilyPage(int preloadAdmissionId,bool initialize)
        : this(preloadAdmissionId, initialize, FamilyBillPreview.ReadMembersAsync, null) { }
    internal StudentFamilyPage(int preloadAdmissionId,bool initialize,Func<int,Task<DataTable>> readMembers,
        Func<int,Task<bool>>? confirmReturn) : base(
        "Family / Siblings",
        "Manage families and siblings. Create families in Student Admissions > Fees & Siblings.")
    {
        this.readMembers=readMembers;
        this.confirmReturn=confirmReturn ?? (id=>{
            using var dialog=new ConfirmReturnAdmissionDialog(id);
            return Task.FromResult(dialog.ShowDialog(FindForm())==DialogResult.OK);
        });
        var refresh = AddBusyAction(UiText.Actions.Refresh, async () => {
            if (student.SelectedId <= 0) { Warn(UiText.Prompts.SelectStudent); return; }
            await LoadFamilyForSelectedStudentAsync();
        }, "Secondary");
        /* adding a member writes a sibling link; the button is disabled while it runs so a double click cannot attempt it twice. */
        var addMember = AddAction("Add Member", async (_, _) => await RunFamilyActionAsync(AddMemberAsync), "Success");
        /* removing a member deletes a sibling link, so it is guarded like adding one. */
        var removeMember = AddAction("Remove Selected Member", async (_, _) => await RunFamilyActionAsync(RemoveSelectedMemberAsync), "Danger");
    /* Students.Family permits viewing; add/remove require Students.Administer in both C# and SQL. */
        GateAction(addMember, "Students.Administer");
        GateAction(removeMember, "Students.Administer");
        confirmReturnButton=AddAction("Confirm Selected Return",async(_,_)=>await RunFamilyActionAsync(ConfirmSelectedReturnAsync),"Primary");
        GateAction(confirmReturnButton,"Students.Administer");
        /* Actions target the currently displayed family and selected card. */
        _addMemberButton = addMember;
        _removeMemberButton = removeMember;
        SetFamilyActionsEnabled(false);
        RelocateFromActions(refresh, addMember, removeMember, confirmReturnButton);
        student.UserSelectionChanged += async (_, _) => await LoadFamilyForSelectedStudentAsync();
        student.SelectionCleared += (_, _) => ClearMembersView();
        student.RowFilter = row => familyAdmissionIds.Contains(GetInt(row, "AdmissionId"));
        student.SearchFailed += ex => Fail(ex);
        if(initialize)StudentSearchCache.Warm();
        if(initialize)Load += async (_, _) => await LoadStudentsAsync(
            preserveAdmissionId: preloadAdmissionId > 0 ? preloadAdmissionId : null,
            initialSelection: preloadAdmissionId > 0);
        var details = HumanUi.Card("Family / Siblings", "Pick a student who already has a family to view or manage it below.");
        var fields = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.Controls.Add(HumanUi.Field("Student", student, true), 0, 0);
        details.Content.Controls.Add(fields);
        var buttonRow = HumanUi.ButtonRow(refresh, addMember, removeMember, confirmReturnButton);
        buttonRow.Dock = DockStyle.Bottom;
        details.Content.Controls.Add(buttonRow);
        headHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        headHost.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        headHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        headHost.Height = AppScale.Scale(150);
        var membersHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        membersHost.Controls.Add(membersFlow);
        membersHost.Controls.Add(emptyStateLabel);
        emptyStateLabel.BringToFront();
        var cardsRoot = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        cardsRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, headHost.Height));
        cardsRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cardsRoot.Controls.Add(headHost, 0, 0);
        cardsRoot.Controls.Add(membersHost, 0, 1);
        var membersCard = HumanUi.Card("Family Members", "Highest-class student is shown first; the rest of the family follows below it.");
        membersCard.Content.Controls.Add(cardsRoot);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(1) + AppMetrics.ButtonRowHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(details, 0, 0);
        root.Controls.Add(membersCard, 0, 1);
        SetContent(root);
    }
    private static DataTable NewStudentLookupTable()
    {
        var dt = new DataTable();
        dt.Columns.Add("AdmissionId", typeof(int));
        dt.Columns.Add("Display", typeof(string));
        return dt;
    }
    private async Task LoadStudentsAsync(int? preserveAdmissionId = null, bool initialSelection = false)
    {
        try
        {
            var ids = await Task.Run(() => {
                var familyTable = DatabaseHelper.QueryUser("GetStudentsWithFamily");
                var found = new HashSet<int>();
                if (familyTable.Columns.Contains("AdmissionId"))
                {
                    foreach (DataRow row in familyTable.Rows)
                        if (row["AdmissionId"] != DBNull.Value)
                            found.Add(Convert.ToInt32(row["AdmissionId"]));
                }
                return found;
            });
            DataTable all = await StudentSearchCache.GetAllAsync();
            if (IsDisposed) return;
            familyAdmissionIds = ids;
            DataRow? keep = null;
            if (initialSelection && preserveAdmissionId is int initialId)
                keep = await StudentSearchCache.FindByAdmissionIdAsync(initialId);
            else if (preserveAdmissionId is int keepId && ids.Contains(keepId))
            {
                foreach (DataRow row in all.Rows)
                {
                    if (GetInt(row, "AdmissionId") == keepId)
                    {
                        keep = row;
                        break;
                    }
                }
            }
            // Opening a profile link must not replace a choice made while it was loading.
            if (IsDisposed || (initialSelection &&
                (student.SelectedId > 0 || !string.IsNullOrWhiteSpace(student.Text)))) return;
            if (keep is not null)
            {
                student.SetStudent(keep);
                await LoadFamilyForSelectedStudentAsync();
            }
            else
            {
                student.Clear();
                ClearMembersView();
                SetStatus(initialSelection
                    ? "This student could not be found. Search for a student to continue."
                    : ids.Count > 0
                    ? "Type a student's name or Admission Number to view their family."
                    : "No families exist yet. Start one from a student's Admission page (Fees & Siblings tab).");
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed) Fail(ex);
        }
    }
    private ModernButton? _addMemberButton;
    private ModernButton? _removeMemberButton;
    /* Preserve permission-denied action states. */
    private readonly bool _canChangeFamily =
        CurrentUser.HasPermission("Students.Administer");
    private bool familyActionBusy;
    public bool IsBusy=>familyActionBusy;
    public string BusyMessage=>"A family link or return is being processed. Wait for the result and refreshed family before leaving.";
    private async Task RunFamilyActionAsync(Func<Task> action)
    {
        if (familyActionBusy) return;
        familyActionBusy = true;
        try
        {
            using var busy = BeginBusy("Processing family…", _addMemberButton, _removeMemberButton, confirmReturnButton, student);
            await action();
        }
        finally
        {
            familyActionBusy = false;
            if (!IsDisposed) SetFamilyActionsEnabled(currentMemberAdmissionIds.Count > 0);
        }
    }
    private void SetFamilyActionsEnabled(bool enabled)
    {
        if(confirmReturnButton is not null)
            confirmReturnButton.Enabled=enabled && !familyActionBusy && selectedCard?.AwaitsReturn==true
                && CurrentUser.HasPermission("Students.Administer");
        if (!_canChangeFamily)
            return;
        if (_addMemberButton is not null &&
            !_addMemberButton.IsDisposed)
        {
            _addMemberButton.Enabled = enabled && !familyActionBusy;
        }
        if (_removeMemberButton is not null &&
            !_removeMemberButton.IsDisposed)
        {
            _removeMemberButton.Enabled = enabled && !familyActionBusy && selectedCard is not null;
        }
    }
    private async Task LoadFamilyForSelectedStudentAsync()
    {
        int version = 0;
        try
        {
            int admissionId = student.SelectedId;
            if (admissionId <= 0)
            {
                ClearMembersView();
                return;
            }
            /* Clear cards and selection when switching students. */
            ClearMembersView();
            version = _familyLoadVersion;
            SetStatus("Loading family members...");
            var familyLookup = await readMembers(admissionId);
            /* The captured student must still be the selected one: this load runs while the picker stays live. */
            if (IsDisposed || version != _familyLoadVersion ||
                student.SelectedId != admissionId)
            {
                return;
            }
            if (familyLookup.Rows.Count == 0 || !familyLookup.Columns.Contains("FamilyId") || familyLookup.Rows[0].IsNull("FamilyId"))
            {
                ClearMembersView();
                SetStatus("This student does not currently belong to a family.", true);
                return;
            }
            var members = familyLookup;
            /* Keep the displayed members tied to the selected student. */
            if (IsDisposed || version != _familyLoadVersion ||
                student.SelectedId != admissionId)
            {
                return;
            }
            RenderMembers(members);
            /* Restore actions only after a current load succeeds. */
            SetFamilyActionsEnabled(currentMemberAdmissionIds.Count > 0);
            int waiting=members.AsEnumerable().Count(FamilyBillingData.AwaitsReturn);
            SetStatus($"{members.Rows.Count} member(s) loaded. {waiting} awaiting return. Select a child to review their readiness.");
        }
        catch (Exception ex)
        {
            if (!IsDisposed && version == _familyLoadVersion) Fail(ex);
        }
    }
    private void ClearMembersView(bool invalidateLoad = true)
    {
        if (invalidateLoad) _familyLoadVersion++;
        DisposeMemberCards(headHost);
        DisposeMemberCards(membersFlow);
        currentMemberAdmissionIds.Clear();
        currentHeadFatherName = string.Empty;
        selectedCard = null;
        emptyStateLabel.Text = "Select a student to view their family.";
        emptyStateLabel.Visible = true;
        SetFamilyActionsEnabled(false);
    }
    private void RenderMembers(DataTable table)
    {
        ClearMembersView(invalidateLoad: false);
        if (table.Rows.Count == 0)
        {
            emptyStateLabel.Text = "This family has no members.";
            emptyStateLabel.Visible = true;
            SetFamilyActionsEnabled(false);
            return;
        }
        emptyStateLabel.Visible = false;
        var rows = table.Rows.Cast<DataRow>().ToList();
        // GetStudentFamily orders by class rank (highest first), so the first row is the "head" of the family shown on its own above.
        var headCard = CreateCard(rows[0], isHead: true);
        headHost.Controls.Add(headCard, 1, 0);
        currentMemberAdmissionIds.Add(GetInt(rows[0], "AdmissionId"));
        currentHeadFatherName = GetText(rows[0], "FatherName");
        for (int i = 1; i < rows.Count; i++)
        {
            membersFlow.Controls.Add(CreateCard(rows[i], isHead: false));
            currentMemberAdmissionIds.Add(GetInt(rows[i], "AdmissionId"));
        }
    }
    private static void DisposeMemberCards(Control host)
    {
        // Controls.Clear only detaches cards; dispose their handles and children.
        while (host.Controls.Count > 0)
            host.Controls[0].Dispose();
    }
    private FamilyMemberCard CreateCard(DataRow row, bool isHead)
    {
        var (label, color) = RelationshipBadge(row, isHead);
        var card = new FamilyMemberCard(
            familyMemberId: GetInt(row, "FamilyMemberId"),
            admissionId: GetInt(row, "AdmissionId"),
            studentName: GetText(row, "StudentName"),
            classSection: BuildClassSection(row)+(GetText(row,"AcademicYear").Length>0?" · "+GetText(row,"AcademicYear"):""),
            studentReference: GetText(row,"AdmissionNumber").Length>0?GetText(row,"AdmissionNumber"):GetText(row,"QuickReference"),
            statusText: FamilyBillingData.Readiness(row),
            relationshipLabel: label,
            relationshipColor: color,
            isHead: isHead,
            awaitsReturn: FamilyBillingData.AwaitsReturn(row));
        card.CardSelected += (_, _) => SelectCard(card);
        return card;
    }
    private void SelectCard(FamilyMemberCard card)
    {
        selectedCard?.SetSelected(false);
        selectedCard = card;
        card.SetSelected(true);
        SetFamilyActionsEnabled(currentMemberAdmissionIds.Count > 0);
        SetStatus($"Selected {card.StudentName}. "+(card.AwaitsReturn
            ? "Review and confirm this child's return before printing new-year fees."
            : "Review the child's billing readiness above. Family changes retain their fees and payments."));
    }
    private async Task ConfirmSelectedReturnAsync()
    {
        if(selectedCard is not { AwaitsReturn:true } card || !CurrentUser.HasPermission("Students.Administer"))return;
        int anchor=student.SelectedId, version=_familyLoadVersion;
        try
        {
            bool confirmed=await confirmReturn(card.AdmissionId);
            if(IsDisposed || anchor!=student.SelectedId || version!=_familyLoadVersion)return;
            await LoadFamilyForSelectedStudentAsync();
            if(IsDisposed || anchor!=student.SelectedId || _familyLoadVersion!=version+1)return;
            var next=headHost.Controls.OfType<FamilyMemberCard>().Concat(membersFlow.Controls.OfType<FamilyMemberCard>())
                .FirstOrDefault(member=>member.AwaitsReturn);
            if(next is not null)SelectCard(next);
            SetStatus(confirmed ? "Return confirmed for "+card.StudentName+". "+
                (next is null?"Review the family bill in Fee Collection.":"Next: "+next.StudentName+". Review and confirm separately.")
                : "Return was not confirmed. Family readiness has been refreshed.");
        }
        catch(Exception ex){if(!IsDisposed)Fail(ex);}
    }
    private static string BuildClassSection(DataRow row)
    {
        string className = GetText(row, "ClassName");
        string sectionName = GetText(row, "SectionName");
        if (string.IsNullOrWhiteSpace(className))
            return "Not currently enrolled";
        return string.IsNullOrWhiteSpace(sectionName)
            ? className
            : $"{className} - {sectionName}";
    }
    private static (string Label, Color Color) RelationshipBadge(DataRow row, bool isHead)
    {
        if (isHead)
            return ("Head of Family", AppTheme.Primary);
        string gender = GetText(row, "Gender");
        if (string.Equals(gender, "Female", StringComparison.OrdinalIgnoreCase))
            return ("Sister", Color.FromArgb(219, 39, 119));
        if (string.Equals(gender, "Male", StringComparison.OrdinalIgnoreCase))
            return ("Brother", Color.FromArgb(37, 99, 235));
        return ("Sibling", AppTheme.MutedText);
    }
    private async Task AddMemberAsync()
    {
        if (!ValidateRequiredInputs())
            return;
        try
        {
            if (student.SelectedId <= 0 || currentMemberAdmissionIds.Count == 0)
            {
                Warn("Select a student with an existing family first.");
                return;
            }
            int anchorAdmissionId = currentMemberAdmissionIds[0];
            int selectedAdmissionId = student.SelectedId;
            using var dialog = new PossibleSiblingSearchForm(FindForm(), anchorAdmissionId, currentHeadFatherName);
            dialog.ShowDialog(FindForm());
            if (dialog.AddedSibling)
            {
                await LoadStudentsAsync(preserveAdmissionId: selectedAdmissionId);
                Info("Student added to the family successfully.");
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    private async Task RemoveSelectedMemberAsync()
    {
        try
        {
            if (selectedCard is null)
            {
                Warn("Select a family member card first.");
                return;
            }
            if (!Confirm($"Remove {selectedCard.StudentName} from this family? This cannot be undone.", "Remove Family Member"))
            {
                return;
            }
            string removedStudentName = selectedCard.StudentName;
            int familyMemberId = selectedCard.FamilyMemberId;
            int removedAdmissionId = selectedCard.AdmissionId;
            int selectedAdmissionId = student.SelectedId;
            await Task.Run(() => DatabaseHelper.Execute(
                "DeleteStudentSibling",
                DatabaseHelper.P("@UserId", CurrentUser.UserId),
                DatabaseHelper.Int("@FamilyMemberId", familyMemberId)));
            selectedCard = null;
            // If the removed student was selected, select another remaining family member.
            int? preserve = removedAdmissionId == selectedAdmissionId
                ? currentMemberAdmissionIds.FirstOrDefault(id => id != removedAdmissionId)
                : selectedAdmissionId;
            if (preserve == 0)
                preserve = null;
            await LoadStudentsAsync(preserveAdmissionId: preserve);
            Info($"{removedStudentName} removed from this family.");
        }
        catch (Exception ex) { Fail(ex); }
    }
    private static int GetInt(DataRow row, string columnName)
    {
        return row.Table.Columns.Contains(columnName) &&
               row[columnName] != DBNull.Value
            ? Convert.ToInt32(row[columnName])
            : 0;
    }
    private static string GetText(DataRow row, string columnName)
    {
        return row.Table.Columns.Contains(columnName) &&
               row[columnName] != DBNull.Value
            ? Convert.ToString(row[columnName]) ?? string.Empty
            : string.Empty;
    }
    // Show student identity, placement, status and family relationship.
    private sealed class FamilyMemberCard : RoundedPanel
    {
        private readonly Color baseBorderColor;
        public int FamilyMemberId { get; }
        public int AdmissionId { get; }
        public string StudentName { get; }
        public bool AwaitsReturn { get; }
        public event EventHandler? CardSelected;
        public FamilyMemberCard(
            int familyMemberId,
            int admissionId,
            string studentName,
            string classSection,
            string studentReference,
            string statusText,
            string relationshipLabel,
            Color relationshipColor,
            bool isHead, bool awaitsReturn)
        {
            FamilyMemberId = familyMemberId;
            AdmissionId = admissionId;
            StudentName = studentName;
            AwaitsReturn=awaitsReturn;
            Width = AppScale.Scale(220);
            Height = AppScale.Scale(134);
            Margin = new Padding(UiDesignSystem.Spacing.Sm);
            Padding = new Padding(UiDesignSystem.Spacing.Md, UiDesignSystem.Spacing.Sm, UiDesignSystem.Spacing.Md, 0);
            CornerRadius = AppTheme.CardRadius;
            BackColor = isHead ? AppTheme.Tint(AppTheme.Primary, 0.94f) : AppTheme.Surface;
            baseBorderColor = isHead ? AppTheme.Primary : AppTheme.Border;
            BorderColor = baseBorderColor;
            ShowBorder = true;
            Cursor = Cursors.Hand;
            var layout = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.Scale(24)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.Scale(20)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.Scale(18)));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var nameLabel = new Label {
                Text = studentName,
                Dock = DockStyle.Fill,
                Font = AppTheme.HeadingFont,
                ForeColor = AppTheme.HeadingText,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            var classLabel = new Label {
                Text = classSection,
                Dock = DockStyle.Fill,
                Font = AppTheme.NormalFont,
                ForeColor = AppTheme.MutedText,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            /* Lead with Admission Number and status; omit registration from the card. */
            var identityParts = new List<string>();
            if (admissionId > 0)
                identityParts.Add(studentReference.Length>0?studentReference:$"Student {admissionId}");
            if (!string.IsNullOrWhiteSpace(statusText))
                identityParts.Add(statusText);
            string registrationLine = string.Join(" · ", identityParts);
            var registrationLabel = new Label {
                Text = registrationLine,
                Dock = DockStyle.Fill,
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.MutedText,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            var badge = new Label {
                Text = relationshipLabel,
                Dock = DockStyle.Fill,
                Font = AppTheme.FontSmallBold,
                ForeColor = Color.White,
                BackColor = relationshipColor,
                TextAlign = ContentAlignment.MiddleCenter
            };
            layout.Controls.Add(nameLabel, 0, 0);
            layout.Controls.Add(classLabel, 0, 1);
            layout.Controls.Add(registrationLabel, 0, 2);
            layout.Controls.Add(badge, 0, 3);
            AccessibleName=studentName+" · "+classSection+" · "+registrationLine;
            registrationLabel.AccessibleName=registrationLine;
            classLabel.AccessibleName=classSection;
            Controls.Add(layout);
            void RaiseSelected(object? sender, EventArgs e) =>
                CardSelected?.Invoke(this, EventArgs.Empty);
            Click += RaiseSelected;
            layout.Click += RaiseSelected;
            nameLabel.Click += RaiseSelected;
            classLabel.Click += RaiseSelected;
            registrationLabel.Click += RaiseSelected;
            badge.Click += RaiseSelected;
        }
        public void SetSelected(bool selected)
        {
            BorderColor = selected ? AppTheme.Danger : baseBorderColor;
            Invalidate();
        }
    }
}
