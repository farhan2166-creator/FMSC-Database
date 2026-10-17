using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Setup.Academics;
public sealed class ClassesPage : MasterPage
{
    public ClassesPage(bool splitEditorAndRecords = true)
        : base(
            "Classes",
            "Create the classes students can join under each academic program.",
            "GetClasses",
            "SaveClass",
            new[]
            {
                new FieldSpec(
                    "ProgramType",
                    "Program Type",
                    FieldKind.ComboString,
                    Required: true,
                    Options: new[]
                    {
                        "School",
                        "College"
                    }),
                new FieldSpec(
                    "AcademicProgramId",
                    "Program",
                    FieldKind.ComboInt,
                    Required: true,
                    LookupProcedure: "GetAcademicPrograms",
                    LookupIdColumn: "AcademicProgramId",
                    LookupTextColumn: "ProgramName",
                    LookupParentField: "ProgramType",
                    LookupParentIdColumn: "ProgramType",
                    LookupParentParameter: "@ProgramType",
                    ManageDialogFactory: () => new AcademicProgramManagerForm()),
                new FieldSpec(
                    "ClassCode",
                    "Code",
                    Required: true),
                new FieldSpec(
                    "ClassName",
                    "Class Name",
                    Required: true),
                new FieldSpec(
                    "DisplayOrder",
                    "Display Order",
                    FieldKind.Integer),
            },
            v => new[]
            {
                DatabaseHelper.P(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@ClassId",
                    Convert.ToInt32(
                        v["Id"] ?? 0)),
                DatabaseHelper.Int(
                    "@AcademicProgramId",
                    Convert.ToInt32(
                        v["AcademicProgramId"] ?? 0)),
                DatabaseHelper.P(
                    "@ClassCode",
                    v["ClassCode"]),
                DatabaseHelper.P(
                    "@ClassName",
                    v["ClassName"]),
                DatabaseHelper.Int(
                    "@DisplayOrder",
                    Convert.ToInt32(
                        v["DisplayOrder"] ?? 0))
            },
            splitEditorAndRecords: splitEditorAndRecords,
            reorderProcedure:
                "ReorderClasses",
            reorderScopeField:
                "AcademicProgramId",
            reorderItemField:
                "ClassId",
            deleteProcedure:
                "DeleteClass",
            deleteIdParameter:
                "@ClassId",
            deleteDisplayField:
                "ClassName",
            usageCountProcedure:
                "GetClassUsageCount",
            usageCountIdParameter:
                "@ClassId",
            usageCountLabel:
                "active student(s) enrolled",
            deleteBlockedProcedure:
                "GetClassDeleteBlockedIds",
            getParams: () => new[]
            {
                DatabaseHelper.Int(
                    "@UserId",
                    CurrentUser.UserId),
                DatabaseHelper.Int(
                    "@AcademicProgramId",
                    0)
            })
    {
    }
}
