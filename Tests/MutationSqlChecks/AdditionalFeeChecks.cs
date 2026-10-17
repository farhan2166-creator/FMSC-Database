using FMSC_SMS.Core;
using Microsoft.Data.SqlClient;

internal static class AdditionalFeeChecks
{
    internal static async Task RunAsync(int admin)
    {
        int checks=0;
        void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS: "+message);}
        DataTable Query(string sql,params SqlParameter[] parameters)
        {
            using var c=DatabaseHelper.GetConnection();c.Open();using var q=new SqlCommand(sql,c){CommandTimeout=120};q.Parameters.AddRange(parameters);
            var data=new DataTable();using var a=new SqlDataAdapter(q);a.Fill(data);return data;
        }
        string prefix="RVA-"+Guid.NewGuid().ToString("N")[..10];
        var fixture=Query("""
            SET NOCOUNT ON;SET XACT_ABORT ON;BEGIN TRANSACTION;
            INSERT dbo.AcademicYears(YearName,StartDate,EndDate,IsCurrent) VALUES(@Prefix,DATEADD(year,-1,CONVERT(date,GETDATE())),DATEADD(year,1,CONVERT(date,GETDATE())),0);DECLARE @Year int=SCOPE_IDENTITY();
            INSERT dbo.Campuses(CampusCode,CampusName) VALUES(@Prefix,@Prefix);DECLARE @Campus int=SCOPE_IDENTITY();
            INSERT dbo.AcademicPrograms(ProgramCode,ProgramName,ProgramType) VALUES(@Prefix,@Prefix,N'School');DECLARE @Program int=SCOPE_IDENTITY();
            INSERT dbo.Classes(ClassCode,ClassName,AcademicProgramId) VALUES(@Prefix,@Prefix,@Program);DECLARE @Class int=SCOPE_IDENTITY();
            INSERT dbo.Sections(SectionCode,SectionName) VALUES(@Prefix,@Prefix);DECLARE @Section int=SCOPE_IDENTITY();
            INSERT dbo.CampusClassSections(CampusId,ClassId,SectionId) VALUES(@Campus,@Class,@Section);DECLARE @Placement int=SCOPE_IDENTITY();
            INSERT dbo.FeeTypes(FeeTypeCode,FeeTypeName,DefaultFrequency,IsMandatory,IsSystemDefined) VALUES(@Prefix,@Prefix,N'As Needed',0,0);DECLARE @Fee int=SCOPE_IDENTITY();
            INSERT dbo.FeeTypes(FeeTypeCode,FeeTypeName,DefaultFrequency,IsMandatory,IsSystemDefined) VALUES(CONCAT(@Prefix,N'B'),CONCAT(@Prefix,N'B'),N'As Needed',0,0);DECLARE @OtherFee int=SCOPE_IDENTITY();
            DECLARE @Number int=1000000000;WHILE EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionNumber=CONVERT(varchar(40),@Number))SET @Number+=1;
            INSERT dbo.Students(AdmissionNumber,AdmissionDate,AdmissionCampusId,StudentName,Disability,Status,LegacySetupPending) VALUES(CONVERT(varchar(40),@Number),DATEADD(month,-1,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)),@Campus,@Prefix,N'No',N'Active',0);DECLARE @A int=SCOPE_IDENTITY();
            INSERT dbo.Enrollments(AdmissionId,AcademicYearId,ClassSectionId,EnrollmentDate,Status,BillingReady,PlacementConfirmedAt) VALUES(@A,@Year,@Placement,DATEADD(month,-1,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)),N'Active',1,SYSDATETIME());DECLARE @E1 int=SCOPE_IDENTITY();
            SET @Number+=1;WHILE EXISTS(SELECT 1 FROM dbo.Students WHERE AdmissionNumber=CONVERT(varchar(40),@Number))SET @Number+=1;
            INSERT dbo.Students(AdmissionNumber,AdmissionDate,AdmissionCampusId,StudentName,Disability,Status,LegacySetupPending) VALUES(CONVERT(varchar(40),@Number),DATEADD(month,-1,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)),@Campus,CONCAT(@Prefix,N'B'),N'No',N'Active',0);DECLARE @B int=SCOPE_IDENTITY();
            INSERT dbo.Enrollments(AdmissionId,AcademicYearId,ClassSectionId,EnrollmentDate,Status,BillingReady,PlacementConfirmedAt) VALUES(@B,@Year,@Placement,DATEADD(month,-1,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1)),N'Active',1,SYSDATETIME());DECLARE @E2 int=SCOPE_IDENTITY();
            COMMIT TRANSACTION;
            SELECT @Fee Fee,@OtherFee OtherFee,@E1 FirstEnrollment,@E2 SecondEnrollment;
            """,DatabaseHelper.P("@Prefix",prefix)).Rows[0];
        int fee=(int)fixture["Fee"],otherFee=(int)fixture["OtherFee"],first=(int)fixture["FirstEnrollment"],second=(int)fixture["SecondEnrollment"];
        Guid request=Guid.NewGuid();
        SqlParameter[] Args(Guid id,int feeId,decimal amount,int[] enrollments,string reason="Disposable additional charge",decimal discount=0m,DateTime? date=null)=>new[]{
            DatabaseHelper.Int("@UserId",admin),new SqlParameter("@RequestId",SqlDbType.UniqueIdentifier){Value=id},DatabaseHelper.Int("@FeeTypeId",feeId),DatabaseHelper.Date("@ChargeDate",date??DateTime.Today),DatabaseHelper.P("@Amount",amount),DatabaseHelper.P("@DiscountAmount",discount),DatabaseHelper.P("@Reason",reason),new SqlParameter("@Selection",SqlDbType.Structured){TypeName="dbo.ManualChargeSelectionType",Value=Tvp.ManualChargeSelection(enrollments)}};
        DataTable Post(SqlParameter[] parameters)
        {
            // A fresh SQL connection models reconnect, keeping the original request ID.
            using var c=DatabaseHelper.GetConnection();c.Open();using var q=new SqlCommand("dbo.PostAdditionalFee",c){CommandType=CommandType.StoredProcedure,CommandTimeout=120};q.Parameters.AddRange(parameters);
            var table=new DataTable();using var a=new SqlDataAdapter(q);a.Fill(table);return table;
        }
        int Charges(Guid id)=>Convert.ToInt32(Query("SELECT COUNT(*) FROM dbo.BillingCharges WHERE ManualRequestId=@ID",new SqlParameter("@ID",SqlDbType.UniqueIdentifier){Value=id}).Rows[0][0]);
        var committed=Post(Args(request,fee,100m,new[]{first,second}));
        try{throw new IOException("Injected lost client reply after procedure commit");}catch(IOException){ }
        Check(committed.Rows.Count==2&&Charges(request)==2,"lost post-commit client reply leaves one charge for each selected enrollment");
        string Ids(DataTable rows)=>string.Join(",",rows.AsEnumerable().Select(r=>Convert.ToInt64(r["BillingChargeId"])).OrderBy(x=>x));
        var replay=Post(Args(request,fee,100m,new[]{second,first}));
        Check(Ids(replay)==Ids(committed)&&Charges(request)==2,"same request on a fresh connection, with reordered selection, returns the exact original charges");
        void Mismatch(SqlParameter[] parameters,string label)
        {
            try{Post(parameters);throw new Exception("Expected changed-payload refusal: "+label);}
            catch(SqlException e) when(e.Number==53026){Check(Charges(request)==2,label+" is refused without another charge");}
        }
        Mismatch(Args(request,fee,101m,new[]{first,second}),"changed amount");
        Mismatch(Args(request,otherFee,100m,new[]{first,second}),"changed fee type");
        Mismatch(Args(request,fee,100m,new[]{first,second},discount:1m),"changed discount");
        Mismatch(Args(request,fee,100m,new[]{first,second},reason:"Changed reason"),"changed reason");
        Mismatch(Args(request,fee,100m,new[]{first,second},date:DateTime.Today.AddDays(-1)),"changed charge date");
        Mismatch(Args(request,fee,100m,new[]{first}),"changed enrollment selection");
        Check(Convert.ToInt32(Query("SELECT COUNT(*) FROM dbo.ManualFeePostingRequests WHERE RequestId=@ID",new SqlParameter("@ID",SqlDbType.UniqueIdentifier){Value=request}).Rows[0][0])==1,"one authoritative posting marker remains after all refused changes");
        Guid concurrent=Guid.NewGuid();var copies=await Task.WhenAll(Task.Run(()=>Post(Args(concurrent,fee,150m,new[]{first,second}))),Task.Run(()=>Post(Args(concurrent,fee,150m,new[]{second,first}))));
        Check(Ids(copies[0])==Ids(copies[1])&&Charges(concurrent)==2,"concurrent copies of one request produce one charge per selected enrollment");
        Check(Ids(Post(Args(request,fee,100m,new[]{first,second})))==Ids(committed),"the original request remains recoverable after intervening requests and reconnection");
        Console.WriteLine($"PASS: {checks} native additional-fee request replay checks. These do not simulate an application restart that loses its in-memory request ID.");
    }
}
