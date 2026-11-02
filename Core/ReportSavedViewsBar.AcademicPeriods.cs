using System.Globalization;

namespace FMSC_SMS.Core;

internal sealed partial class ReportSavedViewsBar
{
    private readonly Func<DataTable?>? academicYears;
    private readonly Action<int>? selectAcademicYear;
    private readonly ModernButton? changePeriod;
    private int academicYearId;
    private DateTime? academicStart;
    private string lastDatePeriod=ReportDatePeriod.Fixed;
    private object[] DateChoices=>ReportDatePeriod.Choices.Concat(academicYears is null?Array.Empty<string>():ReportAcademicPeriod.Choices).Cast<object>().ToArray();

    private void DateChoiceChanged()
    {
        if(IsApplying||resolving)return;
        if(ReportAcademicPeriod.Months(DatePeriod)>0)ChooseAcademicPeriod(true);
        else { ResolveDates();lastDatePeriod=DatePeriod;RefreshAvailability(); }
    }

    private void ChooseAcademicPeriod(bool changingChoice)
    {
        if(!Available()||academicYears?.Invoke() is not { } years) { RestoreChoice();return; }
        try
        {
            using var dialog=new ReportAcademicPeriodDialog(years,ReportAcademicPeriod.Months(DatePeriod),academicYearId,academicStart);
            if(dialog.ShowDialog(FindForm())!=DialogResult.OK||dialog.SelectedRange is not { } range) { RestoreChoice();return; }
            academicYearId=dialog.YearId;academicStart=range.From;
            ResolveDates();lastDatePeriod=DatePeriod;RefreshAvailability();
        }
        catch(Exception ex) { RestoreChoice();status("The period could not be applied. "+DbMessage.UserMessage(ex),true); }

        void RestoreChoice()
        {
            if(changingChoice)
            {
                resolving=true;
                try { period.SelectedItem=lastDatePeriod; } finally { resolving=false; }
            }
            RefreshAvailability();
        }
    }

    private (DateTime? From,DateTime? To) ResolveAcademicPeriod()
    {
        var years=academicYears?.Invoke();
        var year=years?.AsEnumerable().FirstOrDefault(row=>Convert.ToInt32(row["AcademicYearId"])==academicYearId);
        if(year is null||year.IsNull("StartDate")||year.IsNull("EndDate"))
            throw new ReportViewValidationException("Choose an available academic year and period.");
        var range=ReportAcademicPeriod.Ranges(Convert.ToDateTime(year["StartDate"]),Convert.ToDateTime(year["EndDate"]),ReportAcademicPeriod.Months(DatePeriod))
            .FirstOrDefault(r=>r.From==academicStart);
        if(range is null)throw new ReportViewValidationException("The academic year dates changed. Choose the period again.");
        // Keep reports with their own academic-year filter in the same year.
        resolving=true;
        try { selectAcademicYear?.Invoke(academicYearId); } finally { resolving=false; }
        return (range.From,range.To);
    }

    internal void AcademicYearChanged()
    {
        if(!IsApplying&&!resolving&&ReportAcademicPeriod.Months(DatePeriod)>0)ResetDates();
    }

    private void SaveAcademicPeriod(Dictionary<string,string> state)
    {
        if(ReportAcademicPeriod.Months(DatePeriod)==0)return;
        state["$academicYear"]=academicYearId.ToString(CultureInfo.InvariantCulture);
        state["$academicStart"]=academicStart?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??"";
    }

    private void RestoreAcademicPeriod(IReadOnlyDictionary<string,string> state,string choice)
    {
        if(ReportAcademicPeriod.Months(choice)==0)return;
        if(!int.TryParse(state.GetValueOrDefault("$academicYear"),NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0
            ||!DateTime.TryParseExact(state.GetValueOrDefault("$academicStart"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var start))
            throw new ReportViewValidationException("The saved academic period is incomplete. Choose the period again.");
        academicYearId=id;academicStart=start;
    }
}
