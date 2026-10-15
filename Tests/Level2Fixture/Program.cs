using System.Security.Cryptography;
using System.Text.Json;

// Reference-only fixture. Students, admissions, payments and promotions must be
// created through the visible application and never by this setup utility.
Sql.ValidateTarget();
var f=Fixture.Create();
Sql.Key("SaveCampus",Sql.P("@UserId",f.Admin),Sql.P("@CampusCode","EMPTY"),
    Sql.P("@CampusName","Fictional Empty Campus"),Sql.P("@CampusType","School"));
// Admission starts in the preceding year and promotion returns in the current
// year. A further year supplies an invalid date/year combination for validation.
int followingYear=Sql.Key("SaveAcademicYear",Sql.P("@AcademicYearId",0),
    Sql.P("@YearName","Fictional "+(DateTime.UtcNow.Year+1)),
    Sql.P("@StartDate",new DateTime(DateTime.UtcNow.Year+1,1,1)),
    Sql.P("@EndDate",new DateTime(DateTime.UtcNow.Year+1,12,31)),
    Sql.P("@IsCurrent",false),Sql.P("@Remarks","Synthetic Level 2 promotion destination"),Sql.P("@UserId",f.Admin));
string secret=Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))+"!a9";
byte[] salt=RandomNumberGenerator.GetBytes(32);
byte[] hash=Rfc2898DeriveBytes.Pbkdf2(secret,salt,100_000,HashAlgorithmName.SHA512,64);
Sql.Query("""
    UPDATE dbo.Users SET Username=CASE UserId WHEN @Admin THEN N'level2_admin' ELSE N'level2_restricted' END,
        PasswordHash=@Hash,PasswordSalt=@Salt,MustChangePassword=0 WHERE UserId IN(@Admin,@Restricted);
    INSERT dbo.Staff(StaffNumber,StaffName,Gender,Status)
        VALUES(N'L2STAFF',N'Fictional Level Two Staff',N'Male',N'Active');
    """,Sql.P("@Admin",f.Admin),Sql.P("@Restricted",f.Restricted),Sql.P("@Hash",hash),Sql.P("@Salt",salt));
Identity.Check(Sql.Count("SELECT COUNT(*) FROM dbo.Students;")==0,"Fixture must not create students.");
File.WriteAllText(Path.Combine(Identity.Root,"credentials.json"),JsonSerializer.Serialize(new {
    admin="level2_admin",restricted="level2_restricted",password=secret,
    campus=f.Campus,sourceClass=f.Class6,destinationClass=f.Class7,section=f.Section,
    sourceYear=f.SourceYear,nextYear=f.NextYear,followingYear,tuition=f.Tuition,admissionFee=f.AdmissionFee,purchase=f.Purchase
}));
Console.WriteLine("PASS: fictional reference fixture; zero students, payments or promotions.");
