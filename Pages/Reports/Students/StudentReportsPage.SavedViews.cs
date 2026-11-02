using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Reports.Students;

public partial class StudentReportsPage
{
    private readonly ModernComboBox datePeriod=new();
    private readonly ModernButton renameViewButton;
    private bool resolvingDatePeriod;

    private void ResolveStudentDatePeriod()
    {
        if (resolvingDatePeriod) return;
        resolvingDatePeriod=true;
        try
        {
            string period=datePeriod.Text;
            if (period==ReportDatePeriod.Fixed) { limitDate.Checked=true;return; }
            if (period=="All dates") { limitDate.Checked=false;return; }
            var range=ReportDatePeriod.Resolve(period,DateTime.Today);
            if (range.From is { } start) from.Value=start;
            if (range.To is { } end) to.Value=end;
            limitDate.Checked=true;
        }
        finally { resolvingDatePeriod=false; }
    }
    private void StudentDateEdited()
    {
        if (!_suppress && !resolvingDatePeriod) datePeriod.SelectedItem=ReportDatePeriod.Fixed;
        OnFilterChanged();
    }
    private void RenameSelectedView()
    {
        string original=SelectedSavedViewName();if (original.Length==0) return;
        string? name=ReportNameDialog.Prompt(FindForm(),"Rename View","New name for this saved view:",original);
        if (name is null) return;
        var result=ReportLayoutStore.RenameView(ViewsKey,original,name);
        if (!result.Succeeded) { SetStatus("The view was not renamed. "+DbMessage.UserMessage(result.Error!),PageStatusKind.Warning);return; }
        RefreshViewChoices(name);SetStatus(result.Changed ? $"View renamed to \"{name}\"." : "The saved view was unchanged.");
    }
    private bool SavedLocationsAvailable(IReadOnlyDictionary<string,string> state)
    {
        if (campus.DataSource is null || _years is null)
        { Warn("Wait for the report filters to load before applying a saved view.");return false; }
        foreach(string key in new[]{"campus","year","compare","class","section"})
            if (state.TryGetValue(key,out string? value) && (!int.TryParse(value,out int id) || id<0))
            { Warn("This saved view contains an invalid location or year. Review the filters and save it again.");return false; }
        bool InLookup(LookupBox lookup,int id) => id==0 || lookup.Items.Cast<object>().Any(item => item is DataRowView r && Convert.ToInt32(r[lookup.ValueMember])==id);
        bool InTable(DataTable? table,string column,int id) => id==0 || table is not null && table.Columns.Contains(column)
            && table.AsEnumerable().Any(r=>Convert.ToInt32(r[column])==id);
        if (!InLookup(campus,GetInt(state,"campus")) || !InTable(_years,"AcademicYearId",GetInt(state,"year"))
            || !InTable(_years,"AcademicYearId",GetInt(state,"compare"))
            || !InTable(_combos ?? _allClasses,"ClassId",GetInt(state,"class"))
            || !InTable(_combos ?? _allSections,"SectionId",GetInt(state,"section")))
        { Warn("This saved view refers to a location or year that is no longer available. Review the filters and save the view again.");return false; }
        int campusId=GetInt(state,"campus"),classId=GetInt(state,"class"),sectionId=GetInt(state,"section");
        if (_combos is not null && (classId>0 || sectionId>0) && !_combos.AsEnumerable().Any(r=>
            (campusId==0 || Convert.ToInt32(r["CampusId"])==campusId) && (classId==0 || Convert.ToInt32(r["ClassId"])==classId)
            && (sectionId==0 || Convert.ToInt32(r["SectionId"])==sectionId)))
        { Warn("The saved campus, class and section are no longer available together. Review the filters and save the view again.");return false; }
        if (Get(state,"completion").Length>0 && !completionFilter.Items.Cast<Choice>().Any(c=>c.Key==Get(state,"completion")))
        { Warn("The saved record check is not available with your current permissions.");return false; }
        string period=Get(state,"datePeriod",Get(state,"limit")=="1" ? ReportDatePeriod.Fixed : "All dates");
        if (!ReportDatePeriod.Choices.Contains(period)) { Warn("This view's saved date period is not supported.");return false; }
        return true;
    }
}
