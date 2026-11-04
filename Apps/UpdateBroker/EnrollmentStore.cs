using System.Data;
using Microsoft.Data.SqlClient;

namespace FMSC_SMS.UpdateBroker;

internal sealed record EnrollmentResult(Guid SchoolId, Guid DeviceId, string DeviceCredential);
internal sealed record SchoolDevice(Guid DeviceId, string DisplayName, bool Revoked,
    DateTime CreatedUtc, DateTime? LastSeenUtc);
internal enum RevokeResult { Unauthorized, NotFound, Revoked }

/// <summary>
/// Entirely separate broker SQL database; never connects to the school's FMSC_DB.
/// The per-school administrator may enroll or revoke that school's computers only.
/// Credentials are returned once and never persisted or logged in plaintext.
/// </summary>
internal sealed class EnrollmentStore
{
    private readonly BrokerConfig _settings;
    internal EnrollmentStore(BrokerConfig settings) => _settings = settings;

    private SqlConnection OpenConnection() => new(_settings.ConnectionString);

    internal async Task VerifySchemaAsync(CancellationToken token = default)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand(
            @"SELECT CASE WHEN OBJECT_ID(N'dbo.UpdateSchools',N'U') IS NOT NULL
              AND OBJECT_ID(N'dbo.UpdateDevices',N'U') IS NOT NULL THEN 1 ELSE 0 END;",
            connection);
        if ((int)(await command.ExecuteScalarAsync(token) ?? 0) != 1)
            throw new InvalidOperationException(
                "The separate broker database schema is missing. Apply Apps/UpdateBroker/Schema.sql first.");
    }



    internal async Task<(Guid SchoolId, string AdminCredential)> ProvisionSchoolAsync(
        string displayName, CancellationToken cancellationToken = default)
    {
        string name = ValidateName(displayName, 160);
        Guid id = Guid.NewGuid();
        string credential = BrokerTokens.New(BrokerTokens.SchoolAdminPrefix, id);
        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "INSERT INTO dbo.UpdateSchools (SchoolId,DisplayName,AdminKeyHash) VALUES (@id,@name,@hash);",
            connection) { CommandTimeout = 15 };
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@name", SqlDbType.NVarChar, 160).Value = name;
        command.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = Hash(credential);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return (id, credential);
    }

    internal async Task<string?> RotateAdminAsync(Guid schoolId, CancellationToken token = default)
    {
        string newCredential = BrokerTokens.New(BrokerTokens.SchoolAdminPrefix, schoolId);
        await using var connection = OpenConnection();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand(
            @"UPDATE dbo.UpdateSchools SET AdminKeyHash = @hash, UpdatedUtc = SYSUTCDATETIME()
              WHERE SchoolId = @id AND IsActive = 1;", connection);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = schoolId;
        command.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = Hash(newCredential);
        return await command.ExecuteNonQueryAsync(token) == 1 ? newCredential : null;
    }

    internal async Task<bool> DisableSchoolAsync(Guid schoolId, CancellationToken token = default)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand(
            @"UPDATE dbo.UpdateSchools SET IsActive = 0, UpdatedUtc = SYSUTCDATETIME()
              WHERE SchoolId = @id AND IsActive = 1;", connection);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = schoolId;
        return await command.ExecuteNonQueryAsync(token) == 1;
    }

    internal async Task<EnrollmentResult?> EnrollAsync(string? adminCredential,
        string deviceDisplayName, CancellationToken token = default)
    {
        if (!BrokerTokens.TryParse(adminCredential, BrokerTokens.SchoolAdminPrefix, out var schoolId))
            return null;
        string name = ValidateName(deviceDisplayName, 100);
        await using var connection = OpenConnection();
        await connection.OpenAsync(token);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        if (!await AuthenticateAdminInTransactionAsync(connection, tx, schoolId, adminCredential!, token))
            return null;
        await using (var count = new SqlCommand(
            @"SELECT COUNT_BIG(*) FROM dbo.UpdateDevices
              WHERE SchoolId = @school AND RevokedUtc IS NULL;", connection, tx))
        {
            count.Parameters.Add("@school", SqlDbType.UniqueIdentifier).Value = schoolId;
            if ((long)(await count.ExecuteScalarAsync(token) ?? 0L) >= _settings.MaximumDevicesPerSchool)
                throw new DeviceEnrollmentLimitException();
        }
        Guid deviceId = Guid.NewGuid();
        string deviceCredential = BrokerTokens.New(BrokerTokens.DevicePrefix, deviceId);
        await using (var command = new SqlCommand(
            @"INSERT INTO dbo.UpdateDevices (DeviceId,SchoolId,DisplayName,DeviceKeyHash)
              VALUES (@device,@school,@name,@hash);", connection, tx))
        {
            command.Parameters.Add("@device", SqlDbType.UniqueIdentifier).Value = deviceId;
            command.Parameters.Add("@school", SqlDbType.UniqueIdentifier).Value = schoolId;
            command.Parameters.Add("@name", SqlDbType.NVarChar, 100).Value = name;
            command.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = Hash(deviceCredential);
            await command.ExecuteNonQueryAsync(token);
        }
        await tx.CommitAsync(token);
        return new EnrollmentResult(schoolId, deviceId, deviceCredential);
    }

    internal async Task<bool> AuthenticateDeviceAsync(string? deviceCredential,
        CancellationToken token = default)
    {
        if (!BrokerTokens.TryParse(deviceCredential, BrokerTokens.DevicePrefix, out var deviceId))
            return false;
        await using var connection = OpenConnection();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand(
            @"SELECT d.DeviceKeyHash FROM dbo.UpdateDevices AS d
              INNER JOIN dbo.UpdateSchools AS s ON s.SchoolId = d.SchoolId
              WHERE d.DeviceId = @device AND d.RevokedUtc IS NULL AND s.IsActive = 1;",
            connection) { CommandTimeout = 15 };
        command.Parameters.Add("@device", SqlDbType.UniqueIdentifier).Value = deviceId;
        byte[]? digest = (byte[]?)await command.ExecuteScalarAsync(token);
        if (digest is null || !BrokerTokens.Matches(deviceCredential!, _settings.KeyPepper, digest))
            return false;
        // Best-effort activity time; do not prevent a valid update if telemetry is unavailable.
        try
        {
            await using var activity = new SqlCommand(
                @"UPDATE dbo.UpdateDevices SET LastSeenUtc = SYSUTCDATETIME()
                  WHERE DeviceId = @device AND RevokedUtc IS NULL
                  AND (LastSeenUtc IS NULL OR LastSeenUtc < DATEADD(hour,-1,SYSUTCDATETIME()));",
                connection) { CommandTimeout = 5 };
            activity.Parameters.Add("@device", SqlDbType.UniqueIdentifier).Value = deviceId;
            await activity.ExecuteNonQueryAsync(token);
        }
        catch (SqlException) { /* Authentication already succeeded; activity is optional. */ }
        return true;
    }

    internal async Task<IReadOnlyList<SchoolDevice>?> DevicesAsync(string? adminCredential,
        CancellationToken token = default)
    {
        if (!BrokerTokens.TryParse(adminCredential, BrokerTokens.SchoolAdminPrefix, out var schoolId))
            return null;
        await using var connection = OpenConnection();
        await connection.OpenAsync(token);
        if (!await AuthenticateAdminAsync(connection, schoolId, adminCredential!, token))
            return null;
        await using var command = new SqlCommand(
            @"SELECT DeviceId,DisplayName,CreatedUtc,LastSeenUtc,RevokedUtc
              FROM dbo.UpdateDevices WHERE SchoolId = @school ORDER BY CreatedUtc DESC;",
            connection);
        command.Parameters.Add("@school", SqlDbType.UniqueIdentifier).Value = schoolId;
        var result = new List<SchoolDevice>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new SchoolDevice(reader.GetGuid(0), reader.GetString(1), !reader.IsDBNull(4),
                DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc),
                reader.IsDBNull(3) ? null : DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc)));
        return result;
    }

    internal async Task<RevokeResult> RevokeDeviceAsync(string? adminCredential, Guid deviceId,
        CancellationToken token = default)
    {
        if (!BrokerTokens.TryParse(adminCredential, BrokerTokens.SchoolAdminPrefix, out var schoolId))
            return RevokeResult.Unauthorized;
        await using var connection = OpenConnection();
        await connection.OpenAsync(token);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        if (!await AuthenticateAdminInTransactionAsync(connection, tx, schoolId, adminCredential!, token))
            return RevokeResult.Unauthorized;
        await using var command = new SqlCommand(
            @"UPDATE dbo.UpdateDevices SET RevokedUtc = SYSUTCDATETIME()
              WHERE DeviceId = @device AND SchoolId = @school AND RevokedUtc IS NULL;",
            connection, tx);
        command.Parameters.Add("@device", SqlDbType.UniqueIdentifier).Value = deviceId;
        command.Parameters.Add("@school", SqlDbType.UniqueIdentifier).Value = schoolId;
        var updated = await command.ExecuteNonQueryAsync(token);
        await tx.CommitAsync(token);
        return updated == 1 ? RevokeResult.Revoked : RevokeResult.NotFound;
    }

    private async Task<bool> AuthenticateAdminAsync(SqlConnection connection, Guid schoolId,
        string credential, CancellationToken token)
    {
        await using var cmd = new SqlCommand(
            "SELECT AdminKeyHash FROM dbo.UpdateSchools WHERE SchoolId=@school AND IsActive=1;",
            connection);
        cmd.Parameters.Add("@school", SqlDbType.UniqueIdentifier).Value = schoolId;
        byte[]? stored = (byte[]?)await cmd.ExecuteScalarAsync(token);
        return stored is not null && BrokerTokens.Matches(credential, _settings.KeyPepper, stored);
    }

    private async Task<bool> AuthenticateAdminInTransactionAsync(SqlConnection connection,
        SqlTransaction tx, Guid schoolId, string credential, CancellationToken token)
    {
        await using var cmd = new SqlCommand(
            @"SELECT AdminKeyHash FROM dbo.UpdateSchools WITH (UPDLOCK, HOLDLOCK)
              WHERE SchoolId = @school AND IsActive = 1;", connection, tx);
        cmd.Parameters.Add("@school", SqlDbType.UniqueIdentifier).Value = schoolId;
        byte[]? stored = (byte[]?)await cmd.ExecuteScalarAsync(token);
        return stored is not null && BrokerTokens.Matches(credential, _settings.KeyPepper, stored);
    }

    private byte[] Hash(string credential) => BrokerTokens.Hash(credential, _settings.KeyPepper);

    internal static string ValidateName(string? value, int max)
    {
        string name = value?.Trim() ?? "";
        if (name.Length == 0 || name.Length > max || name.Any(char.IsControl))
            throw new ArgumentException("Provide a non-empty name within the allowed length.");
        return name;
    }
}

internal sealed class DeviceEnrollmentLimitException : Exception
{
    internal DeviceEnrollmentLimitException() : base("School device enrollment limit reached.") { }
}
