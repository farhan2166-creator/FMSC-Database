using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Students.Maintenance;
/// <summary>Runs a fuzzy (soundex-based) name/father-name + exact-DOB sweep across all admitted students and surfaces candidate pairs for manual review, in the same "detect -> review -> decide" spirit as StudentRecordChecksPage.</summary>
public sealed class DuplicateStudentAuditPage : V2Page, IBusyPage
{
    public bool IsBusy=>BusyOperationRunning;
    public string BusyMessage=>"Duplicate review is being updated. Wait for the scan or selected batch and its refresh to finish.";
    public DuplicateStudentAuditPage(Action<Func<Control>,string,string>? navigate = null)
        : base(
            "Find Duplicate Students",
            "Review possible duplicates with matching names, father’s names and birth dates.")
    {
        Grid.MultiSelect = true;
        var scan = AddBusyAction("Scan for Duplicates", ScanAsync, "Primary");
        /* resolving a pair writes dbo.ResolveDuplicateStudentReview once per selected row, so a double click meant the whole batch ran twice. */
        var confirm = AddBusyAction("Confirm Duplicate", async () => {
            await ResolveSelectedAsync("Confirmed");
            /* The busy guard puts the clicked button back to Enabled when it ends; the grid selection decides whether it should be. Re-derived after that. */
            Defer(UpdateResolveButtons);
        }, "Warning");
        var dismiss = AddBusyAction("Dismiss", async () => {
            await ResolveSelectedAsync("Dismissed");
            /* The busy guard puts the clicked button back to Enabled when it ends; the grid selection decides whether it should be. Re-derived after that. */
            Defer(UpdateResolveButtons);
        }, "Secondary");
        /* Item 13 - binding highlights the first row without raising SelectionChanged, so nothing on screen said which pair these two buttons would act on. */
        _confirmButton = confirm;
        _dismissButton = dismiss;
        confirm.Enabled = false;
        dismiss.Enabled = false;
        Grid.SelectionChanged += (_, _) => UpdateResolveButtons();
        var export = AddExportAction("FMSC_Duplicate_Student_Candidates");
        var openFirst=AddAction("Open First Record",(_,_)=>OpenRecord("AdmissionId",navigate),"Secondary");
        var openSecond=AddAction("Open Second Record",(_,_)=>OpenRecord("AdmissionId2",navigate),"Secondary");
        GateActionAny(openFirst,"Students.Admit","Students.Administer");GateActionAny(openSecond,"Students.Admit","Students.Administer");
        RelocateFromActions(openFirst,openSecond);
        RelocateFromActions(scan, confirm, dismiss, export);
        Defer(LoadAsync);
        var buttonRow = HumanUi.ButtonRow(scan, openFirst, openSecond, confirm, dismiss, export);
        buttonRow.Dock = DockStyle.Fill;
        buttonRow.Margin = new Padding(0, 0, 0, UiDesignSystem.Spacing.Sm);
        var card = HumanUi.Card(
            "Open Candidate Pairs",
            "Compare both students; select rows to Confirm Duplicate or Dismiss. No automatic merging or deletion.");
        var gridHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        gridHost.Controls.Add(Grid);
        var recordsLayout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        recordsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        recordsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        recordsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        recordsLayout.Controls.Add(buttonRow, 0, 0);
        recordsLayout.Controls.Add(gridHost, 0, 1);
        card.Content.Controls.Add(recordsLayout);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(card, 0, 0);
        SetContent(root);
    }
    private readonly ModernButton _confirmButton;
    private readonly ModernButton _dismissButton;
    private void OpenRecord(string key,Action<Func<Control>,string,string>? navigate)
    {
        if(Grid.CurrentRow?.DataBoundItem is not DataRowView row){Warn("Select a candidate pair first.");return;}
        int admission=Convert.ToInt32(row[key]);
        Func<Control> factory=()=>new Admissions.StudentAdmissionPage(navigate,admission);
        if(navigate is not null)navigate(factory,"Students",PageTitles.Admissions);
        else {using var window=new ApplicationWindowForm{Text=PageTitles.Admissions,WindowState=FormWindowState.Maximized};var page=factory();page.Dock=DockStyle.Fill;window.Controls.Add(page);window.ShowDialog(FindForm());}
    }
    protected override void OnReportPageBound()
    {
        // Keep the bound review data for selection and export, but show only the identifying details needed to compare the two students at a glance.
        var visibleColumns = new (string Key, string Header)[] {
            ("AdmissionNumber", "1 · Adm. No."),
            ("StudentName", "1 · Name"),
            ("FatherName", "1 · Father"),
            ("AdmittedIn", "1 · Admitted / Readmitted"),
            ("LeftOrCurrent", "1 · Left / Current"),
            ("AdmissionNumber2", "2 · Adm. No."),
            ("StudentName2", "2 · Name"),
            ("FatherName2", "2 · Father"),
            ("AdmittedIn2", "2 · Admitted / Readmitted"),
            ("LeftOrCurrent2", "2 · Left / Current"),
            ("DateOfBirth", "Date of Birth")
        };
        foreach (DataGridViewColumn column in Grid.Columns)
            column.Visible = visibleColumns.Any(item => item.Key == column.Name);
        for (int index = 0; index < visibleColumns.Length; index++)
        {
            var item = visibleColumns[index];
            if (!Grid.Columns.Contains(item.Key))
                continue;
            var column = Grid.Columns[item.Key];
            column.HeaderText = item.Header;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.DisplayIndex = index;
        }
        Grid.ScrollBars = ScrollBars.Both;
        Grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        foreach (string name in new[] { "AdmittedIn", "LeftOrCurrent", "AdmittedIn2", "LeftOrCurrent2" })
            Grid.Columns[name].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        if (Grid.Columns.Contains("DateOfBirth"))
            Grid.Columns["DateOfBirth"].DefaultCellStyle.Format = "dd-MMM-yyyy";
    }
    private void UpdateResolveButtons()
    {
        if (IsDisposed)
            return;
        bool hasSelection = !BusyOperationRunning&&SelectedPairs().Count > 0;
        if (!_confirmButton.IsDisposed)
            _confirmButton.Enabled = hasSelection;
        if (!_dismissButton.IsDisposed)
            _dismissButton.Enabled = hasSelection;
    }
    /// <summary>Item 13 - the candidate pairs the grid selection stands for, each with the two students named, so the confirmation can say WHICH pairs are being decided rather than only how many.</summary>
    private List<(int ReviewId, string Label)> SelectedPairs()
    {
        var pairs = new List<(int ReviewId, string Label)>();
        var seen = new HashSet<int>();
        foreach (DataGridViewRow gridRow in Grid.SelectedRows)
        {
            if (gridRow.DataBoundItem is not DataRowView view)
                continue;
            DataRow row = view.Row;
            if (!row.Table.Columns.Contains("ReviewId") ||
                row["ReviewId"] == DBNull.Value)
                continue;
            int reviewId = Convert.ToInt32(row["ReviewId"]);
            if (!seen.Add(reviewId))
                continue;
            pairs.Add((reviewId, PairLabel(row)));
        }
        return pairs;
    }
    private static string PairLabel(DataRow row)
    {
        string Text(string column) =>
            row.Table.Columns.Contains(column) &&
            row[column] != DBNull.Value
                ? (Convert.ToString(row[column]) ?? string.Empty).Trim()
                : string.Empty;
        string one = Text("StudentName");
        string two = Text("StudentName2");
        string oneId = Text("AdmissionNumber");
        string twoId = Text("AdmissionNumber2");
        string oneIdentity = string.IsNullOrWhiteSpace(oneId) ? string.Empty : $"Admission Number {oneId}";
        string twoIdentity = string.IsNullOrWhiteSpace(twoId) ? string.Empty : $"Admission Number {twoId}";
        return
            (oneIdentity.Length == 0 ? one : $"{one} ({oneIdentity})") +
            " and " +
            (twoIdentity.Length == 0 ? two : $"{two} ({twoIdentity})");
    }
    private static DataTable ReadComparisonData()
    {
        var candidates = DatabaseHelper.QueryUser("GetDuplicateStudentCandidates",
            DatabaseHelper.P("@StatusFilter", "Open"));
        foreach (string column in new[] { "AdmittedIn", "LeftOrCurrent", "AdmittedIn2", "LeftOrCurrent2" })
            candidates.Columns.Add(column, typeof(string));
        if (candidates.Rows.Count == 0) return candidates;
        var ids = candidates.AsEnumerable().SelectMany(row => new[] {
            Convert.ToInt32(row["AdmissionId"]), Convert.ToInt32(row["AdmissionId2"])
        }).Distinct().ToArray();
        var events = DatabaseHelper.QuerySql("""
            EXEC dbo.RequirePermission @UserId, N'Students.View';
            WITH People AS
            (
                SELECT S.* FROM dbo.Students S
                JOIN OPENJSON(@Ids) I ON S.AdmissionId = TRY_CONVERT(int,I.value)
            ), Events AS
            (
                SELECT S.AdmissionId, N'Admitted' Kind, S.AdmissionDate EventDate,
                    (SELECT TOP(1) H.NewClassSectionId FROM dbo.EnrollmentHistory H
                         WHERE H.AdmissionId=S.AdmissionId AND H.ActionType=N'ADMIT'
                           AND H.ActionDate=S.AdmissionDate ORDER BY H.EnrollmentHistoryId) ClassSectionId
                FROM People S
                UNION ALL
                SELECT S.AdmissionId, CASE WHEN H.ActionType=N'RE-ADMIT' THEN N'Readmitted' ELSE N'Left' END,
                    H.ActionDate, CASE WHEN H.ActionType=N'RE-ADMIT' THEN H.NewClassSectionId ELSE H.OldClassSectionId END
                FROM People S JOIN dbo.EnrollmentHistory H ON H.AdmissionId=S.AdmissionId
                WHERE H.ActionType IN(N'RE-ADMIT',N'LEAVE')
                UNION ALL
                SELECT S.AdmissionId,N'Left',S.WithdrawalDate,
                    (SELECT TOP(1) E.ClassSectionId FROM dbo.Enrollments E
                     WHERE E.AdmissionId=S.AdmissionId AND E.LeavingDate=S.WithdrawalDate
                     ORDER BY E.EnrollmentId DESC)
                FROM People S WHERE S.WithdrawalDate IS NOT NULL AND NOT EXISTS
                    (SELECT 1 FROM dbo.EnrollmentHistory H WHERE H.AdmissionId=S.AdmissionId
                     AND H.ActionType=N'LEAVE' AND H.ActionDate=S.WithdrawalDate)
                UNION ALL
                SELECT S.AdmissionId,S.Status,CAST(NULL AS date),
                    (SELECT TOP(1) E.ClassSectionId FROM dbo.Enrollments E
                     WHERE E.AdmissionId=S.AdmissionId AND (S.Status<>N'Active' OR E.Status=N'Active')
                     ORDER BY CASE WHEN E.Status=N'Active' THEN 0 ELSE 1 END,
                         E.EnrollmentDate DESC,E.EnrollmentId DESC)
                FROM People S WHERE S.Status<>N'Left' OR
                    (S.WithdrawalDate IS NULL AND NOT EXISTS
                     (SELECT 1 FROM dbo.EnrollmentHistory H WHERE H.AdmissionId=S.AdmissionId AND H.ActionType=N'LEAVE'))
            )
            SELECT E.AdmissionId,E.Kind,E.EventDate,C.ClassName,SEC.SectionName
            FROM Events E
            LEFT JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
            LEFT JOIN dbo.Classes C ON C.ClassId=CCS.ClassId
            LEFT JOIN dbo.Sections SEC ON SEC.SectionId=CCS.SectionId
            ORDER BY E.AdmissionId,CASE WHEN E.EventDate IS NULL THEN 1 ELSE 0 END,E.EventDate;
            """, DatabaseHelper.P("@UserId", CurrentUser.UserId),
            DatabaseHelper.P("@Ids", System.Text.Json.JsonSerializer.Serialize(ids)));
        var summaries = events.AsEnumerable().GroupBy(row => Convert.ToInt32(row["AdmissionId"]))
            .ToDictionary(group => group.Key, group => (
                Admitted: string.Join(Environment.NewLine, group.Where(IsEntry).Select(EventText)),
                LeftOrCurrent: string.Join(Environment.NewLine, group.Where(row => !IsEntry(row)).Select(EventText))));
        foreach (DataRow row in candidates.Rows)
            foreach (string suffix in new[] { "", "2" })
                if (summaries.TryGetValue(Convert.ToInt32(row["AdmissionId" + suffix]), out var summary))
                {
                    row["AdmittedIn" + suffix] = summary.Admitted;
                    row["LeftOrCurrent" + suffix] = summary.LeftOrCurrent;
                }
        // Registration numbers are unnecessary in both the comparison and its export.
        candidates.Columns.Remove("RegistrationNumber");
        candidates.Columns.Remove("RegistrationNumber2");
        return candidates;
    }
    private static bool IsEntry(DataRow row) => Convert.ToString(row["Kind"]) is "Admitted" or "Readmitted";
    private static string EventText(DataRow row)
    {
        string placement = string.Join(" - ", new[] {
            Convert.ToString(row["ClassName"]), Convert.ToString(row["SectionName"])
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (placement.Length == 0) placement = "class/section not recorded";
        string date = row["EventDate"] is DateTime value ? $" · {value:dd-MMM-yyyy}" : string.Empty;
        return $"{row["Kind"]}{date} · {placement}";
    }
    private async Task LoadAsync()
    {
        try
        {
            await BindReportAsync(ReadComparisonData);
            /* Item 13 - the bind leaves a row highlighted that nobody chose. */
            if (!IsDisposed)
            {
                Grid.ClearSelection();
                UpdateResolveButtons();
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    private Task ScanAsync()=>RunBusyAsync("Scanning duplicate candidates…",ScanCoreAsync,this);
    private async Task ScanCoreAsync()
    {
        try
        {
            SetStatus("Scanning admitted students for likely duplicates...");
            object? found = await DatabaseHelper.ScalarUserAsync("ScanForDuplicateStudents");
            int foundCount = found is null or DBNull ? 0 : Convert.ToInt32(found);
            await LoadAsync();
            Info(foundCount > 0
                ? $"Scan complete. {foundCount:N0} new candidate pair(s) found and added below."
                : "Scan complete. No new candidate pairs were found.");
        }
        catch (Exception ex) { Fail(ex); }
    }
    private Task ResolveSelectedAsync(string status)=>RunBusyAsync("Updating selected duplicate candidates…",()=>ResolveSelectedCoreAsync(status),this);
    private async Task ResolveSelectedCoreAsync(string status)
    {
        try
        {
            var pairs = SelectedPairs();
            var reviewIds = pairs
                .Select(pair => pair.ReviewId)
                .ToList();
            if (reviewIds.Count == 0)
            {
                Warn("Select one or more candidate pairs in the grid first (click a row, or Ctrl/Shift-click to select several), then try again.");
                return;
            }
            string verb = status == "Confirmed" ? "confirmed as a likely duplicate" : "dismissed";
            /* Item 13 - the pairs are named in the question. */
            var named = pairs
                .Take(6)
                .Select(pair => $"   - {pair.Label}")
                .ToList();
            if (pairs.Count > named.Count)
                named.Add($"   - and {pairs.Count - named.Count:N0} more");
            if (!Confirm(
                $"Mark {reviewIds.Count:N0} candidate pair(s) as {verb}?\n\n" +
                string.Join("\n", named) +
                "\n\nThis only records the office's finding - it does not merge, edit or delete any student data.",
                "Find Duplicate Students"))
            {
                return;
            }
            int updated = 0;
            /* A per-row failure used to be swallowed, so "N of M" never said which rows failed or why. Each failure now keeps an operator-facing reason and the summary reports it. */
            var failures = new List<string>();
            foreach (int reviewId in reviewIds)
            {
                try
                {
                    await DatabaseHelper.ExecuteUserAsync(
                        "ResolveDuplicateStudentReview",
                        DatabaseHelper.Int("@ReviewId", reviewId),
                        DatabaseHelper.P("@Status", status));
                    updated++;
                }
                catch (Exception ex)
                {
                    failures.Add(DbMessage.UserMessage(ex)
                            .Replace("\r", " ")
                            .Replace("\n", " ")
                            .Trim());
                }
            }
            await LoadAsync();
            if (failures.Count == 0)
            {
                Info($"{updated:N0} of {reviewIds.Count:N0} candidate pair(s) marked {verb}.");
                return;
            }
            var summary = new List<string> {
                $"Selected: {reviewIds.Count:N0} candidate pair(s).",
                $"Marked {verb}: {updated:N0} of {reviewIds.Count:N0}.",
                $"FAILED to mark {verb}: {failures.Count:N0}."
            };
            foreach (var group in failures
                .GroupBy(reason => reason, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()))
            {
                summary.Add($"   - {group.Count():N0} pair(s): {group.Key}");
            }
            summary.Add(
                $"Any of the selected pair(s) still shown as Open in the list below were NOT " +
                $"marked {verb} - retry those once the cause above is cleared.");
            Warn(string.Join("\n", summary));
        }
        catch (Exception ex) { Fail(ex); }
    }
}
