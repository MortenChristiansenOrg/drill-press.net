using DrillPress;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DrillPress.SampleRules.CodecPolicies;

// Matches examples and inventories within the codec scope, retaining original diagnostic anchors.
internal sealed class CodecExamples
{
    private readonly CodecSources _code;
    private readonly CodeQuery<CodeNode<LiteralExpressionSyntax>> _stringLiterals;
    private readonly AnalysisFact<IReadOnlyDictionary<string, int>> _stringOccurrences;

    internal CodecExamples(CodecSources code)
    {
        _code = code;
        RoundTripTests = code.TestMethods.InTestProjects().NameMatching("*RoundTrip");
        _stringLiterals = code
            .Files.Nodes<LiteralExpressionSyntax>()
            .Where(node => node.Constant is { HasValue: true, Value: string });
        _stringOccurrences = new(solution =>
            _stringLiterals
                .In(solution)
                .GroupBy(StringValue)
                .ToDictionary(group => group.Key, group => group.Count())
        );
        ReaderWriterInventoryMismatches = FindReaderWriterInventoryMismatches();
    }

    internal CodeQuery<CodeMethod> RoundTripTests { get; }
    internal CodeQuery<(
        CodeTypeDefinition Reader,
        CodeTypeDefinition Writer
    )> ReaderWriterInventoryMismatches { get; }

    internal bool IsRoundTripTestFor(CodeTypeDefinition codec, CodeMethod example) =>
        example.Name == $"{codec.Name}RoundTrip"
        && codec.Solution.ProjectGraph.Includes(example.Source.Project, codec.Source.Project);

    internal CodeQuery<CodeNode<LiteralExpressionSyntax>> RepeatedStringLiterals(int longerThan)
    {
        var repeatedValues = _stringOccurrences.SelectMany(counts =>
            counts
                .Where(entry => entry.Key.Length > longerThan && entry.Value > 1)
                .Select(entry => entry.Key)
        );
        return _stringLiterals.Join(
            repeatedValues,
            StringValue,
            value => value,
            (literal, _) => literal
        );
    }

    internal CodeQuery<CodeNode<SwitchExpressionSyntax>> RepeatedSwitchExpressions(
        int minimumTokens
    ) => _code.Files.Nodes<SwitchExpressionSyntax>().Duplicates(minimumTokens);

    private CodeQuery<(
        CodeTypeDefinition Reader,
        CodeTypeDefinition Writer
    )> FindReaderWriterInventoryMismatches()
    {
        var readers = _code.Types.Named("ReaderFormats");
        var writers = _code.Types.Named("WriterFormats");
        return readers
            .Join(
                writers,
                reader => reader.Source.Project,
                writer => writer.Source.Project,
                (reader, writer) => (Reader: reader, Writer: writer)
            )
            .Where(pair => !HaveTheSameFormats(pair.Reader, pair.Writer));
    }

    private static bool HaveTheSameFormats(CodeTypeDefinition reader, CodeTypeDefinition writer) =>
        new SetComparison<string>(DeclaredFormats(reader), DeclaredFormats(writer)).AreEqual;

    private static IEnumerable<string> DeclaredFormats(CodeTypeDefinition declaration) =>
        declaration
            .Symbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(field => field.HasConstantValue)
            .Select(field => field.ConstantValue)
            .OfType<string>();

    // Only the resolved string-literal selection calls this conversion.
    private static string StringValue(CodeNode<LiteralExpressionSyntax> literal) =>
        (string)literal.Constant.Value!;
}
