using System.Security.Cryptography;
using System.Text.Json;
using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Administration;
// BUSINESS RULE: Account administration is deliberately not exposed in the application navigation.
// Retain this page for controlled maintenance only; do not publish it without explicit business approval.
public sealed class UsersPage : V2Page, IUnsavedChanges
{
    readonly ModernTextBox search =
        HumanUi.TextBox(false, 100);
    readonly Label userTitle = new() {
        Dock = DockStyle.Fill,
        Font = AppTheme.HeadingFont,
        ForeColor = AppTheme.HeadingText,
        TextAlign = ContentAlignment.MiddleLeft,
        Text = "No user selected"
    };
    readonly Label userStatus = new() {
        Dock = DockStyle.Fill,
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.MutedText,
        TextAlign = ContentAlignment.MiddleLeft,
        Text = "Select a user on the left, or create a new one."
    };
    readonly FlowLayoutPanel roleHost = new() {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Padding = new Padding(2),
        BackColor = AppTheme.PageBackground
    };
    readonly FlowLayoutPanel permissionHost = new() {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Padding = new Padding(2),
        BackColor = AppTheme.PageBackground
    };
    readonly Label roleSummary = new() {
        Dock = DockStyle.Fill,
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.MutedText,
        TextAlign = ContentAlignment.MiddleLeft,
        Text = "No roles selected — no access."
    };
    readonly Label permissionSummary = new() {
        Dock = DockStyle.Fill,
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.MutedText,
        TextAlign = ContentAlignment.MiddleLeft,
        Text = "No effective permissions."
    };
    readonly Label administratorNotice = new() {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.Text,
        BackColor = AppTheme.PrimaryLight,
        Padding = new Padding(12),
        TextAlign = ContentAlignment.MiddleLeft,
        Visible = false
    };
    readonly ModernButton newUserButton =
        new() { Text = "+ New User", Width = AppScale.Scale(130), ThemeRole = "Primary" };
    readonly ModernButton editAccountButton =
        new() { Text = "Edit Account", Width = AppScale.Scale(130), ThemeRole = "Primary" };
    /* assigned through AddBusyAction in the constructor, which is what makes the double-click guard real. The label ("Save Roles") and the "Primary" role are unchanged. */
    readonly ModernButton saveRolesButton;
    readonly ModernButton clearButton =
        new() { Text = "Deselect", Width = AppScale.Scale(110), ThemeRole = "Secondary" };
    readonly ModernButton selectAllRolesButton =
        new() { Text = "Select All", Width = AppScale.Scale(105), ThemeRole = "Secondary" };
    readonly ModernButton clearRolesButton =
        new() { Text = "Clear All", Width = AppScale.Scale(105), ThemeRole = "Secondary" };
    /* The pending role selection is not the saved role set, and the page must say so. Kept in a collapsed row so it costs no space while clean. */
    readonly Label roleChangeNotice = new() {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.Warning,
        BackColor = AppTheme.Tint(AppTheme.Warning, 0.90f),
        Padding = new Padding(10, 2, 10, 2),
        TextAlign = ContentAlignment.MiddleLeft,
        Visible = false
    };
    readonly RowStyle roleChangeRow =
        new(SizeType.Absolute, 0);
    readonly Dictionary<int, CheckBox> roleChecks = new();
    readonly Dictionary<int, string> savedRoleNames = new();
    /* dbo.GetUserPermissions is the source of the effective permissions the page already displays, and dbo.GetRolesAndPermissions is the existing role -> permission mapping RolesPage already uses. */
    DataTable? effectivePermissionTable;
    DataTable? rolePermissionsTable;
    int effectivePermissionUserId;
    /* Set while the role checks are being loaded from the saved roles. */
    int roleChangeUiSuppression;
    DataTable? usersTable;
    DataTable? rolesTable;
    int targetUserId;
    bool isAdministrator;
    /* The user list, the role list and each account's saved roles are what Save Roles, Edit Account, Deselect and the role bulk actions act on, and they arrive asynchronously. */
    int administrativeLoadDepth;
    bool administrativeLoadFailed;
    bool administrativeDataLoading;
    /* The two account-dialog openers are async and were ungated. */
    bool savingAccount;
    string currentUsername = "";
    bool currentMustChangePassword = true;
    readonly bool canViewUsers =
        CurrentUser.HasPermission("System.Users") ||
        CurrentUser.HasPermission("System.Audit");
    readonly bool canManageUsers =
        CurrentUser.HasPermission("System.Users.Manage");
    public UsersPage()
        : base(
            "Users",
            "Create accounts and assign the roles that grant their permissions.")
    {
        newUserButton.Enabled = canManageUsers && canViewUsers;
        editAccountButton.Enabled = false;
        /* (the busy-guard pattern): Save Roles performs a database write, so it is built through AddBusyAction. */
        saveRolesButton = AddBusyAction(
            "Save Roles",
            SaveRolesAsync,
            "Primary");
        saveRolesButton.Width = AppScale.Scale(130);
        RelocateFromActions(saveRolesButton);
        saveRolesButton.Enabled = false;
        WorkspaceState.Remember(search, "Users:Search");
        search.PlaceholderText = "Search users...";
        newUserButton.Click +=
            async (_, _) => await OpenNewUserDialog();
        editAccountButton.Click +=
            async (_, _) => await OpenEditAccountDialog();
        /* Deselect must not silently discard unsaved role selections. */
        clearButton.Click +=
            (_, _) => RequestDeselectUser();
        selectAllRolesButton.Click +=
            (_, _) => SetAllRoles(true);
        clearRolesButton.Click +=
            (_, _) => SetAllRoles(false);
        search.TextChanged +=
            (_, _) => ApplyUserFilter();
        BuildUi();
        Defer(() => _ = InitializeAsync());
    }
    void BuildUi()
    {
        var header = BuildHeader();
        var workspace = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1
        };
        if (canViewUsers)
        {
            workspace.ColumnCount = 2;
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            workspace.Controls.Add(BuildUsersPanel(), 0, 0);
            workspace.Controls.Add(BuildEditorPanel(), 1, 0);
        }
        else
        {
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            workspace.Controls.Add(
                new Label {
                    Text = "You do not have permission to view user accounts.",
                    Dock = DockStyle.Fill,
                    Font = AppTheme.NormalFont,
                    ForeColor = AppTheme.MutedText,
                    TextAlign = ContentAlignment.MiddleCenter
                },
                0,
                0);
        }
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                82));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));
        root.Controls.Add(
            header,
            0,
            0);
        root.Controls.Add(
            workspace,
            0,
            1);
        SetContent(root);
    }
    Control BuildHeader()
    {
        var panel = new Panel {
            Dock = DockStyle.Fill,
            Padding = new Padding(2, 0, 2, 10)
        };
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2
        };
        layout.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                70));
        layout.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                30));
        var titles = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        titles.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                58));
        titles.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                42));
        titles.Controls.Add(
            new Label {
                Text = "User Accounts",
                Dock = DockStyle.Fill,
                Font = AppTheme.PageTitleFont,
                ForeColor = AppTheme.HeadingText,
                TextAlign = ContentAlignment.BottomLeft
            },
            0,
            0);
        titles.Controls.Add(
            new Label {
                Text =
                    "Create accounts and assign roles to grant their permissions.",
                Dock = DockStyle.Fill,
                Font = AppTheme.NormalFont,
                ForeColor = AppTheme.MutedText,
                TextAlign = ContentAlignment.MiddleLeft
            },
            0,
            1);
        var action = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 20, 0, 0),
            ColumnCount = 1
        };
        action.Controls.Add(
            newUserButton,
            0,
            0);
        layout.Controls.Add(
            titles,
            0,
            0);
        layout.Controls.Add(
            action,
            1,
            0);
        panel.Controls.Add(layout);
        return panel;
    }
    Control BuildUsersPanel()
    {
        var card = HumanUi.Card(
            "User Accounts",
            "Select a user to assign roles.");
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(2, 4, 2, 0)
        };
        layout.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                AppMetrics.FieldRowHeight));
        layout.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));
        layout.Controls.Add(
            HumanUi.Field(
                "Search",
                search),
            0,
            0);
        Grid.Dock = DockStyle.Fill;
        Grid.ReadOnly = true;
        Grid.AllowUserToAddRows = false;
        Grid.AllowUserToDeleteRows = false;
        Grid.AllowUserToResizeRows = false;
        Grid.MultiSelect = false;
        Grid.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;
        Grid.AutoGenerateColumns = true;
        Grid.SelectionChanged +=
            (_, _) => _ = OnUserSelectionChangedAsync();
        layout.Controls.Add(
            Grid,
            0,
            1);
        card.Content.Controls.Add(layout);
        return card;
    }
    Control BuildEditorPanel()
    {
        var card = HumanUi.Card(
            "Role Assignment",
            "Assign one or more roles. Effective permissions are calculated from those roles.");
        var editorHeader = BuildEditorHeader();
        var roles = BuildRolesSection();
        var permissions = BuildPermissionsSection();
        var buttons = HumanUi.ButtonRow(
            saveRolesButton,
            clearButton);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                70));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                45));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                55));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                AppMetrics.ButtonRowHeight));
        root.Controls.Add(
            editorHeader,
            0,
            0);
        root.Controls.Add(
            roles,
            0,
            1);
        root.Controls.Add(
            permissions,
            0,
            2);
        root.Controls.Add(
            buttons,
            0,
            3);
        card.Content.Controls.Add(root);
        return card;
    }
    Control BuildEditorHeader()
    {
        var panel = new Panel {
            Dock = DockStyle.Fill,
            Padding = new Padding(4)
        };
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2
        };
        layout.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                65));
        layout.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                35));
        layout.Controls.Add(
            userTitle,
            0,
            0);
        layout.Controls.Add(
            userStatus,
            0,
            1);
        layout.Controls.Add(
            editAccountButton,
            1,
            0);
        layout.SetRowSpan(
            editAccountButton,
            2);
        panel.Controls.Add(layout);
        return panel;
    }
    Control BuildRolesSection()
    {
        var card = HumanUi.Card(
            "Assigned Roles",
            "Select the roles this user should receive.");
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(2, 4, 2, 0)
        };
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                40));
        /* collapsed until the selection differs from the saved set. */
        root.RowStyles.Add(roleChangeRow);
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                44));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                30));
        var toolbar = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 3
        };
        toolbar.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.AutoSize));
        toolbar.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.AutoSize));
        toolbar.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                100));
        toolbar.Controls.Add(
            selectAllRolesButton,
            0,
            0);
        toolbar.Controls.Add(
            clearRolesButton,
            1,
            0);
        toolbar.Controls.Add(
            roleSummary,
            2,
            0);
        root.Controls.Add(
            toolbar,
            0,
            0);
        root.Controls.Add(
            roleChangeNotice,
            0,
            1);
        root.Controls.Add(
            administratorNotice,
            0,
            2);
        root.Controls.Add(
            roleHost,
            0,
            3);
        root.Controls.Add(
            roleSummary,
            0,
            4);
        card.Content.Controls.Add(root);
        return card;
    }
    Control BuildPermissionsSection()
    {
        var card = HumanUi.Card(
            "Effective Permissions",
            "Read-only. These are the actual permissions produced by the user's assigned roles.");
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(2, 4, 2, 0)
        };
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                30));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));
        root.Controls.Add(
            permissionSummary,
            0,
            0);
        root.Controls.Add(
            permissionHost,
            0,
            1);
        card.Content.Controls.Add(root);
        return card;
    }
    async Task InitializeAsync()
    {
        BeginAdministrativeLoad();
        bool loaded = false;
        try
        {
            if (!canViewUsers)
            {
                loaded = true;
                return;
            }
            await Task.WhenAll(LoadRolesAsync(), LoadUsersAsync(), LoadRolePermissionMapAsync());
            DeselectUser();
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
    }
    /// <summary>close the page's administrative actions for the duration of a load.</summary>
    void BeginAdministrativeLoad()
    {
        administrativeLoadDepth++;
        administrativeDataLoading = true;
        newUserButton.Enabled = false;
        editAccountButton.Enabled = false;
        saveRolesButton.Enabled = false;
        clearButton.Enabled = false;
        SetRoleControlsEnabled(false);
    }
    /// <summary>re-open the gate only after a fully successful load. A failed load leaves every dependent action disabled until a later successful load, which is exactly what a failed read must do.</summary>
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
        newUserButton.Enabled = canManageUsers && canViewUsers;
        clearButton.Enabled = true;
        SetRoleControlsEnabled(targetUserId > 0 && !isAdministrator);
        RefreshEditAccountEnabled();
        RefreshSaveRolesEnabled();
    }
    async Task LoadUsersAsync()
    {
        var source =
            await DatabaseHelper.QueryUserAsync(
                "GetUsers",
                DatabaseHelper.Int(
                    "@UserId",
                    CurrentUser.UserId));
        usersTable =
            source.Clone();
        foreach (DataRow row in source.Rows)
        {
            var administrator =
                row["IsAdministrator"] != DBNull.Value &&
                Convert.ToBoolean(
                    row["IsAdministrator"]);
            if (!administrator)
                usersTable.ImportRow(row);
        }
        ApplyUserFilter();
    }
    void ApplyUserFilter()
    {
        if (usersTable == null)
            return;
        var filtered =
            usersTable.Clone();
        var text =
            search.Text.Trim();
        foreach (DataRow row in usersTable.Rows)
        {
            var name =
                Convert.ToString(
                    row["Username"]) ?? "";
            var roleNames =
                Convert.ToString(
                    row["RoleNames"]) ?? "";
            if (!string.IsNullOrWhiteSpace(text) &&
                !name.Contains(
                    text,
                    StringComparison.OrdinalIgnoreCase) &&
                !roleNames.Contains(
                    text,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            filtered.ImportRow(row);
        }
        bool previousSuppression = suppressUserSelection;
        suppressUserSelection = true;
        try
        {
            UiHelper.SetGridEmptyMessage(
                Grid,
                usersTable.Rows.Count == 0
                    ? "No user accounts are available."
                    : "No users match this search.");
            Bind(filtered);
            ConfigureGrid();
            if (targetUserId > 0)
                RestoreGridSelectionTo(targetUserId);
        }
        finally
        {
            suppressUserSelection = previousSuppression;
        }
    }
    void ConfigureGrid()
    {
        if (Grid.Columns.Contains("UserId"))
            Grid.Columns["UserId"].Visible = false;
        if (Grid.Columns.Contains("Username"))
        {
            Grid.Columns["Username"].HeaderText =
                "Username";
            Grid.Columns["Username"].Width = AppScale.Scale(130);
        }
        if (Grid.Columns.Contains("RoleNames"))
        {
            Grid.Columns["RoleNames"].HeaderText =
                "Assigned Roles";
            Grid.Columns["RoleNames"].AutoSizeMode =
                DataGridViewAutoSizeColumnMode.None;
        }
        if (Grid.Columns.Contains("MustChangePassword"))
            Grid.Columns["MustChangePassword"].Visible = false;
        if (Grid.Columns.Contains("IsAdministrator"))
            Grid.Columns["IsAdministrator"].Visible = false;
        if (Grid.Columns.Contains("CreatedAt"))
            Grid.Columns["CreatedAt"].Visible = false;
        if (Grid.Columns.Contains("UpdatedAt"))
            Grid.Columns["UpdatedAt"].Visible = false;
    }
    async Task LoadRolesAsync()
    {
        rolesTable =
            await DatabaseHelper.QueryUserAsync(
                "GetRoles");
        BuildRoleCards();
    }
    /// <summary>The role -> permission mapping the impact preview compares against.</summary>
    async Task LoadRolePermissionMapAsync()
    {
        try
        {
            rolePermissionsTable =
                await DatabaseHelper.QueryUserAsync(
                    "GetRolesAndPermissions");
        }
        catch (Exception nonFatalException28)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException28, "UsersPage: UsersPage fallback");
            /* Deliberately swallowed: this is a read-only preview, and the page must still load for an operator who may manage users but not roles. TryBuildPermissionImpact reports the gap in plain words. */
            rolePermissionsTable = null;
        }
    }
    void BuildRoleCards()
    {
        roleHost.Controls.Clear();
        roleChecks.Clear();
        if (rolesTable == null)
            return;
        foreach (DataRow row in
                 rolesTable.AsEnumerable()
                    .Where(r =>
                        !string.Equals(
                            Convert.ToString(r["RoleName"]),
                            "Administrator",
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(r =>
                        Convert.ToString(
                            r["RoleName"])))
        {
            var roleId =
                Convert.ToInt32(
                    row["RoleId"]);
            var name =
                Convert.ToString(
                    row["RoleName"]) ?? "";
            var description =
                Convert.ToString(
                    row["Description"]) ?? "";
            var check = new ModernCheckBox {
                Text = string.IsNullOrWhiteSpace(
                    description)
                    ? name
                    : $"{name}  —  {description}",
                Tag = roleId,
                AutoSize = true,
                Margin = new Padding(
                    8,
                    8,
                    8,
                    8)
            };
            check.CheckedChanged +=
                (_, _) =>
                    UpdateRoleSummary();
            roleChecks[roleId] = check;
            roleHost.Controls.Add(check);
        }
    }
    /// <summary>The account's saved roles and effective permissions are what Save Roles writes against, so the dependent actions are held closed for the whole load and re-opened only when it completes.</summary>
    async Task LoadSelectedUserAsync()
    {
        if (Grid.SelectedRows.Count == 0)
            return;
        BeginAdministrativeLoad();
        bool loaded = false;
        try
        {
            await LoadSelectedUserCoreAsync();
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
    }
    async Task LoadSelectedUserCoreAsync()
    {
        if (!Grid.Columns.Contains("UserId"))
            return;
        var value =
            Grid.SelectedRows[0]
                .Cells["UserId"]
                .Value;
        if (value == null ||
            value == DBNull.Value)
            return;
        targetUserId =
            Convert.ToInt32(value);
        currentUsername =
            Convert.ToString(
                Grid.SelectedRows[0]
                    .Cells["Username"]
                    .Value) ?? "";
        currentMustChangePassword =
            Convert.ToBoolean(
                Grid.SelectedRows[0]
                    .Cells["MustChangePassword"]
                    .Value);
        isAdministrator = false;
        administratorNotice.Visible = false;
        /* through the page's own rule, so this does not re-open Edit Account while the gate above is still closed. */
        RefreshEditAccountEnabled();
        SetRoleControlsEnabled(true);
        /* both loads rewrite the role checks and the permission list, so the unsaved indication and the impact preview are computed once, after both have settled, and never mid-load. */
        BeginRoleChangeSuppression();
        try
        {
            await Task.WhenAll(LoadAssignedRolesAsync(targetUserId), LoadEffectivePermissionsAsync(targetUserId));
        }
        finally
        {
            EndRoleChangeSuppression();
        }
        UpdateRoleSummary();
        userTitle.Text =
            currentUsername;
        userStatus.Text =
            "Existing user account — assign roles below.";
        SetStatus(
            $"Editing user: {currentUsername}");
    }
    async Task LoadAssignedRolesAsync(int userId)
    {
        /* ticking the checks from the saved roles fires CheckedChanged for every role. */
        BeginRoleChangeSuppression();
        try
        {
            await LoadAssignedRolesCoreAsync(userId);
        }
        finally
        {
            EndRoleChangeSuppression();
        }
        UpdateRoleSummary();
    }
    async Task LoadAssignedRolesCoreAsync(int userId)
    {
        ClearRoleChecks();
        savedRoleNames.Clear();
        var table =
            await DatabaseHelper.QueryUserAsync(
                "GetUserRoles",
                DatabaseHelper.Int(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@TargetUserId",
                    userId));
        /* Reloads role checks for the current user; superseded loads do not update the matrix. */
        if (userId != targetUserId)
            return;
        bool allRolesAssigned =
            table.Rows.Count > 0 &&
            table.AsEnumerable().All(
                r =>
                    Convert.ToBoolean(
                        r["IsAssigned"]));
        bool administrator =
            allRolesAssigned &&
            await IsAdministratorUserAsync(userId);
        /* The administrator probe is a second await: the selection can have moved again while it ran. */
        if (userId != targetUserId)
            return;
        isAdministrator =
            administrator;
        foreach (DataRow row in table.Rows)
        {
            if (!Convert.ToBoolean(
                    row["IsAssigned"]))
                continue;
            var roleId =
                Convert.ToInt32(
                    row["RoleId"]);
            if (roleChecks.TryGetValue(
                    roleId,
                    out var check))
            {
                check.Checked = true;
                savedRoleNames[roleId] =
                    table.Columns.Contains("RoleName")
                        ? Convert.ToString(
                            row["RoleName"]) ?? $"Role {roleId}"
                        : $"Role {roleId}";
            }
        }
        if (isAdministrator)
        {
            administratorNotice.Visible = true;
            administratorNotice.Text =
                "Administrator account: all roles and " +
                "permissions are granted automatically. " +
                "This protected account cannot be edited here.";
            SetRoleControlsEnabled(false);
            saveRolesButton.Enabled = false;
            editAccountButton.Enabled = false;
            userStatus.Text =
                "Protected Administrator account — ALL PERMISSIONS.";
        }
        else
        {
            RefreshSaveRolesEnabled();
        }
    }
    async Task<bool> IsAdministratorUserAsync(int userId)
    {
        var source =
            await DatabaseHelper.QueryUserAsync(
                "GetUsers",
                DatabaseHelper.Int(
                    "@UserId",
                    CurrentUser.UserId));
        foreach (DataRow row in source.Rows)
        {
            if (Convert.ToInt32(
                    row["UserId"]) != userId)
                continue;
            return row["IsAdministrator"] != DBNull.Value &&
                   Convert.ToBoolean(
                       row["IsAdministrator"]);
        }
        return false;
    }
    /// <summary>The saved effective permission set. dbo.GetUserPermissions is the exact source of the list this page already shows, so the impact preview compares against the same rows rather than a second, invented source.</summary>
    async Task LoadEffectivePermissionsAsync(int userId)
    {
        DataTable table;
        if (await IsAdministratorUserAsync(userId))
        {
            table = await DatabaseHelper.QueryUserAsync(
                "GetPermissions",
                DatabaseHelper.Int(
                    "@UserId",
                    CurrentUser.UserId));
        }
        else
        {
            table = await DatabaseHelper.QueryUserAsync(
                "GetUserPermissions",
                DatabaseHelper.Int(
                    "@UserId",
                    userId));
        }
        effectivePermissionTable = table;
        effectivePermissionUserId = userId;
        RenderEffectivePermissions();
    }
    /// <summary>One place that draws the Effective Permissions list from the rows already fetched, so a role checkbox can redraw it without touching the database.</summary>
    void RenderEffectivePermissions()
    {
        /* Mid-load there is no honest answer yet: the role ticks and the saved permission rows are still being applied. The caller re-renders once the load has settled. */
        if (RoleChangeUiSuppressed)
        {
            permissionHost.Controls.Clear();
            permissionSummary.Text =
                "Loading effective permissions.";
            return;
        }
        permissionHost.SuspendLayout();
        try
        {
            permissionHost.Controls.Clear();
            if (effectivePermissionTable is null ||
                effectivePermissionUserId != targetUserId)
            {
                permissionSummary.Text =
                    targetUserId > 0
                        ? "Loading effective permissions."
                        : "Select a user to see effective permissions.";
                return;
            }
            bool pending = HasUnsavedRoleChanges();
            if (pending)
            {
                permissionHost.Controls.Add(
                    BuildPermissionImpactPanel());
            }
            var table =
                effectivePermissionTable;
            if (table.Rows.Count == 0)
            {
                permissionSummary.Text =
                    pending
                        ? "No saved effective permissions."
                        : "No effective permissions.";
                return;
            }
            var groups =
                table.AsEnumerable()
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
            int count = 0;
            foreach (var group in groups)
            {
                permissionHost.Controls.Add(
                    BuildEffectivePermissionGroup(
                        group.Key,
                        group));
                count += group.Count();
            }
            permissionSummary.Text =
                pending
                    ? $"Saved roles currently grant {count} effective permission(s)."
                    : $"{count} effective permission(s).";
        }
        finally
        {
            permissionHost.ResumeLayout(true);
        }
    }
    Control BuildEffectivePermissionGroup(
        string groupName,
        IEnumerable<DataRow> rows)
    {
        var panel = new RoundedPanel {
            Width = Math.Max(
                350,
                permissionHost.ClientSize.Width - 30),
            AutoSize = true,
            AutoSizeMode =
                AutoSizeMode.GrowAndShrink,
            BackColor = AppTheme.Surface,
            BorderColor = AppTheme.Border,
            ShowBorder = true,
            CornerRadius = AppTheme.ControlRadius,
            Padding = new Padding(12),
            Margin = new Padding(
                0,
                0,
                0,
                8)
        };
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode =
                AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                28));
        layout.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));
        layout.Controls.Add(
            new Label {
                Text = groupName,
                Dock = DockStyle.Fill,
                Font = AppTheme.HeadingFont,
                ForeColor = AppTheme.HeadingText,
                TextAlign = ContentAlignment.MiddleLeft
            },
            0,
            0);
        var flow = new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode =
                AutoSizeMode.GrowAndShrink,
            FlowDirection =
                FlowDirection.LeftToRight,
            WrapContents = true
        };
        foreach (var row in
                 rows.OrderBy(r =>
                     Convert.ToString(
                         r["PermissionName"])))
        {
            var label = new Label {
                Text =
                    "✓ " +
                    (Convert.ToString(
                        row["PermissionName"]) ?? ""),
                AutoSize = true,
                Font = AppTheme.NormalFont,
                ForeColor = AppTheme.Success,
                Margin = new Padding(
                    6,
                    4,
                    18,
                    4)
            };
            flow.Controls.Add(label);
        }
        layout.Controls.Add(
            flow,
            0,
            1);
        panel.Controls.Add(layout);
        return panel;
    }
    static readonly string[] PermissionKeyColumns = {
        "PermissionCode",
        "PermissionId",
        "PermissionName"
    };
    /// <summary>The proposed permission impact, computed from the PENDING role selection.</summary>
    bool TryBuildPermissionImpact(
        out Dictionary<string, string> added,
        out Dictionary<string, string> removed,
        out List<string> rolesWithoutData,
        out string problem)
    {
        added = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        removed = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        rolesWithoutData = new List<string>();
        problem = "";
        if (rolePermissionsTable is null ||
            effectivePermissionTable is null)
        {
            problem =
                "the role-to-permission data could not be read for this account";
            return false;
        }
        Dictionary<int, Dictionary<string, string>>? byRole = null;
        Dictionary<string, string>? saved = null;
        foreach (var column in PermissionKeyColumns)
        {
            if (!effectivePermissionTable.Columns.Contains(column))
                continue;
            var roleMap =
                ExtractRolePermissions(
                    rolePermissionsTable,
                    column);
            if (roleMap.Count == 0)
                continue;
            var savedMap =
                ExtractPermissions(
                    effectivePermissionTable,
                    column);
            if (savedMap.Count == 0 &&
                effectivePermissionTable.Rows.Count > 0)
                continue;
            byRole = roleMap;
            saved = savedMap;
            break;
        }
        if (byRole is null ||
            saved is null)
        {
            problem =
                "the proposed roles could not be matched to the saved permission data";
            return false;
        }
        var proposed = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var roleId in SelectedRoleIds().OrderBy(x => x))
        {
            if (byRole.TryGetValue(
                    roleId,
                    out var map))
            {
                foreach (var pair in map)
                    proposed[pair.Key] = pair.Value;
                continue;
            }
            rolesWithoutData.Add(
                RoleDisplayName(
                    roleId,
                    $"Role {roleId}"));
        }
        foreach (var pair in proposed)
        {
            if (!saved.ContainsKey(pair.Key))
                added[pair.Key] = pair.Value;
        }
        foreach (var pair in saved)
        {
            if (!proposed.ContainsKey(pair.Key))
                removed[pair.Key] = pair.Value;
        }
        return true;
    }
    static Dictionary<string, string> ExtractPermissions(
        DataTable table,
        string keyColumn)
    {
        var result = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        if (!table.Columns.Contains(keyColumn))
            return result;
        foreach (DataRow row in table.Rows)
        {
            if (row[keyColumn] is null or DBNull)
                continue;
            var key =
                Convert.ToString(row[keyColumn])?.Trim() ?? "";
            if (key.Length == 0)
                continue;
            result[key] = PermissionLabel(row);
        }
        return result;
    }
    static Dictionary<int, Dictionary<string, string>> ExtractRolePermissions(
        DataTable table,
        string keyColumn)
    {
        var result =
            new Dictionary<int, Dictionary<string, string>>();
        if (!table.Columns.Contains("RoleId") ||
            !table.Columns.Contains(keyColumn))
            return result;
        foreach (DataRow row in table.Rows)
        {
            if (row["RoleId"] is null or DBNull ||
                row[keyColumn] is null or DBNull)
                continue;
            var key =
                Convert.ToString(row[keyColumn])?.Trim() ?? "";
            if (key.Length == 0)
                continue;
            int roleId =
                Convert.ToInt32(row["RoleId"]);
            if (!result.TryGetValue(
                    roleId,
                    out var map))
            {
                map = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                result[roleId] = map;
            }
            map[key] = PermissionLabel(row);
        }
        return result;
    }
    static string PermissionLabel(DataRow row)
    {
        var table =
            row.Table;
        if (table.Columns.Contains("PermissionName"))
        {
            var name =
                Convert.ToString(row["PermissionName"]);
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();
        }
        if (table.Columns.Contains("PermissionCode"))
        {
            var code =
                Convert.ToString(row["PermissionCode"]);
            if (!string.IsNullOrWhiteSpace(code))
                return code.Trim();
        }
        if (table.Columns.Contains("PermissionId") &&
            row["PermissionId"] != DBNull.Value)
        {
            return $"Permission #{Convert.ToInt32(row["PermissionId"])}";
        }
        return "";
    }
    /// <summary>The on-screen preview - which permissions the pending role selection would ADD and which it would REMOVE, with the exact permission names.</summary>
    Control BuildPermissionImpactPanel()
    {
        var panel = new RoundedPanel {
            Width = Math.Max(
                350,
                permissionHost.ClientSize.Width - 30),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = AppTheme.Tint(AppTheme.Warning, 0.92f),
            BorderColor = AppTheme.Warning,
            ShowBorder = true,
            CornerRadius = AppTheme.ControlRadius,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 8)
        };
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                28));
        layout.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));
        layout.Controls.Add(
            new Label {
                Text = "Proposed change - not saved yet",
                Dock = DockStyle.Fill,
                Font = AppTheme.HeadingFont,
                ForeColor = AppTheme.HeadingText,
                TextAlign = ContentAlignment.MiddleLeft
            },
            0,
            0);
        int wrapWidth =
            Math.Max(
                300,
                permissionHost.ClientSize.Width - 90);
        var lines = new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        void AddLine(
            string text,
            Color color)
        {
            lines.Controls.Add(
                new Label {
                    Text = text,
                    AutoSize = true,
                    MaximumSize = new Size(wrapWidth, 0),
                    Font = AppTheme.NormalFont,
                    ForeColor = color,
                    Margin = new Padding(0, 2, 0, 2)
                });
        }
        if (!TryBuildPermissionImpact(
                out var added,
                out var removed,
                out var rolesWithoutData,
                out var problem))
        {
            AddLine(
                "Permission impact preview is not available here: " +
                problem +
                ". The permissions a proposed role set would grant cannot be " +
                "read from the existing data, so no impact is shown.",
                AppTheme.Warning);
            AddLine(
                "The saved effective permissions below are unchanged. " +
                "Save Roles applies the selection and refreshes the list.",
                AppTheme.MutedText);
            layout.Controls.Add(lines, 0, 1);
            panel.Controls.Add(layout);
            return panel;
        }
        AddLine(
            Summarize(
                "Permissions to add",
                added),
            AppTheme.Success);
        AddLine(
            Summarize(
                "Permissions to remove",
                removed),
            AppTheme.Danger);
        if (added.Count == 0 &&
            removed.Count == 0)
        {
            AddLine(
                "The pending role selection does not change the effective permissions.",
                AppTheme.MutedText);
        }
        if (rolesWithoutData.Count > 0)
        {
            AddLine(
                "No permission rows were reported for: " +
                string.Join(", ", rolesWithoutData) +
                " (they may grant nothing, or their data may be missing).",
                AppTheme.MutedText);
        }
        AddLine(
            "Nothing is saved until you choose Save Roles.",
            AppTheme.MutedText);
        layout.Controls.Add(lines, 0, 1);
        panel.Controls.Add(layout);
        return panel;
    }
    /// <summary>exact permission names stay visible. The list is capped only so a very large grant does not fill the card.</summary>
    static string Summarize(
        string heading,
        Dictionary<string, string> permissions)
    {
        if (permissions.Count == 0)
            return $"{heading}: none.";
        var names =
            permissions.Values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .OrderBy(
                    x => x,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        if (names.Count == 0)
            return $"{heading}: none.";
        const int shown = 15;
        var text =
            names.Count > shown
                ? string.Join(", ", names.Take(shown)) +
                  $", and {names.Count - shown} more"
                : string.Join(", ", names);
        return $"{heading} ({names.Count}): {text}";
    }
    void UpdateRoleSummary()
    {
        // Business rule (V20 audit F-03): zero assigned roles intentionally means no
        // effective permissions for a normal account. Users.RoleId is a legacy primary
        // reference, not a fallback permission grant. Preserve the protected administrator.
        // See Docs/AUDIT_DECISIONS.md; do not require a role or silently assign one.
        var selected =
            roleChecks
                .Where(x => x.Value.Checked)
                .Select(x => x.Value.Text)
                .ToList();
        roleSummary.Text =
            selected.Count == 0
                ? "No roles selected — no access."
                : $"{selected.Count} role(s) selected.";
        if (RoleChangeUiSuppressed)
            return;
        /* say plainly that the ticks are a pending selection, not a save. */
        UpdateUnsavedRoleIndicator();
        /* recompute the proposed permission impact from the pending roles. Nothing here reads or writes the database. */
        if (targetUserId > 0 &&
            !isAdministrator)
        {
            RenderEffectivePermissions();
        }
    }
    HashSet<int> SelectedRoleIds() =>
        roleChecks
            .Where(x => x.Value.Checked)
            .Select(x => x.Key)
            .ToHashSet();
    HashSet<int> SavedRoleIds() =>
        savedRoleNames
            .Keys
            .ToHashSet();
    /// <summary>True when the ticks on screen differ from the roles the database currently holds. Used by the unsaved indication, the Deselect guard and the navigation guard.</summary>
    bool HasUnsavedRoleChanges()
    {
        if (targetUserId <= 0 ||
            isAdministrator)
            return false;
        return !SelectedRoleIds()
            .SetEquals(SavedRoleIds());
    }
    /// <summary>"Unsaved role changes" plus, when known, how many roles the save would add and remove.</summary>
    void UpdateUnsavedRoleIndicator()
    {
        if (!HasUnsavedRoleChanges())
        {
            HideRoleChangeNotice();
            return;
        }
        var selected = SelectedRoleIds();
        var saved = SavedRoleIds();
        int added = selected.Except(saved).Count();
        int removed = saved.Except(selected).Count();
        string text;
        if (selected.Count == 0)
        {
            text = removed == 1
                ? "No access after saving — unsaved change. 1 role will be removed."
                : $"No access after saving — unsaved change. {removed} roles will be removed.";
        }
        else
        {
            var parts = new List<string> {
                $"{selected.Count} role(s) selected"
            };
            if (added > 0)
                parts.Add($"{added} to add");
            if (removed > 0)
                parts.Add($"{removed} to remove");
            text =
                "Unsaved role changes: " +
                string.Join(", ", parts) +
                ". The saved roles are unchanged until Save Roles.";
        }
        roleChangeNotice.Text = text;
        roleChangeNotice.Visible = true;
        roleChangeRow.Height = AppScale.Scale(34);
        LayoutRoleNoticeRow();
    }
    /// <summary>A RowStyle height change is not by itself a relayout request, so the owning table is asked to lay out again and the indication appears or disappears immediately.</summary>
    void LayoutRoleNoticeRow()
    {
        if (roleChangeNotice.Parent is Control owner)
            owner.PerformLayout();
    }
    void HideRoleChangeNotice()
    {
        roleChangeNotice.Text = "";
        roleChangeNotice.Visible = false;
        roleChangeRow.Height = 0;
        LayoutRoleNoticeRow();
    }
    void BeginRoleChangeSuppression() =>
        roleChangeUiSuppression++;
    void EndRoleChangeSuppression()
    {
        if (roleChangeUiSuppression > 0)
            roleChangeUiSuppression--;
    }
    bool RoleChangeUiSuppressed =>
        roleChangeUiSuppression > 0;
    void SetAllRoles(bool value)
    {
        if (isAdministrator)
            return;
        /* Clear All and Select All change the PENDING selection only - they never write to the database. */
        BeginRoleChangeSuppression();
        try
        {
            foreach (var check in roleChecks.Values)
                check.Checked = value;
        }
        finally
        {
            EndRoleChangeSuppression();
        }
        UpdateRoleSummary();
    }
    void ClearRoleChecks()
    {
        foreach (var check in roleChecks.Values)
            check.Checked = false;
    }
    void SetRoleControlsEnabled(bool enabled)
    {
        /* The role ticks and their bulk buttons read the role list, so an enabling decision made while the gate is open is deferred. */
        bool effective = enabled && !administrativeDataLoading;
        foreach (var check in roleChecks.Values)
            check.Enabled = effective;
        selectAllRolesButton.Enabled =
            effective;
        clearRolesButton.Enabled =
            effective;
    }
    /// <summary>Clearing the selected user also clears the pending role ticks, so when those differ from the saved set the operator is asked first. Neither answer writes to the database.</summary>
    /* Set while the grid selection is changed programmatically, so the handler below does not re-ask and does not reload the user (which would clear the very ticks being protected). */
    private bool suppressUserSelection;
    /// <summary>selecting another account clears the pending role ticks, which this page itself announces as unsaved. Nothing is written either way.</summary>
    async Task OnUserSelectionChangedAsync()
    {
        if (suppressUserSelection)
            return;
        if (HasUnsavedRoleChanges())
        {
            var lines = new List<string> {
                $"{currentUsername} has unsaved role changes.",
                "",
                "No (Stay) keeps the selected user and the unsaved role selections.",
                "Yes (Discard) loads the user you just selected instead.",
                "",
                "The roles saved in the database are not changed either way."
            };
            if (!ModernMessageDialog.Confirm(
                    this,
                    string.Join(Environment.NewLine, lines),
                    "Unsaved Role Changes"))
            {
                RestoreGridSelectionTo(targetUserId);
                return;
            }
        }
        await LoadSelectedUserAsync();
    }
    /// <summary>puts the grid selection back on the loaded user WITHOUT reloading them.</summary>
    void RestoreGridSelectionTo(int userId)
    {
        bool previousSuppression = suppressUserSelection;
        suppressUserSelection = true;
        try
        {
            foreach (DataGridViewRow row in Grid.Rows)
            {
                if (row.Cells["UserId"].Value is null or DBNull)
                    continue;
                if (Convert.ToInt32(row.Cells["UserId"].Value) != userId)
                    continue;
                row.Selected = true;
                if (row.Cells.Count > 0)
                    Grid.CurrentCell = row.Cells.Cast<DataGridViewCell>().FirstOrDefault(cell => cell.Visible);
                break;
            }
        }
        finally
        {
            suppressUserSelection = previousSuppression;
        }
    }
    void RequestDeselectUser()
    {
        if (HasUnsavedRoleChanges())
        {
            var lines = new List<string> {
                $"{currentUsername} has unsaved role changes.",
                "",
                "No (Stay) keeps the selected user and the unsaved role selections.",
                "Yes (Discard) clears the selection instead.",
                "",
                "The roles saved in the database are not changed either way."
            };
            if (!ModernMessageDialog.Confirm(
                    this,
                    string.Join(Environment.NewLine, lines),
                    "Unsaved Role Changes"))
                return;
        }
        DeselectUser();
    }
    /// <summary>leaving the page with a pending role selection is the same kind of context loss as Deselect, so the dashboard's existing unsaved-changes guard now sees it too.</summary>
    public bool HasUnsavedChanges =>
        HasUnsavedRoleChanges();
    public string UnsavedChangesMessage =>
        "This user has unsaved role changes." +
        Environment.NewLine +
        Environment.NewLine +
        "If you leave now, the selected roles will not be saved. " +
        "The roles saved in the database are unchanged.";
    void DeselectUser()
    {
        /* The checks are about to be cleared, which fires CheckedChanged once per role. Suppressed so nothing is recomputed against a half-cleared editor, and the pending state is reset explicitly below. */
        BeginRoleChangeSuppression();
        try
        {
            targetUserId = 0;
            effectivePermissionUserId = 0;
            isAdministrator = false;
            Grid.ClearSelection();
            userTitle.Text =
                "No user selected";
            userStatus.Text =
                "Select a user on the left, or create a new one.";
            currentUsername = "";
            currentMustChangePassword = true;
            administratorNotice.Visible = false;
            editAccountButton.Enabled = false;
            saveRolesButton.Enabled = false;
            SetRoleControlsEnabled(false);
            ClearRoleChecks();
            savedRoleNames.Clear();
            effectivePermissionTable = null;
            roleSummary.Text =
                "No roles selected — no access.";
            HideRoleChangeNotice();
            permissionHost.Controls.Clear();
            permissionSummary.Text =
                "Select a user to see effective permissions.";
        }
        finally
        {
            EndRoleChangeSuppression();
        }
        SetStatus(
            "Ready.");
    }
    async Task OpenNewUserDialog()
    {
        /* (P5) - see savingAccount. */
        if (savingAccount)
            return;
        /* When the save fails the dialog now re-opens with the username and the force-change choice still filled in (the password is never carried over). */
        string prefillUsername = "";
        bool prefillMustChange = true;
        while (true)
        {
            using var dialog =
                new UserAccountDialog(
                    isNewUser: true,
                    existingUsername: prefillUsername,
                    existingMustChangePassword: prefillMustChange);
            if (dialog.ShowDialog(FindForm()) !=
                DialogResult.OK)
            {
                return;
            }
            if (savingAccount)
                return;
            var heldDuringSave = HoldAccountActions();
            bool saved;
            try
            {
                saved = await SaveAccountAsync(
                    dialog,
                    targetUserIdOverride: 0,
                    roleIdsOverride: new List<int>());
            }
            finally
            {
                ReleaseAccountActions(heldDuringSave);
            }
            if (saved || IsDisposed)
                return;
            prefillUsername = dialog.ResultUsername;
            prefillMustChange = dialog.ResultMustChangePassword;
        }
    }
    async Task OpenEditAccountDialog()
    {
        /* (P5) - see savingAccount. */
        if (savingAccount)
            return;
        if (targetUserId <= 0 ||
            isAdministrator)
        {
            return;
        }
        /* UX - same error recovery as New User: a refused save re-opens the dialog with the username the person had typed instead of discarding it. */
        string editUsername = currentUsername;
        bool editMustChange = currentMustChangePassword;
        while (true)
        {
            using var dialog =
                new UserAccountDialog(
                    isNewUser: false,
                    editUsername,
                    editMustChange);
            if (dialog.ShowDialog(FindForm()) !=
                DialogResult.OK)
            {
                return;
            }
            if (savingAccount)
                return;
            var heldDuringSave = HoldAccountActions();
            bool saved;
            try
            {
                saved = await SaveAccountAsync(
                    dialog,
                    targetUserIdOverride: targetUserId,
                    roleIdsOverride: null);
            }
            finally
            {
                ReleaseAccountActions(heldDuringSave);
            }
            if (saved || IsDisposed)
                return;
            editUsername = dialog.ResultUsername;
            editMustChange = dialog.ResultMustChangePassword;
        }
    }
    /// <summary>(P5) - the page owns both account actions until the write settles: savingAccount refuses a click that was already queued, and the buttons are held so the gate is visible.</summary>
    List<(Control Control, bool WasEnabled)> HoldAccountActions()
    {
        savingAccount = true;
        var held =
            new List<(Control Control, bool WasEnabled)>
            {
                (newUserButton, newUserButton.Enabled),
                (editAccountButton, editAccountButton.Enabled)
            };
        newUserButton.Enabled = false;
        editAccountButton.Enabled = false;
        return held;
    }
    void ReleaseAccountActions(
        List<(Control Control, bool WasEnabled)> held)
    {
        savingAccount = false;
        foreach (var (control, wasEnabled) in held)
        {
            if (!control.IsDisposed)
                control.Enabled = wasEnabled;
        }
        /* The page's own rules have the last word, including their "never while a load is in flight" clause - the same reason RefreshEditAccountEnabled exists for its button. */
        newUserButton.Enabled =
            canManageUsers &&
            canViewUsers &&
            !administrativeDataLoading;
        RefreshEditAccountEnabled();
    }
    async Task<bool> SaveAccountAsync(
        UserAccountDialog dialog,
        int targetUserIdOverride,
        List<int>? roleIdsOverride)
    {
        try
        {
            byte[]? hash = null;
            byte[]? salt = null;
            if (!string.IsNullOrWhiteSpace(
                    dialog.ResultPassword))
            {
                using var rng =
                    RandomNumberGenerator.Create();
                salt = new byte[32];
                rng.GetBytes(salt);
                hash =
                    Rfc2898DeriveBytes.Pbkdf2(
                        dialog.ResultPassword,
                        salt,
                        100_000,
                        HashAlgorithmName.SHA512,
                        64);
            }
            var roleIds =
                roleIdsOverride ??
                roleChecks
                    .Where(x => x.Value.Checked)
                    .Select(x => x.Key)
                    .Distinct()
                    .ToList();
            await CallSaveUserAsync(
                dialog.ResultUsername,
                dialog.ResultMustChangePassword,
                hash,
                salt,
                targetUserIdOverride,
                roleIds);
            Info(
                dialog.IsNewUser
                    ? "User account created."
                    : "User account updated.");
            return true;
        }
        catch (Exception ex)
        {
            Fail(ex);
            return false;
        }
    }
    async Task SaveRolesAsync()
    {
        try
        {
            if (targetUserId <= 0 ||
                isAdministrator)
            {
                return;
            }
            var roleIds =
                roleChecks
                    .Where(x => x.Value.Checked)
                    .Select(x => x.Key)
                    .Distinct()
                    .ToList();
            var removedRoleNames =
                savedRoleNames
                    .Where(x => !roleIds.Contains(x.Key))
                    .Select(x => RoleDisplayName(x.Key, x.Value))
                    .OrderBy(x => x)
                    .ToList();
            if (removedRoleNames.Count > 0)
            {
                var saveRoleLines = new List<string> {
                    $"Change permissions for {currentUsername}?",
                    "",
                    $"Roles to remove: {string.Join(", ", removedRoleNames)}",
                    "",
                    "This will remove the permissions granted by these roles."
                };
                if (!ModernMessageDialog.Confirm(
                        this,
                        string.Join(Environment.NewLine, saveRoleLines),
                        "Save Roles"))
                    return;
            }
            await CallSaveUserAsync(
                currentUsername,
                currentMustChangePassword,
                hash: null,
                salt: null,
                targetUserIdOverride: targetUserId,
                roleIds: roleIds);
            Info(
                "Roles saved.");
            /* AddBusyAction re-enables its button in a finally, which runs after this method returns and could contradict the page's own rule for the current user. */
            Defer(RefreshSaveRolesEnabled);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    /// <summary>The page's own rule for whether Save Roles may be used for what is currently selected.</summary>
    void RefreshSaveRolesEnabled()
    {
        saveRolesButton.Enabled =
            targetUserId > 0 &&
            canManageUsers &&
            !isAdministrator &&
            !administrativeDataLoading;
    }
    /// <summary>The page's own rule for Edit Account - a selected account the operator may manage and that is not the protected Administrator.</summary>
    void RefreshEditAccountEnabled()
    {
        editAccountButton.Enabled =
            targetUserId > 0 &&
            canManageUsers &&
            !isAdministrator &&
            !administrativeDataLoading;
    }
    string RoleDisplayName(
        int roleId,
        string fallbackName)
    {
        if (roleChecks.TryGetValue(
                roleId,
                out var check))
        {
            var text =
                check.Text ?? "";
            int separator =
                text.IndexOf(
                    "  ",
                    StringComparison.Ordinal);
            return separator > 0
                ? text.Substring(0, separator).Trim()
                : text.Trim();
        }
        return fallbackName;
    }
    async Task CallSaveUserAsync(
        string username,
        bool mustChangePassword,
        byte[]? hash,
        byte[]? salt,
        int targetUserIdOverride,
        List<int> roleIds)
    {
        var cleanUsername =
            username.Trim();
        if (string.IsNullOrWhiteSpace(
                cleanUsername))
        {
            ModernMessageDialog.Show(
                this,
                "Username is required.",
                "Validation",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        // An empty JSON array is intentional: SaveUser removes all effective role
        // memberships. Do not replace [] with a primary/default role (V20 F-03).
        var roleIdsJson =
            JsonSerializer.Serialize(
                roleIds);
        var result =
            await DatabaseHelper.ScalarUserAsync(
                "SaveUser",
                DatabaseHelper.P(
                    "@Username",
                    cleanUsername),
                DatabaseHelper.Int(
                    "@RoleId",
                    0),
                DatabaseHelper.Int(
                    "@TargetUserId",
                    targetUserIdOverride),
                DatabaseHelper.Bit(
                    "@MustChangePassword",
                    mustChangePassword),
                new SqlParameter(
                    "@PasswordHash",
                    hash ??
                    (object)DBNull.Value),
                new SqlParameter(
                    "@PasswordSalt",
                    salt ??
                    (object)DBNull.Value),
                DatabaseHelper.P(
                    "@RoleIdsJson",
                    roleIdsJson));
        var savedUserId =
            result != null &&
            result != DBNull.Value
                ? Convert.ToInt32(result)
                : targetUserIdOverride;
        await LoadUsersAsync();
        SelectUser(savedUserId);
    }
    void SelectUser(int userId)
    {
        foreach (DataGridViewRow row in Grid.Rows)
        {
            if (row.Cells["UserId"].Value is null or DBNull)
                continue;
            if (Convert.ToInt32(
                    row.Cells["UserId"].Value) == userId)
            {
                row.Selected = true;
                if (row.Cells.Count > 0)
                    Grid.CurrentCell =
                        row.Cells.Cast<DataGridViewCell>().FirstOrDefault(cell => cell.Visible);
                _ = LoadSelectedUserAsync();
                return;
            }
        }
        DeselectUser();
    }
}
