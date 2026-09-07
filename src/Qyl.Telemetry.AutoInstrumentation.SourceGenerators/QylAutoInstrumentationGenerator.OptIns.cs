using ANcpLua.Roslyn.Utilities;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Qyl.Telemetry.AutoInstrumentation.SourceGenerators;

/// <summary>
/// The <c>QYL1002</c> lane: a library whose native <c>ActivitySource</c> qyl subscribes, but whose
/// telemetry the consumer has to turn on in their own code.
/// </summary>
/// <remarks>
/// One table, one diagnostic, one message shape. A row is not a claim that the library is silent
/// without the call — that differs per library, and the row's consequence text says which case it
/// is. MySql.Data has no row on purpose: its add-on package's <c>AddConnectorNet()</c> is
/// <c>AddSource("connector-net")</c> and nothing more, which is exactly what <c>AddQyl()</c>
/// already does, so there is nothing for a consumer to add.
/// </remarks>
public sealed partial class QylAutoInstrumentationGenerator
{
    /// <summary>One library's opt-in: what names it, what turns its telemetry on, and what is lost without it.</summary>
    private readonly record struct OptInDeclaration(
        string Library,
        string TriggerNamespace,
        EquatableArray<string> TriggerMethods,
        string OptInMethod,
        string OptInDisplay,
        string Consequence);

    private static readonly ImmutableArray<OptInDeclaration> s_optIns =
    [
        new(
            "GraphQL.NET",
            "GraphQL",
            ImmutableArray.Create("AddGraphQL").AsEquatableArray(),
            "UseTelemetry",
            "IGraphQLBuilder.UseTelemetry()",
            "GraphQL.NET's ActivitySource stays silent, so the application produces no GraphQL spans at all"),
        new(
            "Oracle ODP.NET",
            "Oracle.ManagedDataAccess",
            ImmutableArray.Create("ExecuteScalar", "ExecuteNonQuery", "ExecuteReader").AsEquatableArray(),
            "AddOracleDataProviderInstrumentation",
            "TracerProviderBuilder.AddOracleDataProviderInstrumentation()",
            "ODP.NET still emits its command spans, but only with db.system, db.odp.roundtrip.count, "
            + "db.odp.roundtrip.duration and db.response.returned_rows; db.name, db.user, db.statement, "
            + "server.address, server.port, db.odp.connection.id, db.odp.sql_id and exception recording "
            + "stay off"),
    ];

    /// <summary>What one invocation said about the opt-in table: it used a library, or it opted one in.</summary>
    private readonly record struct OptInSignal(int Row, bool IsOptIn, Location? Location);

    private static OptInSignal ScanOptIn(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var compilation = context.SemanticModel.Compilation;
        if (compilation.AssemblyName is { } assemblyName && s_qylRuntimeAssemblies.Contains(assemblyName))
            return default;

        if (context.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol symbol)
            return default;

        for (var row = 0; row < s_optIns.Length; row++)
        {
            var declaration = s_optIns[row];
            if (string.Equals(symbol.Name, declaration.OptInMethod, StringComparison.Ordinal))
                return new OptInSignal(row + 1, true, null);

            if (!ContainsOrdinal(declaration.TriggerMethods, symbol.Name))
                continue;

            if (IsInNamespace(symbol, declaration.TriggerNamespace) ||
                (symbol.ReducedFrom is { Parameters.Length: > 0 } reduced &&
                 IsInNamespace(reduced.Parameters[0].Type, declaration.TriggerNamespace)))
            {
                return new OptInSignal(row + 1, false, invocation.GetLocation());
            }
        }

        return default;
    }

    private static bool ContainsOrdinal(EquatableArray<string> values, string value)
    {
        foreach (var candidate in values)
        {
            if (string.Equals(candidate, value, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsInNamespace(IMethodSymbol symbol, string namespaceName)
        => IsInNamespace(symbol.ContainingType, namespaceName)
           || (symbol.ContainingNamespace is { } declaring &&
               NamespaceMatches(declaring.ToDisplayString(), namespaceName));

    private static bool IsInNamespace(ITypeSymbol? type, string namespaceName)
        => type?.ContainingNamespace is { } containing && NamespaceMatches(containing.ToDisplayString(), namespaceName);

    private static bool NamespaceMatches(string actual, string expected)
        => string.Equals(actual, expected, StringComparison.Ordinal)
           || (actual.Length > expected.Length && actual.StartsWithOrdinal(expected) && actual[expected.Length] is '.');

    private static void ReportMissingOptIns(SourceProductionContext context, ImmutableArray<OptInSignal> signals)
    {
        if (signals.IsDefaultOrEmpty)
            return;

        var optedIn = new HashSet<int>();
        var used = new Dictionary<int, Location>();
        foreach (var signal in signals)
        {
            if (signal.Row is 0)
                continue;

            if (signal.IsOptIn)
                optedIn.Add(signal.Row);
            else if (signal.Location is { } location && !used.ContainsKey(signal.Row))
                used[signal.Row] = location;
        }

        // Stable order so the build log is reproducible whatever order Roslyn visited the trees in.
        var rows = new List<int>(used.Keys);
        rows.Sort();
        foreach (var row in rows)
        {
            if (optedIn.Contains(row))
                continue;

            var declaration = s_optIns[row - 1];
            context.ReportDiagnostic(Diagnostic.Create(
                QylGeneratorDiagnostics.NativeTelemetryOptInMissing,
                used[row],
                declaration.Library,
                declaration.OptInDisplay,
                declaration.Consequence));
        }
    }
}
