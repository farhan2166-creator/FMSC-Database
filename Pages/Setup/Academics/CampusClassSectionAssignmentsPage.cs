using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Setup.Academics;
public sealed class CampusClassSectionAssignmentsPage : V2Page, IUnsavedChanges
{
    private readonly LookupBox campus = new();
    private readonly DataGridView matrix = new() {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        MultiSelect = true,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
        EditMode = DataGridViewEditMode.EditProgrammatically,
        BackgroundColor = AppTheme.Surface,
        BorderStyle = BorderStyle.None
    };
    private readonly List<ClassInfo> classes = new();
    private readonly List<SectionInfo> sections = new();
    /* The matrix holds UNAPPLIED ticks, and `LoadCampusMatrix` clears the grid and refills it from the database, so changing the campus threw them away without a word. */
    private string? appliedMatrixSignature;
    private int loadedCampusId;
    /* Set while the campus picker is being put back after a declined change, so the restore cannot re-enter the change handler. */
    private bool suppressCampusChanged;
    private bool updatingMatrix;
    private bool matrixReady;
    private readonly Dictionary<(int ClassId, int SectionId), bool> savedAssignments = new();
    private readonly Label assignmentSummary = new() {
        Dock = DockStyle.Fill, AutoSize = true, Font = AppTheme.NormalFont,
        ForeColor = AppTheme.MutedText, Padding = new Padding(4, 6, 4, 6)
    };
    private readonly List<ModernButton> matrixActions = new();
    private ModernButton applyButton = null!;
    private Point? assignmentAnchor;
    private Point? dragStart;
    private bool dragAssigned;
    private ModernButton resetButton = null!;
    public CampusClassSectionAssignmentsPage()
        : base(PageTitles.CampusAssignments, "Assign the available classes and sections to each campus in one operation")
    {
        applyButton =
            AddAction("Apply Changes", (_, _) => Save(), "Success");
        SetSaveShortcut(applyButton);
        resetButton = AddAction("Reset Changes", (_, _) => ResetChanges(), "Secondary");
        var selectAll =
            AddAction("Assign All", (_, _) => SelectAll(), "Secondary");
        var clearAll =
            AddAction("Clear All", (_, _) => ClearAll(), "Secondary");
        matrixActions.AddRange(new[] { selectAll, clearAll, resetButton });
        RelocateFromActions(applyButton, selectAll, clearAll, resetButton);
        BuildMatrix();
        campus.SelectedIndexChanged +=
            (_, _) => {
                if (suppressCampusChanged)
                    return;
                /* The campus change is what reloads the matrix, so this is where the unapplied ticks would be lost. */
                if (HasUnsavedChanges &&
                    !Confirm(
                        $"{UnsavedChangesMessage}{Environment.NewLine}{Environment.NewLine}" +
                        $"Changing the campus reloads the matrix from the database and discards them.{Environment.NewLine}{Environment.NewLine}" +
                        "Use Apply Changes first to keep them - the saved assignments are not changed either way.",
                        "Unsaved Assignment Changes"))
                {
                    RestoreLoadedCampusSelection();
                    return;
                }
                LoadCampusMatrix();
            };
        Defer(BindLookups);
        var contextCard =
            HumanUi.Card("Campus", "Select a campus to configure its class and section assignments.");
        var campusField =
            HumanUi.Field("Campus", campus, true);
        var campusLayout =
            new TableLayoutPanel {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 1,
                Height = AppMetrics.FieldRowHeight,
                Padding = new Padding(0)
            };
        campusLayout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        campusLayout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        campusLayout.Controls.Add(campusField, 0, 0);
        contextCard.Content.Controls.Add(campusLayout);
        var matrixCard =
            HumanUi.Card("Class & Section Assignments", "Click and drag to check or uncheck a range. Ctrl+click toggles separate cells; Shift+click extends a range. Click a class name or section heading to toggle its group. Apply Changes saves your assignments.");
        var bulkTools = new FlowLayoutPanel {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
            Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 4)
        };
        foreach (var button in new[] { applyButton }.Concat(matrixActions))
        {
            button.AutoSize = true;
            button.Margin = new Padding(0, 2, 8, 2);
            bulkTools.Controls.Add(button);
        }
        var matrixLayout = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        matrixLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        matrixLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        matrixLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        matrixLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        matrixLayout.Controls.Add(bulkTools, 0, 0);
        matrixLayout.Controls.Add(assignmentSummary, 0, 1);
        matrixLayout.Controls.Add(matrix, 0, 2);
        matrixCard.Content.Controls.Add(matrixLayout);
        var root =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(1)));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(contextCard, 0, 0);
        root.Controls.Add(matrixCard, 0, 1);
        SetContent(root);
        UpdateAssignmentSummary();
    }
    private void BuildMatrix()
    {
        matrix.Columns.Clear();
        matrix.AutoGenerateColumns = false;
        matrix.AllowUserToAddRows = false;
        matrix.AllowUserToDeleteRows = false;
        matrix.AllowUserToResizeRows = false;
        matrix.AllowUserToResizeColumns = true;
        matrix.RowHeadersVisible = false;
        matrix.MultiSelect = true;
        matrix.SelectionMode = DataGridViewSelectionMode.CellSelect;
        matrix.EditMode = DataGridViewEditMode.EditProgrammatically;
        matrix.BorderStyle = BorderStyle.None;
        matrix.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        matrix.BackgroundColor = AppTheme.Surface;
        matrix.GridColor = AppTheme.Border;
        matrix.EnableHeadersVisualStyles = false;
        matrix.ColumnHeadersHeight =
            AppMetrics.GridHeaderHeight;
        matrix.ColumnHeadersDefaultCellStyle =
            new DataGridViewCellStyle {
                BackColor = AppTheme.ModuleAccent("Setup"),
                ForeColor = Color.White,
                Font = AppTheme.ButtonFont,
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                Padding = new Padding(8, 0, 8, 0),
                SelectionBackColor = AppTheme.ModuleAccent("Setup"),
                SelectionForeColor = Color.White
            };
        matrix.RowTemplate.Height = AppMetrics.GridRowHeight;
        matrix.DefaultCellStyle =
            new DataGridViewCellStyle {
                BackColor = AppTheme.Surface,
                ForeColor = AppTheme.Text,
                Font = AppTheme.NormalFont,
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                SelectionBackColor = AppTheme.PrimaryLight,
                SelectionForeColor = AppTheme.Text,
                Padding = new Padding(6, 0, 6, 0)
            };
        matrix.AlternatingRowsDefaultCellStyle =
            new DataGridViewCellStyle {
                BackColor = AppTheme.SurfaceSecondary,
                ForeColor = AppTheme.Text,
                Font = AppTheme.NormalFont,
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                SelectionBackColor = AppTheme.PrimaryLight,
                SelectionForeColor = AppTheme.Text,
                Padding = new Padding(6, 0, 6, 0)
            };
        var classColumn =
            new DataGridViewTextBoxColumn {
                Name = "Class",
                HeaderText = "CLASS",
                ReadOnly = true,
                Frozen = true,
                ToolTipText = "Click a class name to assign all its sections, or clear them if all are assigned.",
                Width = AppScale.Scale(220),
                MinimumWidth = AppScale.Scale(180),
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        classColumn.DefaultCellStyle =
            new DataGridViewCellStyle {
                BackColor = AppTheme.Surface,
                ForeColor = AppTheme.Text,
                Font = AppTheme.ButtonFont,
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 10, 0),
                SelectionBackColor = AppTheme.PrimaryLight,
                SelectionForeColor = AppTheme.Text
            };
        matrix.Columns.Add(classColumn);
        matrix.CellValueChanged += (_, _) => { if (!updatingMatrix) UpdateAssignmentSummary(); };
        matrix.SelectionChanged += (_, _) => { if (!updatingMatrix) UpdateAssignmentSummary(); };
        // Own the toggle so the painted checkbox and the rest of the cell share
        // one hit area. Read-only cells prevent a second native checkbox toggle.
        matrix.CellMouseDown += (_, e) => {
            if (!matrixReady || e.Button != MouseButtons.Left || e.Clicks > 1 ||
                e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (e.ColumnIndex == 0)
                ToggleAssignments(matrix.Rows[e.RowIndex].Cells.Cast<DataGridViewCell>().Where(c => c.ColumnIndex > 0));
            else
                BeginAssignmentGesture(e.RowIndex, e.ColumnIndex, ModifierKeys);
        };
        matrix.MouseMove += (_, e) => {
            if (e.Button != MouseButtons.Left || dragStart is null) return;
            var hit = matrix.HitTest(e.X, e.Y);
            ExtendAssignmentGesture(hit.RowIndex, hit.ColumnIndex);
        };
        matrix.MouseUp += (_, _) => dragStart = null;
        matrix.MouseCaptureChanged += (_, _) => { if (!matrix.Capture) dragStart = null; };
        matrix.ColumnHeaderMouseClick += (_, e) => {
            if (e.ColumnIndex > 0 && matrixReady && e.Button == MouseButtons.Left && e.Clicks <= 1)
                ToggleAssignments(matrix.Rows.Cast<DataGridViewRow>().Select(row => row.Cells[e.ColumnIndex]));
        };
        matrix.KeyDown += (_, e) => {
            if (e.KeyCode != Keys.Space || !matrixReady) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            ToggleAssignments(HighlightedCells());
        };
        matrix.CellPainting +=
            (_, e) => {
                if (e.RowIndex < 0 ||
                    e.ColumnIndex <= 0 ||
                    matrix.Columns[e.ColumnIndex]
                        is not DataGridViewCheckBoxColumn)
                {
                    return;
                }
                e.PaintBackground(e.CellBounds, true);
                bool selected =
                    Convert.ToBoolean(e.FormattedValue ?? false);
                const int size = 22;
                int x =
                    e.CellBounds.X +
                    (e.CellBounds.Width - size) / 2;
                int y =
                    e.CellBounds.Y +
                    (e.CellBounds.Height - size) / 2;
                var box =
                    new Rectangle(x, y, size, size);
                using var borderPen =
                    new Pen(
                        selected
                            ? AppTheme.Primary
                            : AppTheme.Border,
                        2);
                using var fillBrush =
                    new SolidBrush(
                        selected
                            ? AppTheme.Primary
                            : AppTheme.Surface);
                // DataGridView supplies Graphics on every CellPainting event; it is never null here.
                e.Graphics!.FillRectangle(fillBrush, box);
                e.Graphics.DrawRectangle(borderPen, box);
                if (selected)
                {
                    using var checkPen =
                        new Pen(
                            Color.White,
                            2.5f) {
                            StartCap =
                                System.Drawing.Drawing2D.LineCap.Round,
                            EndCap =
                                System.Drawing.Drawing2D.LineCap.Round,
                            LineJoin =
                                System.Drawing.Drawing2D.LineJoin.Round
                        };
                    e.Graphics.DrawLines(
                        checkPen,
                        new[]
                        {
                        new Point(
                            box.Left + 5,
                            box.Top + 11),
                        new Point(
                            box.Left + 9,
                            box.Top + 15),
                        new Point( box.Left + 17, box.Top + 6)
                        });
                }
                e.Handled = true;
            };
        matrix.DataError +=
            (_, e) => {
                e.ThrowException = false;
            };
    }
    private void BindLookups()
    {
        try
        {
            suppressCampusChanged = true;
            campus.Bind(
                DatabaseHelper.QueryUser("GetCampuses"), "CampusId", "CampusName");
            BindClasses();
            BindSections();
            suppressCampusChanged = false;
            LoadCampusMatrix();
            SetStatus("Select a campus to configure its class and section assignments.");
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            suppressCampusChanged = false;
        }
    }
    private void BindClasses()
    {
        classes.Clear();
        var dt =
            DatabaseHelper.QueryUser("GetClasses");
        foreach (DataRow row in dt.Rows)
        {
            classes.Add(
                new ClassInfo {
                    ClassId =
                        Convert.ToInt32(
                            row["ClassId"]),
                    ClassName =
                        Convert.ToString(
                            row["ClassName"]) ?? string.Empty
                });
        }
    }
    private void BindSections()
    {
        sections.Clear();
        /* All defined sections are available for placement. */
        var dt =
            DatabaseHelper.QueryUser(
                "GetSections");
        foreach (DataRow row in dt.Rows)
        {
            sections.Add(
                new SectionInfo {
                    SectionId =
                        Convert.ToInt32(
                            row["SectionId"]),
                    SectionName =
                        Convert.ToString(
                            row["SectionName"]) ?? string.Empty
                });
        }
    }
    private void BuildMatrixColumns()
    {
        while (matrix.Columns.Count > 1)
            matrix.Columns.RemoveAt(1);
        foreach (var section in sections)
        {
            var column =
                new DataGridViewCheckBoxColumn {
                    Name =
                        $"Section_{section.SectionId}",
                    HeaderText =
                        section.SectionName,
                    ReadOnly = true,
                    ToolTipText = "Click this heading to assign this section to every class, or clear it if all are assigned. Click anywhere in a cell to toggle one assignment.",
                    MinimumWidth = AppScale.Scale(80),
                    AutoSizeMode =
                        DataGridViewAutoSizeColumnMode.None,
                    FillWeight = 12,
                    SortMode =
                        DataGridViewColumnSortMode.NotSortable,
                    FlatStyle =
                        FlatStyle.Flat
                };
            column.DefaultCellStyle =
                new DataGridViewCellStyle {
                    Alignment =
                        DataGridViewContentAlignment.MiddleCenter,
                    Padding =
                        new Padding(4),
                    BackColor =
                        AppTheme.Surface,
                    SelectionBackColor =
                        AppTheme.PrimaryLight,
                    SelectionForeColor =
                        AppTheme.Text
                };
            matrix.Columns.Add(column);
        }
    }
    /// <summary>every tick in the matrix, in one comparable string: each row's class id followed by its section ticks, in column order.</summary>
    private string MatrixSignature()
    {
        var parts = new List<string>();
        foreach (DataGridViewRow row in matrix.Rows)
        {
            if (row.IsNewRow || row.Tag is null)
                continue;
            parts.Add(Convert.ToString(row.Tag) ?? string.Empty);
            for (int column = 1;
                 column < matrix.Columns.Count;
                 column++)
            {
                parts.Add(
                    Convert.ToBoolean(row.Cells[column].Value ?? false)
                        ? "1"
                        : "0");
            }
        }
        return string.Join("|", parts);
    }
    /// <summary>True when the ticks on screen differ from the ones the last load put there, i.e. changes the operator has not applied yet.</summary>
    public bool HasUnsavedChanges =>
        loadedCampusId > 0 &&
        appliedMatrixSignature is not null &&
        !string.Equals(appliedMatrixSignature, MatrixSignature(), StringComparison.Ordinal);
    public string UnsavedChangesMessage =>
        $"The class/section ticks for \"{campus.Text}\" have not been applied yet.";
    /// <summary>puts the campus picker back on the campus whose ticks are still on screen, WITHOUT reloading the matrix that is already showing them. Reloading would be the very defect this guards against.</summary>
    private void RestoreLoadedCampusSelection()
    {
        if (loadedCampusId <= 0 ||
            campus.SelectedId == loadedCampusId)
        {
            return;
        }
        suppressCampusChanged = true;
        try
        {
            campus.SelectedValue = loadedCampusId;
        }
        finally
        {
            suppressCampusChanged = false;
        }
    }
    private void LoadCampusMatrix()
    {
        updatingMatrix = true;
        matrixReady = false;
        assignmentAnchor = null;
        dragStart = null;
        savedAssignments.Clear();
        loadedCampusId = 0;
        appliedMatrixSignature = null;
        try
        {
            matrix.Rows.Clear();
            if (campus.SelectedId <= 0)
            {
                /* nothing is loaded, so there is nothing unapplied either. */
                loadedCampusId = 0;
                appliedMatrixSignature = null;
                SetStatus("Select a campus to configure its class and section assignments.");
                return;
            }
            var existing =
                DatabaseHelper.QueryUser(
                    "GetClassSections",
                    DatabaseHelper.Int(
                        "@CampusId",
                        campus.SelectedId),
                    DatabaseHelper.Int(
                        "@ClassId",
                        0),
                    DatabaseHelper.Int("@SectionId", 0));
            var activePlacements =
                new HashSet<(int ClassId, int SectionId)>();
            foreach (DataRow row in existing.Rows)
            {
                activePlacements.Add(
                    (
                        Convert.ToInt32(
                            row["ClassId"]),
                        Convert.ToInt32(row["SectionId"])
                    ));
            }
            BuildMatrixColumns();
            foreach (var cls in classes)
            {
                int rowIndex =
                    matrix.Rows.Add();
                var row =
                    matrix.Rows[rowIndex];
                row.Cells["Class"].Value =
                    cls.ClassName;
                row.Cells["Class"].Style = matrix.Columns["Class"].DefaultCellStyle.Clone();
                row.Tag =
                    cls.ClassId;
                for (int column = 1;
                     column < matrix.Columns.Count;
                     column++)
                {
                    int sectionId =
                        sections[column - 1].SectionId;
                    row.Cells[column].Value =
                        activePlacements.Contains(
                            (
                                cls.ClassId,
                                sectionId
                            ));
                    savedAssignments[(cls.ClassId, sectionId)] = Convert.ToBoolean(row.Cells[column].Value);
                }
            }
            SetStatus($"{matrix.Rows.Count:N0} class(es) loaded for {campus.Text}.");
            /* The ticks just loaded are the baseline "unapplied changes" is measured against. Captured here rather than in the change handler so that the reload after Apply Changes resets it too. */
            loadedCampusId =
                campus.SelectedId;
            appliedMatrixSignature =
                MatrixSignature();
            matrixReady = true;
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            updatingMatrix = false;
            matrix.Enabled = matrixReady;
            UpdateAssignmentSummary();
        }
    }
    private IEnumerable<DataGridViewCell> AssignmentCells() =>
        matrix.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow && row.Tag is not null)
            .SelectMany(row => row.Cells.Cast<DataGridViewCell>().Where(cell => cell.ColumnIndex > 0));

    private void BeginAssignmentGesture(int row, int column, Keys modifiers)
    {
        if (!matrixReady || row < 0 || column <= 0 ||
            row >= matrix.Rows.Count || column >= matrix.Columns.Count) return;
        var clicked = new Point(column, row);
        if ((modifiers & Keys.Shift) != Keys.None && assignmentAnchor is Point anchor)
        {
            dragStart = anchor;
            dragAssigned = Convert.ToBoolean(matrix.Rows[anchor.Y].Cells[anchor.X].Value ?? false);
        }
        else
        {
            assignmentAnchor = dragStart = clicked;
            dragAssigned = !Convert.ToBoolean(matrix.Rows[row].Cells[column].Value ?? false);
        }
        ExtendAssignmentGesture(row, column);
    }

    private void ExtendAssignmentGesture(int row, int column)
    {
        if (!matrixReady || dragStart is not Point start || row < 0 || column <= 0 ||
            row >= matrix.Rows.Count || column >= matrix.Columns.Count) return;
        // Write assignment values directly; highlighting is only visual feedback.
        SetAssignments(AssignmentCells().Where(cell =>
            cell.RowIndex >= Math.Min(start.Y, row) && cell.RowIndex <= Math.Max(start.Y, row) &&
            cell.ColumnIndex >= Math.Min(start.X, column) && cell.ColumnIndex <= Math.Max(start.X, column)), dragAssigned);
    }

    private DataGridViewCell[] HighlightedCells() => matrix.SelectedCells.Cast<DataGridViewCell>()
        .Where(cell => cell.RowIndex >= 0 && cell.ColumnIndex > 0).ToArray();

    private void ToggleAssignments(IEnumerable<DataGridViewCell> source)
    {
        matrix.EndEdit();
        var cells = source.ToArray();
        if (cells.Length == 0) return;
        SetAssignments(cells, !cells.All(cell => Convert.ToBoolean(cell.Value ?? false)));
    }

    private void SetAssignments(IEnumerable<DataGridViewCell> source, bool selected)
    {
        if (!matrixReady || campus.SelectedId != loadedCampusId) return;
        matrix.EndEdit();
        updatingMatrix = true;
        try
        {
            foreach (var cell in source)
                cell.Value = selected;
        }
        finally
        {
            updatingMatrix = false;
            UpdateAssignmentSummary();
        }
        SetStatus("Assignments updated on screen. Apply Changes to save, or Reset Changes to restore saved assignments.");
    }

    private void ResetChanges()
    {
        if (!matrixReady) return;
        matrix.EndEdit();
        updatingMatrix = true;
        try
        {
            foreach (var cell in AssignmentCells())
                cell.Value = savedAssignments[(Convert.ToInt32(cell.OwningRow.Tag), sections[cell.ColumnIndex - 1].SectionId)];
        }
        finally
        {
            updatingMatrix = false;
            UpdateAssignmentSummary();
        }
        SetStatus("Saved assignments restored. No changes to apply.");
    }

    private void UpdateAssignmentSummary()
    {
        int assigned = 0, additions = 0, removals = 0;
        if (matrixReady)
        {
            foreach (var cell in AssignmentCells())
            {
                bool selected = Convert.ToBoolean(cell.Value ?? false);
                bool saved = savedAssignments[(Convert.ToInt32(cell.OwningRow.Tag), sections[cell.ColumnIndex - 1].SectionId)];
                if (selected) assigned++;
                if (selected && !saved) additions++;
                if (!selected && saved) removals++;
            }
        }
        int highlighted = matrixReady ? HighlightedCells().Length : 0;
        assignmentSummary.Text = matrixReady
            ? $"{assigned:N0} assigned combinations  ·  {highlighted:N0} highlighted  ·  Pending: {additions:N0} additions, {removals:N0} removals"
            : "Select a campus to load assignments.";
        foreach (var button in matrixActions) button.Enabled = matrixReady && matrix.Rows.Count > 0 && sections.Count > 0;
        applyButton.Enabled = resetButton.Enabled = matrixReady && additions + removals > 0;
    }
    private void SelectAll()
    {
        SetAssignments(AssignmentCells(), true);
    }
    private void ClearAll()
    {
        SetAssignments(AssignmentCells(), false);
    }
    private void Save()
    {
        matrix.EndEdit();
        if (!matrixReady || loadedCampusId != campus.SelectedId || !HasUnsavedChanges)
            return;
        if (!ValidateRequiredInputs())
            return;
        applyButton.Enabled = false;
        try
        {
            if (campus.SelectedId <= 0)
            {
                Warn("Select a campus first.");
                return;
            }
            var assignments = new DataTable();
            assignments.Columns.Add("ClassId", typeof(int));
            assignments.Columns.Add("SectionId", typeof(int));
            assignments.Columns.Add("IsSelected", typeof(bool));
            foreach (DataGridViewRow row in matrix.Rows)
            {
                if (row.IsNewRow ||
                    row.Tag is null)
                    continue;
                int classId =
                    Convert.ToInt32(row.Tag);
                for (int column = 1;
                     column < matrix.Columns.Count;
                     column++)
                {
                    int sectionId =
                        sections[column - 1].SectionId;
                    bool selected =
                        Convert.ToBoolean(row.Cells[column].Value ?? false);
                    assignments.Rows.Add(classId, sectionId, selected);
                }
            }
            DatabaseHelper.Execute(
                "SaveCampusClassSections",
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@CampusId",
                    campus.SelectedId),
                new SqlParameter(
                    "@Assignments",
                    System.Data.SqlDbType.Structured) {
                    TypeName =
                        "dbo.CampusClassSectionAssignmentType",
                    Value =
                        assignments
                });
            LoadCampusMatrix();
            if (matrixReady)
            {
                Info("Campus class and section assignments saved successfully.");
                matrix.Focus();
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            UpdateAssignmentSummary();
        }
    }
    private sealed class ClassInfo
    {
        public int ClassId { get; init; }
        public string ClassName { get; init; } = string.Empty;
    }
    private sealed class SectionInfo
    {
        public int SectionId { get; init; }
        public string SectionName { get; init; } = string.Empty;
    }
}
