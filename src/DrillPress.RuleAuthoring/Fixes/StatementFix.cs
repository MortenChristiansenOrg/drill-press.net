using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>Structured edits of written statements.</summary>
public sealed class StatementFix
{
    private readonly AnalysisSource _source;
    private readonly StatementSyntax _statement;

    internal StatementFix(AnalysisSource source, StatementSyntax statement)
    {
        _source = source;
        _statement = statement;
    }

    /// <summary>Replaces a foreach containing one xUnit assertion with Assert.All, explicitly accepting aggregated failure reporting.</summary>
    public AssertionLoopConversion UseAssertAll() => new(_source, _statement);
}
