using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Setup;
public sealed class CampusesPage : MasterPage
{
    public CampusesPage() : base(
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
        splitEditorAndRecords: true,
        reorderProcedure: "ReorderCampuses",
        reorderItemField: "CampusId",
        getParams: () => new[] { DatabaseHelper.Int("@UserId", CurrentUser.UserId) },
        deleteProcedure: "DeleteCampus",
        deleteIdParameter: "@CampusId",
        deleteDisplayField: "CampusName",
        usageCountProcedure: "GetCampusUsageCount",
        usageCountIdParameter: "@CampusId",
        usageCountLabel: "active student(s) enrolled",
        deleteBlockedProcedure: "GetCampusDeleteBlockedIds")
    {
    }
}
