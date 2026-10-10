using Microsoft.Data.SqlClient;

namespace FMSC_SMS.Core;

internal sealed class DatabaseCompatibilityException : InvalidOperationException
{
    internal DatabaseCompatibilityException(string message) : base(message) { }
}

/// <summary>Shared startup/restore contract for the current application schema.</summary>
internal static class DatabaseCompatibility
{
    private static readonly string Requirements = LoadRequirements();

    private static string LoadRequirements()
    {
        using var stream = typeof(DatabaseCompatibility).Assembly.GetManifestResourceStream(
            "FMSC_SMS.Core.DatabaseCompatibilityRequirements.json")
            ?? throw new DatabaseCompatibilityException("The application's database compatibility definition is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static void Validate(SqlConnection connection)
    {
        using (var capability = new SqlCommand("SELECT compatibility_level FROM sys.databases WHERE database_id=DB_ID();", connection))
            if (Convert.ToInt32(capability.ExecuteScalar()) < 130)
                throw new DatabaseCompatibilityException("This database is incompatible: SQL compatibility level 130 or later is required.");
        using var versionCommand = new SqlCommand("""
            SELECT TOP(1) CONVERT(nvarchar(50),VersionNumber) FROM dbo.DatabaseVersion ORDER BY VersionId DESC;
            """, connection) { CommandTimeout = 30 };
        string? version = Convert.ToString(versionCommand.ExecuteScalar());
        using var command = new SqlCommand("""
            SET NOCOUNT ON;
            DECLARE @Problems TABLE (Problem nvarchar(400));
            INSERT @Problems
            SELECT CONCAT('Missing ', R.[type], ': ', R.[schema], '.', R.name)
            FROM OPENJSON(@Requirements,'$.objects') WITH
                ([schema] nvarchar(128),name nvarchar(128),[type] varchar(2)) R
            WHERE OBJECT_ID(QUOTENAME(R.[schema])+'.'+QUOTENAME(R.name),R.[type]) IS NULL;
            INSERT @Problems
            SELECT CONCAT('Missing/incompatible column: ',R.[schema],'.',R.objectName,'.',R.name)
            FROM OPENJSON(@Requirements,'$.columns') WITH
                ([schema] nvarchar(128),objectName nvarchar(128),name nvarchar(128),
                 [type] nvarchar(128),maxLength int,[precision] int,scale int,isNullable bit,isIdentity bit,isComputed bit) R
            LEFT JOIN sys.columns C ON C.object_id=OBJECT_ID(QUOTENAME(R.[schema])+'.'+QUOTENAME(R.objectName)) AND C.name=R.name
            WHERE C.column_id IS NULL OR TYPE_NAME(C.user_type_id)<>R.[type]
                OR C.max_length<>R.maxLength OR C.precision<>R.[precision] OR C.scale<>R.scale
                OR C.is_nullable<>R.isNullable OR C.is_identity<>R.isIdentity OR C.is_computed<>R.isComputed;
            INSERT @Problems
            SELECT CONCAT('Missing/incompatible parameter: ',R.[schema],'.',R.objectName,' ',R.name)
            FROM OPENJSON(@Requirements,'$.parameters') WITH
                ([schema] nvarchar(128),objectName nvarchar(128),name nvarchar(128),
                 [type] nvarchar(128),maxLength int,[precision] int,scale int,isOutput bit) R
            LEFT JOIN sys.parameters P ON P.object_id=OBJECT_ID(QUOTENAME(R.[schema])+'.'+QUOTENAME(R.objectName)) AND P.name=R.name
            WHERE P.parameter_id IS NULL OR TYPE_NAME(P.user_type_id)<>R.[type]
                OR P.max_length<>R.maxLength OR P.precision<>R.[precision] OR P.scale<>R.scale OR P.is_output<>R.isOutput;
            INSERT @Problems
            SELECT CONCAT('Missing/incompatible table-valued parameter column: ',R.[schema],'.',R.objectName,'.',R.name)
            FROM OPENJSON(@Requirements,'$.tableTypes') WITH
                ([schema] nvarchar(128),objectName nvarchar(128),name nvarchar(128),
                 [type] nvarchar(128),maxLength int,[precision] int,scale int,isNullable bit,ordinal int) R
            LEFT JOIN sys.table_types T ON T.schema_id=SCHEMA_ID(R.[schema]) AND T.name=R.objectName
            LEFT JOIN sys.columns C ON C.object_id=T.type_table_object_id AND C.name=R.name
            WHERE C.column_id IS NULL OR TYPE_NAME(C.user_type_id)<>R.[type]
                OR C.max_length<>R.maxLength OR C.precision<>R.[precision] OR C.scale<>R.scale OR C.is_nullable<>R.isNullable OR C.column_id<>R.ordinal;
            INSERT @Problems
            SELECT CONCAT('Missing/incompatible uniqueness safeguard: ',R.[schema],'.',R.objectName,' ',R.name)
            FROM OPENJSON(@Requirements,'$.uniqueIndexes') WITH
                ([schema] nvarchar(128),objectName nvarchar(128),name nvarchar(128),[filter] nvarchar(max),keys nvarchar(max)) R
            WHERE NOT EXISTS
                (SELECT 1 FROM sys.indexes I
                 WHERE I.object_id=OBJECT_ID(QUOTENAME(R.[schema])+'.'+QUOTENAME(R.objectName))
                   AND I.is_unique=1 AND I.is_disabled=0 AND I.is_hypothetical=0
                   AND ISNULL(I.filter_definition,'')=ISNULL(R.[filter],'')
                   AND (SELECT STRING_AGG(CONVERT(nvarchar(max),C.name),',') WITHIN GROUP(ORDER BY IC.key_ordinal)
                        FROM sys.index_columns IC JOIN sys.columns C ON C.object_id=IC.object_id AND C.column_id=IC.column_id
                        WHERE IC.object_id=I.object_id AND IC.index_id=I.index_id AND IC.key_ordinal>0)=R.keys);
            IF NOT EXISTS
                (SELECT 1 FROM sys.indexes I JOIN sys.index_columns IC ON IC.object_id=I.object_id AND IC.index_id=I.index_id
                 JOIN sys.columns C ON C.object_id=IC.object_id AND C.column_id=IC.column_id
                 WHERE I.object_id=OBJECT_ID('dbo.Students') AND I.is_unique=1 AND I.is_disabled=0
                   AND I.has_filter=0 AND IC.key_ordinal=1 AND C.name='AdmissionNumber'
                   AND NOT EXISTS(SELECT 1 FROM sys.index_columns K WHERE K.object_id=I.object_id AND K.index_id=I.index_id AND K.key_ordinal>1))
                INSERT @Problems VALUES(N'Missing unique admission-number constraint/index.');
            SELECT COUNT(*) AS ProblemCount FROM @Problems;
            SELECT TOP(12) Problem FROM @Problems ORDER BY Problem;
            """, connection) { CommandTimeout = 30 };
        command.Parameters.Add("@Requirements", SqlDbType.NVarChar, -1).Value = Requirements;
        using (var reader = command.ExecuteReader())
        {
            reader.Read();
            int count = reader.GetInt32(0);
            reader.NextResult();
            var problems = new List<string>();
            while (reader.Read()) problems.Add(reader.GetString(0));
            if (count > 0)
                throw new DatabaseCompatibilityException(
                    "This database is incompatible with this application. Required schema features are missing or incompatible.\n" +
                    $"Restored schema version {version ?? "unknown"}; this application requires {DatabaseHelper.RequiredDatabaseSchemaVersion}.\n\n" +
                    string.Join("\n", problems) + (count > problems.Count ? $"\n({count - problems.Count} more)" : "") +
                    "\n\nUse a backup from the current release or contact support for the required database updates.");
        }

        if (!string.Equals(version, DatabaseHelper.RequiredDatabaseSchemaVersion, StringComparison.OrdinalIgnoreCase))
            throw new DatabaseCompatibilityException($"This database is incompatible: schema version {(string.IsNullOrWhiteSpace(version) ? "unknown" : version)}; this application requires {DatabaseHelper.RequiredDatabaseSchemaVersion}.");
    }
}
