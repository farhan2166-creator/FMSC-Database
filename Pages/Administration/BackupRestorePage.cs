using FMSC_SMS.Core;
using FMSC_SMS.Services.Backup;
namespace FMSC_SMS.Pages.Administration;
public sealed class BackupRestorePage : V2Page, IBusyPage
{
    readonly ModernTextBox backupFolder = HumanUi.TextBox(false, 500);
    readonly ModernTextBox restoreFile = HumanUi.TextBox(false, 500);
    readonly bool canRestore = CurrentUser.HasPermission("Backup.Restore");
    ModernButton backupNowButton = null!;
    ModernButton? restoreButton;
    ModernButton? locateFileButton;
    bool isBusy;
    /* what the shell says when the operator tries to leave, sign out or close during a backup or a restore. Set beside isBusy so the two cannot drift apart. */
    string busyMessage = string.Empty;
    /// <summary>leaving this page disposes it, and signing out or closing ends the process.</summary>
    public bool IsBusy => isBusy;
    public string BusyMessage =>
        string.IsNullOrEmpty(busyMessage)
            ? "Work is running on this page. Please wait until it finishes."
            : busyMessage;
    /* Refresh History is a plain action, so it stayed clickable while a backup or a restore was running. It is held with the other busy-state actions now. */
    ModernButton? refreshHistoryButton;
    /* history reads had no version, so two overlapping ones (the operator's Refresh and the reload the busy path runs at the end) bound their tables in whichever order the database… */
    int historyLoadVersion;
    bool historyLoadedLatest;
    /* The backup history (and, for Locate Missing File, the row picked out of it) is what Restore and Locate Missing File depend on, and LoadHistoryAsync reads it asynchronously. */
    int historyLoadDepth;
    bool historyLoadFailed;
    bool historyLoading;
    DatabaseBackupManager.BackupInfo? selectedRestoreBackupInfo;
    /* The restore card's dedicated backup-status area. It is built only for an operator who may restore, so every use below is guarded. */
    Panel? restoreStatusPanel;
    Label? restoreStatusHeadline;
    Label? restoreStatusDetail;
    /* whether the selected file may reach the final confirmation at all. The final confirmation stays mandatory for everything else. */
    bool restoreAllowed = true;
    /// <summary>The four states this page can already tell apart from the header the manager reads.</summary>
    private enum RestoreFileState
    {
        None,
        Valid,
        Stale,
        WrongDatabase,
        Unverified
    }
    public BackupRestorePage()
        : base(
            PageTitles.BackupRestore,
            "Save a full copy of the FMSC database, or restore one that was saved earlier.")
    {
        backupFolder.ReadOnly = true;
        restoreFile.ReadOnly = true;
        var chooseBackupButton =
            AddBusyAction("Choose Backup Folder...", ChooseBackupFolderAsync);
        backupNowButton =
            AddBusyAction("Backup Database", BackupNowAsync, "Success");
        ModernButton? chooseRestoreButton = null;
        if (canRestore)
        {
            chooseRestoreButton =
                AddAction("Choose Backup File...", (_, _) => ChooseRestoreFile());
            /* restoring replaces the whole database, so it must never be able to start twice from a double click. */
            restoreButton =
                AddBusyAction("Restore Database", RestoreNowAsync, "Danger");
            /* AddBusyAction re-enables its button in a finally block after every attempt, which would undo a disabled state. */
            restoreButton.EnabledChanged += (_, _) => {
                if ((!restoreAllowed || !HistoryDependentActionsUsable) &&
                    restoreButton is { Enabled: true })
                    restoreButton.Enabled = false;
            };
        }
        refreshHistoryButton = AddBusyAction("Refresh History", LoadHistoryAsync);
        locateFileButton =
            AddBusyAction("Locate Missing File...", LocateMissingFileAsync, "Secondary");
        RelocateFromActions(
            chooseBackupButton,
            backupNowButton);
        if (chooseRestoreButton is not null && restoreButton is not null)
            RelocateFromActions(
                chooseRestoreButton,
                restoreButton);
        UseToolbar();
        var backupCard = HumanUi.Card(
            "Backup",
            "Choose a folder and save a full FMSC_DB backup file to it.");
        var localOnlyNotice = new Label {
            Dock = DockStyle.Top,
            Height = AppScale.Scale(46),
            AutoEllipsis = false,
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText,
            TextAlign = ContentAlignment.TopLeft,
            Text = "Note: this database lives only on this computer (it is not shared over a " +
                   "network). If your school uses more than one computer, records entered on " +
                   "one machine will not automatically appear on another. Back up regularly - " +
                   "this is the only copy of your data."
        };
        var backupFields = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(2, 4, 2, 0)
        };
        backupFields.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        backupFields.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
        backupFields.Controls.Add(
            HumanUi.Field("Backup Folder", backupFolder),
            0,
            0);
        backupFields.Controls.Add(
            HumanUi.ButtonRow(chooseBackupButton, backupNowButton),
            0,
            1);
        backupCard.Content.Controls.Add(backupFields);
        backupCard.Content.Controls.Add(localOnlyNotice);
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = canRestore ? 3 : 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(1) + AppMetrics.ButtonRowHeight + localOnlyNotice.Height));
        root.Controls.Add(backupCard, 0, 0);
        if (canRestore)
        {
            var restoreCard = HumanUi.Card(
                "Restore",
                "Replace the current database with a previously saved backup. This cannot be undone.");
            var restoreFields = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(2, 4, 2, 0)
            };
            restoreFields.RowStyles.Add(
                new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
            /* The backup-status area sits between the file field and the buttons, so the operator reads what the file IS before reaching the actions. */
            int restoreStatusHeight =
                AppMetrics.TextHeight(AppTheme.FontSmallBold) +
                AppMetrics.TextHeight(AppTheme.NormalFont) +
                2 * AppMetrics.SpacingSm;
            restoreFields.RowStyles.Add(
                new RowStyle(SizeType.Absolute, restoreStatusHeight));
            restoreFields.RowStyles.Add(
                new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
            restoreStatusHeadline = new Label {
                Dock = DockStyle.Top,
                Height = AppMetrics.TextHeight(AppTheme.FontSmallBold),
                Font = AppTheme.FontSmallBold,
                ForeColor = AppTheme.MutedText,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            restoreStatusDetail = new Label {
                Dock = DockStyle.Fill,
                Font = AppTheme.NormalFont,
                ForeColor = AppTheme.MutedText,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            restoreStatusPanel = new Panel {
                Dock = DockStyle.Fill,
                Padding = new Padding(
                    AppMetrics.SpacingSm,
                    AppMetrics.SpacingXs,
                    AppMetrics.SpacingSm,
                    AppMetrics.SpacingXs),
                BackColor = AppTheme.Tint(AppTheme.MutedText, 0.90f)
            };
            /* Dock order: the Fill detail is added first, so the Top headline takes the top edge and the detail fills what is left below it. */
            restoreStatusPanel.Controls.Add(restoreStatusDetail);
            restoreStatusPanel.Controls.Add(restoreStatusHeadline);
            restoreFields.Controls.Add(
                HumanUi.Field("Backup File", restoreFile),
                0,
                0);
            restoreFields.Controls.Add(
                restoreStatusPanel,
                0,
                1);
            restoreFields.Controls.Add(
                HumanUi.ButtonRow(chooseRestoreButton!, restoreButton!),
                0,
                2);
            restoreCard.Content.Controls.Add(restoreFields);
            root.RowStyles.Add(
                new RowStyle(
                    SizeType.Absolute,
                    AppMetrics.FieldCardHeight(1) +
                    AppMetrics.ButtonRowHeight +
                    restoreStatusHeight));
            root.Controls.Add(restoreCard, 0, 1);
            /* no file is selected yet, so the area starts in its neutral state rather than looking like a verified backup. */
            ShowRestoreFileStatus(RestoreFileState.None, null);
        }
        var historyCard = HumanUi.Card(
            "Backup & Restore History",
            "Every backup and restore operation performed from this application.");
        historyCard.Content.Controls.Add(Grid);
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(historyCard, 0, canRestore ? 2 : 1);
        SetContent(root);
        LoadBackupFolder();
        /* LoadHistoryAsync opens the gate for the deferred load; this closes it first so no frame is painted in which Restore or Locate Missing File looks usable before the history has… */
        CloseHistoryDependentActions();
        Defer(LoadHistoryAsync);
    }
    /// <summary>True only while the actions that read the backup history may be used at all.</summary>
    bool HistoryDependentActionsUsable =>
        !isBusy &&
        !historyLoading &&
        !historyLoadFailed;
    void CloseHistoryDependentActions()
    {
        if (locateFileButton is not null)
            locateFileButton.Enabled = false;
        if (restoreButton is not null)
            restoreButton.Enabled = false;
    }
    void BeginHistoryLoad()
    {
        historyLoadDepth++;
        historyLoading = true;
        CloseHistoryDependentActions();
    }
    /// <summary>re-open the gate only after a fully successful read.</summary>
    void EndHistoryLoad()
    {
        if (historyLoadDepth > 0)
            historyLoadDepth--;
        if (historyLoadDepth > 0)
            return;
        historyLoading = false;
        historyLoadFailed = !historyLoadedLatest;
        if (historyLoadFailed)
            return;
        ApplyHistoryDependentEnablement();
    }
    /// <summary>The page's own rules for the two history-dependent actions, in one place so the gate and the busy guard cannot contradict each other.</summary>
    void ApplyHistoryDependentEnablement()
    {
        bool usable = HistoryDependentActionsUsable;
        if (locateFileButton is not null)
            locateFileButton.Enabled = usable;
        if (restoreButton is not null)
            restoreButton.Enabled = usable && restoreAllowed;
    }
    void LoadBackupFolder()
    {
        string folder = BackupSettings.GetBackupFolder();
        if (!string.IsNullOrWhiteSpace(folder))
            backupFolder.Text = folder;
    }
    async Task ChooseBackupFolderAsync()
    {
        if (isBusy)
            return;
        using var dialog = new FolderBrowserDialog {
            Description = "Select the folder where FMSC database backups will be saved.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        string currentFolder = backupFolder.Text.Trim();
        if (Directory.Exists(currentFolder))
            dialog.SelectedPath = currentFolder;
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            return;
        string newFolder = dialog.SelectedPath;
        /* setting the backup folder also relinks existing backup records to it, which is a real state change rather than a preference. */
        int relinkable = await Task.Run(
            () => DatabaseBackupManager.CountAutoRelinkableBackups(newFolder));
        if (relinkable > 0)
        {
            var confirmation = ModernMessageDialog.Show(
                FindForm(),
                "Change backup folder?\n\n" +
                $"Folder: {newFolder}\n\n" +
                $"Existing backup records found: {relinkable}\n\n" +
                "Those records will be relinked to this folder. This changes where the " +
                "application expects those backup files to be located.\n\n" +
                "Do you want to change the backup folder?",
                "Confirm Backup Folder Change",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes)
            {
                SetStatus("Backup folder was not changed.");
                return;
            }
        }
        backupFolder.Text = newFolder;
        BackupSettings.SetBackupFolder(newFolder);
        int relinked = await Task.Run(() => DatabaseBackupManager.TryAutoRelinkMissingBackups(newFolder));
        SetStatus($"Backup folder changed to {newFolder}.");
        if (relinked > 0)
            Info($"Relinked {relinked} existing backup record(s) to this folder.");
        await LoadHistoryAsync();
    }
    async Task BackupNowAsync()
    {
        if (isBusy)
            return;
        string folder = backupFolder.Text.Trim();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            Warn("Please choose a valid backup folder first.");
            return;
        }
        string backupPath = DatabaseBackupManager.CreateUniqueBackupPath(
            folder,
            DatabaseBackupManager.CreateBackupFileName());
        isBusy = true;
        busyMessage = "A backup is running. Please wait until it finishes.";
        SetBusyState(true);
        SetStatus("Backing up database...");
        try
        {
            var result = await Task.Run(() => DatabaseBackupManager.CreateBackup(backupPath));
            /* reload FIRST, then report. */
            await LoadHistoryAsync();
            if (result.Success)
            {
                /* A dialog ON PURPOSE, and the only one of its kind on this page. */
                ModernMessageDialog.ShowInfo(
                    FindForm(),
                    $"{result.Message}\n\n{backupPath}");
            }
            else
            {
                Warn(result.Message);
            }
        }
        catch (Exception ex)
        {
            await LoadHistoryAsync();
            Fail(ex);
        }
        finally
        {
            isBusy = false;
            busyMessage = string.Empty;
            SetBusyState(false);
        }
    }
    void ChooseRestoreFile()
    {
        if (isBusy)
            return;
        using var dialog = new OpenFileDialog {
            Title = "Select FMSC Database Backup",
            Filter = "SQL Server Backup Files (*.bak)|*.bak|All Files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            return;
        restoreFile.Text = dialog.FileName;
        selectedRestoreBackupInfo = DatabaseBackupManager.GetBackupInfo(dialog.FileName);
        RestoreFileState state = ClassifyRestoreFile(selectedRestoreBackupInfo);
        ShowRestoreFileStatus(state, selectedRestoreBackupInfo);
        string ageText = selectedRestoreBackupInfo is null
            ? "unknown"
            : FormatShortAge(selectedRestoreBackupInfo);
        switch (state)
        {
            case RestoreFileState.Unverified:
                SetStatus(
                    "Restore file selected (backup metadata could not be verified). " +
                    "Restore is disabled.",
                    true);
                break;
            case RestoreFileState.WrongDatabase:
                SetStatus("Restore file selected (wrong database). Restore is disabled.", true);
                /* this is now refused, not merely advised. The wording says so, because the restore itself will reject the file. */
                Warn(
                    $"This file was backed up from \"{selectedRestoreBackupInfo!.DatabaseName}\", " +
                    "not FMSC_DB.\n\nIt cannot be restored: a restore replaces the FMSC database, " +
                    "and only a backup of FMSC_DB may do that. Choose the correct backup file.");
                break;
            case RestoreFileState.Stale:
                SetStatus(
                    $"Restore file selected (stale FMSC backup, {ageText} old).",
                    true);
                break;
            case RestoreFileState.Valid:
                SetStatus($"Restore file selected (valid FMSC backup, {ageText} old).");
                break;
        }
    }
    /// <summary>classify the metadata the manager already read. Nothing here re-reads the file or re-implements a validation rule; it only names the outcome GetBackupInfo / BackupInfo already express.</summary>
    static RestoreFileState ClassifyRestoreFile(DatabaseBackupManager.BackupInfo? info)
    {
        /* A null BackupInfo is the manager's "RESTORE HEADERONLY could not be read" result - the unreadable/corrupt case. */
        if (info is null)
            return RestoreFileState.Unverified;
        /* "(unknown)" is DatabaseBackupManager's own placeholder for a header that reported no DatabaseName. */
        if (string.IsNullOrWhiteSpace(info.DatabaseName) ||
            string.Equals(info.DatabaseName, "(unknown)", StringComparison.Ordinal))
        {
            return RestoreFileState.Unverified;
        }
        if (!info.IsForFmscDatabase)
            return RestoreFileState.WrongDatabase;
        return info.IsStale
            ? RestoreFileState.Stale
            : RestoreFileState.Valid;
    }
    /// <summary>write the state into the restore card's status area.</summary>
    void ShowRestoreFileStatus(RestoreFileState state, DatabaseBackupManager.BackupInfo? info)
    {
        if (restoreStatusPanel is null ||
            restoreStatusHeadline is null ||
            restoreStatusDetail is null)
            return;
        string databaseName = info?.DatabaseName ?? string.Empty;
        string backupAge = info is null ? string.Empty : FormatBackupAge(info);
        string headline;
        string detail;
        Color accent;
        switch (state)
        {
            case RestoreFileState.Valid:
                accent = AppTheme.Success;
                headline = "Valid FMSC backup";
                detail =
                    $"Database: {databaseName} - backup taken {backupAge}. " +
                    "Restore Database will ask for confirmation.";
                break;
            case RestoreFileState.Stale:
                accent = AppTheme.Warning;
                headline = "Stale FMSC backup - older than the staleness limit";
                detail =
                    $"Database: {databaseName} - backup taken {backupAge}. " +
                    "Warning: newer records would be lost.";
                break;
            case RestoreFileState.WrongDatabase:
                accent = AppTheme.Danger;
                headline = "Wrong database - this is not an FMSC_DB backup";
                detail =
                    $"Database: {databaseName} (not FMSC_DB). Restore is disabled.";
                break;
            case RestoreFileState.Unverified:
                accent = AppTheme.Danger;
                headline = "Backup metadata could not be verified";
                detail =
                    "Backup header could not be read: database and date unknown. " +
                    "Restore is disabled.";
                break;
            default:
                accent = AppTheme.MutedText;
                headline = "No backup file selected";
                detail = "Choose a .bak file to see whether it is a valid FMSC backup.";
                break;
        }
        restoreStatusHeadline.Text = headline;
        restoreStatusHeadline.ForeColor = accent;
        restoreStatusDetail.Text = detail;
        restoreStatusDetail.ForeColor =
            state == RestoreFileState.None ? AppTheme.MutedText : AppTheme.Text;
        restoreStatusPanel.BackColor = AppTheme.Tint(accent, 0.90f);
        /* Valid and stale backups keep the final confirmation; wrong-database and unverifiable files cannot be restored at all. */
        SetRestoreAllowed(
            state is RestoreFileState.Valid or RestoreFileState.Stale or RestoreFileState.None);
    }
    void SetRestoreAllowed(bool allowed)
    {
        restoreAllowed = allowed;
        if (restoreButton is null)
            return;
        /* refuses the file; / additionally refuse to re-open the button over an unfinished or failed history read. */
        restoreButton.Enabled =
            allowed &&
            HistoryDependentActionsUsable;
    }
    static string FormatShortAge(DatabaseBackupManager.BackupInfo info)
    {
        TimeSpan age = info.Age;
        return age.TotalDays >= 1
            ? $"{(int)age.TotalDays}d"
            : age.TotalHours >= 1
                ? $"{(int)age.TotalHours}h"
                : "<1h";
    }
    static string FormatBackupAge(DatabaseBackupManager.BackupInfo info)
    {
        string when = info.BackupStartDate.ToString("dd-MMM-yyyy HH:mm");
        TimeSpan age = info.Age;
        string ageText = age.TotalDays >= 1
            ? $"{(int)age.TotalDays} day(s) ago"
            : age.TotalHours >= 1
                ? $"{(int)age.TotalHours} hour(s) ago"
                : "less than an hour ago";
        return $"on {when} ({ageText})";
    }
    async Task RestoreNowAsync()
    {
        if (isBusy)
            return;
        try
        {
            RequirePermission("Backup.Restore");
        }
        catch (Exception ex)
        {
            Fail(ex);
            return;
        }
        string backupPath = restoreFile.Text.Trim();
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            Warn("Please choose a valid .bak backup file first.");
            return;
        }
        if (selectedRestoreBackupInfo is null ||
            !string.Equals(restoreFile.Tag as string, backupPath, StringComparison.OrdinalIgnoreCase))
        {
            selectedRestoreBackupInfo = DatabaseBackupManager.GetBackupInfo(backupPath);
        }
        restoreFile.Tag = backupPath;
        var info = selectedRestoreBackupInfo;
        /* The header is re-read above whenever the selected path changed, so the status area is refreshed here and a file whose metadata is not verifiable (or is from another database)… */
        RestoreFileState state = ClassifyRestoreFile(info);
        ShowRestoreFileStatus(state, info);
        if (state is RestoreFileState.Unverified or RestoreFileState.WrongDatabase)
        {
            Warn(
                state == RestoreFileState.Unverified
                    ? "This file's backup metadata could not be verified, so it cannot be " +
                      "restored. Choose a readable FMSC_DB backup file."
                    : $"This file was backed up from \"{info?.DatabaseName}\", not FMSC_DB, " +
                      "so it cannot be restored. Choose an FMSC_DB backup file.");
            return;
        }
        var detailLines = new List<string> {
            "Restoring this backup will replace the current FMSC database with the " +
                "selected backup.",
            "",
            "Any data created after this backup may be lost. This action cannot be undone."
        };
        if (info is null)
        {
            detailLines.Add("");
            detailLines.Add(
                "WARNING: This file's backup header could not be read, so its source " +
                "database and date are unknown. It will still be integrity-checked " +
                "before anything is changed, but proceed with extra caution.");
        }
        else
        {
            detailLines.Add("");
            detailLines.Add($"Backup source database: {info.DatabaseName}");
            detailLines.Add($"Backup taken: {FormatBackupAge(info)}");
            if (!info.IsForFmscDatabase)
            {
                detailLines.Add("");
                detailLines.Add(
                    $"This backup was taken from \"{info.DatabaseName}\", not FMSC_DB. " +
                    "It will be REJECTED and nothing will be changed. Choose a backup " +
                    "of FMSC_DB instead.");
            }
            else if (info.IsStale)
            {
                detailLines.Add("");
                detailLines.Add(
                    $"WARNING: This backup is {(int)info.Age.TotalDays} day(s) old. " +
                    "Everything recorded in FMSC since then - admissions, fee payments, " +
                    "and so on - will be lost if you continue.");
            }
        }
        detailLines.Add("");
        detailLines.Add(
            "A safety backup of the CURRENT database will be taken automatically " +
            "before the restore proceeds, so this can be undone if needed.");
        detailLines.Add("");
        detailLines.Add(UiText.Dialogs.Continue);
        var confirmation = ModernMessageDialog.Show(
            FindForm(),
            string.Join("\n", detailLines),
            "Confirm Database Restore",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
            return;
        isBusy = true;
        busyMessage = "A restore is running. Please wait until it finishes.";
        SetBusyState(true);
        SetStatus("Restoring database...");
        try
        {
            var result = await Task.Run(() => DatabaseBackupManager.RestoreBackup(backupPath));
            if (result.Success)
            {
                ModernMessageDialog.Show(
                    FindForm(),
                    result.Message + "\n\nThe application will now restart so all database " +
                    "connections and data are refreshed.",
                    "Restore Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                /* The RESTORE statement has finished, so closing the application is safe now. */
                isBusy = false;
                busyMessage = string.Empty;
                RestartApplication(FindForm());
                return;
            }
            /* same ordering rule as the backup path above: reload, then report, so the outcome is the last thing written to the status. */
            await LoadHistoryAsync();
            Warn(result.Message);
        }
        catch (Exception ex)
        {
            await LoadHistoryAsync();
            Fail(ex);
        }
        finally
        {
            isBusy = false;
            busyMessage = string.Empty;
            /* After a successful restore the window has been closed above, so this page may already be disposed. */
            if (!IsDisposed)
                SetBusyState(false);
        }
    }
    /// <summary>Restarts the application after a completed restore, leaving exactly one instance running. If the close is refused, nothing is launched and the operator is told to reopen the application.</summary>
    static void RestartApplication(Form? shell)
    {
        var exePath = Environment.ProcessPath;
        if (shell == null || shell.IsDisposed || string.IsNullOrEmpty(exePath))
        {
            try
            {
                Application.Restart();
            }
            catch (Exception nonFatalException26)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException26, "BackupRestorePage: RestartApplication fallback");
                Application.Exit();
            }
            return;
        }
        FormClosedEventHandler? launchNewInstance = null;
        launchNewInstance = (_, _) => {
            shell.FormClosed -= launchNewInstance;
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(exePath) {
                        UseShellExecute = true,
                        WorkingDirectory = AppContext.BaseDirectory
                    });
            }
            catch (Exception nonFatalException27)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException27, "BackupRestorePage: RestartApplication fallback");
                /* The application is closing; the operator can start it again from the shortcut. */
            }
        };
        shell.FormClosed += launchNewInstance;
        shell.Close();
        if (!shell.IsDisposed)
        {
            /* The close was refused, so the window is still open and no second instance was started. */
            shell.FormClosed -= launchNewInstance;
            ModernMessageDialog.ShowInfo(
                shell,
                "The restore is complete, but FMSC could not close itself. " +
                "Please close FMSC and open it again.",
                "Restore Complete");
        }
    }
    /// <summary>The history and the two actions that read it are gated for the duration of this read.</summary>
    async Task LoadHistoryAsync()
    {
        int version = ++historyLoadVersion;
        BeginHistoryLoad();
        try
        {
            /* Tidy BEFORE the list is read, so what the operator sees is what survived. */
            (int removed, int failed, string? firstFailure) pruned =
                await Task.Run(DatabaseBackupManager.PruneHistory);
            DataTable history = await Task.Run(DatabaseBackupManager.GetHistory);
            if (IsDisposed) return;
            /* A read that has been superseded must not bind its table over the newer one, and must not claim the outcome the gate is about to apply. */
            if (version == historyLoadVersion)
            {
                Bind(history);
                historyLoadedLatest = true;
                /* After Bind, because the rebind writes its own record count over the status line - the same ordering the backup and restore paths use. */
                if (pruned.removed > 0)
                {
                    SetStatus(
                        $"{pruned.removed} history entr" +
                        (pruned.removed == 1 ? "y was" : "ies were") +
                        " removed: the backup file is no longer there, or the list is past " +
                        $"its {DatabaseBackupManager.HistoryLimit}-entry limit.");
                }
                if (pruned.failed > 0)
                {
                    /* Told, not swallowed: the usual cause is a database that has not had FMSC_FIX_083 applied, and then the old entries simply keep returning. */
                    SetStatus(
                        $"{pruned.failed} history entr" +
                        (pruned.failed == 1 ? "y could" : "ies could") +
                        $" not be tidied: {pruned.firstFailure}",
                        true);
                }
            }
        }
        catch (Exception ex)
        {
            if (version == historyLoadVersion)
                historyLoadedLatest = false;
            if (!IsDisposed) Fail(ex);
        }
        finally
        {
            EndHistoryLoad();
        }
    }
    async Task LocateMissingFileAsync()
    {
        if (isBusy)
            return;
        if (Grid.SelectedRows.Count == 0 || Grid.SelectedRows[0].DataBoundItem is not DataRowView selected)
        {
            Warn("Select a row in the history grid first, then choose Locate Missing File.");
            return;
        }
        DataRow row = selected.Row;
        long backupId = Convert.ToInt64(row["BackupId"]);
        string recordedFileName = row["FileName"] as string ?? string.Empty;
        string recordedPath = row["FilePath"] as string ?? string.Empty;
        if (File.Exists(recordedPath))
        {
            Info($"This backup's recorded file is already available:\n\n{recordedPath}");
            return;
        }
        using var dialog = new OpenFileDialog {
            Title = $"Locate {recordedFileName}",
            Filter = "SQL Server Backup Files (*.bak)|*.bak|All Files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            return;
        var result = await Task.Run(() => DatabaseBackupManager.RelocateBackupPath(backupId, dialog.FileName));
        if (result.Success)
        {
            SetStatus("Backup record relocated.");
            await LoadHistoryAsync();
        }
        else
        {
            Warn(result.Message);
        }
    }
    void SetBusyState(bool busy)
    {
        backupNowButton.Enabled = !busy;
        /* Refresh History is the retry path for a failed READ, which is why it is left open by BeginHistoryLoad. */
        if (refreshHistoryButton is not null)
            refreshHistoryButton.Enabled = !busy;
        if (busy)
        {
            if (restoreButton != null)
                restoreButton.Enabled = false;
            if (locateFileButton != null)
                locateFileButton.Enabled = false;
            return;
        }
        /* The page's own rule rather than forcing Enabled = true. */
        ApplyHistoryDependentEnablement();
    }
}
