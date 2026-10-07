namespace DrillPress.SampleRules.Fixes;

/// <summary>Supplies the framework-specific equivalence; the SDK validates source, surrounding binding and compiler-supplied arguments.</summary>
internal static class EmptyStringFix
{
    private static readonly CodeMember _empty = CodeType.Of<string>().Member(nameof(string.Empty));

    public static FixProposal? Create(MemberReference reference) =>
        Fix.For(reference)
            .ReplaceWithLiteral("")
            .SafeWhen(change => change.Before.RefersTo(_empty) && change.After.Is(""));
}
