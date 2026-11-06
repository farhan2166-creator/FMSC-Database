using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FMSC_SMS.Core;
using Microsoft.Reporting.WinForms;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

internal static partial class Program
{
    private sealed record CertificateReviewCase(
        string Template, string Title, string FileKey, string Recipient, string Parent, string Gender,
        IReadOnlyDictionary<string, object>? Values = null, string[]? Subjects = null);

    // These are clearly fictional school records, but their names, dates, classes,
    // genders and certificate content read like documents an operator would print.
    // Use the actual production RDLC and print-data/letterhead transformations.
    private static readonly CertificateReviewCase[] CertificateReviewCases =
    {
        new("CharacterCertificate", "Character Certificate — SSC, Student", "character-ssc-student",
            "Muhammad Hamza", "Abdul Rehman", "Male",
            new Dictionary<string, object> { ["ExamTypeDisplay"]="SSC", ["LastExamYear"]=2025, ["RollNumber"]="182734", ["Remarks"]="Excellent" }),
        new("CharacterCertificate", "Character Certificate — HSSC, Student", "character-hssc-student",
            "Ayesha Noor", "Muhammad Ilyas", "Female",
            new Dictionary<string, object> { ["ExamTypeDisplay"]="HSSC", ["LastExamYear"]=2026, ["RollNumber"]="476290", ["Remarks"]="Very Good" }),
        new("CharacterCertificate", "Character Certificate — Gender Not Recorded", "character-unspecified-gender",
            "Ali Raza", "Naveed Ahmad", "",
            new Dictionary<string, object> { ["ExamTypeDisplay"]="SSC", ["RollNumber"]="188206" }),

        new("DateOfBirthCertificate", "Date of Birth Certificate — Female Student", "date-of-birth-female",
            "Fatima Zahra", "Arshad Khan", "Female",
            new Dictionary<string, object> { ["DateOfBirth"]=new DateTime(2012,3,9) }),
        new("DateOfBirthCertificate", "Date of Birth Certificate — Male Student", "date-of-birth-male",
            "Hassan Ali", "Imran Yousaf", "Male",
            new Dictionary<string, object> { ["DateOfBirth"]=new DateTime(2011,7,22) }),
        new("DateOfBirthCertificate", "Date of Birth Certificate — Gender Not Recorded", "date-of-birth-unspecified",
            "Noor Ahmad", "Azhar Mahmood", ""),

        new("RegularStudentCertificate", "Regular Student Certificate — Middle School", "regular-student-middle",
            "Abdullah Farooq", "Farooq Ahmed", "Male",
            new Dictionary<string, object> { ["ClassName"]="Class VIII", ["DateOfBirth"]=new DateTime(2012,1,19) }),
        new("RegularStudentCertificate", "Regular Student Certificate — Higher Secondary", "regular-student-higher-secondary",
            "Zainab Ali", "Muhammad Javed", "Female",
            new Dictionary<string, object> { ["ClassName"]="Class XI", ["DateOfBirth"]=new DateTime(2009,9,14) }),
        new("RegularStudentCertificate", "Regular Student Certificate — Longer Recorded Name", "regular-student-long-name",
            "Muhammad Abdul Rehman Khan", "Abdul Qayyum Khan", "Male",
            new Dictionary<string, object> { ["ClassName"]="Class IX" }),

        new("LeaveCertificate", "School Leaving Certificate — Original", "school-leaving-original",
            "Bilal Ahmad", "Shahid Mahmood", "Male",
            new Dictionary<string, object> { ["CopyType"]="Original", ["ClassStudied"]="Class X", ["ClassPromotedTo"]="Class XI", ["Remarks"]="On parents' request" }),
        new("LeaveCertificate", "School Leaving Certificate — Duplicate", "school-leaving-duplicate",
            "Maryam Khan", "Abdul Wahid", "Female",
            new Dictionary<string, object> { ["CopyType"]="Duplicate", ["ClassStudied"]="Class VIII", ["ClassPromotedTo"]="Class IX", ["Remarks"]="Family relocated to another city" }),
        new("LeaveCertificate", "School Leaving Certificate — Same Class", "school-leaving-same-class",
            "Saad Usman", "Usman Tariq", "Male",
            new Dictionary<string, object> { ["ClassStudied"]="Class VII", ["ClassPromotedTo"]="Class VII", ["AttendanceRecord"]="Satisfactory" }),

        new("CollegeLeaveCertificate", "College Leaving Certificate — Original", "college-leaving-original",
            "Hira Batool", "Rashid Iqbal", "Female",
            new Dictionary<string, object> { ["CopyType"]="Original", ["ClassStudied"]="HSSC Part II", ["ClassPromotedTo"]="HSSC Part II", ["DateOfBirth"]=new DateTime(2008,6,15), ["Remarks"]="Completed studies" }),
        new("CollegeLeaveCertificate", "College Leaving Certificate — Duplicate", "college-leaving-duplicate",
            "Danish Kamal", "Kamal Din", "Male",
            new Dictionary<string, object> { ["CopyType"]="Duplicate", ["ClassStudied"]="HSSC Part I", ["ClassPromotedTo"]="HSSC Part II", ["DateOfBirth"]=new DateTime(2008,12,4) }),

        new("SportsCertificate", "Sports Certificate — Cricket", "sports-cricket",
            "Usman Khalid", "Khalid Mehmood", "Male",
            new Dictionary<string, object> { ["SportName"]="Cricket", ["Performance"]="Represented the school cricket team in the district championship and displayed excellent sportsmanship." }),
        new("SportsCertificate", "Sports Certificate — Athletics", "sports-athletics",
            "Sana Tariq", "Tariq Aziz", "Female",
            new Dictionary<string, object> { ["SportName"]="Athletics", ["Performance"]="Secured first position in the 200-metre race at the inter-school sports meet." }),
        new("SportsCertificate", "Sports Certificate — Team Leadership", "sports-team-leadership",
            "Areeba Saleem", "Saleem Akhtar", "Female",
            new Dictionary<string, object> { ["SportName"]="Volleyball", ["Performance"]="Served as team captain, encouraged cooperation among teammates and represented the college in inter-college competitions." }),

        new("NoObjectionCertificate", "No Objection Certificate — Male Student", "no-objection-male",
            "Hamza Yousaf", "Yousaf Ali", "Male"),
        new("NoObjectionCertificate", "No Objection Certificate — Female Student", "no-objection-female",
            "Iqra Jamil", "Jamil Ahmed", "Female"),

        new("ProvisionalCertificate", "Provisional Certificate — SSC", "provisional-ssc",
            "Mahnoor Iqbal", "Iqbal Hussain", "Female",
            new Dictionary<string, object> { ["ExamType"]="SSC", ["ExamTypeLabel"]="SSC (Matric)", ["Grade"]="A+", ["MarksObtained"]=979, ["MarksOutOf"]=1100 },
            new[] { "English", "Urdu", "Islamiyat", "Pakistan Studies", "Mathematics", "Physics", "Chemistry", "Biology" }),
        new("ProvisionalCertificate", "Provisional Certificate — HSSC Part I", "provisional-hssc-part-one",
            "Ahmad Raza", "Nisar Ahmad", "Male",
            new Dictionary<string, object> { ["ExamType"]="HSSC1", ["ExamTypeLabel"]="HSSC Part I", ["Grade"]="A", ["MarksObtained"]=501, ["MarksOutOf"]=550, ["DateOfBirth"]=new DateTime(2008,2,12) },
            new[] { "English", "Urdu", "Islamic Education", "Physics", "Chemistry", "Mathematics" }),
        new("ProvisionalCertificate", "Provisional Certificate — HSSC Part II, Extended Subjects", "provisional-hssc-part-two",
            "Zoya Bibi", "Habib Ullah", "Female",
            new Dictionary<string, object> { ["ExamType"]="HSSC2", ["ExamTypeLabel"]="HSSC Part II", ["Grade"]="B", ["MarksObtained"]=854, ["MarksOutOf"]=1100, ["DateOfBirth"]=new DateTime(2007,10,28) },
            new[] { "English", "Urdu", "Islamiyat", "Pakistan Studies", "Mathematics", "Physics", "Chemistry", "Biology", "Computer Science", "Economics", "Statistics", "Civics" }),

        new("RegularLecturerCertificate", "Regular Teacher Certificate — Female Teacher", "regular-teacher",
            "Sadia Parveen", "Ghulam Mustafa", "Female",
            new Dictionary<string, object> { ["RoleTitle"]="Teacher", ["SubjectTaught"]="English" }),
        new("RegularLecturerCertificate", "Regular Lecturer Certificate — Male Lecturer", "regular-lecturer",
            "Muhammad Adnan", "Zafar Iqbal", "Male",
            new Dictionary<string, object> { ["RoleTitle"]="Lecturer", ["SubjectTaught"]="Physics" }),

        new("BankAccountOpeningCertificate", "Bank Account Opening Certificate — Teacher", "bank-account-teacher",
            "Naveed Akram", "Akram Khan", "Male",
            new Dictionary<string, object> { ["Designation"]="Senior Teacher", ["BankName"]="National Bank of Pakistan", ["BranchName"]="University Road Branch", ["MonthlyIncome"]=78000m, ["Purpose"]="Opening of salary account" }),
        new("BankAccountOpeningCertificate", "Bank Account Opening Certificate — Lecturer", "bank-account-lecturer",
            "Rabia Gul", "Sher Mohammad", "Female",
            new Dictionary<string, object> { ["Designation"]="Lecturer", ["BankName"]="Bank of Khyber", ["BranchName"]="Peshawar Cantonment Branch", ["MonthlyIncome"]=92000m, ["Purpose"]="Verification of employment for salary account" })
    };

    private static void CheckCertificatePdfReview()
    {
        string folder=Path.GetFullPath("artifacts/certificate-pdf-review");
        Directory.CreateDirectory(folder);
        string destination=Path.Combine(folder,"FMSC-Certificates-Review.pdf");
        string pending=Path.Combine(folder,"FMSC-Certificates-Review.pending.pdf");
        File.Delete(destination);
        File.Delete(pending);

        string commit=Environment.GetEnvironmentVariable("GITHUB_SHA")??"Local build";
        var expected=new List<(double Width,double Height,string Text)>();
        var entries=new List<object>();
        string[] templates=CertificateReviewCases.Select(sample=>sample.Template).Distinct().ToArray();
        Check(templates.Length==10,"All ten production certificate templates are covered");
        Check(CertificateReviewCases.Select(sample=>sample.FileKey).Distinct(StringComparer.OrdinalIgnoreCase).Count()==CertificateReviewCases.Length,
            "Each certificate scenario must have a unique file key");
        try
        {
            using(var combined=new PdfDocument())
            {
                combined.Info.Title="FMSC Certificates — PDF and Print Review";
                combined.Info.Subject=$"Fictional school records rendered with production RDLC templates. Source commit: {commit}";
                foreach(var (sample,index) in CertificateReviewCases.Select((item,i)=>(item,i)))
                {
                    byte[] bytes=RenderCertificateReviewCase(sample,index);
                    string path=Path.Combine(folder,sample.FileKey+".pdf");
                    File.WriteAllBytes(path,bytes);
                    using var input=PdfReader.Open(new MemoryStream(bytes),PdfDocumentOpenMode.Import);
                    using var pdf=UglyToad.PdfPig.PdfDocument.Open(bytes);
                    Check(input.PageCount==pdf.NumberOfPages && input.PageCount>=1,
                        sample.Title+": exported PDF has readable pages");
                    Check(input.PageCount==1,sample.Title+": certificate must fit on a single physical sheet");
                    string readable=string.Join(" ",pdf.GetPages().SelectMany(page=>page.GetWords()).Select(word=>word.Text));
                    Check(readable.Length>40,sample.Title+": rendered certificate page contains text");
                    int first=combined.PageCount;
                    foreach(var page in input.Pages)combined.AddPage(page);
                    for(int i=1;i<=pdf.NumberOfPages;i++)
                        expected.Add((input.Pages[i-1].Width.Point,input.Pages[i-1].Height.Point,pdf.GetPage(i).Text));
                    combined.Outlines.Add(sample.Title,combined.Pages[first],false);
                    entries.Add(new {sample.Template,sample.Title,Recipient=sample.Recipient,Gender=sample.Gender,
                        File=sample.FileKey+".pdf",Pages=input.PageCount,CombinedFirstPage=first+1,CombinedLastPage=combined.PageCount,
                        Sha256=Convert.ToHexString(SHA256.HashData(bytes))});
                }
                Check(combined.Outlines.Count==CertificateReviewCases.Length,"A bookmark exists for every certificate combination");
                Check(combined.PageCount==expected.Count,"Combined review retains every source certificate page");
                combined.Save(pending);
            }
            using(var check=PdfReader.Open(pending,PdfDocumentOpenMode.Import))
            using(var text=UglyToad.PdfPig.PdfDocument.Open(pending))
            {
                Check(check.PageCount==expected.Count && text.NumberOfPages==expected.Count,
                    "Saved review has every certificate page");
                Check(check.Outlines.Count==CertificateReviewCases.Length,
                    "Saved review has one bookmark per certificate combination");
                for(int i=0;i<expected.Count;i++)
                {
                    Check(Math.Abs(check.Pages[i].Width.Point-expected[i].Width)<.01 &&
                          Math.Abs(check.Pages[i].Height.Point-expected[i].Height)<.01,
                        "Combining certificates preserves their physical page sizes");
                    Check(text.GetPage(i+1).Text==expected[i].Text,
                        "Combining certificates preserves their printed content");
                }
            }
            File.Move(pending,destination,overwrite:true);
            File.WriteAllText(Path.Combine(folder,"provenance.json"),JsonSerializer.Serialize(new {
                SourceCommit=commit,Checks="PASSED",ScenarioCount=CertificateReviewCases.Length,
                TemplateCount=templates.Length,PageCount=expected.Count,
                Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination))),Samples=entries
            },new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"::notice title=Certificate PDF review::{CertificateReviewCases.Length} certificate combinations, {expected.Count} pages in FMSC-Certificates-Review.pdf.");
        }
        finally { File.Delete(pending); }
    }

    private static byte[] RenderCertificateReviewCase(CertificateReviewCase sample,int index)
    {
        string path=Path.Combine(AppContext.BaseDirectory,"Reports",sample.Template+".rdlc");
        Check(File.Exists(path),sample.Template+": production RDLC is available");

        var document=XDocument.Load(path);
        XNamespace ns=document.Root!.Name.Namespace;
        var dataSet=document.Descendants(ns+"DataSet").Single();
        string dataSetName=(string?)dataSet.Attribute("Name")??throw new InvalidDataException("Certificate RDLC dataset has no name");
        using var source=new DataTable();
        foreach(var field in dataSet.Element(ns+"Fields")!.Elements(ns+"Field"))
        {
            string fieldName=field.Element(ns+"DataField")!.Value;
            string typeName=field.Elements().FirstOrDefault(element=>element.Name.LocalName=="TypeName")?.Value??"System.String";
            source.Columns.Add(fieldName,Type.GetType(typeName,throwOnError:true)!);
        }

        DateTime born=sample.Values is not null && sample.Values.TryGetValue("DateOfBirth",out var birth) && birth is DateTime date
            ? date : new DateTime(2010,5,14);
        DateTime issue=new(2026,11,5);
        var values=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase)
        {
            ["CertificateRecordId"]=index+101, ["RegularStudentCertificateId"]=index+101,
            ["NoObjectionCertificateId"]=index+101, ["ProvisionalCertificateId"]=index+101,
            ["RegularLecturerCertificateId"]=index+101, ["BankAccountOpeningCertificateId"]=index+101,
            ["SportsCertificateId"]=index+101, ["StaffId"]=index+101,
            ["AdmissionNumber"]=(1000+index).ToString(CultureInfo.InvariantCulture),
            ["CertificateNumber"]=$"FMSC/2026/{index+1:0000}",
            ["StudentName"]=sample.Recipient, ["StaffName"]=sample.Recipient,
            ["FatherName"]=sample.Parent, ["Gender"]=sample.Gender,
            ["DateOfBirth"]=born, ["DateOfBirthInFigures"]=born.ToString("dd-MM-yyyy",CultureInfo.InvariantCulture),
            ["DateOfBirthInWords"]=born.ToString("d MMMM yyyy",CultureInfo.GetCultureInfo("en-GB")),
            ["DateOfBirthWords"]=born.ToString("d MMMM yyyy",CultureInfo.GetCultureInfo("en-GB")),
            ["SchoolName"]="Fahim Model School & College",
            ["InstitutionLine"]="Fahim Model School & College, Peshawar",
            ["CertificateDate"]=issue, ["DateOfIssue"]=issue, ["DateofIssue"]=issue,
            ["DateofAdmission"]=new DateTime(2020,4,6), ["LeaveDate"]=new DateTime(2026,10,31),
            ["ClassName"]="Class X", ["ClassStudied"]="Class X", ["ClassPromotedTo"]="Class XI",
            ["CopyType"]="Original", ["Remarks"]="Good", ["AcademicRecord"]="Very Good",
            ["AttendanceRecord"]="Regular", ["ExamTypeDisplay"]="SSC", ["LastExamYear"]=2025,
            ["RollNumber"]="182734", ["SportName"]="Cricket",
            ["Performance"]="Represented the school team in the district sports competition.",
            ["ExamType"]="SSC", ["ExamTypeLabel"]="SSC (Matric)", ["ExamYear"]=2026,
            ["BoardName"]="Board of Intermediate and Secondary Education, Peshawar",
            ["MarksObtained"]=979, ["MarksOutOf"]=1100, ["Grade"]="A+",
            ["SubjectsPassedLine"]="English, Urdu, Islamiyat, Pakistan Studies, Mathematics, Physics, Chemistry and Biology",
            ["RoleTitle"]="Teacher", ["SubjectTaught"]="English", ["Designation"]="Teacher",
            ["BankName"]="National Bank of Pakistan", ["BranchName"]="Main Branch",
            ["MonthlyIncome"]=78000m, ["CNIC"]="00000-0000000-0",
            ["Purpose"]="Opening of salary account"
        };
        if(sample.Values is not null)
            foreach(var pair in sample.Values)values[pair.Key]=pair.Value;
        // Keep all forms of DOB text consistent with the selected source date.
        if(values["DateOfBirth"] is DateTime selectedDate)
        {
            values["DateOfBirthInFigures"]=selectedDate.ToString("dd-MM-yyyy",CultureInfo.InvariantCulture);
            values["DateOfBirthInWords"]=selectedDate.ToString("d MMMM yyyy",CultureInfo.GetCultureInfo("en-GB"));
            values["DateOfBirthWords"]=values["DateOfBirthInWords"];
        }
        var row=source.NewRow();
        foreach(DataColumn column in source.Columns)
        {
            Check(values.TryGetValue(column.ColumnName,out object? value),
                sample.Title+": fixture specifies "+column.ColumnName);
            row[column]=value is null ? DBNull.Value :
                value.GetType()==column.DataType ? value :
                Convert.ChangeType(value,column.DataType,CultureInfo.InvariantCulture);
        }
        source.Rows.Add(row);
        using var profile=new DataTable();
        foreach(string name in new[]{"SchoolName","AddressLine","City","ContactNumber","Email"})
            profile.Columns.Add(name,typeof(string));
        var institution=profile.Rows.Add("Fahim Model School & College","University Road","Peshawar","091-0000000","office@school.example");
        if(!CertificateLetterhead.UsesPreprintedPad(path))
            document=XDocument.Parse(CertificateLetterhead.Definition(path,institution,row,sample.Subjects));
        using var printable=CertificateUi.PrintData(path,source);
        using var report=new LocalReport();
        report.EnableExternalImages=true;
        report.LoadReportDefinition(new StringReader(document.ToString()));
        report.DataSources.Add(new ReportDataSource(dataSetName,printable));

        string html=System.Net.WebUtility.HtmlDecode(Encoding.UTF8.GetString(report.Render("HTML4.0")));
        Check(html.Contains(sample.Recipient,StringComparison.Ordinal),
            sample.Title+": the displayed certificate contains the correct recipient name");
        Check(html.Contains(sample.Parent,StringComparison.Ordinal),
            sample.Title+": the displayed certificate contains the correct parent name");
        if(sample.Template is "LeaveCertificate" or "CollegeLeaveCertificate" &&
           sample.Values?.TryGetValue("CopyType",out var copy)==true && Equals(copy,"Duplicate"))
            Check(html.Contains("DUPLICATE COPY",StringComparison.Ordinal),
                sample.Title+": the saved duplicate is visibly marked");

        return report.Render("PDF");
    }
}
