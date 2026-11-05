using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using FMSC_SMS.Pages.Reports.Finance;
using FMSC_SMS.Pages.Reports.Certificates;

internal static partial class Program
{
    private static void CheckQueryContracts()
    {
        foreach(string query in new[] { CreditBalanceReportData.Query,ConcessionSummaryData.Query,MonthlyFeeReconciliationData.Query,
            CertificateIssueRegisterData.BuildQuery(true,true),CertificateIssueRegisterData.BuildQuery(true,false),CertificateIssueRegisterData.BuildQuery(false,true) })
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
        string migration=File.ReadAllText(Path.Combine(folder.FullName,"Tests","LifecycleSqlChecks","Migrations","70.sql"));
        string collectionMigration=File.ReadAllText(Path.Combine(folder.FullName,"Tests","LifecycleSqlChecks","Migrations","75.sql"));
        new TSql160Parser(true).Parse(new StringReader(collectionMigration),out var collectionErrors);
        Check(collectionErrors.Count==0,"Migration 75 parses: "+string.Join("; ",collectionErrors.Select(e=>e.Message)));
        Check(collectionMigration.Contains("[__ReceiptStudents]")&&collectionMigration.Contains("A.__ReceiptSnapshot")&&!collectionMigration.Contains("THEN N'Mixed'"),"Collections exposes roster metadata and actual distinct methods");
        new TSql160Parser(true).Parse(new StringReader(migration),out var migrationErrors);
        Check(migrationErrors.Count==0,"Migration 70 parses as SQL Server syntax: "+string.Join("; ",migrationErrors.Select(e=>e.Message)));
        Check(migration.Contains("LEFT JOIN dbo.FamilyMembers") && migration.Contains("CASE WHEN F.FamilyId IS NULL THEN M.AdmissionId END"),
            "Family debt retains and separately groups students without family links");
        Check(System.Text.RegularExpressions.Regex.Matches(migration,@"\[Due Date Missing\]").Count==3,
            "All three debt views expose missing-date money instead of losing it from the split");
        Check(migration.Contains("DueDate < @AsOfDate") && migration.Contains("DueDate = @AsOfDate") && migration.Contains("DueDate > @AsOfDate"),
            "Debt reader separates overdue, due today and future charges");
        string identityMigration=File.ReadAllText(Path.Combine(folder.FullName,"Tests","LifecycleSqlChecks","Migrations","74.sql"));
        new TSql160Parser(true).Parse(new StringReader(identityMigration),out var identityErrors);
        Check(identityErrors.Count==0,"Migration 74 parses as SQL Server syntax: "+string.Join("; ",identityErrors.Select(e=>e.Message)));
        Check(System.Text.RegularExpressions.Regex.Matches(identityMigration,@"FatherName \[__StudentFatherName\], Gender \[__StudentGender\]").Count==2,
            "Student and charge debt views return private recorded parent/gender metadata");
        string Procedure(string sql)=>sql[sql.IndexOf("CREATE OR ALTER PROCEDURE",StringComparison.Ordinal)..sql.IndexOf("\nGO\nINSERT",StringComparison.Ordinal)].Replace("\r","");
        // Strip only the four metadata additions, then compare the entire reader:
        // permissions, grouping, arithmetic, filtering and sort must remain identical.
        string withoutMetadata=identityMigration.Replace("\r","")
            .Replace("S.StudentName, S.FatherName, S.Gender,","S.StudentName,")
            .Replace("StudentName [Student], FatherName [__StudentFatherName], Gender [__StudentGender],","StudentName [Student],")
            .Replace("AdmissionNumber, StudentName, FatherName, Gender, CampusName","AdmissionNumber, StudentName, CampusName");
        Check(Procedure(withoutMetadata)==Procedure(migration.Replace("\r","")),"Migration 74 changes only parent/gender metadata; all financial logic is byte-for-byte preserved");
        using var archive=ZipFile.OpenRead(Path.Combine(folder.FullName,"fmsc.dacpac"));
        using var stream=archive.GetEntry("model.xml")!.Open();var model=XDocument.Load(stream);
        var columns=model.Descendants().Where(e=>(string?)e.Attribute("Type") is "SqlSimpleColumn" or "SqlComputedColumn").Select(e=>(string?)e.Attribute("Name")).ToHashSet();
        var required=new Dictionary<string,string[]> {
            ["StudentCreditEntries"]=new[]{"AdmissionId","Amount","CreatedAt"},
            ["Students"]=new[]{"AdmissionId","AdmissionNumber","QuickReference","StudentName","FatherName","Gender","Status"},
            ["Families"]=new[]{"FamilyId","FamilyCode"},["FamilyMembers"]=new[]{"AdmissionId","FamilyId"},
            ["Enrollments"]=new[]{"AdmissionId","EnrollmentId","EnrollmentDate","LeavingDate","Status","ClassSectionId","AcademicYearId","BillingStartDate"},
            ["StudentFeeAssignments"]=new[]{"StudentFeeAssignmentId","AdmissionId","EnrollmentId","FeeTypeId","Frequency","ConcessionLabel","DiscountAmount","CancelledAt","EffectiveFrom","EffectiveTo"},
            ["AcademicYears"]=new[]{"AcademicYearId","StartDate","EndDate"},["FeeTypes"]=new[]{"FeeTypeId","FeeTypeName"},
            ["CampusClassSections"]=new[]{"ClassSectionId","CampusId","ClassId","SectionId"},
            ["BillingCharges"]=new[]{"BillingChargeId","BillingMonth","ChargeDate","AdmissionId","EnrollmentId","FeeTypeId","GrossAmount","DiscountAmount","NetAmount","Status"},
            ["FeePayments"]=new[]{"FeePaymentId","Status","IsCreditApplication"},["FeePaymentAllocations"]=new[]{"FeePaymentId","BillingChargeId","Amount"},
            ["Users"]=new[]{"UserId","Username"},
            ["Campuses"]=new[]{"CampusId","CampusName","DisplayOrder"},["Classes"]=new[]{"ClassId","ClassName","DisplayOrder"},["Sections"]=new[]{"SectionId","SectionName","DisplayOrder"}
        };
        foreach(var (table,fields) in required)foreach(string field in fields)
            Check(columns.Contains($"[dbo].[{table}].[{field}]"),$"DACPAC contains {table}.{field}");
        foreach(var (table,key,staff,date) in new[] {
            ("CharacterCertificates","CertificateRecordId",false,"CertificateDate"),("DateOfBirthCertificates","CertificateRecordId",false,"CertificateDate"),
            ("LeaveCertificates","LeaveRecordId",false,"LeaveDate"),("RegularStudentCertificates","RegularStudentCertificateId",false,"CertificateDate"),
            ("SportsCertificates","SportsCertificateId",false,"CertificateDate"),("NoObjectionCertificates","NoObjectionCertificateId",false,"CertificateDate"),
            ("ProvisionalCertificates","ProvisionalCertificateId",false,"CertificateDate"),("RegularLecturerCertificates","RegularLecturerCertificateId",true,"CertificateDate"),
            ("BankAccountOpeningCertificates","BankAccountOpeningCertificateId",true,"CertificateDate") })
            foreach(string field in new[] {key,date,"CertificateNumber","CreatedAt","CreatedByUserId",staff ? "StaffId" : "AdmissionId",staff ? "StaffName" : "StudentName"})
                Check(columns.Contains($"[dbo].[{table}].[{field}]"),$"DACPAC contains register source {table}.{field}");
        foreach(var (table,key,record) in new[] { ("CharacterCertificateIssues","CharacterCertificateIssueId","CertificateRecordId"),
            ("DateOfBirthCertificateIssues","DateOfBirthCertificateIssueId","CertificateRecordId"),("LeaveCertificateIssues","LeaveCertificateIssueId","LeaveRecordId") })
            foreach(string field in new[] {key,record,"IssuedAt","IssuedByUserId","CollectedAt","CollectedBy"})
                Check(columns.Contains($"[dbo].[{table}].[{field}]"),$"DACPAC contains issue log {table}.{field}");
        Check(columns.Contains("[dbo].[LeaveCertificates].[CertificateType]") && columns.Contains("[dbo].[LeaveCertificateIssues].[IssueType]"),"Leaving register preserves school/college and original/duplicate distinctions");
        Check(model.Descendants().Any(e=>(string?)e.Attribute("Name")=="[dbo].[UQ_FamilyMembers_Student]"),"DACPAC enforces one family membership per student, preventing credit multiplication");
    }
}
