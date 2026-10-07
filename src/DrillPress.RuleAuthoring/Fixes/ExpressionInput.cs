using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>A tracked original operand for constructing replacements without losing occurrence identity.</summary>
internal sealed class ExpressionInput
{
    internal ExpressionInput(AnalysisSource source, ExpressionSyntax original)
    {
        Source = source;
        Original = original;
        Annotation = new SyntaxAnnotation();
        Syntax = original.WithoutTrivia().WithAdditionalAnnotations(Annotation);
    }

    /// <summary>The original operand's compilation membership.</summary>
    internal AnalysisSource Source { get; }

    /// <summary>The exact original occurrence, not just its symbol or source text.</summary>
    internal ExpressionSyntax Original { get; }

    /// <summary>Embed this annotated node in a replacement and register the input with MapInputs.</summary>
    internal ExpressionSyntax Syntax { get; }
    internal SyntaxAnnotation Annotation { get; }
}
