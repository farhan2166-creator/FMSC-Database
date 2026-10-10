using System.Globalization;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Core;

public partial class MasterPage
{
    private static bool IsOperatorHidden(FieldSpec field)
    {
        if (field.Kind == FieldKind.ComboString &&
            !string.IsNullOrWhiteSpace(field.LookupProcedure))
        {
            return false;
        }
        return field.Key.EndsWith(
            "Code",
            StringComparison.OrdinalIgnoreCase);
    }
    private static bool IsDisplayOrder(FieldSpec field) =>
        string.Equals(
            field.Key,
            "DisplayOrder",
            StringComparison.OrdinalIgnoreCase);
    private void OnEditorSizeChanged(object? sender, EventArgs e)
    {
        if (_editor is null)
            return;
        int width = _editor.ClientSize.Width;
        if (width <= 0)
            return;
        if (_editorBaselineWidth is null)
            _editorBaselineWidth = width;
        ReflowEditorIfNeeded(width);
    }
    private void ReflowEditorIfNeeded(int width)
    {
        if (_editor is null ||
            _editorBaselineWidth is null ||
            _editorFields.Count == 0)
        {
            return;
        }
        int minColumnWidth =
            AppScale.Scale(300, AppScale.ControlSizeScale);
        int columnsThatFit =
            Math.Max(1, width / minColumnWidth);
        int columns = Math.Min(
            _originalEditorColumns,
            columnsThatFit);
        if (columns == _editor.ColumnCount)
            return;
        RebuildEditorColumns(columns);
    }
    private void RebuildEditorColumns(int columns)
    {
        if (_editor is null)
            return;
        _editor.SuspendLayout();
        // The controls in _editorFields are the actual field controls that must be reused when the editor changes between one and two columns.
        _editor.Controls.Clear();
        _editor.ColumnStyles.Clear();
        _editor.RowStyles.Clear();
        _editor.RowCount = 0;
        _editor.ColumnCount = columns;
        for (int column = 0; column < columns; column++)
        {
            _editor.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100F / columns));
        }
        int fieldIndex = 0;
        while (fieldIndex < _editorFields.Count)
        {
            int row = _editor.RowCount++;
            var firstField = _editorFields[fieldIndex];
            var rowFields = firstField.Multiline
                ? new[] { firstField }
                : _editorFields
                    .Skip(fieldIndex)
                    .Take(columns)
                    .ToArray();
            fieldIndex += rowFields.Length;
            int rowHeight =
                rowFields.Any(field => field.Multiline)
                    ? AppMetrics.MultilineInputHeight(72) + UiDesignSystem.Spacing.Sm + (UiDesignSystem.Spacing.Xs * 2)
                    : AppMetrics.InputControlHeight + UiDesignSystem.Spacing.Sm + (UiDesignSystem.Spacing.Xs * 2);
            _editor.RowStyles.Add(
                new RowStyle(SizeType.Absolute, rowHeight));
            for (int column = 0;
                 column < rowFields.Length;
                 column++)
            {
                Control fieldControl = rowFields[column].Control;
                _editor.Controls.Add(fieldControl, column, row);
                bool isLastFieldInRow =
                    column == rowFields.Length - 1;
                bool rowIsShort =
                    rowFields.Length < columns;
                if (isLastFieldInRow && rowIsShort)
                {
                    _editor.SetColumnSpan(
                        fieldControl,
                        columns - column);
                }
            }
        }
        _editor.ResumeLayout(true);
    }
    private Control CreateField(FieldSpec field)
    {
        var panel = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(
                0,
                0,
                0,
                UiDesignSystem.Spacing.Sm),
            Padding = new Padding(0),
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            TabStop = false
        };
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                AppScale.Scale(
                    145,
                    AppScale.ControlSizeScale)));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                100F));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                field.ManageDialogFactory is null
                    ? 0
                    : AppMetrics.ControlHeight));
        var caption = new Label {
            Text = field.Required
                ? $"{field.Caption} *"
                : field.Caption,
            Dock = DockStyle.Fill,
            Font = AppTheme.FieldCaptionFont,
            ForeColor = AppTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(
                UiDesignSystem.Spacing.Xs,
                0,
                UiDesignSystem.Spacing.Md,
                0),
            AutoEllipsis = true,
            Margin = new Padding(0),
            TabStop = false,
            Tag = UiHelper.FieldCaptionTag
        };
        void FitCaption() => panel.ColumnStyles[0].Width = Math.Max(
            AppScale.Scale(145, AppScale.ControlSizeScale),
            TextRenderer.MeasureText(caption.Text, caption.Font).Width + caption.Padding.Horizontal);
        caption.FontChanged += (_, _) => FitCaption();
        caption.TextChanged += (_, _) => FitCaption();
        FitCaption();
        Control control = CreateControl(field);
        RecordTextPolicy.WireInput(control, "@" + field.Key);
        UiAccessibility.Describe(
            control,
            field.Caption,
            field.Required ? "Required" : null);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(
            0,
            UiDesignSystem.Spacing.Xs,
            0,
            UiDesignSystem.Spacing.Xs);
        control.TabStop = true;
        _controls[field.Key] = control;
        panel.Controls.Add(
            caption,
            0,
            0);
        if (field.ManageDialogFactory is null)
        {
            panel.Controls.Add(
                control,
                1,
                0);
            panel.SetColumnSpan(
                control,
                2);
            return panel;
        }
        var manageButton = new ModernButton {
            Text = "+",
            Dock = DockStyle.Fill,
            Width = AppMetrics.ControlHeight,
            Height = AppMetrics.ControlHeight,
            ThemeRole = "Primary",
            Margin = new Padding(
                UiDesignSystem.Spacing.Xs,
                UiDesignSystem.Spacing.Xs,
                0,
                UiDesignSystem.Spacing.Xs),
            TabStop = true
        };
        manageButton.Click += async (_, _) =>
            await ManageLookup(field);
        _manageButtons[field.Key] =
            manageButton;
        panel.Controls.Add(
            control,
            1,
            0);
        panel.Controls.Add(
            manageButton,
            2,
            0);
        return panel;
    }
    private async Task ManageLookup(FieldSpec field)
    {
        if (field.ManageDialogFactory is null)
            return;
        if (!_controls.TryGetValue(field.Key, out var childControl))
            return;
        try
        {
            using Form dialog = field.ManageDialogFactory();
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                return;
            var managed = dialog as IManagedLookupDialog;
            int selectedId = managed?.SelectedId ?? 0;
            bool hasParent =
                !string.IsNullOrWhiteSpace(field.LookupParentField) &&
                _controls.TryGetValue(field.LookupParentField!, out var parentControl) &&
                parentControl is ModernComboBox;
            if (hasParent)
            {
                var parentCombo =
                    (ModernComboBox)_controls[field.LookupParentField!];
                string? parentValue = managed?.SelectedParentValue;
                int parentIndex =
                    string.IsNullOrWhiteSpace(parentValue)
                        ? -1
                        : parentCombo.Items.IndexOf(parentValue!);
                if (parentIndex >= 0 && parentCombo.SelectedIndex != parentIndex)
                    parentCombo.SelectedIndex = parentIndex;
                else
                    await RefreshDependentLookupAsync(field, parentCombo);
            }
            else if (!string.IsNullOrWhiteSpace(field.LookupProcedure))
            {
                DataTable table = await DatabaseHelper.QueryUserAsync(field.LookupProcedure!);
                BindLookupControl(field, childControl, table);
            }
            if (selectedId > 0 && childControl is LookupBox lookupBox)
                lookupBox.SelectedValue = selectedId;
        }
        catch (Exception ex) { Fail(ex); }
    }
    /* Fallback text lengths for MasterPage-generated editors.: this table was a SECOND, independent set of limits that disagreed both with the UI's own literals and with the schema. */
    private static readonly IReadOnlyDictionary<string, int> F1TextLengths =
        new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase) {
            ["Username"] = SchemaLengths.Username,
            ["Password"] = SchemaLengths.Username,
            ["CampusCode"] = SchemaLengths.CampusCode,
            ["CampusName"] = SchemaLengths.CampusName,
            ["CampusType"] = SchemaLengths.CampusType,
            ["AddressLine"] = SchemaLengths.AddressLine,
            ["City"] = SchemaLengths.City,
            ["ContactNumber"] = SchemaLengths.ContactNumber,
            ["Email"] = SchemaLengths.Email,
            ["YearName"] = SchemaLengths.YearName,
            ["ClassCode"] = SchemaLengths.ClassCode,
            ["ClassName"] = SchemaLengths.ClassName,
            ["SectionCode"] = SchemaLengths.SectionCode,
            ["SectionName"] = SchemaLengths.SectionName,
            ["SubjectCode"] = SchemaLengths.SubjectCode,
            ["SubjectName"] = SchemaLengths.SubjectName,
            ["Description"] = SchemaLengths.SubjectDescription,
            ["FeeTypeCode"] = SchemaLengths.FeeTypeCode,
            ["FeeTypeName"] = SchemaLengths.FeeTypeName,
            // No matching schema column - feature not present in this schema.
            ["DocumentCode"] = 30,
            ["DocumentName"] = 100,
            // No matching schema column - feature not present in this schema.
            ["SchemeCode"] = 30,
            ["SchemeName"] = 100,
            // No matching schema column - feature not present in this schema.
            ["GradeCode"] = 20,
            ["GradeName"] = 100,
            // No matching schema column - feature not present in this schema.
            ["PeriodCode"] = 30,
            ["PeriodName"] = 100,
            ["StaffNumber"] = SchemaLengths.StaffNumber,
            ["StaffName"] = SchemaLengths.StaffName,
            /* Staff.FatherName is nvarchar(150) while Students.FatherName is nvarchar(500), and a bare column name cannot express both. */
            ["FatherName"] = SchemaLengths.StaffFatherName,
            ["Gender"] = SchemaLengths.Gender,
            ["CNIC"] = SchemaLengths.Cnic,
            ["AlternateContactNumber"] = SchemaLengths.AlternateContactNumber,
            ["Address"] = SchemaLengths.Address,
            ["Qualification"] = SchemaLengths.Qualification,
            ["Specialization"] = SchemaLengths.Specialization,
            ["Designation"] = SchemaLengths.Designation,
            ["Department"] = SchemaLengths.Department,
            ["EmploymentType"] = SchemaLengths.EmploymentType,
            ["RoleName"] = SchemaLengths.RoleName,
            /* No matching schema column. Students.FatherOccupation is 250 but that field lives on StudentAdmissionPage, not here. */
            ["Occupation"] = 250,
            // No matching schema column - feature not present in this schema.
            ["RoomCode"] = 30,
            ["RoomName"] = 100,
            ["Remarks"] = SchemaLengths.ListingRemarks
        };
    private static int EffectiveMaxLength(FieldSpec field)
    {
        if (field.MaxLength != 1000)
            return field.MaxLength;
        return F1TextLengths.TryGetValue(
            field.Key,
            out int length)
            ? length
            : 1000;
    }
    private Control CreateControl(FieldSpec field)
    {
        if (!string.IsNullOrWhiteSpace(field.LookupProcedure))
        {
            if (field.Kind == FieldKind.ComboString)
            {
                return new ModernComboBox {
                    DropDownStyle =
                        field.LookupAllowCustomValue
                            ? ComboBoxStyle.DropDown
                            : ComboBoxStyle.DropDownList,
                    Height = AppMetrics.InputControlHeight,
                    Dock = DockStyle.Fill,
                    Font = AppTheme.NormalFont,
                    TabStop = true
                };
            }
            return new LookupBox {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Height = AppMetrics.InputControlHeight,
                Dock = DockStyle.Fill,
                Font = AppTheme.NormalFont,
                TabStop = true
            };
        }
        switch (field.Kind)
        {
            case FieldKind.Integer:
                return new ModernNumericTextBox {
                    Minimum = -100000000,
                    Maximum = 100000000,
                    Dock = DockStyle.Fill,
                    Font = AppTheme.NormalFont,
                    ThousandsSeparator = false,
                    TabStop = true
                };
            case FieldKind.Decimal:
                return new ModernNumericTextBox {
                    Minimum = 0,
                    Maximum = 1000000000,
                    DecimalPlaces = 2,
                    Increment = 0.01M,
                    Dock = DockStyle.Fill,
                    Font = AppTheme.NormalFont,
                    ThousandsSeparator = true,
                    TabStop = true
                };
            case FieldKind.Date:
                return new ModernDatePicker {
                    Format = DateTimePickerFormat.Custom,
                    CustomFormat = "dd-MMM-yyyy",
                    Height = AppMetrics.InputControlHeight,
                    Dock = DockStyle.Fill,
                    Font = AppTheme.NormalFont,
                    ShowCheckBox = !field.Required,
                    Checked = field.Required,
                    TabStop = true
                };
            case FieldKind.Boolean:
                return new ModernCheckBox {
                    Text = field.Caption,
                    AutoSize = true,
                    Dock = DockStyle.Left,
                    Padding = new Padding(2, 4, 0, 0),
                    TabStop = true
                };
            case FieldKind.ComboInt:
            case FieldKind.ComboString:
                {
                    var combo = new ModernComboBox {
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        Height = AppMetrics.InputControlHeight,
                        Dock = DockStyle.Fill,
                        Font = AppTheme.NormalFont,
                        TabStop = true
                    };
                    if (field.Options is not null)
                    {
                        combo.Items.AddRange(
                            field.Options.Cast<object>().ToArray());
                    }
                    return combo;
                }
            case FieldKind.Time:
                return new ModernDatePicker {
                    TimeOnly = true,
                    Height = AppMetrics.InputControlHeight,
                    Dock = DockStyle.Fill,
                    Font = AppTheme.NormalFont,
                    TabStop = true
                };
            case FieldKind.Password:
                return new ModernTextBox {
                    Dock = DockStyle.Fill,
                    Height = AppMetrics.InputControlHeight,
                    MaxLength = EffectiveMaxLength(field),
                    UseSystemPasswordChar = true,
                    TabStop = true
                };
            default:
                return new ModernTextBox {
                    Dock = DockStyle.Fill,
                    Multiline = field.Multiline,
                    ScrollBars = field.Multiline
                        ? ScrollBars.Vertical
                        : ScrollBars.None,
                    Height = field.Multiline
                        ? AppMetrics.MultilineInputHeight(72)
                        : AppMetrics.InputControlHeight,
                    MaxLength = EffectiveMaxLength(field),
                    TabStop = true
                };
        }
    }
    private async Task BindLookupsAsync()
    {
        var loads = new List<(FieldSpec Field, Control Control, Task<DataTable> Task)>();
        /* every lookup control in the editor and the Save action are held disabled until the data above has really arrived. */
        var restore = new List<(Control Control, bool Enabled)>();
        var changedWhileLoading = new HashSet<Control>();
        EventHandler onEnabledChanged = (sender, _) => {
            if (sender is Control control)
                changedWhileLoading.Add(control);
        };
        foreach (var field in _fields)
        {
            if (string.IsNullOrWhiteSpace(field.LookupProcedure) ||
                !_controls.TryGetValue(field.Key, out var control))
                continue;
            restore.Add((control, control.Enabled));
            if (!string.IsNullOrWhiteSpace(field.LookupParentField))
            {
                WireDependentLookup(field);
                ClearLookup(field);
                continue;
            }
            var task = DatabaseHelper.QueryUserAsync(field.LookupProcedure!);
            loads.Add((field, control, task));
        }
        if (_saveButton is not null)
            restore.Add((_saveButton, _saveButton.Enabled));
        foreach (var (control, _) in restore)
        {
            control.Enabled = false;
            control.EnabledChanged += onEnabledChanged;
        }
        bool loaded = false;
        try
        {
            await Task.WhenAll(loads.Select(x => x.Task));
            foreach (var item in loads)
            {
                if (!IsDisposed)
                    BindLookupControl(item.Field, item.Control, await item.Task);
            }
            loaded = true;
        }
        catch (Exception ex)
        {
            if (!IsDisposed) Fail(ex);
        }
        finally
        {
            foreach (var (control, _) in restore)
                control.EnabledChanged -= onEnabledChanged;
            if (!IsDisposed)
            {
                foreach (var (control, wasEnabled) in restore)
                {
                    if (control.IsDisposed ||
                        changedWhileLoading.Contains(control))
                        continue;
                    control.Enabled = loaded && wasEnabled;
                }
            }
        }
    }
    private void BindLookupControl(
    FieldSpec field,
    Control control,
    DataTable table)
    {
        if (field.Kind == FieldKind.ComboString &&
            control is ModernComboBox stringCombo)
        {
            stringCombo.Items.Clear();
            if (!table.Columns.Contains(
                    field.LookupTextColumn))
            {
                throw new InvalidOperationException(
                    $"Lookup procedure '{field.LookupProcedure}' " +
                    $"does not return column " +
                    $"'{field.LookupTextColumn}'.");
            }
            var rows =
                table.Rows
                    .Cast<DataRow>()
                    .Where(row =>
                        row[field.LookupTextColumn] != DBNull.Value &&
                        !string.IsNullOrWhiteSpace(
                            Convert.ToString(
                                row[field.LookupTextColumn],
                                CultureInfo.InvariantCulture)))
                    .ToList();
            _lookupRows[field.Key] = rows;
            foreach (DataRow row in rows)
            {
                string text =
                    Convert.ToString(
                        row[field.LookupTextColumn],
                        CultureInfo.InvariantCulture)
                    ?? string.Empty;
                stringCombo.Items.Add(text);
            }
            stringCombo.SelectedIndex = -1;
            if (!field.LookupAllowCustomValue)
            {
                stringCombo.Text = string.Empty;
            }
            return;
        }
        if (control is LookupBox lookup)
        {
            lookup.Bind(
                table,
                field.LookupIdColumn,
                field.LookupTextColumn);
            return;
        }
        throw new InvalidOperationException(
            $"Field '{field.Key}' specifies lookup procedure " +
            $"'{field.LookupProcedure}' but its control type " +
            $"is not supported.");
    }
    private void WireDependentLookup(
    FieldSpec field)
    {
        if (string.IsNullOrWhiteSpace(
                field.LookupParentField))
        {
            return;
        }
        if (!_controls.TryGetValue(
                field.Key,
                out var childControl))
        {
            return;
        }
        if (!_controls.TryGetValue(
                field.LookupParentField,
                out var parentControl))
        {
            return;
        }
        if (parentControl is ModernComboBox parentCombo)
        {
            parentCombo.SelectedIndexChanged +=
                async (_, _) => await RefreshDependentLookupAsync(
                    field,
                    parentCombo);
        }
    }
    private async Task RefreshDependentLookupAsync(
    FieldSpec field,
    ModernComboBox parentCombo)
    {
        if (!_controls.TryGetValue(
                field.Key,
                out var childControl))
        {
            return;
        }
        int version = _lookupVersions.TryGetValue(field.Key, out var current)
            ? current + 1
            : 1;
        _lookupVersions[field.Key] = version;
        if (parentCombo.SelectedIndex < 0 ||
            parentCombo is LookupBox { SelectedId: <= 0 })
        {
            ClearLookup(field);
            return;
        }
        /* Never leave a child value visible while a different parent's list is loading. */
        ClearLookup(field);
        object? parentValue = null;
        if (parentCombo is LookupBox parentLookup)
        {
            parentValue = parentLookup.SelectedId;
        }
        else if (_lookupRows.TryGetValue(
                field.LookupParentField!,
                out var parentRows))
        {
            if (parentCombo.SelectedIndex >= parentRows.Count)
            {
                ClearLookup(field);
                return;
            }
            DataRow parentRow =
                parentRows[parentCombo.SelectedIndex];
            if (!parentRow.Table.Columns.Contains(
                    field.LookupParentIdColumn))
            {
                Fail(
                    new InvalidOperationException(
                        $"Lookup '{field.LookupParentField}' " +
                        $"does not contain '{field.LookupParentIdColumn}'."));
                return;
            }
            parentValue =
                parentRow[field.LookupParentIdColumn];
        }
        else
        {
            parentValue =
                parentCombo.SelectedItem;
        }
        if (parentValue is null ||
            parentValue == DBNull.Value)
        {
            ClearLookup(field);
            return;
        }
        string? textValue =
            Convert.ToString(
                parentValue,
                CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(textValue))
        {
            ClearLookup(field);
            return;
        }
        try
        {
            SqlParameter parameter;
            if (int.TryParse(
                    textValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int intValue))
            {
                parameter =
                    DatabaseHelper.Int(
                        field.LookupParentParameter,
                        intValue);
            }
            else
            {
                parameter =
                    DatabaseHelper.P(
                        field.LookupParentParameter,
                        textValue);
            }
            DataTable table = await DatabaseHelper.QueryUserAsync(
                field.LookupProcedure!,
                parameter);
            if (IsDisposed ||
                !_lookupVersions.TryGetValue(field.Key, out var latest) ||
                latest != version)
                return;
            BindLookupControl(field, childControl, table);
        }
        catch (Exception ex)
        {
            if (IsDisposed ||
                !_lookupVersions.TryGetValue(field.Key, out var latestVersion) ||
                latestVersion != version)
                return;
            ClearLookup(field);
            Fail(ex);
        }
    }
    private void ClearLookup(
    FieldSpec field)
    {
        if (!_controls.TryGetValue(
                field.Key,
                out var control))
        {
            return;
        }
        _lookupRows.Remove(field.Key);
        if (control is ModernComboBox combo)
        {
            combo.DataSource = null;
            combo.Items.Clear();
            combo.SelectedIndex = -1;
            combo.Text = string.Empty;
        }
        else if (control is LookupBox lookup)
        {
            lookup.DataSource = null;
            lookup.Items.Clear();
            lookup.SelectedIndex = -1;
            lookup.Text = string.Empty;
        }
    }
    private bool LoadRow(DataGridViewRow row)
    {
        try
        {
            object? id =
                FindRowId(row);
            _id =
                id is null
                    ? 0
                    : Convert.ToInt32(
                        id,
                        CultureInfo.InvariantCulture);
            if (_id <= 0)
                throw new InvalidOperationException("The selected record has no valid ID and cannot be edited.");
            foreach (var field in _fields)
            {
                if (!_controls.TryGetValue(
                        field.Key,
                        out var control))
                {
                    continue;
                }
                object? value =
                    row.Cells
                        .Cast<DataGridViewCell>()
                        .FirstOrDefault(cell =>
                            string.Equals(
                                cell.OwningColumn?.Name,
                                field.Key,
                                StringComparison.OrdinalIgnoreCase))
                        ?.Value;
                MasterFieldValueCodec.Write(control, value);
            }
            SetStatus(
                "Record loaded for editing.");
            /* say which record is in the editor and that this is an edit, so an opened record cannot be mistaken for a blank Add form. */
            string recordName =
                GetDeleteDisplayName(row).Trim();
            if (string.IsNullOrWhiteSpace(recordName))
            {
                _editorCard?.SetHeaderText(
                    "Edit Record",
                    "Loaded from the list.");
            }
            else
            {
                string entityLabel =
                    MasterEntityNaming.Singularize(NavigationTitle);
                _editorCard?.SetHeaderText(
                    $"Edit {entityLabel}",
                    $"{entityLabel}: {recordName}");
            }
            /* The freshly loaded record IS the baseline, so simply viewing a record never counts as an unsaved change. */
            CaptureEditorBaseline();
            if (_getProc == "GetStaff" && _staffRecordPicker is not null)
            {
                string TextFor(string name) => Convert.ToString(row.Cells.Cast<DataGridViewCell>()
                    .FirstOrDefault(cell => string.Equals(cell.OwningColumn?.Name, name, StringComparison.OrdinalIgnoreCase))?.Value) ?? string.Empty;
                _staffRecordPicker.SetStaff(_id, string.Join(" - ", new[] { TextFor("StaffNumber"), TextFor("StaffName") }.Where(value => !string.IsNullOrWhiteSpace(value))));
            }
            RecordLoaded(row);
            if (_entryRecordsTabs is not null) _entryRecordsTabs.SelectedIndex = 0;
            return true;
        }
        catch (Exception ex)
        {
            // A partially loaded record must never remain available for saving.
            Clear();
            CaptureEditorBaseline();
            Fail(ex);
            return false;
        }
    }
    private Dictionary<string, object?> Values()
    {
        var values =
            _controls.ToDictionary(
                pair => pair.Key,
                pair => MasterFieldValueCodec.Read(pair.Value),
                StringComparer.OrdinalIgnoreCase);
        foreach (var field in _fields)
        {
            if (string.IsNullOrWhiteSpace(
                    field.LookupProcedure))
            {
                continue;
            }
            if (!_controls.TryGetValue(
                    field.Key,
                    out var control))
            {
                continue;
            }
            if (control is not ModernComboBox combo)
            {
                continue;
            }
            if (!_lookupRows.TryGetValue(
                    field.Key,
                    out var rows))
            {
                continue;
            }
            if (combo.SelectedIndex < 0 ||
                combo.SelectedIndex >= rows.Count)
            {
                continue;
            }
            DataRow row =
                rows[combo.SelectedIndex];
            if (!row.Table.Columns.Contains(
                    field.LookupIdColumn))
            {
                continue;
            }
            object id =
                row[field.LookupIdColumn];
            if (id == DBNull.Value)
            {
                continue;
            }
            if (int.TryParse(
                    Convert.ToString(
                        id,
                        CultureInfo.InvariantCulture),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int lookupId))
            {
                values[field.Key + "Id"] =
                    lookupId;
            }
        }
        return values;
    }
    /* UX - the editor had no Enter key at all: typing a name and pressing Enter did nothing (with a system beep). */
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter && !_saving &&
            MasterEditorFocusNavigator.TryAdvance(FindForm()?.ActiveControl, _controls.Values, _saveButton))
            return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private bool AdvanceEditorOnEnter()
    {
        Control? active = FindForm()?.ActiveControl;
        if (active is null ||
            active is TextBoxBase { Multiline: true } ||
            active is ComboBox { DroppedDown: true } ||
            active is not (TextBoxBase or ComboBox or DateTimePicker))
        {
            return false;
        }
        var ordered = _controls.Values
            .Where(c => c.Visible && c.Enabled)
            .OrderBy(c => c.TabIndex)
            .ToList();
        int index = ordered.FindIndex(c => c == active || c.Contains(active));
        if (index < 0)
            return false;
        for (int i = index + 1; i < ordered.Count; i++)
        {
            if (FocusInside(ordered[i]))
                return true;
        }
        if (_saveButton is { Enabled: true, Visible: true })
        {
            _saveButton.PerformClick();
            return true;
        }
        return false;
    }
    private static bool FocusInside(Control control)
    {
        if (control.CanFocus && control.TabStop && control.Focus())
        {
            if (control is TextBoxBase { Multiline: false } text)
                text.SelectAll();
            return true;
        }
        foreach (Control child in control.Controls)
        {
            if (FocusInside(child))
                return true;
        }
        return false;
    }
    private async void Save(
        object? sender,
        EventArgs e)
    {
        /* This is an async void event handler with no protection at all, so clicking twice quickly. */
        if (!CanSaveRecord()) return;
        if (_saving)
            return;
        /* An existing record that was only loaded, not edited, has nothing to save.
           Return before validation or the save procedure so the record cannot be
           mistaken for a duplicate of itself. */
        if (!HasUnsavedChanges)
            return;
        if (!ValidateRequiredInputs())
            return;
        _saving = true;
        /* Holds the editor, actions, and grid while a save is in flight. */
        var busyControls =
            new List<(Control Control, bool WasEnabled)>();
        void TrackBusy(Control? control)
        {
            if (control is null ||
                control.IsDisposed)
            {
                return;
            }
            busyControls.Add((control, control.Enabled));
        }
        /* The control to return to is remembered here and given focus once the editor is enabled again (finally, below). */
        Control? focusAfterSave = FindForm()?.ActiveControl;
        bool focusNewRecord = false;
        TrackBusy(_saveButton);
        foreach (var editorControl in _controls.Values)
            TrackBusy(editorControl);
        TrackBusy(Grid);
        foreach (Control pageAction in Actions.Controls)
            TrackBusy(pageAction);
        foreach (var (control, _) in busyControls)
            control.Enabled = false;
        try
        {
            var values = Values();
            values["Id"] = _id;
            foreach (var hiddenField
                     in _fields.Where(IsOperatorHidden))
            {
                if (_id == 0)
                {
                    values[hiddenField.Key] =
                        GenerateInternalCode(
                            hiddenField);
                }
            }
            foreach (var field
                     in _fields.Where(x => x.Required))
            {
                object? value =
                    values[field.Key];
                if (value is null ||
                    value is string text &&
                    string.IsNullOrWhiteSpace(text))
                {
                    Warn(
                        $"{field.Caption} is required.");
                    focusAfterSave = _controls[field.Key];
                    return;
                }
            }
            if (values.TryGetValue("YearName", out var yearName) &&
                !RecordTextPolicy.IsValidYearName(Convert.ToString(yearName)?.Trim() ?? string.Empty))
            {
                Warn("Year Name must contain digits and an optional hyphen only, for example 2025-2026.");
                focusAfterSave = _controls["YearName"];
                return;
            }
            if (HasReorderConfiguration || _fields.Any(IsDisplayOrder))
            {
                // The database assigns new positions and preserves existing positions.
                values["DisplayOrder"] = 0;
            }
            SqlParameter[] parameters =
                _buildParams(values);
            _id = await SaveRecordAsync(parameters);
            AfterSave(
                _id,
                values);

            Info(
                "Record saved successfully.");
            Clear();
            /* what is on screen now matches the database, so the page is no longer dirty and leaving it must not prompt. */
            CaptureEditorBaseline();
            await ReloadAsync();
            /* UX - the editor is empty and ready for the next record: the cursor goes back to its first field instead of nowhere. */
            focusAfterSave = null;
            focusNewRecord = true;
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            _saving = false;
            /* Restored to the state each control was in before the save, not to a blanket true. */
            foreach (var (control, wasEnabled) in busyControls)
            {
                if (!control.IsDisposed)
                    control.Enabled = wasEnabled;
            }
            if (focusNewRecord)
                FocusFirstEditorField();
            else if (focusAfterSave is { IsDisposed: false } && focusAfterSave.CanFocus)
            {
                if (focusAfterSave.Focus() &&
                    focusAfterSave is TextBoxBase { Multiline: false } focusedText)
                {
                    focusedText.SelectAll();
                }
                else if (!focusAfterSave.Focused)
                {
                    MasterEditorFocusNavigator.FocusInside(focusAfterSave);
                }
            }
        }
    }
    protected virtual void AfterSave(
        int id,
        Dictionary<string, object?> values)
    {
    }
    private void Clear()
    {
        _id = 0;
        foreach (var field in _fields)
        {
            if (!_controls.TryGetValue(
                    field.Key,
                    out var control))
            {
                continue;
            }
            if (IsOperatorHidden(field))
            {
                if (control is TextBox hiddenCode)
                {
                    hiddenCode.Text =
                        GenerateInternalCode(field);
                }
                else if (control is ModernTextBox hiddenModernCode)
                {
                    hiddenModernCode.Text =
                        GenerateInternalCode(field);
                }
                continue;
            }
            switch (control)
            {
                case TextBox text:
                    text.Clear();
                    break;
                case ModernNumericTextBox number:
                    number.Value =
                        number.Minimum;
                    break;
                case ModernTextBox modernText:
                    modernText.Clear();
                    break;
                case DateTimePicker date:
                    date.Value =
                        DateTime.Today;
                    if (date.ShowCheckBox)
                    {
                        date.Checked = false;
                    }
                    break;
                case ModernDatePicker modernDate:
                    modernDate.Value =
                        DateTime.Today;
                    if (modernDate.ShowCheckBox)
                    {
                        modernDate.Checked = false;
                    }
                    break;
                case CheckBox check:
                    check.Checked = false;
                    break;
                case ComboBox combo:
                    combo.SelectedIndex = -1;
                    combo.Text = string.Empty;
                    break;
                case ModernComboBox modernCombo:
                    modernCombo.SelectedIndex = -1;
                    modernCombo.Text = string.Empty;
                    break;
            }
        }
        SetStatus(
            "Ready for a new record.");
        /* The editor is blank again, so the header must stop naming the record that was just saved, cleared or deleted. */
        _editorCard?.SetHeaderText(
            NewRecordHeaderTitle,
            NewRecordHeaderSubtitle);
        FocusFirstEditorField();
    }
    private string GenerateInternalCode(
        FieldSpec field)
    {
        string nameKey =
            field.Key[..^4] + "Name";
        string name =
            _controls.TryGetValue(
                nameKey,
                out var control)
                ? Convert.ToString(
                    MasterFieldValueCodec.Read(control),
                    CultureInfo.InvariantCulture)
                    ?.Trim() ?? string.Empty
                : string.Empty;
        string code =
            string.IsNullOrWhiteSpace(name)
                ? $"AUTO-{Guid.NewGuid():N}"
                : new string(
                    name
                        .Where(char.IsLetterOrDigit)
                        .ToArray())
                    .ToUpperInvariant();
        int maxLength =
            Math.Max(
                1,
                EffectiveMaxLength(field));
        return code.Length > maxLength
            ? code[..maxLength]
            : code;
    }
    private int GetNextDisplayOrder(
        Dictionary<string, object?> currentValues)
    {
        DataTable table = QueryCurrentTable();
        var rows =
            GetOrderedScopeRows(
                table,
                currentValues);
        return rows.Count + 1;
    }
    private int GetCurrentDisplayOrder(
        Dictionary<string, object?> currentValues)
    {
        DataTable table = QueryCurrentTable();
        if (!table.Columns.Contains("DisplayOrder"))
            return 0;
        foreach (DataRow row in table.Rows)
        {
            if (RowId(row) != _id ||
                !SameOrderScope(row, currentValues))
            {
                continue;
            }
            if (row["DisplayOrder"] != DBNull.Value &&
                int.TryParse(
                    Convert.ToString(
                        row["DisplayOrder"],
                        CultureInfo.InvariantCulture),
                    out int order))
            {
                return order;
            }
        }
        return GetNextDisplayOrder(currentValues);
    }
}
