using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DrillPress.Flow;

/// <summary>Compiler flow facts for one method, cached within its solution when used through For. These facts do not prove behavioral equivalence of edits.</summary>
public sealed class MethodFlow
{
    private readonly CodeMethod _method;
    private readonly Lazy<ControlFlowGraph?> _graph;
    private readonly Lazy<DataFlowAnalysis?> _data;

    /// <summary>Creates lazy method-local analysis. Nested functions have their own control flow.</summary>
    public MethodFlow(CodeMethod method)
    {
        _method = method;
        _graph = new(() => method.Source.Model.GetOperation(method.Syntax, method.Source.Project.CancellationToken) is IMethodBodyOperation body
            ? ControlFlowGraph.Create(body, method.Source.Project.CancellationToken) : null);
        _data = new(() => method.Syntax.Body is { } block ? method.Source.Model.AnalyzeDataFlow(block) :
            method.Syntax.ExpressionBody is { Expression: { } expression } ? method.Source.Model.AnalyzeDataFlow(expression) : null);
    }

    /// <summary>Returns the same method analysis for repeated rules within one solution.</summary>
    public static MethodFlow For(AnalysisSolution solution, CodeMethod method) => solution.Cached(method, () => new MethodFlow(method));

    /// <summary>The compiler CFG, or null for a declaration without a supported executable body.</summary>
    public ControlFlowGraph? Graph => _graph.Value;

    /// <summary>Roslyn reads, writes, captures and data-flow sets; inspect Succeeded before relying on results.</summary>
    public DataFlowAnalysis? Data => _data.Value;

    /// <summary>The compiler's nullable state at an expression; None means analysis has no answer.</summary>
    public NullableFlowState NullState(ExpressionSyntax expression) => _method.Source.Model
        .GetTypeInfo(expression, _method.Source.Project.CancellationToken).Nullability.FlowState;
}
