using System.Data;
using FMSC_SMS.Core;
using FMSC_SMS.Pages.Reports.Certificates;
using FMSC_SMS.Pages.Reports.Finance;
using Microsoft.Reporting.WinForms;

internal static partial class Program
{
    private static void CheckReportStudentIdentity()
    {
        Check(ReportStudentIdentity.Format("163","Kausar","Ali","Female")=="163 - Kausar d/o Ali","Female identity uses admission, name and recorded father");
        Check(ReportStudentIdentity.Format("163a","Hamid","Ali","M")=="163a - Hamid s/o Ali","Male identity preserves admission suffix");
        Check(ReportStudentIdentity.Format("QR-003","Kausar","Ali","")=="QR-003 - Kausar child of Ali","Unknown gender is not guessed from the name");
        Check(ReportStudentIdentity.Format("163","Kausar","", "Female")=="163 - Kausar","Missing parent has no dangling relation");
        Check(ReportStudentIdentity.Format("","Kausar","Ali","F")=="Kausar d/o Ali","Identity works without a selected admission column");
        Check(ReportStudentIdentity.Format("163","","Ali","F")=="163\nFather: Ali","Missing student name does not hide a selected recorded parent");
        string output=Path.GetFullPath("artifacts/report-student-identity");Directory.CreateDirectory(output);
        DataTable Project(DataTable source,params (string Key,string Header)[] fields)
        {
            using var grid=new DataGridView{AutoGenerateColumns=false};
            foreach(var (key,header) in fields)grid.Columns.Add(new DataGridViewTextBoxColumn{Name=key,DataPropertyName=key,HeaderText=header,Visible=true});
            return ReportExportBuilder.Build(source,grid.Columns.Cast<DataGridViewColumn>(),source.Columns.Cast<DataColumn>().Where(c=>c.DataType==typeof(DateTime)).Select(c=>c.ColumnName));
        }
        List<GridReportColumns.Column> Plan(DataTable table)
        {
            var columns=GridReportColumns.Create(table,"#,##0.##");
            Check(columns.SelectMany(c=>c.Fields).OrderBy(i=>i).SequenceEqual(Enumerable.Range(0,table.Columns.Count)),"Every selected source field belongs to exactly one printed column");
            return columns;
        }
        string Cell(DataTable table,GridReportColumns.Column column)=>GridReportColumns.DisplayValue(table.Rows[0],column,"#,##0.##");
        void Render(string filename,string title,DataTable table,params string[] searches)
        {
            var built=GridReportPreview.Build("Fictional Meadow School & College",title,"Campus: All   Class: All",table,"Example Operator");
            using(built.Data)
            using(var report=new LocalReport())
            {
                report.LoadReportDefinition(new StringReader(built.Definition));report.DataSources.Add(new ReportDataSource("ReportData",built.Data));
                byte[] pdf=report.Render("PDF");File.WriteAllBytes(Path.Combine(output,filename+".pdf"),pdf);
                var index=ReportPageSearchIndex.FromPdf(pdf);
                using var parsed=UglyToad.PdfPig.PdfDocument.Open(pdf);
                Check(parsed.NumberOfPages==1,title+" fits a single complete page without horizontal overflow");
                Check(index.FindPages("Fictional Meadow School & College").SequenceEqual(new[]{1}) && index.FindPages(title).SequenceEqual(new[]{1}),title+" retains the school and report headings");
                foreach(string search in searches)Check(index.FindPages(search).SequenceEqual(new[]{1}),title+" PDF contains readable "+search);
                File.WriteAllBytes(Path.Combine(output,filename+".png"),report.Render("IMAGE","<DeviceInfo><OutputFormat>PNG</OutputFormat><DpiX>110</DpiX><DpiY>110</DpiY></DeviceInfo>"));
            }
        }

        using var students=new DataTable();
        foreach(string name in new[]{"AdmissionNumber","StudentName","FatherName","Gender","AdmissionClassSection","StudentStatus","LeavingClassSection","YearName","CampusName","ClassSection"})students.Columns.Add(name);
        students.Columns.Add("DateOfBirth",typeof(DateTime));students.Columns.Add("AdmissionDate",typeof(DateTime));
        students.Columns.Add("FamilyMemberCount",typeof(int));students.Columns.Add("OutstandingBalance",typeof(decimal));
        students.Rows.Add("163","Kausar","Ali","Female","One - F","Active","","2026-2027","Campus Girls","Four - F",new DateTime(2016,2,4),new DateTime(2021,3,1),2,600m);
        // Deliberately put money first to verify natural presentation and unmodified CSV order.
        using var list=Project(students,("OutstandingBalance","Outstanding Balance"),("AdmissionNumber","Admission Number"),("StudentName","Student"),("FatherName","Father"),
            ("DateOfBirth","Date of Birth"),("AdmissionDate","Admission Date"),("AdmissionClassSection","Class Admitted To"),("StudentStatus","Status"),
            ("LeavingClassSection","Class Left"),("YearName","Academic Year"),("CampusName","Campus"),("ClassSection","Class / Section"),("FamilyMemberCount","Family Size"));
        var plan=Plan(list);
        Check(plan[0].Presentation==GridReportColumns.Kind.Student && plan[1].Presentation==GridReportColumns.Kind.Placement,"Identity precedes placement in the printed report even when money is selected first");
        Check(Cell(list,plan[0])=="163 - Kausar d/o Ali\nDOB: 04-02-2016\nStatus: Active","Identity combines selected personal fields with explicit DOB/status labels");
        Check(Cell(list,plan[1]).Contains("Campus Girls\nFour - F\n2026-2027"),"Placement keeps campus, current class and academic year together");
        Check(Cell(list,plan.Single(c=>c.Presentation==GridReportColumns.Kind.Admission))=="Date: 01-03-2021\nClass: One - F","Admission class remains distinct from current and leaving class");
        Check(plan.Count==6 && GridReportColumns.Bands(plan,7.25).Count==1,"Default student columns collapse into one readable section");
        Check(plan.Single(c=>c.Caption=="Outstanding Balance").Numeric && plan.Single(c=>c.Caption=="Family Size").Numeric,"Money and family count remain distinct numeric columns");
        Check(list.Columns[0].ColumnName=="Outstanding Balance" && list.Rows[0].Field<decimal>(0)==600m && list.Rows[0].Field<string>("Student")=="Kausar","Raw exports retain chosen column order, values and numeric types");
        string csv=Path.Combine(output,"student-list.csv");ReportExportBuilder.WriteCsv(list,csv);
        Check(File.ReadAllText(csv).Contains("Father")&&!File.ReadAllText(csv).Contains("d/o"),"CSV retains separate selected fields rather than printed cell text");
        Render("student-list","Student List",list,"163 - Kausar d/o Ali","DOB: 04-02-2016","Class: One - F","600");
        using(var noParent=Project(students,("AdmissionNumber","Admission Number"),("StudentName","Student")))
            Check(Cell(noParent,Plan(noParent)[0])=="163 - Kausar","A deselected father field is not reintroduced by print metadata");
        using(var noAdmission=Project(students,("StudentName","Student"),("FatherName","Father"),("Gender","Gender")))
            Check(Cell(noAdmission,Plan(noAdmission)[0]).StartsWith("Kausar d/o Ali"),"Selected parent and gender still format correctly when admission is hidden");

        using var debt=new DataTable();
        foreach(string name in new[]{"Adm. / QR No.","Student","__StudentFatherName","__StudentGender","Campus","Class / Section","Aging"})debt.Columns.Add(name);
        foreach(string name in new[]{"Outstanding","Overdue Amount","Due Today","Not Yet Due","Due Date Missing"})debt.Columns.Add(name,typeof(decimal));
        debt.Columns.Add("Oldest Due Date",typeof(DateTime));debt.Columns.Add("Days Overdue",typeof(int));
        debt.Rows.Add("QR-163a","Kausar","Ali","F","Campus Girls","Four - F","1-30 days",600m,200m,100m,300m,0m,new DateTime(2026,10,27),7);
        using var projectedDebt=Project(debt,debt.Columns.Cast<DataColumn>().Where(c=>!c.ColumnName.StartsWith("__")).Select(c=>(c.ColumnName,c.ColumnName)).ToArray());
        var debtPlan=Plan(projectedDebt);
        Check(Cell(projectedDebt,debtPlan[0])=="QR-163a - Kausar d/o Ali","Private reader metadata supports debt identity without adding grid/export columns");
        Check(!projectedDebt.Columns.Cast<DataColumn>().Any(c=>c.ColumnName.StartsWith("__")),"Private identity metadata is absent from selectable/exported columns");
        Check(debtPlan.Count(c=>c.Numeric)==5 && debtPlan.Single(c=>c.Caption=="Outstanding").Fields.Length==1,"Debt measures stay separate and retain their source values");
        Check(Cell(projectedDebt,debtPlan.Single(c=>c.Presentation==GridReportColumns.Kind.Due)).Contains("7 days overdue"),"Aging retains the selected overdue day count");
        Render("outstanding-aging","Outstanding & Aging — Students",projectedDebt,"QR-163a - Kausar d/o Ali","7 days overdue","600");
        using(var matchingNames=debt.Copy())
        {
            var second=matchingNames.Rows.Add((object[])debt.Rows[0].ItemArray.Clone());
            second["Adm. / QR No."]="164";second["__StudentFatherName"]="Hamid";second["__StudentGender"]="M";
            using var reordered=new DataView(matchingNames){Sort="[Adm. / QR No.] DESC"}.ToTable();
            using var identityOnly=Project(reordered,("Adm. / QR No.","Admission Number"),("Student","Student"));
            var identity=Plan(identityOnly)[0];
            Check(GridReportColumns.DisplayValue(identityOnly.Rows[0],identity,"#,##0.##")=="QR-163a - Kausar d/o Ali" &&
                GridReportColumns.DisplayValue(identityOnly.Rows[1],identity,"#,##0.##")=="164 - Kausar s/o Hamid","Sorting and matching student names cannot attach the wrong parent metadata");
        }

        using var changes=new DataTable();
        foreach(string name in new[]{"StudentName","AdmissionNumber","OldClassSection","NewClassSection","OldStatus","NewStatus","Remarks"})changes.Columns.Add(name);
        changes.Rows.Add("Kausar","163","Three - F","Four - F","Active","Active","Promoted");
        var changePlan=Plan(changes);
        Check(Cell(changes,changePlan.Single(c=>c.Presentation==GridReportColumns.Kind.ClassChange))=="From: Three - F\nTo: Four - F","Merged transfer columns identify the previous and new classes");
        Check(Cell(changes,changePlan.Single(c=>c.Presentation==GridReportColumns.Kind.StatusChange))=="From: Active\nTo: Active","Equal old/new status values retain their distinct historical roles");
        Render("student-changes","Student Changes",changes,"From: Three - F","To: Four - F");

        using var certificates=new DataTable();
        foreach(string name in new[]{"Recorded On","Certificate Type","Certificate No.","Recipient Type","Recipient","Student / Staff No.","Certificate Date","Entry","Issue Type","Recorded By","Collection","Collected On","Collected By"})certificates.Columns.Add(name);
        certificates.Rows.Add("03-11-2026 11:40","Character","CC-2026-001","Student","Kausar","163","02-11-2026","Issue logged","Original","Operator","Collected","03-11-2026 12:00","Ali");
        var certificatePlan=Plan(certificates);
        Check(certificatePlan.Count==4 && Cell(certificates,certificatePlan.Single(c=>c.Presentation==GridReportColumns.Kind.Recipient)).Contains("163 - Kausar"),"Certificate register groups certificate, recipient, recording and collection details");
        Render("certificate-register","Certificate Issue Register",certificates,"163 - Kausar","CC-2026-001","By: Ali","03-11-2026 12:00");

        using var creditSource=new DataTable();
        foreach(string name in new[]{"Adm. / QR No.","Student","Father Name","__StudentGender","Family","Campus","Class / Section","Student Status"})creditSource.Columns.Add(name);
        creditSource.Columns.Add("Available Credit",typeof(decimal));creditSource.Columns.Add("Last Activity",typeof(DateTime));
        creditSource.Rows.Add("163","Kausar","Ali","Female","FAM-1","Campus Girls","Four - F","Active",500m,new DateTime(2026,11,3));
        var credit=CreditBalanceReportData.Create(creditSource,false,0);
        using(credit.Rows)using(credit.Totals)
        using(var creditExport=Project(credit.Rows,credit.Rows.Columns.Cast<DataColumn>().Where(c=>!c.ColumnName.StartsWith("__")).Select(c=>(c.ColumnName,c.ColumnName)).ToArray()))
            Check(Cell(creditExport,Plan(creditExport)[0]).Contains("163 - Kausar d/o Ali"),"Advance balance report preserves recorded gender through its explicit projection");
    }
}
