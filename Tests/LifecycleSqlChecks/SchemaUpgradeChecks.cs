using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Test-only metadata and synthetic-row evidence, never a production connection.
internal static class SchemaUpgradeChecks
{
    internal static int Export(bool snapshot)
    {
        Sql.ValidateTarget();
        using (var connection = DatabaseHelper.GetConnection()) { connection.Open(); DatabaseCompatibility.Validate(connection); }
        string path = Environment.GetEnvironmentVariable("FMSC_SCHEMA_UPGRADE_PRIVATE_OUTPUT")
            ?? throw new InvalidOperationException("Private metadata output is required.");
        string root = Path.GetFullPath(Environment.GetEnvironmentVariable("FMSC_TEST_PRIVATE_ROOT")!);
        if (!Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Schema evidence must remain private.");
        var result = new Dictionary<string, object>();
        string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        string Quote(string value) => "[" + value.Replace("]", "]]") + "]";
        if (snapshot)
        {
            var tables = new Dictionary<string, object>();
            string? originalPath = Environment.GetEnvironmentVariable("FMSC_SCHEMA_UPGRADE_SNAPSHOT_BASE");
            if (originalPath is not null && !Path.GetFullPath(originalPath).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("Original snapshot must remain private.");
            using var original = originalPath is null ? null : JsonDocument.Parse(File.ReadAllText(originalPath));
            foreach (DataRow row in Sql.Query("SELECT SCHEMA_NAME(schema_id) AS S,name AS N FROM sys.tables WHERE is_ms_shipped=0 ORDER BY S,N;").Rows)
            {
                string name = Quote((string)row["S"]) + "." + Quote((string)row["N"]);
                string columns = string.Join(",", Sql.Query("SELECT name FROM sys.columns WHERE object_id=OBJECT_ID(@N) AND is_computed=0 ORDER BY column_id;", Sql.P("@N", name))
                    .AsEnumerable().Select(c => Quote((string)c["name"])));
                if (original is not null && original.RootElement.GetProperty("tables").TryGetProperty(name, out var previous))
                    columns = previous.GetProperty("columns").GetString()!;
                // Existing columns only, so additive backfills do not hide edits to original values.
                var rows = Sql.Query("SELECT (SELECT " + columns + " FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES) AS J FROM " + name + ";")
                    .AsEnumerable().Select(r => Hash((string)r["J"])).Order().ToArray();
                tables[name] = new { columns, rows };
            }
            result["tables"] = tables;
            result["recoveryScope"] = Convert.ToString(Sql.Scalar("SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'FMSC_QuickRegistrationScope';"))!;
        }
        else
        {
            var catalog = new Dictionary<string, object>();
            foreach (var (section, query) in Queries)
            {
                // SQL Server splits a large top-level FOR JSON value across rows.
                string json = string.Concat(Sql.Query(query + " FOR JSON PATH,INCLUDE_NULL_VALUES;")
                    .AsEnumerable().Select(row => Convert.ToString(row[0])));
                using var document = JsonDocument.Parse(json);
                catalog[section] = document.RootElement.Clone();
            }
            result["contract"] = catalog;
            // Complete catalog evidence; raw definitions never leave this private directory.
            foreach (var (section, query) in new[] {
                ("modules", "SELECT SCHEMA_NAME(schema_id) AS S,name,type,REPLACE(OBJECT_DEFINITION(object_id),CHAR(13)+CHAR(10),CHAR(10)) AS D FROM sys.objects WHERE type IN ('P','V','FN','IF','TF','TR') AND is_ms_shipped=0 ORDER BY S,name"),
                ("checks", "SELECT OBJECT_SCHEMA_NAME(parent_object_id) AS S,OBJECT_NAME(parent_object_id) AS T,definition,is_disabled,is_not_trusted FROM sys.check_constraints ORDER BY S,T,definition"),
                ("defaults", "SELECT OBJECT_SCHEMA_NAME(parent_object_id) AS S,OBJECT_NAME(parent_object_id) AS T,COL_NAME(parent_object_id,parent_column_id) AS C,definition FROM sys.default_constraints ORDER BY S,T,C"),
                ("indexes", "SELECT OBJECT_SCHEMA_NAME(I.object_id) AS S,OBJECT_NAME(I.object_id) AS T,CASE WHEN K.is_system_named=1 THEN N'<system-generated>' ELSE I.name END AS name,I.type,I.is_unique,I.is_disabled,I.filter_definition,IC.key_ordinal,IC.is_descending_key,IC.is_included_column,C.name AS C FROM sys.indexes I JOIN sys.index_columns IC ON I.object_id=IC.object_id AND I.index_id=IC.index_id JOIN sys.columns C ON C.object_id=IC.object_id AND C.column_id=IC.column_id LEFT JOIN sys.key_constraints K ON K.parent_object_id=I.object_id AND K.unique_index_id=I.index_id WHERE OBJECTPROPERTY(I.object_id,'IsUserTable')=1 ORDER BY S,T,I.name,IC.index_column_id"),
                ("permissions", "SELECT P.name,P.type,P.authentication_type_desc,D.state,D.permission_name,OBJECT_SCHEMA_NAME(D.major_id) AS S,OBJECT_NAME(D.major_id) AS O,D.minor_id FROM sys.database_principals P LEFT JOIN sys.database_permissions D ON P.principal_id=D.grantee_principal_id WHERE P.principal_id>4 ORDER BY P.name,D.class,D.major_id,D.minor_id,D.permission_name"),
                ("roleMembership", "SELECT USER_NAME(role_principal_id) AS R,USER_NAME(member_principal_id) AS M FROM sys.database_role_members ORDER BY R,M") })
            {
                var rows = Sql.Query(query + ";").AsEnumerable().Select(row =>
                    row.Table.Columns.Cast<DataColumn>().ToDictionary(c => c.ColumnName, c => row[c] is DBNull ? null : Convert.ToString(row[c]))).ToArray();
                if (section == "modules")
                    foreach (var row in rows)
                        if (row["D"] is { } definition)
                            row["D"] = System.Text.RegularExpressions.Regex.Replace(definition, @"\A\s*(?:CREATE\s+OR\s+ALTER|CREATE|ALTER)\s+", "MODULE ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result[section] = Hash(JsonSerializer.Serialize(rows.Select(row => JsonSerializer.Serialize(row)).Order(StringComparer.Ordinal).ToArray()));
            }
        }
        File.WriteAllText(path, JsonSerializer.Serialize(result));
        Console.WriteLine("PASS: private guarded " + (snapshot ? "synthetic record snapshot" : "schema catalog and production compatibility") + ".");
        return 0;
    }

    private static readonly (string Section, string Query)[] Queries = {
        ("objects", "SELECT SCHEMA_NAME(schema_id) AS [schema],name,type FROM sys.objects WHERE is_ms_shipped=0 AND type IN ('U','P','V','TR','FN','IF','TF') ORDER BY [schema],name"),
        ("columns", "SELECT SCHEMA_NAME(O.schema_id) AS [schema],O.name AS objectName,C.name,TYPE_NAME(C.user_type_id) AS type,C.max_length AS maxLength,C.precision,C.scale,C.is_nullable AS isNullable,C.is_identity AS isIdentity,C.is_computed AS isComputed FROM sys.columns C JOIN sys.tables O ON C.object_id=O.object_id WHERE O.is_ms_shipped=0 ORDER BY [schema],O.name,C.column_id"),
        ("parameters", "SELECT SCHEMA_NAME(O.schema_id) AS [schema],O.name AS objectName,P.name,TYPE_NAME(P.user_type_id) AS type,P.max_length AS maxLength,P.precision,P.scale,P.is_output AS isOutput FROM sys.parameters P JOIN sys.objects O ON P.object_id=O.object_id WHERE O.is_ms_shipped=0 AND O.type IN ('P','FN','IF','TF') ORDER BY [schema],O.name,P.parameter_id"),
        ("tableTypes", "SELECT SCHEMA_NAME(T.schema_id) AS [schema],T.name AS objectName,C.name,TYPE_NAME(C.user_type_id) AS type,C.max_length AS maxLength,C.precision,C.scale,C.is_nullable AS isNullable,C.column_id AS ordinal FROM sys.table_types T JOIN sys.columns C ON T.type_table_object_id=C.object_id ORDER BY [schema],T.name,C.column_id"),
        ("uniqueIndexes", "SELECT OBJECT_SCHEMA_NAME(I.object_id) AS [schema],OBJECT_NAME(I.object_id) AS objectName,I.name,I.filter_definition AS [filter],(SELECT STRING_AGG(CONVERT(nvarchar(max),C.name),',') WITHIN GROUP(ORDER BY IC.key_ordinal) FROM sys.index_columns IC JOIN sys.columns C ON C.object_id=IC.object_id AND C.column_id=IC.column_id WHERE IC.object_id=I.object_id AND IC.index_id=I.index_id AND IC.key_ordinal>0) AS keys FROM sys.indexes I WHERE I.is_unique=1 AND OBJECTPROPERTY(I.object_id,'IsUserTable')=1 ORDER BY [schema],objectName,I.name"),
        ("triggers", "SELECT OBJECT_SCHEMA_NAME(T.object_id) AS [schema],T.name,OBJECT_SCHEMA_NAME(T.parent_id) AS parentSchema,OBJECT_NAME(T.parent_id) AS parentName,(SELECT STRING_AGG(CONVERT(nvarchar(max),E.type_desc),',') WITHIN GROUP(ORDER BY E.type_desc) FROM sys.trigger_events E WHERE E.object_id=T.object_id) AS events,T.is_instead_of_trigger AS insteadOf FROM sys.triggers T WHERE T.is_ms_shipped=0 ORDER BY [schema],T.name"),
        ("foreignKeys", "SELECT OBJECT_SCHEMA_NAME(F.parent_object_id) AS [schema],OBJECT_NAME(F.parent_object_id) AS objectName,OBJECT_SCHEMA_NAME(F.referenced_object_id) AS referencedSchema,OBJECT_NAME(F.referenced_object_id) AS referencedObject,(SELECT STRING_AGG(CONVERT(nvarchar(max),C.name),',') WITHIN GROUP(ORDER BY K.constraint_column_id) FROM sys.foreign_key_columns K JOIN sys.columns C ON C.object_id=K.parent_object_id AND C.column_id=K.parent_column_id WHERE K.constraint_object_id=F.object_id) AS columns,(SELECT STRING_AGG(CONVERT(nvarchar(max),C.name),',') WITHIN GROUP(ORDER BY K.constraint_column_id) FROM sys.foreign_key_columns K JOIN sys.columns C ON C.object_id=K.referenced_object_id AND C.column_id=K.referenced_column_id WHERE K.constraint_object_id=F.object_id) AS referencedColumns,F.delete_referential_action AS deleteAction,F.update_referential_action AS updateAction FROM sys.foreign_keys F WHERE F.is_disabled=0 AND F.is_not_trusted=0 ORDER BY [schema],objectName,F.name"),
        ("checkConstraints", "SELECT OBJECT_SCHEMA_NAME(parent_object_id) AS [schema],OBJECT_NAME(parent_object_id) AS objectName,name,CASE WHEN is_system_named=1 THEN CAST(1 AS bit) END AS isSystemNamed,CASE WHEN is_system_named=1 THEN definition END AS definition FROM sys.check_constraints WHERE is_disabled=0 AND is_not_trusted=0 ORDER BY [schema],objectName,name"),
        ("databaseProperties", "SELECT name,CONVERT(nvarchar(128),SQL_VARIANT_PROPERTY(value,'BaseType')) AS type FROM sys.extended_properties WHERE class=0 AND name<>N'FMSC_Level1Owner' ORDER BY name")
    };
}
