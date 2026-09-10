namespace DrillPress.Queries;

internal readonly record struct MemberSyntaxCandidate(
    AnalysisSource Source,
    Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax Syntax,
    string Name);
