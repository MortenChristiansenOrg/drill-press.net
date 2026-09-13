using DrillPress.Baselines;
using DrillPress.SampleRules.CodecPolicies;

namespace DrillPress.SampleRules;

/// <summary>Example policies for a deterministic text-codec library. Analysis details live in the CodecPolicies namespace.</summary>
public static class ShowcaseRules
{
    /// <summary>Creates configured codec policies. Supplying an accepted source analysis additionally enables change-aware review.</summary>
    public static RuleSet Create(SourceBaseline? accepted = null)
    {
        var rules = new RuleSet();
        Register(rules, accepted);
        return rules;
    }

    /// <summary>Registers examples scoped to projects whose names start with CodecExamples, including their test projects.</summary>
    public static void Register(RuleSet rules, SourceBaseline? accepted = null)
    {
        var code = new CodecSources();
        var examples = new CodecExamples(code);

        rules
            .For(code.Calls.Where(CodecBehavior.CreatesNonRepeatableValues))
            .Forbid(
                "SDK2001",
                "Pass reproducible values into codecs instead of creating random values."
            );

        rules
            .For(code.Calls.Where(CodecBehavior.WritesToConsole))
            .Require(
                CodecBehavior.IsInTracingAdapter,
                "SDK2002",
                "Keep console output in the Tracing adapter."
            );

        rules
            .For(code.AsyncMethods.WhoseCallPathsReachBlockingSleep())
            .Forbid("SDK2003", "Keep blocking sleeps out of asynchronous codec call paths.");

        rules
            .For(
                code.TextCodecs.WithoutMatching(
                    examples.RoundTripTests,
                    examples.IsRoundTripTestFor
                )
            )
            .Forbid("SDK2004", "Provide an xUnit <CodecName>RoundTrip test for each text codec.");

        rules
            .For(examples.RepeatedStringLiterals(longerThan: 80))
            .Forbid("SDK2005", "Give repeated wire-format examples a shared declaration.");

        rules
            .For(examples.RepeatedSwitchExpressions(minimumTokens: 20))
            .Forbid("SDK2006", "Share the repeated format-dispatch expression.");

        rules
            .For(examples.ReaderWriterInventoryMismatches)
            .Forbid("SDK2007", "Keep reader and writer format inventories in agreement.");

        rules
            .For(code.Files.Where(CodecSources.ProjectReferencesNewtonsoftJson))
            .Forbid("SDK2008", "Use the codec library's System.Text.Json serialization contract.");

        rules
            .For(code.TopLevelTypes.Where(CodecSources.DeclaresInternalAccessibility))
            .Forbid(
                "SDK2011",
                "Use the implicit assembly visibility for codec implementation types.",
                fix: ModifierFix.RemoveRedundantAccessibility
            );

        rules
            .For(code.TypeDeclarations.Where(CodecSources.HasSerializableAttribute))
            .Forbid(
                "SDK2012",
                "Declare an explicit text-codec contract instead of legacy binary serialization."
            );

        if (accepted is not null)
        {
            rules
                .For(code.NewLegacyFilesSince(accepted))
                .Forbid("SDK2013", "Add new codec implementations to the current format layer.");
        }

        rules
            .For(code.Calls.Where(CodecBehavior.TrimsPossiblyNullText))
            .Forbid("SDK2009", "Handle a missing input before normalizing codec text.");

        rules
            .For(code.Methods.Where(CodecBehavior.CapturesRentedBuffer))
            .Forbid(
                "SDK2010",
                "Do not capture rented buffers; callbacks can outlive the array-pool lease."
            );
    }
}
