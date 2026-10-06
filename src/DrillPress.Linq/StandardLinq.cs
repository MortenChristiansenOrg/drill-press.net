using System.Collections.Frozen;

namespace DrillPress.Presets;

/// <summary>Optional maintained facts for signed Enumerable and Queryable overloads in .NET 8, 9 and 10.</summary>
public static class StandardLinq
{
    private static readonly FrozenDictionary<string, LinqCatalogueEntry> _entries =
        LinqCatalogueData.Entries.ToFrozenDictionary(entry => entry.Signature);

    /// <summary>Supported evaluated base frameworks; platform suffixes such as net10.0-windows use the same base catalogue.</summary>
    public static IReadOnlyList<string> SupportedFrameworks { get; } =
        Array.AsReadOnly(new[] { "net8.0", "net9.0", "net10.0" });

    /// <summary>Classifies an exact normalized declaration and exposes its written sequence inputs once, without guessing newer or custom APIs.</summary>
    public static LinqOperation Inspect(CodeInvocation call)
    {
        call.Source.Project.CancellationToken.ThrowIfCancellationRequested();
        if (!call.IsResolved)
            return new(LinqClassificationStatus.Unresolved, null, null, []);
        LinqSurface? surface =
            call.IsDeclaredOn(CodeType.Framework("System.Linq.Enumerable")) ? LinqSurface.Enumerable
            : call.IsDeclaredOn(CodeType.Framework("System.Linq.Queryable")) ? LinqSurface.Queryable
            : null;
        if (surface is null)
            return new(LinqClassificationStatus.NotStandardSymbol, null, null, []);
        var framework = call.Source.Project.TargetFramework.Split('-')[0];
        var version = framework switch
        {
            "net8.0" => 1,
            "net9.0" => 2,
            "net10.0" => 4,
            _ => 0,
        };
        if (version == 0)
            return new(LinqClassificationStatus.UnsupportedFramework, surface, null, []);
        if (
            !_entries.TryGetValue(LinqSignature.Of(call.Declaration), out var entry)
            || (entry.Frameworks & version) == 0
        )
            return new(LinqClassificationStatus.UnsupportedOperation, surface, null, []);
        var inputs = call.EvaluationInputs()
            .Where(input =>
                input.Parameter is { } parameter
                && entry.SequenceParameters.Contains(parameter.Ordinal)
            )
            .Select(input => new LinqSequenceInput(input.Parameter!.Name, input.Value))
            .ToArray();
        return new(
            LinqClassificationStatus.Supported,
            surface,
            entry.Category,
            Array.AsReadOnly(inputs)
        )
        {
            SequenceConsumption = entry.SequenceConsumption,
        };
    }
}
