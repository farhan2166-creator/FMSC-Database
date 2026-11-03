using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;

internal static partial class Program
{
    private static async Task CheckSqlAsync()
    {
        var supplied=Environment.GetEnvironmentVariable("FMSC_GLOBAL_SEARCH_SQL_CONNECTION")??throw new InvalidOperationException("SQL checks require an explicitly supplied local disposable SQL connection with Initial Catalog=master.");
        var options=new SqlConnectionStringBuilder(supplied);
        bool local=options.DataSource.StartsWith("127.0.0.1",StringComparison.OrdinalIgnoreCase)||options.DataSource.StartsWith("localhost",StringComparison.OrdinalIgnoreCase)
            ||options.DataSource.StartsWith("(localdb)\\Fmsc_GlobalSearchChecks_",StringComparison.OrdinalIgnoreCase);
        if(!local||!options.InitialCatalog.Equals("master",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Fixture refuses non-local endpoints and school database catalogs.");
        string database="Fmsc_GlobalSearchChecks_"+Guid.NewGuid().ToString("N");
        using var master=new SqlConnection(options.ConnectionString);await master.OpenAsync();
        async Task Execute(SqlConnection connection,string sql){using var cmd=new SqlCommand(sql,connection);await cmd.ExecuteNonQueryAsync();}
        await Execute(master,$"CREATE DATABASE [{database}]");
        try
        {
            options.InitialCatalog=database;using var cn=new SqlConnection(options.ConnectionString);await cn.OpenAsync();
            await Execute(cn,"""
                CREATE TABLE dbo.FixtureRights(UserId int,Permission nvarchar(100));
                INSERT dbo.FixtureRights VALUES(1,N'Students.View'),(1,N'Fees.View'),(2,N'Students.View'),(3,N'Fees.View'),(4,N'Authenticated');
                CREATE TABLE dbo.Students(AdmissionId int PRIMARY KEY,AdmissionNumber nvarchar(50),QuickReference nvarchar(50),RegistrationNumber nvarchar(50),StudentName nvarchar(500),FatherName nvarchar(500),Status nvarchar(30));
                CREATE TABLE dbo.Enrollments(EnrollmentId int,AdmissionId int,ClassSectionId int,EnrollmentDate date,Status nvarchar(30));
                CREATE TABLE dbo.CampusClassSections(ClassSectionId int,CampusId int,ClassId int,SectionId int);
                CREATE TABLE dbo.Campuses(CampusId int,CampusName nvarchar(100));
                CREATE TABLE dbo.Classes(ClassId int,ClassName nvarchar(100));
                CREATE TABLE dbo.Sections(SectionId int,SectionName nvarchar(100));
                CREATE TABLE dbo.IssuedFeeBills(IssuedFeeBillId uniqueidentifier,BillNumber nvarchar(80),PrimaryAdmissionId int,DocumentType nvarchar(10),IssuedAt datetime2,SnapshotXml xml,StudentIdsJson nvarchar(max));
                CREATE TABLE dbo.CollectionReceipts(CollectionReceiptId uniqueidentifier,PrimaryAdmissionId int,ReceiptNumber nvarchar(80),TransactionDate datetime2,PaymentMethod nvarchar(50),SnapshotJson nvarchar(max));
                CREATE TABLE dbo.CollectionReceiptPayments(CollectionReceiptId uniqueidentifier,FeePaymentId int);
                CREATE TABLE dbo.FeePayments(FeePaymentId int,AdmissionId int,ReceiptNumber nvarchar(80),PaymentDate datetime2,PaymentMethod nvarchar(50),TotalAmount decimal(12,2),Status nvarchar(30));
                INSERT dbo.Students VALUES(6508,N'6508',NULL,N'Reg-1',N'Ali Example',N'Khan',N'Active'),
                 (6509,NULL,N'QR-6509',NULL,N'Pending Example',N'Khan',N'Pending'),
                 (6510,N'6510',NULL,NULL,N'Former Example',N'Khan',N'Left'),
                 (6511,N'6511',NULL,NULL,N'Literal %_[~ Example',N'Parent',N'Active'),
                 (6512,N'6512',NULL,NULL,N'Ali Example',N'Other Parent',N'Active'),
                 (6513,N'6513',NULL,NULL,N'اردو مثال',N'والد',N'Active');
                INSERT dbo.Campuses VALUES(1,N'Fictional Campus');INSERT dbo.Classes VALUES(1,N'Nine');INSERT dbo.Sections VALUES(1,N'A');
                INSERT dbo.CampusClassSections VALUES(1,1,1,1);INSERT dbo.Enrollments VALUES(1,6508,1,'2026-04-01',N'Active');
                INSERT dbo.IssuedFeeBills VALUES('00000000-0000-0000-0000-000000000001',N'B-2026-00015',6508,N'B','2026-10-20',N'<saved>original</saved>',N'[6508]');
                INSERT dbo.CollectionReceipts VALUES('00000000-0000-0000-0000-000000000002',6508,N'FR-2026-00012','2026-10-20',N'Cash',N'{"Students":[{"AdmissionId":6508}]}');
                INSERT dbo.FeePayments VALUES(9001,6508,N'R-2026-00012','2026-10-20',N'Cash',200,N'Completed'),
                 (9002,6508,N'FR-2026-00012','2026-10-20',N'Cash',100,N'Completed');
                INSERT dbo.CollectionReceiptPayments VALUES('00000000-0000-0000-0000-000000000002',9001),('00000000-0000-0000-0000-000000000002',9002);
                """);
            await Execute(cn,"CREATE PROCEDURE dbo.RequireAuthenticatedUser @UserId int AS IF NOT EXISTS(SELECT 1 FROM dbo.FixtureRights WHERE UserId=@UserId) THROW 51001,'Authentication refused',1;");
            await Execute(cn,"CREATE PROCEDURE dbo.RequirePermission @UserId int,@Permission nvarchar(100) AS IF NOT EXISTS(SELECT 1 FROM dbo.FixtureRights WHERE UserId=@UserId AND Permission=@Permission) THROW 51002,'Permission refused',1;");
            async Task<DataTable> Search(string text,int user=1,bool students=true,bool documents=true,GlobalSearchScope scope=GlobalSearchScope.All)
            {
                using var cmd=new SqlCommand(GlobalRecordSearch.Query,cn);
                cmd.Parameters.AddWithValue("@UserId",user);cmd.Parameters.AddWithValue("@Text",text);
                cmd.Parameters.AddWithValue("@Prefix",GlobalSearchInput.EscapeLike(text)+"%");cmd.Parameters.AddWithValue("@Contains","%"+GlobalSearchInput.EscapeLike(text)+"%");
                cmd.Parameters.AddWithValue("@Words",System.Text.Json.JsonSerializer.Serialize(GlobalSearchInput.Words(text).Select(w=>"%"+GlobalSearchInput.EscapeLike(w)+"%")));
                cmd.Parameters.AddWithValue("@Students",students);cmd.Parameters.AddWithValue("@Documents",documents);cmd.Parameters.AddWithValue("@Number",text.Length>=3&&text.All(char.IsDigit));
                cmd.Parameters.AddWithValue("@Scope",(int)scope);
                var data=new DataTable();using var reader=await cmd.ExecuteReaderAsync();data.Load(reader);return data;
            }
            using(var data=await Search("6508"))Check(data.Rows[0]["Kind"].ToString()=="Student"&&data.Rows[0]["Rank"].ToString()=="0","Exact admission matches rank first");
            using(var data=await Search("Example Ali",scope:GlobalSearchScope.Students))Check(data.Rows.Count==2,"Student/father keywords work in either order without merging duplicate names");
            using(var data=await Search("QR-6509"))Check(data.Rows.Count==1,"Pending quick references are searchable");
            using(var data=await Search("Former"))Check(data.Rows.Count==1,"Former students remain discoverable");
            using(var data=await Search("%_[~"))Check(data.Rows.Count==1&&data.Rows[0]["Reference"].ToString()=="6511","LIKE punctuation remains literal");
            using(var data=await Search("اردو"))Check(data.Rows.Count==1,"Unicode names are preserved");
            foreach(var pair in new[]{("B-2026-00015","Bill"),("FR-2026-00012","Receipt"),("R-2026-00012","PaymentReference")})
            {using var data=await Search(pair.Item1);Check(data.Rows.Count==1&&data.Rows[0]["Kind"].ToString()==pair.Item2,"Document/payment references return their exact record without duplicate collection rows");}
            using(var data=await Search("0012"))Check(data.Rows.Count==2,"Numeric document suffixes preserve distinct original and payment references");
            using(var data=await Search("6508",scope:GlobalSearchScope.Bills))Check(data.Rows.Count==1&&data.Rows[0]["Kind"].ToString()=="Bill","Bill scope finds saved documents by admission without returning student rows");
            using(var data=await Search("Example Ali",scope:GlobalSearchScope.Bills))Check(data.Rows.Count==1,"Bills can be found with student keywords in either order");
            using(var data=await Search("6508",scope:GlobalSearchScope.Receipts))Check(data.Rows.Count==1&&data.Rows[0]["Kind"].ToString()=="Receipt","Student searches show the saved receipt once rather than each linked payment alias");
            using(var data=await Search("Ali",3,false,true,GlobalSearchScope.Receipts))Check(data.Rows.Count==1,"Fees-only operators can find permitted documents by student name");
            using(var data=await Search("2026",scope:GlobalSearchScope.Bills))Check(data.Rows.Count==1&&data.Rows[0]["Kind"].ToString()=="Bill","Bill scope is applied before the SQL result limit");
            using(var data=await Search("2026",scope:GlobalSearchScope.Receipts))Check(data.Rows.Count==2&&data.AsEnumerable().All(r=>r["Kind"].ToString() is "Receipt" or "PaymentReference"),"Receipt scope preserves both saved receipts and payment aliases");
            using(var data=await Search("2026",scope:GlobalSearchScope.Students))Check(data.Rows.Count==0,"Student scope excludes document-number matches in SQL");
            await Execute(cn,"""
                WITH Numbers AS(SELECT TOP(25) ROW_NUMBER() OVER(ORDER BY object_id) AS N FROM sys.all_objects)
                INSERT dbo.IssuedFeeBills
                SELECT NEWID(),CONCAT(N'B-2026-',10000+N),6508,N'B',DATEADD(day,CONVERT(int,N),'2026-10-20'),N'<saved>fictional</saved>',N'[6508]' FROM Numbers;
                """);
            using(var data=await Search("2026"))Check(data.Rows.Count==20&&data.AsEnumerable().All(r=>r["Kind"].ToString()=="Bill"),"Broad lookups remain bounded with many document matches");
            using(var data=await Search("2026",scope:GlobalSearchScope.Receipts))Check(data.Rows.Count==2,"Receipt scope finds records crowded out of All by twenty bills");
            using(var data=await Search("Ali",scope:GlobalSearchScope.Bills))Check(data.Rows.Count==20&&data.Rows[0]["Reference"].ToString()=="B-2026-10025","Name searches put the newest matching documents first within their type");
            using(var data=await Search("Ali",2,true,false))Check(data.Rows.Count==2,"Student permission works without financial access");
            using(var data=await Search("FR-2026",3,false,true))Check(data.Rows.Count==1,"Financial lookup does not require student-navigation permission");
            foreach(var pair in new[]{(User:2,Students:false,Documents:true),(User:3,Students:true,Documents:false),(User:4,Students:true,Documents:true)})
            {
                bool denied=false;try {using var ignored=await Search("6508",pair.User,pair.Students,pair.Documents);}catch(SqlException){denied=true;}
                Check(denied,"SQL rejects unauthorized search flags independently of the desktop");
            }
            using(var cmd=new SqlCommand(GlobalRecordSearch.ReadBill,cn))
            {
                cmd.Parameters.AddWithValue("@UserId",1);cmd.Parameters.AddWithValue("@Id",Guid.Parse("00000000-0000-0000-0000-000000000001"));cmd.Parameters.AddWithValue("@Reference","B-2026-00015");
                using var reader=await cmd.ExecuteReaderAsync();Check(await reader.ReadAsync()&&reader["SnapshotXml"].ToString()=="<saved>original</saved>","Bill lookup returns the original stored snapshot");
            }
            await Execute(cn,"""
                INSERT dbo.Students VALUES(6514,N'6514',NULL,NULL,N'Saved Child',N'Khan',N'Left'),
                 (6515,N'6515',NULL,NULL,N'New Child',N'Khan',N'Active'),
                 (6516,NULL,NULL,NULL,N'Unnumbered Student',N'Parent',N'Active');
                INSERT dbo.IssuedFeeBills VALUES('00000000-0000-0000-0000-000000000003',N'FB-2026-00999',6508,N'FB','2026-12-01',N'<saved>family original</saved>',N'[6508,6514]'),
                 ('00000000-0000-0000-0000-000000000004',N'B-2026-00888',6516,N'B','2026-12-02',N'<saved>legacy</saved>',N'invalid old metadata');
                INSERT dbo.CollectionReceipts VALUES('00000000-0000-0000-0000-000000000003',6508,N'FR-2026-00999','2026-12-01',N'Cash',N'{"Students":[{"AdmissionId":6508},{"AdmissionId":6514}]}'),
                 ('00000000-0000-0000-0000-000000000004',6516,N'FR-2026-00888','2026-12-03',N'Cash',N'invalid old metadata');
                INSERT dbo.FeePayments VALUES(9003,6514,N'R-2025-00888','2025-10-20',N'Cash',50,N'Completed');
                """);
            using(var data=await Search("Saved Child",scope:GlobalSearchScope.Bills))
                Check(data.Rows.Count==1&&data.Rows[0]["Reference"].ToString()=="FB-2026-00999"&&Convert.ToInt32(data.Rows[0]["AdmissionId"])==6514&&data.Rows[0]["Context"].ToString()!.Contains("6514"),"Family bills match the saved child and retain that child's account target");
            using(var data=await Search("6514",scope:GlobalSearchScope.Receipts))
                Check(data.Rows.Count==2&&data.Rows[0]["Reference"].ToString()=="FR-2026-00999"&&Convert.ToInt32(data.Rows[0]["AdmissionId"])==6514&&data.Rows[1]["Kind"].ToString()=="PaymentReference","Saved family receipts and older standalone payments remain discoverable for former children");
            foreach(var scope in new[]{GlobalSearchScope.Bills,GlobalSearchScope.Receipts})
            {using var data=await Search("New Child",scope:scope);Check(data.Rows.Count==0,"A child absent from saved participants does not acquire old family documents");}
            using(var data=await Search("Khan",scope:GlobalSearchScope.Bills))
                Check(data.AsEnumerable().Count(r=>r["Reference"].ToString()=="FB-2026-00999")==1,"A family document appears once when several saved children match");
            using(var data=await Search("Unnumbered",scope:GlobalSearchScope.Students))
                Check(data.Rows.Count==1&&data.Rows[0]["Reference"].ToString()=="Not assigned"&&data.Rows[0]["RecordKey"].ToString()=="6516","An internal student key never becomes a displayed admission number");
            foreach(var scope in new[]{GlobalSearchScope.Bills,GlobalSearchScope.Receipts})
            {using var data=await Search("Unnumbered",scope:scope);Check(data.Rows.Count==1,"Missing legacy participant metadata preserves primary-student lookup without breaking the search");}
            using(var data=await Search("Unnumbered"))
                Check(data.Rows.Count==3&&data.Rows[0]["Kind"].ToString()=="Student"&&data.Rows[1]["Kind"].ToString()=="Receipt","All retains the better identity match and sorts equally ranked documents newest first across types");
            using(var data=await Search("FB-2026-00999",3,false,true,GlobalSearchScope.Bills))
                Check(data.Rows.Count==1&&data.Rows[0]["Rank"].ToString()=="0","Exact document references still rank first with family-name lookup enabled");
            await Execute(cn,"IF (SELECT COUNT(*) FROM dbo.IssuedFeeBills)<>28 OR (SELECT COUNT(*) FROM dbo.CollectionReceipts)<>3 OR (SELECT SUM(TotalAmount) FROM dbo.FeePayments)<>350 THROW 51003,'Search changed stored documents or money',1;");
            Check(true,"All searches leave document counts and recorded money unchanged");
        }
        finally
        {
            SqlConnection.ClearAllPools();await Execute(master,$"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]");
        }
    }
}
