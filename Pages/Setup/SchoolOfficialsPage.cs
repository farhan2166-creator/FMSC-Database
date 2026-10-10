using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Setup;

public sealed class SchoolOfficialsPage : MasterPage
{
    public SchoolOfficialsPage()
        : base(
            "School Officials",
            "Add the names and roles shown on school documents.",
            "GetSchoolOfficials",
            "SaveSchoolOfficial",
            new[]
            {
                new FieldSpec(
                    "Designation",
                    "Designation",
                    Required: true),
                new FieldSpec(
                    "PersonName",
                    "Person Name",
                    Required: true),
            },
            v => new[]
            {
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@SchoolOfficialId",
                    Convert.ToInt32(v["Id"] ?? 0)),
                DatabaseHelper.P(
                    "@Designation",
                    v["Designation"]),
                DatabaseHelper.P(
                    "@PersonName",
                    v["PersonName"])
            },
            splitEditorAndRecords: true,
            recordIdColumn: "SchoolOfficialId",
            guidance:
                "Use one official per designation. Click a record to edit it.",
            getParams: () => new[]
            {
                DatabaseHelper.Int(
                    "@UserId",
                    CurrentUser.UserId)
            })
    {
    }
}
