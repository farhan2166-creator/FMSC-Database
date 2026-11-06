using System.Globalization;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using FMSC_SMS.Core;

namespace FMSC_SMS.Pages.Reports.Finance;

internal static class StudentFeeChangeHistory
{
    internal static async Task ShowAsync(Control owner, Func<int> selectedStudent)
    {
        int admissionId=selectedStudent();
        if (admissionId<=0) throw new InvalidOperationException("Select a student to view fee change history.");
        int userId=CurrentUser.UserId;
        var historyTask=Task.Run(() => Load(admissionId,userId));
        var institutionTask=InstitutionProfile.GetPrintIdentityAsync();
        await Task.WhenAll(historyTask,institutionTask);
        if (owner.IsDisposed || selectedStudent()!=admissionId || CurrentUser.UserId!=userId) return;
        var report=Report(await historyTask,(await institutionTask).Name,CurrentUser.Username);
        RdlcReportPreview.Show(owner.FindForm(),"Fee Change History","","ReportData",report.Data,
            fitPageWidth:true,reportDefinition:report.Definition);
    }
    internal sealed record Result(DataTable Rows, string Identity);
    internal static Result Load(int admissionId, int userId)
    {
        using var connection = DatabaseHelper.GetConnection();
        DatabaseHelper.OpenConnection(connection);
        using var command = new SqlCommand("""
            EXEC dbo.RequirePermission @UserId,N'Fees.View';
            EXEC dbo.RequirePermission @UserId,N'Fees.Statement.View';
            SELECT S.AdmissionNumber,S.StudentName,S.FatherName,C.CampusName,CL.ClassName,
                SE.SectionName,Y.YearName
            FROM dbo.Students S
            OUTER APPLY(SELECT TOP(1) * FROM dbo.Enrollments E WHERE E.AdmissionId=S.AdmissionId
                ORDER BY CASE WHEN E.Status=N'Active' THEN 0 ELSE 1 END,E.EnrollmentId DESC) E
            LEFT JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
            LEFT JOIN dbo.Campuses C ON C.CampusId=CCS.CampusId
            LEFT JOIN dbo.Classes CL ON CL.ClassId=CCS.ClassId
            LEFT JOIN dbo.Sections SE ON SE.SectionId=CCS.SectionId
            LEFT JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
            WHERE S.AdmissionId=@AdmissionId;
            SELECT S.*,F.FeeTypeName,F.DisplayOrder,Y.YearName,Y.EndDate
            FROM dbo.StudentFeeAssignments S JOIN dbo.FeeTypes F ON F.FeeTypeId=S.FeeTypeId
            JOIN dbo.Enrollments E ON E.EnrollmentId=S.EnrollmentId
            JOIN dbo.AcademicYears Y ON Y.AcademicYearId=E.AcademicYearId
            WHERE S.AdmissionId=@AdmissionId ORDER BY Y.StartDate DESC,F.DisplayOrder,S.EffectiveFrom;
            SELECT B.*,F.FeeTypeName,F.DisplayOrder FROM dbo.BillingCharges B
            JOIN dbo.FeeTypes F ON F.FeeTypeId=B.FeeTypeId
            WHERE B.AdmissionId=@AdmissionId ORDER BY B.BillingMonth,F.DisplayOrder,B.BillingChargeId;
            """, connection) { CommandTimeout = 120 };
        command.Parameters.AddRange(new[] { DatabaseHelper.Int("@UserId", userId), DatabaseHelper.Int("@AdmissionId", admissionId) });
        var data = new DataSet();
        using var adapter = new SqlDataAdapter(command);
        adapter.Fill(data);
        return Build(data, DateTime.Today);
    }

    internal static Result Build(DataSet data, DateTime today)
    {
        if (data.Tables.Count != 3 || data.Tables[0].Rows.Count != 1)
            throw new InvalidOperationException("The student's fee history could not be loaded completely.");
        var identity = data.Tables[0].Rows[0];
        string identityText = $"Admission {Text(identity,"AdmissionNumber")}   |   {Text(identity,"StudentName")}   |   Father: {Text(identity,"FatherName")}\n"
            + $"{Text(identity,"ClassName")} / {Text(identity,"SectionName")}   |   {Text(identity,"CampusName")}   |   {Text(identity,"YearName")}";
        var rows = new DataTable();
        foreach (string name in new[] { "Recorded / Status", "Fee Type / Action", "Amounts / Changes", "Effective period", "By", "Reason / Notes" }) rows.Columns.Add(name);
        DateTime month = new(today.Year,today.Month,1);
        AddSection("FEE RATE SCHEDULE", "Stored rate versions across all academic years. Status is evaluated for " + month.ToString("MMMM yyyy") + ".");
        int RateRank(DataRow r) => r["CancelledAt"]!=DBNull.Value ? 3
            : r["EffectiveTo"]!=DBNull.Value && Convert.ToDateTime(r["EffectiveTo"])<month
                || r["EndDate"]!=DBNull.Value && Convert.ToDateTime(r["EndDate"])<month ? 2
            : r["EffectiveFrom"]!=DBNull.Value && Convert.ToDateTime(r["EffectiveFrom"])>month ? 1 : 0;
        var recurringRates=data.Tables[1].AsEnumerable().Where(r => !string.Equals(Text(r,"Frequency"),"As Needed",StringComparison.OrdinalIgnoreCase)).OrderBy(RateRank).ToArray();
        foreach (DataRow r in recurringRates)
        {
            string status = r["CancelledAt"] != DBNull.Value ? "Cancelled"
                : r["EffectiveTo"] != DBNull.Value && Convert.ToDateTime(r["EffectiveTo"]) < month ? "Ended"
                : r["EndDate"] != DBNull.Value && Convert.ToDateTime(r["EndDate"]) < month ? "Ended"
                : r["EffectiveFrom"] != DBNull.Value && Convert.ToDateTime(r["EffectiveFrom"]) > month ? "Scheduled" : "Applicable";
            rows.Rows.Add(status,Text(r,"FeeTypeName"),
                $"Amount Rs. {Money(r["Amount"])}\nDiscount Rs. {Money(r["DiscountAmount"])}\nNet Rs. {Money(Convert.ToDecimal(r["Amount"])-Convert.ToDecimal(r["DiscountAmount"]))}",
                $"{Month(r["EffectiveFrom"],"From start")} to {Month(r["EffectiveTo"],"Ongoing")}","—",
                $"{Text(r,"YearName")} · {Text(r,"Frequency")}\nRate version #{r["StudentFeeAssignmentId"]}");
        }
        if (recurringRates.Length == 0) rows.Rows.Add("—","No recurring fee assignments","—","—","—","Purchases appear in posted charges.");
        AddSection("POSTED CHARGES", "Actual charge amounts can differ from the rate schedule. Cancelled charges remain visible as history.");
        foreach (DataRow r in data.Tables[2].Rows)
            rows.Rows.Add(Text(r,"Status")=="PartiallyPaid" ? "Partially paid" : Text(r,"Status")=="Open" ? "Unpaid" : Text(r,"Status"),Text(r,"FeeTypeName"),
                $"Gross Rs. {Money(r["GrossAmount"])}\nDiscount Rs. {Money(r["DiscountAmount"])}\nNet Rs. {Money(r["NetAmount"])}",
                Month(r["BillingMonth"],"Not recorded"),"—",$"Charge #{r["BillingChargeId"]}\n{ChargeNote(r)}");
        if (data.Tables[2].Rows.Count == 0) rows.Rows.Add("—","No posted charges","—","—","—","No charges were found.");
        return new(rows,identityText);
        void AddSection(string title,string note) => rows.Rows.Add("SECTION",title,"","","",note);
    }

    private static string ChargeNote(DataRow row) => Text(row,"CancellationReason") is { Length: > 0 } reason
        ? Text(row,"Remarks")+"\nCancellation: "+reason : Text(row,"Remarks");
    private static string Text(DataRow row,string key) => row.Table.Columns.Contains(key) && row[key]!=DBNull.Value ? Convert.ToString(row[key]) ?? "" : "";
    private static string Money(object value) => Convert.ToDecimal(value).ToString("N0");
    private static string Month(object value,string blank) => value==DBNull.Value ? blank : Convert.ToDateTime(value).ToString("MMM yyyy");
    internal static GridReportPreview.Report Report(Result result,string institution,string printedBy)
    {
        var report=GridReportPreview.Build(institution,"Student Fee Change History",result.Identity,result.Rows,printedBy);
        XNamespace ns="http://schemas.microsoft.com/sqlserver/reporting/2008/01/reportdefinition";
        var document=XDocument.Parse(report.Definition);
        XElement root=document.Root!;
        root.Element(ns+"Width")!.Value="10.67in";
        root.Element(ns+"Page")!.Element(ns+"PageWidth")!.Value="11.69in";
        root.Element(ns+"Page")!.Element(ns+"PageHeight")!.Value="8.27in";
        foreach(var textbox in root.Descendants(ns+"PageHeader").Descendants(ns+"Textbox")) textbox.Element(ns+"Width")!.Value="10.67in";
        root.Descendants(ns+"PageHeader").First().Element(ns+"Height")!.Value="1.1in";
        var table=root.Descendants(ns+"Tablix").Single();
        table.Add(new XElement(ns+"KeepTogether",false),new XElement(ns+"RepeatColumnHeaders",true),new XElement(ns+"RepeatRowHeaders",true));
        table.Element(ns+"TablixRowHierarchy")!.Descendants(ns+"TablixMember").First().Add(new XElement(ns+"FixedData",true));
        table.Element(ns+"Width")!.Value="10.67in";
        double[] widths=[1.1,1.65,2.1,1.75,0.8,3.27];
        // Explicit page groups avoid split event rows and keep headings with the next entry.
        report.Data.Columns.Add("PageKey",typeof(int));
        int page=0;
        double used=0;
        for(int rowIndex=0;rowIndex<report.Data.Rows.Count;rowIndex++)
        {
            DataRow row=report.Data.Rows[rowIndex];
            double height=RowHeight(row);
            double needed=height;
            if(Convert.ToString(row["C0"])=="SECTION" && rowIndex+1<report.Data.Rows.Count)
                needed+=RowHeight(report.Data.Rows[rowIndex+1]);
            if(used>0 && used+needed>4.7) { page++; used=0; }
            row["PageKey"]=page;
            used+=height;
        }
        double RowHeight(DataRow row)
        {
            int lines=1;
            for(int column=0;column<widths.Length;column++)
            {
                int capacity=Math.Max(8,(int)(widths[column]*72/4.6));
                int count=(Convert.ToString(row["C"+column]) ?? "").Split('\n')
                    .Sum(line => Math.Max(1,(int)Math.Ceiling(line.Length/(double)capacity)));
                lines=Math.Max(lines,count);
            }
            return Math.Max(.2,lines*.145+.06);
        }
        root.Descendants(ns+"Fields").Single().Add(new XElement(ns+"Field",new XAttribute("Name","PageKey"),new XElement(ns+"DataField","PageKey")));
        var members=table.Element(ns+"TablixRowHierarchy")!.Element(ns+"TablixMembers")!;
        var existing=members.Elements().Select(e => new XElement(e)).ToArray();
        members.ReplaceNodes(new XElement(ns+"TablixMember",
            new XElement(ns+"Group",new XAttribute("Name","PrintedPage"),
                new XElement(ns+"GroupExpressions",new XElement(ns+"GroupExpression","=Fields!PageKey.Value")),
                new XElement(ns+"PageBreak",new XElement(ns+"BreakLocation","Between"))),
            new XElement(ns+"TablixMembers",existing)));
        int index=0;
        foreach(var column in table.Descendants(ns+"TablixColumn")) column.Element(ns+"Width")!.Value=widths[index++].ToString(CultureInfo.InvariantCulture)+"in";
        foreach(var textbox in table.Descendants(ns+"Textbox").Where(t => ((string?)t.Attribute("Name"))?.StartsWith("Value")==true))
        {
            textbox.Descendants(ns+"FontWeight").First().Value="=IIf(Fields!C0.Value = \"SECTION\", \"Bold\", \"Normal\")";
            textbox.Element(ns+"Style")!.Element(ns+"BackgroundColor")!.Value="=IIf(Fields!C0.Value = \"SECTION\", \"#E8F1EE\", IIf(RowNumber(Nothing) Mod 2 = 0, \"#F6F9FA\", \"White\"))";
            if ((string?)textbox.Attribute("Name")=="Value0") textbox.Descendants(ns+"Value").First().Value="=IIf(Fields!C0.Value = \"SECTION\", \"\", Fields!C0.Value)";
        }
        return new(document.ToString(),report.Data);
    }
}
