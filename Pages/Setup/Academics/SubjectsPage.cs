using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Setup.Academics;
public sealed class SubjectsPage : MasterPage
{
    public SubjectsPage() : base(
        "Subjects",
        "Add the subjects taught by the school or college.",
        "GetSubjects",
        "SaveSubject",
        new[]
        {
            new FieldSpec("SubjectCode", "Code", Required: true),
            new FieldSpec("SubjectName", "Subject Name", Required: true),
            new FieldSpec("Description", "Description")
        },
        v => new[]
        {
            DatabaseHelper.P("@UserId", CurrentUser.UserId),
            DatabaseHelper.Int("@SubjectId", Convert.ToInt32(v["Id"] ?? 0)),
            DatabaseHelper.P("@SubjectCode", v["SubjectCode"]),
            DatabaseHelper.P("@SubjectName", v["SubjectName"]),
            DatabaseHelper.P("@Description", v["Description"])
        },
        splitEditorAndRecords: true,
        reorderProcedure: "ReorderSubjects",
        reorderItemField: "SubjectId",
        getParams: () => new[] { DatabaseHelper.Int("@UserId", CurrentUser.UserId) },
        deleteProcedure: "DeleteSubject",
        deleteIdParameter: "@SubjectId",
        deleteDisplayField: "SubjectName",
        deleteBlockedProcedure: "GetSubjectDeleteBlockedIds")
    {
    }
}
