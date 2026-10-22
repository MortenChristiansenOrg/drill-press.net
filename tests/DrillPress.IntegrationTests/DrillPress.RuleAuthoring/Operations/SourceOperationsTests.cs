using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class SourceOperationsTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    // Each construct has its own document, so a document whose written names omit a compiler-implied
    // call name would be skipped by the name filter and its call lost.
    private static readonly TestSource[] _constructs =
    [
        new(
            "Support.cs",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            public class Base { public Base() { } public Base(int value) { } }
            public class Bag : IEnumerable<int>
            {
                public void Add(int value) { }
                public IEnumerator<int> GetEnumerator() => throw new NotImplementedException();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            public class Holder { public Bag Items { get; } = new(); }
            public class Pair
            {
                public void Deconstruct(out int first, out int second) => (first, second) = (1, 2);
            }
            public class Loop { public Cursor GetEnumerator() => new(); }
            public class Cursor { public int Current => 0; public bool MoveNext() => false; }
            public class Resource { public void Dispose() { } }
            public struct Awaitable { public Awaiter GetAwaiter() => new(); }
            public struct Awaiter : INotifyCompletion
            {
                public bool IsCompleted => true;
                public int GetResult() => 0;
                public void OnCompleted(Action continuation) { }
            }
            public class Sliced { public int Length => 0; public int this[int index] => 0; public Sliced Slice(int start, int length) => this; }
            [InterpolatedStringHandler]
            public ref struct Handler
            {
                public Handler(int literalLength, int formattedCount) { }
                public void AppendLiteral(string value) { }
                public void AppendFormatted<T>(T value) { }
            }
            public static class Sink { public static void Write(Handler handler) { } }
            [CollectionBuilder(typeof(Built), nameof(Built.Make))]
            public class Built : IEnumerable<int>
            {
                public static Built Make(ReadOnlySpan<int> values) => new();
                public IEnumerator<int> GetEnumerator() => throw new NotImplementedException();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            public static class Extensions { public static int Twice(this int value) => value * 2; }
            public class Source<T>
            {
                public Source<T> Where(Func<T, bool> predicate) => this;
                public Source<TResult> Select<TResult>(Func<T, TResult> selector) => new();
            }
            public class Published { public event Action? Changed; public void Raise() => Changed?.Invoke(); }
            """
        ),
        new("ExplicitBase.cs", "class ExplicitBase : Base { ExplicitBase() : base(1) { } }"),
        new(
            "ExplicitThis.cs",
            "class ExplicitThis : Base { ExplicitThis() : this(1) { } ExplicitThis(int value) { } }"
        ),
        new("ImplicitBase.cs", "class ImplicitBase : Base { ImplicitBase(int value) { } }"),
        new("PrimaryBase.cs", "class PrimaryBase(int value) : Base(value) { }"),
        new(
            "CollectionInitializer.cs",
            "class CollectionInitializer { object M() => new Bag { 1, 2 }; }"
        ),
        new(
            "NestedInitializer.cs",
            "class NestedInitializer { object M() => new Holder { Items = { 3 } }; }"
        ),
        new(
            "DictionaryInitializer.cs",
            "class DictionaryInitializer { object M() => new System.Collections.Generic.Dictionary<int, int> { { 1, 2 } }; }"
        ),
        new(
            "QuerySyntax.cs",
            """
            using System.Linq;
            class QuerySyntax
            {
                object M(int[] values, int[] others) =>
                    from int value in values
                    join other in others on value equals other
                    let doubled = value * 2
                    where doubled > 1
                    from extra in others
                    orderby value, extra descending
                    group extra by value;
                object Custom(Source<int> source) => from value in source where value > 0 select value;
                object Into(int[] values, int[] others) =>
                    from value in values
                    join other in others on value equals other into matches
                    orderby value descending
                    select matches.Count();
            }
            """
        ),
        new(
            "Interpolation.cs",
            "class Interpolation { void M(int value) => Sink.Write($\"value {value}\"); }"
        ),
        new("DelegateCall.cs", "class DelegateCall { int M(System.Func<int> f) => f(); }"),
        new(
            "EventRaise.cs",
            "class EventRaise { void M(Published published) => published.Raise(); }"
        ),
        new("ForEach.cs", "class ForEach { void M(Loop loop) { foreach (var item in loop) { } } }"),
        new(
            "Deconstruction.cs",
            "class Deconstruction { int M(Pair pair) { var (first, second) = pair; return pair is (1, _) ? first : second; } }"
        ),
        new("Using.cs", "class Using { void M() { using var resource = new Resource(); } }"),
        new(
            "Await.cs",
            "class Await { async System.Threading.Tasks.Task<int> M(Awaitable awaitable) => await awaitable; }"
        ),
        new("Ranges.cs", "class Ranges { object M(Sliced sliced) => (sliced[^1], sliced[1..2]); }"),
        new(
            "CollectionExpressions.cs",
            "class CollectionExpressions { object M() => ((Built)[1, 2], (System.Collections.Generic.List<int>)[3], (Bag)[4]); }"
        ),
        new(
            "Imports.cs",
            """
            using static System.Math;
            using Mathematics = System.Math;
            class Imports { int M() => Max(1, 2) + Mathematics.Min(1, 2); }
            """
        ),
        new(
            "ExtensionSpellings.cs",
            "class ExtensionSpellings { int M() => 1.Twice() + Extensions.Twice(2); }"
        ),
        new("Escapes.cs", "class Escapes { void Run() { } void M() { @Run(); R\\u0075n(); } }"),
        new(
            "Members.cs",
            """
            class Members
            {
                int _field = Twice(1);
                int Property => Twice(2);
                int Accessor { get { return Twice(3); } }
                Members() => Local();
                void Local() { int Inner() => Twice(4); System.Func<int> lambda = () => Twice(5); Inner(); lambda(); }
                static int Twice(int value) => value.Twice();
                T Generic<T>() => default!;
                int Conditional(Members? other) => other?.Generic<int>() ?? 0;
            }
            """
        ),
        new(
            "Unresolved.cs",
            "class Unresolved { void Send(int value) { } void M() { Send(1); int unassigned; Send(unassigned); } void N() => Send(2); }"
        ),
    ];

    [Fact]
    public void Name_filtered_calls_match_exhaustive_discovery_for_every_target_name()
    {
        var optimized = Analyze(new AnalysisOptions());
        var exhaustive = Analyze(new AnalysisOptions { EnableOptimizations = false });
        var names = Code
            .Calls.In(exhaustive)
            .Select(call => call.Target.Name)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

        var mismatches = names
            .Where(name =>
                !Describe(Code.Calls.ToMethodsNamed(name), optimized)
                    .SequenceEqual(Describe(Code.Calls.ToMethodsNamed(name), exhaustive))
            )
            .ToArray();

        Assert.Empty(mismatches);
        Assert.Equal(
            [
                ".ctor",
                "Add",
                "AppendFormatted",
                "AppendLiteral",
                "Cast",
                "Count",
                "Generic",
                "GetEnumerator",
                "GroupBy",
                "GroupJoin",
                "Inner",
                "Invoke",
                "Join",
                "Local",
                "Max",
                "Min",
                "OrderBy",
                "OrderByDescending",
                "Raise",
                "Run",
                "Select",
                "SelectMany",
                "Send",
                "ThenByDescending",
                "Twice",
                "Where",
                "Write",
            ],
            names
        );
    }

    [Fact]
    public void Configured_members_globs_and_api_sets_match_exhaustive_discovery()
    {
        var optimized = Analyze(new AnalysisOptions());
        var exhaustive = Analyze(new AnalysisOptions { EnableOptimizations = false });
        CodeQuery<CodeInvocation>[] queries =
        [
            Code.Calls.To(CodeType.Named("Base").Member(".ctor")),
            Code.Calls.To(CodeType.Named("Bag").Member("Add")),
            Code.Calls.To(CodeType.Named("Extensions").Member("Twice")),
            Code.Calls.To(CodeType.Named("System.Math").Member("Max")),
            Code.Calls.To(CodeType.Named("System.Linq.Enumerable").Member("GroupJoin")),
            Code.Calls.To(
                new ApiSet(
                    CodeType.Named("Handler").Member("AppendLiteral"),
                    CodeType.Named("Escapes").Member("Run")
                )
            ),
            Code.Calls.ToMethodsMatching("Append*"),
            Code.Calls.ToMethodsMatching("?nvoke"),
            Code.Calls.ToMethodsMatching("*"),
            Code.Calls.ToMethodsMatching("Then*").Where(call => call.Arguments.Count > 0),
            Code.Calls.ToMethodsNamed("Select").Union(Code.Calls.ToMethodsMatching("Order*")),
            Code.Calls.ToMethodsNamed("Max").ExceptWhen(call => call.Arguments.Count == 0),
            Code.Calls.Where(call => call.Target.Name == "Add"),
        ];

        var actual = queries.Select(query => Describe(query, optimized)).ToArray();

        Assert.Equal(queries.Select(query => Describe(query, exhaustive)), actual);
        Assert.All(actual.Take(9), calls => Assert.NotEmpty(calls));
    }

    [Fact]
    public void Resolution_agrees_with_the_compiler_errors_in_each_call()
    {
        var solution = Analyze(new AnalysisOptions());
        var model = Analyze(new AnalysisOptions()).Projects[0].Compilation;

        var actual = Code
            .Calls.In(solution)
            .Select(call => (call.Operation.Syntax.ToString(), call.IsResolved))
            .ToArray();

        Assert.Equal(
            Code.Calls.In(solution)
                .Select(call =>
                    (
                        call.Operation.Syntax.ToString(),
                        call.Target.ContainingType.TypeKind != TypeKind.Error
                            && !model
                                .GetSemanticModel(
                                    model.SyntaxTrees.Single(tree =>
                                        tree.FilePath == call.Source.Tree.FilePath
                                    )
                                )
                                .GetDiagnostics(call.Operation.Syntax.Span)
                                .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    )
                ),
            actual
        );
        Assert.Equal(
            [("Send(1)", true), ("Send(unassigned)", false), ("Send(2)", true)],
            actual.Where(call => call.Item1.StartsWith("Send(", StringComparison.Ordinal))
        );
    }

    private AnalysisSolution Analyze(AnalysisOptions options)
    {
        var workspace = fixture.Workspace();
        var project = workspace.AddProject("Constructs", _constructs, allowErrors: true);
        return new([project], options, TestContext.Current.CancellationToken);
    }

    private static string[] Describe(CodeQuery<CodeInvocation> query, AnalysisSolution solution) =>
        query
            .In(solution)
            .Select(call =>
                $"{call.Source.Document.Path}:{call.Operation.Syntax.Span}:{call.Target.ToDisplayString()}:{call.IsResolved}"
            )
            .ToArray();
}
