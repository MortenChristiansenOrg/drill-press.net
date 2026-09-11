using System.Text.RegularExpressions;

namespace DrillPress.BundleVerification;

public static partial class PublishWarnings
{
    // Roslyn 5.9.0: GetAssemblyLocation handles empty locations; pooled callbacks
    // use supplied factories. The import-tracking constructor is exercised by
    // the alias golden. No whole-assembly roots or category-wide suppressions.
    // Revisit these exact upstream origins when Roslyn or engine paths change.
    private static readonly HashSet<(string Code, string Origin)> _known =
    [
        ("IL3000", "Microsoft.CodeAnalysis.CommonCompiler.GetAssemblyLocation(Type)"),
        (
            "IL2091",
            "Microsoft.CodeAnalysis.PooledObjects.PooledDelegates.GetPooledCreateValueCallback<TKey,TArg,TValue>(Func`3<TKey,TArg,TValue>,TArg,ConditionalWeakTable`2.CreateValueCallback<!!0,!!2>&)"
        ),
        (
            "IL2091",
            "Microsoft.CodeAnalysis.PooledObjects.PooledDelegates.CreateValueCallbackWithBoundArgument`3"
        ),
        (
            "IL2091",
            "Microsoft.CodeAnalysis.PooledObjects.PooledDelegates.CreateValueCallbackWithBoundArgument`3.CreateValueCallbackWithBoundArgument`3()"
        ),
        (
            "IL2091",
            "Microsoft.CodeAnalysis.PooledObjects.PooledDelegates.CreateValueCallbackWithBoundArgument`3.Bind()"
        ),
        ("IL2091", "Roslyn.Utilities.RoslynLazyInitializer.EnsureInitialized<T>(!!0&)"),
    ];

    public static void Validate(string output)
    {
        foreach (Match match in WarningPattern().Matches(output))
        {
            var origin = match.Groups[2].Value.Split(": ", 2)[0];
            if (!_known.Contains((match.Groups[1].Value, origin)))
            {
                throw new InvalidOperationException(
                    $"Unexplained publication warning: {match.Value}"
                );
            }
        }
    }

    [GeneratedRegex(@"\bwarning ([A-Z]+\d+): ([^\r\n]*)")]
    private static partial Regex WarningPattern();
}
