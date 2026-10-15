using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.SqlServer.Dac.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

internal static class AuditExtensions
{
    private static readonly HashSet<string> Procedures = new(StringComparer.Ordinal)
    {
        "Query", "QueryUser", "QueryUserSet", "Execute", "ExecuteUser", "Scalar", "ScalarUser",
        "QueryUserAsync", "QueryUserSetAsync", "ExecuteUserAsync", "ScalarUserAsync"
    };
    private static readonly HashSet<string> TextMethods = new(StringComparer.Ordinal)
    {
        "QuerySql", "QuerySqlAsync"
    };

    internal static string Normalize(string value)
    {
        var names = value.Trim().Replace("[", "").Replace("]", "")
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return names.Length == 1 ? "dbo." + names[0] : string.Join(".", names);
    }

    internal static (string? Name, string? SqlType, string? TypeName) ParseParameter(
        ExpressionSyntax expression, SemanticModel? model)
    {
        expression = Unwrap(expression);
        if (expression is InvocationExpressionSyntax call &&
            call.Expression is MemberAccessExpressionSyntax member &&
            member.Expression.ToString().EndsWith("DatabaseHelper", StringComparison.Ordinal))
        {
            var helper = member.Name.Identifier.ValueText;
            var first = call.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            var name = first is null ? null : Constant(first, model);
            var type = helper switch
            {
                "Int" or "NullableInt" => "Int",
                "BigInt" => "BigInt",
                "Bit" => "Bit",
                "Date" => "Date",
                _ => null
            };
            return (name, type, null);
        }
        if (expression is ObjectCreationExpressionSyntax creation &&
            creation.Type.ToString().EndsWith("SqlParameter", StringComparison.Ordinal))
            return ParseNewParameter(creation.ArgumentList?.Arguments, creation.Initializer, model);
        if (expression is ImplicitObjectCreationExpressionSyntax implicitCreation)
            return ParseNewParameter(implicitCreation.ArgumentList?.Arguments, implicitCreation.Initializer, model);
        return (null, null, null);
    }

    private static (string? Name, string? SqlType, string? TypeName) ParseNewParameter(
        SeparatedSyntaxList<ArgumentSyntax>? args, InitializerExpressionSyntax? initializer, SemanticModel? model)
    {
        var first = args?.FirstOrDefault()?.Expression;
        var name = first is null ? null : Constant(first, model);
        string? type = null;
        string? typeName = null;
        if (args is { Count: > 1 })
            type = EnumMember(args.Value[1].Expression, "SqlDbType");
        if (initializer is not null)
        {
            foreach (var expr in initializer.Expressions.OfType<AssignmentExpressionSyntax>())
            {
                var key = expr.Left.ToString();
                if (key == "ParameterName") name = Constant(expr.Right, model);
                if (key == "SqlDbType") type = EnumMember(expr.Right, "SqlDbType");
                if (key == "TypeName") typeName = Constant(expr.Right, model);
            }
        }
        return (name, type, typeName);
    }

    private static string? EnumMember(ExpressionSyntax expr, string prefix)
    {
        var text = expr.ToString();
        var index = text.LastIndexOf(prefix + ".", StringComparison.Ordinal);
        return index < 0 ? null : text[(index + prefix.Length + 1)..].Trim();
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expr)
    {
        while (expr is ParenthesizedExpressionSyntax p) expr = p.Expression;
        return expr;
    }

    internal static string? Constant(ExpressionSyntax expression, SemanticModel? model, int depth = 0)
    {
        if (depth > 8) return null;
        expression = Unwrap(expression);
        if (model is not null)
        {
            var constant = model.GetConstantValue(expression);
            if (constant.HasValue && constant.Value is string value) return value;
        }
        if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            return literal.Token.ValueText;
        if (expression is InterpolatedStringExpressionSyntax interpolated &&
            interpolated.Contents.All(c => c is InterpolatedStringTextSyntax))
            return string.Concat(interpolated.Contents.OfType<InterpolatedStringTextSyntax>()
                .Select(t => t.TextToken.ValueText));
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
        {
            var a = Constant(binary.Left, model, depth + 1);
            var b = Constant(binary.Right, model, depth + 1);
            return a is null || b is null ? null : a + b;
        }
        if (expression is ConditionalExpressionSyntax conditional)
        {
            var a = Constant(conditional.WhenTrue, model, depth + 1);
            var b = Constant(conditional.WhenFalse, model, depth + 1);
            return a == b ? a : null;
        }
        if (expression is IdentifierNameSyntax id && model is not null &&
            model.GetSymbolInfo(id).Symbol is ILocalSymbol local)
        {
            var declaration = local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as VariableDeclaratorSyntax;
            if (declaration?.Initializer is null || declaration.SpanStart >= expression.SpanStart) return null;
            var block = declaration.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
            var useBlock = expression.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
            if (block is null || useBlock is null || !useBlock.AncestorsAndSelf().Contains(block)) return null;
            var changes = block.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                .Any(a => a.SpanStart > declaration.SpanStart &&
                    a.Left is IdentifierNameSyntax name &&
                    SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name).Symbol, local));
            var increments = block.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>()
                .Any(x => x.Operand is IdentifierNameSyntax name &&
                    SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name).Symbol, local));
            if (!changes && !increments)
                return Constant(declaration.Initializer.Value, model, depth + 1);
        }
        return null;
    }

    internal sealed record ParameterSpec(string Name, string Type, string? TableType, bool Optional, bool Output);
    internal sealed record ProcedureSpec(string Name, IReadOnlyDictionary<string, ParameterSpec> Parameters, bool Known);
    internal sealed record TableSpec(string Name, HashSet<string> Columns);

    internal sealed class Schema
    {
        internal readonly Dictionary<string, ProcedureSpec> Procedures = new(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, TableSpec> Tables = new(StringComparer.OrdinalIgnoreCase);
        internal readonly HashSet<string> TableTypes = new(StringComparer.OrdinalIgnoreCase);

        internal Schema(TSqlModel model)
        {
            foreach (var obj in model.GetObjects(DacQueryScopes.UserDefined).Where(x => x.Name.HasName))
            {
                var name = Normalize(obj.Name.ToString());
                switch (obj.ObjectType.Name)
                {
                    case "Procedure":
                        var p = ReadProcedure(obj, name);
                        Procedures[name] = p;
                        break;
                    case "Table":
                    case "View":
                        var relation = obj.ObjectType.Name == "Table" ? Table.Columns : View.Columns;
                        Tables[name] = new TableSpec(name, obj.GetReferenced(relation)
                            .Select(c => c.Name.Parts.Last()).ToHashSet(StringComparer.OrdinalIgnoreCase));
                        break;
                    case "TableType":
                        TableTypes.Add(name);
                        break;
                }
            }
        }

        private static ProcedureSpec ReadProcedure(TSqlObject obj, string name)
        {
            try
            {
                // DacFx builds unscripted models with TSqlStatementSnippet ASTs.
                // Generate DDL from model metadata and parse it with ScriptDom.
                var ddl = obj.GetScript();
                var parsed = new TSql160Parser(initialQuotedIdentifiers: true)
                    .Parse(new StringReader(ddl), out var errors);
                if (errors.Count > 0) return new ProcedureSpec(
                    name, new Dictionary<string, ParameterSpec>(), false);
                var procedures = ((TSqlScript)parsed).Batches.SelectMany(b => b.Statements)
                    .OfType<ProcedureStatementBody>().ToArray();
                if (procedures.Length != 1) return new ProcedureSpec(
                    name, new Dictionary<string, ParameterSpec>(), false);
                var parameters = procedures[0].Parameters
                    .Select(ReadParameter)
                    .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
                return new ProcedureSpec(name, parameters, true);
            }
            catch (Exception)
            {
                // An unreadable model definition must never become an empty valid signature.
                return new ProcedureSpec(name, new Dictionary<string, ParameterSpec>(), false);
            }
        }
    }

    private static ParameterSpec ReadParameter(ProcedureParameter node)
    {
        string type;
        string? tableType = null;
        if (node.DataType is SqlDataTypeReference builtin)
            type = builtin.SqlDataTypeOption.ToString();
        else if (node.DataType is UserDataTypeReference custom)
        {
            type = "Structured";
            tableType = Normalize(string.Join(".", custom.Name.Identifiers.Select(x => x.Value)));
        }
        else type = "Unknown";
        return new ParameterSpec(node.VariableName.Value, type, tableType,
            node.Value is not null, node.Modifier == ParameterModifier.Output);
    }

        private sealed class SqlReferences : TSqlFragmentVisitor
    {
        internal readonly List<(string Name, string Alias)> Tables = new();
        internal readonly List<(string? Prefix, string Column)> Columns = new();
        internal readonly HashSet<string> Ctes = new(StringComparer.OrdinalIgnoreCase);
        public override void ExplicitVisit(CommonTableExpression node)
        {
            Ctes.Add(node.ExpressionName.Value);
            base.ExplicitVisit(node);
        }
        public override void ExplicitVisit(NamedTableReference node)
        {
            var parts = node.SchemaObject.Identifiers.Select(x => x.Value).ToArray();
            if (parts.Length > 0)
                Tables.Add((string.Join(".", parts), node.Alias?.Value ?? parts[^1]));
        }
        public override void ExplicitVisit(ColumnReferenceExpression node)
        {
            var parts = node.MultiPartIdentifier?.Identifiers.Select(x => x.Value).ToArray();
            if (parts is { Length: > 0 } && parts[^1] != "*")
                Columns.Add((parts.Length >= 2 ? parts[^2] : null, parts[^1]));
        }
    }

    internal static IReadOnlyList<Finding> AnalyzeSql(Schema schema, string sql, string file, int line, string caller)
    {
        var results = new List<Finding>();
        var fragment = new TSql160Parser(true).Parse(new StringReader(sql), out var errors);
        if (errors.Count > 0)
        {
            results.Add(new Finding("REVIEW_REQUIRED", file, line, caller, "",
                "SQL parsing incomplete; references cannot be verified."));
            return results;
        }
        var visitor = new SqlReferences();
        fragment.Accept(visitor);
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, alias) in visitor.Tables)
        {
            var plainName = name.Split('.').Last();
            if (visitor.Ctes.Contains(plainName) || plainName.StartsWith('#') || plainName.StartsWith('@') ||
                name.StartsWith("sys.", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("INFORMATION_SCHEMA.", StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.Split('.').Length > 2)
            {
                results.Add(new Finding("REVIEW_REQUIRED", file, line, caller, name,
                    "Cross-database/server table reference needs review."));
                continue;
            }
            var qualified = Normalize(name);
            if (!schema.Tables.ContainsKey(qualified))
            {
                // An unqualified identifier can resolve through a user/default schema.
                var certain = name.Split('.').Length == 2;
                results.Add(new Finding(certain ? "FAIL" : "REVIEW_REQUIRED", file, line, caller, qualified,
                    certain ? "Explicit SQL table/view reference is absent from the DACPAC." :
                        "Unqualified SQL table/view is not resolved under dbo; default schema needs review."));
                continue;
            }
            aliases[alias] = qualified;
            aliases[plainName] = qualified;
        }
        foreach (var (prefix, column) in visitor.Columns.Distinct())
        {
            string? table = null;
            if (prefix is not null)
                aliases.TryGetValue(prefix, out table);
            else if (aliases.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
                table = aliases.Values.First();
            if (table is null || !schema.Tables.TryGetValue(table, out var tableSpec)) continue;
            if (!tableSpec.Columns.Contains(column))
                results.Add(new Finding("REVIEW_REQUIRED", file, line, caller, table + "." + column,
                    "Column was not found on the inferred table; resolve SQL alias/scope before treating as definite."));
        }
        return results;
    }

    private static IEnumerable<ExpressionSyntax> Flatten(ExpressionSyntax expression, SemanticModel model, int depth = 0)
    {
        if (depth > 5) { yield return expression; yield break; }
        expression = Unwrap(expression);
        InitializerExpressionSyntax? items = expression switch
        {
            ArrayCreationExpressionSyntax x => x.Initializer,
            ImplicitArrayCreationExpressionSyntax x => x.Initializer,
            _ => null
        };
        if (items is not null)
        {
            foreach (var item in items.Expressions)
                foreach (var inner in Flatten(item, model, depth + 1)) yield return inner;
            yield break;
        }
        if (expression is IdentifierNameSyntax id &&
            model.GetSymbolInfo(id).Symbol is ILocalSymbol local &&
            local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax d &&
            d.Initializer is not null && d.SpanStart < expression.SpanStart)
        {
            var block = d.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
            // If the array may have been mutated, do not assert that its original contents are complete.
            if (block is not null && !block.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                    .Any(a => a.SpanStart > d.SpanStart &&
                         (a.Left.ToString() == id.Identifier.ValueText || a.Left.ToString().StartsWith(id.Identifier.ValueText + "[", StringComparison.Ordinal))) &&
                !block.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Any(a => a.Expression.ToString().StartsWith(id.Identifier.ValueText + ".", StringComparison.Ordinal)))
            {
                foreach (var inner in Flatten(d.Initializer.Value, model, depth + 1))
                    yield return inner;
                yield break;
            }
        }
        yield return expression;
    }

    internal static IReadOnlyList<Finding> AnalyzeParameters(
        Schema schema, string procedure, string method, IEnumerable<ExpressionSyntax> args,
        SemanticModel model, string file, int line)
    {
        var findings = new List<Finding>();
        if (!schema.Procedures.TryGetValue(procedure, out var signature) || !signature.Known)
        {
            findings.Add(new Finding("REVIEW_REQUIRED", file, line, method, procedure,
                "Procedure signature is not available in the model."));
            return findings;
        }
        var provided = new Dictionary<string, (string? Type, string? TableType)>(StringComparer.OrdinalIgnoreCase);
        var uncertain = false;
        foreach (var arg in args)
        {
            // Timeouts and cancellation tokens are positional overload options, not SQL arguments.
            if (arg is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NumericLiteralExpression)) continue;
            if (arg.ToString().Contains("CancellationToken", StringComparison.Ordinal)) continue;
            foreach (var item in Flatten(arg, model))
            {
                var (name, type, typeName) = ParseParameter(item, model);
                if (name is null || !name.StartsWith("@", StringComparison.Ordinal))
                {
                    uncertain = true;
                    continue;
                }
                if (!provided.TryAdd(name, (type, typeName)))
                    findings.Add(new Finding("REVIEW_REQUIRED", file, line, method, procedure,
                        "Duplicate supplied SQL parameter " + name + "."));
            }
        }
        if (method.Contains("User", StringComparison.Ordinal))
            provided.TryAdd("@UserId", ("Int", null)); // DatabaseHelper.WithCurrentUser
        foreach (var (name, supplied) in provided)
        {
            if (!signature.Parameters.TryGetValue(name, out var expected))
            {
                findings.Add(new Finding("FAIL", file, line, method, procedure,
                    "Supplied SQL parameter " + name + " does not exist in procedure signature."));
                continue;
            }
            if (expected.Type == "Structured")
            {
                if (supplied.Type is not null && supplied.Type != "Structured")
                    findings.Add(new Finding("FAIL", file, line, method, procedure,
                        name + " expects a table-valued parameter (Structured)."));
                else if (supplied.TableType is not null && !string.Equals(
                    Normalize(supplied.TableType), expected.TableType, StringComparison.OrdinalIgnoreCase))
                    findings.Add(new Finding("FAIL", file, line, method, procedure,
                        name + " TVP type differs: expected " + expected.TableType + "."));
                else if (supplied.TableType is null)
                    findings.Add(new Finding("REVIEW_REQUIRED", file, line, method, procedure,
                        name + " TVP TypeName cannot be verified."));
            }
            else if (supplied.Type == "Structured")
                findings.Add(new Finding("FAIL", file, line, method, procedure,
                    name + " sends a TVP but the procedure expects " + expected.Type + "."));
            else if (supplied.Type is not null && expected.Type != "Unknown" &&
                     !string.Equals(supplied.Type, expected.Type, StringComparison.OrdinalIgnoreCase))
                findings.Add(new Finding("REVIEW_REQUIRED", file, line, method, procedure,
                    name + " SQL type differs (" + supplied.Type + " vs " + expected.Type +
                    "); SQL implicit conversion may be valid."));
        }
        var missing = signature.Parameters.Values.Where(p => !p.Optional && !provided.ContainsKey(p.Name))
            .Select(p => p.Name).ToArray();
        if (missing.Length > 0)
            findings.Add(new Finding("REVIEW_REQUIRED", file, line, method, procedure,
                "Required parameters not statically supplied: " + string.Join(", ", missing) +
                (uncertain ? " (dynamic array may contain them)." : "")));
        if (uncertain)
            findings.Add(new Finding("REVIEW_REQUIRED", file, line, method, procedure,
                "Some SqlParameter values are dynamically constructed or forwarded."));
        return findings;
    }

    internal static int Scan(TSqlModel model, string root, IReadOnlyList<string> files, List<Finding> findings)
    {
        var schema = new Schema(model);
        if (schema.Procedures.Count == 0) throw new InvalidOperationException("DACPAC has no procedures.");
        var trees = files.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path),
            new CSharpParseOptions(LanguageVersion.Preview), path)).ToArray();
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p)).ToArray()
            ?? new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
        var compilation = CSharpCompilation.Create("ReadOnlyAudit",
            trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        int audited = 0;
        for (var i = 0; i < trees.Length; i++)
        {
            var tree = trees[i];
            var semantic = compilation.GetSemanticModel(tree);
            var file = Path.GetRelativePath(root, files[i]).Replace('\\', '/');
            // Wrapper implementations are checked at their call sites, rather than their internal forwarding calls.
            if (file == "DatabaseHelper.cs" || file.StartsWith("Tools/", StringComparison.Ordinal)) continue;
            var doc = tree.GetRoot();
            foreach (var call in doc.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (call.Expression is not MemberAccessExpressionSyntax member) continue;
                var name = member.Name.Identifier.ValueText;
                if (!Procedures.Contains(name) && !TextMethods.Contains(name)) continue;
                var resolved = semantic.GetSymbolInfo(call).Symbol as IMethodSymbol;
                if (resolved is not null)
                {
                    // Semantic identity permits namespace aliases while rejecting lookalike helper classes.
                    if (resolved.ContainingType.Name != "DatabaseHelper" ||
                        resolved.ContainingType.ContainingNamespace.ToDisplayString() != "FMSC_SMS") continue;
                }
                else
                {
                    // No usable binding: fall back to syntax but never promote an unknown receiver to certainty.
                    if (!member.Expression.ToString().EndsWith("DatabaseHelper", StringComparison.Ordinal)) continue;
                    if (semantic.GetSymbolInfo(member.Expression).Symbol is ILocalSymbol or IFieldSymbol or IParameterSymbol)
                        continue;
                }
                var argument = call.ArgumentList.Arguments.FirstOrDefault(a =>
                    a.NameColon?.Name.Identifier.ValueText is "procedure" or "sql")
                    ?? call.ArgumentList.Arguments.FirstOrDefault();
                if (argument is null) continue;
                audited++;
                var loc = tree.GetLineSpan(argument.Expression.Span).StartLinePosition;
                var line = loc.Line + 1;
                var literal = Constant(argument.Expression, semantic);
                if (literal is null)
                {
                    findings.Add(new Finding("REVIEW_REQUIRED", file, line, name, "",
                        "Dynamic procedure/SQL identifier: cannot prove target. Includes local variables and forwarding wrappers."));
                    continue;
                }
                if (TextMethods.Contains(name))
                {
                    findings.AddRange(AnalyzeSql(schema, literal, file, line, name));
                    continue;
                }
                var proc = Normalize(literal);
                if (!schema.Procedures.ContainsKey(proc))
                {
                    findings.Add(new Finding("FAIL", file, line, name, proc,
                        "Referenced stored procedure is absent from DACPAC."));
                    continue;
                }
                findings.AddRange(AnalyzeParameters(schema, proc, name,
                    call.ArgumentList.Arguments.Where(a => !ReferenceEquals(a, argument))
                        .Select(a => a.Expression), semantic, file, line));
            }
            foreach (var command in doc.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                         .Where(x => x.Type.ToString().EndsWith("SqlCommand", StringComparison.Ordinal)))
            {
                var first = command.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
                if (first is null) continue;
                var explicitInitializer = command.Initializer?.Expressions.OfType<AssignmentExpressionSyntax>()
                    .Any(a => a.Left.ToString() == "CommandType" &&
                        a.Right.ToString().EndsWith("CommandType.StoredProcedure", StringComparison.Ordinal)) == true;
                var statement = command.Ancestors().OfType<StatementSyntax>().FirstOrDefault();
                var variable = command.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
                var block = command.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
                var assignments = block?.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                    .Where(a => a.SpanStart > command.SpanStart && variable is not null &&
                        a.Left.ToString() == variable.Identifier.ValueText + ".CommandType")
                    .ToArray() ?? Array.Empty<AssignmentExpressionSyntax>();
                var setProcedure = assignments.Any(a =>
                    a.Right.ToString().EndsWith("CommandType.StoredProcedure", StringComparison.Ordinal));
                if (!explicitInitializer && !setProcedure) continue; // Plain SqlCommand is CommandType.Text.
                audited++;
                var line = tree.GetLineSpan(first.Span).StartLinePosition.Line + 1;
                var identifier = Constant(first, semantic);
                if (assignments.Any(a => !a.Right.ToString().EndsWith("CommandType.StoredProcedure", StringComparison.Ordinal)))
                {
                    findings.Add(new Finding("REVIEW_REQUIRED", file, line, "SqlCommand", "",
                        "Conflicting/conditional CommandType assignments; verify runtime command type."));
                    continue;
                }
                if (identifier is null)
                {
                    findings.Add(new Finding("REVIEW_REQUIRED", file, line, "SqlCommand", "",
                        "Dynamic SqlCommand procedure name."));
                    continue;
                }
                var proc = Normalize(identifier);
                if (!schema.Procedures.ContainsKey(proc))
                    findings.Add(new Finding("FAIL", file, line, "SqlCommand", proc,
                        "Stored procedure is absent from DACPAC."));
                else
                    findings.Add(new Finding("REVIEW_REQUIRED", file, line, "SqlCommand", proc,
                        "SqlCommand.Parameters mutation/collection requires flow-sensitive parameter analysis."));
            }
        }
        return audited;
    }
}
