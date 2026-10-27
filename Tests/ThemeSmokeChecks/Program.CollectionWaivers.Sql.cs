using System.Data;
using Microsoft.Data.SqlClient;
using FMSC_SMS.Core;

internal static partial class Program
{
    private static void CheckCollectionWaiverSql()
    {
        var connectionText=Environment.GetEnvironmentVariable("FMSC_WAIVER_TEST_CONNECTION")??throw new InvalidOperationException("An owned test database is required.");
        var builder=new SqlConnectionStringBuilder(connectionText);
        if(!builder.InitialCatalog.StartsWith("FMSC_CollectionWaiver_",StringComparison.Ordinal)||builder.InitialCatalog.Length!=54)
            throw new InvalidOperationException("Only the uniquely owned collection-waiver clone may be tested.");
        using var connection=new SqlConnection(connectionText);connection.Open();
        using var tx=connection.BeginTransaction();
        DataTable Query(string sql,params SqlParameter[] args)
        {
            using var cmd=new SqlCommand(sql,connection,tx);cmd.CommandTimeout=60;cmd.Parameters.AddRange(args);
            var data=new DataTable();using var adapter=new SqlDataAdapter(cmd);adapter.Fill(data);return data;
        }
        DataSet QuerySet(string sql,params SqlParameter[] args)
        {
            using var cmd=new SqlCommand(sql,connection,tx);cmd.CommandTimeout=60;cmd.Parameters.AddRange(args);
            var data=new DataSet();using var adapter=new SqlDataAdapter(cmd);adapter.Fill(data);return data;
        }
        int actor=Convert.ToInt32(Query("SELECT TOP(1) U.UserId FROM dbo.Users U JOIN dbo.Roles R ON R.RoleId=U.RoleId WHERE R.RoleName=N'Administrator';").Rows[0][0]);
        using var setup=Query("""
            SET NOCOUNT ON; SET XACT_ABORT ON;
            DECLARE @Enrollment int=(SELECT TOP(1) EnrollmentId FROM dbo.Enrollments WHERE AdmissionId=542 ORDER BY EnrollmentId DESC);
            IF @Enrollment IS NULL THROW 51000,'The source student fixture is missing.',1;
            CREATE TABLE #ReviewFixture(Kind nvarchar(30),ChargeId bigint);
            INSERT dbo.FeeTypes(FeeTypeCode,FeeTypeName,IsMandatory,IsTransportLinked)
             VALUES(CONCAT(N'CWR-',LEFT(CONVERT(nvarchar(36),NEWID()),8)),N'Collection waiver fixture',0,0);
            DECLARE @Fee int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.StudentFeeAssignments(AdmissionId,EnrollmentId,FeeTypeId,Amount,DiscountAmount,Frequency)
             VALUES(542,@Enrollment,@Fee,700,0,N'As Needed');
            DECLARE @Assignment int=CONVERT(int,SCOPE_IDENTITY());
            DECLARE @Kind nvarchar(30),@Gross decimal(12,2),@Discount decimal(12,2),@Paid decimal(12,2),@Credit bit;
            DECLARE Fixture CURSOR LOCAL FAST_FORWARD FOR SELECT * FROM (VALUES
             (N'Unpaid',500,0,0,0),(N'Waived',700,700,0,0),(N'Cash paid',400,0,400,0),
             (N'Paid + waived',600,400,200,0),(N'Credit paid',300,0,300,1)) F(Kind,Gross,Discount,Paid,Credit);
            OPEN Fixture;FETCH NEXT FROM Fixture INTO @Kind,@Gross,@Discount,@Paid,@Credit;
            WHILE @@FETCH_STATUS=0
            BEGIN
             INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind)
              VALUES(@Assignment,@Enrollment,542,@Fee,CONVERT(date,GETDATE()),CONVERT(date,GETDATE()),@Gross,@Discount,N'Manual');
             DECLARE @Charge bigint=CONVERT(bigint,SCOPE_IDENTITY());
             INSERT #ReviewFixture VALUES(@Kind,@Charge);
             IF @Paid>0
             BEGIN
              INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Status,CreatedByUserId,IsCreditApplication)
               VALUES(542,@Enrollment,CONVERT(date,GETDATE()),CONCAT(N'CWR-',LEFT(CONVERT(nvarchar(36),NEWID()),25)),@Paid,N'Cash',N'Completed',@Actor,@Credit);
              DECLARE @Payment int=CONVERT(int,SCOPE_IDENTITY());
              INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(@Payment,542,@Charge,@Paid);
             END
             IF @Gross-@Discount-@Paid=0 UPDATE dbo.BillingCharges SET Status=N'Paid' WHERE BillingChargeId=@Charge;
             FETCH NEXT FROM Fixture INTO @Kind,@Gross,@Discount,@Paid,@Credit;
            END
            CLOSE Fixture;DEALLOCATE Fixture;
            SELECT * FROM #ReviewFixture;
            """,new SqlParameter("@Actor",actor));
        long Id(string kind)=>setup.AsEnumerable().Single(row=>row.Field<string>("Kind")==kind).Field<long>("ChargeId");
        DataTable Read()=>Query(CollectionChargeReview.Query,new SqlParameter("@UserId",actor),new SqlParameter("@AdmissionId",542));
        bool Contains(DataTable data,long id)=>data.AsEnumerable().Any(row=>row.Field<long>("BillingChargeId")==id);
        using(var data=Read())
        {
            Check(Contains(data,Id("Unpaid")),"An unpaid charge remains collectable");
            Check(!Contains(data,Id("Cash paid"))&&!Contains(data,Id("Credit paid")),"Cash-settled and credit-settled fees remain out of the collection grid");
            foreach(string kind in new[]{"Waived","Paid + waived"})
                Check(CollectionChargeReview.IsPendingWaiver(data.AsEnumerable().Single(row=>row.Field<long>("BillingChargeId")==Id(kind))),
                    "A newly waived fee stays visible even when its database status is Paid");
        }
        int payments=Convert.ToInt32(Query("SELECT COUNT(*) FROM dbo.FeePayments WHERE AdmissionId=542;").Rows[0][0]);
        using var acknowledged=Query("""
            DECLARE @Now datetime2(0)=SYSDATETIME();
            EXEC dbo.IssueCollectionReceipt @Actor,542,@Now,N'Fee changes',N'Owned grid regression acknowledgement',N'[542]',N'[]',N'[]',0,0,0,1;
            """,new SqlParameter("@Actor",actor));
        using(var data=Read())
        {
            Check(!Contains(data,Id("Waived"))&&!Contains(data,Id("Paid + waived")),"Acknowledged waivers disappear after reload");
            Check(Contains(data,Id("Unpaid")),"Acknowledging a waiver leaves actual unpaid charges untouched");
        }
        Check(payments==Convert.ToInt32(Query("SELECT COUNT(*) FROM dbo.FeePayments WHERE AdmissionId=542;").Rows[0][0]),"A waiver acknowledgement creates no payment record");
        using(var saved=QuerySet("EXEC dbo.GetCollectionReceipt @Actor,@Receipt;",new SqlParameter("@Actor",actor),new SqlParameter("@Receipt",acknowledged.Rows[0]["ReceiptNumber"])))
        using(var previous=Query(FeeChangeReport.PreviousPlansQuery,new SqlParameter("@UserId",actor),new SqlParameter("@ReceiptId",saved.Tables[0].Rows[0]["CollectionReceiptId"])))
        using(var note=FeeChangeReport.BuildData(new InstitutionProfile.PrintIdentity("Fixture school","",""),saved.Tables[0].Rows[0],saved.Tables[1],saved.Tables[2],saved.Tables[3],previous))
        {
            Check(note.AsEnumerable().Any(row=>row.Field<string>("ChangeLabel")=="Waived"),"Actual saved acknowledgement data builds the dedicated fee-change note");
            Check(note.AsEnumerable().All(row=>row.Field<decimal>("GroupOutstanding")==Convert.ToDecimal(saved.Tables[0].Rows[0]["BalanceRemaining"])),
                "The actual note and previous-plan snapshot query preserve the saved outstanding total");
        }
        using var newWaiver=Query("""
            INSERT dbo.BillingCharges(StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,ChargeDate,DueDate,GrossAmount,DiscountAmount,ChargeKind)
             SELECT StudentFeeAssignmentId,EnrollmentId,AdmissionId,FeeTypeId,ChargeDate,DueDate,900,900,ChargeKind FROM dbo.BillingCharges WHERE BillingChargeId=@Original;
            DECLARE @Waived bigint=CONVERT(bigint,SCOPE_IDENTITY());SELECT @Waived ChargeId;
            """,new SqlParameter("@Original",Id("Unpaid")));
        long newId=newWaiver.Rows[0].Field<long>("ChargeId");
        using(var data=Read())Check(Contains(data,newId),"A later waiver is visible even after an earlier acknowledgement");
        using var collected=Query("""
            DECLARE @Enrollment int=(SELECT EnrollmentId FROM dbo.BillingCharges WHERE BillingChargeId=@Charge);
            INSERT dbo.FeePayments(AdmissionId,EnrollmentId,PaymentDate,ReceiptNumber,TotalAmount,PaymentMethod,Status,CreatedByUserId)
             VALUES(542,@Enrollment,CONVERT(date,GETDATE()),CONCAT(N'CWR-',LEFT(CONVERT(nvarchar(36),NEWID()),25)),500,N'Cash',N'Completed',@Actor);
            DECLARE @Payment int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.FeePaymentAllocations(FeePaymentId,AdmissionId,BillingChargeId,Amount) VALUES(@Payment,542,@Charge,500);
            DECLARE @Payments nvarchar(max)=CONCAT(N'[',@Payment,N']'),@Lines nvarchar(max)=CONCAT(N'[{"ChargeId":',@Charge,N',"AdmissionId":542,"EnrollmentId":',@Enrollment,N',"AmountBefore":500,"Discount":0,"Cash":500,"Credit":0}]'),@Now datetime2(0)=SYSDATETIME();
            EXEC dbo.IssueCollectionReceipt @Actor,542,@Now,N'Cash',N'Owned grid regression payment',N'[542]',@Payments,@Lines,500,0,0;
            """,new SqlParameter("@Actor",actor),new SqlParameter("@Charge",Id("Unpaid")));
        using var printed=Query("""
            SELECT L.ChargeId,L.Discount,L.Cash FROM dbo.CollectionReceipts R
            CROSS APPLY OPENJSON(R.SnapshotJson,'$.Lines') WITH(ChargeId bigint,Discount decimal(12,2),Cash decimal(12,2)) L
            WHERE R.ReceiptNumber=@Receipt;
            """,new SqlParameter("@Receipt",collected.Rows[0]["ReceiptNumber"]));
        Check(printed.AsEnumerable().Any(row=>row.Field<long>("ChargeId")==newId&&row.Field<decimal>("Discount")==900m),"A pending waiver appears on the next actual payment receipt");
        Check(!printed.AsEnumerable().Any(row=>row.Field<long>("ChargeId")==Id("Waived")),"An older documented waiver is not repeated on that receipt");
        using(var data=Read())Check(!Contains(data,newId)&&!Contains(data,Id("Unpaid")),"Both paid fees and newly documented waivers leave the grid after payment");
        tx.Rollback();
    }
}
