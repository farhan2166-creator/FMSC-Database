using FMSC_SMS.Pages.Security;

namespace FMSC_SMS.Core;

// BD-22: derive year choices from saved academic-year evidence; do not restore the removed Passed Exam gate.
internal static class CharacterCertificateOptions
{
    internal static readonly string[] Conduct={"Good","Very Good","Excellent"};

    internal static DataTable ReadAcademicYears()=>DatabaseHelper.QuerySql("""
        EXEC dbo.RequirePermission @UserId,N'Certificates.Create';
        SELECT AcademicYearId,YearName,StartDate,EndDate FROM dbo.AcademicYears ORDER BY EndDate DESC,AcademicYearId DESC;
        """,DatabaseHelper.Int("@UserId",CurrentUser.UserId));

    internal static void BindYears(LookupBox picker,DataTable years)
    {
        using var choices=years.Clone();
        foreach(DataRow year in years.Rows)
            if(Convert.ToDateTime(year["EndDate"]).Year-1<=DateTime.Today.Year)choices.ImportRow(year);
        choices.Columns.Add("CertificateYearLabel",typeof(string));
        foreach(DataRow year in choices.Rows)
            year["CertificateYearLabel"]=$"{year["YearName"]} (Passing year: {Convert.ToDateTime(year["EndDate"]).Year-1})";
        picker.Bind(choices,"AcademicYearId","CertificateYearLabel");
    }

    internal static int PassingYear(LookupBox picker)=>picker.SelectedItem is DataRowView year
        &&picker.SelectedId>0&&!year.Row.IsNull("EndDate")?Convert.ToDateTime(year["EndDate"]).Year-1:0;

    internal static void SelectYear(LookupBox picker,int passingYear,bool chooseCurrent=false)
    {
        if(picker.DataSource is not DataTable choices)return;
        choices.Rows[0]["CertificateYearLabel"]="-- Select --";
        var chosen=choices.AsEnumerable().FirstOrDefault(row=>Convert.ToInt32(row["AcademicYearId"])>0&&!row.IsNull("EndDate")
            &&(chooseCurrent?Convert.ToDateTime(row["StartDate"])<=DateTime.Today&&Convert.ToDateTime(row["EndDate"])>=DateTime.Today
                :Convert.ToDateTime(row["EndDate"]).Year-1==passingYear));
        picker.SelectedValue=chosen is null?0:chosen["AcademicYearId"];
    }

    internal static void SelectConduct(ModernComboBox picker,string value)
    {
        picker.Items.Clear();picker.Items.AddRange(Conduct);
        picker.SelectedIndex=picker.Items.IndexOf(value);
    }
}
