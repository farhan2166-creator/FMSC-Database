using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Setup;
public sealed class SchoolProfilePage : V2Page, IUnsavedChanges
{
    private readonly ModernTextBox schoolName =
        HumanUi.TextBox(false, 300);
    private readonly ModernTextBox addressLine =
        HumanUi.TextBox(false, 500);
    private readonly ModernTextBox city =
        HumanUi.TextBox(false, 200);
    private readonly ModernTextBox contactNumber =
        HumanUi.TextBox(false, 150);
    private readonly ModernTextBox email =
        HumanUi.TextBox(false, 250);
    private readonly ModernNumericTextBox feeProrationCutoffDay =
        HumanUi.Number(28);
    private readonly PictureBox logoPreview = new() {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = AppTheme.Surface,
        BorderStyle = BorderStyle.FixedSingle
    };
    private readonly Label logoInfo = new() {
        Dock = DockStyle.Bottom,
        Height = Math.Max(16, AppMetrics.TextHeight(AppTheme.NormalFont)),
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.Text,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private byte[]? _logoBytes;
    private string? _logoFileName;
    private string? _logoContentType;
    private bool _logoRemoved;
    public SchoolProfilePage()
        : base(PageTitles.SchoolInformation, "Set the school details shown on reports, certificates and receipts.")
    {
        UiHelper.DisposePictureImageOnDispose(logoPreview);
        RecordTextPolicy.WireInput(schoolName, "@SchoolName");
        RecordTextPolicy.WireInput(addressLine, "@AddressLine");
        RecordTextPolicy.WireInput(city, "@City");
        AddAction(UiText.Actions.Save, (_, _) => Save(), "Success");
        /* # - Reload re-reads every field and the logo, so a click could throw away typed work without a word. */
        AddAction(UiText.Actions.Reload, (_, _) => {
            if (!ConfirmDiscardChanges())
                return;
            LoadProfile();
        });
        AddBusyAction("Choose Logo…", ChooseLogoAsync);
        AddAction("Remove Logo", (_, _) => RemoveLogo(), "Danger");
        UseToolbar();
        var details =
            HumanUi.Card("School Identity", "Shown wherever a document needs the school's name, address or contact details.");
        var fields =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        fields.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 3; i++)
        {
            fields.RowStyles.Add(
                new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        }
        fields.Controls.Add(
            HumanUi.Field("School Name", schoolName, true), 0, 0);
        fields.Controls.Add(
            HumanUi.Field("Phone / Mobile", contactNumber), 1, 0);
        fields.Controls.Add(
            HumanUi.Field("Address", addressLine), 0, 1);
        fields.Controls.Add(
            HumanUi.Field("City", city), 1, 1);
        fields.Controls.Add(
            HumanUi.Field("Email", email), 0, 2);
        details.Content.Controls.Add(fields);
        var logoCard =
            HumanUi.Card("Logo", "Choose a PNG or JPEG image up to 2 MB.");
        var logoPanel =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
        logoPanel.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        logoPanel.RowStyles.Add(
            new RowStyle(SizeType.Absolute, logoInfo.Height));
        logoPanel.Controls.Add(logoPreview, 0, 0);
        logoPanel.Controls.Add(logoInfo, 0, 1);
        logoCard.Content.Controls.Add(logoPanel);
        var feePolicyCard =
            HumanUi.Card("Fee Policy", "When monthly fees begin for new admissions.");
        var feePolicyFields =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        feePolicyFields.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        feePolicyFields.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 50));
        feePolicyFields.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.FieldRowHeight));
        feePolicyFields.Controls.Add(
            HumanUi.Field("Fee Proration Cutoff Day", feeProrationCutoffDay), 0, 0);
        const string cutoffExplanation = "New admissions on or after this day start monthly fees next month; earlier admissions start in their admission month. Readmission uses the month chosen during fee review.";
        SetToolTip(feeProrationCutoffDay, cutoffExplanation);
        feePolicyFields.RowCount = 2;
        int guidanceHeight = AppMetrics.TextHeight(AppTheme.NormalFont) * 3 + 8;
        feePolicyFields.RowStyles.Add(new RowStyle(SizeType.Absolute, guidanceHeight));
        var cutoffGuidance = new Label {
            Text = cutoffExplanation,
            Dock = DockStyle.Fill,
            Font = AppTheme.NormalFont,
            ForeColor = AppTheme.MutedText,
            AutoEllipsis = true
        };
        SetToolTip(cutoffGuidance, cutoffExplanation);
        feePolicyFields.Controls.Add(cutoffGuidance, 0, 1);
        feePolicyFields.SetColumnSpan(cutoffGuidance, 2);
        feePolicyCard.Content.Controls.Add(feePolicyFields);
        /* Left column: School Identity, then Fee Policy under it. Row heights are absolute so the logo card spans exactly the two left cards. */
        int gap = UiDesignSystem.Spacing.Md;
        var root =
            new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        root.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 62));
        root.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 38));
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(3) + gap));
        root.RowStyles.Add(
            new RowStyle(SizeType.Absolute, AppMetrics.FieldCardHeight(1) + guidanceHeight));
        details.Margin = new Padding(0, 0, gap, gap);
        feePolicyCard.Margin = new Padding(0, 0, gap, 0);
        logoCard.Margin = new Padding(0);
        root.Controls.Add(details, 0, 0);
        root.Controls.Add(feePolicyCard, 0, 1);
        root.Controls.Add(logoCard, 1, 0);
        root.SetRowSpan(logoCard, 2);
        SetContent(
            root,
            "Update the school details and click Save. Leave the fee cutoff day blank to start monthly fees in the admission month.");
        FormFlow.EnterMovesToNext(this);
        Defer(LoadProfile);
    }
    /* True only after a read of the profile completed (a profile, or none set up yet). */
    private bool _profileReadCompleted;
    private void LoadProfile()
    {
        try
        {
            _profileReadCompleted = false;
            _logoRemoved = false;
            var dt =
                DatabaseHelper.QueryUser("GetSchoolProfile");
            if (dt.Rows.Count == 0)
            {
                schoolName.Clear();
                addressLine.Clear();
                city.Clear();
                contactNumber.Clear();
                email.Clear();
                feeProrationCutoffDay.Value = 0;
                ClearLogoState();
                CaptureBaseline();
                _profileReadCompleted = true;
                SetStatus("The school profile has not been set up yet. Fill in the details below and click Save.");
                return;
            }
            DataRow row =
                dt.Rows[0];
            schoolName.Text =
                TextOf(row["SchoolName"]);
            addressLine.Text =
                TextOf(row["AddressLine"]);
            city.Text =
                TextOf(row["City"]);
            contactNumber.Text =
                TextOf(row["ContactNumber"]);
            email.Text =
                TextOf(row["Email"]);
            feeProrationCutoffDay.Value =
                row["FeeProrationCutoffDay"] == DBNull.Value
                    ? 0
                    : Convert.ToDecimal(row["FeeProrationCutoffDay"]);
            if (row["LogoImage"] != DBNull.Value)
            {
                _logoBytes =
                    (byte[])row["LogoImage"];
                _logoFileName =
                    TextOf(row["LogoFileName"]);
                _logoContentType =
                    TextOf(row["LogoContentType"]);
                UiHelper.ReplacePictureImage(logoPreview, BitmapFromBytes(_logoBytes));
                logoInfo.Text =
                    string.IsNullOrWhiteSpace(_logoFileName)
                        ? "Logo on file."
                        : _logoFileName;
            }
            else
            {
                ClearLogoState();
            }
            SetStatus("School profile loaded.");
            /* # - the baseline Reload compares against. Save reloads the profile afterwards, so a committed save leaves the form clean and Reload never asks about work that was just saved. */
            CaptureBaseline();
            _profileReadCompleted = true;
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    private async Task ChooseLogoAsync()
    {
        try
        {
            using var dialog =
                new OpenFileDialog {
                    Filter =
                        "Image files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                    Title =
                        "Choose the school logo"
                };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                return;
            byte[] bytes =
                await File.ReadAllBytesAsync(dialog.FileName);
            if (bytes.Length > 2_000_000)
            {
                Warn("Please choose an image smaller than 2 MB.");
                return;
            }
            UiHelper.ReplacePictureImage(logoPreview, BitmapFromBytes(bytes));
            _logoBytes = bytes;
            _logoFileName =
                Path.GetFileName(dialog.FileName);
            _logoContentType =
                Path.GetExtension(dialog.FileName)
                    .ToLowerInvariant() switch {
                        ".png" => "image/png",
                        _ => "image/jpeg"
                    };
            _logoRemoved = false;
            logoInfo.Text =
                $"{_logoFileName} (not saved yet)";
            SetStatus("Logo selected. Click Save to store it.");
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    private void RemoveLogo()
    {
        if (_logoBytes is null)
        {
            Warn("There is no logo to remove.");
            return;
        }
        ClearLogoState();
        _logoRemoved = true;
        logoInfo.Text =
            "No logo (not saved yet)";
        SetStatus("Logo removed. Click Save to make this permanent.");
    }
    private void ClearLogoState()
    {
        UiHelper.ReplacePictureImage(logoPreview, null);
        logoInfo.Text =
            "No logo on file.";
        _logoBytes = null;
        _logoFileName = null;
        _logoContentType = null;
    }
    private static Bitmap BitmapFromBytes(byte[] bytes)
    {
        using var stream =
            new MemoryStream(bytes);
        using var loaded =
            Image.FromStream(stream);
        return new Bitmap(loaded);
    }
    private void Save()
    {
        try
        {
            if (!_profileReadCompleted)
            {
                Warn("The school profile has not been read successfully, so saving now could overwrite it with blanks. Use Reload first.");
                return;
            }
            if (!ValidateRequiredInputs())
                return;
            if (string.IsNullOrWhiteSpace(schoolName.Text))
            {
                Warn("Enter the school name.");
                return;
            }
            /* this used to be a bare Contains('@'), which accepted "abc@", "@abc" and "@". The field is optional, so blank stays acceptable; anything else must look like a deliverable address. */
            if (!EmailAddressValidator.IsBlankOrValid(email.Text))
            {
                Warn("Enter a valid email address, such as name@example.com, or leave it blank.");
                return;
            }
            byte[]? logoToSave =
                _logoRemoved
                    ? null
                    : _logoBytes;
            string? logoFileNameToSave =
                _logoRemoved
                    ? null
                    : _logoFileName;
            string? logoContentTypeToSave =
                _logoRemoved
                    ? null
                    : _logoContentType;
            DatabaseHelper.Execute(
                "SaveSchoolProfile",
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.P(
                    "@SchoolName",
                    schoolName.Text.Trim()),
                DatabaseHelper.P(
                    "@AddressLine",
                    DatabaseHelper.OptionalText(addressLine.Text)),
                DatabaseHelper.P(
                    "@City",
                    DatabaseHelper.OptionalText(city.Text)),
                DatabaseHelper.P(
                    "@ContactNumber",
                    DatabaseHelper.OptionalText(contactNumber.Text)),
                DatabaseHelper.P(
                    "@Email",
                    DatabaseHelper.OptionalText(email.Text)),
                new SqlParameter(
                    "@LogoImage",
                    SqlDbType.VarBinary,
                    -1) {
                    Value =
                        (object?)logoToSave
                        ?? DBNull.Value
                },
                DatabaseHelper.P(
                    "@LogoFileName",
                    logoFileNameToSave),
                DatabaseHelper.P(
                    "@LogoContentType",
                    logoContentTypeToSave),
                DatabaseHelper.NullableInt(
                    "@FeeProrationCutoffDay",
                    feeProrationCutoffDay.Value > 0
                        ? (int?)feeProrationCutoffDay.Value
                        : null));
            _logoRemoved = false;
            Info("School profile saved.");
            LoadProfile();
            Defer(() => FormValidator.FocusInput(schoolName));
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    /* # - the field and logo values as they were when the profile was last loaded. */
    private string[] loadedValues =
        Array.Empty<string>();
    private byte[]? loadedLogoBytes;
    private string? loadedLogoFileName;
    private bool loadedLogoRemoved;
    private string[] CurrentValues() => new[]
    {
        schoolName.Text,
        addressLine.Text,
        city.Text,
        contactNumber.Text,
        email.Text,
        feeProrationCutoffDay.Value.ToString()
    };
    private void CaptureBaseline()
    {
        loadedValues =
            CurrentValues();
        loadedLogoBytes =
            _logoBytes;
        loadedLogoFileName =
            _logoFileName;
        loadedLogoRemoved =
            _logoRemoved;
    }
    /// <summary>implements IUnsavedChanges so the dashboard's navigation guard can see this page.</summary>
    public bool HasUnsavedChanges =>
        ComputeHasUnsavedChanges();
    public string UnsavedChangesMessage =>
        "This record has unsaved changes.\r\n\r\nIf you leave now, those changes will be lost.";
    /* Renamed from HasUnsavedChanges so the interface property can take that name: the page had a private method of that name. */
    private bool ComputeHasUnsavedChanges()
    {
        if (loadedValues.Length == 0)
            return false;
        try
        {
            if (_logoRemoved != loadedLogoRemoved)
                return true;
            if (!ReferenceEquals(_logoBytes, loadedLogoBytes))
                return true;
            if (!string.Equals(_logoFileName, loadedLogoFileName, StringComparison.Ordinal))
            {
                return true;
            }
            string[] current =
                CurrentValues();
            if (current.Length != loadedValues.Length)
                return true;
            for (int i = 0; i < current.Length; i++)
            {
                if (!string.Equals(current[i], loadedValues[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception nonFatalException36)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException36, "SchoolProfilePage: ComputeHasUnsavedChanges fallback");
            /* A dirty check must never throw, and it must never invent a prompt: an unreadable form is treated as clean. */
            return false;
        }
    }
    private bool ConfirmDiscardChanges()
    {
        if (!ComputeHasUnsavedChanges())
            return true;
        return ModernMessageDialog.Confirm(FindForm(), UnsavedChangesMessage, UiText.Dialogs.UnsavedChanges);
    }
    private static string TextOf(
        object value) =>
        value is null || value == DBNull.Value
            ? ""
            : Convert.ToString(value) ?? "";
}
