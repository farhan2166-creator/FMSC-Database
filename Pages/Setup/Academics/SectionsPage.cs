using FMSC_SMS.Core;
namespace FMSC_SMS.Pages.Setup.Academics;
public sealed class SectionsPage : MasterPage
{
    public SectionsPage(bool splitEditorAndRecords = true) : base(
        "Sections",
        "Create reusable section names such as A, B or Morning.",
        "GetSections",
        "SaveSection",
        new[]
        {
            new FieldSpec("SectionCode", "Code", Required: true),
            new FieldSpec("SectionName", "Section Name", Required: true),
            new FieldSpec("Description", "Description", MaxLength: SchemaLengths.SectionDescription)
        },
        v => new[]
        {
            DatabaseHelper.P("@UserId", CurrentUser.UserId),
            DatabaseHelper.Int("@SectionId", Convert.ToInt32(v["Id"] ?? 0)),
            DatabaseHelper.P("@SectionCode", v["SectionCode"]),
            DatabaseHelper.P("@SectionName", v["SectionName"]),
            DatabaseHelper.P("@Description", v["Description"])
        },
        splitEditorAndRecords: splitEditorAndRecords,
        reorderProcedure: "ReorderSections",
        reorderItemField: "SectionId",
        deleteProcedure: "DeleteSection",
        deleteIdParameter: "@SectionId",
        deleteDisplayField: "SectionName",
        usageCountProcedure: "GetSectionUsageCount",
        usageCountIdParameter: "@SectionId",
        usageCountLabel: "active student(s) enrolled",
        deleteBlockedProcedure: "GetSectionDeleteBlockedIds",
        getParams: () => new[] { DatabaseHelper.Int("@UserId", CurrentUser.UserId) })
    {
    }
}
