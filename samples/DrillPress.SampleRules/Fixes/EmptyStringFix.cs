namespace DrillPress.SampleRules.Fixes;

/// <summary>Supplies the framework-specific equivalence; the SDK validates source, surrounding binding and compiler-supplied arguments.</summary>
internal static class EmptyStringFix
{
    public static FixProposal? Create(MemberReference reference) =>
        Fix.For(reference)
            .ReplaceWith(Code.Literal(""))
            .SafeWhen(change =>
                change.Expressions is { } pair
                && pair.Before.RefersTo(CodeType.Of<string>().Member(nameof(string.Empty)))
                && pair.After.IsConstant("")
            );
}
