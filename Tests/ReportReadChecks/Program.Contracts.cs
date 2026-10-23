using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using FMSC_SMS.Pages.Reports.Finance;

internal static partial class Program
{
    private static void CheckQueryContracts()
    {
        foreach(string query in new[] { CreditBalanceReportData.Query,ConcessionSummaryData.Query })
        {
            var fragment=(TSqlScript)new TSql160Parser(true).Parse(new StringReader(query),out var errors);
            Check(errors.Count==0,"Read query parses as SQL Server 2022 syntax: "+string.Join("; ",errors.Select(e=>e.Message)));
            foreach(var statement in fragment.Batches.SelectMany(b=>b.Statements))
            {
                Check(statement is SelectStatement or ExecuteStatement,"No financial DML or schema change is present in report SQL");
                if(statement is ExecuteStatement)
                    Check(query.Substring(statement.StartOffset,statement.FragmentLength).StartsWith("EXEC dbo.RequirePermission",StringComparison.Ordinal),"Only permission-check procedures execute in report SQL");
            }
        }
        var folder=new DirectoryInfo(AppContext.BaseDirectory);
        while(folder is not null && !File.Exists(Path.Combine(folder.FullName,"fmsc.dacpac")))folder=folder.Parent;
        if(folder is null)throw new InvalidOperationException("Run checks from a checkout containing fmsc.dacpac.");
        using var archive=ZipFile.OpenRead(Path.Combine(folder.FullName,"fmsc.dacpac"));
        using var stream=archive.GetEntry("model.xml")!.Open();var model=XDocument.Load(stream);
        var columns=model.Descendants().Where(e=>(string?)e.Attribute("Type")=="SqlSimpleColumn").Select(e=>(string?)e.Attribute("Name")).ToHashSet();
        var required=new Dictionary<string,string[]> {
            ["StudentCreditEntries"]=new[]{"AdmissionId","Amount","CreatedAt"},
            ["Students"]=new[]{"AdmissionId","AdmissionNumber","QuickReference","StudentName","FatherName","Status"},
            ["Families"]=new[]{"FamilyId","FamilyCode"},["FamilyMembers"]=new[]{"AdmissionId","FamilyId"},
            ["Enrollments"]=new[]{"AdmissionId","EnrollmentId","EnrollmentDate","Status","ClassSectionId","AcademicYearId","BillingStartDate"},
            ["StudentFeeAssignments"]=new[]{"AdmissionId","EnrollmentId","FeeTypeId","Frequency","ConcessionLabel","DiscountAmount","CancelledAt","EffectiveFrom","EffectiveTo"},
            ["AcademicYears"]=new[]{"AcademicYearId","EndDate"},["FeeTypes"]=new[]{"FeeTypeId","FeeTypeName"},
            ["CampusClassSections"]=new[]{"ClassSectionId","CampusId","ClassId","SectionId"},
            ["Campuses"]=new[]{"CampusId","CampusName","DisplayOrder"},["Classes"]=new[]{"ClassId","ClassName","DisplayOrder"},["Sections"]=new[]{"SectionId","SectionName","DisplayOrder"}
        };
        foreach(var (table,fields) in required)foreach(string field in fields)
            Check(columns.Contains($"[dbo].[{table}].[{field}]"),$"DACPAC contains {table}.{field}");
        Check(model.Descendants().Any(e=>(string?)e.Attribute("Name")=="[dbo].[UQ_FamilyMembers_Student]"),"DACPAC enforces one family membership per student, preventing credit multiplication");
    }
}
