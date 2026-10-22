using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>A selected call ready for replacement, or for an argument to be added or removed.</summary>
public sealed class InvocationFix : ExpressionFix
{
    internal InvocationFix(AnalysisSource source, ExpressionSyntax call)
        : base(source, call) { }

    /// <summary>Removes the explicit argument bound to a declaration parameter, under an expected overload change and separate value and evaluation proofs.</summary>
    public ArgumentRemoval RemoveArgument(string parameter) => new(Source, Target, parameter);

    /// <summary>Adds an argument for a parameter, such as <c>AddArgument("comparisonType", "StringComparison.Ordinal")</c>. Without <see cref="ArgumentAddition.ExpectOverloadChange"/> it supplies an omitted optional parameter of the same overload; with it, the call moves to the expected overload. The argument is positional when it follows the written ones in order, otherwise named.</summary>
    /// <param name="parameter">The destination parameter's name.</param>
    /// <param name="expression">A complete C# expression for the value; it binds in the call's context.</param>
    public ArgumentAddition AddArgument(string parameter, string expression) =>
        new(Source, Target, parameter, expression);
}
