using System.ComponentModel;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Finance.Collection;
using FMSC_SMS.Pages.Finance.StudentAccounts;
using FMSC_SMS.Pages.Students.Dialogs;
using FMSC_SMS.Pages.Students.Enrollment;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Students.Admissions;
public sealed class StudentAdmissionPage : ContextPage, IUnsavedChanges, IBusyPage
{
    protected override int ActionStatusWidth => 120;
    private ToolStripItem? confirmPlacementAction;
    private int pendingPlacementId;
    private static readonly Padding AdmissionDetailsCardPadding =
        new(2, 4, 2, 2);
    private static readonly string[] MandatoryFeeTypeCodes = { "TUITION", "ADMISSION" };
    /// <summary>The page's normal subtitle, held in one place so the header can be restored exactly when the form returns to "new admission" mode.</summary>
    private const string AdmissionContextSubtitle =
        "Admit a student or update their details, class, fees and family.";
    private static readonly string[] TransportFeeTypeCodes = { "TRANSPORT", "TRANSPORTFEE" };
    private static bool IsMandatoryFeeTypeCode(string? code) =>
        code is not null &&
        MandatoryFeeTypeCodes.Contains(code, StringComparer.OrdinalIgnoreCase);
    private static bool IsTransportFeeType(string? code, string? name)
    {
        string normalizedCode = string.Concat(
            (code ?? string.Empty).Where(char.IsLetterOrDigit));
        if (TransportFeeTypeCodes.Contains(
                normalizedCode,
                StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }
        string normalizedName = (name ?? string.Empty).Trim();
        return string.Equals(normalizedName, "Transport", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedName, "Transport Fee", StringComparison.OrdinalIgnoreCase);
    }
    private sealed class FeeSelectionRow
    {
        public int StudentFeeAssignmentId { get; set; }
        public int FeeTypeId { get; set; }
        public string FeeTypeCode { get; set; } = "";
        public string FeeTypeName { get; set; } = "";
        public decimal StandardAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal NetAmount { get; set; }
        public string Frequency { get; set; } = "";
        public bool IsMandatory { get; set; }
        public bool IsTransportLinked { get; set; }
        public bool HasPayments { get; set; }
    }
    private sealed class FeeConfigOption
    {
        public int FeeTypeId;
        public string FeeTypeCode = "";
        public string FeeTypeName = "";
        public decimal Amount;
        public string Frequency = "";
        public bool IsMandatory;
        public bool IsTransportLinked;
    }
    private readonly BindingList<FeeSelectionRow> _selectedFees = new();
    private List<FeeConfigOption> _availableFeeOptions = new();
    private string _workspaceStudentStatus = string.Empty;
    /* The student's status (Active, Left, New Admission ...). */
    private readonly Label studentStatusBadge = new() {
        Dock = DockStyle.Fill,
        Font = AppTheme.ButtonFont,
        TextAlign = ContentAlignment.MiddleCenter,
        AutoEllipsis = true
    };
    /* Quick find - type an admission no, name or registration no and pick the student to load the record here. It sits in the toolbar beside the Find Student button. */
    private readonly StudentPicker quickStudent = new();
    private readonly Button tab1Button = new ModernButton {
        Text = "Student Information",
        Height = AppMetrics.ButtonHeight,
        Image = UiHelper.CreateGlyphIcon("♙", AppTheme.Primary, 17),
        ImageAlign = ContentAlignment.MiddleLeft,
        TextImageRelation = TextImageRelation.ImageBeforeText
    };
    private readonly Button tab2Button = new ModernButton {
        Text = "Fees & Siblings",
        Height = AppMetrics.ButtonHeight,
        Image = UiHelper.CreateGlyphIcon("Rs", AppTheme.Primary, 17),
        ImageAlign = ContentAlignment.MiddleLeft,
        TextImageRelation = TextImageRelation.ImageBeforeText
    };
    private readonly Panel tab1Panel = new() { Dock = DockStyle.Fill, Visible = true };
    private readonly Panel tab2Panel = new() { Dock = DockStyle.Fill, Visible = false };
    private bool detailsTabBuilt;
    private readonly HashSet<ModernTextBox> suggestionLoadsInFlight = new();
    private readonly HashSet<ModernTextBox> suggestionLoadsCompleted = new();
    /* every limit below comes from SchemaLengths, which is verified against the Students table in the schema baseline. */
    private readonly ModernTextBox fatherName = HumanUi.TextBox(false, SchemaLengths.StudentFatherName);
    private readonly ModernTextBox fatherOccupation = HumanUi.TextBox(false, SchemaLengths.StudentFatherOccupation);
    private readonly ModernComboBox disability = HumanUi.Combo();
    /* Single line, like every other field on the page. */
    private readonly ModernTextBox address = HumanUi.TextBox(false, SchemaLengths.Address);
    private readonly ModernTextBox placeOfBirthInput = HumanUi.TextBox(false, SchemaLengths.StudentPlaceOfBirth);
    private readonly ModernTextBox contact = HumanUi.TextBox(false, SchemaLengths.StudentContactNumber);
    private readonly ModernTextBox altContact = HumanUi.TextBox(false, SchemaLengths.AlternateContactNumber);
    private readonly ModernTextBox name = HumanUi.TextBox(false, SchemaLengths.StudentName);
    private readonly ModernDatePicker dob = HumanUi.Date();
    private readonly ModernComboBox gender = HumanUi.Combo();
    private readonly ModernTextBox bform = HumanUi.TextBox(false, SchemaLengths.BFormNumber);
    private readonly ModernComboBox religion =
    HumanUi.Combo();
    private readonly ModernComboBox blood = HumanUi.Combo();
    private readonly ModernTextBox prevSchool = HumanUi.TextBox(false, SchemaLengths.StudentPreviousSchool);
    private readonly ModernTextBox casteTribe = HumanUi.TextBox(false, SchemaLengths.StudentCasteTribe);
    private readonly ModernDatePicker admissionDate = HumanUi.Date();
    private int admissionNumberVersion;
    private AdmissionNumberPlan? admissionNumberPlan;
    private string automaticAdmissionNumber = string.Empty;
    private AdmissionNumberPlan? existingAdmissionNumberPlan;
    private readonly LinkLabel correctAdmissionNumber = new() {
        Text = "Review", AutoSize = true, Visible = false,
        LinkColor = AppTheme.Danger, Anchor = AnchorStyles.None, Margin = Padding.Empty
    };
    private readonly ModernTextBox registrationNo = HumanUi.TextBox(false, SchemaLengths.RegistrationNumber);
    private readonly ModernTextBox enrolmentNo = HumanUi.TextBox(false, SchemaLengths.BiseEnrolmentNumber);
    private readonly Label admissionIdCaption = new() {
        Dock = DockStyle.Right,
        Width = AppScale.Scale(140),
        Font = AppTheme.HeadingFont,
        ForeColor = AppTheme.HeadingText,
        Text = "Admission Number",
        TextAlign = ContentAlignment.MiddleRight,
        AutoEllipsis = true,
        Padding = new Padding(0, 0, 10, 0)
    };
    private readonly ModernTextBox admissionIdValue = new() {
        Dock = DockStyle.Right,
        Width = AppScale.Scale(180),
        Height = AppMetrics.InputControlHeight,
        MaxLength = 40,
        Font = AppTheme.NormalFont,
        PlaceholderText = string.Empty
    };
    private readonly ModernComboBox transport = HumanUi.Combo();
    private bool UsesTransport =>
        string.Equals(transport.Text, "Yes", StringComparison.OrdinalIgnoreCase);
    private readonly CheckBox assignFirstPlacement = new() { Text = "Record verified first placement", AutoSize = true };
    private readonly ModernComboBox firstPlacementStatus = HumanUi.Combo();
    private readonly ModernDatePicker firstEnrollmentDate = HumanUi.Date();
    private readonly ModernDatePicker firstWithdrawalDate = HumanUi.Date();
    private Control firstPlacementStatusField = null!;
    private Control firstEnrollmentDateField = null!;
    private Control firstWithdrawalDateField = null!;
    private RowStyle placementCardRow = null!;
    private float normalPlacementCardHeight;
    private bool initializingFirstPlacement;
    private bool HistoricalAdmission => currentAdmissionId <= 0 && assignFirstPlacement.Checked;
    private bool AssigningFirstPlacement => currentAdmissionId > 0 && _editEnrollmentId <= 0 && assignFirstPlacement.Checked;
    private TableLayoutPanel placementEditGrid = null!;
    private TableLayoutPanel placementViewGrid = null!;
    private readonly Panel placementHost = new() { Dock = DockStyle.Fill };
    private readonly Label placementGuidance = new() {
        Dock = DockStyle.Fill,
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.Warning,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        Margin = new Padding(0)
    };
    private static ModernTextBox PlacementViewField() => new() {
        Enabled = false,
        ReadOnly = true,
        TabStop = false,
        BackColor = AppTheme.SurfaceSecondary,
        ForeColor = AppTheme.MutedText
    };
    private readonly ModernTextBox viewAdmissionDate = PlacementViewField();
    private readonly ModernTextBox viewEnrollmentDate = PlacementViewField();
    private readonly ModernTextBox editEnrollmentDate = PlacementViewField();
    private readonly ModernTextBox viewRegistrationNo = PlacementViewField();
    private readonly ModernTextBox viewCampus = PlacementViewField();
    private readonly ModernTextBox viewYear = PlacementViewField();
    private Label? viewYearCaption;
    private readonly ModernTextBox viewTerm = PlacementViewField();
    private readonly ModernTextBox viewClass = PlacementViewField();
    private readonly ModernTextBox viewSection = PlacementViewField();
    private readonly ModernTextBox viewTransport = PlacementViewField();
    private readonly PictureBox photoPreview = new() {
        TabStop = false,
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = AppTheme.Surface,
        BorderStyle = BorderStyle.FixedSingle
    };
    private readonly Label photoInfo = new() {
        TabStop = false,
        Dock = DockStyle.Fill,
        AutoSize = false,
        TextAlign = ContentAlignment.TopCenter,
        Font = AppTheme.NormalFont,
        ForeColor = AppTheme.MutedText,
        Margin = new Padding(0, 4, 0, 4)
    };
    private readonly ModernButton choosePhotoButton = new() {
        TakesFocus = false,
        Text = "Choose Photo...",
        Width = AppScale.Scale(150),
        Height = AppMetrics.ButtonHeight,
        Margin = new Padding(0, 2, 0, 4),
        Image = UiHelper.CreateGlyphIcon("＋", AppTheme.Primary, 16),
        ImageAlign = ContentAlignment.MiddleLeft,
        TextImageRelation = TextImageRelation.ImageBeforeText
    };
    private readonly ModernButton removePhotoButton = new() {
        TakesFocus = false,
        Text = "Remove Photo",
        Width = AppScale.Scale(150),
        Height = AppMetrics.ButtonHeight,
        Margin = new Padding(0, 0, 0, 4),
        ThemeRole = "Danger",
        Image = UiHelper.CreateGlyphIcon("×", AppTheme.Danger, 16),
        ImageAlign = ContentAlignment.MiddleLeft,
        TextImageRelation = TextImageRelation.ImageBeforeText
    };
    private readonly StudentPhotoDraft photoDraft = new();
    private byte[]? _pendingPhotoData => photoDraft.Current?.Data;
    private readonly LatestReadScope photoRead = new();
    private readonly Func<int, byte[], string?, string?, Task> writePhoto;
    private readonly Func<int, CancellationToken, Task<DataTable>> readPhoto;
    private bool photoBusy;
    private readonly ModernButton savePhotoButton = new() {
        TakesFocus = false, Text = "Save Photo", ThemeRole = "Secondary", Visible = false,
        Width = AppScale.Scale(150), Height = AppMetrics.ButtonHeight
    };
    private RowStyle? photoSaveRow, photoActionsRow;
    private readonly DataGridView feeGrid = HumanUi.Grid(false);
    private readonly ModernComboBox feePreviewMonth=HumanUi.Combo();
    public bool IsBusy => saveBusy || photoBusy;
    public string BusyMessage => photoBusy ? "Please wait while the student's photo is being prepared or saved." : "Please wait while the admission is being saved.";
    private sealed record FeePreviewMonth(DateTime Month) {
        public override string ToString() => Month.ToString("MMMM yyyy");
    }
    private bool settingFeePreviewMonth;
    private int feePreviewEnrollmentId;
    private readonly Label feeStatus = new() {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = AppTheme.MutedText,
        Font = AppTheme.NormalFont,
        AutoEllipsis = true
    };
    private readonly Button addFeeButton = new ModernButton {
        Text = "+ Add Fee",
        Width = AppScale.Scale(110),
        Height = AppTheme.CompactButtonHeight,
        ThemeRole = "Secondary"
    };
    private readonly ContextMenuStrip addFeeMenu = new StyledContextMenu();
    private readonly Button findSiblingButton = new ModernButton {
        Text = "Find Possible Siblings",
        Width = AppScale.Scale(190),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Primary"
    };
    private readonly Button openSiblingButton = new ModernButton {
        Text = "Open Selected Sibling",
        Width = AppScale.Scale(190),
        Height = AppMetrics.ButtonHeight,
        ThemeRole = "Secondary",
        Enabled = false
    };
    private readonly DataGridView siblingsGrid = HumanUi.Grid(false);
    private readonly Label siblingsStatus = new() {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = AppTheme.MutedText,
        Font = AppTheme.NormalFont,
        AutoEllipsis = true
    };
    private int currentAdmissionId;
    private ModernButton? saveButton;
    private ModernButton? saveAndNewButton;
    private ModernButton? checkAdmissionButton;
    private AdmissionSubmission? unconfirmedAdmission;
    private bool savedAdmissionReadyForNext;
    private readonly ModernCheckBox keepPlacement = new() {
        Text = "Keep placement", Checked = true, AutoSize = true,
        Margin = new Padding(4, 6, 4, 0)
    };
    private bool saveBusy;
    /* UX - inline validation, the post-save "what next" strip, and the toolbar buttons a running save must not be interrupted by. */
    private FormValidator? admissionValidator;
    private ModernButton? findButton;
    private ModernButton? newAdmissionButton;
    private readonly ContextMenuStrip printMenu = new StyledContextMenu();
    private readonly ContextMenuStrip enrollmentActionsMenu = new StyledContextMenu();
    private ModernButton? confirmReturnButton;
    private ModernButton? collectPaymentButton;
    private ModernButton? admissionPrintButton;
    private ModernButton? enrollmentActionsButton;
    /// <summary>The admission form is the largest in the application and had no unsaved-changes protection at all.</summary>
    private List<AdmissionPart>? _admissionBaseline;
    /// <summary>One comparable piece of the form: what it is, for the diagnostic, and the value the operator can change.</summary>
    private readonly record struct AdmissionPart(string Where, string Value);
    /// <summary>At most one report per baseline: HasUnsavedChanges is asked several times on some paths.</summary>
    private bool _admissionDifferenceReported;
    public bool HasUnsavedChanges => unconfirmedAdmission is not null || _pendingPhotoData is not null || HasAdmissionEdits;
    private bool HasAdmissionEdits
    {
        get
        {
            if (_admissionBaseline is null)
                return false;
            List<AdmissionPart> current;
            try
            {
                current = AdmissionParts(pendingPhoto: false);
            }
            catch (Exception nonFatalException47)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException47, "StudentAdmissionPage: AdmissionPart fallback");
                /* A dirty check must never throw, and it must never invent a prompt. An unreadable form is treated as clean - the behaviour before any of this existed. */
                return false;
            }
            string? difference = FirstDifference(_admissionBaseline, current);
            if (difference is null)
                return false;
            ReportFirstDifference(difference);
            return true;
        }
    }
    public string UnsavedChangesMessage => unconfirmedAdmission is { } submitted
        ? $"Admission {submitted.Number} may already have been saved. Check Saved Admission or Find Student before submitting it again.\r\n\r\nLeave this form and discard its entries?"
        : "This admission contains unsaved changes.\r\n\r\nIf you leave now, those changes will be lost.";
    /// <summary>names the first field that differs, once per baseline, in the same log the rest of the application writes to. This makes the next occurrence say which field.</summary>
    private void ReportFirstDifference(string difference)
    {
        if (_admissionDifferenceReported)
            return;
        _admissionDifferenceReported = true;
        try
        {
            AppExceptionHandler.LogNonFatal(
                new InvalidOperationException(
                    "The admission form reports unsaved changes: " + difference +
                    " If the operator did not type anything, this is the field the application moved on its own."),
                nameof(StudentAdmissionPage) + ".HasUnsavedChanges");
        }
        catch (Exception nonFatalException48)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException48, "StudentAdmissionPage: ReportFirstDifference fallback");
            /* Diagnostics must never be the reason a page misbehaves. */
        }
    }
    /// <summary>The first place two field lists disagree, described for the operator's log, or null when they are the same form.</summary>
    private static string? FirstDifference(
        List<AdmissionPart> baseline,
        List<AdmissionPart> current)
    {
        int shared = Math.Min(baseline.Count, current.Count);
        for (int index = 0; index < shared; index++)
        {
            if (!string.Equals(baseline[index].Value, current[index].Value, StringComparison.Ordinal))
            {
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "'{0}' was \"{1}\" and is now \"{2}\" (field {3} of {4}).",
                    current[index].Where,
                    baseline[index].Value,
                    current[index].Value,
                    index + 1,
                    current.Count);
            }
        }
        if (baseline.Count != current.Count)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "the form had {0} readable fields and now has {1}{2}.",
                baseline.Count,
                current.Count,
                current.Count > shared
                    ? ", starting at '" + current[shared].Where + "'"
                    : ", starting at '" + baseline[shared].Where + "'");
        }
        return null;
    }
    private void CaptureAdmissionBaseline()
    {
        try
        {
            /* captured without the pending photo on purpose: a photo that has not been stored yet must keep the form dirty, so leaving with one still prompts. */
            _admissionBaseline =
                AdmissionParts(pendingPhoto: false);
            _admissionDifferenceReported = false;
        }
        catch (Exception nonFatalException49)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException49, "StudentAdmissionPage: CaptureAdmissionBaseline fallback");
            /* A dirty check must never throw, and it must never invent a prompt. */
            _admissionBaseline = null;
        }
    }
    /// <summary>The live form's signature, with the pending photo counted as part of it. The baseline is captured without that part; see the overload below.</summary>
    private string AdmissionSignature()
    {
        var text = new System.Text.StringBuilder();
        foreach (AdmissionPart part in AdmissionParts(pendingPhoto: _pendingPhotoData is not null))
            text.Append(part.Value).Append('|');
        return text.ToString();
    }
    /// <summary>Everything the operator can type or tick, in one comparable string.</summary>
    private List<AdmissionPart> AdmissionParts(bool pendingPhoto)
    {
        var parts =
            new List<AdmissionPart>();
        var signature =
            new System.Text.StringBuilder();
        foreach (Control control in ControlsRecursive(this))
        {
            /* The quick-find box is a search, not part of the admission: what is typed in it is not the student's data. */
            if (IsWithinQuickFind(control) || IsWithinFeePreviewMonth(control))
                continue;
            // This is an entry preference, not part of the student's record.
            if (ReferenceEquals(control, keepPlacement))
                continue;
            /* A field the operator cannot reach cannot hold their work, so its value is recorded as that field type's EMPTY value rather than as what the application has put in… */
            bool editable =
                IsOperatorEditable(control);
            int segmentStart =
                signature.Length;
            switch (control)
            {
                case Label or Button or DataGridView or PictureBox:
                    continue;
                case CheckBox check:
                    signature.Append(check.Checked);
                    break;
                case TextBox text:
                    signature.Append(text.Text);
                    break;
                /* A LookupBox is matched before ModernComboBox on purpose. SelectedId is 0 in both of those states and non-zero only once the operator actually picks something. */
                case LookupBox lookup:
                    signature.Append(lookup.SelectedId);
                    break;
                case ComboBox combo:
                    signature.Append(combo.Text);
                    /* The index only counts when the control actually shows a choice; a list that has just been bound sits on its blank first row, which is not an operator action. */
                    if (!string.IsNullOrWhiteSpace(combo.Text))
                    {
                        signature
                            .Append('~')
                            .Append(combo.SelectedIndex);
                    }
                    break;
                case DateTimePicker date:
                    signature.Append(date.Value.Ticks);
                    /* The date of birth is optional, and the tick is what says whether it is entered at all. */
                    signature
                        .Append('~')
                        .Append(date.Checked);
                    break;
                case ModernTextBox modernText:
                    signature.Append(modernText.Text);
                    break;
                case ModernComboBox modernCombo:
                    signature.Append(modernCombo.Text);
                    if (!string.IsNullOrWhiteSpace(modernCombo.Text))
                    {
                        signature
                            .Append('~')
                            .Append(modernCombo.SelectedIndex);
                    }
                    break;
                case ModernDatePicker modernDate:
                    signature.Append(modernDate.Value.Ticks);
                    signature
                        .Append('~')
                        .Append(modernDate.Checked);
                    break;
                default:
                    continue;
            }
            signature.Append('|');
            /* The switch above is untouched on purpose: each counted control still appends exactly what it always appended. */
            string value =
                signature
                    .ToString(
                        segmentStart,
                        signature.Length - segmentStart)
                    .TrimEnd('|');
            if (!editable)
                value = UnchosenValue(control);
            // The proposal is generated by the application, including the native text box
            // inside ModernTextBox. Manual alternatives and existing-record edits still count.
            if (currentAdmissionId <= 0 && IsWithinAdmissionNumber(control) &&
                string.Equals(admissionIdValue.Text, automaticAdmissionNumber, StringComparison.Ordinal))
                value = string.Empty;
            parts.Add(
                new AdmissionPart(
                    DescribeControl(control),
                    value));
        }
        /* The fee grid: its rows live in _selectedFees, not in the control tree, and the discount cells are edited in place on those same row objects. */
        foreach (FeeSelectionRow fee in _selectedFees.Where(_ => _editEnrollmentId <= 0))
        {
            parts.Add(
                new AdmissionPart(
                    "fee row for fee type " + fee.FeeTypeId,
                    string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0}:{1}:{2}",
                        fee.FeeTypeId,
                        fee.DiscountAmount,
                        fee.NetAmount)));
        }
        parts.Add(
            new AdmissionPart(
                "the pending photo",
                pendingPhoto ? "photo" : "no-photo"));
        return parts;
    }
    /// <summary>True when the operator can actually change this control.</summary>
    private bool IsWithinQuickFind(Control control)
    {
        for (Control? current = control; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, quickStudent))
                return true;
        }
        return false;
    }
    private bool IsWithinAdmissionNumber(Control control)
    {
        for (Control? current = control; current is not null; current = current.Parent)
            if (ReferenceEquals(current, admissionIdValue)) return true;
        return false;
    }
    private bool IsWithinFeePreviewMonth(Control control)
    {
        for (Control? current = control; current is not null; current = current.Parent)
            if (ReferenceEquals(current, feePreviewMonth))
                return true;
        return false;
    }
    private static bool IsOperatorEditable(Control control)
    {
        if (!control.Enabled)
            return false;
        return control switch {
            TextBox text => !text.ReadOnly,
            ModernTextBox modern => !modern.ReadOnly,
            _ => true
        };
    }
    /// <summary>The value a field of this kind reports when nothing has been chosen.</summary>
    private static string UnchosenValue(Control control) =>
        control switch {
            LookupBox => "0",
            CheckBox => bool.FalseString,
            DateTimePicker => string.Empty,
            ModernDatePicker => string.Empty,
            _ => string.Empty
        };
    /// <summary>A short name for a field, for the diagnostic in <see cref="ReportFirstDifference"/> and nothing else.</summary>
    private static string DescribeControl(Control control)
    {
        if (!string.IsNullOrWhiteSpace(control.Name))
            return control.Name;
        try
        {
            if (control.Parent is Control parent)
            {
                foreach (Control sibling in parent.Controls)
                {
                    if (sibling is Label label &&
                        !string.IsNullOrWhiteSpace(label.Text))
                    {
                        return label.Text.Trim().TrimEnd(':') +
                               " (" + control.GetType().Name + ")";
                    }
                }
            }
        }
        catch (Exception nonFatalException50)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException50, "StudentAdmissionPage: DescribeControl fallback");
            /* Diagnostics only - a failure here must not affect the guard. */
        }
        return control.GetType().Name;
    }
    /// <summary>True when it is safe to replace what is on screen. Uses the same confirmation as the navigation guard, so leaving the page and switching student within it behave identically.</summary>
    private bool ConfirmDiscardAdmission()
    {
        if (!HasUnsavedChanges)
            return true;
        return Confirm(
            UnsavedChangesMessage,
            UiText.Dialogs.UnsavedChanges);
    }
    private int _editEnrollmentId;
    private int _editCampusId;
    private int _editYearId;
    private int _editClassId;
    /* The scope the rows in _selectedFees were built for. */
    private int _feeScopeCampusId;
    private int _feeScopeYearId;
    private int _feeScopeClassId;
    private readonly Action<Func<Control>, string, string>? _navigate;
    /// <param name="navigate">The shell's page-switch callback, unchanged.</param>
    private readonly bool registrationReview;
    internal event Action? RegistrationFinished;
    public StudentAdmissionPage(
        Action<Func<Control>, string, string>? navigate = null, int preselectAdmissionId = 0, bool registrationReview = false, bool focusPhoto = false)
        : this(navigate, preselectAdmissionId, registrationReview, focusPhoto, WritePreparedPhotoAsync,
            (id, token) => DatabaseHelper.QueryUserAsync("GetStudentPhoto", token, DatabaseHelper.Int("@AdmissionId", id))) { }

    internal StudentAdmissionPage(Action<Func<Control>, string, string>? navigate, int preselectAdmissionId,
        bool registrationReview, bool focusPhoto, Func<int, byte[], string?, string?, Task> writePhoto,
        Func<int, CancellationToken, Task<DataTable>> readPhoto)
    : base(
        registrationReview ? "Review Registration" : PageTitles.Admissions,
        registrationReview ? "Complete the details, save, then confirm admission. Official numbers are assigned on confirmation." : AdmissionContextSubtitle)
    {
        this.registrationReview = registrationReview;
        this.writePhoto = writePhoto;
        this.readPhoto = readPhoto;
        Disposed += (_, _) => photoRead.Dispose();
        firstPlacementStatus.Items.AddRange(new object[] { "Active", "Left" });
        viewEnrollmentDate.Text=editEnrollmentDate.Text="-";
        RecordTextPolicy.WireInput(name, "@StudentName");
        RecordTextPolicy.WireInput(fatherName, "@FatherName");
        RecordTextPolicy.WireInput(address, "@Address");
        RecordTextPolicy.WireInput(fatherOccupation, "@FatherOccupation");
        RecordTextPolicy.WireInput(prevSchool, "@PreviousSchool");
        RecordTextPolicy.WireInput(placeOfBirthInput, "@PlaceOfBirth");
        RecordTextPolicy.WireInput(casteTribe, "@CasteTribe");
        NationalIdentityInput.Attach(bform);
        firstEnrollmentDate.ShowCheckBox = true;
        firstEnrollmentDate.Checked = false;
        firstWithdrawalDate.ShowCheckBox = true;
        firstWithdrawalDate.Checked = false;
        assignFirstPlacement.CheckedChanged += (_, _) => {
            if (initializingFirstPlacement) return;
            if (currentAdmissionId <= 0)
            {
                feeStructureLoadVersion++;
                if (HistoricalAdmission)
                {
                    _selectedFees.Clear();
                    feeStatus.Text = "Historical record verification does not generate fees.";
                }
                else Defer(LoadFeeStructure);
                feeGrid.Enabled = !HistoricalAdmission;
            }
            ShowPlacementSummary(currentAdmissionId > 0);
        };
        firstPlacementStatus.SelectedIndexChanged += (_, _) => { if (!initializingFirstPlacement) UpdateAdmissionEntryFlow(); };
        UiHelper.DisposePictureImageOnDispose(photoPreview);
        StudentPhotoViewForm.EnableZoom(photoPreview, () => currentAdmissionId > 0
            ? $"{AdmissionNumbers.ForId(currentAdmissionId)} - {name.Text.Trim()}"
            : name.Text.Trim());
        foreach (Button button in new[] { tab1Button, tab2Button, choosePhotoButton, savePhotoButton, removePhotoButton })
            UiHelper.DisposeOwnedButtonImageOnDispose(button);
        _navigate = navigate;
        // This constructor builds by far the largest, most field-dense control tree in the app (student/guardian/address/placement fields, the fee grid, the siblings grid, both tabs).
        SuspendLayout();
        try
        {
            BuildAdmissionForm();
        }
        finally
        {
            ResumeLayout(true);
        }
        FormFlow.EnterMovesToNext(this);
        FormFlow.OrderTableFields(tab1Panel);
        quickStudent.RowFilter = row => Convert.ToString(row["Status"]) != "Pending" && Convert.ToString(row["Status"]) != "Cancelled";
        if (registrationReview)
        {
            quickStudent.Visible = findButton!.Visible = newAdmissionButton!.Visible = saveAndNewButton!.Visible = false;
            enrollmentActionsButton!.Visible = collectPaymentButton!.Visible = admissionPrintButton!.Visible = keepPlacement.Visible = false;
            var confirm = AddBusyAction("Confirm Admission", CompleteRegistrationAsync, "Success");
            GateAction(confirm, "Students.Admit");
            var cancel = AddBusyAction("Cancel Registration", () => CloseAdmissionAsync(true), "Warning");
            GateAction(cancel, "Students.Administer");
            admissionIdCaption.Text = "Temp Reference";
        }
        if (preselectAdmissionId > 0) Defer(async () => {
            await LoadStudentForEditAsync(preselectAdmissionId);
            if (!IsDisposed && focusPhoto && currentAdmissionId == preselectAdmissionId)
            {
                SwitchTab(0);
                if (choosePhotoButton.Enabled)
                {
                    choosePhotoButton.TakesFocus = true;
                    choosePhotoButton.Focus();
                    SetStatus($"Photo · {name.Text} · Choose Photo to resize and save an image within 80 KB.");
                }
                else SetStatus($"Photo · {name.Text} · Your role can view the photo but cannot change it.");
            }
        });
    }
    internal override bool FocusEntryField() => name.Visible && name.Enabled && name.Focus();
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (tab1Panel.Visible && (keyData == Keys.Tab || keyData == (Keys.Tab | Keys.Shift)))
        {
            // Cross the separate details and placement cards in entry order,
            // focusing the native input inside each wrapped control.
            Control[] entryOrder = { altContact, admissionDate, Campus, Year, Class, Section, transport };
            int current = Array.FindIndex(entryOrder, control => control.ContainsFocus);
            if (current >= 0)
            {
                bool forward = keyData == Keys.Tab;
                int step = forward ? 1 : -1;
                for (int next = current + step; next >= 0 && next < entryOrder.Length; next += step)
                    if (MasterEditorFocusNavigator.FocusInside(entryOrder[next])) return true;
                if (forward)
                {
                    SwitchTab(1);
                    if (!MasterEditorFocusNavigator.FocusInside(feeGrid)) tab2Button.Focus();
                    return true;
                }
            }
        }
        return base.ProcessDialogKey(keyData);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            printMenu.Dispose();
            enrollmentActionsMenu.Dispose();
            addFeeMenu.Dispose();
            if (!detailsTabBuilt)
            {
                feeGrid.Dispose();
                feeStatus.Dispose();
                addFeeButton.Dispose();
                siblingsGrid.Dispose();
                siblingsStatus.Dispose();
                findSiblingButton.Dispose();
                openSiblingButton.Dispose();
            }
        }
        base.Dispose(disposing);
    }
    private static int AdmissionFieldRowHeight =>
        Math.Max(16, AppMetrics.TextHeight(UiDesignSystem.Typography.Caption)) +
        AppMetrics.InputControlHeight + UiDesignSystem.Spacing.Xs;

    private static Panel AdmissionField(string caption, Control input, bool required = false)
    {
        var field = HumanUi.Field(caption, input, required);
        ((TableLayoutPanel)field).ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        field.Padding = new Padding(0, 0, UiDesignSystem.Spacing.Sm, UiDesignSystem.Spacing.Xs);
        foreach (Control child in field.Controls) child.Margin = Padding.Empty;
        return field;
    }

    private static TableLayoutPanel AdmissionGrid(int columns, int rows)
    {
        var grid = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows,
            Padding = Padding.Empty, Margin = Padding.Empty
        };
        for (int row = 0; row < rows; row++)
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, AdmissionFieldRowHeight));
        return grid;
    }

    private void BuildAdmissionForm()
    {
        registrationNo.ReadOnly = true;
        registrationNo.Enabled = false;
        registrationNo.BackColor = AppTheme.SurfaceSecondary;
        registrationNo.PlaceholderText = "Assigned after admission";
        /* The number is produced by the save, so the field is never typeable and never needs focus: keeping it out of the tab order leaves the keyboard path as ... */
        registrationNo.TabStop = false;
        enrolmentNo.PlaceholderText =
            "Optional reference number";
        casteTribe.PlaceholderText =
            "Optional";
        religion.Items.AddRange(
    new object[]
    {
        "Islam",
        "Other"
    });
        // BUSINESS RULE: Islam is the intentional quick-entry default for the school's normal
        // intake. Operators can select Other; do not replace this with a blank audit default.
        religion.SelectedIndex = 0;
        gender.Items.Clear();
        gender.Items.AddRange(
            new object[]
            {
            "Male",
            "Female",
            "Other"
            });
        gender.SelectedIndex = -1;
        disability.Items.Clear();
        disability.Items.AddRange(
            new object[]
            {
            "No",
            "Yes"
            });
        disability.SelectedIndex = 0;
        blood.Items.Clear();
        blood.Items.AddRange(
            new object[]
            {
            "Select",
            "A+",
            "A-",
            "B+",
            "B-",
            "AB+",
            "AB-",
            "O+",
            "O-"
            });
        blood.SelectedIndex = 0;
        transport.Items.Clear();
        transport.Items.AddRange(new object[] { "No", "Yes" });
        transport.SelectedIndex = 0;
        ConfigureFieldSuggestions();
        /* Finding a student is a toolbar job, not a panel: the quick-find box and the full finder come first, then the admission actions, then collecting and printing. */
        quickStudent.Dock = DockStyle.None;
        quickStudent.Width = AppScale.Scale(320);
        // Keep the finder width independent of status text and action visibility.
        quickStudent.MinimumSize = new Size(AppScale.Scale(320), 0);
        quickStudent.MaximumSize = new Size(AppScale.Scale(320), 0);
        quickStudent.Margin = new Padding(
            4,
            Math.Max(0, (AppTheme.CompactButtonHeight - AppMetrics.InputControlHeight) / 2),
            4,
            0);
        Actions.Controls.Add(quickStudent);
        findButton = AddBusyAction(
            "Find Student",
            OpenStudentSearchDialog,
            "Primary");
        AddToolbarSeparator();
        /* One reset action clears the current student and starts a fresh admission. */
        newAdmissionButton = AddAction(
            "Clear / New",
            (_, _) => {
                if (saveBusy) return;
                StartNewAdmission();
            }, "Secondary");
        UiAccessibility.Describe(newAdmissionButton, "Clear and start a new admission",
            "Discard current student entries and start a new admission. Placement is retained when Keep placement is selected.");
        /* Save writes the student record (and can create it), so it is guarded against a double submit. Clear is local; the print actions only read. */
        // These actions own their enabled state; a generic busy action must not
        // re-enable Save after an unconfirmed admission reply.
        saveButton = AddAction(UiText.Actions.Save, async (_, _) => await Save(), "Success");
        saveAndNewButton = AddAction("Save & New", async (_, _) => await SaveAndStartNextAsync(), "Success");
        checkAdmissionButton = AddAction("Check Saved Admission", async (_, _) => await RecheckAdmissionAsync(), "Secondary");
        checkAdmissionButton.Visible = false;
        UiAccessibility.Describe(keepPlacement, "Keep placement for the next student",
            "Retain campus, academic year, class and section for Clear / New and Save & New. Student details and discounts are cleared.");
        AddToolbarSeparator();
        /* Collect the fee once the admission is saved, then print - the order the work happens in. */
        foreach (string operation in new[] { "Transfer Section", "Transfer Class", "Transfer Campus", "Re-Admit" })
        {
            var item = enrollmentActionsMenu.Items.Add(operation);
            item.Click += async (_, _) => await OpenEnrollmentActionAsync(operation);
        }
        enrollmentActionsMenu.Opening += (_, _) => {
            foreach (ToolStripItem item in enrollmentActionsMenu.Items)
                item.Enabled = currentAdmissionId > 0 && !saveBusy &&
                    pendingPlacementId == 0 && !IsPendingRegistration && !legacySetupPending &&
                    (item.Text != "Re-Admit" || !string.Equals(_workspaceStudentStatus.Trim(), "Active", StringComparison.OrdinalIgnoreCase)) &&
                    CurrentUser.HasPermission(item.Text == "Re-Admit" ? "Students.Administer" : "Enrollment.Manage");
        };
        enrollmentActionsMenu.Items.Add(new ToolStripSeparator());
        var correctPlacement = enrollmentActionsMenu.Items.Add("Placement History / Corrections");
        correctPlacement.Click += async (_, _) => await OpenPlacementCorrectionAsync();
        enrollmentActionsMenu.Opening += (_, _) => correctPlacement.Enabled = currentAdmissionId > 0 && !IsPendingRegistration && !saveBusy && CurrentUser.HasPermission("Students.Administer");
        var verifyLegacy = enrollmentActionsMenu.Items.Add("Mark Legacy Record Verified");
        verifyLegacy.Click += async (_, _) => await VerifyLegacyAsync();
        var markLeft = enrollmentActionsMenu.Items.Add("Mark as Left");
        markLeft.Click += async (_, _) => await CloseAdmissionAsync(false);
        enrollmentActionsMenu.Opening += (_, _) => {
            verifyLegacy.Visible = legacySetupPending && currentAdmissionId > 0;
            verifyLegacy.Enabled = !saveBusy && CurrentUser.HasPermission("Students.Administer");
            markLeft.Visible = _workspaceStudentStatus == "Active" && currentAdmissionId > 0;
            markLeft.Enabled = !saveBusy && CurrentUser.HasPermission("Students.Administer");
        };
        confirmPlacementAction = enrollmentActionsMenu.Items.Add("Confirm Placement");
        confirmPlacementAction.Visible = false;
        confirmPlacementAction.Click += async (_, _) => await ConfirmPlacementAsync();
        enrollmentActionsMenu.Opening += (_, _) => confirmPlacementAction.Enabled = pendingPlacementId > 0 && !saveBusy &&
            (CurrentUser.HasPermission("Students.Admit") || CurrentUser.HasPermission("Students.Administer"));
        enrollmentActionsButton = AddAction("Actions ▾",
            (_, _) => enrollmentActionsMenu.Show(enrollmentActionsButton!, new Point(0, enrollmentActionsButton!.Height)),
            "Secondary");
        UiAccessibility.Describe(enrollmentActionsButton, "Student placement actions",
            "Transfer section, class or campus, or readmit the saved student.");
        confirmReturnButton = AddBusyAction("Confirm Return", OpenConfirmReturnAsync, "Success");
        confirmReturnButton.Visible = false;
        GateAction(confirmReturnButton, "Students.Administer");
        collectPaymentButton = AddAction(
            "Collect Payment",
            (_, _) => CollectPaymentNow(),
            "Success");
        /* ONE bill, and it shows the real amounts. The parameter stays, so showActualAmount: false brings the parent copy back in one edit. */
        /* UX - Print Bill and Print Form share one "Print" button (1366 x 768 has no room for both), each still one click away in its menu. */
        var printBillItem = printMenu.Items.Add("Print Bill");
        printBillItem.Click += async (_, _) =>
            await RunBusyAsync(
                "Preparing bill…",
                () => PrintAdmissionBill(showActualAmount: true));
        var printFormItem = printMenu.Items.Add("Print Form");
        printFormItem.Click += async (_, _) =>
            await RunBusyAsync(
                "Preparing admission form…",
                PrintAdmissionForm);
        admissionPrintButton = AddAction(
            "Print ▾",
            (_, _) => printMenu.Show(admissionPrintButton!, new Point(0, admissionPrintButton!.Height)),
            "Secondary");
        /* this page is reached with Students.Admit. New Admission and Clear are local and stay. */
        GateActionAny(saveButton, "Students.Admit", "Students.Administer");
        GateAction(saveAndNewButton, "Students.Admit");
        GateAction(collectPaymentButton, "Fees.Collect");
        UseWrappingToolbar();
        SetActionButtonImage("Find Student", "⌕");
        SetActionButtonImage("Clear / New", "＋");
        SetActionButtonImage("Save", "✓");
        SetActionButtonImage("Print ▾", "▣");
        SetActionButtonImage("Collect Payment", "Rs");
        LoadCampuses();
        Campus.SelectedIndexChanged +=
    (_, _) => Defer(LoadFeeStructure);
        Year.SelectedIndexChanged +=
            (_, _) => Defer(LoadFeeStructure);
        Year.SelectedIndexChanged += (_, _) => Defer(ShowNextAdmissionNumberAsync);
        admissionDate.ValueChanged += (_, _) => Defer(ShowNextAdmissionNumberAsync);
        Class.SelectedIndexChanged +=
            (_, _) => Defer(LoadFeeStructure);
        addFeeButton.Click +=
            (_, _) => ShowAddFeeMenu();
        transport.SelectedIndexChanged +=
            (_, _) => TransportChanged();
        findSiblingButton.Click +=
            async (_, _) => await OpenPossibleSiblingSearch();
        openSiblingButton.Click +=
            async (_, _) => await OpenSelectedSiblingAsync();
        siblingsGrid.SelectionChanged +=
            (_, _) => UpdateOpenSiblingAvailability();
        siblingsGrid.CellDoubleClick +=
            async (_, e) => {
                if (e.RowIndex >= 0)
                    await OpenSelectedSiblingAsync();
            };
        siblingsGrid.KeyDown +=
            async (_, e) => {
                if (e.KeyCode != Keys.Enter || SelectedSiblingAdmissionId() <= 0)
                    return;
                e.Handled = true;
                e.SuppressKeyPress = true;
                await OpenSelectedSiblingAsync();
            };
        quickStudent.PlaceholderText = StudentPicker.SearchPlaceholder;
        // The full student list is useful for quick find, but it is not required to paint
        // the admission form. Starting it through the UI queue lets the page appear first.
        Defer(StudentSearchCache.Warm);
        quickStudent.SearchFailed += ex => Fail(ex);
        quickStudent.UserSelectionChanged += async (_, _) => await LoadQuickStudentAsync();
        UpdateStudentWorkspace();
        tab1Button.Click +=
            (_, _) => SwitchTab(0);
        tab2Button.Click +=
            (_, _) => SwitchTab(1);
        choosePhotoButton.Click +=
            async (_, _) => await ChoosePhotoAsync();
        savePhotoButton.Click += async (_, _) => await SavePhotoAsync();
        removePhotoButton.Click +=
            async (_, _) => await RemovePhoto();
        UpdatePhotoActions();
        var studentGuardianCard = HumanUi.Card(
            "Student & Family Information",
            null,
            Accent);
        studentGuardianCard.Padding = new Padding(UiDesignSystem.Spacing.Sm, 3,
            UiDesignSystem.Spacing.Sm, 3);
        studentGuardianCard.Content.Padding = Padding.Empty;
        studentGuardianCard.Header.Padding = new Padding(UiDesignSystem.Spacing.Md, 0,
            UiDesignSystem.Spacing.Md, 0);
        studentGuardianCard.Header.Height = Math.Max(studentGuardianCard.Header.Height,
            AppMetrics.InputControlHeight + UiDesignSystem.Spacing.Sm * 2);
        // Keep the title, status and generated numbers together in the original title strip.
        var headingLabel = studentGuardianCard.Header.Controls.OfType<Label>().First();
        studentGuardianCard.Header.Controls.Clear();
        headingLabel.Dock = DockStyle.Fill;
        headingLabel.Font = AppTheme.BodyBoldFont;
        headingLabel.TextAlign = ContentAlignment.MiddleLeft;
        headingLabel.Margin = Padding.Empty;
        headingLabel.Padding = Padding.Empty;
        var identityRow = new TableLayoutPanel {
            Tag = "ThemeHeaderContainer",
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        foreach (float width in new[] { 30F, 12F, 32F, 26F })
            identityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        identityRow.Controls.Add(headingLabel, 0, 0);
        identityRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var statusHost = new Panel {
            Dock = DockStyle.Fill, Margin = Padding.Empty,
            Padding = new Padding(UiDesignSystem.Spacing.Sm, 0, UiDesignSystem.Spacing.Sm, 0)
        };
        studentStatusBadge.Dock = DockStyle.None;
        studentStatusBadge.Font = AppTheme.BodyBoldFont;
        statusHost.Controls.Add(studentStatusBadge);
        void AlignStatusBadge()
        {
            int width = Math.Max(0, Math.Min(statusHost.ClientSize.Width - statusHost.Padding.Horizontal,
                AppScale.Scale(120)));
            int height = Math.Min(statusHost.ClientSize.Height,
                AppMetrics.TextHeight(studentStatusBadge.Font) + UiDesignSystem.Spacing.Sm);
            studentStatusBadge.SetBounds(statusHost.Padding.Left,
                (statusHost.ClientSize.Height - height) / 2, width, height);
        }
        statusHost.Resize += (_, _) => AlignStatusBadge();
        AlignStatusBadge();
        identityRow.Controls.Add(statusHost, 1, 0);
        var registrationLabel = new Label {
            Dock = DockStyle.Fill, AutoEllipsis = true, Font = AppTheme.BodyBoldFont,
            ForeColor = AppTheme.Text, TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
            Padding = new Padding(UiDesignSystem.Spacing.Sm, 0, UiDesignSystem.Spacing.Sm, 0)
        };
        void UpdateRegistrationLabel() => registrationLabel.Text = "Registration No.: " +
            (string.IsNullOrWhiteSpace(registrationNo.Text) ? "Assigned after admission" : registrationNo.Text);
        registrationNo.TextChanged += (_, _) => UpdateRegistrationLabel();
        UpdateRegistrationLabel();
        identityRow.Controls.Add(registrationLabel, 2, 0);
        var admissionIdentity = new TableLayoutPanel {
            Tag = "ThemeHeaderContainer",
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(UiDesignSystem.Spacing.Sm, 0, 0, 0)
        };
        admissionIdentity.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        admissionIdentity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        admissionIdentity.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        admissionIdCaption.AutoSize = true;
        admissionIdCaption.Dock = DockStyle.Fill;
        admissionIdCaption.Text = "Admission No.";
        admissionIdCaption.Font = AppTheme.NormalFont;
        admissionIdCaption.ForeColor = AppTheme.MutedText;
        admissionIdCaption.Padding = Padding.Empty;
        admissionIdCaption.Margin = new Padding(0, 0, UiDesignSystem.Spacing.Sm, 0);
        admissionIdCaption.TextAlign = ContentAlignment.MiddleLeft;
        admissionIdValue.Dock = DockStyle.None;
        admissionIdValue.Anchor = AnchorStyles.None;
        admissionIdValue.Font = AppTheme.BodyBoldFont;
        admissionIdValue.Height = AppMetrics.InputControlHeight;
        admissionIdValue.MaximumSize = new Size(AppScale.Scale(140), 0);
        admissionIdValue.CornerRadius = 0;
        admissionIdValue.BlendWithParent = true;
        admissionIdValue.TextAlign = HorizontalAlignment.Center;
        admissionIdValue.AccessibleName = "Admission Number";
        admissionIdValue.Margin = Padding.Empty;
        admissionIdentity.Controls.Add(admissionIdCaption, 0, 0);
        var admissionNumberHost = new Panel {
            Dock = DockStyle.Fill, Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        admissionNumberHost.Controls.Add(admissionIdValue);
        void AlignAdmissionNumber()
        {
            int width = Math.Min(AppScale.Scale(140), admissionNumberHost.ClientSize.Width);
            int height = Math.Min(AppMetrics.InputControlHeight, admissionNumberHost.ClientSize.Height);
            admissionIdValue.SetBounds(0, Math.Max(0, (admissionNumberHost.ClientSize.Height - height) / 2),
                Math.Max(0, width), Math.Max(0, height));
        }
        admissionNumberHost.Resize += (_, _) => AlignAdmissionNumber();
        admissionIdentity.Controls.Add(admissionNumberHost, 1, 0);
        AlignAdmissionNumber();
        admissionIdentity.ColumnCount = 3;
        admissionIdentity.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        admissionIdentity.Controls.Add(correctAdmissionNumber, 2, 0);
        admissionIdValue.TextChanged += (_, _) => UpdateAdmissionNumberWarning();
        correctAdmissionNumber.LinkClicked += (_, _) => {
            var plan = existingAdmissionNumberPlan;
            if (plan is null) return;
            string problem = plan.Review(admissionIdValue.Text) ?? "Review this admission number.";
            if (plan.Suggested is not { } suggestion) {
                MessageBox.Show(this, problem + "\n\nNo safe number can be suggested. Review the surrounding admission records; you can leave this number unchanged.",
                    "Admission Number Review", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(this, problem + $"\n\nUse suggested admission number {suggestion}? Save the student to apply the correction.",
                "Admission Number Review", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                admissionIdValue.Text = suggestion;
        };
        identityRow.Controls.Add(admissionIdentity, 3, 0);

        studentGuardianCard.Header.Controls.Add(identityRow);
        var studentGuardianContent = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(2),
            Margin = new Padding(0)
        };
        studentGuardianContent.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                100F));
        studentGuardianContent.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                AppScale.Scale(210)));
        /* Three visual groups share this card: Student Details and Parent / Contact on the left (each under a small heading rule) and Photo on the right. */
        int groupHeadingHeight = Math.Max(16, AppMetrics.TextHeight(UiDesignSystem.Typography.Caption)) + 6;
        int groupGap = UiDesignSystem.Spacing.Sm;
        var informationPanel = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        // Short categorical fields get less width; names and school text keep room.
        var studentInfoGrid = AdmissionGrid(4, 3);
        foreach (RowStyle row in studentInfoGrid.RowStyles) row.Height = AdmissionFieldRowHeight;
        studentInfoGrid.ColumnStyles.Clear();
        foreach (float width in new[] { 30F, 24F, 23F, 23F })
            studentInfoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        studentInfoGrid.Controls.Add(AdmissionField("Student Name", name, true), 0, 0);
        dob.ShowCheckBox = true;
        studentInfoGrid.Controls.Add(AdmissionField("Date of Birth", dob), 1, 0);
        studentInfoGrid.Controls.Add(AdmissionField("Gender", gender, true), 2, 0);
        studentInfoGrid.Controls.Add(AdmissionField("Blood Group", blood), 3, 0);
        studentInfoGrid.Controls.Add(AdmissionField("Place of Birth", placeOfBirthInput, true), 0, 1);
        studentInfoGrid.Controls.Add(AdmissionField("B-Form / CNIC", bform), 1, 1);
        studentInfoGrid.Controls.Add(AdmissionField("Religion", religion), 2, 1);
        studentInfoGrid.Controls.Add(AdmissionField("Disability", disability), 3, 1);
        var previousSchoolField = AdmissionField("Previous School", prevSchool);
        studentInfoGrid.Controls.Add(previousSchoolField, 0, 2);
        studentInfoGrid.SetColumnSpan(previousSchoolField, 2);
        studentInfoGrid.Controls.Add(AdmissionField("Caste / Tribe", casteTribe), 2, 2);
        var enrolmentField = AdmissionField("BISEP No. (Record)", enrolmentNo);
        HumanUi.MuteFieldCaption(enrolmentField);
        studentInfoGrid.Controls.Add(enrolmentField, 3, 2);
        var guardianGrid = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Padding = Padding.Empty,
            Margin = new Padding(0)
        };
        for (int column = 0; column < 4; column++)
        {
            guardianGrid.ColumnStyles.Add(
                new ColumnStyle(
                    SizeType.Percent,
                    25F));
        }
        /* Controls are added in the order the operator moves through them (a TableLayoutPanel's tab order is its add order). */
        for (int row = 0; row < 2; row++)
        {
            guardianGrid.RowStyles.Add(
                new RowStyle(
                    SizeType.Absolute,
                    AdmissionFieldRowHeight));
        }
        var fatherNameField = AdmissionField(
            "Father Name",
            fatherName,
            true);
        guardianGrid.Controls.Add(
            fatherNameField,
            0,
            0);
        guardianGrid.SetColumnSpan(
            fatherNameField,
            2);
        guardianGrid.Controls.Add(
            AdmissionField(
                "Father Occupation",
                fatherOccupation),
            2,
            0);
        ConfigurePhoneInput(contact);
        ConfigurePhoneInput(altContact);
        guardianGrid.Controls.Add(
            AdmissionField(
                "Primary Phone",
                contact,
                registrationReview),
            3,
            0);
        var addressField = AdmissionField(
            "Address",
            address,
            true);
        guardianGrid.Controls.Add(
            addressField,
            0,
            1);
        guardianGrid.SetColumnSpan(
            addressField,
            3);
        guardianGrid.Controls.Add(
            AdmissionField(
                "Alternate Phone",
                altContact),
            3,
            1);
        int studentInfoHeight =
            AdmissionFieldRowHeight * 3;
        int guardianInfoHeight =
            AdmissionFieldRowHeight * 2;
        /* Heading, student rows, heading (with a little air above it), parent rows. */
        int informationHeight =
            studentInfoHeight +
            groupHeadingHeight + groupGap +
            guardianInfoHeight;
        informationPanel.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                0));
        informationPanel.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                studentInfoHeight));
        informationPanel.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                groupHeadingHeight + groupGap));
        informationPanel.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                guardianInfoHeight));
        informationPanel.Controls.Add(
            studentInfoGrid,
            0,
            1);
        informationPanel.Controls.Add(
            HumanUi.GroupHeading("Parent / Contact"),
            0,
            2);
        informationPanel.Controls.Add(
            guardianGrid,
            0,
            3);
        var photoPanel = new TableLayoutPanel {
            TabStop = false,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(AppMetrics.SpacingSm, 0, AppMetrics.SpacingSm, 2),
            Margin = new Padding(0)
        };
        photoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F));
        photoPanel.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                groupHeadingHeight));
        photoPanel.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                120));
        photoActionsRow = new RowStyle(SizeType.Absolute, 2 * (AppMetrics.ButtonHeight + 4));
        photoPanel.RowStyles.Add(photoActionsRow);
        photoPanel.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));
        var photoCaption =
            HumanUi.GroupHeading("Photo");
        photoCaption.TabStop = false;
        photoPreview.Dock = DockStyle.Fill;
        photoPreview.Margin = new Padding(0,4,0,4);
        photoPreview.SizeMode =
            PictureBoxSizeMode.Zoom;
        var photoButtons = new TableLayoutPanel {
            TabStop = false,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = Padding.Empty,
            Margin = new Padding(0)
        };
        photoButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F));
        photoButtons.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight + 4));
        photoSaveRow = new RowStyle(SizeType.Absolute, 0);
        photoButtons.RowStyles.Add(photoSaveRow);
        photoButtons.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonHeight + 4));
        choosePhotoButton.Dock = savePhotoButton.Dock = removePhotoButton.Dock = DockStyle.Fill;
        choosePhotoButton.Margin = savePhotoButton.Margin = removePhotoButton.Margin = new Padding(0,2,0,2);
        // Photo controls stay outside the Tab / Enter path from details to placement.
        photoButtons.Controls.Add(choosePhotoButton,0,0);
        photoButtons.Controls.Add(savePhotoButton,0,1);
        photoButtons.Controls.Add(removePhotoButton,0,2);
        UpdatePhotoActions();
        photoPanel.Controls.Add(
            photoCaption,
            0,
            0);
        photoPanel.Controls.Add(
            photoPreview,
            0,
            1);
        photoPanel.Controls.Add(
            photoButtons,
            0,
            2);
        photoPanel.Controls.Add(
            photoInfo,
            0,
            3);
        studentGuardianContent.Controls.Add(
            informationPanel,
            0,
            0);
        studentGuardianContent.Controls.Add(
            photoPanel,
            1,
            0);
        studentGuardianCard.Content.Controls.Add(
            studentGuardianContent);
        var placementHeader = new Panel {
            Width = AppScale.Scale(180), Margin = Padding.Empty, Padding = Padding.Empty
        };
        keepPlacement.Dock = DockStyle.Right;
        placementHeader.Controls.Add(keepPlacement);
        var admissionCard = HumanUi.Card("Admission / Placement", null, Accent, placementHeader);
        admissionCard.Padding = new Padding(UiDesignSystem.Spacing.Sm, 3,
            UiDesignSystem.Spacing.Sm, 3);
        admissionCard.Content.Padding = Padding.Empty;
        placementHeader.SendToBack();
        keepPlacement.Margin = Padding.Empty;
        var placementTitle = admissionCard.Header.Controls.OfType<Label>().First();
        admissionCard.Header.Controls.Remove(placementTitle);
        var placementTitleRow = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        placementTitleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        placementTitleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        placementTitleRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        placementTitle.AutoSize = true;
        placementTitle.Dock = DockStyle.Fill;
        placementTitle.TextAlign = ContentAlignment.MiddleLeft;
        placementTitle.Margin = Padding.Empty;
        placementGuidance.Dock = DockStyle.Fill;
        placementGuidance.Padding = new Padding(UiDesignSystem.Spacing.Md, 0, UiDesignSystem.Spacing.Sm, 0);
        placementTitleRow.Controls.Add(placementTitle, 0, 0);
        placementTitleRow.Controls.Add(placementGuidance, 1, 0);
        admissionCard.Header.Controls.Add(placementTitleRow);
        placementTitleRow.BringToFront();
        placementEditGrid = AdmissionGrid(4, 3);
        placementEditGrid.ColumnStyles.Clear();
        foreach (float width in new[] { 25F, 25F, 25F, 25F })
            placementEditGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        placementEditGrid.RowStyles[0].Height = AdmissionFieldRowHeight;
        placementEditGrid.RowStyles[2].Height = 0;
        placementEditGrid.Controls.Add(AdmissionField("First Admission", admissionDate, true), 0, 0);
        placementEditGrid.Controls.Add(AdmissionField("Placement Start Date", editEnrollmentDate), 1, 0);
        placementEditGrid.Controls.Add(AdmissionField("Campus", Campus, true), 2, 0);
        placementEditGrid.Controls.Add(AdmissionField("Academic Year", Year, true), 3, 0);
        placementEditGrid.Controls.Add(AdmissionField("Class", Class, true), 0, 1);
        placementEditGrid.Controls.Add(AdmissionField("Section", Section, true), 1, 1);
        placementEditGrid.Controls.Add(AdmissionField("Transport", transport), 2, 1);
        firstPlacementStatusField = AdmissionField("Verified status", firstPlacementStatus, true);
        firstEnrollmentDateField = AdmissionField("Placement Start Date", firstEnrollmentDate, true);
        firstWithdrawalDateField = AdmissionField("Withdrawal date (if known)", firstWithdrawalDate);
        placementEditGrid.Controls.Add(firstPlacementStatusField, 0, 2);
        placementEditGrid.Controls.Add(firstEnrollmentDateField, 1, 2);
        placementEditGrid.Controls.Add(firstWithdrawalDateField, 2, 2);
        placementViewGrid = AdmissionGrid(4, 2);
        placementViewGrid.ColumnStyles.Clear();
        foreach (ColumnStyle column in placementEditGrid.ColumnStyles)
            placementViewGrid.ColumnStyles.Add(new ColumnStyle(column.SizeType, column.Width));
        placementViewGrid.RowStyles[0].Height = AdmissionFieldRowHeight;
        placementViewGrid.Visible = false;
        placementViewGrid.Controls.Add(AdmissionField("First Admission", viewAdmissionDate), 0, 0);
        placementViewGrid.Controls.Add(AdmissionField("Placement Start Date", viewEnrollmentDate), 1, 0);
        placementViewGrid.Controls.Add(AdmissionField("Campus", viewCampus), 2, 0);
        var enrollmentYearField = AdmissionField("Current Academic Year", viewYear);
        viewYearCaption = enrollmentYearField.Controls.OfType<Label>().FirstOrDefault();
        placementViewGrid.Controls.Add(enrollmentYearField, 3, 0);
        placementViewGrid.Controls.Add(AdmissionField("Class", viewClass), 0, 1);
        placementViewGrid.Controls.Add(AdmissionField("Section", viewSection), 1, 1);
        placementViewGrid.Controls.Add(AdmissionField("Transport", viewTransport), 2, 1);
        placementHost.Controls.Add(
            placementViewGrid);
        placementHost.Controls.Add(
            placementEditGrid);
        placementHost.Margin = Padding.Empty;
        var placementContent = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        placementContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        placementContent.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.TextHeight(AppTheme.NormalFont)));
        placementContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        var placementHelp = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        assignFirstPlacement.Dock = DockStyle.Right;
        placementHelp.Controls.Add(assignFirstPlacement);
        placementContent.Controls.Add(placementHelp, 0, 0);
        placementContent.Controls.Add(placementHost, 0, 1);
        admissionCard.Content.Controls.Add(placementContent);
        // Keep the primary entry form compact; the secondary tab is built on demand.
        var tab1Root = new TableLayoutPanel {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        tab1Root.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                100F));
        int studentGuardianCardHeight = studentGuardianCard.Header.Height + informationHeight +
            studentGuardianContent.Padding.Vertical + studentGuardianCard.Padding.Vertical;
        int admissionCardHeight = admissionCard.Header.Height + AppMetrics.TextHeight(AppTheme.NormalFont) +
            AdmissionFieldRowHeight * 2 + admissionCard.Padding.Vertical;
        tab1Root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                studentGuardianCardHeight));
        tab1Root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                admissionCardHeight));
        placementCardRow = tab1Root.RowStyles[1];
        normalPlacementCardHeight = admissionCardHeight;
        studentGuardianCard.Margin =
            Padding.Empty;
        admissionCard.Margin =
            new Padding(0);
        tab1Root.Controls.Add(
            studentGuardianCard,
            0,
            0);
        tab1Root.Controls.Add(
            admissionCard,
            0,
            1);
        var tab1Scroll = new Panel {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(
                0,
                0,
                0,
                0),
            Margin = new Padding(0)
        };
        tab1Scroll.Controls.Add(
            tab1Root);
        tab1Panel.Controls.Add(
            tab1Scroll);
        // Fees & Siblings is the secondary step. Its cards and grids are built on the
        // first visit to that tab so the primary admission form can paint sooner.
        var tabBar = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        tabBar.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                50));
        tabBar.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                50));
        tab1Button.Dock = DockStyle.Fill;
        tab2Button.Dock = DockStyle.Fill;
        tabBar.Controls.Add(
            tab1Button,
            0,
            0);
        tabBar.Controls.Add(
            tab2Button,
            1,
            0);
        var tabHost = new Panel {
            Dock = DockStyle.Fill,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        tabHost.Controls.Add(tab1Panel);
        tabHost.Controls.Add(tab2Panel);
        var rightPanel = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        rightPanel.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                AppMetrics.ButtonHeight));
        rightPanel.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));
        rightPanel.Controls.Add(
            tabBar,
            0,
            0);
        rightPanel.Controls.Add(
            tabHost,
            0,
            1);
        SetContent(rightPanel);
        name.TextChanged += (_, _) => UpdateAdmissionEntryFlow();
        gender.SelectedIndexChanged += (_, _) => UpdateAdmissionEntryFlow();
        fatherName.TextChanged += (_, _) => UpdateAdmissionEntryFlow();
        address.TextChanged += (_, _) => UpdateAdmissionEntryFlow();
        placeOfBirthInput.TextChanged += (_, _) => UpdateAdmissionEntryFlow();
        SwitchTab(0);
        ShowPlacementSummary(false);
        ClearAll();
    }
    private void SetActionButtonImage(string text, string glyph)
    {
        foreach (Control control in ControlsRecursive(this))
        {
            if (control is ModernButton button &&
                string.Equals(button.Text, text, StringComparison.Ordinal))
            {
                Image? oldImage = button.Image;
                button.Image = UiHelper.CreateGlyphIcon(glyph, AppTheme.Primary, 17);
                UiHelper.DisposeOwnedButtonImageOnDispose(button);
                oldImage?.Dispose();
                button.ImageAlign = ContentAlignment.MiddleLeft;
                button.TextImageRelation = TextImageRelation.ImageBeforeText;
                button.Padding = new Padding(9, 0, 9, 0);
            }
        }
    }
    private static IEnumerable<Control> ControlsRecursive(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in ControlsRecursive(child))
                yield return nested;
        }
    }
    private void ConfigurePhoneInput(ModernTextBox control) =>
        PhoneNumberInput.Attach(control, () => admissionValidator ??= CreateValidator());
    private void ConfigureFeeGrid()
    {
        feeGrid.AutoGenerateColumns = false;
        feeGrid.ReadOnly = false;
        feeGrid.AllowUserToAddRows = false;
        feeGrid.AllowUserToDeleteRows = false;
        feeGrid.AllowUserToResizeRows = false;
        feeGrid.MultiSelect = false;
        feeGrid.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;
        feeGrid.EditMode =
            DataGridViewEditMode.EditOnEnter;
        feeGrid.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.None;
        feeGrid.Columns.Clear();
        feeGrid.CellBeginEdit += (_, e) => {
            if (_editEnrollmentId > 0 && feeGrid.Columns[e.ColumnIndex].Name == "DiscountAmount")
                e.Cancel = true;
        };
        feeGrid.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "StudentFeeAssignmentId",
                DataPropertyName = "StudentFeeAssignmentId",
                Visible = false,
                ReadOnly = true
            });
        feeGrid.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "FeeTypeId",
                DataPropertyName = "FeeTypeId",
                Visible = false,
                ReadOnly = true
            });
        feeGrid.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "IsMandatory",
                DataPropertyName = "IsMandatory",
                Visible = false,
                ReadOnly = true
            });
        feeGrid.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "IsTransportLinked",
                DataPropertyName = "IsTransportLinked",
                Visible = false,
                ReadOnly = true
            });
        feeGrid.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "HasPayments",
                DataPropertyName = "HasPayments",
                Visible = false,
                ReadOnly = true
            });
        feeGrid.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "FeeTypeName",
                HeaderText = "Fee Type",
                DataPropertyName = "FeeTypeName",
                ReadOnly = true,
                FillWeight = 26
            });
        feeGrid.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "StandardAmount",
                HeaderText = "Standard Fee",
                DataPropertyName = "StandardAmount",
                ReadOnly = true,
                FillWeight = 18,
                DefaultCellStyle =
                {
                Alignment =
                    DataGridViewContentAlignment.MiddleRight,
                Format = "N0"
                }
            });
        feeGrid.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "DiscountAmount",
                HeaderText = "Discount (Rs.)",
                DataPropertyName = "DiscountAmount",
                ReadOnly = false,
                FillWeight = 18,
                DefaultCellStyle =
                {
                Alignment =
                    DataGridViewContentAlignment.MiddleRight,
                Format = "N0"
                }
            });
        feeGrid.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "NetAmount",
                HeaderText = "Net Fee",
                DataPropertyName = "NetAmount",
                ReadOnly = true,
                FillWeight = 18,
                DefaultCellStyle =
                {
                Alignment =
                    DataGridViewContentAlignment.MiddleRight,
                Format = "N0"
                }
            });
        feeGrid.Columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "Frequency",
                HeaderText = "Frequency",
                DataPropertyName = "Frequency",
                ReadOnly = true,
                FillWeight = 16
            });
        feeGrid.Columns.Add(
            CreateCrossActionColumn(
                "Remove",
                34,
                false));
        /* The admission sends typed discounts with its fee selections so its opening charges are posted at the discounted amount. */
        feeGrid.CellValidating +=
            FeeGrid_CellValidating;
        feeGrid.CellValueChanged +=
            FeeGrid_CellValueChanged;
        feeGrid.CellFormatting +=
            FeeGrid_CellFormatting;
        feeGrid.CellContentClick +=
            FeeGrid_CellContentClick;
    }
    private void FeeGrid_CellFormatting(
        object? sender,
        DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 ||
            feeGrid.Columns[e.ColumnIndex].Name != "Remove")
        {
            return;
        }
        DataGridViewRow row = feeGrid.Rows[e.RowIndex];
        bool isMandatory = Convert.ToBoolean(
            row.Cells["IsMandatory"].Value ?? false);
        bool isTransportLinked = Convert.ToBoolean(
            row.Cells["IsTransportLinked"].Value ?? false);
        bool hasPayments = Convert.ToBoolean(
            row.Cells["HasPayments"].Value ?? false);
        bool isLocked = isMandatory ||
            hasPayments ||
            (isTransportLinked && UsesTransport);
        /* Every other fee on this list is governed by Fee Configuration - it is added, changed or withdrawn there. */
        bool canRemove = isTransportLinked && !isLocked;
        var cell = (DataGridViewButtonCell)row.Cells["Remove"];
        if (canRemove)
        {
            cell.Value = "✕";
            cell.ReadOnly = false;
            cell.ToolTipText = "Remove the transport fee from this admission";
        }
        else
        {
            cell.Value = "";
            cell.ReadOnly = true;
            cell.ToolTipText = hasPayments
                ? "This fee already has a posted charge. In Fee Collection, right-click the unpaid charge and choose 'Reverse this charge…'. If money was collected, review it in Student Fee Corrections first."
                : isMandatory
                    ? "This fee is mandatory and can't be removed here."
                    : isTransportLinked
                        ? "Uncheck \"Uses Transport\" to remove the transport fee."
                        : "Fees are managed in Fee Setup. Only the transport fee can be removed here.";
        }
    }
    private void FeeGrid_CellContentClick(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 ||
            feeGrid.Columns[e.ColumnIndex].Name != "Remove")
        {
            return;
        }
        FeeSelectionRow? row = feeGrid.Rows[e.RowIndex].DataBoundItem as FeeSelectionRow;
        if (row is null ||
            !row.IsTransportLinked ||
            row.IsMandatory ||
            row.HasPayments ||
            (row.IsTransportLinked && UsesTransport))
        {
            /* !row.IsTransportLinked is the rule, not a safety net: only the transport fee offers a ✕. */
            return;
        }
        /* RemoveFeeRow is now async Task. */
        _ = RemoveFeeRow(row);
    }
    private async Task RemoveFeeRow(FeeSelectionRow row)
    {
        if (row.StudentFeeAssignmentId > 0)
        {
            /* The ✕ removes a SAVED fee assignment from the student's record, and that cannot be undone, so it names the fee and asks first. */
            if (!Confirm(
                    $"Delete the saved fee assignment \"{row.FeeTypeName}\" " +
                    $"(Rs. {row.NetAmount:N0}) from this student?\r\n\r\n" +
                    "This action will remove the selected record.",
                    "Delete Fee Assignment"))
            {
                return;
            }
            try
            {
                await DatabaseHelper.ExecuteUserAsync(
                    "DeleteStudentFeeAssignment",
                    DatabaseHelper.Int(
                        "@StudentFeeAssignmentId",
                        row.StudentFeeAssignmentId));
                await LoadStudentFeeAssignmentsAsync(_editEnrollmentId);
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
            return;
        }
        _selectedFees.Remove(row);
        RefreshFeeStatus();
    }
    private void ShowAddFeeMenu()
    {
        addFeeMenu.Items.Clear();
        var alreadyAdded = new HashSet<int>(
            _selectedFees.Select(f => f.FeeTypeId));
        var remaining = _availableFeeOptions
            .Where(o => !alreadyAdded.Contains(o.FeeTypeId))
            .ToList();
        if (remaining.Count == 0)
        {
            var none = addFeeMenu.Items.Add(
                "All configured fees are already on this student's list");
            none.Enabled = false;
        }
        else
        {
            foreach (var option in remaining)
            {
                var item = addFeeMenu.Items.Add(
                    $"{option.FeeTypeName}  (Rs. {option.Amount:N0})");
                item.Click += async (_, _) => await AddFeeRow(option);
            }
        }
        addFeeMenu.Show(
            addFeeButton,
            new Point(0, -addFeeMenu.PreferredSize.Height));
    }
    private async Task AddFeeRow(FeeConfigOption option)
    {
        if (_editEnrollmentId > 0 && currentAdmissionId > 0)
        {
            try
            {
                int feeEnrollmentId=_editEnrollmentId;
                await Task.Run(() => StudentFeeAssignmentAdd.Save(feeEnrollmentId,option.FeeTypeId));
                await LoadStudentFeeAssignmentsAsync(_editEnrollmentId);
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
            return;
        }
        _selectedFees.Add(new FeeSelectionRow {
            FeeTypeId = option.FeeTypeId,
            FeeTypeCode = option.FeeTypeCode,
            FeeTypeName = option.FeeTypeName,
            StandardAmount = option.Amount,
            DiscountAmount = 0,
            NetAmount = option.Amount,
            Frequency = option.Frequency,
            IsMandatory = option.IsMandatory,
            IsTransportLinked = option.IsTransportLinked
        });
        RefreshFeeStatus();
    }
    private void RefreshFeeStatus()
    {
        feeStatus.Text = _selectedFees.Count == 0
            ? "No fees selected yet - use + Add Fee below."
            : $"{_selectedFees.Count} fee item(s) selected.";
    }
    private void FeeGrid_CellValidating(
        object? sender,
        DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0 ||
            e.ColumnIndex < 0 ||
            feeGrid.Columns[e.ColumnIndex].Name != "DiscountAmount")
        {
            return;
        }
        string text = Convert.ToString(e.FormattedValue)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            e.Cancel = true;
            feeGrid.Rows[e.RowIndex]
                .ErrorText = "Enter a discount amount.";
            return;
        }
        if (!decimal.TryParse(
                text,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture,
                out decimal discount) || !WholeRupees.IsWhole(discount))
        {
            e.Cancel = true;
            feeGrid.Rows[e.RowIndex]
                .ErrorText = "Enter a valid whole-rupee discount amount.";
            return;
        }
        decimal amount = Convert.ToDecimal(
            feeGrid.Rows[e.RowIndex]
                .Cells["StandardAmount"]
                .Value);
        if (discount < 0)
        {
            e.Cancel = true;
            feeGrid.Rows[e.RowIndex]
                .ErrorText = "Discount cannot be negative.";
            return;
        }
        if (discount > amount)
        {
            e.Cancel = true;
            feeGrid.Rows[e.RowIndex]
                .ErrorText =
                    $"Discount cannot exceed Rs. {amount:N0}.";
            return;
        }
        feeGrid.Rows[e.RowIndex].ErrorText = string.Empty;
    }
    private void FeeGrid_CellValueChanged(
    object? sender,
    DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 ||
            e.ColumnIndex < 0 ||
            feeGrid.Columns[e.ColumnIndex].Name != "DiscountAmount")
        {
            return;
        }
        DataGridViewRow row = feeGrid.Rows[e.RowIndex];
        if (!decimal.TryParse(
                Convert.ToString(row.Cells["StandardAmount"].Value),
                out decimal amount))
        {
            return;
        }
        if (!decimal.TryParse(
                Convert.ToString(row.Cells["DiscountAmount"].Value),
                out decimal discount) || !WholeRupees.IsWhole(discount))
        {
            return;
        }
        row.Cells["NetAmount"].Value =
            Math.Max(0, amount - discount);
    }
    private void SwitchTab(int index)
    {
        if (index == 1)
            EnsureDetailsTabBuilt();
        tab1Panel.Visible = index == 0;
        tab2Panel.Visible = index == 1;
        ApplyTabTheme(tab1Button, index == 0);
        ApplyTabTheme(tab2Button, index == 1);
    }
    private void EnsureDetailsTabBuilt()
    {
        if (detailsTabBuilt || IsDisposed)
            return;
        detailsTabBuilt = true;
        tab2Panel.SuspendLayout();
        try
        {
            ConfigureFeeGrid();
            siblingsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            feeGrid.DataSource = _selectedFees;

            var feeCard = HumanUi.Card(
                "Fee Structure",
                "Latest assignments; earlier charges remain in Fee Collection.",
                Accent);
            int previewStatusRowHeight =
                AppMetrics.TextHeight(AppTheme.NormalFont) + AppMetrics.SpacingSm;
            var feeLayout = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = AdmissionDetailsCardPadding,
                Margin = Padding.Empty
            };
            feeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, AppScale.Scale(48)));
            feeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            feeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
            feePreviewMonth.Enabled=false;
            feePreviewMonth.SelectedIndexChanged += async (_, _) => {
                if(!settingFeePreviewMonth && _editEnrollmentId>0 && feePreviewMonth.SelectedItem is FeePreviewMonth selected)
                    await LoadStudentFeeAssignmentsAsync(_editEnrollmentId,selected.Month);
            };
            feeStatus.AutoEllipsis=false;
            feeStatus.Font = AppTheme.SmallFont;
            feeLayout.Controls.Add(feeStatus, 0, 0);
            feeLayout.Controls.Add(feeGrid, 0, 1);
            var manageFees = new ModernButton {
                Text = "Fee Discounts & Transport",
                AutoSize = true,
                ThemeRole = "Secondary"
            };
            manageFees.Click += async (_, _) => {
                if (_editEnrollmentId <= 0 || currentAdmissionId <= 0) {
                    ModernMessageDialog.Show(this, "Save the admission first, then select the student to manage their fees.",
                        "Student Fees", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (saveBusy || !CurrentUser.HasPermission("Fees.Adjust")) return;
                using var dialog = new FeeAssignmentsDialog(_editEnrollmentId, currentAdmissionId,
                    AdmissionNumbers.ForId(currentAdmissionId));
                dialog.ShowDialog(this);
                if (dialog.ChangesMade) await LoadStudentFeeAssignmentsAsync(_editEnrollmentId);
            };

            var feeButtonRow = HumanUi.ButtonRow(addFeeButton, manageFees);
            feeButtonRow.Dock = DockStyle.Left;
            feeLayout.Controls.Add(feeButtonRow, 0, 2);
            feeCard.Content.Controls.Add(feeLayout);

            var siblingsCard = HumanUi.Card(
                "Current Siblings",
                "Siblings already linked to this student.",
                Accent);
            var siblingsLayout = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = AdmissionDetailsCardPadding,
                Margin = Padding.Empty
            };
            siblingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, previewStatusRowHeight));
            siblingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            siblingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, AppMetrics.ButtonRowHeight));
            siblingsLayout.Controls.Add(siblingsStatus, 0, 0);
            siblingsLayout.Controls.Add(siblingsGrid, 0, 1);
            siblingsLayout.Controls.Add(
                HumanUi.ButtonRow(findSiblingButton, openSiblingButton),
                0,
                2);
            siblingsCard.Content.Controls.Add(siblingsLayout);

            var tab2Root = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            tab2Root.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tab2Root.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tab2Root.Controls.Add(feeCard, 0, 0);
            tab2Root.Controls.Add(siblingsCard, 0, 1);
            var tab2Scroll = new Panel {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(
                    0,
                    0,
                    SystemInformation.VerticalScrollBarWidth + AppMetrics.SpacingSm,
                    AppMetrics.SpacingSm),
                Margin = Padding.Empty
            };
            tab2Scroll.Controls.Add(tab2Root);
            tab2Panel.Controls.Add(tab2Scroll);
            FormFlow.OrderTableFields(tab2Panel);
        }
        finally
        {
            tab2Panel.ResumeLayout(true);
        }
    }
    private static void ApplyTabTheme(Button button, bool active)
    {
        TabStrip.ApplyButtonTheme(button, active);
    }
    private void ShowPlacementSummary(bool editing)
    {
        bool missing = currentAdmissionId > 0 && _editEnrollmentId <= 0;
        bool locked = editing && !missing;
        placementEditGrid.Visible = !locked;
        placementViewGrid.Visible = locked;
        registrationNo.Visible = !locked;
        viewRegistrationNo.Visible = locked;
        assignFirstPlacement.Visible = missing || currentAdmissionId <= 0;
        assignFirstPlacement.Text = currentAdmissionId <= 0 ? "Historical record (no fees)" : "Record verified first placement";
        bool review = missing || HistoricalAdmission;
        firstPlacementStatusField.Visible = review;
        firstEnrollmentDateField.Visible = review;
        firstWithdrawalDateField.Visible = review;
        placementEditGrid.RowStyles[2].Height = review ? AdmissionFieldRowHeight : 0;
        if (placementCardRow is not null)
            placementCardRow.Height = normalPlacementCardHeight + (review ? AdmissionFieldRowHeight : 0);
        admissionDate.Enabled = currentAdmissionId <= 0;
        UpdateAdmissionEntryFlow();
    }
    /// <summary>A thin divider between groups of toolbar controls.</summary>
    private void AddToolbarSeparator()
    {
        int height = Math.Max(16, AppTheme.CompactButtonHeight - 12);
        Actions.Controls.Add(new Panel {
            Width = 1,
            Height = height,
            BackColor = AppTheme.Border,
            Margin = new Padding(
                4,
                Math.Max(0, (AppTheme.CompactButtonHeight - height) / 2),
                4,
                0)
        });
    }
    /// <summary>Shows the student's status in the Student &amp; Family Information header: "New Admission" until a student is loaded, then the stored status, coloured by what it says…</summary>
    private void UpdateStudentWorkspace()
    {
        UpdatePhotoActions();
        bool savedStudentReady = currentAdmissionId > 0 && !IsBusy;
        if (collectPaymentButton is not null)
            collectPaymentButton.Enabled = savedStudentReady && _navigate is not null && CurrentUser.HasPermission("Fees.Collect");
        if (admissionPrintButton is not null)
            admissionPrintButton.Enabled = savedStudentReady;
        if (enrollmentActionsButton is not null)
            enrollmentActionsButton.Enabled = savedStudentReady &&
                (CurrentUser.HasPermission("Students.Admit") || CurrentUser.HasPermission("Enrollment.Manage") || CurrentUser.HasPermission("Students.Administer"));
        findSiblingButton.Enabled = savedStudentReady && CurrentUser.HasPermission("Students.Administer");
        if (saveAndNewButton is not null)
            saveAndNewButton.Enabled = !IsBusy && currentAdmissionId <= 0 && unconfirmedAdmission is null;
        if (saveButton is not null) saveButton.Enabled = !IsBusy && unconfirmedAdmission is null;
        if (checkAdmissionButton is not null)
        {
            checkAdmissionButton.Visible = unconfirmedAdmission is not null;
            checkAdmissionButton.Enabled = !IsBusy;
        }
        string text;
        Color color;
        if (unconfirmedAdmission is { } submitted)
        {
            text = $"Admission {submitted.Number} · Check save outcome";
            color = AppTheme.Warning;
        }
        else if (currentAdmissionId <= 0)
        {
            text = "New Admission";
            color = AppTheme.Primary;
        }
        else
        {
            text = string.IsNullOrWhiteSpace(_workspaceStudentStatus)
                ? "Loaded"
                : _workspaceStudentStatus.Trim();
            string lower = text.ToLowerInvariant();
            if (lower.Contains("inactive") ||
                lower.Contains("left") ||
                lower.Contains("struck") ||
                lower.Contains("withdraw") ||
                lower.Contains("transfer") ||
                lower.Contains("passed"))
            {
                color = AppTheme.Warning;
            }
            else if (lower.Contains("active") ||
                     lower.Contains("enrol") ||
                     lower == "loaded")
            {
                color = AppTheme.Success;
            }
            else
            {
                color = AppTheme.Primary;
            }
        }
        studentStatusBadge.Text = text;
        studentStatusBadge.ForeColor = AppTheme.StatusText(color);
        studentStatusBadge.BackColor = AppTheme.Tint(color, 0.88f);
    }
    private async Task OpenStudentSearchDialog()
    {
        try
        {
            using var dialog =
                new StudentSearchDialog(this, officialAdmissionsOnly:true);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }
            /* loading another student replaces everything on the form. Asked here rather than before the dialog opens, so merely looking at the search results never prompts. */
            if (!ConfirmDiscardAdmission())
                return;
            if (!string.IsNullOrWhiteSpace(dialog.NewAdmissionNumber))
            {
                StartNewAdmission();
                admissionIdValue.Text = dialog.NewAdmissionNumber;
                name.Focus();
                return;
            }
            if (dialog.SelectedAdmissionId > 0)
                await LoadStudentForEditAsync(dialog.SelectedAdmissionId);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    /// <summary>Quick find: loads the student picked in the toolbar box. Like the full finder, the operator is asked about unsaved work only once a student has been chosen.</summary>
    private async Task LoadQuickStudentAsync()
    {
        try
        {
            int admissionId = quickStudent.SelectedId;
            if (admissionId <= 0)
                return;
            if (admissionId == currentAdmissionId)
            {
                SetStatus("That student is already loaded.");
                return;
            }
            if (!ConfirmDiscardAdmission())
            {
                quickStudent.Clear();
                return;
            }
            await LoadStudentForEditAsync(admissionId);
            /* The load may have been refused or failed, or another load may have won. */
            if (currentAdmissionId != admissionId)
                quickStudent.Clear();
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    private void StartNewAdmission()
    {
        if (unconfirmedAdmission is not null && !ConfirmDiscardAdmission()) return;
        pendingPlacementId = 0;
        if (confirmPlacementAction is not null) confirmPlacementAction.Visible = false;
        currentAdmissionId = 0;
        _workspaceStudentStatus = string.Empty;
        _editEnrollmentId = 0;
        _editCampusId = 0;
        _editYearId = 0;
        _editClassId = 0;
        ShowPlacementSummary(false);
        ClearAll(true, keepPlacement.Checked);
        UpdateStudentWorkspace();
        SwitchTab(0);
        SetStatus(
            keepPlacement.Checked && Campus.SelectedId > 0
                ? "New · Placement kept"
                : "New admission");
    }
    private void UpdateAdmissionIdDisplay()
    {
        automaticAdmissionNumber = string.Empty;
        existingAdmissionNumberPlan = null;
        UpdateAdmissionNumberWarning();
        if (currentAdmissionId > 0)
        {
            admissionIdValue.Text = AdmissionNumbers.ForId(currentAdmissionId);
            admissionIdValue.PlaceholderText = string.Empty;
            if (!IsPendingRegistration) Defer(CheckExistingAdmissionNumberAsync);
        }
        else
        {
            admissionIdValue.Clear();
            admissionIdValue.PlaceholderText = string.Empty;
            Defer(ShowNextAdmissionNumberAsync);
        }
    }
    private void UpdateAdmissionNumberWarning()
    {
        bool warning = currentAdmissionId > 0 && existingAdmissionNumberPlan is { } plan && plan.Review(admissionIdValue.Text) is not null;
        admissionIdValue.ForeColor = warning ? AppTheme.Danger : AppTheme.Text;
        correctAdmissionNumber.Visible = warning;
        admissionIdValue.AccessibleDescription = warning ? "Admission number does not fit the original admission date. Review is optional." : string.Empty;
    }
    private async Task CheckExistingAdmissionNumberAsync()
    {
        int id = currentAdmissionId;
        if (id <= 0 || IsDisposed) return;
        try {
            var plan = await AdmissionNumberPolicy.LoadExistingAsync(id);
            if (IsDisposed || currentAdmissionId != id) return;
            existingAdmissionNumberPlan = plan;
            UpdateAdmissionNumberWarning();
        }
        catch (Exception ex) { if (!IsDisposed && currentAdmissionId == id) Fail(ex); }
    }
    private async Task ShowNextAdmissionNumberAsync()
    {
        if(currentAdmissionId>0 || IsDisposed || saveBusy) return;
        int version = ++admissionNumberVersion;
        DateTime date=admissionDate.Value.Date;
        int yearId=Year.SelectedId;
        admissionNumberPlan=null;
        try
        {
            if (yearId <= 0)
            {
                string? preview = await AdmissionNumberPolicy.PreviewNextAsync();
                if (IsDisposed || saveBusy || version != admissionNumberVersion || currentAdmissionId > 0 || date != admissionDate.Value.Date || yearId != Year.SelectedId) return;
                automaticAdmissionNumber = preview ?? string.Empty;
                admissionIdValue.Text = automaticAdmissionNumber;
                admissionIdValue.PlaceholderText = string.Empty;
                SetStatus("Select an academic year for the admission number.");
                return;
            }
            var plan=await AdmissionNumberPolicy.LoadAsync(date,yearId,HistoricalAdmission);
            if(IsDisposed || saveBusy || version!=admissionNumberVersion || currentAdmissionId>0 || date!=admissionDate.Value.Date || yearId!=Year.SelectedId) return;
            admissionNumberPlan=plan;
            // Refresh the proposal on date/year changes, keeping the final number editable.
            automaticAdmissionNumber = plan.Suggested ?? string.Empty;
            admissionIdValue.Text = automaticAdmissionNumber;
            admissionIdValue.PlaceholderText=string.Empty;
            if(plan.Error is not null) SetStatus(plan.Error,true);
            else if(HistoricalAdmission) SetStatus(plan.Suggested is not null
                ? $"Suggested: {plan.Suggested}. Verify with the register."
                : "Enter the verified register admission number.");
            else if(plan.Suggested is not null) SetStatus($"Suggested admission no.: {plan.Suggested}. Edit if needed.");
            else SetStatus("Enter an unused register number for this date.",true);
        }
        catch (Exception ex)
        {
            if (!IsDisposed && version == admissionNumberVersion && currentAdmissionId == 0) Fail(ex);
        }
    }
    private void ConfigureFieldSuggestions()
    {
        foreach (var (field, databaseField) in new[]
        {
            (fatherOccupation, "FatherOccupation"),
            (prevSchool, "PreviousSchool"),
            (address, "Address"),
            (placeOfBirthInput, "PlaceOfBirth"),
            (casteTribe, "CasteTribe")
        })
        {
            field.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            field.AutoCompleteSource = AutoCompleteSource.CustomSource;
            field.AutoCompleteCustomSource = new AutoCompleteStringCollection();
            field.Enter += async (_, _) =>
                await EnsureFieldSuggestionsLoadedAsync(field, databaseField);
        }
    }
    private async Task EnsureFieldSuggestionsLoadedAsync(
        ModernTextBox field,
        string databaseField)
    {
        if (suggestionLoadsCompleted.Contains(field) ||
            !suggestionLoadsInFlight.Add(field))
        {
            return;
        }
        try
        {
            DataTable table = await DatabaseHelper.QueryUserAsync(
                "GetStudentFieldSuggestions",
                DatabaseHelper.P("@Field", databaseField));
            if (IsDisposed || field.IsDisposed)
                return;
            var source = new AutoCompleteStringCollection();
            foreach (DataRow row in table.Rows)
            {
                string? value = Convert.ToString(row["Value"]);
                if (!string.IsNullOrWhiteSpace(value))
                    source.Add(value);
            }
            field.AutoCompleteCustomSource = source;
            suggestionLoadsCompleted.Add(field);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"{nameof(EnsureFieldSuggestionsLoadedAsync)} failed: {ex.Message}");
        }
        finally
        {
            suggestionLoadsInFlight.Remove(field);
        }
    }
    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value!;
        }
        return string.Empty;
    }
    private static void AddSuggestionIfNew(ModernTextBox field, string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return;
        if (field.AutoCompleteCustomSource.Cast<string>()
                .Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        field.AutoCompleteCustomSource.Add(value);
    }
    /* invalidates an in-flight fee-assignment load. */
    private int feeAssignmentsLoadVersion;
    private async Task LoadStudentFeeAssignmentsAsync(int enrollmentId,DateTime? requestedMonth=null)
    {
        int version = ++feeAssignmentsLoadVersion;
        feeGrid.DataSource = null;
        _selectedFees.Clear();
        if (enrollmentId <= 0)
        {
            feeStatus.Text =
                "No active placement is available for fee assignments.";
            return;
        }
        try
        {
            var placement = await Task.Run(() => DatabaseHelper.QuerySql("""
                EXEC dbo.RequirePermission @UserId,N'Students.View';
                SELECT Status FROM dbo.Enrollments WHERE EnrollmentId=@EnrollmentId;
                """,DatabaseHelper.Int("@UserId",CurrentUser.UserId),DatabaseHelper.Int("@EnrollmentId",enrollmentId)));
            if(IsDisposed || version!=feeAssignmentsLoadVersion || _editEnrollmentId!=enrollmentId) return;
            if(placement.Rows.Count>0 && string.Equals(Convert.ToString(placement.Rows[0]["Status"]),"Left",StringComparison.OrdinalIgnoreCase))
            {
                feeGrid.DataSource=_selectedFees;
                feeGrid.ReadOnly=true;
                feeStatus.Text="Student has left. Fee history and outstanding balances remain in Fee Collection.";
                viewTransport.Text="No";
                return;
            }
            DataTable table =
                await DatabaseHelper.QueryUserAsync(
                    "GetStudentFeeAssignments",
                    DatabaseHelper.Int(
                        "@EnrollmentId",
                        enrollmentId));
            /* The enrollment this reply is for must still be the one being edited. */
            if (IsDisposed ||
                version != feeAssignmentsLoadVersion ||
                _editEnrollmentId != enrollmentId)
            {
                return;
            }
            feeGrid.ReadOnly = false;
            DateTime currentMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
            if(table.Columns.Contains("LastBilledMonth"))
                foreach(DataRow assignment in table.Rows)
                    if(assignment["LastBilledMonth"]!=DBNull.Value && Convert.ToDateTime(assignment["LastBilledMonth"])>currentMonth)
                        currentMonth=Convert.ToDateTime(assignment["LastBilledMonth"]);
            if(requestedMonth.HasValue) currentMonth=requestedMonth.Value;
            else if(feePreviewEnrollmentId==enrollmentId && feePreviewMonth.SelectedItem is FeePreviewMonth selected)
                currentMonth=selected.Month;
            string transportStatus=await Task.Run(() => EnrollmentTransportStatus.Read(enrollmentId));
            if(IsDisposed || version!=feeAssignmentsLoadVersion || _editEnrollmentId!=enrollmentId) return;
            viewTransport.Text=transportStatus;
            SetFeePreviewMonths(table,enrollmentId,currentMonth);
            var currentFees = FeeAssignmentPresentation.Latest(table);
            foreach (DataRow row in currentFees)
            {
                string feeTypeCode = table.Columns.Contains("FeeTypeCode") &&
                        row["FeeTypeCode"] != DBNull.Value
                    ? Convert.ToString(row["FeeTypeCode"]) ?? ""
                    : "";
                string feeTypeName = Convert.ToString(row["FeeTypeName"]) ?? "";
                _selectedFees.Add(new FeeSelectionRow {
                    StudentFeeAssignmentId = Convert.ToInt32(
                        row["StudentFeeAssignmentId"]),
                    FeeTypeId = table.Columns.Contains("FeeTypeId") && row["FeeTypeId"] != DBNull.Value
                        ? Convert.ToInt32(row["FeeTypeId"])
                        : 0,
                    FeeTypeCode = feeTypeCode,
                    FeeTypeName = feeTypeName,
                    StandardAmount = Convert.ToDecimal(row["StandardAmount"]),
                    DiscountAmount = row["DiscountAmount"] == DBNull.Value
                        ? 0m
                        : Convert.ToDecimal(row["DiscountAmount"]),
                    NetAmount = row["NetAmount"] == DBNull.Value
                        ? Convert.ToDecimal(row["StandardAmount"])
                        : Convert.ToDecimal(row["NetAmount"]),
                    Frequency = row["Frequency"] == DBNull.Value
                        ? ""
                        : Convert.ToString(row["Frequency"]) ?? "",
                    IsMandatory = IsMandatoryFeeTypeCode(feeTypeCode),
                    IsTransportLinked = IsTransportFeeType(feeTypeCode, feeTypeName),
                    HasPayments = table.Columns.Contains("HasPayments") &&
                        row["HasPayments"] != DBNull.Value &&
                        Convert.ToBoolean(row["HasPayments"])
                });
            }
            feeGrid.DataSource = _selectedFees;
            if (feeGrid.Columns.Contains(
                    "DiscountAmount"))
            {
                feeGrid.Columns[
                    "DiscountAmount"].ReadOnly = false;
            }
            feeStatus.Text = _selectedFees.Count == 0 ? "No fee assignments." : FeeAssignmentPresentation.CompactSummary(table, currentMonth);
            feeGrid.ClearSelection();
            await LoadAvailableFeeOptionsAsync(
                _editCampusId,
                _editYearId,
                _editClassId);
        }
        catch (Exception ex)
        {
            feeGrid.DataSource = null;
            feeStatus.Text =
                "Unable to load student fee assignments.";
            Fail(ex);
        }
    }
    internal static Task<DataTable> PreviewStudentFeesAsync(int enrollmentId, DateTime month) =>
        FeeAssignmentPosting.PendingThroughAsync(enrollmentId, month);
    private void SetFeePreviewMonths(DataTable table,int enrollmentId,DateTime selected)
    {
        DateTime first=new(DateTime.Today.Year,DateTime.Today.Month,1);
        DateTime last=first.AddMonths(24);
        foreach(DataRow row in table.Rows)
            foreach(string column in new[]{"EffectiveFrom","EffectiveTo","LastBilledMonth"})
                if(table.Columns.Contains(column) && row[column]!=DBNull.Value)
                {
                    DateTime value=Convert.ToDateTime(row[column]);
                    if(value<first) first=value;
                    if(value>last) last=value;
                }
        if(selected<first) first=selected;
        if(selected>last) last=selected;
        settingFeePreviewMonth=true;
        try
        {
            feePreviewMonth.Items.Clear();
            for(DateTime month=first;month<=last;month=month.AddMonths(1)) feePreviewMonth.Items.Add(new FeePreviewMonth(month));
            feePreviewMonth.SelectedItem=feePreviewMonth.Items.Cast<FeePreviewMonth>().First(item=>item.Month==selected);
            feePreviewMonth.Enabled=true;
            feePreviewEnrollmentId=enrollmentId;
        }
        finally { settingFeePreviewMonth=false; }
    }
    private static string FeeWaiverSummary(DataTable table,DateTime month)
    {
        var notes=new List<string>();
        foreach(DataRow row in table.Rows)
        {
            if(Convert.ToDecimal(row["StandardAmount"])<=0 || Convert.ToDecimal(row["NetAmount"])!=0
                || row["EffectiveFrom"]==DBNull.Value
                || row["EffectiveTo"]!=DBNull.Value && Convert.ToDateTime(row["EffectiveTo"])<month) continue;
            DateTime start=Convert.ToDateTime(row["EffectiveFrom"]);
            string period=row["EffectiveTo"]==DBNull.Value ? $"from {start:MMMM yyyy}"
                : $"{start:MMM yyyy}–{Convert.ToDateTime(row["EffectiveTo"]):MMM yyyy}";
            string note=$"{row["FeeTypeName"]}: No fee {period}";
            if(row["EffectiveTo"]!=DBNull.Value)
            {
                DateTime resume=Convert.ToDateTime(row["EffectiveTo"]).AddMonths(1);
                DataRow? next=table.AsEnumerable().FirstOrDefault(r=>Equals(r["FeeTypeId"],row["FeeTypeId"])
                    && r["EffectiveFrom"]!=DBNull.Value && Convert.ToDateTime(r["EffectiveFrom"])==resume);
                if(next!=null) note+=$"; Rs. {Convert.ToDecimal(next["NetAmount"]):N0} from {resume:MMM yyyy}";
            }
            notes.Add(note+".");
        }
        return string.Join("  |  ",notes);
    }
    private async Task LoadAvailableFeeOptionsAsync(int campusId, int yearId, int classId)
    {
        _availableFeeOptions = new List<FeeConfigOption>();
        if (campusId <= 0 || yearId <= 0 || classId <= 0)
            return;
        try
        {
            var configurationsTask = DatabaseHelper.QueryUserAsync(
                "GetFeeConfigurations",
                DatabaseHelper.Int("@CampusId", campusId),
                DatabaseHelper.Int("@ClassId", classId));
            var feeTypesTask = DatabaseHelper.QueryUserAsync("GetFeeTypes");
            await Task.WhenAll(configurationsTask, feeTypesTask);
            var raw = await configurationsTask;
            var feeTypes = await feeTypesTask;
            var feePositions = feeTypes.AsEnumerable().ToDictionary(r => Convert.ToInt32(r["FeeTypeId"]),
                r => r.IsNull("DisplayOrder") ? int.MaxValue : Convert.ToInt32(r["DisplayOrder"]));
            foreach (DataRow row in raw.AsEnumerable().OrderBy(r =>
                feePositions.GetValueOrDefault(Convert.ToInt32(r["FeeTypeId"]), int.MaxValue)))
            {
                if (raw.Columns.Contains("Frequency") &&
                    string.Equals(Convert.ToString(row["Frequency"]), "As Needed",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue; // Purchases are added as saved charges in Fee Collection.
                }
                string feeTypeCode = raw.Columns.Contains("FeeTypeCode") &&
                        row["FeeTypeCode"] != DBNull.Value
                    ? Convert.ToString(row["FeeTypeCode"]) ?? ""
                    : "";
                string feeTypeName = Convert.ToString(row["FeeTypeName"]) ?? "";
                _availableFeeOptions.Add(new FeeConfigOption {
                    FeeTypeId = Convert.ToInt32(row["FeeTypeId"]),
                    FeeTypeCode = feeTypeCode,
                    FeeTypeName = feeTypeName,
                    Amount = row["Amount"] == DBNull.Value
                        ? 0m
                        : Convert.ToDecimal(row["Amount"]),
                    Frequency = row["Frequency"] == DBNull.Value
                        ? ""
                        : Convert.ToString(row["Frequency"]) ?? "",
                    IsMandatory = IsMandatoryFeeTypeCode(feeTypeCode),
                    IsTransportLinked = IsTransportFeeType(feeTypeCode, feeTypeName)
                });
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    /// <summary>The fee-discount lines this save will send, validated but not yet written. They travel as one table-valued parameter into dbo.SaveStudentDetailsWithFirstEnrollment, which saves details and discounts atomically and can also create a verified first enrollment.</summary>
    private bool TryBuildFeeDiscountPlan(
        out List<(int assignmentId, decimal discount)> feeDiscounts)
    {
        feeDiscounts = new List<(int assignmentId, decimal discount)>();
        // Saved enrollments use dated discounts rather than rewriting their fee history on admission save.
        if (_editEnrollmentId > 0) return true;
        if (feeGrid.Rows.Count == 0)
            return true;
        try
        {
            feeGrid.EndEdit();
            foreach (DataGridViewRow row in feeGrid.Rows)
            {
                if (row.IsNewRow)
                    continue;
                object? assignmentValue =
                    row.Cells["StudentFeeAssignmentId"].Value;
                if (assignmentValue is null ||
                    assignmentValue == DBNull.Value)
                {
                    continue;
                }
                int assignmentId =
                    Convert.ToInt32(assignmentValue);
                if (assignmentId <= 0)
                    continue;
                object? discountValue =
                    row.Cells["DiscountAmount"].Value;
                decimal discount = 0;
                if (discountValue is not null &&
                    discountValue != DBNull.Value &&
                    !string.IsNullOrWhiteSpace(
                        Convert.ToString(discountValue)))
                {
                    if (!decimal.TryParse(
                            Convert.ToString(discountValue),
                            System.Globalization.NumberStyles.Number,
                            System.Globalization.CultureInfo.CurrentCulture,
                            out discount) || !WholeRupees.IsWhole(discount))
                    {
                        Warn("One or more fee discounts are invalid. Enter whole rupees.");
                        return false;
                    }
                }
                feeDiscounts.Add((assignmentId, discount));
            }
            return true;
        }
        catch (Exception ex)
        {
            Fail(ex);
            return false;
        }
    }
    /* Item 6 - the religion the loaded student has stored when it is not one of the values this list offers. */
    private string? _loadedReligionNotInList;
    private string? ReligionToSave() =>
        religion.SelectedIndex >= 0
            ? religion.Text.Trim()
            : _loadedReligionNotInList;
    /// <summary>The discounts typed on a NEW admission, keyed by fee type: at that moment no fee assignment exists yet, so the rows carry no StudentFeeAssignmentId to key on.</summary>
    private bool TryBuildTypedDiscountsByFeeType(
        out List<(int feeTypeId, decimal discount)> typedDiscounts)
    {
        typedDiscounts = new List<(int feeTypeId, decimal discount)>();
        if (feeGrid.Rows.Count == 0)
            return true;
        try
        {
            feeGrid.EndEdit();
            foreach (DataGridViewRow row in feeGrid.Rows)
            {
                if (row.IsNewRow)
                    continue;
                object? feeTypeValue =
                    row.Cells["FeeTypeId"].Value;
                if (feeTypeValue is null ||
                    feeTypeValue == DBNull.Value)
                {
                    continue;
                }
                int feeTypeId =
                    Convert.ToInt32(feeTypeValue);
                if (feeTypeId <= 0)
                    continue;
                object? discountValue =
                    row.Cells["DiscountAmount"].Value;
                if (discountValue is null ||
                    discountValue == DBNull.Value ||
                    string.IsNullOrWhiteSpace(
                        Convert.ToString(discountValue)))
                {
                    continue;
                }
                if (!decimal.TryParse(
                        Convert.ToString(discountValue),
                        System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.CurrentCulture,
                        out decimal discount) || !WholeRupees.IsWhole(discount))
                {
                    Warn("One or more fee discounts are invalid. Enter whole rupees.");
                    return false;
                }
                if (discount > 0)
                    typedDiscounts.Add((feeTypeId, discount));
            }
            return true;
        }
        catch (Exception ex)
        {
            Fail(ex);
            return false;
        }
    }
    /// <summary>UX - the admission's own rules, checked all at once and shown on the fields themselves (danger-coloured border plus a few words on the caption).</summary>
    private bool ValidateAdmissionInline(bool isNew, bool confirmingRegistration = false)
    {
        FormValidator v = admissionValidator ??= CreateValidator();
        v.Begin();
        bool validNumber() => IsPendingRegistration || AdmissionNumbers.TryNormalize(admissionIdValue.Text, out _) &&
            (!isNew || admissionNumberPlan?.Validate(admissionIdValue.Text) is null && admissionNumberPlan is not null);
        v.Check(admissionIdValue, validNumber(), isNew ? admissionNumberPlan?.Validate(admissionIdValue.Text) ?? "Wait for the admission number suggestion, then enter a valid number." : "Use a number such as 1 or 1a", validNumber);
        if(isNew && admissionNumberPlan?.Error is { } dateError)
            v.Check(admissionDate,false,dateError);
        static string NameProblem(string text)
        {
            string trimmed = text.Trim();
            return trimmed.Length == 0
                ? "Required"
                : trimmed.Length > SchemaLengths.StudentName
                    ? $"Max {SchemaLengths.StudentName} characters"
                    : string.Empty;
        }
        v.Check(name, NameProblem(name.Text).Length == 0,
            NameProblem(name.Text), () => NameProblem(name.Text).Length == 0);
        bool detailsComplete = true;
        if (isNew || confirmingRegistration)
        {
            detailsComplete = HasRequiredAdmissionDetails();
            v.Required(gender, "Select one");
            v.Required(fatherName);
            v.Required(address);
            v.Required(placeOfBirthInput);
            if (confirmingRegistration) v.Required(contact);
        }
        v.Check(dob,
            !(dob.Checked && dob.Value.Date > admissionDate.Value.Date),
            "Later than admission date",
            () => !(dob.Checked && dob.Value.Date > admissionDate.Value.Date));
        if ((isNew && detailsComplete) || AssigningFirstPlacement)
        {
            v.Required(Campus, "Select a campus");
            v.Required(Year, "Select a year");
            v.Required(Class, "Select a class");
            v.Required(Section, "Select a section");
        }
        if (AssigningFirstPlacement || HistoricalAdmission)
        {
            v.Required(firstPlacementStatus, "Choose Active or Left");
            v.Check(firstEnrollmentDate, firstEnrollmentDate.Checked && firstEnrollmentDate.Value.Date >= admissionDate.Value.Date && firstEnrollmentDate.Value.Date <= DateTime.Today,
                "Confirm a date on/after admission, not future");
            bool left = firstPlacementStatus.Text == "Left";
            v.Check(firstPlacementStatus, _workspaceStudentStatus != "Left" || left, "Use Left for a historical left student");
            v.Check(firstWithdrawalDate, !firstWithdrawalDate.Checked || (left && firstWithdrawalDate.Value.Date >= firstEnrollmentDate.Value.Date && firstWithdrawalDate.Value.Date <= DateTime.Today),
                "Withdrawal must follow placement, not future");
        }
        v.Check(contact, IsValidOptionalPhone(contact.Text),
            PakistanPhoneNumber.ValidationMessage, () => IsValidOptionalPhone(contact.Text));
        v.Check(altContact, IsValidOptionalPhone(altContact.Text),
            PakistanPhoneNumber.ValidationMessage, () => IsValidOptionalPhone(altContact.Text));
        v.Check(bform, IsValidOptionalBFormOrCnic(bform.Text),
            "Enter 13 digits, with or without dashes", () => IsValidOptionalBFormOrCnic(bform.Text));
        if (!isNew)
        {
            /* The load leaves this combo unselected when the stored value is not Yes or No, and SaveStudentDetails refuses anything else. */
            v.Required(disability, "Choose Yes or No");
        }
        RecordTextPolicy.ValidateInputs(v, this);
        return v.Finish();
    }
    private bool HasRequiredAdmissionDetails() =>
        !string.IsNullOrWhiteSpace(name.Text) &&
        gender.SelectedIndex >= 0 &&
        !string.IsNullOrWhiteSpace(fatherName.Text) &&
        !string.IsNullOrWhiteSpace(address.Text) &&
        !string.IsNullOrWhiteSpace(placeOfBirthInput.Text);
    private void UpdateAdmissionEntryFlow()
    {
        bool missing = currentAdmissionId > 0 && _editEnrollmentId <= 0;
        bool placementUnlocked = currentAdmissionId <= 0 ? HasRequiredAdmissionDetails() : missing && assignFirstPlacement.Checked;
        firstPlacementStatus.Enabled = AssigningFirstPlacement || HistoricalAdmission;
        firstEnrollmentDate.Enabled = AssigningFirstPlacement || HistoricalAdmission;
        firstWithdrawalDate.Enabled = (AssigningFirstPlacement || HistoricalAdmission) && firstPlacementStatus.Text == "Left";
        assignFirstPlacement.Enabled = CurrentUser.HasPermission("Enrollment.Manage");
        Campus.Enabled = placementUnlocked;
        Year.Enabled = placementUnlocked;
        Class.Enabled = placementUnlocked;
        Section.Enabled = placementUnlocked;
        transport.Enabled = placementUnlocked;
        placementGuidance.Text = HistoricalAdmission
            ? "Historical record: confirm the register number, placement and dates. No fees are generated."
            : missing ? "No placement: tick verified first placement, then confirm placement and dates. No fees are generated."
            : currentAdmissionId > 0
                ? "Use Actions → Placement History / Corrections to review placement."
                : placementUnlocked ? "Choose Campus, Academic Year, Class and Section in order."
                    : "Complete the required student and family fields above to unlock academic placement.";
        placementGuidance.ForeColor = placementUnlocked
            ? AppTheme.MutedText
            : AppTheme.Warning;
    }
    private static int GetIntColumn(DataRow row, string columnName)
    {
        if (!row.Table.Columns.Contains(columnName) ||
            row[columnName] == DBNull.Value ||
            row[columnName] is null)
        {
            return 0;
        }
        return int.TryParse(
            Convert.ToString(row[columnName]),
            out int value)
            ? value
            : 0;
    }
    /* The load generation for the edit form. */
    private int admissionLoadVersion;
    private async Task<bool> LoadStudentForEditAsync(int admissionId)
    {
        pendingPlacementId = -1; // Fail closed while placement confirmation state is loading.
        if (confirmPlacementAction is not null) confirmPlacementAction.Visible = false;
        if (confirmReturnButton is not null) confirmReturnButton.Visible = false;
        int version = ++admissionLoadVersion;
        photoRead.Cancel();
        /* Item 11 - a photo chosen but not yet saved belongs to the record this load is replacing. */
        if (admissionId != currentAdmissionId)
        {
            /* Re-loading the same student keeps a prepared photo available for Save Photo or retry. */
            photoDraft.Discard();
        }
        /* Item 6 - and the stored-religion carry-over belongs to the previous student too. */
        _loadedReligionNotInList = null;
        /* Item 3 - only a load that actually finished, on the student it was started for, may replace the placement pickers with the read-only summary. */
        bool loadedThisStudent = false;
        try
        {
            if (admissionId <= 0)
            {
                Warn("The selected student is invalid.");
                return false;
            }
            DataTable dt =
                await DatabaseHelper.QueryUserAsync(
                    "GetStudentDetails",
                    DatabaseHelper.Int(
                        "@AdmissionId",
                        admissionId));
            if (dt.Rows.Count == 0)
            {
                Alert(
                    "The selected student could not be loaded.");
                return false;
            }
            /* The form may have been cleared, or another student loaded, while this query was running. */
            if (IsDisposed ||
                version != admissionLoadVersion)
            {
                return false;
            }
            DataRow row = dt.Rows[0];
            if (!registrationReview && Convert.ToString(row["Status"]) == "Pending")
            {
                if (!QuickRegistrationAccess.CanUse(CurrentUser.HasPermission)) Warn("Your role needs admission and student-view access to edit or confirm this quick registration.");
                else if (_navigate is not null) _navigate(() => new QuickRegistrationPage(_navigate, admissionId), "Students", "Quick Registration");
                else Warn("This is a pending registration. Review and confirm it in Dashboard Quick Registration.");
                return false;
            }
            unconfirmedAdmission = null;
            legacySetupPending = row.Table.Columns.Contains("LegacySetupPending") && Convert.ToBoolean(row["LegacySetupPending"]);
            currentAdmissionId =
                admissionId;
            _workspaceStudentStatus =
                row.Table.Columns.Contains("Status") &&
                row["Status"] != DBNull.Value
                    ? Convert.ToString(row["Status"]) ?? string.Empty
                    : string.Empty;
            UpdateAdmissionIdDisplay();
            name.Text =
                Convert.ToString(
                    row["StudentName"])
                ?? string.Empty;
            fatherName.Text =
                row["FatherName"] == DBNull.Value
                    ? string.Empty
                    : Convert.ToString(
                        row["FatherName"])
                      ?? string.Empty;
            fatherOccupation.Text =
                row["FatherOccupation"] == DBNull.Value
                    ? string.Empty
                    : Convert.ToString(
                        row["FatherOccupation"])
                      ?? string.Empty;
            /* Each combo is reset first, and a stored value the list does not offer is named in the status line for the operator to decide. */
            var valuesNotInList = new List<string>();
            string disabilityValue =
                row["Disability"] == DBNull.Value
                    ? "No"
                    : Convert.ToString(
                        row["Disability"])
                      ?? "No";
            disability.SelectedIndex = -1;
            int disabilityIndex =
                disability.FindStringExact(
                    disabilityValue.Trim());
            if (disabilityIndex >= 0)
            {
                disability.SelectedIndex = disabilityIndex;
            }
            else
            {
                valuesNotInList.Add(
                    $"disability \"{disabilityValue.Trim()}\"");
            }
            contact.Text =
                row["ContactNumber"] == DBNull.Value
                    ? string.Empty
                    : Convert.ToString(
                        row["ContactNumber"])
                      ?? string.Empty;
            altContact.Text =
                row["AlternateContactNumber"] == DBNull.Value
                    ? string.Empty
                    : Convert.ToString(
                        row["AlternateContactNumber"])
                      ?? string.Empty;
            address.Text =
                row["Address"] == DBNull.Value
                    ? string.Empty
                    : Convert.ToString(
                        row["Address"])
                      ?? string.Empty;
            placeOfBirthInput.Text =
                row["PlaceOfBirth"] == DBNull.Value
                    ? string.Empty
                    : Convert.ToString(row["PlaceOfBirth"]) ?? string.Empty;
            prevSchool.Text =
                row["PreviousSchool"] == DBNull.Value
                    ? string.Empty
                    : Convert.ToString(
                        row["PreviousSchool"])
                      ?? string.Empty;
            bform.Text =
                row["BFormNumber"] == DBNull.Value
                    ? string.Empty
                    : Convert.ToString(
                        row["BFormNumber"])
                      ?? string.Empty;
            string religionValue =
                row["Religion"] == DBNull.Value
                    ? string.Empty
                    : Convert.ToString(
                        row["Religion"])
                      ?? string.Empty;
            religion.SelectedIndex = -1;
            if (!string.IsNullOrWhiteSpace(religionValue))
            {
                int religionIndex =
                    religion.FindStringExact(
                        religionValue.Trim());
                if (religionIndex >= 0)
                {
                    religion.SelectedIndex = religionIndex;
                }
                else
                {
                    /* Kept so the save can send it back unchanged rather than blanking a value this list cannot show. */
                    _loadedReligionNotInList = religionValue.Trim();
                    valuesNotInList.Add(
                        $"religion \"{religionValue.Trim()}\"");
                }
            }
            string bloodGroup =
                row["BloodGroup"] == DBNull.Value
                    ? string.Empty
                    : Convert.ToString(
                        row["BloodGroup"])
                      ?? string.Empty;
            blood.SelectedIndex = 0;
            if (!string.IsNullOrWhiteSpace(bloodGroup))
            {
                int bloodIndex =
                    blood.FindStringExact(
                        bloodGroup.Trim());
                if (bloodIndex >= 0)
                {
                    blood.SelectedIndex =
                        bloodIndex;
                }
                else
                {
                    blood.SelectedIndex = 0;
                }
            }
            /* Item 6 - set from THIS student every time: a student with no admission date used to keep the previous student's date on the form and in the summary. */
            bool hasAdmissionDate =
                row["AdmissionDate"] != DBNull.Value;
            admissionDate.Value =
                hasAdmissionDate
                    ? Convert.ToDateTime(
                        row["AdmissionDate"])
                    : DateTime.Today;
            /* Set from THIS student every time: a student with no date of birth used to leave the previous student's date in the picker, and Save then wrote it. */
            if (row["DateOfBirth"] != DBNull.Value)
            {
                dob.Value =
                    Convert.ToDateTime(
                        row["DateOfBirth"]);
                dob.Checked = true;
            }
            else
            {
                dob.Checked = false;
            }
            string genderValue =
                row["Gender"] == DBNull.Value
                    ? string.Empty
                    : Convert.ToString(
                        row["Gender"])
                      ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(
                    genderValue) &&
                gender.Items.Contains(
                    genderValue))
            {
                gender.SelectedItem =
                    genderValue;
            }
            else
            {
                gender.SelectedIndex = -1;
            }
            _editCampusId =
                GetIntColumn(
                    row,
                    "CurrentCampusId");
            if (_editCampusId <= 0)
            {
                _editCampusId =
                    GetIntColumn(
                        row,
                        "AdmissionCampusId");
            }
            _editYearId =
                GetIntColumn(
                    row,
                    "AcademicYearId");
            _editClassId =
                GetIntColumn(
                    row,
                    "ClassId");
            _editEnrollmentId =
                GetIntColumn(
                    row,
                    "EnrollmentId");
            registrationNo.Text =
                row["RegistrationNumber"] == DBNull.Value
                    ? string.Empty
                    : Convert.ToString(
                        row["RegistrationNumber"])
                      ?? string.Empty;
            enrolmentNo.Text =
                row.Table.Columns.Contains("BiseEnrolmentNumber") &&
                row["BiseEnrolmentNumber"] != DBNull.Value
                    ? Convert.ToString(
                        row["BiseEnrolmentNumber"])
                      ?? string.Empty
                    : string.Empty;
            casteTribe.Text =
                row.Table.Columns.Contains("CasteTribe") &&
                row["CasteTribe"] != DBNull.Value
                    ? Convert.ToString(
                        row["CasteTribe"])
                      ?? string.Empty
                    : string.Empty;
            string PlacementText(
                string columnName,
                string fallback = "-")
            {
                if (!row.Table.Columns.Contains(
                        columnName))
                {
                    return fallback;
                }
                if (row[columnName] == DBNull.Value)
                {
                    return fallback;
                }
                return Convert.ToString(
                           row[columnName])
                       ?? fallback;
            }
            string campusName =
                PlacementText(
                    "CurrentCampusName");
            if (campusName == "-")
            {
                campusName =
                    PlacementText(
                        "AdmissionCampusName");
            }
            int transportEnrollmentId=row.Table.Columns.Contains("EnrollmentId") && row["EnrollmentId"]!=DBNull.Value
                ? Convert.ToInt32(row["EnrollmentId"]) : 0;
            string transportText=await Task.Run(() => EnrollmentTransportStatus.Read(transportEnrollmentId));
            if(IsDisposed || version!=admissionLoadVersion) return false;
            viewRegistrationNo.Text =
                registrationNo.Text;
            viewAdmissionDate.Text =
                hasAdmissionDate
                    ? admissionDate.Value.ToString(
                        "dd-MMM-yyyy")
                    : "-";
            string currentEnrollmentDate=row.Table.Columns.Contains("EnrollmentDate") && row["EnrollmentDate"]!=DBNull.Value &&
                row.Table.Columns.Contains("EnrollmentStatus") && Convert.ToString(row["EnrollmentStatus"])=="Active"
                ? Convert.ToDateTime(row["EnrollmentDate"]).ToString("dd-MMM-yyyy") : "-";
            viewEnrollmentDate.Text=currentEnrollmentDate;
            editEnrollmentDate.Text=currentEnrollmentDate;
            viewYear.Text =
                PlacementText(
                    "YearName");
            bool hasActiveEnrollment = row.Table.Columns.Contains("EnrollmentStatus") &&
                string.Equals(Convert.ToString(row["EnrollmentStatus"]), "Active", StringComparison.OrdinalIgnoreCase);
            string yearCaption = _editEnrollmentId > 0 && !hasActiveEnrollment
                ? "Last Academic Year" : "Current Academic Year";
            if (viewYearCaption is not null) viewYearCaption.Text = yearCaption;
            viewYear.AccessibleName = yearCaption;
            viewCampus.Text =
                campusName;
            viewClass.Text =
                PlacementText(
                    "ClassName");
            viewSection.Text =
                PlacementText(
                    "SectionName");
            viewTransport.Text =
                transportText;
            if (IsDisposed || version != admissionLoadVersion) return false;
            initializingFirstPlacement = true;
            try
            {
                assignFirstPlacement.Checked = false;
                firstPlacementStatus.SelectedItem = _workspaceStudentStatus;
                firstEnrollmentDate.Value = admissionDate.Value;
                firstEnrollmentDate.Checked = false;
                bool hasWithdrawal = !row.IsNull("WithdrawalDate");
                firstWithdrawalDate.Value = hasWithdrawal ? Convert.ToDateTime(row["WithdrawalDate"]) : admissionDate.Value;
                firstWithdrawalDate.Checked = hasWithdrawal;
            }
            finally { initializingFirstPlacement = false; }
            if (_editEnrollmentId <= 0)
                await PrepareFirstPlacementAsync(_editCampusId);
            if (IsDisposed || version != admissionLoadVersion) return false;
            ShowPlacementSummary(true);
            loadedThisStudent = true;
            if (_editEnrollmentId > 0)
            {
                await LoadStudentFeeAssignmentsAsync(
                    _editEnrollmentId);
            }
            else
            {
                feeGrid.DataSource = null;
                feeStatus.Text =
                    "This student has no current placement.";
            }
            /* Item 4 - every await in this tail can finish after the operator has moved to another student (or cleared the form). */
            if (IsDisposed ||
                version != admissionLoadVersion ||
                currentAdmissionId != admissionId)
            {
                return false;
            }
            await LoadPhotoForAdmissionAsync(
                currentAdmissionId);
            if (IsDisposed ||
                version != admissionLoadVersion ||
                currentAdmissionId != admissionId)
            {
                return false;
            }
            await LoadCurrentSiblingsAsync();
            if (IsDisposed ||
                version != admissionLoadVersion ||
                currentAdmissionId != admissionId)
            {
                return false;
            }
            UpdateStudentWorkspace();
            SwitchTab(0);
            SetStatus(
                $"Editing · {AdmissionNumbers.ForId(admissionId)}"
                + (valuesNotInList.Count > 0
                    ? $" · Select valid {string.Join(" and ", valuesNotInList)} before saving."
                    : string.Empty));
        }
        finally
        {
            /* Item 3 - a load that threw, or that a newer one has overtaken, must NOT hide the placement pickers behind the summary. */
            if (!IsDisposed &&
                loadedThisStudent &&
                version == admissionLoadVersion)
            {
                ShowPlacementSummary(true);
            }
        }
        /* and again before the header and the clean baseline. */
        if (IsDisposed ||
            version != admissionLoadVersion)
        {
            return false;
        }
        /* and this is the point at which the page knows WHICH record it is showing, so the header says so rather than looking like a blank form. */
        string loadedStudent = name.Text.Trim();
        SetPageHeaderText(
            registrationReview ? "Review Registration" : "Edit Student",
            registrationReview ? $"{loadedStudent} · {AdmissionNumbers.ForId(admissionId)} · Save details, then confirm official admission."
                : $"Student: {loadedStudent} (Admission Number {AdmissionNumbers.ForId(admissionId)}) - saving updates this student.");
        /* this is the point at which the form has settled on a record. */
        CaptureAdmissionBaseline();
        Defer(() => RefreshConfirmReturnAvailabilityAsync(admissionId, version));
        return true;
    }
    /* The load generation for the new-admission fee structure. Its loader is now awaited, so a second Campus/Year/Class change can overtake this run and this one must not write after it. */
    private int feeStructureLoadVersion;
    private async Task LoadFeeStructure()
    {
        bool editing = currentAdmissionId > 0;
        if (editing || HistoricalAdmission)
            return;
        int campusId = Campus.SelectedId;
        int yearId = Year.SelectedId;
        int classId = Class.SelectedId;
        /* two guards before anything is cleared. */
        if (_selectedFees.Count > 0 &&
            campusId == _feeScopeCampusId &&
            yearId == _feeScopeYearId &&
            classId == _feeScopeClassId)
        {
            return;
        }
        int version = ++feeStructureLoadVersion;
        _selectedFees.Clear();
        if (campusId <= 0 || yearId <= 0 || classId <= 0)
        {
            _availableFeeOptions = new List<FeeConfigOption>();
            feeGrid.DataSource = _selectedFees;
            feeStatus.Text =
                "Select Campus, Academic Year and Class above to load fees.";
            return;
        }
        try
        {
            /* this was fire-and-forget, and the callee's FIRST statement replaces _availableFeeOptions with an empty list before its first await. */
            await LoadAvailableFeeOptionsAsync(campusId, yearId, classId);
            if (IsDisposed ||
                version != feeStructureLoadVersion || HistoricalAdmission ||
                Campus.SelectedId != campusId ||
                Year.SelectedId != yearId ||
                Class.SelectedId != classId)
            {
                return;
            }
            foreach (var option in _availableFeeOptions.Where(
                o => o.IsMandatory || (o.IsTransportLinked && UsesTransport)))
            {
                _selectedFees.Add(new FeeSelectionRow {
                    FeeTypeId = option.FeeTypeId,
                    FeeTypeCode = option.FeeTypeCode,
                    FeeTypeName = option.FeeTypeName,
                    StandardAmount = option.Amount,
                    DiscountAmount = 0,
                    NetAmount = option.Amount,
                    Frequency = option.Frequency,
                    IsMandatory = option.IsMandatory,
                    IsTransportLinked = option.IsTransportLinked
                });
            }
            feeGrid.DataSource = _selectedFees;
            /* The rows now on screen were built for this scope, so this is the scope a later change is measured against. */
            _feeScopeCampusId = campusId;
            _feeScopeYearId = yearId;
            _feeScopeClassId = classId;
            string contextLabel = $"{Campus.Text} / {Year.Text} / {Class.Text}";
            if (_availableFeeOptions.Count == 0)
            {
                feeStatus.Text =
                    $"No fee structure has been configured yet for {contextLabel}.";
            }
            else
            {
                int remaining = _availableFeeOptions.Count - _selectedFees.Count;
                feeStatus.Text = _selectedFees.Count == 0
                    ? $"{_availableFeeOptions.Count} fee(s) available for {contextLabel} - use + Add Fee below."
                    : $"{_selectedFees.Count} fee(s) loaded for {contextLabel}." +
                      (remaining > 0 ? $" {remaining} more available - use + Add Fee below." : "");
            }
        }
        catch (Exception nonFatalException51)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException51, "StudentAdmissionPage: LoadFeeStructure fallback");
            feeGrid.DataSource = null;
            feeStatus.Text = "Unable to load the fee structure right now.";
        }
    }
    private void TransportChanged()
    {
        if (currentAdmissionId > 0)
            return;
        var transportOption = _availableFeeOptions
            .FirstOrDefault(o => o.IsTransportLinked);
        if (transportOption is null)
            return;
        var existingRow = _selectedFees
            .FirstOrDefault(f => f.FeeTypeId == transportOption.FeeTypeId);
        if (UsesTransport)
        {
            if (existingRow is null)
            {
                _selectedFees.Add(new FeeSelectionRow {
                    FeeTypeId = transportOption.FeeTypeId,
                    FeeTypeCode = transportOption.FeeTypeCode,
                    FeeTypeName = transportOption.FeeTypeName,
                    StandardAmount = transportOption.Amount,
                    DiscountAmount = 0,
                    NetAmount = transportOption.Amount,
                    Frequency = transportOption.Frequency,
                    IsTransportLinked = true
                });
            }
        }
        else if (existingRow is not null)
        {
            _selectedFees.Remove(existingRow);
        }
        RefreshFeeStatus();
    }
    private async Task LoadCurrentSiblingsAsync()
    {
        /* Item 4 - the student this list is being built for. */
        int admissionId = currentAdmissionId;
        try
        {
            if (admissionId <= 0)
            {
                siblingsGrid.DataSource = null;
                UpdateOpenSiblingAvailability();
                siblingsStatus.Text =
                    "Save the admission first to manage siblings.";
                return;
            }
            var family = await DatabaseHelper.QueryUserAsync(
                "GetStudentFamily",
                DatabaseHelper.Int("@AdmissionId", admissionId));
            var campusRows = await Task.Run(() => DatabaseHelper.QuerySql("""
                EXEC dbo.RequirePermission @UserId,N'Students.View';
                SELECT E.EnrollmentId,C.CampusName
                FROM dbo.Enrollments E
                JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
                JOIN dbo.Campuses C ON C.CampusId=CCS.CampusId
                WHERE E.EnrollmentId IN (SELECT TRY_CONVERT(int,[value]) FROM OPENJSON(@EnrollmentIds));
                """, DatabaseHelper.Int("@UserId",CurrentUser.UserId),
                DatabaseHelper.P("@EnrollmentIds",System.Text.Json.JsonSerializer.Serialize(
                    family.AsEnumerable().Select(row => GetIntColumn(row,"EnrollmentId")).Distinct().ToArray()))));
            var campuses = campusRows.AsEnumerable().ToDictionary(
                row => GetIntColumn(row,"EnrollmentId"), row => GetTextColumn(row,"CampusName"));
            if (IsDisposed ||
                currentAdmissionId != admissionId)
            {
                return;
            }
            var display = new DataTable();
            display.Columns.Add("Student Name", typeof(string));
            display.Columns.Add("Father Name", typeof(string));
            display.Columns.Add(UiText.Fields.RegistrationNumber, typeof(string));
            display.Columns.Add("Class", typeof(string));
            display.Columns.Add("Campus", typeof(string));
            display.Columns.Add("Status", typeof(string));
            display.Columns.Add("FamilyMemberId", typeof(int));
            display.Columns.Add("AdmissionId", typeof(int));
            display.Columns.Add("AdmissionNumber", typeof(string));
            if (family.Rows.Count > 0 &&
                family.Columns.Contains("FamilyId") &&
                family.Rows[0]["FamilyId"] != DBNull.Value)
            {
                var members = family;
                if (IsDisposed ||
                    currentAdmissionId != admissionId)
                {
                    return;
                }
                foreach (DataRow row in members.AsEnumerable()
                    .OrderBy(row => long.TryParse(AdmissionNumbers.Read(row), out long number) ? number : long.MaxValue)
                    .ThenBy(row => AdmissionNumbers.Read(row),StringComparer.CurrentCultureIgnoreCase))
                {
                    int siblingAdmissionId = GetIntColumn(row, "AdmissionId");
                    if (siblingAdmissionId <= 0 || siblingAdmissionId == admissionId)
                        continue;
                    display.Rows.Add(
                        GetTextColumn(row, "StudentName"),
                        GetTextColumn(row, "FatherName"),
                        GetTextColumn(row, "RegistrationNumber"),
                        string.IsNullOrWhiteSpace(GetTextColumn(row,"SectionName"))
                            ? GetTextColumn(row,"ClassName")
                            : $"{GetTextColumn(row,"ClassName")} - {GetTextColumn(row,"SectionName")}",
                        campuses.GetValueOrDefault(GetIntColumn(row,"EnrollmentId"),string.Empty),
                        GetTextColumn(row, "Status"),
                        GetIntColumn(row, "FamilyMemberId"),
                        siblingAdmissionId,
                        AdmissionNumbers.Read(row));
                }
            }
            // Define the schema even when the details tab has not created a binding context yet.
            siblingsGrid.DataSource = null;
            siblingsGrid.AutoGenerateColumns = false;
            siblingsGrid.Columns.Clear();
            foreach (DataColumn field in display.Columns)
                siblingsGrid.Columns.Add(new DataGridViewTextBoxColumn {
                    Name = field.ColumnName,
                    DataPropertyName = field.ColumnName,
                    HeaderText = field.ColumnName,
                    ReadOnly = true
                });
            siblingsGrid.DataSource = display;
            if (siblingsGrid.Columns.Contains("FamilyMemberId"))
                siblingsGrid.Columns["FamilyMemberId"].Visible = false;
            /* Students are identified by Admission Number, so it leads the row. The registration number (a reporting label) stays in the table but is not shown to the operator. */
            if (siblingsGrid.Columns.Contains(UiText.Fields.RegistrationNumber))
                siblingsGrid.Columns[UiText.Fields.RegistrationNumber].Visible = false;
            if (siblingsGrid.Columns.Contains("AdmissionId"))
            {
                siblingsGrid.Columns["AdmissionId"].Visible = false;
                DataGridViewColumn admissionColumn = siblingsGrid.Columns["AdmissionNumber"];
                admissionColumn.Visible = true;
                admissionColumn.HeaderText = "Admission Number";
                admissionColumn.ReadOnly = true;
                admissionColumn.DisplayIndex = 0;
            }
            string[] visibleOrder = { "AdmissionNumber", "Student Name", "Father Name", "Class", "Campus", "Status" };
            for (int index = 0; index < visibleOrder.Length; index++)
                siblingsGrid.Columns[visibleOrder[index]].DisplayIndex = index;
            siblingsGrid.Columns["AdmissionNumber"].HeaderText = "Adm. No.";
            siblingsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            siblingsGrid.Columns["AdmissionNumber"].FillWeight = 65;
            siblingsGrid.Columns["Student Name"].FillWeight = 130;
            siblingsGrid.Columns["Father Name"].FillWeight = 120;
            siblingsGrid.Columns["Campus"].FillWeight = 110;
            siblingsStatus.Text = display.Rows.Count > 0
                ? $"{display.Rows.Count} sibling(s) currently linked. Select one and click Open Selected Sibling, or double-click the row."
                : "No siblings are linked yet. Click Find Possible Siblings to add one.";
            if (display.Rows.Count > 0)
                siblingsGrid.ClearSelection();
            UpdateOpenSiblingAvailability();
        }
        catch (Exception nonFatalException52)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException52, "StudentAdmissionPage: LoadCurrentSiblingsAsync fallback");
            siblingsGrid.DataSource = null;
            UpdateOpenSiblingAvailability();
            siblingsStatus.Text =
                "Unable to load the student's current siblings right now.";
        }
    }
    private static string GetTextColumn(DataRow row, string columnName)
    {
        return row.Table.Columns.Contains(columnName) &&
               row[columnName] != DBNull.Value
            ? Convert.ToString(row[columnName]) ?? string.Empty
            : string.Empty;
    }
    private static decimal GetDecimalColumn(DataRow row, string columnName)
    {
        return row.Table.Columns.Contains(columnName) &&
               row[columnName] != DBNull.Value
            ? Convert.ToDecimal(row[columnName])
            : 0m;
    }
    private void CollectPaymentNow()
    {
        if (pendingPlacementId > 0) { SetStatus("Review and Confirm Placement before collecting payment."); return; }
        if (currentAdmissionId <= 0)
        {
            Warn("Save this admission first, then you can collect payment.");
            return;
        }
        if (_navigate is null)
        {
            Alert("Fee Collection could not be opened from here.");
            return;
        }
        int admissionId = currentAdmissionId;
        _navigate(
            () => new FeeCollectionPage(admissionId),
            "Finance",
            "Fee Collection");
    }
    private async Task PrintAdmissionBill(bool showActualAmount)
    {
        try
        {
            if (currentAdmissionId <= 0)
            {
                Warn("Save this admission first, then you can print a bill.");
                return;
            }
            int enrollmentId =
                await GetActiveEnrollmentIdAsync(currentAdmissionId);
            if (enrollmentId <= 0)
            {
                Warn("No placement was found for this student.");
                return;
            }
            /* A print action writing to the ledger is wrong on its own terms: the operator was looking at a document, not posting fees. */
            var raw =
                await DatabaseHelper.QueryUserAsync(
                    "GetOutstandingCharges",
                    DatabaseHelper.Int(
                        "@AdmissionId",
                        currentAdmissionId),
                    DatabaseHelper.Int(
                        "@EnrollmentId",
                        enrollmentId),
                    DatabaseHelper.Date(
                        "@AsOfDate",
                        DateTime.Today));
            var billRows = new List<BillLineItem>();
            foreach (DataRow row in raw.Rows)
            {
                decimal outstanding =
                    GetDecimalColumn(row, "Balance");
                if (outstanding <= 0m)
                    continue;
                decimal grossAmount = GetDecimalColumn(row, "GrossAmount");
                decimal netAmount = GetDecimalColumn(row, "NetAmount");
                decimal paidAmount = GetDecimalColumn(row, "PaidAmount");
                decimal nominalOutstanding =
                    Math.Max(0m, grossAmount - paidAmount);
                decimal discountAmount =
                    Math.Max(0m, grossAmount - netAmount);
                decimal amountToPrint =
                    showActualAmount
                        ? outstanding
                        : nominalOutstanding;
                billRows.Add(
                    new BillLineItem {
                        FeeName = GetTextColumn(row, "FeeTypeName"),
                        BillingMonth =
                            row.Table.Columns.Contains("BillingMonth")
                                ? FormatBillMonth(row["BillingMonth"])
                                : string.Empty,
                        Amount = amountToPrint,
                        DiscountAmount = discountAmount
                    });
            }
            if (billRows.Count == 0)
            {
                Warn("There is nothing outstanding to bill for this student.");
                return;
            }
            string classSectionText =
                showActualAmount
                    ? $"{(string.IsNullOrWhiteSpace(viewClass.Text) ? "-" : viewClass.Text)} - " +
                      $"{(string.IsNullOrWhiteSpace(viewSection.Text) ? "-" : viewSection.Text)}"
                    : string.Empty;
            var institution = await InstitutionProfile.GetPrintIdentityAsync();
            if (string.IsNullOrWhiteSpace(institution.Name))
            {
                Alert("Check School Profile before printing the admission bill.");
                return;
            }
            AdmissionBillReport.Show(FindForm(), institution, name.Text.Trim(), currentAdmissionId,
                classSectionText, billRows.Select(row => new AdmissionBillReport.Line(
                    row.FeeName, row.BillingMonth, row.Amount, row.DiscountAmount)).ToList(), showActualAmount,
                viewCampus.Text == "-" ? string.Empty : viewCampus.Text.Trim(), fatherName.Text.Trim());
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    private async Task PrintAdmissionForm()
    {
        try
        {
            if (currentAdmissionId <= 0)
            {
                Warn("Save this admission first, then you can print the admission form.");
                return;
            }
            DataTable studentData =
                await DatabaseHelper.QueryUserAsync(
                    "GetStudentDetails",
                    DatabaseHelper.Int(
                        "@AdmissionId",
                        currentAdmissionId));
            if (studentData.Rows.Count == 0)
            {
                Alert("The selected student could not be loaded.");
                return;
            }
            DataRow s = studentData.Rows[0];
            DataTable profileData =
                await DatabaseHelper.QueryUserAsync("GetSchoolProfile");
            DataTable officialsData =
                await DatabaseHelper.QueryUserAsync(
                    "GetSchoolOfficials");
            string schoolName = string.Empty;
            string schoolContactLine = string.Empty;
            if (profileData.Rows.Count > 0)
            {
                DataRow p = profileData.Rows[0];
                schoolName = GetTextColumn(p, "SchoolName");
                var contactParts = new[] {
                    GetTextColumn(p, "AddressLine"),
                    GetTextColumn(p, "City"),
                    string.IsNullOrWhiteSpace(GetTextColumn(p, "ContactNumber"))
                        ? string.Empty
                        : "Ph: " + GetTextColumn(p, "ContactNumber"),
                    GetTextColumn(p, "Email")
                }.Where(x => !string.IsNullOrWhiteSpace(x));
                schoolContactLine = string.Join("     |     ", contactParts);
            }
            string principalName = string.Empty;
            DataRow? principalRow = officialsData.AsEnumerable()
                .Where(r =>
                    string.Equals(
                        Convert.ToString(r["Designation"]),
                        "Principal",
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(r =>
                    r.Table.Columns.Contains("DisplayOrder") && r["DisplayOrder"] != DBNull.Value
                        ? Convert.ToInt32(r["DisplayOrder"])
                        : 0)
                .FirstOrDefault();
            if (principalRow != null)
                principalName = GetTextColumn(principalRow, "PersonName");
            string campusName = GetTextColumn(s, "CurrentCampusName");
            if (string.IsNullOrWhiteSpace(campusName))
                campusName = GetTextColumn(s, "AdmissionCampusName");
            string DateText(string column) =>
                s.Table.Columns.Contains(column) &&
                s[column] != DBNull.Value
                    ? Convert.ToDateTime(s[column]).ToString("dd-MMM-yyyy")
                    : "-";
            string TextOrDash(string column)
            {
                string v = GetTextColumn(s, column);
                return string.IsNullOrWhiteSpace(v) ? "-" : v;
            }
            string placeOfBirth = GetTextColumn(s, "PlaceOfBirth");
            string disability = GetTextColumn(s, "Disability");
            int transportEnrollmentId=s.Table.Columns.Contains("EnrollmentId") && s["EnrollmentId"]!=DBNull.Value
                ? Convert.ToInt32(s["EnrollmentId"]) : 0;
            string transportStatus=await Task.Run(() => EnrollmentTransportStatus.Read(transportEnrollmentId));
            var data = new DataTable();
            foreach (string col in new[] {
                "SchoolName", "SchoolContactLine", "RegistrationNumber", "AdmissionNumber",
                "AdmissionDateText", "PrincipalName",
                "StudentName", "DateOfBirthText", "GenderText",
                "PlaceOfBirthText", "BloodGroupText", "ReligionText",
                "BFormNumberText", "DisabilityText", "PreviousSchoolText",
                "FatherName", "FatherOccupationText", "ContactNumberText",
                "AlternateContactNumberText", "AddressText",
                "CampusNameText", "YearNameText", "ClassNameText",
                "SectionNameText", "TransportText"
            })
            {
                data.Columns.Add(col, typeof(string));
            }
            DataRow row = data.NewRow();
            row["SchoolName"] =
                string.IsNullOrWhiteSpace(schoolName)
                    ? "School Name Not Set"
                    : schoolName;
            row["SchoolContactLine"] = schoolContactLine;
            row["RegistrationNumber"] = TextOrDash("RegistrationNumber");
            row["AdmissionNumber"] = TextOrDash("AdmissionNumber");
            row["AdmissionDateText"] = DateText("AdmissionDate");
            row["PrincipalName"] = principalName;
            row["StudentName"] = TextOrDash("StudentName");
            row["DateOfBirthText"] = DateText("DateOfBirth");
            row["GenderText"] = TextOrDash("Gender");
            row["PlaceOfBirthText"] =
                string.IsNullOrWhiteSpace(placeOfBirth)
                    ? "-"
                    : placeOfBirth;
            row["BloodGroupText"] = TextOrDash("BloodGroup");
            row["ReligionText"] = TextOrDash("Religion");
            row["BFormNumberText"] = TextOrDash("BFormNumber");
            row["DisabilityText"] =
                string.IsNullOrWhiteSpace(disability)
                    ? "No"
                    : disability;
            row["PreviousSchoolText"] = TextOrDash("PreviousSchool");
            row["FatherName"] = TextOrDash("FatherName");
            row["FatherOccupationText"] = TextOrDash("FatherOccupation");
            row["ContactNumberText"] = TextOrDash("ContactNumber");
            row["AlternateContactNumberText"] = TextOrDash("AlternateContactNumber");
            row["AddressText"] = TextOrDash("Address");
            row["CampusNameText"] =
                string.IsNullOrWhiteSpace(campusName)
                    ? "-"
                    : campusName;
            row["YearNameText"] = TextOrDash("YearName");
            row["ClassNameText"] = TextOrDash("ClassName");
            row["SectionNameText"] = TextOrDash("SectionName");
            row["TransportText"] = transportStatus;
            data.Rows.Add(row);
            string reportPath = Path.Combine(
                AppContext.BaseDirectory,
                "Reports",
                "AdmissionForm.rdlc");
            if (!File.Exists(reportPath))
            {
                throw new FileNotFoundException(
                    "Admission Form RDLC was not found.",
                    reportPath);
            }
            RdlcReportPreview.Show(
                FindForm(),
                "Student Admission Form",
                reportPath,
                "AdmissionFormData",
                data);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    private sealed class BillLineItem
    {
        public string FeeName { get; init; } = string.Empty;
        public string BillingMonth { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public decimal DiscountAmount { get; init; }
    }
    private async Task RefreshConfirmReturnAvailabilityAsync(int admissionId, int version)
    {
        if (confirmReturnButton is null) return;
        try
        {
            var placement = await Task.Run(() => DatabaseHelper.QuerySql("""
                EXEC dbo.RequireAnyPermission @UserId,N'Students.Admit',N'Students.Administer';
                SELECT TOP(1) EnrollmentId,PlacementConfirmedAt FROM dbo.Enrollments WHERE AdmissionId=@AdmissionId
                ORDER BY CASE WHEN Status=N'Active' THEN 0 ELSE 1 END,EnrollmentDate DESC,EnrollmentId DESC;
                """, DatabaseHelper.Int("@UserId", CurrentUser.UserId), DatabaseHelper.Int("@AdmissionId", admissionId)));
            if (IsDisposed || version != admissionLoadVersion || currentAdmissionId != admissionId) return;
            pendingPlacementId = !IsPendingRegistration && !legacySetupPending && placement.Rows.Count > 0 && placement.Rows[0]["PlacementConfirmedAt"] == DBNull.Value
                ? Convert.ToInt32(placement.Rows[0]["EnrollmentId"]) : 0;
            if (confirmPlacementAction is not null) confirmPlacementAction.Visible = false;
            if (pendingPlacementId > 0) SetStatus($"Draft · {AdmissionNumbers.ForId(admissionId)}");
            if (CurrentUser.HasPermission("Students.Administer"))
            {
                var pending = await DatabaseHelper.QueryUserAsync("GetPendingReturnAdmissions");
                if (IsDisposed || version != admissionLoadVersion || currentAdmissionId != admissionId) return;
                confirmReturnButton.Visible = !legacySetupPending && !IsPendingRegistration && pending.AsEnumerable().Any(row => GetIntColumn(row, "AdmissionId") == admissionId);
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed && version == admissionLoadVersion)
                AppExceptionHandler.LogNonFatal(ex, "Checking promoted student return eligibility");
        }
    }

    private async Task ConfirmPlacementAsync()
    {
        if (currentAdmissionId <= 0 || pendingPlacementId <= 0 || saveBusy) return;
        if (HasUnsavedChanges) { SetStatus("Save changes before confirming placement."); return; }
        int admissionId = currentAdmissionId;
        await RunBusyAsync("Confirming placement…", async () => {
            if (await StudentPlacementConfirmation.ConfirmAsync(FindForm(), pendingPlacementId,
                $"{AdmissionNumbers.ForId(admissionId)} · {name.Text}"))
                if (!IsDisposed && currentAdmissionId == admissionId) await LoadStudentForEditAsync(admissionId);
        });
    }

    private async Task CompleteRegistrationAsync()
    {
        if (!registrationReview || !IsPendingRegistration || currentAdmissionId <= 0 || saveBusy) return;
        // Confirmation requires the full admission details, even though saving a
        // pending registration allows the operator to complete them in stages.
        if (!ValidateAdmissionInline(isNew: false, confirmingRegistration: true)) return;
        if (HasUnsavedChanges) { SetStatus("Save the completed details first, then Confirm Admission."); return; }
        string suggestion = await AdmissionNumberPolicy.PreviewNextAsync() ?? "";
        using var dialog = new CompleteAdmissionDialog(suggestion, name.Text);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        await RunBusyAsync("Completing admission…", async () => {
            await DatabaseHelper.ExecuteUserAsync("CompleteQuickAdmission", DatabaseHelper.Int("@AdmissionId", currentAdmissionId), DatabaseHelper.P("@AdmissionNumber", dialog.AdmissionNumber));
            StudentSearchCache.Invalidate(); await LoadStudentForEditAsync(currentAdmissionId);
        });
        if (!IsPendingRegistration) RegistrationFinished?.Invoke();
    }
    private async Task VerifyLegacyAsync()
    {
        if (HasUnsavedChanges) { SetStatus("Save corrections before marking this legacy record verified."); return; }
        await RunBusyAsync("Verifying legacy record…", async () => {
            await DatabaseHelper.ExecuteUserAsync("VerifyLegacyStudent", DatabaseHelper.Int("@AdmissionId", currentAdmissionId));
            SetStatus("Legacy record verified. Later edits to details, placement or fees require verification again.");
        });
    }
    private async Task CloseAdmissionAsync(bool cancel)
    {
        if (currentAdmissionId <= 0 || saveBusy || (cancel && (!registrationReview || !IsPendingRegistration))) return;
        if (HasUnsavedChanges) { SetStatus("Save changes before closing this record."); return; }
        using var dialog = new CloseAdmissionDialog(currentAdmissionId,name.Text,cancel);
        dialog.ShowDialog(FindForm());
        if (dialog.Changed){await LoadStudentForEditAsync(currentAdmissionId);if(cancel)RegistrationFinished?.Invoke();}
    }
    private async Task OpenConfirmReturnAsync()
    {
        if (currentAdmissionId <= 0 || saveBusy || !CurrentUser.HasPermission("Students.Administer")) return;
        if (HasUnsavedChanges) { SetStatus("Save admission changes before confirming the student's return."); return; }
        int admissionId = currentAdmissionId;
        using var dialog = new ConfirmReturnAdmissionDialog(admissionId);
        dialog.ShowDialog(FindForm());
        if (!IsDisposed && currentAdmissionId == admissionId) await LoadStudentForEditAsync(admissionId);
    }

    private async Task OpenPlacementCorrectionAsync()
    {
        if (currentAdmissionId <= 0 || saveBusy || !CurrentUser.HasPermission("Students.Administer")) return;
        if (HasUnsavedChanges) { SetStatus("Save personal-detail changes before correcting placement."); return; }
        int admissionId = currentAdmissionId;
        using var dialog = new StudentPlacementCorrectionDialog(admissionId, name.Text);
        dialog.ShowDialog(FindForm());
        if (dialog.Changed && !IsDisposed && currentAdmissionId == admissionId) await LoadStudentForEditAsync(admissionId);
    }

    private async Task OpenEnrollmentActionAsync(string operation)
    {
        if (currentAdmissionId <= 0 || saveBusy) return;
        if (!CurrentUser.HasPermission(operation == "Re-Admit" ? "Students.Administer" : "Enrollment.Manage")) return;
        if (operation == "Re-Admit" && string.Equals(_workspaceStudentStatus.Trim(), "Active", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("An active student cannot be readmitted. The student must have left or be inactive first.");
            return;
        }
        if (HasUnsavedChanges)
        {
            SetStatus("Save your admission changes before opening a placement action.");
            return;
        }
        int admissionId = currentAdmissionId;
        using var dialog = StudentEnrollmentActionDialog.Create(operation, admissionId, name.Text);
        dialog.ShowDialog(FindForm());
        if (!IsDisposed && currentAdmissionId == admissionId)
            await LoadStudentForEditAsync(admissionId);
    }

    private static async Task<int> GetActiveEnrollmentIdAsync(int admissionId)
    {
        var raw =
            await DatabaseHelper.QueryUserAsync(
                "GetStudentEnrollments",
                DatabaseHelper.Int(
                    "@AdmissionId",
                    admissionId));
        if (raw.Rows.Count == 0)
            return 0;
        foreach (DataRow row in raw.Rows)
        {
            if (string.Equals(
                    GetTextColumn(row, "Status"),
                    "Active",
                    StringComparison.OrdinalIgnoreCase))
            {
                return GetIntColumn(row, "EnrollmentId");
            }
        }
        return GetIntColumn(raw.Rows[0], "EnrollmentId");
    }
    private static string FormatBillMonth(object value)
    {
        if (value is null || value == DBNull.Value)
            return string.Empty;
        return DateTime.TryParse(
                Convert.ToString(value),
                out DateTime date)
            ? date.ToString(
                "MMM-yyyy",
                System.Globalization.CultureInfo.InvariantCulture)
            : Convert.ToString(value) ?? string.Empty;
    }
    private async Task OpenPossibleSiblingSearch()
    {
        if (currentAdmissionId <= 0)
        {
            Warn("Save this admission first, then you can add siblings.");
            return;
        }
        using var dialog = new PossibleSiblingSearchForm(
            FindForm(),
            currentAdmissionId,
            fatherName.Text.Trim());
        dialog.ShowDialog(FindForm());
        if (dialog.AddedSibling)
            await LoadCurrentSiblingsAsync();
    }
    private bool openingSibling;
    private int SelectedSiblingAdmissionId()
    {
        if (siblingsGrid.SelectedRows.Count == 0)
            return 0;
        if (!siblingsGrid.Columns.Contains("AdmissionId"))
            return 0;
        DataGridViewRow row = siblingsGrid.SelectedRows[0];
        object? value = row.Cells["AdmissionId"].Value;
        return value is not null && value != DBNull.Value &&
               int.TryParse(Convert.ToString(value), out int admissionId)
            ? admissionId
            : 0;
    }
    private void UpdateOpenSiblingAvailability()
    {
        openSiblingButton.Enabled =
            !openingSibling &&
            SelectedSiblingAdmissionId() > 0;
    }
    private async Task OpenSelectedSiblingAsync()
    {
        if (openingSibling)
            return;
        int siblingAdmissionId = SelectedSiblingAdmissionId();
        if (siblingAdmissionId <= 0)
        {
            Warn("Select a sibling from the list first.");
            return;
        }
        if (siblingAdmissionId == currentAdmissionId)
        {
            SetStatus("That student is already open.");
            return;
        }
        if (!ConfirmDiscardAdmission())
            return;
        openingSibling = true;
        UpdateOpenSiblingAvailability();
        try
        {
            await LoadStudentForEditAsync(siblingAdmissionId);
            if (!IsDisposed && currentAdmissionId == siblingAdmissionId)
                Info($"Opened sibling {name.Text.Trim()} for review or editing.");
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            openingSibling = false;
            if (!IsDisposed)
                UpdateOpenSiblingAvailability();
        }
    }
    private static Bitmap BitmapFromBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var loaded = Image.FromStream(stream);
        return new Bitmap(loaded);
    }
    private async Task LoadPhotoForAdmissionAsync(int admissionId)
    {
        if (IsDisposed) return;
        var request = photoRead.Begin();
        int version = admissionLoadVersion;
        if (_pendingPhotoData is not null)
        {
            StudentPhotoViewForm.SetPreviewImage(photoPreview, BitmapFromBytes(_pendingPhotoData));
            ShowPendingPhotoStatus();
            UpdatePhotoActions();
            return;
        }
        StudentPhotoViewForm.SetPreviewImage(photoPreview, null);
        photoInfo.Text = string.Empty;
        if (admissionId <= 0)
            return;
        try
        {
            var dt = await readPhoto(admissionId, request.Token);
            /* Item 4 - a photo that arrives after the operator has moved to another student must not be shown on them. */
            if (IsDisposed ||
                !photoRead.IsCurrent(request) || admissionLoadVersion != version || currentAdmissionId != admissionId)
            {
                return;
            }
            if (dt.Rows.Count == 0)
            {
                photoInfo.Text = "No photo on file.";
                UpdatePhotoActions();
                return;
            }
            var row = dt.Rows[0];
            StudentPhotoViewForm.SetPreviewImage(
                photoPreview,
                BitmapFromBytes((byte[])row["PhotoData"]));
            photoInfo.Text = $"Updated on {Convert.ToDateTime(row["UpdatedAt"]):dd MMM yyyy, hh:mm tt}";
            UpdatePhotoActions();
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception nonFatalException53)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException53, "StudentAdmissionPage: LoadPhotoForAdmissionAsync fallback");
            if (!IsDisposed && photoRead.IsCurrent(request)) photoInfo.Text = "Unable to load photo right now.";
        }
    }
    private async Task ChoosePhotoAsync()
    {
        if (IsBusy || !CurrentUser.HasPermission("Students.Administer")) return;
        int selectedAdmissionId = currentAdmissionId;
        int selectedVersion = admissionLoadVersion;
        photoBusy = true;
        try
        {
            using var busy = BeginBusy(BusyMessage, this);
            using var dialog = new OpenFileDialog {
                Filter = "Image files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                Title = "Choose a student photo — resized automatically to 80 KB or less"
            };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
            var request = photoRead.Begin();
            byte[] bytes;
            try
            {
                var original = await StudentPhotoInput.ReadAsync(dialog.FileName, request.Token);
                bytes = await Task.Run(() => StudentPhotoProcessor.Prepare(original, request.Token), request.Token);
            }
            catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { return; }
            catch (StudentPhotoInputException ex) { if (!IsDisposed && photoRead.IsCurrent(request)) Warn(ex.Message); return; }
            catch (Exception ex)
            {
                AppExceptionHandler.LogNonFatal(ex, "Preparing student photo");
                if (!IsDisposed && photoRead.IsCurrent(request)) Warn("That image could not be processed. Choose another JPEG or PNG photo.");
                return;
            }
            if (IsDisposed || !photoRead.IsCurrent(request) || currentAdmissionId != selectedAdmissionId || admissionLoadVersion != selectedVersion)
                return;
            string fileName = Path.ChangeExtension(Path.GetFileName(dialog.FileName), ".jpg");
            StagePreparedPhoto(bytes, fileName);
            SetStatus(selectedAdmissionId > 0
                ? "Photo prepared for preview. Click Save Photo to store this selection."
                : "Photo prepared for preview. It will be stored when this admission is saved.");
        }
        catch (Exception ex) { if (!IsDisposed) Fail(ex); }
        finally { photoBusy = false; if (!IsDisposed) UpdatePhotoActions(); }
    }
    private async Task RemovePhoto()
    {
        if (IsBusy || !CurrentUser.HasPermission("Students.Administer")) return;
        int selectedAdmissionId = currentAdmissionId;
        int selectedVersion = admissionLoadVersion;
        photoBusy = true;
        try
        {
            using var busy = BeginBusy(BusyMessage, this);
            if (_pendingPhotoData is not null)
            {
                photoRead.Cancel();
                photoDraft.Discard();
                await LoadPhotoForAdmissionAsync(selectedAdmissionId);
                if (!IsDisposed && currentAdmissionId == selectedAdmissionId && admissionLoadVersion == selectedVersion)
                    SetStatus("The pending photo selection was discarded. The stored photo was not changed.");
                return;
            }
            if (selectedAdmissionId > 0)
            {
                /* this deletes the stored photo and cannot be undone, so it is confirmed first. */
                if (!Confirm(
                        "Remove this student's profile photo?\r\n\r\n" +
                        "The existing photo will be deleted from the student record.",
                        "Remove Photo"))
                {
                    return;
                }
                if (currentAdmissionId != selectedAdmissionId || admissionLoadVersion != selectedVersion) return;
                photoRead.Cancel();
                await DatabaseHelper.ExecuteUserAsync(
                    "DeleteStudentPhoto",
                    DatabaseHelper.Int("@AdmissionId", selectedAdmissionId));
                if (IsDisposed || currentAdmissionId != selectedAdmissionId || admissionLoadVersion != selectedVersion) return;
                Info("Photo removed.");
                photoDraft.Discard();
                await LoadPhotoForAdmissionAsync(currentAdmissionId);
            }
            else
            {
                photoRead.Cancel();
                StudentPhotoViewForm.SetPreviewImage(photoPreview, null);
                photoInfo.Text = string.Empty;
                photoDraft.Discard();
            }
        }
        catch (Exception ex) { if (!IsDisposed) Fail(ex); }
        finally { photoBusy = false; if (!IsDisposed) UpdatePhotoActions(); }
    }
    private static Task WritePreparedPhotoAsync(int id, byte[] data, string? fileName, string? contentType) =>
        DatabaseHelper.ExecuteUserAsync("SaveStudentPhoto", DatabaseHelper.Int("@AdmissionId", id),
            new SqlParameter("@PhotoData", SqlDbType.VarBinary, -1) { Value = data },
            DatabaseHelper.P("@FileName", fileName), DatabaseHelper.P("@ContentType", contentType));

    private void StagePreparedPhoto(byte[] bytes, string fileName)
    {
        var image = BitmapFromBytes(bytes);
        try
        {
            photoDraft.Stage(bytes, fileName);
            StudentPhotoViewForm.SetPreviewImage(photoPreview, image);
        }
        catch { image.Dispose(); throw; }
        ShowPendingPhotoStatus();
        UpdatePhotoActions();
    }

    private void ShowPendingPhotoStatus()
    {
        if (photoDraft.Current is not { } selection) return;
        string action = currentAdmissionId <= 0 ? "save with admission"
            : selection.SaveFailed ? "not saved — click Retry Photo Save" : "preview — click Save Photo";
        photoInfo.Text = $"{selection.FileName} · {selection.Data.Length / 1024d:N0} KB · {action}";
    }

    private void UpdatePhotoActions()
    {
        bool allowed = CurrentUser.HasPermission("Students.Administer");
        bool pending = photoDraft.Current is not null;
        bool existing = currentAdmissionId > 0;
        choosePhotoButton.Enabled = allowed && !IsBusy;
        savePhotoButton.Visible = existing;
        savePhotoButton.Enabled = allowed && existing && pending && !IsBusy;
        savePhotoButton.Text = photoDraft.Current?.SaveFailed == true ? "Retry Photo Save" : "Save Photo";
        removePhotoButton.Text = pending ? "Discard Selection" : "Remove Photo";
        removePhotoButton.ThemeRole = pending ? "Secondary" : "Danger";
        removePhotoButton.Enabled = allowed && !IsBusy && (pending || photoPreview.Image is not null);
        if (photoSaveRow is not null) photoSaveRow.Height = existing ? AppMetrics.ButtonHeight + 4 : 0;
        if (photoActionsRow is not null) photoActionsRow.Height = (existing ? 3 : 2) * (AppMetrics.ButtonHeight + 4);
    }

    private async Task SavePhotoAsync()
    {
        if (IsBusy || currentAdmissionId <= 0 || _pendingPhotoData is null || !CurrentUser.HasPermission("Students.Administer")) return;
        int id = currentAdmissionId, version = admissionLoadVersion;
        photoBusy = true; UpdatePhotoActions();
        try
        {
            using var hold = BeginBusy(BusyMessage, this);
            bool saved = await PersistPendingPhotoIfAnyAsync(id);
            if (IsDisposed || currentAdmissionId != id || admissionLoadVersion != version) return;
            if (saved) SetStatus("Photo saved. Student details were not changed.", PageStatusKind.Success);
        }
        finally { photoBusy = false; if (!IsDisposed) UpdatePhotoActions(); }
    }

    private async Task<bool> PersistPendingPhotoIfAnyAsync(int admissionId)
    {
        int version = admissionLoadVersion;
        if (_pendingPhotoData is not null && !CurrentUser.HasPermission("Students.Administer"))
        {
            SetStatus("Your role cannot save student photos. The prepared selection has been retained.", PageStatusKind.Warning);
            return false;
        }
        var result = await photoDraft.SaveAsync(admissionId, writePhoto,
            () => !IsDisposed && currentAdmissionId == admissionId && admissionLoadVersion == version);
        if (result.Error is not null) AppExceptionHandler.LogNonFatal(result.Error, "Saving prepared student photo");
        if (IsDisposed || currentAdmissionId != admissionId || admissionLoadVersion != version) return false;
        if (result.Outcome == PhotoSaveOutcome.Failed)
        {
            ShowPendingPhotoStatus();
            SetStatus("Photo not saved. The prepared selection is still here; use Retry Photo Save. " + DbMessage.UserMessage(result.Error!), PageStatusKind.Warning);
        }
        else if (result.Outcome == PhotoSaveOutcome.Saved) photoInfo.Text = "Photo saved.";
        UpdatePhotoActions();
        return result.Outcome is PhotoSaveOutcome.Saved or PhotoSaveOutcome.NoPendingPhoto;
    }
    private static TableLayoutPanel BuildGrid(
    int columns,
    params (string Caption, Control Control, bool Required)[] fields)
    {
        int rows =
            (fields.Length + columns - 1) / columns;
        const int horizontalGap = 12;
        var table = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = columns,
            RowCount = rows,
            Padding = new Padding(
                2,
                2,
                2,
                0),
            Margin = new Padding(0)
        };
        const float fieldWidth = 45F;
        for (int column = 0;
             column < columns;
             column++)
        {
            table.ColumnStyles.Add(
                new ColumnStyle(
                    SizeType.Percent,
                    fieldWidth));
        }
        for (int row = 0;
             row < rows;
             row++)
        {
            table.RowStyles.Add(
                new RowStyle(
                    SizeType.Absolute,
                    AppMetrics.FieldRowHeight));
        }
        for (int i = 0;
             i < fields.Length;
             i++)
        {
            int column = i % columns;
            int row = i / columns;
            var field = HumanUi.Field(
                fields[i].Caption,
                fields[i].Control,
                fields[i].Required);
            int leftGap =
                column == 0
                    ? 0
                    : horizontalGap / 2;
            int rightGap =
                column == columns - 1
                    ? 0
                    : horizontalGap / 2;
            field.Margin = new Padding(
                leftGap,
                0,
                rightGap,
                0);
            table.Controls.Add(
                field,
                column,
                row);
        }
        return table;
    }
    private bool IsPendingRegistration => _workspaceStudentStatus == "Pending";
    private bool legacySetupPending;
    private async Task Save()
    {
        if (IsBusy || unconfirmedAdmission is not null || (registrationReview && (!IsPendingRegistration || currentAdmissionId <= 0)))
            return;
        saveBusy = true;
        UpdateStudentWorkspace();
        savedAdmissionReadyForNext = false;
        if (saveButton is not null)
            saveButton.Enabled = false;
        /* UX - while the save runs, the actions that would wipe or replace the form under it (Find, New Admission, Clear, quick find) are held. */
        BusyScope holdInterfering =
            BeginBusy(string.Empty, findButton, newAdmissionButton, quickStudent, saveAndNewButton, keepPlacement);
        try
        {
            if (currentAdmissionId > 0)
                await SaveExistingAsync();
            else
                await SaveNewAdmissionAsync();
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            // Restore temporarily held controls before applying the final save/recovery state.
            // Otherwise the scope could undo Save & New's enabled state after validation fails.
            holdInterfering.Dispose();
            saveBusy = false;
            if (!IsDisposed)
                UpdateStudentWorkspace();
        }
    }
    private async Task SaveAndStartNextAsync()
    {
        if (saveBusy || currentAdmissionId > 0)
            return;
        await Save();
        // Keep a committed student visible if photo storage or refreshing failed.
        if (!IsDisposed && savedAdmissionReadyForNext && currentAdmissionId > 0)
            StartNewAdmission();
    }
    private async Task SaveNewAdmissionAsync()
    {
        using var numberingInputs=BeginBusy(string.Empty,admissionDate,Year,admissionIdValue);
        try
        {
            admissionNumberPlan=await AdmissionNumberPolicy.LoadAsync(admissionDate.Value.Date,Year.SelectedId,HistoricalAdmission);
            /* UX - every problem is marked on its own field at once (border + a few words on the caption), the first one takes focus. */
            if (!ValidateAdmissionInline(isNew: true))
                return;
            string studentName =
                name.Text.Trim();
            if (!ConfirmReasonableAge())
                return;
            if (!dob.Checked &&
                !Confirm(
                    "No date of birth has been entered, so none will be stored.\n\n" +
                    "Certificates that need it (Date of Birth, Regular Student) cannot be issued " +
                    "until it is added on the student's record.\n\nSave the admission without a date of birth?",
                    "Date of Birth Not Entered"))
            {
                dob.Focus();
                return;
            }
            if (!await ConfirmNotDuplicateAsync(studentName, fatherName.Text.Trim()))
                return;
            /* Read before anything is written: an unreadable discount stops the save here, where nothing has happened yet, rather than after the student is admitted. */
            var typedDiscounts = new List<(int feeTypeId, decimal discount)>();
            if (!HistoricalAdmission && !TryBuildTypedDiscountsByFeeType(out typedDiscounts))
                return;
            string? bloodGroup =
                blood.SelectedIndex > 0
                    ? blood.Text.Trim()
                    : null;
            if (HistoricalAdmission) typedDiscounts.Clear();
            var admissionParameters = new List<SqlParameter> {
                DatabaseHelper.P("@AdmissionNumber", string.IsNullOrWhiteSpace(admissionIdValue.Text) ? null : admissionIdValue.Text.Trim()),
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Date(
                    "@AdmissionDate",
                    admissionDate.Value),
                DatabaseHelper.Int(
                    "@AdmissionCampusId",
                    Campus.SelectedId),
                DatabaseHelper.Int(
                    "@AcademicYearId",
                    Year.SelectedId),
                DatabaseHelper.Int(
                    "@ClassSectionId",
                    Section.SelectedId),
                DatabaseHelper.P(
                    "@StudentName",
                    studentName),
                DatabaseHelper.P(
                    "@FatherName",
                    fatherName.Text.Trim()),
                DatabaseHelper.P(
                    "@DateOfBirth",
                    dob.Checked
                        ? dob.Value.Date
                        : (object?)null),
                DatabaseHelper.P(
                    "@PlaceOfBirth",
                    placeOfBirthInput.Text.Trim()),
                DatabaseHelper.P(
                    "@Gender",
                    gender.SelectedIndex >= 0
                        ? gender.Text
                        : null),
                DatabaseHelper.P(
                    "@BFormNumber",
                    NationalIdentityNumber.NormalizeOptional(bform.Text)),
                DatabaseHelper.P(
                    "@Religion",
                    religion.Text.Trim()),
                DatabaseHelper.P(
                    "@BloodGroup",
                    bloodGroup),
                DatabaseHelper.P(
                    "@ContactNumber",
                    contact.Text.Trim()),
                DatabaseHelper.P(
                    "@AlternateContactNumber",
                    altContact.Text.Trim()),
                DatabaseHelper.P(
                    "@Address",
                    address.Text.Trim()),
                DatabaseHelper.P(
                    "@PreviousSchool",
                    prevSchool.Text.Trim()),
                DatabaseHelper.P(
                    "@FatherOccupation",
                    fatherOccupation.Text.Trim()),
                DatabaseHelper.P(
                    "@Disability",
                    disability.SelectedIndex >= 0
                        ? disability.Text
                        : null),
                DatabaseHelper.P(
                    "@BiseEnrolmentNumber",
                    enrolmentNo.Text.Trim()),
                DatabaseHelper.P(
                    "@CasteTribe",
                    casteTribe.Text.Trim()),
                DatabaseHelper.Bit(
                    "@UsesTransport",
                    UsesTransport),
                /* Every fee row the operator has on this grid travels to the database, mandatory fees excepted - AdmitStudent always assigns those straight from Fee Configurations. */
                new SqlParameter(
                    "@SelectedFeeTypeIds",
                    System.Data.SqlDbType.Structured) {
                    TypeName = "dbo.FeeTypeIdList",
                    Value = Tvp.FeeTypeIds(HistoricalAdmission ? Array.Empty<int>() :
                        _selectedFees
                            .Where(f => !f.IsMandatory)
                            .Select(f => f.FeeTypeId))
                },
                new SqlParameter(
                    "@AdmissionDiscounts",
                    System.Data.SqlDbType.Structured) {
                    TypeName = "dbo.AdmissionFeeDiscountType",
                    Value = Tvp.AdmissionFeeDiscounts(typedDiscounts)
                } };
            if (HistoricalAdmission)
            {
                admissionParameters.Add(DatabaseHelper.P("@VerifiedEnrollmentDate", firstEnrollmentDate.Checked ? firstEnrollmentDate.Value.Date : null));
                admissionParameters.Add(DatabaseHelper.P("@VerifiedStatus", firstPlacementStatus.Text));
                admissionParameters.Add(DatabaseHelper.P("@VerifiedWithdrawalDate", firstWithdrawalDate.Checked ? firstWithdrawalDate.Value.Date : null));
            }
            bool savedHistorical = HistoricalAdmission;
            admissionNumberPlan=await AdmissionNumberPolicy.LoadAsync(admissionDate.Value.Date,Year.SelectedId,savedHistorical);
            if(admissionNumberPlan.Validate(admissionIdValue.Text) is { } numberingProblem)
            {
                Warn(numberingProblem);
                return;
            }
            if(!AdmissionNumbers.TryNormalize(admissionIdValue.Text,out string cleanAdmissionNumber)) return;
            admissionParameters.First(parameter=>parameter.ParameterName=="@AdmissionNumber").Value=cleanAdmissionNumber;
            var submitted = new AdmissionSubmission(cleanAdmissionNumber, studentName,
                Convert.ToString(admissionParameters.First(p => p.ParameterName == "@FatherName").Value) ?? "",
                Convert.ToDateTime(admissionParameters.First(p => p.ParameterName == "@AdmissionDate").Value),
                Convert.ToInt32(admissionParameters.First(p => p.ParameterName == "@AdmissionCampusId").Value),
                savedHistorical, typedDiscounts.Count);
            int submittedVersion = admissionLoadVersion;
            unconfirmedAdmission = submitted;
            var confirmation = await AdmissionSaveConfirmation.SubmitAsync(submitted,
                () => DatabaseHelper.QueryUserSetAsync(savedHistorical ? "AdmitHistoricalStudent" : "AdmitStudent", admissionParameters.ToArray()),
                ReadSubmittedAdmissionAsync);
            if (IsDisposed || submittedVersion != admissionLoadVersion) return;
            await ApplyAdmissionConfirmationAsync(submitted, confirmation);
        }
        catch (Exception ex)
        {
            if (unconfirmedAdmission is not null) ShowUnconfirmedAdmission();
            else Fail(ex);
            AppExceptionHandler.LogNonFatal(ex, "Saving a new admission");
        }
    }

    private static Task<DataTable> ReadSubmittedAdmissionAsync(AdmissionSubmission submitted) =>
        DatabaseHelper.QuerySqlAsync("""
            EXEC dbo.RequirePermission @UserId,N'Students.Admit';
            SELECT AdmissionId,AdmissionNumber,RegistrationNumber,StudentName,FatherName,AdmissionDate,AdmissionCampusId
            FROM dbo.Students WHERE AdmissionNumber=@AdmissionNumber;
            """, DatabaseHelper.Int("@UserId", CurrentUser.UserId), DatabaseHelper.P("@AdmissionNumber", submitted.Number));

    private void ShowUnconfirmedAdmission()
    {
        if (unconfirmedAdmission is not { } submitted) return;
        savedAdmissionReadyForNext = false;
        SetStatus($"Admission {submitted.Number}: the save outcome could not be confirmed. Your entries and photo are retained. " +
            "Use Check Saved Admission or Find Student. Do not submit this student again until you verify whether the admission exists.", PageStatusKind.Warning);
        UpdateStudentWorkspace();
    }

    private async Task RecheckAdmissionAsync()
    {
        if (IsBusy || unconfirmedAdmission is not { } submitted) return;
        int version = admissionLoadVersion;
        saveBusy = true; UpdateStudentWorkspace();
        try
        {
            using var hold = BeginBusy("Checking the saved admission…", this);
            var confirmation = await AdmissionSaveConfirmation.RecheckAsync(submitted, ReadSubmittedAdmissionAsync);
            if (IsDisposed || version != admissionLoadVersion || !ReferenceEquals(unconfirmedAdmission, submitted)) return;
            await ApplyAdmissionConfirmationAsync(submitted, confirmation);
        }
        finally { saveBusy = false; if (!IsDisposed) UpdateStudentWorkspace(); }
    }

    private async Task ApplyAdmissionConfirmationAsync(AdmissionSubmission submitted, AdmissionConfirmation confirmation)
    {
        if (confirmation.WriteError is { } writeError) AppExceptionHandler.LogNonFatal(writeError, "Admission write response");
        if (confirmation.ReadError is { } readError) AppExceptionHandler.LogNonFatal(readError, "Checking admission after an uncertain write");
        if (confirmation.Admission is not { } admitted)
        {
            unconfirmedAdmission = submitted;
            ShowUnconfirmedAdmission();
            return;
        }
        unconfirmedAdmission = null;
        StudentSearchCache.Invalidate();
        currentAdmissionId = admitted.Id;
        registrationNo.Text = admitted.RegistrationNumber;
        int version = admissionLoadVersion;
        // Confirmation precedes all photo writes, reloads and success messages.
        bool photoSaved = await PersistPendingPhotoIfAnyAsync(admitted.Id);
        if (IsDisposed || currentAdmissionId != admitted.Id || version != admissionLoadVersion) return;
        bool reloaded = false;
        string reloadError = "";
        try { reloaded = await LoadStudentForEditAsync(admitted.Id); }
        catch (Exception ex)
        {
            reloadError = DbMessage.UserMessage(ex);
            AppExceptionHandler.LogNonFatal(ex, "Refreshing a confirmed admission");
        }
        if (IsDisposed || currentAdmissionId != admitted.Id) return;
        AddSuggestionIfNew(fatherOccupation, fatherOccupation.Text);
        AddSuggestionIfNew(prevSchool, prevSchool.Text);
        AddSuggestionIfNew(address, address.Text);
        AddSuggestionIfNew(placeOfBirthInput, placeOfBirthInput.Text);
        AddSuggestionIfNew(casteTribe, casteTribe.Text);
        string message = submitted.Historical ? "Historical student saved for legacy review. No fees were generated."
            : "Student admitted. Placement and opening fees are ready.";
        if (submitted.DiscountCount > 0) message += " The typed fee discount(s) have been saved.";
        if (confirmation.Reconciled) message += " The saved record was verified after an incomplete save reply.";
        message += reloaded ? " The form has been kept open with the saved student details."
            : " The form could not be refreshed; open this student from Find Student before further edits. " + reloadError;
        if (!photoSaved) message += " The selected photo was not saved; it is retained here. Use Retry Photo Save.";
        SetStatus(message, photoSaved && reloaded ? PageStatusKind.Success : PageStatusKind.Warning);
        savedAdmissionReadyForNext = photoSaved && reloaded;
    }
    /// <summary>The edit path uses dbo.SaveStudentDetailsWithFirstEnrollment to save student details, fee discounts and an optional verified first enrollment in one transaction.</summary>
    private async Task SaveDetailsAndDiscountsAsync(
        int admissionId,
        string studentName,
        string? bloodGroup,
        List<(int assignmentId, decimal discount)> feeDiscounts)
    {
        await DatabaseHelper.ExecuteUserAsync(
            "SaveStudentDetailsWithFirstEnrollment",
            DatabaseHelper.Bit("@CreateFirstEnrollment", AssigningFirstPlacement),
            DatabaseHelper.P("@PlacementCampusId", AssigningFirstPlacement ? Campus.SelectedId : null),
            DatabaseHelper.P("@PlacementAcademicYearId", AssigningFirstPlacement ? Year.SelectedId : null),
            DatabaseHelper.P("@PlacementClassSectionId", AssigningFirstPlacement ? Section.SelectedId : null),
            DatabaseHelper.P("@PlacementEnrollmentDate", AssigningFirstPlacement && firstEnrollmentDate.Checked ? firstEnrollmentDate.Value.Date : null),
            DatabaseHelper.P("@PlacementStatus", AssigningFirstPlacement ? firstPlacementStatus.Text : null),
            DatabaseHelper.P("@PlacementLeavingDate", AssigningFirstPlacement && firstWithdrawalDate.Checked ? firstWithdrawalDate.Value.Date : null),
            DatabaseHelper.Bit("@PlacementUsesTransport", AssigningFirstPlacement && UsesTransport),
            DatabaseHelper.P("@AdmissionNumber", IsPendingRegistration ? null : admissionIdValue.Text.Trim()),
            DatabaseHelper.Int(
                "@AdmissionId",
                admissionId),
            DatabaseHelper.P(
                "@StudentName",
                studentName),
            DatabaseHelper.P(
                "@FatherName",
                fatherName.Text.Trim()),
            DatabaseHelper.P(
                "@DateOfBirth",
                dob.Checked
                    ? dob.Value.Date
                    : (object?)null),
            DatabaseHelper.P(
                "@Gender",
                gender.SelectedIndex >= 0
                    ? gender.Text
                    : null),
            DatabaseHelper.P(
                "@BFormNumber",
                NationalIdentityNumber.NormalizeOptional(bform.Text)),
            DatabaseHelper.P(
                "@Religion",
                ReligionToSave()),
            DatabaseHelper.P(
                "@BloodGroup",
                bloodGroup),
            DatabaseHelper.P(
                "@ContactNumber",
                contact.Text.Trim()),
            DatabaseHelper.P(
                "@AlternateContactNumber",
                altContact.Text.Trim()),
            DatabaseHelper.P(
                "@Address",
                address.Text.Trim()),
            DatabaseHelper.P(
                "@PreviousSchool",
                prevSchool.Text.Trim()),
            DatabaseHelper.P(
                "@PlaceOfBirth",
                placeOfBirthInput.Text.Trim()),
            DatabaseHelper.P(
                "@FatherOccupation",
                fatherOccupation.Text.Trim()),
            DatabaseHelper.P(
                "@Disability",
                disability.SelectedIndex >= 0
                    ? disability.Text
                    : null),
            DatabaseHelper.P(
                "@BiseEnrolmentNumber",
                enrolmentNo.Text.Trim()),
            DatabaseHelper.P(
                "@CasteTribe",
                casteTribe.Text.Trim()),
            /* both halves of this Save now travel to one procedure that commits them together. See Migrations\FMSC_FIX_076_AtomicStudentDetailsAndDiscounts.sql. */
            new Microsoft.Data.SqlClient.SqlParameter(
                "@Discounts",
                System.Data.SqlDbType.Structured) {
                TypeName = "dbo.StudentFeeDiscountType",
                Value = Tvp.StudentFeeDiscounts(feeDiscounts)
            });
    }
    private async Task SaveExistingAsync()
    {
        int savedStudent = currentAdmissionId, savedVersion = admissionLoadVersion;
        try
        {
            if (!HasAdmissionEdits)
            {
                if (_pendingPhotoData is not null)
                {
                    int photoStudent = currentAdmissionId, photoVersion = admissionLoadVersion;
                    bool stored = await PersistPendingPhotoIfAnyAsync(photoStudent);
                    if (!IsDisposed && currentAdmissionId == photoStudent && admissionLoadVersion == photoVersion && stored)
                        SetStatus("Photo saved. Student details were not changed.", PageStatusKind.Success);
                }
                else SetStatus("No changes to save.");
                return;
            }
            /* UX - see SaveNewAdmissionAsync: all problems shown at once, inline. */
            if (!ValidateAdmissionInline(isNew: false))
                return;
            string studentName =
                name.Text.Trim();
            if (!ConfirmReasonableAge())
                return;
            string? bloodGroup =
                blood.SelectedIndex > 0
                    ? blood.Text.Trim()
                    : null;
            /* The discount lines are built and validated FIRST: an unparseable discount must abort before the database is touched. */
            if (!TryBuildFeeDiscountPlan(out var feeDiscounts))
                return;
            if (IsDisposed || currentAdmissionId != savedStudent || admissionLoadVersion != savedVersion) return;
            await SaveDetailsAndDiscountsAsync(
                savedStudent,
                studentName,
                bloodGroup,
                feeDiscounts);
            StudentSearchCache.Invalidate();
            if (IsDisposed || currentAdmissionId != savedStudent || admissionLoadVersion != savedVersion) return;
            AddSuggestionIfNew(fatherOccupation, fatherOccupation.Text);
            AddSuggestionIfNew(prevSchool, prevSchool.Text);
            AddSuggestionIfNew(address, address.Text);
            AddSuggestionIfNew(placeOfBirthInput, placeOfBirthInput.Text);
            AddSuggestionIfNew(casteTribe, casteTribe.Text);
            bool hadPhoto = _pendingPhotoData is not null;
            bool photoSaved = await PersistPendingPhotoIfAnyAsync(savedStudent);
            if (IsDisposed || currentAdmissionId != savedStudent || admissionLoadVersion != savedVersion) return;
            string success = AssigningFirstPlacement ? "Student details and verified first placement saved. No fees were generated."
                : "Student details and fee discounts updated successfully.";
            /* The details transaction is committed by now, so a reload that fails is only a stale screen. */
            try
            {
                bool refreshed = await LoadStudentForEditAsync(savedStudent);
                if (IsDisposed || currentAdmissionId != savedStudent) return;
                SetStatus(success + (photoSaved && hadPhoto ? " Photo saved." : "") +
                    (!photoSaved ? " Photo not saved; use Retry Photo Save." : "") +
                    (!refreshed ? " The form could not refresh; reopen the student from Find Student before further edits." : ""),
                    photoSaved && refreshed ? PageStatusKind.Success : PageStatusKind.Warning);
            }
            catch (Exception nonFatalException55)
            {
                FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException55, "StudentAdmissionPage: SaveExistingAsync fallback");
                SetStatus(success + (photoSaved && hadPhoto ? " Photo saved." : "") +
                    (!photoSaved ? " Photo not saved; use Retry Photo Save." : "") +
                    " The form could not refresh. Reopen the student from Find Student before further edits.", PageStatusKind.Warning);
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }
    private static bool IsValidOptionalPhone(string? text) => PakistanPhoneNumber.IsValidOptional(text);
    /// <summary>Validates the optional 13-digit B-Form/CRC or CNIC identity number, with or without dashes.</summary>
    private static bool IsValidOptionalBFormOrCnic(string? text)
    {
        string value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
            return true;
        return NationalIdentityNumber.TryNormalizeOptional(value, out _);
    }
    /// <summary>Warns (but does not block) when the entered date of birth would put the student's age at admission outside a normal school-age range.</summary>
    private bool ConfirmReasonableAge()
    {
        if (!dob.Checked)
            return true;
        DateTime birth = dob.Value.Date;
        DateTime admission = admissionDate.Value.Date;
        int age = admission.Year - birth.Year;
        if (birth.Date > admission.AddYears(-age))
            age--;
        if (age is >= 1 and <= 25)
            return true;
        return Confirm(
            $"Based on the dates entered, this student would be about {age} year(s) old " +
            "at admission, which is unusual for a school student.\n\n" +
            "Please double-check the Date of Birth and Admission Date fields.\n\n" +
            "Continue saving with this age anyway?",
            "Unusual Age");
    }
    /// <summary>Best-effort check for an existing active/inactive student with the same name and father's name before a brand-new admission is created.</summary>
    private async Task<bool> ConfirmNotDuplicateAsync(string studentName, string fatherNameText)
    {
        if (string.IsNullOrWhiteSpace(studentName))
            return true;
        try
        {
            var raw = await DatabaseHelper.QueryUserAsync(
                "GetStudents",
                DatabaseHelper.Int("@CampusId", 0),
                DatabaseHelper.Int("@AcademicYearId", 0),
                DatabaseHelper.P("@Search", studentName),
                DatabaseHelper.Bit("@IncludeFormerStudents", true));
            if (!raw.Columns.Contains("StudentName"))
                return true;
            var matches = raw.AsEnumerable().Where(row =>
                string.Equals(
                    Convert.ToString(row["StudentName"])?.Trim(),
                    studentName,
                    StringComparison.OrdinalIgnoreCase) &&
                (!raw.Columns.Contains("FatherName") ||
                 string.IsNullOrWhiteSpace(fatherNameText) ||
                 string.Equals(
                     Convert.ToString(row["FatherName"])?.Trim(),
                     fatherNameText,
                     StringComparison.OrdinalIgnoreCase)))
                .Take(5)
                .ToList();
            if (matches.Count == 0)
                return true;
            // Strengthened: show the actual matched record(s) side-by-side with what's being entered now.
            string DescribeMatch(DataRow row)
            {
                // Returns null for a column that is absent or DBNull; every caller below already handles that.
                string? Col(string name) =>
                    row.Table.Columns.Contains(name) && row[name] != DBNull.Value
                        ? Convert.ToString(row[name])
                        : null;
                var admissionNumber = Col("AdmissionNumber") ?? "?";
                var registrationNumber = Col("RegistrationNumber");
                var dob = row.Table.Columns.Contains("DateOfBirth") && row["DateOfBirth"] != DBNull.Value
                    ? Convert.ToDateTime(row["DateOfBirth"]).ToString("dd-MMM-yyyy")
                    : null;
                var className = Col("ClassName");
                var sectionName = Col("SectionName");
                var status = Col("Status") ?? Col("EnrollmentStatus");
                var parts = new List<string> { $"Admission Number {admissionNumber}" };
                _ = registrationNumber; // kept for the data read; the registration number is not shown in this warning
                if (!string.IsNullOrWhiteSpace(dob)) parts.Add($"DOB {dob}");
                if (!string.IsNullOrWhiteSpace(className))
                    parts.Add(string.IsNullOrWhiteSpace(sectionName) ? className : $"{className} / {sectionName}");
                if (!string.IsNullOrWhiteSpace(status)) parts.Add(status);
                return "  • " + string.Join("  |  ", parts);
            }
            var matchList = string.Join("\n", matches.Select(DescribeMatch));
            var message =
                $"A student named \"{studentName}\"" +
                (string.IsNullOrWhiteSpace(fatherNameText) ? "" : $" (father: \"{fatherNameText}\")") +
                " already exists in the system:\n\n" +
                matchList +
                "\n\nCompare the details above with what you're entering now.\n" +
                "If this is the SAME child, do not create a new record -- use \"Find Possible Sibling\" " +
                "only for an actual sibling, or open the existing record instead.\n\n" +
                "Are you certain this is a DIFFERENT student and should be admitted as new?";
            return ModernMessageDialog.Show(
                FindForm(),
                message,
                "Possible Duplicate Student",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
        catch (Exception nonFatalException56)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException56, "StudentAdmissionPage: ConfirmNotDuplicateAsync fallback");
            // Best-effort only: if the duplicate check itself fails (e.g. no network/DB hiccup), don't block the actual admission on it.
            return true;
        }
    }
    private void ClearAll(
    bool clearGenerated = true, bool retainPlacement = false)
    {
        /* any student load still in flight belongs to the form this clear is replacing, so it must not land on the cleared page. */
        admissionLoadVersion++;
        unconfirmedAdmission = null;
        photoRead.Cancel();
        pendingPlacementId = 0;
        if (confirmPlacementAction is not null) confirmPlacementAction.Visible = false;
        if (confirmReturnButton is not null) confirmReturnButton.Visible = false;
        feeStructureLoadVersion++;
        admissionValidator?.ClearAll();
        currentAdmissionId = 0;
        /* The quick-find box names the student that was on the form, which this clear is replacing. */
        quickStudent.Clear();
        UpdateAdmissionIdDisplay();
        _editEnrollmentId = 0;
        _editCampusId = 0;
        _editYearId = 0;
        _editClassId = 0;
        name.Clear();
        bform.Clear();
        enrolmentNo.Clear();
        casteTribe.Clear();
        religion.SelectedIndex = 0;
        fatherName.Clear();
        contact.Clear();
        altContact.Clear();
        address.Clear();
        placeOfBirthInput.Clear();
        prevSchool.Clear();
        fatherOccupation.Clear();
        gender.SelectedIndex = -1;
        if (disability.Items.Count > 0)
            disability.SelectedIndex = 0;
        if (blood.Items.Count > 0)
            blood.SelectedIndex = 0;
        if (transport.Items.Count > 0)
            transport.SelectedIndex = 0;
        if (clearGenerated)
            registrationNo.Clear();
        initializingFirstPlacement = true;
        assignFirstPlacement.Checked = false;
        firstPlacementStatus.SelectedItem = "Active";
        firstEnrollmentDate.Checked = false;
        firstWithdrawalDate.Checked = false;
        initializingFirstPlacement = false;
        feeGrid.Enabled = true;
        registrationNo.PlaceholderText =
            "Assigned after admission";
        admissionDate.Value =
            DateTime.Today;
        dob.Value =
            DateTime.Today.AddYears(-5);
        dob.Checked = false;
        if (!retainPlacement)
        {
            if (Campus.Items.Count > 0) Campus.SelectedIndex = -1;
            if (Year.Items.Count > 0) Year.SelectedIndex = -1;
            if (Section.Items.Count > 0) Section.SelectedIndex = -1;
            if (Class.Items.Count > 0) Class.SelectedIndex = -1;
        }
        ShowPlacementSummary(false);
        viewRegistrationNo.Text = "-";
        viewAdmissionDate.Text = "-";
        viewEnrollmentDate.Text = "-";
        editEnrollmentDate.Text = "-";
        viewYear.Text = "-";
        if (viewYearCaption is not null) viewYearCaption.Text = "Current Academic Year";
        viewYear.AccessibleName = "Current Academic Year";
        viewCampus.Text = "-";
        viewClass.Text = "-";
        viewSection.Text = "-";
        viewTransport.Text = "-";
        feePreviewMonth.Enabled=false;
        feePreviewEnrollmentId=0;
        _selectedFees.Clear();
        _availableFeeOptions = new List<FeeConfigOption>();
        feeGrid.DataSource = _selectedFees;
        feeStatus.Text =
            "Select Campus, Academic Year and Class to load fees.";
        siblingsGrid.DataSource = null;
        siblingsStatus.Text =
            "Save the admission first to manage siblings.";
        photoDraft.Discard();
        _loadedReligionNotInList = null;
        StudentPhotoViewForm.SetPreviewImage(photoPreview, null);
        photoInfo.Text = string.Empty;
        SetStatus(
            "New admission");
        UpdateStudentWorkspace();
        SwitchTab(0);
        UpdateAdmissionEntryFlow();
        name.Focus();
        /* back to "new admission" mode, so the header never keeps claiming the last record after the form has been emptied. */
        SetPageHeaderText(
            PageTitles.Admissions,
            AdmissionContextSubtitle);
        /* An empty form is clean, so the baseline is captured here too. */
        CaptureAdmissionBaseline();
        if (retainPlacement && Campus.SelectedId > 0 && Year.SelectedId > 0 && Class.SelectedId > 0)
        {
            int clearedVersion = admissionLoadVersion;
            var clearedFields = AdmissionParts(false).Where(part => !part.Where.StartsWith("fee row for fee type ", StringComparison.Ordinal)).ToArray();
            Defer(async () => {
                await LoadFeeStructure();
                if (IsDisposed || clearedVersion != admissionLoadVersion || currentAdmissionId > 0)
                    return;
                var currentFields = AdmissionParts(_pendingPhotoData is not null)
                    .Where(part => !part.Where.StartsWith("fee row for fee type ", StringComparison.Ordinal));
                // Automatic mandatory-fee defaults are clean; concurrent typing remains dirty.
                if (clearedFields.SequenceEqual(currentFields) &&
                    _selectedFees.All(fee => fee.IsMandatory && fee.DiscountAmount == 0))
                    CaptureAdmissionBaseline();
            });
        }
    }
}


