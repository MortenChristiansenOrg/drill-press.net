using DrillPress.Collections;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress;

/// <summary>An immutable extraction plan for a selected expression group. Grouping alone never proves a correction safe.</summary>
public sealed class ExpressionExtraction
{
    private readonly ExpressionGroup _group;
    private readonly AnalysisSource _destination;
    private readonly TypeDeclarationSyntax? _part;
    private readonly string? _name;
    private readonly string _parameter;
    private readonly bool _reuse;
    private readonly ExtractionNameCollision _collision;

    internal ExpressionExtraction(ExpressionGroup group)
        : this(
            group,
            group.Owner.Source,
            group.Owner.Syntax as TypeDeclarationSyntax,
            null,
            "value",
            true,
            ExtractionNameCollision.Refuse
        ) { }

    private ExpressionExtraction(
        ExpressionGroup group,
        AnalysisSource destination,
        TypeDeclarationSyntax? part,
        string? name,
        string parameter,
        bool reuse,
        ExtractionNameCollision collision
    )
    {
        _group = group;
        _destination = destination;
        _part = part;
        _name = name;
        _parameter = parameter;
        _reuse = reuse;
        _collision = collision;
    }

    /// <summary>Chooses a private constant name. Reuse prefers an equivalent constant with that name, otherwise requires exactly one equivalent constant in the owner.</summary>
    public ExpressionExtraction ToConstant(
        string name,
        bool reuseExisting = true,
        ExtractionNameCollision collision = ExtractionNameCollision.Refuse
    )
    {
        if (_group.Kind != ExpressionGroupKind.Constant)
            throw new InvalidOperationException(
                "Constant extraction requires a constant expression group."
            );
        ValidateName(name);
        if (!Enum.IsDefined(collision))
            throw new ArgumentOutOfRangeException(nameof(collision));
        return new(_group, _destination, _part, name, _parameter, reuseExisting, collision);
    }

    /// <summary>Chooses a private static string helper with one explicitly typed value parameter. Existing helpers are never reused by text matching.</summary>
    public ExpressionExtraction ToMethod(
        string name,
        string parameterName = "value",
        ExtractionNameCollision collision = ExtractionNameCollision.Refuse
    )
    {
        if (_group.Kind != ExpressionGroupKind.OneHoleTemplate)
            throw new InvalidOperationException(
                "Helper extraction requires a one-hole template group."
            );
        ValidateName(name);
        ValidateName(parameterName);
        if (!Enum.IsDefined(collision))
            throw new ArgumentOutOfRangeException(nameof(collision));
        return new(_group, _destination, _part, name, parameterName, false, collision);
    }

    /// <summary>Selects the insertion part explicitly. It must be an editable ordinary part of the same source type and context.</summary>
    public ExpressionExtraction InPart(CodeNode<TypeDeclarationSyntax> part) =>
        new(_group, part.Source, part.Syntax, _name, _parameter, _reuse, _collision);

    /// <summary>Proposes insertion plus all selected replacements atomically. The required consumer proof owns behavioral assumptions, including allowlisted call effects and formatting/culture.</summary>
    public FixProposal? Propose(Func<ExtractionEvidence, ProofResult> provesBehavior)
    {
        if (_name is null || _part is null)
            return null;
        return ExtractionPlan
            .Create(_group, _destination, _part, _name, _parameter, _reuse, _collision)
            ?.Propose(provesBehavior);
    }

    private static void ValidateName(string name)
    {
        if (
            string.IsNullOrWhiteSpace(name)
            || name.StartsWith('@')
            || !SyntaxFacts.IsValidIdentifier(name)
            || SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
        )
            throw new ArgumentException("Use a non-empty, unescaped C# identifier.", nameof(name));
    }
}
