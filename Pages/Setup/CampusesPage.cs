using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Setup;
public sealed class CampusesPage : MasterPage
{
    private FormValidator? _contactValidator;
    protected override bool ReserveEditorValidationSpace => true;
    protected override int EditorValidationLines => 2;
    protected override bool EditorValidationSpansCaption => true;
    private FormValidator ContactValidator
    {
        get
        {
            if (_contactValidator is null)
            {
                _contactValidator = CreateValidator();
                BindEditorValidationMessages(_contactValidator);
            }
            return _contactValidator;
        }
    }
    protected override bool CanSaveRecord()
    {
        var validator = ContactValidator;
        validator.Begin();
        validator.RequiredFields(this);
        if (TryGetEditorControl("ContactNumber", out var phone) && phone is not null)
            validator.Rule(phone, () => SchoolContactValidation.IsValidPhoneList(phone.Text), SchoolContactValidation.PhoneMessage);
        if (TryGetEditorControl("Email", out var email) && email is not null)
            validator.Rule(email, () => EmailAddressValidator.IsBlankOrValid(email.Text), SchoolContactValidation.EmailMessage);
        return validator.Finish();
    }
    protected override void EditorCleared(bool afterSave)
    {
        base.EditorCleared(afterSave);
        _contactValidator?.ClearAll();
    }
    protected override void RecordLoaded(DataGridViewRow row)
    {
        base.RecordLoaded(row);
        _contactValidator?.ClearAll();
    }
    public CampusesPage() : this(true) { }
    internal CampusesPage(bool initialize) : base(
        "Campuses",
        "Add and maintain each school or college location.",
        "GetCampuses",
        "SaveCampus",
        new[]
        {
            new FieldSpec("CampusCode", "Code", Required: true),
            new FieldSpec("CampusName", "Name", Required: true),
            new FieldSpec("CampusType", "Type", FieldKind.ComboString, Required: true, Options: new[] { "School", "College", "Combined", "Other" }),
            new FieldSpec("AddressLine", "Address"),
            new FieldSpec("City", "City"),
            new FieldSpec("ContactNumber", "Contact"),
            new FieldSpec("Email", "Email"),
                    },
        v => new[]
        {
            DatabaseHelper.P("@UserId", CurrentUser.UserId),
            DatabaseHelper.Int("@CampusId", Convert.ToInt32(v["Id"] ?? 0)),
            DatabaseHelper.P("@CampusCode", v["CampusCode"]),
            DatabaseHelper.P("@CampusName", v["CampusName"]),
            DatabaseHelper.P("@CampusType", v["CampusType"]),
            DatabaseHelper.P("@AddressLine", v["AddressLine"]),
            DatabaseHelper.P("@City", v["City"]),
            DatabaseHelper.P("@ContactNumber", v["ContactNumber"]),
            DatabaseHelper.P("@Email", v["Email"])
        },
        splitEditorAndRecords: false,
        reorderProcedure: "ReorderCampuses",
        reorderItemField: "CampusId",
        getParams: () => new[] { DatabaseHelper.Int("@UserId", CurrentUser.UserId) },
        deleteProcedure: "DeleteCampus",
        deleteIdParameter: "@CampusId",
        deleteDisplayField: "CampusName",
        usageCountProcedure: "GetCampusUsageCount",
        usageCountIdParameter: "@CampusId",
        usageCountLabel: "active student(s) enrolled",
        deleteBlockedProcedure: "GetCampusDeleteBlockedIds",
        initialize: initialize)
    {
        if (TryGetEditorControl("ContactNumber", out var phone) && phone is ModernTextBox contact)
            SchoolContactInput.AttachPhone(contact, () => ContactValidator);
        if (TryGetEditorControl("Email", out var email) && email is ModernTextBox address)
            SchoolContactInput.AttachEmail(address, () => ContactValidator);
    }
}
