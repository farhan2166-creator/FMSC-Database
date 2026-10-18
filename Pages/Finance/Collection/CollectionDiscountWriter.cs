using Microsoft.Data.SqlClient;
namespace FMSC_SMS.Pages.Finance.Collection;

internal static class CollectionDiscountWriter
{
    internal sealed record Edit(long ChargeId, int AdmissionId, int EnrollmentId,
        decimal OriginalDiscount, decimal Discount, decimal OriginalBalance);

    internal sealed record CreditEdit(long ChargeId, int AdmissionId, int EnrollmentId,
        decimal Amount, decimal ExpectedBalance, decimal ExpectedCredit);

    public static Task<DataTable> CollectAsync(string procedure, IReadOnlyList<Edit> edits, string recoverySummary,
        params SqlParameter[] parameters) => CollectWithCreditAsync(procedure, edits, Array.Empty<CreditEdit>(), DateTime.Today, recoverySummary, parameters);

    public static Task<DataTable> CollectWithCreditAsync(string? procedure, IReadOnlyList<Edit> edits,
        IReadOnlyList<CreditEdit> credits, DateTime paymentDate, string recoverySummary,
        params SqlParameter[] parameters) => Task.Run(() => {
        using var connection = DatabaseHelper.GetConnection();
        DatabaseHelper.OpenConnection(connection);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        using var recovery = CollectionRecoveryJournal.Begin(recoverySummary);
        bool commitAttempted = false;
        try
        {
            using (var billingLock = new SqlCommand("DECLARE @L int; EXEC @L=sys.sp_getapplock @Resource=N'FMSC.MonthlyBilling',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000; IF @L<0 THROW 54901,'Billing is busy. Try again.',1;", connection, transaction))
                billingLock.ExecuteNonQuery();
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
                    """, connection, transaction);
                update.Parameters.AddRange(new[] {
                    DatabaseHelper.Int("@UserId",CurrentUser.UserId), DatabaseHelper.P("@Id",edit.ChargeId),
                    DatabaseHelper.Int("@AdmissionId",edit.AdmissionId), DatabaseHelper.Int("@EnrollmentId",edit.EnrollmentId),
                    DatabaseHelper.P("@Expected",edit.OriginalDiscount), DatabaseHelper.P("@Discount",edit.Discount),
                    DatabaseHelper.P("@ExpectedBalance",edit.OriginalBalance)
                });
                update.ExecuteNonQuery();
            }
            if (credits.Count > 0)
            {
                using var apply = new SqlCommand("dbo.ApplyCollectionCredit", connection, transaction) {
                    CommandType = CommandType.StoredProcedure, CommandTimeout = 120
                };
                apply.Parameters.AddRange(new[] {
                    DatabaseHelper.Int("@UserId", CurrentUser.UserId),
                    DatabaseHelper.Date("@TransactionDate", paymentDate),
                    DatabaseHelper.P("@CreditsJson", System.Text.Json.JsonSerializer.Serialize(credits))
                });
                apply.ExecuteNonQuery();
            }
            var result = new DataTable();
            if (procedure is not null)
            {
                using var collect = new SqlCommand(procedure, connection, transaction) {
                    CommandType = CommandType.StoredProcedure, CommandTimeout = 120
                };
                collect.Parameters.AddRange(parameters);
                collect.Parameters.Add(DatabaseHelper.Int("@UserId",CurrentUser.UserId));
                using var reader = collect.ExecuteReader();
                result.Load(reader);
            }
            commitAttempted = true;
            transaction.Commit();
            // Keep confirmed receipts usable even if local recovery-file cleanup fails.
            // A retained intent will conservatively require review before the next write.
            try { recovery.Complete(); }
            catch (Exception ex) { Core.AppExceptionHandler.LogNonFatal(ex, "Clearing confirmed collection recovery record"); }
            return result;
        }
        catch (Exception ex)
        {
            if (!commitAttempted)
            {
                try
                {
                    transaction.Rollback();
                    recovery.Complete();
                }
                catch (Exception rollbackEx)
                {
                    Core.AppExceptionHandler.LogNonFatal(rollbackEx, "Collection rollback could not be confirmed");
                    throw new CollectionOutcomeUnknownException(ex);
                }
                throw;
            }
            // A failed commit acknowledgment does not prove rollback. Retain the
            // flushed intent across page closure and app restart; never retry here.
            throw new CollectionOutcomeUnknownException(ex);
        }
    });
}
