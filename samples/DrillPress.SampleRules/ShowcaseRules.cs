using DrillPress.Baselines;
using DrillPress.Collections;
using DrillPress.Configuration;
using DrillPress.Facts;
using DrillPress.Flow;
using DrillPress.Operations;
using DrillPress.Projects;
using DrillPress.Queries;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.SampleRules;

/// <summary>Independent example policies for a deterministic text-codec library. The SDK surface, rather than the five preview rules, is the focus.</summary>
public static class ShowcaseRules
{
    /// <summary>Creates configured codec policies. Supplying an accepted source analysis additionally enables change-aware review.</summary>
    public static RuleSet Create(SourceBaseline? accepted = null)
    {
        var rules = new RuleSet();
        Register(rules, accepted);
        return rules;
    }

    /// <summary>Registers examples scoped to projects named CodecExamples, keeping repository-specific policy explicit.</summary>
    public static void Register(RuleSet rules, SourceBaseline? accepted = null)
    {
        var files = Sources.Files.Where(file => file.Source.Project.Name.StartsWith("CodecExamples", StringComparison.Ordinal));
        var calls = OperationQueries.InvocationsIn(files);
        var methods = Code.Methods.Where(method => method.Source.Project.Name.StartsWith("CodecExamples", StringComparison.Ordinal));
        var types = Code.Types.Where(type => type.Source.Project.Name.StartsWith("CodecExamples", StringComparison.Ordinal));
        RegisterCallPolicies(rules, calls, methods);
        RegisterRoundTripCoverage(rules, types, methods);
        RegisterSharedExamples(rules, files);
        RegisterFormatInventories(rules, types);
        RegisterSourcePolicies(rules, files, accepted);
        RegisterFlowPolicies(rules, calls, methods);
    }

    private static void RegisterCallPolicies(RuleSet rules, CodeQuery<CodeInvocation> calls, CodeQuery<CodeMethod> methods)
    {
        var nonRepeatable = new ApiSet(new(CodeType.Of<Guid>(), nameof(Guid.NewGuid)),
            new(CodeType.Of<Random>(), nameof(Random.Next)));
        var tracingFiles = new PathPattern("**/Tracing/*.cs");

        rules.For(calls.Where(call => nonRepeatable.Contains(call.Target)))
            .Forbid("SDK2001", "Pass reproducible values into codecs instead of creating random values.");
        rules.For(calls.Where(call => call.Calls(new(CodeType.Named("System.Console"), "WriteLine"))))
            .Require(call => tracingFiles.Matches(call.Source.Document.Path), "SDK2002", "Keep console output in the Tracing adapter.");

        var blockingSleep = new CodeMember(CodeType.Named("System.Threading.Thread"), "Sleep");
        var blockingMethods = CodeQuery<CodeMethod>.Create(solution => methods.In(solution).Where(method =>
            method.Symbol is { IsAsync: true } symbol && CodeRelationships.In(solution).Reaches(symbol, blockingSleep)));
        rules.For(blockingMethods).Forbid("SDK2003", "Keep blocking sleeps out of asynchronous codec call paths.");
    }

    private static void RegisterRoundTripCoverage(RuleSet rules, CodeQuery<CodeDeclaration> types, CodeQuery<CodeMethod> methods)
    {
        var graphs = new AnalysisFact<ProjectGraph>(solution => new(solution));
        var codecs = types.Where(type => type.Implements(CodeType.Named("CodecExamples.ITextCodec")));
        var roundTrips = methods.Where(method => method.Source.Project.IsTestProject && method.Symbol?.Name.EndsWith("RoundTrip", StringComparison.Ordinal) == true);
        rules.For(codecs.WithoutMatching(roundTrips, (codec, method) => method.Name == codec.Name + "RoundTrip" &&
                graphs.In(codec.Solution).Includes(method.Source.Project, codec.Source.Project)))
            .Forbid("SDK2004", "Provide a named round-trip example for each text codec.");
    }

    private static void RegisterSharedExamples(RuleSet rules, CodeQuery<CodeFile> files)
    {
        var literalLengths = new AnalysisFact<IReadOnlyDictionary<string, int>>(solution => files.In(solution)
            .SelectMany(file => file.Nodes<LiteralExpressionSyntax>()).Where(node => node.Constant is { HasValue: true, Value: string })
            .GroupBy(node => (string)node.Constant.Value!).ToDictionary(group => group.Key, group => group.Count()));
        var repeatedLargeLiterals = literalLengths.SelectMany(counts => counts.Where(pair => pair.Key.Length > 80 && pair.Value > 1));
        var repeatedText = files.SelectMany(file => file.Nodes<LiteralExpressionSyntax>())
            .Join(repeatedLargeLiterals, node => node.Constant.Value as string ?? "", pair => pair.Key, (node, _) => node);
        rules.For(repeatedText).Forbid("SDK2005", "Give repeated wire-format examples a shared declaration.");

        rules.For(DuplicateSyntax.In(files.SelectMany(file => file.Nodes<SwitchExpressionSyntax>()), minimumTokens: 20))
            .Forbid("SDK2006", "Share the repeated format-dispatch expression.");
    }

    private static void RegisterFormatInventories(RuleSet rules, CodeQuery<CodeDeclaration> types)
    {
        var formatCatalogs = types.Where(type => type.Name is "ReaderFormats" or "WriterFormats");
        var readerWriterPairs = formatCatalogs.Where(type => type.Name == "ReaderFormats")
            .Join(formatCatalogs.Where(type => type.Name == "WriterFormats"),
                type => type.Source.Project, type => type.Source.Project, (reader, writer) => (Reader: reader, Writer: writer));
        var inconsistentCatalogs = readerWriterPairs.Where(pair => !new SetComparison<string>(Formats(pair.Reader), Formats(pair.Writer)).AreEqual)
            .At(pair => pair.Reader);
        rules.For(inconsistentCatalogs).Forbid("SDK2007", "Keep reader and writer format inventories in agreement.");
    }

    private static void RegisterFlowPolicies(RuleSet rules, CodeQuery<CodeInvocation> calls, CodeQuery<CodeMethod> methods)
    {
        var trimCalls = calls.Where(call => call.Calls(new(CodeType.Of<string>(), nameof(string.Trim), [])));
        rules.For(trimCalls.Where(call => call.Operation.Instance is { } receiver && call.Source.Model.GetTypeInfo(receiver.Syntax)
                .Nullability.FlowState == NullableFlowState.MaybeNull))
            .Forbid("SDK2009", "Handle a missing input before normalizing codec text.");

        var capturedBuffers = CodeQuery<CodeMethod>.Create(solution => methods.In(solution).Where(method =>
            MethodFlow.For(solution, method).Data is { Succeeded: true } data && data.Captured.Any(symbol => symbol.Name == "scratchBuffer")));
        rules.For(capturedBuffers).Forbid("SDK2010", "Keep scratch buffers local to one codec invocation.");
    }

    private static void RegisterSourcePolicies(RuleSet rules, CodeQuery<CodeFile> files, SourceBaseline? accepted)
    {
        rules.For(files.Where(file => file.Source.Project.ReferencesPackage("Newtonsoft.Json")))
            .Forbid("SDK2008", "Use the codec library's System.Text.Json serialization contract.");

        var declarations = files.SelectMany(file => file.Nodes<MemberDeclarationSyntax>())
            .Where(node => node.Syntax is TypeDeclarationSyntax { Parent: CompilationUnitSyntax or BaseNamespaceDeclarationSyntax } type &&
                type.Modifiers.Any(SyntaxKind.InternalKeyword));
        rules.For(declarations).Forbid("SDK2011", "Use the implicit assembly visibility for codec implementation types.",
            fix: ModifierFix.RemoveRedundantAccessibility);

        rules.For(SymbolQueries.DeclarationsIn(files).Where(symbol =>
                symbol.Symbol is INamedTypeSymbol type && Symbols.HasAttribute(type, CodeType.Named("System.SerializableAttribute"))))
            .Forbid("SDK2012", "Declare an explicit text-codec contract instead of legacy binary serialization.");

        if (accepted is not null)
        {
            rules.For(files.Where(file => accepted.ChangeOf(file) == SourceChange.Added && file.Name.EndsWith(".Legacy.cs", StringComparison.Ordinal)))
                .Forbid("SDK2013", "Add new codec implementations to the current format layer.");
        }
    }

    private static IEnumerable<string> Formats(CodeDeclaration declaration) => declaration.Symbol.GetMembers().OfType<IFieldSymbol>()
        .Where(field => field.HasConstantValue).Select(field => field.ConstantValue).OfType<string>();
}
