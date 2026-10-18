using Microsoft.Data.SqlClient;
using FMSC_SMS.Services.Communication;

// End-to-end business examples against the actual release DACPAC, not just the
// historical upgrade fixture. Every amount below belongs to a fictional school.
internal sealed class SchoolScenarios(Fixture f, Report r)
{
    private (int Student, int Enrollment) Admit(string name, int? family = null)
    {
        var row = Sql.Proc("AdmitStudent", f.AdmissionArgs(name, family, current: true)).Rows[0];
        return (Convert.ToInt32(row["AdmissionId"]), Convert.ToInt32(row["EnrollmentId"]));
    }
    private long Tuition(int enrollment) => Convert.ToInt64(Sql.Scalar(
        "SELECT TOP(1) BillingChargeId FROM dbo.BillingCharges WHERE EnrollmentId=@E AND FeeTypeId=@F ORDER BY BillingChargeId;",
        Sql.P("@E", enrollment), Sql.P("@F", f.Tuition)));
    private decimal Credit(int student) => Sql.Money("SELECT COALESCE(SUM(Amount),0) FROM dbo.StudentCreditEntries WHERE AdmissionId=@S;", Sql.P("@S", student));
    private decimal Due(long charge) => Sql.Money("SELECT dbo.GetFeeChargeBalance(@C);", Sql.P("@C", charge));
    private Task<DataTable> Pay(int student, int enrollment, decimal amount, params (long, decimal)[] rows) =>
        CollectionDiscountWriter.CollectAsync("ProcessFeeTransaction", Array.Empty<CollectionDiscountWriter.Edit>(), "Fictional school scenario",
            Lifecycle.CollectionArgs(student, enrollment, DateTime.Today, amount, rows, "Fictional school scenario"));
    private void CreditAction(int student, string action, decimal amount, Guid request, int? target = null) =>
        Sql.Proc("ProcessStudentCredit", Sql.P("@UserId", f.Admin), Sql.P("@AdmissionId", student), Sql.P("@Action", action),
            Sql.P("@Amount", amount), Sql.P("@RequestId", request), Sql.P("@TargetAdmissionId", target), Sql.P("@Reason", "Fictional school scenario"));
    private void Rate(long charge, decimal amount, string action = "Credit") => Sql.Proc("AdjustBillingChargeRate",
        Sql.P("@UserId", f.Admin), Sql.P("@BillingChargeId", charge), Sql.P("@NewAmount", amount),
        Sql.P("@PaidReductionAction", action), Sql.P("@Reason", "Fictional rate correction"), Sql.P("@RequestId", Guid.NewGuid()));

    internal async Task Run()
    {
        await r.Stage("School: partial payment and remaining-balance waiver", PartialWaiver);
        await r.Stage("School: paid bill reductions, credit and repeated rate changes", PaidRateCorrection);
        await r.Stage("School: credit deposit replay, sibling transfer and cash refund", Credits);
        await r.Stage("School: pending registration confirmation preserves paid opening bill", PendingConfirmation);
        await r.Stage("School: automatic monthly posting excludes legacy and reversed fees", AutomaticBilling);
    }

    internal async Task Upgrade()
    {
        using var connection = DatabaseHelper.GetConnection(); connection.Open();
        using var stream = typeof(FMSC_SMS.Core.CommunicationInstallation).Assembly
            .GetManifestResourceStream("FMSC_SMS.Communication.Schema.sql")!;
        using var reader = new StreamReader(stream);
        string script = await reader.ReadToEndAsync();
        r.Check(await CommunicationFixture.InstallAsync(connection, script) == CommunicationInstallation.Ready,
            "The optional communication fixture installs disabled against the current base schema.");
        var child = Admit("Upgrade Preservation Example");
        long charge = Tuition(child.Enrollment);
        await Pay(child.Student, child.Enrollment, 1000, (charge, 1000));
        int students = Sql.Count("SELECT COUNT(*) FROM dbo.Students;");
        decimal received = Sql.Money("SELECT SUM(TotalAmount) FROM dbo.FeePayments;");
        // Reconstruct only the known old module version check, then exercise the
        // production migration. This owned database is never a school target.
        Sql.Query("""
            DECLARE @Definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'comm.IngestMeta'));
            SET @Definition=REPLACE(@Definition,N'FROM dbo.DatabaseVersion)<>20',N'FROM dbo.DatabaseVersion)<>6');
            SET @Definition=N'ALTER '+SUBSTRING(@Definition,CHARINDEX(N'PROCEDURE',@Definition),LEN(@Definition));
            EXEC sys.sp_executesql @Definition;
            UPDATE dbo.DatabaseVersion SET VersionNumber=N'19' WHERE VersionId=(SELECT MAX(VersionId) FROM dbo.DatabaseVersion);
            """);
        string migration = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Migrations", "20.sql"));
        Sql.Query("BEGIN TRANSACTION;\n" + migration + "\nCOMMIT TRANSACTION;");
        r.Check(await CommunicationSchemaContract.VerifyAsync(connection), "An existing callback module matches the runtime contract after migration 20.");
        r.Check(Sql.Count("SELECT COUNT(*) FROM dbo.Students;") == students &&
            Sql.Money("SELECT SUM(TotalAmount) FROM dbo.FeePayments;") == received && Due(charge) == 4000,
            "Migration preserves student count, receipts and the exact unpaid tuition balance.");
        r.Check(Sql.Count("SELECT COUNT(*) FROM sys.procedures WHERE name IN(N'GetRoles',N'GetUserRoles',N'GetRolesAndPermissions',N'GetAdjustableFeePayments');") == 0,
            "The four retired read procedures are absent after the upgrade.");
    }

    private async Task PartialWaiver()
    {
        var child = Admit("Partial Waiver Example"); long charge = Tuition(child.Enrollment);
        await Pay(child.Student, child.Enrollment, 4000, (charge, 4000));
        r.Check(Due(charge) == 1000, "A 5000 tuition bill with 4000 received has 1000 unpaid.");
        int before = Sql.Count("SELECT COUNT(*) FROM dbo.FeePayments;");
        await Pay(child.Student, child.Enrollment, 0, (charge, -1000));
        r.Check(Due(charge) == 0, "Waiving only the unpaid 1000 settles the tuition bill.");
        r.Check(Sql.Count("SELECT COUNT(*) FROM dbo.FeePayments;") == before, "A noncash waiver creates no fake cash receipt.");
        r.Check(Sql.Money("SELECT SUM(TotalAmount) FROM dbo.FeePayments WHERE AdmissionId=@S AND IsCreditApplication=0;", Sql.P("@S", child.Student)) == 4000,
            "The original 4000 received remains recorded after the waiver.");
    }

    private async Task PaidRateCorrection()
    {
        var child = Admit("Rate Correction Example"); long charge = Tuition(child.Enrollment);
        await Pay(child.Student, child.Enrollment, 5000, (charge, 5000));
        Rate(charge, 4000);
        r.Check(Due(charge) == 0 && Credit(child.Student) == 1000, "Reducing a paid 5000 bill to 4000 releases exactly 1000 as credit.");
        r.Check(Sql.Money("SELECT SUM(TotalAmount) FROM dbo.FeePayments WHERE AdmissionId=@S AND IsCreditApplication=0;", Sql.P("@S", child.Student)) == 5000,
            "Moving a paid difference to credit does not invent a cash refund.");
        Rate(charge, 6000);
        decimal due = Sql.Money("SELECT SUM(dbo.GetFeeChargeBalance(BillingChargeId)) FROM dbo.BillingCharges WHERE BillingChargeId=@C OR CorrectionOfBillingChargeId=@C;", Sql.P("@C", charge));
        // Tuition has priority over the annual admission bill.
        r.Check(Credit(child.Student) == 0 && due == 1000, "A later increase uses available credit without negative credit or duplicated liability.");
        Rate(charge, 3000);
        r.Check(Sql.Money("SELECT SUM(NetAmount) FROM dbo.BillingCharges WHERE (BillingChargeId=@C OR CorrectionOfBillingChargeId=@C) AND Status<>N'Cancelled';", Sql.P("@C", charge)) == 3000,
            "A repeated up/down correction reconciles original and difference charges to the final 3000 rate.");
        r.Check(Sql.Count("SELECT COUNT(*) FROM dbo.StudentCreditEntries GROUP BY AdmissionId HAVING SUM(Amount)<0;") == 0, "Rate correction never makes a student credit balance negative.");
    }

    private async Task Credits()
    {
        var parent = Admit("Credit Source Example"); var sibling = Admit("Credit Sibling Example", parent.Student);
        Guid request = Guid.NewGuid(); CreditAction(parent.Student, "Deposit", 3000, request); CreditAction(parent.Student, "Deposit", 3000, request);
        r.Check(Credit(parent.Student) == 3000, "Replaying an acknowledged 3000 deposit does not receive another 3000.");
        await r.Refused("Reusing a request with a changed amount is refused", () => { CreditAction(parent.Student, "Deposit", 4000, request); return Task.CompletedTask; }, 54905);
        CreditAction(parent.Student, "Transfer", 1000, Guid.NewGuid(), sibling.Student);
        r.Check(Credit(parent.Student) == 2000 && Credit(sibling.Student) == 1000, "Sibling credit transfer preserves the family's total credit.");
        CreditAction(parent.Student, "Refund", 500, Guid.NewGuid());
        r.Check(Credit(parent.Student) == 1500, "Refunding unused credit reduces the source account exactly once.");
        r.Check(Sql.Money("SELECT SUM(TotalAmount) FROM dbo.FeePayments WHERE AdmissionId IN(@A,@B) AND IsCreditApplication=0;", Sql.P("@A", parent.Student), Sql.P("@B", sibling.Student)) == 2500,
            "3000 cash received minus 500 refunded equals 2500; sibling transfer adds no cash.");
        await r.Refused("Credit cannot be refunded beyond the available amount", () => { CreditAction(parent.Student, "Refund", 2000, Guid.NewGuid()); return Task.CompletedTask; }, 54906);
    }

    private async Task PendingConfirmation()
    {
        var args = f.AdmissionArgs("Pending Registration Example", current: true);
        args.Single(p => p.ParameterName == "@QuickRegistration").Value = true;
        DataRow row = Sql.Proc("AdmitStudent", args).Rows[0];
        int student = Convert.ToInt32(row["AdmissionId"]), enrollment = Convert.ToInt32(row["EnrollmentId"]);
        long charge = Tuition(enrollment); await Pay(student, enrollment, 2000, (charge, 2000));
        int count = Sql.Count("SELECT COUNT(*) FROM dbo.BillingCharges WHERE AdmissionId=@S;", Sql.P("@S", student));
        Sql.Proc("CompleteQuickAdmission", Sql.P("@UserId", f.Admin), Sql.P("@AdmissionId", student));
        r.Check(Sql.Count("SELECT COUNT(*) FROM dbo.Students WHERE AdmissionId=@S AND Status=N'Active' AND AdmissionNumber IS NOT NULL AND RegistrationNumber IS NOT NULL;", Sql.P("@S", student)) == 1,
            "Confirming pending admission allocates official identities and activates the same student.");
        r.Check(Sql.Count("SELECT COUNT(*) FROM dbo.BillingCharges WHERE AdmissionId=@S;", Sql.P("@S", student)) == count && Due(charge) == 3000,
            "Confirmation preserves the 2000 payment and does not post opening fees again.");
    }

    private Task AutomaticBilling()
    {
        var child = Admit("Automatic Billing Example"); var legacy = Admit("Legacy Excluded Example");
        Sql.Query("UPDATE dbo.Students SET LegacySetupPending=1 WHERE AdmissionId=@S;", Sql.P("@S", legacy.Student));
        Sql.Query("IF NOT EXISTS(SELECT 1 FROM dbo.BillingAutomationState WHERE StateId=1) INSERT dbo.BillingAutomationState(StateId,StartMonth) VALUES(1,DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1));");
        // Use a DataSet for the first summary table; Sql.Proc returns the last result table.
        using var connection = DatabaseHelper.GetConnection(); connection.Open();
        string Snapshot()
        {
            using var cmd = new SqlCommand("EXEC dbo.RunAutomaticMonthlyBilling @UserId=@U;", connection);
            cmd.Parameters.Add(Sql.P("@U", f.Admin)); using var reader = cmd.ExecuteReader(); reader.Read(); return reader.GetString(reader.GetOrdinal("Snapshot"));
        }
        void Post() => Sql.Proc("RunAutomaticMonthlyBilling", Sql.P("@UserId", f.Admin), Sql.P("@Preview", false), Sql.P("@ExpectedSnapshot", Snapshot()));
        Post();
        int count = Sql.Count("SELECT COUNT(*) FROM dbo.BillingCharges;"); Post();
        r.Check(Sql.Count("SELECT COUNT(*) FROM dbo.BillingCharges;") == count, "Repeated automatic billing creates no duplicate charges.");
        r.Check(Sql.Count("SELECT COUNT(*) FROM dbo.BillingCharges WHERE AdmissionId=@S AND BillingMonth=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1);", Sql.P("@S", legacy.Student)) == (DateTime.Today.Month == 1 ? 2 : 0),
            "Legacy references receive no new automatic monthly fees.");
        Sql.Query("UPDATE dbo.BillingCharges SET Status=N'Cancelled' WHERE AdmissionId=@S AND FeeTypeId=@F AND BillingMonth=DATEFROMPARTS(YEAR(GETDATE()),MONTH(GETDATE()),1);", Sql.P("@S", child.Student), Sql.P("@F", f.Tuition));
        Post();
        r.Check(Sql.Count("SELECT COUNT(*) FROM dbo.BillingCharges;") == count, "Automatic billing does not recreate a deliberately reversed fee.");
        return Task.CompletedTask;
    }
}
