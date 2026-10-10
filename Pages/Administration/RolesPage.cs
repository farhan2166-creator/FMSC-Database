using System.Text.Json;
using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Administration;
// BUSINESS RULE: Role administration is deliberately not exposed in the application navigation.
// Retain this page for controlled maintenance only; do not publish it without explicit business approval.
public sealed class RolesPage : V2Page
{
    readonly ModernTextBox search = HumanUi.TextBox(false, 100);
    string currentRoleName = "";
    string currentDescription = "";
    readonly Label selectedRole = new() {
        Dock = DockStyle.Fill,
        Font = AppTheme.HeadingFont,
        ForeColor = AppTheme.HeadingText,
        TextAlign = ContentAlignment.MiddleLeft,
        Text = "New Role"
    };
    readonly Label roleStatus = new() {
        Dock = DockStyle.Fill,
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.MutedText,
        TextAlign = ContentAlignment.MiddleLeft,
        Text = "Create a new security role."
    };
    readonly Label permissionSummary = new() {
        Dock = DockStyle.Fill,
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.MutedText,
        TextAlign = ContentAlignment.MiddleLeft,
        Text = "No permissions selected."
    };
    readonly Label administratorNotice = new() {
        Dock = DockStyle.Fill,
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.Text,
        BackColor = AppTheme.PrimaryLight,
        Padding = new Padding(12),
        TextAlign = ContentAlignment.MiddleLeft,
        Visible = false
    };
    readonly ModernButton newRoleButton =
        new() { Text = "+ New Role", Width = AppScale.Scale(135), ThemeRole = "Primary" };
    readonly ModernButton editRoleButton =
        new() { Text = "Edit Role", Width = AppScale.Scale(135), ThemeRole = "Primary" };
    readonly ModernButton saveButton =
        new() { Text = "Save Permissions", Width = AppScale.Scale(150), ThemeRole = "Success" };
    readonly ModernButton clearButton =
        new() { Text = "Deselect", Width = AppScale.Scale(110), ThemeRole = "Secondary" };
    readonly ModernButton selectAllButton =
        new() { Text = "Select All", Width = AppScale.Scale(105), ThemeRole = "Secondary" };
    readonly ModernButton clearAllButton =
        new() { Text = "Clear All", Width = AppScale.Scale(105), ThemeRole = "Secondary" };
    readonly Panel permissionHost = new() {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        BackColor = AppTheme.PageBackground
    };
    readonly Panel rolesHost = new() {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        BackColor = AppTheme.PageBackground
    };
    DataTable? rolesTable;
    DataTable? permissionsTable;
    readonly Dictionary<int, CheckBox> permissionChecks = new();
    readonly Dictionary<int, RoundedPanel> roleCards = new();
    readonly ToolTip permissionToolTip = new();
    int targetRoleId;
    bool isAdministratorRole;
    /* The role list and a role's permission matrix are what Save Permissions, Edit Role, Deselect and the bulk permission buttons act on, and both are read asynchronously. */
    int administrativeLoadDepth;
    bool administrativeLoadFailed;
    bool administrativeDataLoading;
    /* SavePermissionsAsync ends by awaiting SelectRoleAsync, which re-enables saveButton before the operation has finished, so the disabled button alone cannot refuse a click in that window. */
    bool savingPermissions;
    readonly bool canManageRoles =
        CurrentUser.HasPermission("System.Roles.Manage");
    public RolesPage()
        : base("Roles && Permissions", "Create security roles and control exactly which tasks each role can perform.")
    {
        newRoleButton.Enabled = canManageRoles;
        editRoleButton.Enabled = false;
        saveButton.Enabled = false;
        WorkspaceState.Remember(search, "Roles:Search");
        search.PlaceholderText = "Search roles...";
        Disposed += (_, _) => permissionToolTip.Dispose();
        newRoleButton.Click += async (_, _) =>
            await OpenNewRoleDialogAsync();
        editRoleButton.Click += async (_, _) =>
            await OpenEditRoleDialogAsync();
        /* Save Permissions writes the whole matrix through dbo.SaveRolePermissions, so a double click could post it twice. */
        saveButton.Click += async (_, _) => {
            if (savingPermissions || !saveButton.Enabled)
                return;
            savingPermissions = true;
            saveButton.Enabled = false;
            try
            {
                await SavePermissionsAsync();
            }
            finally
            {
                savingPermissions = false;
                /* The page's own rule, which also refuses to re-open the button over an in-flight load. */
                RefreshRoleActionEnabled();
            }
        };
        clearButton.Click += (_, _) =>
            DeselectRole();
        selectAllButton.Click += (_, _) =>
            SetAllPermissions(true);
        clearAllButton.Click += (_, _) =>
            SetAllPermissions(false);
        search.TextChanged += (_, _) =>
            ApplyRoleFilter();
        BuildUi();
        Defer(async () => {
            BeginAdministrativeLoad();
            bool loaded = false;
            try
            {
                await Task.WhenAll(
                    LoadPermissionsAsync(),
                    LoadRolesAsync());
                DeselectRole();
                loaded = true;
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
            finally
            {
                EndAdministrativeLoad(loaded);
            }
        });
    }
    void BuildUi()
    {
        var rolesPanel = BuildRolesPanel();
        var editorPanel = BuildEditorPanel();
        var workspace = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        workspace.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 38));
        workspace.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 62));
        workspace.Controls.Add(rolesPanel, 0, 0);
        workspace.Controls.Add(editorPanel, 1, 0);
        SetContent(workspace);
    }
    Control BuildRolesPanel()
    {
        var card = HumanUi.Card("Security Roles", "Select a role to configure its permissions.", accessory: newRoleButton);
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(2, 4, 2, 0)
        };
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(
            HumanUi.Field("Search", search), 0, 0);
        layout.Controls.Add(rolesHost, 0, 1);
        card.Content.Controls.Add(layout);
        return card;
    }
    Control BuildEditorPanel()
    {
        var card = HumanUi.Card("Role Configuration", "Select the tasks this role is allowed to perform.", accessory: editRoleButton);
        var header = BuildEditorHeader();
        var permissions = BuildPermissionSection();
        var buttons = HumanUi.ButtonRow(saveButton, clearButton);
        buttons.Dock = DockStyle.Fill;
        var editor = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        editor.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 44));
        editor.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
        editor.Controls.Add(header, 0, 0);
        editor.Controls.Add(permissions, 0, 1);
        editor.Controls.Add(buttons, 0, 2);
        card.Content.Controls.Add(editor);
        return card;
    }
    Control BuildEditorHeader()
    {
        var panel = new Panel {
            Dock = DockStyle.Fill,
            Padding = new Padding(4, 2, 4, 2)
        };
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 45));
        layout.Controls.Add(selectedRole, 0, 0);
        layout.Controls.Add(roleStatus, 0, 1);
        panel.Controls.Add(layout);
        return panel;
    }
    Control BuildPermissionSection()
    {
        var card = HumanUi.Card(
            "Permissions",
            "Permissions are grouped by functional area. A checked item grants that task to this role.");
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(2, 4, 2, 0)
        };
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        var toolbar = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1
        };
        toolbar.ColumnStyles.Add(
            new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(
            new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));
        toolbar.Controls.Add(selectAllButton, 0, 0);
        toolbar.Controls.Add(clearAllButton, 1, 0);
        toolbar.Controls.Add(permissionSummary, 2, 0);
        root.Controls.Add(administratorNotice, 0, 0);
        root.Controls.Add(toolbar, 0, 1);
        root.Controls.Add(permissionHost, 0, 2);
        card.Content.Controls.Add(root);
        return card;
    }
    /// <summary>close the page's administrative actions for the duration of a load.</summary>
    void BeginAdministrativeLoad()
    {
        administrativeLoadDepth++;
        administrativeDataLoading = true;
        newRoleButton.Enabled = false;
        clearButton.Enabled = false;
        RefreshRoleActionEnabled();
        SetPermissionControlsEnabled(false);
    }
    /// <summary>re-open the gate only after a fully successful load. A failed load leaves every dependent action disabled until a later successful load.</summary>
    void EndAdministrativeLoad(bool loaded)
    {
        if (administrativeLoadDepth > 0)
            administrativeLoadDepth--;
        if (!loaded)
            administrativeLoadFailed = true;
        if (administrativeLoadDepth > 0)
            return;
        administrativeDataLoading = false;
        if (administrativeLoadFailed)
        {
            administrativeLoadFailed = false;
            return;
        }
        ApplyAdministrativeEnablement();
    }
    /// <summary>The page's own enablement rules, applied once the data they depend on has arrived.</summary>
    void ApplyAdministrativeEnablement()
    {
        newRoleButton.Enabled = canManageRoles;
        clearButton.Enabled = true;
        RefreshRoleActionEnabled();
        SetPermissionControlsEnabled(true);
    }
    /// <summary>The single enablement rule for the actions that depend on the selected role.</summary>
    void RefreshRoleActionEnabled()
    {
        bool allowed =
            targetRoleId > 0 &&
            !isAdministratorRole &&
            canManageRoles &&
            !administrativeDataLoading;
        editRoleButton.Enabled = allowed;
        saveButton.Enabled = allowed;
        bool bulkAllowed =
            targetRoleId > 0 &&
            !isAdministratorRole &&
            !administrativeDataLoading;
        selectAllButton.Enabled = bulkAllowed;
        clearAllButton.Enabled = bulkAllowed;
    }
    /// <summary>The permission ticks are rewritten by the load, so they are held closed while it runs. Their own rule is the page's existing one - usable once the matrix has arrived.</summary>
    void SetPermissionControlsEnabled(bool enabled)
    {
        foreach (var check in permissionChecks.Values)
            check.Enabled = enabled;
    }
    async Task LoadPermissionsAsync()
    {
        permissionsTable =
            await DatabaseHelper.QueryUserAsync("GetPermissions");
        BuildPermissionControls();
    }
    void BuildPermissionControls()
    {
        permissionHost.Controls.Clear();
        permissionChecks.Clear();
        if (permissionsTable == null)
            return;
        var groups =
            permissionsTable.AsEnumerable()
                .Where(r =>
                    r["PermissionId"] != DBNull.Value)
                .GroupBy(r => {
                    var code =
                        Convert.ToString(
                            r["PermissionCode"]) ?? "";
                    var dot =
                        code.IndexOf('.');
                    return dot > 0
                        ? code[..dot]
                        : "Other";
                })
                .OrderBy(g => g.Key)
                .ToList();
        var outer = new FlowLayoutPanel {
            Dock = DockStyle.Top,
            FlowDirection =
                FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode =
                AutoSizeMode.GrowAndShrink,
            Padding = new Padding(
                4,
                4,
                12,
                12),
            Margin = Padding.Empty,
            BackColor =
                AppTheme.PageBackground
        };
        foreach (var group in groups)
        {
            outer.Controls.Add(
                BuildPermissionGroup(group.Key, group));
        }
        permissionHost.Controls.Add(outer);
    }
    Control BuildPermissionGroup(string groupName, IEnumerable<DataRow> rows)
    {
        var card = new RoundedPanel {
            Width = AppScale.Scale(340),
            AutoSize = true,
            AutoSizeMode =
                AutoSizeMode.GrowAndShrink,
            MinimumSize =
                new Size(AppScale.Scale(320), 0),
            BackColor =
                AppTheme.Surface,
            BorderColor =
                AppTheme.Border,
            ShowBorder = true,
            CornerRadius =
                AppTheme.ControlRadius,
            Padding =
                new Padding(14),
            Margin =
                new Padding(4, 4, 10, 10)
        };
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode =
                AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(
            new RowStyle(SizeType.AutoSize));
        var title = new Label {
            Text = groupName,
            Dock = DockStyle.Fill,
            Font =
                AppTheme.HeadingFont,
            ForeColor =
                AppTheme.HeadingText,
            TextAlign =
                ContentAlignment.MiddleLeft
        };
        root.Controls.Add(title, 0, 0);
        var permissionGrid =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode =
                    AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
        permissionGrid.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        permissionGrid.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        var orderedRows =
            rows.OrderBy(
                r =>
                    Convert.ToString(r["PermissionName"]))
            .ToList();
        int index = 0;
        foreach (var row in orderedRows)
        {
            var permissionId =
                Convert.ToInt32(row["PermissionId"]);
            var name =
                Convert.ToString(
                    row["PermissionName"]) ?? "";
            var code =
                Convert.ToString(
                    row["PermissionCode"]) ?? "";
            var description =
                Convert.ToString(
                    row["Description"]) ?? "";
            var check = new ModernCheckBox {
                Text = name,
                Tag = permissionId,
                AutoSize = true,
                Margin =
                    new Padding(4, 5, 8, 5)
            };
            check.CheckedChanged +=
                (_, _) =>
                    UpdatePermissionSummary();
            permissionChecks[
                permissionId] =
                check;
            permissionToolTip.SetToolTip(
                check,
                $"{code}\r\n{description}");
            permissionGrid.Controls.Add(check, index % 2, index / 2);
            index++;
        }
        root.Controls.Add(permissionGrid, 0, 1);
        card.Controls.Add(root);
        return card;
    }
    async Task LoadRolesAsync()
    {
        rolesTable =
            await DatabaseHelper.QueryUserAsync(
                "GetRoles");
        ApplyRoleFilter();
    }
    void ApplyRoleFilter()
    {
        roleCards.Clear();
        rolesHost.Controls.Clear();
        if (rolesTable == null)
            return;
        var text =
            search.Text.Trim();
        var filtered =
            rolesTable.AsEnumerable()
                .Where(row => {
                    var name =
                        Convert.ToString(
                            row["RoleName"]) ?? "";
                    var desc =
                        Convert.ToString(
                            row["Description"]) ?? "";
                    if (string.IsNullOrWhiteSpace(text))
                        return true;
                    return
                        name.Contains(text, StringComparison.OrdinalIgnoreCase)
                        ||
                        desc.Contains(text, StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(row =>
                    Convert.ToString(row["RoleName"]))
                .ToList();
        var grid = new TableLayoutPanel {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = Math.Max(
                1,
                (int)Math.Ceiling(
                    filtered.Count / 3.0)),
            AutoSize = true,
            AutoSizeMode =
                AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            Padding = new Padding(2, 4, 8, 8)
        };
        for (int column = 0;
             column < 3;
             column++)
        {
            grid.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 33.3333f));
        }
        for (int rowIndex = 0;
             rowIndex < grid.RowCount;
             rowIndex++)
        {
            grid.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 88));
        }
        for (int index = 0;
             index < filtered.Count;
             index++)
        {
            var row = filtered[index];
            var roleId =
                Convert.ToInt32(row["RoleId"]);
            var roleCard =
                BuildRoleCard(row);
            roleCards[roleId] =
                roleCard;
            grid.Controls.Add(roleCard, index % 3, index / 3);
        }
        rolesHost.Controls.Add(grid);
    }
    RoundedPanel BuildRoleCard(DataRow row)
    {
        var roleId =
            Convert.ToInt32(row["RoleId"]);
        var name =
            Convert.ToString(
                row["RoleName"]) ?? "";
        var descriptionText =
            Convert.ToString(
                row["Description"]) ?? "";
        var administrator =
            string.Equals(name, "Administrator", StringComparison.OrdinalIgnoreCase);
        var card = new RoundedPanel {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            BorderColor = administrator
                ? AppTheme.Primary
                : AppTheme.Border,
            ShowBorder = true,
            CornerRadius = AppTheme.ControlRadius,
            Padding = new Padding(9),
            Margin = new Padding(3, 3, 6, 6),
            Cursor = Cursors.Hand,
            Tag = roleId
        };
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 18));
        var title = new Label {
            Text = name,
            Dock = DockStyle.Fill,
            Font = AppTheme.HeadingFont,
            ForeColor = AppTheme.HeadingText,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        var desc = new Label {
            Text = string.IsNullOrWhiteSpace(descriptionText)
                ? "No description provided."
                : descriptionText,
            Dock = DockStyle.Fill,
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true
        };
        var status = new Label {
            Text = administrator
                ? "Protected • Administrator"
                : "Security role",
            Dock = DockStyle.Fill,
            Font = AppTheme.NormalFont,
            ForeColor = administrator
                ? AppTheme.Primary
                : AppTheme.MutedText,
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(desc, 0, 1);
        layout.Controls.Add(status, 0, 2);
        card.Controls.Add(layout);
        AttachRoleCardClick(card, roleId);
        AttachRoleCardClick(layout, roleId);
        AttachRoleCardClick(title, roleId);
        AttachRoleCardClick(desc, roleId);
        AttachRoleCardClick(status, roleId);
        return card;
    }
    void AttachRoleCardClick(Control control, int roleId)
    {
        control.Click += async (_, _) =>
            await SelectRoleAsync(roleId);
    }
    /// <summary>A role's permission matrix arrives asynchronously, and Save Permissions writes that matrix, so every role-dependent action is held closed for the whole load and re-opened only when it completes.</summary>
    async Task SelectRoleAsync(int roleId)
    {
        if (rolesTable == null)
            return;
        var row =
            rolesTable.AsEnumerable()
                .FirstOrDefault(r =>
                    r["RoleId"] != DBNull.Value &&
                    Convert.ToInt32(r["RoleId"]) == roleId);
        if (row == null)
            return;
        BeginAdministrativeLoad();
        bool loaded = false;
        try
        {
            await LoadRoleRowAsync(row);
            loaded = true;
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            EndAdministrativeLoad(loaded);
        }
        if (loaded)
            HighlightRoleCard(roleId);
    }
    async Task LoadRoleRowAsync(DataRow row)
    {
        /* The row's own id is captured: the load below awaits, and a later card click may move targetRoleId while this one is still running. */
        int roleId =
            Convert.ToInt32(row["RoleId"]);
        targetRoleId =
            roleId;
        var name =
            Convert.ToString(
                row["RoleName"]) ?? "";
        selectedRole.Text =
            name;
        currentRoleName =
            name;
        currentDescription =
            Convert.ToString(
                row["Description"]) ?? "";
        isAdministratorRole =
            string.Equals(name, "Administrator", StringComparison.OrdinalIgnoreCase);
        roleStatus.Text =
            isAdministratorRole
                ? "Protected system role — all permissions."
                : "Security role.";
        administratorNotice.Visible =
            isAdministratorRole;
        administratorNotice.Text =
            "Administrator is a protected system role. " +
            "Its access is managed automatically and cannot " +
            "be edited through this permission matrix.";
        /* The page's own rule, applied through one place so the load gate above can keep these closed until the matrix has arrived. */
        RefreshRoleActionEnabled();
        await LoadRolePermissionsAsync(roleId);
        /* A card clicked while this load was running owns the screen now: this role's header, highlight and status must not overwrite it. */
        if (roleId != targetRoleId)
            return;
        HighlightRoleCard(roleId);
        SetStatus($"Editing role: {name}");
    }
    void HighlightRoleCard(int roleId)
    {
        foreach (var pair in roleCards)
        {
            var card = pair.Value;
            var name =
                GetRoleName(pair.Key);
            var administrator =
                string.Equals(name, "Administrator", StringComparison.OrdinalIgnoreCase);
            card.BorderColor =
                pair.Key == roleId
                    ? AppTheme.Primary
                    : administrator
                        ? AppTheme.Primary
                        : AppTheme.Border;
            card.Invalidate();
        }
    }
    string GetRoleName(int roleId)
    {
        if (rolesTable == null)
            return "";
        var row =
            rolesTable.AsEnumerable()
                .FirstOrDefault(r =>
                    r["RoleId"] != DBNull.Value &&
                    Convert.ToInt32(r["RoleId"]) == roleId);
        return row == null
            ? ""
            : Convert.ToString(
                  row["RoleName"]) ?? "";
    }
    async Task LoadRolePermissionsAsync(int roleId)
    {
        ClearPermissionChecks();
        if (isAdministratorRole)
        {
            SetAllPermissions(true);
            UpdatePermissionSummary();
            return;
        }
        if (permissionsTable == null)
            return;
        var table =
            await DatabaseHelper.QueryUserAsync("GetRolesAndPermissions");
        /* Reloads the permission matrix from the selected role. */
        if (roleId != targetRoleId)
            return;
        var assigned =
            table.AsEnumerable()
                .Where(r =>
                    r["RoleId"] != DBNull.Value &&
                    Convert.ToInt32(
                        r["RoleId"]) == roleId &&
                    r["PermissionId"] != DBNull.Value)
                .Select(r =>
                    Convert.ToInt32(r["PermissionId"]))
                .ToHashSet();
        foreach (var pair in permissionChecks)
        {
            pair.Value.Checked =
                assigned.Contains(pair.Key);
        }
        UpdatePermissionSummary();
    }
    void ClearPermissionChecks()
    {
        foreach (var check in permissionChecks.Values)
            check.Checked = false;
    }
    void SetAllPermissions(bool value)
    {
        foreach (var check in permissionChecks.Values)
            check.Checked = value;
        UpdatePermissionSummary();
    }
    void UpdatePermissionSummary()
    {
        var selected =
            permissionChecks.Values
                .Count(x => x.Checked);
        var total =
            permissionChecks.Count;
        permissionSummary.Text =
            total == 0
                ? "No permissions available."
                : $"{selected} of {total} permissions selected.";
    }
    void DeselectRole()
    {
        targetRoleId = 0;
        isAdministratorRole = false;
        selectedRole.Text =
            "No role selected";
        roleStatus.Text =
            "Select a role on the left, or create a new one.";
        currentRoleName = "";
        currentDescription = "";
        administratorNotice.Visible = false;
        /* no role selected, so the page's own rule disables them - and the rule is not applied over an in-flight load. */
        RefreshRoleActionEnabled();
        ClearPermissionChecks();
        UpdatePermissionSummary();
        HighlightRoleCard(0);
        SetStatus("Ready.");
    }
    async Task OpenNewRoleDialogAsync()
    {
        /* It now re-opens with the name and description still filled in; Cancel leaves. */
        string prefillName = "";
        string prefillDescription = "";
        while (true)
        {
            using var dialog =
                new RoleDialog(isNewRole: true, prefillName, prefillDescription);
            if (dialog.ShowDialog(FindForm()) !=
                DialogResult.OK)
            {
                return;
            }
            bool saved =
                await SaveRoleFromDialogAsync(
                    dialog,
                    targetRoleIdOverride: 0,
                    permissionIdsOverride: new List<int>());
            if (saved || IsDisposed)
                return;
            prefillName = dialog.ResultRoleName;
            prefillDescription = dialog.ResultDescription;
        }
    }
    async Task OpenEditRoleDialogAsync()
    {
        if (targetRoleId <= 0 ||
            isAdministratorRole)
        {
            return;
        }
        string editName = currentRoleName;
        string editDescription = currentDescription;
        while (true)
        {
            using var dialog =
                new RoleDialog(isNewRole: false, editName, editDescription);
            if (dialog.ShowDialog(FindForm()) !=
                DialogResult.OK)
            {
                return;
            }
            bool saved = await SaveRoleFromDialogAsync(dialog, targetRoleIdOverride: targetRoleId, permissionIdsOverride: null);
            if (saved || IsDisposed)
                return;
            editName = dialog.ResultRoleName;
            editDescription = dialog.ResultDescription;
        }
    }
    async Task<bool> SaveRoleFromDialogAsync(RoleDialog dialog, int targetRoleIdOverride, List<int>? permissionIdsOverride)
    {
        try
        {
            var permissionIds =
                permissionIdsOverride ??
                permissionChecks
                    .Where(x => x.Value.Checked)
                    .Select(x => x.Key)
                    .ToList();
            var roleId =
                await SaveRoleDefinitionAsync(
                    dialog.ResultRoleName,
                    dialog.ResultDescription,
                    targetRoleIdOverride);
            var permissionJson =
                JsonSerializer.Serialize(permissionIds);
            await DatabaseHelper.ExecuteUserAsync(
                "SaveRolePermissions",
                DatabaseHelper.Int(
                    "@RoleId",
                    roleId),
                DatabaseHelper.P("@PermissionIdsJson", permissionJson));
            Info(
                dialog.IsNewRole
                    ? "Role created."
                    : "Role updated.");
            await LoadRolesAsync();
            await SelectRoleAsync(roleId);
            return true;
        }
        catch (Exception ex)
        {
            Fail(ex);
            return false;
        }
    }
    async Task SavePermissionsAsync()
    {
        try
        {
            if (targetRoleId <= 0 ||
                isAdministratorRole)
                return;
            var selectedPermissions =
                permissionChecks
                    .Where(x => x.Value.Checked)
                    .Select(x => x.Key)
                    .ToList();
            var permissionJson =
                JsonSerializer.Serialize(selectedPermissions);
            await DatabaseHelper.ExecuteUserAsync(
                "SaveRolePermissions",
                DatabaseHelper.Int(
                    "@RoleId",
                    targetRoleId),
                DatabaseHelper.P("@PermissionIdsJson", permissionJson));
            Info("Permissions saved.");
            await LoadRolesAsync();
            await SelectRoleAsync(targetRoleId);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    async Task<int> SaveRoleDefinitionAsync(string cleanName, string roleDescription, int targetRoleIdOverride)
    {
        var table =
            await DatabaseHelper.QueryUserAsync(
                "SaveRole",
                DatabaseHelper.P(
                    "@RoleName",
                    cleanName),
                DatabaseHelper.Int(
                    "@RoleId",
                    targetRoleIdOverride),
                DatabaseHelper.P(
                    "@Description",
                    roleDescription));
        if (table.Rows.Count == 0)
            throw new InvalidOperationException("The database did not return the saved role ID.");
        return Convert.ToInt32(table.Rows[0]["RoleId"]);
    }
}
