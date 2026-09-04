namespace DrillPress;

/// <summary>A stable source location reported by a compiled rule.</summary>
public sealed record SourceLocation(
    string FilePath,
    int Start,
    int Length,
    int Line,
    int Column);

/// <summary>A source-level member reference discovered by the analysis engine.</summary>
public sealed record MemberReference(
    CodeType ContainingType,
    string MemberName,
    SourceLocation Location);

/// <summary>A stable CLR type identity used by strongly typed rule helpers.</summary>
public readonly record struct CodeType(string MetadataName)
{
    public static CodeType Of<T>() => new(GetMetadataName(typeof(T)));

    public static CodeType Named(string metadataName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataName);
        return new CodeType(metadataName);
    }

    private static string GetMetadataName(Type type)
    {
        if (type.IsConstructedGenericType)
        {
            type = type.GetGenericTypeDefinition();
        }

        if (type.DeclaringType is not null)
        {
            return $"{GetMetadataName(type.DeclaringType)}+{type.Name}";
        }

        return string.IsNullOrEmpty(type.Namespace)
            ? type.Name
            : $"{type.Namespace}.{type.Name}";
    }
}

/// <summary>A composable condition over candidates selected by a query.</summary>
public sealed class RuleCondition<T>(Func<T, bool> predicate)
{
    internal bool Evaluate(T candidate) => predicate(candidate);
}

/// <summary>A reusable selection of analysis candidates.</summary>
public sealed class CodeQuery<T>
{
    private readonly Func<IReadOnlyList<MemberReference>, IEnumerable<T>> _select;
    private readonly RuleCondition<T>? _condition;

    internal CodeQuery(
        Func<IReadOnlyList<MemberReference>, IEnumerable<T>> select,
        RuleCondition<T>? condition = null)
    {
        _select = select;
        _condition = condition;
    }

    public CodeQuery<T> Where(RuleCondition<T> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return new CodeQuery<T>(
            _select,
            _condition is null
                ? condition
                : new RuleCondition<T>(candidate =>
                    _condition.Evaluate(candidate) && condition.Evaluate(candidate)));
    }

    internal IEnumerable<T> Evaluate(IReadOnlyList<MemberReference> memberReferences)
    {
        var candidates = _select(memberReferences);
        return _condition is null ? candidates : candidates.Where(_condition.Evaluate);
    }
}

public static class Code
{
    public static CodeQuery<MemberReference> MemberReferences { get; } =
        new(static memberReferences => memberReferences);
}

public static class Members
{
    public static RuleCondition<MemberReference> Are<TDeclaringType>(string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        var declaringType = CodeType.Of<TDeclaringType>();
        return new RuleCondition<MemberReference>(reference =>
            reference.ContainingType == declaringType &&
            StringComparer.Ordinal.Equals(reference.MemberName, memberName));
    }
}

public sealed record RuleDescriptor(string Id, string Message);

public sealed record RuleDiagnostic(RuleDescriptor Descriptor, SourceLocation Location);

public sealed class RuleSet
{
    private readonly List<ICompiledRule> _rules = [];

    public RuleScope<T> For<T>(CodeQuery<T> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new RuleScope<T>(this, query);
    }

    public IReadOnlyList<RuleDiagnostic> Evaluate(IReadOnlyList<MemberReference> memberReferences) =>
        _rules
            .SelectMany(rule => rule.Evaluate(memberReferences))
            .OrderBy(diagnostic => diagnostic.Descriptor.Id, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.FilePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.Start)
            .ToArray();

    internal void Add<T>(CodeQuery<T> query, string id, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (_rules.Any(rule => StringComparer.Ordinal.Equals(rule.Id, id)))
        {
            throw new InvalidOperationException($"Rule id '{id}' is registered more than once.");
        }

        _rules.Add(new CompiledRule<T>(query, new RuleDescriptor(id, message)));
    }

    private interface ICompiledRule
    {
        string Id { get; }

        IEnumerable<RuleDiagnostic> Evaluate(IReadOnlyList<MemberReference> memberReferences);
    }

    private sealed class CompiledRule<T>(CodeQuery<T> query, RuleDescriptor descriptor) : ICompiledRule
    {
        public string Id => descriptor.Id;

        public IEnumerable<RuleDiagnostic> Evaluate(IReadOnlyList<MemberReference> memberReferences) =>
            query.Evaluate(memberReferences)
                .Select(candidate => candidate is MemberReference reference
                    ? new RuleDiagnostic(descriptor, reference.Location)
                    : throw new InvalidOperationException(
                        $"Candidate type '{typeof(T)}' does not expose a source location."));
    }
}

public sealed class RuleScope<T>(RuleSet ruleSet, CodeQuery<T> query)
{
    public RuleScope<T> Forbid(string id, string message)
    {
        ruleSet.Add(query, id, message);
        return this;
    }
}
