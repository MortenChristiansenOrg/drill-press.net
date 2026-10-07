using DrillPress;
using DrillPress.SampleRules.CodecPolicies;

namespace DrillPress.SampleRules;

/// <summary>Example policies for a deterministic text-codec library. Analysis details live in the CodecPolicies namespace.</summary>
public static class ShowcaseRules
{
    /// <summary>Creates configured codec policies. Supplying an accepted source analysis additionally enables change-aware review.</summary>
    public static RuleCatalog Create(SourceBaseline? accepted = null)
    {
        var rules = new RuleCatalog();
        Register(rules, accepted);
        return rules;
    }

    /// <summary>Registers examples scoped to projects whose names start with CodecExamples, including their test projects.</summary>
    public static void Register(RuleCatalog rules, SourceBaseline? accepted = null)
    {
        var code = new CodecSources();
        var examples = new CodecExamples(code);

        rules
            .Rule(
                "SDK2001",
                "Pass reproducible values into codecs instead of creating random values."
            )
            .For(code.Calls.To(CodecBehavior.NonRepeatableValues))
            .Forbid();

        rules
            .Rule("SDK2002", "Keep console output in the Tracing adapter.")
            .For(code.Calls.To(CodecBehavior.ConsoleWriteLine))
            .Require(CodecBehavior.IsInTracingAdapter);

        rules
            .Rule("SDK2003", "Keep blocking sleeps out of asynchronous codec call paths.")
            .For(code.AsyncMethods.WhoseCallPathsReachBlockingSleep())
            .Forbid();

        rules
            .Rule("SDK2004", "Provide an xUnit <CodecName>RoundTrip test for each text codec.")
            .For(
                code.TextCodecs.WithoutMatching(
                    examples.RoundTripTests,
                    examples.IsRoundTripTestFor
                )
            )
            .Forbid();

        rules
            .Rule("SDK2005", "Give repeated wire-format examples a shared declaration.")
            .For(examples.RepeatedStringLiterals(longerThan: 80))
            .Forbid();

        rules
            .Rule("SDK2006", "Share the repeated format-dispatch expression.")
            .For(examples.RepeatedSwitchExpressions(minimumTokens: 20))
            .Forbid();

        rules
            .Rule("SDK2007", "Keep reader and writer format inventories in agreement.")
            .For(examples.ReaderWriterInventoryMismatches)
            .ReportAt(pair => pair.Reader)
            .Forbid();

        rules
            .Rule("SDK2008", "Use the codec library's System.Text.Json serialization contract.")
            .For(code.Files.Where(CodecSources.ProjectReferencesNewtonsoftJson))
            .Forbid();

        rules
            .Rule("SDK2011", "Use the implicit assembly visibility for codec implementation types.")
            .For(code.TypeDeclarations.TopLevel().WithExplicitModifier(Modifier.Internal))
            .Forbid(fix: type => Fix.For(type).RemoveModifier(Modifier.Internal).Propose());

        rules
            .Rule(
                "SDK2012",
                "Declare an explicit text-codec contract instead of legacy binary serialization."
            )
            .For(
                code.TypeDeclarations.WithAttribute(CodeType.Named("System.SerializableAttribute"))
            )
            .Forbid();

        if (accepted is not null)
        {
            rules
                .Rule("SDK2013", "Add new codec implementations to the current format layer.")
                .For(code.NewLegacyFilesSince(accepted))
                .Forbid();
        }

        rules
            .Rule("SDK2009", "Handle a missing input before normalizing codec text.")
            .For(code.Calls.Where(CodecBehavior.TrimsPossiblyNullText))
            .Forbid();

        rules
            .Rule(
                "SDK2010",
                "Do not capture rented buffers; callbacks can outlive the array-pool lease."
            )
            .For(code.Methods.Where(CodecBehavior.CapturesRentedBuffer))
            .Forbid();
    }
}
