using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Students.Profile;
public sealed class StudentPhotoPage : V2Page
{
    private readonly StudentPicker student = new();
    private readonly PictureBox preview = new() {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = AppTheme.Surface,
        BorderStyle = BorderStyle.FixedSingle
    };
    private readonly Label fileInfo = new() {
        Dock = DockStyle.Bottom,
        Height = Math.Max(16, AppMetrics.TextHeight(AppTheme.NormalFont)),
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.Text,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private byte[]? _pendingPhoto;
    private string? _pendingFileName;
    private string? _pendingContentType;
    private ModernButton choosePhotoButton = null!;
    private ModernButton savePhotoButton = null!;
    private ModernButton removePhotoButton = null!;
    private bool CanManagePhotos => CurrentUser.HasPermission("Students.Administer");
    public StudentPhotoPage() : base("Student Photo", "Add or update a student’s profile photo.")
    {
        UiHelper.DisposePictureImageOnDispose(preview);
        choosePhotoButton = AddBusyAction("Choose Photo…", ChoosePhotoAsync);
        /* writing a photo is a real write; guarded so a double click cannot save twice. */
        savePhotoButton = AddBusyAction("Save Photo", SavePhotoAsync, "Success");
        /* removing a photo deletes a row from the student record, so it is guarded like Save. */
        removePhotoButton = AddBusyAction("Remove Photo", RemovePhotoAsync, "Danger");
        var clear = AddAction(UiText.Actions.Clear, (_, _) => {
            ClearForm();
            FormValidator.FocusInput(student);
        }, "Secondary");
        /* The page is reachable with Students.Family, but saving or removing a photo goes to dbo.SaveStudentPhoto / dbo.DeleteStudentPhoto, which require Students.Administer. */
        GateAction(choosePhotoButton, "Students.Administer");
        GateAction(savePhotoButton, "Students.Administer");
        GateAction(removePhotoButton, "Students.Administer");
        choosePhotoButton.EnabledChanged += (_, _) => KeepActionAvailability();
        savePhotoButton.EnabledChanged += (_, _) => KeepActionAvailability();
        removePhotoButton.EnabledChanged += (_, _) => KeepActionAvailability();
        UseToolbar();
        student.UserSelectionChanged += async (_, _) => { await LoadPhotoAsync(); UpdateActionAvailability(); };
        student.SelectionCleared += async (_, _) => { await LoadPhotoAsync(); UpdateActionAvailability(); };
        student.SearchFailed += ex => Fail(ex);
        StudentSearchCache.Warm();
        var details = HumanUi.Card("Student", "Select a student to view or replace their profile photo.");
        details.Content.Controls.Add(HumanUi.Field("Student", student, true));
        var photoCard = HumanUi.Card("Photo", "Choose a JPEG or PNG image up to 5 MB.");
        var photoPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        photoPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        photoPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, fileInfo.Height));
        photoPanel.Controls.Add(preview, 0, 0);
        photoPanel.Controls.Add(fileInfo, 0, 1);
        photoCard.Content.Controls.Add(photoPanel);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(1)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(details, 0, 0);
        root.Controls.Add(photoCard, 0, 1);
        SetContent(root);
        ClearForm();
    }
    private async Task LoadPhotoAsync()
    {
        _pendingPhoto = null;
        _pendingFileName = null;
        _pendingContentType = null;
        UiHelper.ReplacePictureImage(preview, null);
        fileInfo.Text = string.Empty;
        if (student.SelectedId <= 0)
            return;
        try
        {
            var admissionId = student.SelectedId;
            var dt = await DatabaseHelper.QueryUserAsync("GetStudentPhoto", DatabaseHelper.Int("@AdmissionId", admissionId));
            if (IsDisposed || student.SelectedId != admissionId) return;
            if (dt.Rows.Count == 0)
            {
                SetStatus("No photo on file for this student.");
                UpdateActionAvailability();
                return;
            }
            var row = dt.Rows[0];
            UiHelper.ReplacePictureImage(
                preview,
                BitmapFromBytes((byte[])row["PhotoData"]));
            fileInfo.Text = $"{row["FileName"]} • updated {Convert.ToDateTime(row["UpdatedAt"]):g}";
            SetStatus("Photo loaded.");
            UpdateActionAvailability();
        }
        catch (Exception ex) { Fail(ex); }
    }
    private async Task ChoosePhotoAsync()
    {
        try
        {
            if (student.SelectedId <= 0)
            {
                Warn(UiText.Prompts.SelectStudent);
                return;
            }
            /* The student this photo is being chosen FOR, captured before the file read. Every other path in this file already re-checks the captured id. */
            int chosenAdmissionId =
                student.SelectedId;
            using var dialog = new OpenFileDialog {
                Filter = "Image files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                Title = "Choose a student photo"
            };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                return;
            var bytes = await File.ReadAllBytesAsync(dialog.FileName);
            if (IsDisposed ||
                student.SelectedId != chosenAdmissionId)
            {
                SetStatus(
                    "The selected student changed while the photo was being read, so it was not attached. Choose the photo again.",
                    true);
                return;
            }
            if (bytes.Length > 5_000_000)
            {
                Warn("Please choose an image smaller than 5 MB.");
                return;
            }
            /* Item 12 - the picture already on screen was disposed BEFORE the new one was decoded. */
            Bitmap chosenImage;
            try
            {
                chosenImage = BitmapFromBytes(bytes);
            }
            catch (Exception nonFatalException75)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException75, "StudentPhotoPage: ChoosePhotoAsync fallback");
                Warn(
                    "That file could not be read as an image, so nothing was changed. " +
                    "Please choose a different JPG or PNG file.");
                return;
            }
            UiHelper.ReplacePictureImage(preview, chosenImage);
            _pendingPhoto = bytes;
            _pendingFileName = Path.GetFileName(dialog.FileName);
            _pendingContentType = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch {
                ".png" => "image/png",
                _ => "image/jpeg"
            };
            fileInfo.Text = $"{_pendingFileName} (not saved yet)";
            SetStatus("Photo selected. Click Save Photo to store it.");
            UpdateActionAvailability();
        }
        catch (Exception ex) { Fail(ex); }
    }
    private static Bitmap BitmapFromBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var loaded = Image.FromStream(stream);
        return new Bitmap(loaded);
    }
    private async Task SavePhotoAsync()
    {
        if (!ValidateRequiredInputs())
            return;
        try
        {
            if (student.SelectedId <= 0)
            {
                Warn(UiText.Prompts.SelectStudent);
                return;
            }
            if (_pendingPhoto is null)
            {
                Warn("Choose a photo first.");
                return;
            }
            var admissionId = student.SelectedId;
            await DatabaseHelper.ExecuteUserAsync(
                "SaveStudentPhoto",
                DatabaseHelper.Int("@AdmissionId", admissionId),
                new SqlParameter("@PhotoData", SqlDbType.VarBinary, -1) { Value = _pendingPhoto },
                DatabaseHelper.P("@FileName", _pendingFileName),
                DatabaseHelper.P("@ContentType", _pendingContentType));
            if (IsDisposed || student.SelectedId != admissionId) return;
            Info("Photo saved successfully.");
            await LoadPhotoAsync();
        }
        catch (Exception ex) { Fail(ex); }
    }
    private async Task RemovePhotoAsync()
    {
        try
        {
            if (student.SelectedId <= 0)
            {
                Warn(UiText.Prompts.SelectStudent);
                return;
            }
            /* this deletes a stored photo from the student record and cannot be undone, so it is confirmed first. */
            if (!Confirm(
                    "Remove this student's profile photo?\r\n\r\n" +
                    "The existing photo will be deleted from the student record.",
                    "Remove Photo"))
            {
                return;
            }
            var admissionId = student.SelectedId;
            await DatabaseHelper.ExecuteUserAsync("DeleteStudentPhoto", DatabaseHelper.Int("@AdmissionId", admissionId));
            if (IsDisposed || student.SelectedId != admissionId) return;
            Info("Photo removed.");
            await LoadPhotoAsync();
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void ClearForm()
    {
        student.Clear();
        _pendingPhoto = null;
        _pendingFileName = null;
        _pendingContentType = null;
        UiHelper.ReplacePictureImage(preview, null);
        fileInfo.Text = string.Empty;
        SetStatus("Select a student to view or replace their photo.");
        UpdateActionAvailability();
    }
    private void KeepActionAvailability()
    {
        if (choosePhotoButton.Enabled && (!CanManagePhotos || student.SelectedId <= 0))
            choosePhotoButton.Enabled = false;
        if (savePhotoButton.Enabled && (!CanManagePhotos || student.SelectedId <= 0 || _pendingPhoto is null))
            savePhotoButton.Enabled = false;
        if (removePhotoButton.Enabled && (!CanManagePhotos || student.SelectedId <= 0 || preview.Image is null || _pendingPhoto is not null))
            removePhotoButton.Enabled = false;
    }
    private void UpdateActionAvailability()
    {
        choosePhotoButton.Enabled = CanManagePhotos && student.SelectedId > 0;
        savePhotoButton.Enabled = CanManagePhotos && student.SelectedId > 0 && _pendingPhoto is not null;
        removePhotoButton.Enabled = CanManagePhotos && student.SelectedId > 0 && preview.Image is not null && _pendingPhoto is null;
    }
}
