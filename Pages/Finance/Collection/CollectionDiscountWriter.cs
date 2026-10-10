using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.Collection;

internal static class CollectionDiscountWriter
{
    internal sealed record Edit(long ChargeId, int AdmissionId, int EnrollmentId,
        decimal OriginalDiscount, decimal Discount, decimal OriginalBalance);

    public static Task<DataTable> CollectAsync(string procedure, IReadOnlyList<Edit> edits,
        params SqlParameter[] parameters) => Task.Run(() => {
        using var connection = DatabaseHelper.GetConnection();
        DatabaseHelper.OpenConnection(connection);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        foreach (var edit in edits)
        {
            using var update = new SqlCommand("""
                EXEC dbo.RequirePermission @UserId,N'Fees.Adjust';
                DECLARE @Gross decimal(12,2), @Old decimal(12,2), @Paid decimal(12,2);
                SELECT @Gross=GrossAmount,@Old=DiscountAmount,
                    @Paid=NetAmount-dbo.GetFeeChargeBalance(BillingChargeId)
                FROM dbo.BillingCharges WITH (UPDLOCK,HOLDLOCK)
                WHERE BillingChargeId=@Id AND AdmissionId=@AdmissionId AND EnrollmentId=@EnrollmentId
                    AND Status NOT IN(N'Cancelled',N'Paid');
                IF @Gross IS NULL OR @Old<>@Expected OR @Gross-@Old-@Paid<>@ExpectedBalance
                    THROW 53104,'This bill has changed. Reload the student before collecting.',1;
                IF @Discount<0 OR @Discount>@Gross-@Paid
                    THROW 53103,'Discount cannot exceed the unpaid fee.',1;
                -- Increases are negative allocation lines, handled by the collection procedure.
                IF @Discount>@Old RETURN;
                UPDATE dbo.BillingCharges SET DiscountAmount=@Discount
                    WHERE BillingChargeId=@Id;
                DECLARE @OperationId uniqueidentifier=NEWID(),
                    @RecordId nvarchar(200)=CONVERT(nvarchar(200),@Id);
                INSERT dbo.AuditOperations(OperationId,UserId,OperationType,CorrelationNote)
                    VALUES(@OperationId,@UserId,N'FEE_DISCOUNT_CHANGE',@RecordId);
                DECLARE @Before nvarchar(max)=(SELECT @Old DiscountAmount FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
                    @After nvarchar(max)=(SELECT @Discount DiscountAmount,@Id BillingChargeId
                        FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
                EXEC dbo.WriteAuditLog @OperationId,@UserId,N'BillingCharges',@RecordId,N'UPDATE',@Before,@After;
                """, connection, transaction);
            update.Parameters.AddRange(new[] {
                DatabaseHelper.Int("@UserId",CurrentUser.UserId), DatabaseHelper.P("@Id",edit.ChargeId),
                DatabaseHelper.Int("@AdmissionId",edit.AdmissionId), DatabaseHelper.Int("@EnrollmentId",edit.EnrollmentId),
                DatabaseHelper.P("@Expected",edit.OriginalDiscount), DatabaseHelper.P("@Discount",edit.Discount),
                DatabaseHelper.P("@ExpectedBalance",edit.OriginalBalance)
            });
            update.ExecuteNonQuery();
        }
        using var collect = new SqlCommand(procedure, connection, transaction) {
            CommandType=CommandType.StoredProcedure, CommandTimeout=120
        };
        collect.Parameters.AddRange(parameters);
        collect.Parameters.Add(DatabaseHelper.Int("@UserId",CurrentUser.UserId));
        using var reader = collect.ExecuteReader();
        var result = new DataTable();
        result.Load(reader);
        reader.Close();
        transaction.Commit();
        return result;
    });
}
