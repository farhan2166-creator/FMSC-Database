using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Staff;
public sealed class StaffProfilePage : MasterPage
{
    public sealed record Selection(int Id, string StaffNumber, string StaffName);
    public event Action<Selection>? StaffSelected;
    public event Action<Selection>? StaffSaved;
    public event Action? StaffSelectionCleared;
    public Func<bool>? ContextChangeGuard { get; set; }
    protected override bool CanChangeRecord() => ContextChangeGuard?.Invoke() != false;
    private FormValidator? _staffValidator;
    private string _savedStaffNumber = string.Empty;
    protected override string NewRecordHeaderTitle => "New Staff Member";
    protected override string NewRecordHeaderSubtitle => "Staff number is assigned automatically when saved.";
    protected override string EntryTabCaption => "Staff Details";
    protected override string RecordsTabCaption => "Staff Records";
    protected override bool CanSaveRecord()
    {
        if (EditorRecordId != _lastNotifiedStaffId && !CanChangeRecord()) return false;
        _staffValidator ??= CreateValidator();
        _staffValidator.Begin();
        _staffValidator.RequiredFields(this);
        if (TryGetEditorControl("CNIC", out var cnic) && cnic is not null)
            _staffValidator.Rule(cnic, () => NationalIdentityNumber.TryNormalizeOptional(cnic.Text, out _), "Enter 13 digits, with or without CNIC hyphens.");
        foreach (string key in new[] { "ContactNumber", "AlternateContactNumber" })
            if (TryGetEditorControl(key, out var phone) && phone is not null)
                _staffValidator.Rule(phone, () => IsValidPhone(phone.Text), "Enter 7–15 digits; spaces, +, hyphens and parentheses are allowed.");
        if (TryGetEditorControl("DateOfBirth", out var dob) && dob is ModernDatePicker date)
            _staffValidator.Rule(date, () => !date.Checked || date.Value.Date <= DateTime.Today, "Date of birth cannot be in the future.");
        return _staffValidator.Finish();
    }
    private static bool IsValidPhone(string input)
    {
        string text = input.Trim();
        if (text.Length == 0) return true;
        return System.Text.RegularExpressions.Regex.IsMatch(text, @"\A\+?[0-9 ()-]+\z") &&
            text.Count(c => c is >= '0' and <= '9') is >= 7 and <= 15;
    }
    protected override async Task<int> SaveRecordAsync(SqlParameter[] parameters)
    {
        DataTable saved = await DatabaseHelper.QueryUserAsync("SaveStaff", parameters);
        _savedStaffNumber = Convert.ToString(saved.Rows[0]["StaffNumber"]) ?? string.Empty;
        return Convert.ToInt32(saved.Rows[0]["StaffId"]);
    }
    private int _lastNotifiedStaffId;
    internal void RefreshStaffRecords() => Reload();
    public StaffProfilePage() : base(
        "Staff",
        "Manage staff details and contact information.",
        "GetStaff",
        "SaveStaff",
        new[] {
            new FieldSpec("StaffNumber", "Staff Number", MaxLength: 30),
            new FieldSpec("StaffName", "Name", Required: true),
            new FieldSpec("FatherName", "Father Name"),
            new FieldSpec("DateOfBirth", "Date of Birth", FieldKind.Date),
            new FieldSpec("Gender", "Gender", FieldKind.ComboString, Options: new[] { "Male", "Female", "Other" }),
            new FieldSpec("CNIC", "CNIC"),
            new FieldSpec("ContactNumber", "Contact"),
            new FieldSpec("AlternateContactNumber", "Alternate Contact"),
            new FieldSpec("Address", "Address"),
            new FieldSpec("Qualification", "Qualification"),
            new FieldSpec("Specialization", "Specialization")
        },
        v => new[] {
            DatabaseHelper.P("@UserId", CurrentUser.UserId),
            DatabaseHelper.Int("@StaffId", Convert.ToInt32(v["Id"] ?? 0)),
            DatabaseHelper.P("@StaffNumber", v["StaffNumber"]),
            DatabaseHelper.P("@StaffName", v["StaffName"]),
            DatabaseHelper.P("@FatherName", v["FatherName"]),
            DatabaseHelper.P("@DateOfBirth", v["DateOfBirth"]),
            DatabaseHelper.P("@Gender", v["Gender"]),
            DatabaseHelper.P("@CNIC", NationalIdentityNumber.NormalizeOptional(Convert.ToString(v["CNIC"]))),
            DatabaseHelper.P("@ContactNumber", v["ContactNumber"]),
            DatabaseHelper.P("@AlternateContactNumber", v["AlternateContactNumber"]),
            DatabaseHelper.P("@Address", v["Address"]),
            DatabaseHelper.P("@Qualification", v["Qualification"]),
            DatabaseHelper.P("@Specialization", v["Specialization"])
        },
        // this page is reached with Staff.View, but dbo.SaveStaff requires Staff.Manage, so MasterPage hides Save for a view-only staff user.
        savePermission: "Staff.Manage",
        separateEntryAndRecords: true)
    {
        if (TryGetEditorControl("StaffNumber", out var number) && number is ModernTextBox text)
        {
            text.ReadOnly = true;
            text.TabStop = false;
            text.PlaceholderText = "Automatic on save";
        }
        if (TryGetEditorControl("CNIC", out var cnic) && cnic is ModernTextBox identity)
            identity.PlaceholderText = "12345-1234567-1";
        foreach (string key in new[] { "ContactNumber", "AlternateContactNumber" })
            if (TryGetEditorControl(key, out var phone) && phone is ModernTextBox contact)
                contact.PlaceholderText = "0300 1234567 or +92 300 1234567";
    }
    protected override void AfterSave(int id, Dictionary<string, object?> values)
    {
        try
        {
            StaffSaved?.Invoke(new Selection(
                id,
                _savedStaffNumber,
                Convert.ToString(values["StaffName"]) ?? string.Empty));
            _lastNotifiedStaffId = id;
        }
        catch (Exception nonFatalException41)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException41, "StaffPage: AfterSave fallback");
        }
        base.AfterSave(id, values);
    }
    protected override void EditorCleared(bool afterSave)
    {
        base.EditorCleared(afterSave);
        if (afterSave) return;
        _lastNotifiedStaffId = 0;
        StaffSelectionCleared?.Invoke();
    }
    protected override void RecordLoaded(DataGridViewRow loadedRow)
    {
        try
        {
            var row = loadedRow;
            if (row is null) return;
            int id = 0;
            foreach (DataGridViewCell cell in row.Cells)
            {
                string col = cell.OwningColumn?.Name ?? string.Empty;
                if (string.Equals(col, "StaffId", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(col, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    if (cell.Value != null && cell.Value != DBNull.Value)
                    {
                        int.TryParse(Convert.ToString(cell.Value), out id);
                        break;
                    }
                }
            }
            if (id == 0)
            {
                var first = row.Cells.Cast<DataGridViewCell>().FirstOrDefault(c => c.Value != null && c.Value != DBNull.Value);
                if (first != null)
                    int.TryParse(Convert.ToString(first.Value), out id);
            }
            if (id <= 0 || id == _lastNotifiedStaffId)
                return;
            _lastNotifiedStaffId = id;
            StaffSelected?.Invoke(new Selection(
                id,
                CellText(row, "StaffNumber"),
                CellText(row, "StaffName")));
        }
        catch (Exception nonFatalException42)
        {
            FMSC_SMS.Core.AppExceptionHandler.LogNonFatal(nonFatalException42, "StaffPage: RecordLoaded fallback");
        }
    }
    private static string CellText(DataGridViewRow row, string columnName)
    {
        DataGridViewCell? cell = row.Cells
            .Cast<DataGridViewCell>()
            .FirstOrDefault(candidate => string.Equals(
                candidate.OwningColumn?.Name,
                columnName,
                StringComparison.OrdinalIgnoreCase));
        return cell?.Value is null or DBNull
            ? string.Empty
            : Convert.ToString(cell.Value) ?? string.Empty;
    }
}
