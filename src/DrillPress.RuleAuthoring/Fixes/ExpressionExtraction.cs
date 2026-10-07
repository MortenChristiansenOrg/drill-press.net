using DrillPress;
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
    private readonly CodeQuery<CodeExpression>? _coverage;

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
        ExtractionNameCollision collision,
        CodeQuery<CodeExpression>? coverage = null
    )
    {
        _group = group;
        _destination = destination;
        _part = part;
        _name = name;
        _parameter = parameter;
        _reuse = reuse;
        _collision = collision;
        _coverage = coverage;
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
        return new(
            _group,
            _destination,
            _part,
            name,
            _parameter,
            reuseExisting,
            collision,
            _coverage
        );
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
        return new(_group, _destination, _part, name, parameterName, false, collision, _coverage);
    }

    /// <summary>Requires every selected expression in this owner, across all ordinary partial parts in each validated context, to belong to the extraction group. Include compliant uses in the supplied query; nested types are excluded.</summary>
    public ExpressionExtraction OnlyWhenGroupCoversAll(CodeQuery<CodeExpression> expressions) =>
        new(_group, _destination, _part, _name, _parameter, _reuse, _collision, expressions);

    /// <summary>Camel-cases the capture name when all occurrences agree; differing, missing or invalid names fall back to value.</summary>
    public ExpressionExtraction ToMethod(
        string name,
        ParameterName parameterName,
        ExtractionNameCollision collision = ExtractionNameCollision.Refuse
    )
    {
        if (parameterName != ParameterName.FromCapture)
            throw new ArgumentOutOfRangeException(nameof(parameterName));
        var captureNames = _group
            .Occurrences.Select(occurrence => occurrence.Capture?.Symbol?.Name)
            .Distinct()
            .Take(2)
            .ToArray();
        var selected = System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(
            captureNames.Length == 1 ? captureNames[0] ?? "value" : "value"
        );
        if (
            !SyntaxFacts.IsValidIdentifier(selected)
            || SyntaxFacts.GetKeywordKind(selected) != SyntaxKind.None
        )
            selected = "value";
        return ToMethod(name, selected, collision);
    }

    /// <summary>Selects the insertion part explicitly. It must be an editable ordinary part of the same source type and context.</summary>
    public ExpressionExtraction InPart(CodeNode<TypeDeclarationSyntax> part) =>
        new(_group, part.Source, part.Syntax, _name, _parameter, _reuse, _collision, _coverage);

    /// <summary>Proposes a constant extraction, whose equivalence the library proves. Template groups need <see cref="SafeWhen"/>.</summary>
    /// <remarks>Gates: editable ordinary partial parts, trivia and observable contexts, owner and group identity, constant values or template shape, capture correspondence, moved-call bindings, enclosing bindings, compiler-supplied arguments and the combined compilation in every affected context.</remarks>
    public FixProposal? Propose() =>
        _group.Kind == ExpressionGroupKind.Constant ? SafeWhen(_ => true) : null;

    /// <summary>Proposes the member insertion and every replacement as one batch when your proof holds; template extraction must justify formatting, culture and moved-call behavior.</summary>
    /// <remarks>Gates: editable ordinary partial parts, trivia and observable contexts, owner and group identity, constant values or template shape, capture correspondence, moved-call bindings, enclosing bindings, compiler-supplied arguments and the combined compilation in every affected context.</remarks>
    public FixProposal? SafeWhen(Func<ExtractionChange, bool> proof)
    {
        if (_name is null || _part is null || !Covers(_group.Owner.Source.Project))
            return null;
        return ExtractionPlan
            .Create(_group, _destination, _part, _name, _parameter, _reuse, _collision)
            ?.Propose(evidence => Covers(evidence.Context.Original) && proof(evidence));
    }

    private bool Covers(AnalysisProject project)
    {
        if (_coverage is null)
            return true;
        var selected = _group
            .Occurrences.Select(occurrence =>
                (
                    occurrence.Expression.Source.Document.FileIdentity,
                    occurrence.Expression.Syntax.Span
                )
            )
            .ToHashSet();
        var ownerIdentity = RewriteSymbols.Identity(_group.Owner.Symbol);
        return _coverage
            .In(_group.Owner.Solution)
            .Where(expression => expression.Source.Project == project)
            .Where(expression =>
                RewriteSymbols.Identity(
                    expression
                        .Source.Model.GetEnclosingSymbol(
                            expression.Syntax.SpanStart,
                            project.CancellationToken
                        )
                        ?.ContainingType
                ) == ownerIdentity
            )
            .All(expression =>
                selected.Contains((expression.Source.Document.FileIdentity, expression.Syntax.Span))
            );
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
