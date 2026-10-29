using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DrillPress.Engine;

internal sealed class GeneratorDictionaryOptions(ImmutableDictionary<string, string> values)
    : AnalyzerConfigOptions
{
    public override IEnumerable<string> Keys => values.Keys;

    public override bool TryGetValue(string key, out string value) =>
        values.TryGetValue(key, out value!);
}
