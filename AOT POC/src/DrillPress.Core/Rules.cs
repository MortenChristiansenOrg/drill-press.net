using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DrillPress;

public sealed class RuleCondition<T>
{
    private readonly Func<T, bool> _evaluate;

    public RuleCondition(Func<T, bool> evaluate)
        : this(evaluate, null)
    {
    }

    private RuleCondition(Func<T, bool> evaluate, ImmutableHashSet<string>? requiredMemberNames)
    {
        _evaluate = evaluate;
        RequiredMemberNames = requiredMemberNames;
    }

    internal ImmutableHashSet<string>? RequiredMemberNames { get; }

    internal bool Evaluate(T value) => _evaluate(value);

    public RuleCondition<T> And(RuleCondition<T> other) => new(
        value => Evaluate(value) && other.Evaluate(value),
        IntersectRequiredMemberNames(RequiredMemberNames, other.RequiredMemberNames));

    public RuleCondition<T> Or(RuleCondition<T> other) => new(
        value => Evaluate(value) || other.Evaluate(value),
        UnionRequiredMemberNames(RequiredMemberNames, other.RequiredMemberNames));

    public RuleCondition<T> Not() => new(value => !Evaluate(value));

    /// <summary>
    /// Treats the base condition as satisfied whenever the exception applies.
    /// </summary>
    public RuleCondition<T> ExceptWhen(RuleCondition<T> exception) => Or(exception);

    public static RuleCondition<T> From(Func<T, bool> condition) => new(condition);

    internal static RuleCondition<T> FromMemberNames(
        Func<T, bool> condition,
        params string[] memberNames) =>
        new(condition, memberNames.ToImmutableHashSet(StringComparer.Ordinal));

    private static ImmutableHashSet<string>? IntersectRequiredMemberNames(
        ImmutableHashSet<string>? left,
        ImmutableHashSet<string>? right)
    {
        if (left is null)
        {
            return right;
        }

        return right is null ? left : left.Intersect(right);
    }

    private static ImmutableHashSet<string>? UnionRequiredMemberNames(
        ImmutableHashSet<string>? left,
        ImmutableHashSet<string>? right) =>
        left is null || right is null ? null : left.Union(right);
}

public sealed class RuleLocation<T>(Func<T, SourceLocation> select)
{
    internal SourceLocation Select(T value) => select(value);

    public static RuleLocation<T> From(Func<T, SourceLocation> select) => new(select);
}

public sealed class RuleFix<T>(Func<T, ImmutableArray<TextEdit>> create)
{
    internal ImmutableArray<TextEdit> Create(T value) => create(value);

    public static RuleFix<T> From(Func<T, TextEdit> create) => new(value => [create(value)]);

    public static RuleFix<T> FromMany(Func<T, ImmutableArray<TextEdit>> create) => new(create);
}

public sealed record TextEdit(string FilePath, TextSpan Span, string NewText);

public sealed class CodeQuery<T>
{
    private readonly Func<AnalysisSolution, ImmutableHashSet<string>?, IEnumerable<T>> _select;
    private readonly RuleCondition<T>? _filter;

    public CodeQuery(Func<AnalysisSolution, IEnumerable<T>> select)
        : this((solution, _) => select(solution), selectsMemberReferences: false, filter: null)
    {
    }

    internal CodeQuery(
        Func<AnalysisSolution, ImmutableHashSet<string>?, IEnumerable<T>> select,
        bool selectsMemberReferences,
        RuleCondition<T>? filter = null)
    {
        _select = select;
        SelectsMemberReferences = selectsMemberReferences;
        _filter = filter;
    }

    internal bool SelectsMemberReferences { get; }

    internal ImmutableHashSet<string>? RequiredMemberNames => _filter?.RequiredMemberNames;

    internal IEnumerable<T> Evaluate(AnalysisSolution solution) =>
        _filter is null
            ? _select(solution, null)
            : _select(solution, _filter.RequiredMemberNames).Where(_filter.Evaluate);

    public CodeQuery<T> Where(RuleCondition<T> condition) =>
        new(_select, SelectsMemberReferences, _filter is null ? condition : _filter.And(condition));
}

public static class Code
{
    public static CodeQuery<MethodModel> Methods { get; } =
        new((solution, _) => solution.Methods, selectsMemberReferences: false);

    public static CodeQuery<InterfaceModel> Interfaces { get; } =
        new((solution, _) => solution.Interfaces, selectsMemberReferences: false);

    public static CodeQuery<NamedTypeModel> Types { get; } =
        new((solution, _) => solution.Types, selectsMemberReferences: false);

    public static CodeQuery<MemberReferenceModel> MemberReferences { get; } =
        new((solution, names) => solution.GetMemberReferences(names), selectsMemberReferences: true);
}

public sealed record RuleDescriptor(string Id, string Message);

public sealed record RuleDiagnostic(
    RuleDescriptor Descriptor,
    SourceLocation Location,
    ImmutableArray<TextEdit> Fixes);

internal interface ICompiledRule
{
    bool SelectsMemberReferences { get; }

    ImmutableHashSet<string>? RequiredMemberNames { get; }

    IEnumerable<RuleDiagnostic> Evaluate(AnalysisSolution solution);
}

internal sealed class RequiredRule<T>(
    CodeQuery<T> query,
    RuleDescriptor descriptor,
    RuleCondition<T> condition,
    RuleLocation<T> location,
    RuleFix<T>? fix) : ICompiledRule
{
    public bool SelectsMemberReferences => query.SelectsMemberReferences;

    public ImmutableHashSet<string>? RequiredMemberNames => query.RequiredMemberNames;

    public IEnumerable<RuleDiagnostic> Evaluate(AnalysisSolution solution)
    {
        foreach (var candidate in query.Evaluate(solution))
        {
            if (!condition.Evaluate(candidate))
            {
                var selectedLocation = location.Select(candidate);
                yield return new RuleDiagnostic(
                    descriptor,
                    selectedLocation,
                    selectedLocation.Document.IsGenerated ? [] : fix?.Create(candidate) ?? []);
            }
        }
    }
}

public sealed class RuleSet
{
    private readonly List<ICompiledRule> _rules = [];

    public RuleScope<T> For<T>(CodeQuery<T> query)
        where T : ICodeEntity => new(this, query);

    internal void Add<T>(
        CodeQuery<T> query,
        string id,
        RuleCondition<T> condition,
        string message,
        RuleLocation<T>? location,
        RuleFix<T>? fix)
        where T : ICodeEntity
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (_rules.OfType<IRuleWithId>().Any(rule => rule.Id == id))
        {
            throw new InvalidOperationException($"Rule id '{id}' is registered more than once.");
        }

        _rules.Add(new IdentifiedRule<T>(
            id,
            new RequiredRule<T>(
                query,
                new RuleDescriptor(id, message),
                condition,
                location ?? RuleLocation<T>.From(static entity => entity.Location),
                fix)));
    }

    public ImmutableArray<RuleDiagnostic> Evaluate(
        AnalysisSolution solution,
        Action<string, TimeSpan>? reportProfile = null)
    {
        var memberRules = _rules.Where(static rule => rule.SelectsMemberReferences).ToArray();
        if (memberRules.Length > 0)
        {
            var preparationStarted = Stopwatch.GetTimestamp();
            var requiredNames = memberRules.Any(static rule => rule.RequiredMemberNames is null)
                ? null
                : memberRules.SelectMany(static rule => rule.RequiredMemberNames!).ToImmutableHashSet(StringComparer.Ordinal);
            solution.PrepareMemberReferences(requiredNames);
            reportProfile?.Invoke(
                "prepare-member-references",
                Stopwatch.GetElapsedTime(preparationStarted));
        }

        IEnumerable<RuleDiagnostic> diagnostics = reportProfile is null
            ? _rules.SelectMany(rule => rule.Evaluate(solution))
            : EvaluateProfiled(solution, reportProfile);
        return diagnostics
            .OrderBy(diagnostic => diagnostic.Location.Document.Path, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.Span.Start)
            .ThenBy(diagnostic => diagnostic.Descriptor.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private IEnumerable<RuleDiagnostic> EvaluateProfiled(
        AnalysisSolution solution,
        Action<string, TimeSpan> reportProfile)
    {
        foreach (var rule in _rules.Cast<IRuleWithId>())
        {
            var started = Stopwatch.GetTimestamp();
            var diagnostics = ((ICompiledRule)rule).Evaluate(solution).ToImmutableArray();
            reportProfile(rule.Id, Stopwatch.GetElapsedTime(started));
            foreach (var diagnostic in diagnostics)
            {
                yield return diagnostic;
            }
        }
    }

    private interface IRuleWithId
    {
        string Id { get; }
    }

    private sealed class IdentifiedRule<T>(string id, ICompiledRule inner) : ICompiledRule, IRuleWithId
    {
        public string Id { get; } = id;

        public bool SelectsMemberReferences => inner.SelectsMemberReferences;

        public ImmutableHashSet<string>? RequiredMemberNames => inner.RequiredMemberNames;

        public IEnumerable<RuleDiagnostic> Evaluate(AnalysisSolution solution) => inner.Evaluate(solution);
    }
}

public sealed class RuleScope<T>(RuleSet rules, CodeQuery<T> query)
    where T : ICodeEntity
{
    public RuleScope<T> Require(
        string id,
        RuleCondition<T> condition,
        string message,
        RuleLocation<T>? at = null,
        RuleFix<T>? fix = null)
    {
        rules.Add(query, id, condition, message, at, fix);
        return this;
    }

    public RuleScope<T> Forbid(
        string id,
        string message,
        RuleLocation<T>? at = null,
        RuleFix<T>? fix = null) =>
        Require(id, RuleCondition<T>.From(static _ => false), message, at, fix);
}

public static class Methods
{
    public static RuleCondition<MethodModel> AreDeclaredOn<T>() =>
        AreDeclaredOn(CodeType.Of<T>());

    public static RuleCondition<MethodModel> AreDeclaredOn(CodeType type) =>
        RuleCondition<MethodModel>.From(method => CodeTypeMatching.Matches(method.Symbol.ContainingType, type));

    public static RuleCondition<MethodModel> HaveAttribute<TAttribute>()
        where TAttribute : Attribute => HaveAnyAttribute(CodeType.Of<TAttribute>());

    public static RuleCondition<MethodModel> HaveAnyAttribute(params CodeType[] types) =>
        RuleCondition<MethodModel>.From(method => method.Symbol.GetAttributes().Any(attribute =>
            types.Any(type => CodeTypeMatching.Matches(attribute.AttributeClass, type))));

    public static RuleCondition<MethodModel> HaveAnyAttribute(params string[] fullyQualifiedNames) =>
        HaveAnyAttribute(fullyQualifiedNames.Select(name => CodeType.Named(name)).ToArray());

    public static RuleCondition<MethodModel> Return<T>() => Return(CodeType.Of<T>());

    public static RuleCondition<MethodModel> Return(CodeType type) =>
        RuleCondition<MethodModel>.From(method => CodeTypeMatching.Matches(method.Symbol.ReturnType, type));

    public static RuleCondition<MethodModel> HaveParameter<T>() => HaveParameter(CodeType.Of<T>());

    public static RuleCondition<MethodModel> HaveParameter(CodeType type) =>
        RuleCondition<MethodModel>.From(method =>
            method.Symbol.Parameters.Any(parameter => CodeTypeMatching.Matches(parameter.Type, type)));

    public static RuleCondition<MethodModel> HaveAtMostEmptyLines(int count) =>
        RuleCondition<MethodModel>.From(method => method.EmptyLines.Length <= count);

    public static RuleCondition<MethodModel> HaveAllAssertionsAfterLastEmptyLine { get; } =
        RuleCondition<MethodModel>.From(method =>
            method.EmptyLines.IsEmpty ||
            method.Assertions.All(assertion => assertion.Location.Span.Start > method.EmptyLines[^1].Span.End));

    public static RuleCondition<MethodModel> HaveOnlyAssertion(RuleCondition<AssertionModel> condition) =>
        RuleCondition<MethodModel>.From(method =>
            method.Assertions is [var assertion] && condition.Evaluate(assertion));

    public static RuleLocation<MethodModel> EmptyLineAfter(int allowedCount) =>
        RuleLocation<MethodModel>.From(method =>
            method.EmptyLines.Length > allowedCount ? method.EmptyLines[allowedCount] : method.Location);

    public static RuleLocation<MethodModel> FirstAssertionBeforeLastEmptyLine { get; } =
        RuleLocation<MethodModel>.From(method =>
        {
            if (method.EmptyLines.IsEmpty)
            {
                return method.Location;
            }

            return method.Assertions
                       .FirstOrDefault(assertion => assertion.Location.Span.Start <= method.EmptyLines[^1].Span.End)
                       ?.Location
                   ?? method.Location;
        });
}

public static class Assertions
{
    public static RuleCondition<AssertionModel> Are<TDeclaringType>(string methodName) =>
        Are(CodeType.Of<TDeclaringType>(), methodName);

    public static RuleCondition<AssertionModel> Are(CodeType declaringType, string methodName) =>
        RuleCondition<AssertionModel>.From(assertion =>
            assertion.Symbol.Name == methodName &&
            CodeTypeMatching.Matches(assertion.Symbol.ContainingType, declaringType));
}

public static class Interfaces
{
    public static RuleCondition<InterfaceModel> Are<TInterface>() => Are(CodeType.Of<TInterface>());

    public static RuleCondition<InterfaceModel> Are(CodeType type) =>
        RuleCondition<InterfaceModel>.From(@interface => CodeTypeMatching.Matches(@interface.Symbol, type));

    public static RuleCondition<InterfaceModel> DoNotHaveExactlyOneNonTestImplementation { get; } =
        RuleCondition<InterfaceModel>.From(@interface =>
            @interface.NonTestConcreteImplementations.Take(2).Count() != 1);
}

public static class Members
{
    public static RuleCondition<MemberReferenceModel> Are<TDeclaringType>(string memberName) =>
        Are(CodeType.Of<TDeclaringType>(), memberName);

    public static RuleCondition<MemberReferenceModel> Are(CodeType containingType, string memberName) =>
        RuleCondition<MemberReferenceModel>.FromMemberNames(
            reference =>
                reference.Symbol.Name == memberName &&
                CodeTypeMatching.Matches(reference.Symbol.ContainingType, containingType),
            memberName);

    public static RuleCondition<MemberReferenceModel> Are(string containingType, string memberName) =>
        Are(CodeType.Named(containingType), memberName);

    public static RuleCondition<MemberReferenceModel> HaveType<T>() => HaveType(CodeType.Of<T>());

    public static RuleCondition<MemberReferenceModel> HaveType(CodeType type) =>
        RuleCondition<MemberReferenceModel>.From(reference =>
            CodeTypeMatching.Matches(GetMemberType(reference.Symbol), type));

    public static RuleFix<MemberReferenceModel> ReplaceWith(string replacement) =>
        RuleFix<MemberReferenceModel>.From(reference =>
            new TextEdit(reference.Document.Path, reference.Syntax.Span, replacement));

    public static RuleFix<MemberReferenceModel> RemoveArgument { get; } =
        RuleFix<MemberReferenceModel>.FromMany(reference =>
        {
            if (reference.Syntax.Parent is not ArgumentSyntax argument ||
                argument.Parent is not ArgumentListSyntax argumentList)
            {
                return [];
            }

            var arguments = argumentList.Arguments;
            var index = arguments.IndexOf(argument);
            if (index < 0)
            {
                return [];
            }

            if (argumentList.Parent is not InvocationExpressionSyntax invocation)
            {
                return [];
            }

            var rewrittenInvocation = invocation.WithArgumentList(
                argumentList.WithArguments(arguments.RemoveAt(index)));
            var rewrittenSymbol = reference.Document.SemanticModel.GetSpeculativeSymbolInfo(
                invocation.SpanStart,
                rewrittenInvocation,
                SpeculativeBindingOption.BindAsExpression).Symbol;
            if (rewrittenSymbol is not IMethodSymbol)
            {
                return [];
            }

            TextSpan span;
            if (arguments.Count == 1)
            {
                span = argument.Span;
            }
            else if (index < arguments.Count - 1)
            {
                span = TextSpan.FromBounds(argument.FullSpan.Start, arguments[index + 1].FullSpan.Start);
            }
            else
            {
                var separator = arguments.GetSeparator(index - 1);
                span = TextSpan.FromBounds(separator.SpanStart, argument.FullSpan.End);
            }

            return [new TextEdit(reference.Document.Path, span, string.Empty)];
        });

    private static ITypeSymbol? GetMemberType(ISymbol symbol) => symbol switch
    {
        IFieldSymbol field => field.Type,
        IPropertySymbol property => property.Type,
        IMethodSymbol method => method.ReturnType,
        _ => null,
    };
}

public static class Types
{
    public static RuleCondition<NamedTypeModel> Are<T>() => Are(CodeType.Of<T>());

    public static RuleCondition<NamedTypeModel> Are(CodeType type) =>
        RuleCondition<NamedTypeModel>.From(candidate => CodeTypeMatching.Matches(candidate.Symbol, type));

    public static RuleCondition<NamedTypeModel> Implement<TInterface>() =>
        Implement(CodeType.Of<TInterface>());

    public static RuleCondition<NamedTypeModel> Implement(CodeType interfaceType) =>
        RuleCondition<NamedTypeModel>.From(candidate =>
            candidate.Symbol.AllInterfaces.Any(@interface => CodeTypeMatching.Matches(@interface, interfaceType)));
}
