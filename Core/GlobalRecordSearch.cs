using System.Text.Json;
using FMSC_SMS.Pages.Security;

namespace FMSC_SMS.Core;

// Read-only lookup. Navigation rechecks access and opens saved records; no issuer/writer runs here.
internal static class GlobalRecordSearch
{
    internal static bool CanRead(string kind)=>kind=="Student"?CurrentUser.HasPermission("Students.View"):
        kind is "Bill" or "Receipt" or "PaymentReference"&&CurrentUser.HasPermission("Fees.View");

    internal static bool MatchesScope(string kind,GlobalSearchScope scope)=>scope switch {
        GlobalSearchScope.All=>kind is "Student" or "Bill" or "Receipt" or "PaymentReference",
        GlobalSearchScope.Students=>kind=="Student",
        GlobalSearchScope.Bills=>kind=="Bill",
        GlobalSearchScope.Receipts=>kind is "Receipt" or "PaymentReference",
        _=>false
    };

    internal static Task<IReadOnlyList<GlobalSearchRecord>> ReadAsync(string text,CancellationToken token)=>ReadAsync(text,GlobalSearchScope.All,token);
    internal static async Task<IReadOnlyList<GlobalSearchRecord>> ReadAsync(string text,GlobalSearchScope scope,CancellationToken token)
    {
        text=GlobalSearchInput.Normalize(text);
        if(!GlobalSearchInput.CanSearch(text)||!Enum.IsDefined(scope))return Array.Empty<GlobalSearchRecord>();
        string endpoint=DatabaseHelper.DatabaseEndpoint;
        int actor=CurrentUser.UserId;
        bool students=MatchesScope("Student",scope)&&CanRead("Student"),
            documents=(MatchesScope("Bill",scope)||MatchesScope("Receipt",scope))&&CanRead("Bill");
        if(actor<=0||!students&&!documents)return Array.Empty<GlobalSearchRecord>();
        using var data=await DatabaseHelper.QuerySqlAsync(Query,token,
            DatabaseHelper.Int("@UserId",actor),DatabaseHelper.P("@Text",text),
            DatabaseHelper.P("@Prefix",GlobalSearchInput.EscapeLike(text)+"%"),
            DatabaseHelper.P("@Contains","%"+GlobalSearchInput.EscapeLike(text)+"%"),
            DatabaseHelper.P("@Words",JsonSerializer.Serialize(GlobalSearchInput.Words(text).Select(w=>"%"+GlobalSearchInput.EscapeLike(w)+"%"))),
            DatabaseHelper.Bit("@Students",students),DatabaseHelper.Bit("@Documents",documents),DatabaseHelper.Int("@Scope",(int)scope),
            DatabaseHelper.Bit("@Number",text.Length>=3&&text.All(char.IsDigit)));
        if(actor!=CurrentUser.UserId||endpoint!=DatabaseHelper.DatabaseEndpoint||token.IsCancellationRequested)return Array.Empty<GlobalSearchRecord>();
        return data.AsEnumerable().Select(row=>new GlobalSearchRecord(Convert.ToString(row["Kind"])!,Convert.ToString(row["RecordKey"])!,
            Convert.ToInt32(row["AdmissionId"]),Convert.ToString(row["Reference"])??"",Convert.ToString(row["Name"])??"",
            Convert.ToString(row["Context"])??"",Convert.ToInt32(row["Rank"]))).Where(r=>CanRead(r.Kind)&&MatchesScope(r.Kind,scope)).Take(20).ToArray();
    }

    internal const string Query="""
        SET NOCOUNT ON;
        EXEC dbo.RequireAuthenticatedUser @UserId;
        DECLARE @Matches TABLE(Kind nvarchar(20),RecordKey nvarchar(40),AdmissionId int,
            Reference nvarchar(80),Name nvarchar(500),Context nvarchar(1000),Rank int);
        IF @Students=1 AND @Scope IN(0,1)
        BEGIN
         EXEC dbo.RequirePermission @UserId,N'Students.View';
         INSERT @Matches
         SELECT TOP(20) N'Student',CONVERT(nvarchar(40),S.AdmissionId),S.AdmissionId,
          COALESCE(NULLIF(S.AdmissionNumber,N''),NULLIF(S.QuickReference,N''),CONVERT(nvarchar(20),S.AdmissionId)),S.StudentName,
          CONCAT(N'Father: ',S.FatherName,N' · ',S.Status,N' · ',Place.CampusName,N' / ',Place.ClassName,N' / ',Place.SectionName),
          CASE WHEN S.AdmissionNumber=@Text OR S.QuickReference=@Text OR S.RegistrationNumber=@Text THEN 0
            WHEN S.StudentName=@Text THEN 1 WHEN S.StudentName LIKE @Prefix ESCAPE N'~' THEN 2 ELSE 3 END
         FROM dbo.Students S
         OUTER APPLY(SELECT TOP(1) CA.CampusName,C.ClassName,SEC.SectionName
          FROM dbo.Enrollments E JOIN dbo.CampusClassSections CCS ON CCS.ClassSectionId=E.ClassSectionId
          JOIN dbo.Campuses CA ON CA.CampusId=CCS.CampusId JOIN dbo.Classes C ON C.ClassId=CCS.ClassId
          JOIN dbo.Sections SEC ON SEC.SectionId=CCS.SectionId WHERE E.AdmissionId=S.AdmissionId
          ORDER BY CASE WHEN E.Status=N'Active' THEN 0 ELSE 1 END,E.EnrollmentDate DESC,E.EnrollmentId DESC) Place
         WHERE S.AdmissionNumber LIKE @Prefix ESCAPE N'~' OR S.QuickReference LIKE @Prefix ESCAPE N'~'
          OR S.RegistrationNumber LIKE @Prefix ESCAPE N'~'
          OR (LEN(@Text)>=2 AND NOT EXISTS(SELECT 1 FROM OPENJSON(@Words) W
           WHERE COALESCE(S.StudentName,N'') NOT LIKE W.value ESCAPE N'~' AND COALESCE(S.FatherName,N'') NOT LIKE W.value ESCAPE N'~'))
         ORDER BY 7,S.StudentName,S.AdmissionId;
        END;
        IF @Documents=1
        BEGIN
         EXEC dbo.RequirePermission @UserId,N'Fees.View';
         IF @Scope IN(0,2)
         BEGIN
         INSERT @Matches SELECT TOP(20) N'Bill',CONVERT(nvarchar(40),B.IssuedFeeBillId),B.PrimaryAdmissionId,B.BillNumber,
          COALESCE(S.StudentName,N''),CONCAT(CASE WHEN B.DocumentType=N'FB' THEN N'Family bill' ELSE N'Student bill' END,N' · ',CONVERT(nvarchar(10),B.IssuedAt,105)),
          CASE WHEN B.BillNumber=@Text THEN 0 ELSE 3 END
         FROM dbo.IssuedFeeBills B LEFT JOIN dbo.Students S ON S.AdmissionId=B.PrimaryAdmissionId
         WHERE B.BillNumber LIKE @Prefix ESCAPE N'~' OR (@Number=1 AND B.BillNumber LIKE @Contains ESCAPE N'~')
         ORDER BY 7,B.IssuedAt DESC,B.BillNumber;
         END;
         IF @Scope IN(0,3)
         BEGIN
         INSERT @Matches SELECT TOP(20) N'Receipt',CONVERT(nvarchar(40),R.CollectionReceiptId),R.PrimaryAdmissionId,R.ReceiptNumber,
          COALESCE(S.StudentName,N''),CONCAT(N'Saved receipt · ',CONVERT(nvarchar(10),R.TransactionDate,105),N' · ',R.PaymentMethod),
          CASE WHEN R.ReceiptNumber=@Text THEN 0 ELSE 3 END
         FROM dbo.CollectionReceipts R LEFT JOIN dbo.Students S ON S.AdmissionId=R.PrimaryAdmissionId
         WHERE R.ReceiptNumber LIKE @Prefix ESCAPE N'~' OR (@Number=1 AND R.ReceiptNumber LIKE @Contains ESCAPE N'~')
         ORDER BY 7,R.TransactionDate DESC,R.ReceiptNumber;
         INSERT @Matches SELECT TOP(20) N'PaymentReference',CONVERT(nvarchar(40),P.FeePaymentId),P.AdmissionId,P.ReceiptNumber,
          COALESCE(S.StudentName,N''),CONCAT(N'Payment entry · ',CONVERT(nvarchar(10),P.PaymentDate,105),N' · ',P.PaymentMethod),
          CASE WHEN P.ReceiptNumber=@Text THEN 0 ELSE 3 END
         FROM dbo.FeePayments P LEFT JOIN dbo.Students S ON S.AdmissionId=P.AdmissionId
         WHERE NULLIF(P.ReceiptNumber,N'') IS NOT NULL AND (P.ReceiptNumber LIKE @Prefix ESCAPE N'~'
          OR (@Number=1 AND P.ReceiptNumber LIKE @Contains ESCAPE N'~'))
          AND NOT EXISTS(SELECT 1 FROM dbo.CollectionReceipts R WHERE R.ReceiptNumber=P.ReceiptNumber)
         ORDER BY 7,P.PaymentDate DESC,P.FeePaymentId DESC;
         END;
        END;
        SELECT TOP(20) * FROM @Matches ORDER BY Rank,
          CASE Kind WHEN N'Bill' THEN 0 WHEN N'Receipt' THEN 1 WHEN N'PaymentReference' THEN 2 ELSE 3 END,Reference,RecordKey;
        """;
    internal const string ReadBill="""
        EXEC dbo.RequirePermission @UserId,N'Fees.View';
        SELECT BillNumber,IssuedAt,DocumentType,CONVERT(nvarchar(max),SnapshotXml) SnapshotXml
        FROM dbo.IssuedFeeBills WHERE IssuedFeeBillId=@Id AND BillNumber=@Reference;
        """;
    internal const string ReadReceiptIdentity="""
        EXEC dbo.RequirePermission @UserId,N'Fees.View';
        SELECT ReceiptNumber FROM dbo.CollectionReceipts WHERE CollectionReceiptId=@Id AND ReceiptNumber=@Reference;
        """;
    internal const string ReadPayment="""
        EXEC dbo.RequirePermission @UserId,N'Fees.View';
        SELECT ReceiptNumber,AdmissionId,PaymentDate,TotalAmount,PaymentMethod,Status
        FROM dbo.FeePayments WHERE FeePaymentId=@Id AND ReceiptNumber=@Reference;
        """;
}
