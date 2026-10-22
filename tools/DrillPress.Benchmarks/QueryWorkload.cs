using System.Text;
using DrillPress.Testing;

namespace DrillPress.Benchmarks;

/// <summary>A synthetic service-style solution and rule catalogs of typical convention rules, for measuring how query cost scales with rule count.</summary>
public static class QueryWorkload
{
    private static readonly string[] _templates =
    [
        "call",
        "call",
        "call",
        "glob",
        "reference",
        "creation",
        "type",
        "comment",
        "catch",
        "scope",
        "call",
        "method",
    ];

    /// <summary>Adds one project of <paramref name="files"/> documents to a fresh workspace.</summary>
    public static AnalysisProject Project(RuleTestWorkspace workspace, int files) =>
        workspace.AddProject(
            "Workload",
            [
                new("Shared.cs", Shared),
                .. Enumerable
                    .Range(0, files)
                    .Select(index => new TestSource($"Service{index}.cs", Service(index))),
            ]
        );

    /// <summary>Creates <paramref name="count"/> distinct rules cycling through typical rule shapes; only call rules name members.</summary>
    public static RuleCatalog Rules(int count)
    {
        var rules = new RuleCatalog();
        for (var index = 0; index < count; index++)
        {
            var rule = rules.Rule($"Q{index:D3}", $"Rule {index}.");
            var number = index;
            _ = _templates[index % _templates.Length] switch
            {
                "call" => rule.For(
                        Code.Calls.To(
                            CodeType.Named("Workload.Shared").Member($"Operation{number}")
                        )
                    )
                    .Forbid(),
                "glob" => rule.For(Code.Calls.ToMethodsMatching($"Operation{number}*")).Forbid(),
                "reference" => rule.For(
                        CodeType.Named("Workload.Shared").Member($"Setting{number % 8}").References
                    )
                    .Forbid(),
                "creation" => rule.For(
                        Code.ObjectCreations.Of(CodeType.Named($"Workload.Payload{number % 8}"))
                    )
                    .Forbid(),
                "type" => rule.For(CodeType.Named($"Workload.Payload{number % 8}").References)
                    .Forbid(),
                "comment" => rule.For(
                        Code.Comments.Where(comment =>
                            comment.Text.Contains($"NOTE{number}", StringComparison.Ordinal)
                        )
                    )
                    .Forbid(),
                "catch" => rule.For(Code.Catches.Where(@catch => @catch.CatchesAnyException))
                    .Forbid(),
                "scope" => rule.For(
                        Code.Calls.ToMethodsNamed($"Operation{number}")
                            .Where(call => call.IsInProject("Work*"))
                    )
                    .Forbid(),
                _ => rule.For(Code.Methods.Where(method => method.Name.EndsWith($"{number}")))
                    .Forbid(),
            };
        }

        return rules;
    }

    private static string Shared
    {
        get
        {
            var text = new StringBuilder("namespace Workload;\npublic static class Shared\n{\n");
            for (var index = 0; index < 8; index++)
                text.AppendLine($"    public static int Setting{index} {{ get; set; }}");
            for (var index = 0; index < 64; index++)
                text.AppendLine(
                    $"    public static int Operation{index}(int value) => value + {index};"
                );
            text.AppendLine("}");
            for (var index = 0; index < 8; index++)
                text.AppendLine($"public sealed record Payload{index}(int Value);");
            return text.ToString();
        }
    }

    // Each service calls a few shared operations, so a named rule binds only some documents.
    private static string Service(int index) =>
        $$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            namespace Workload;
            public sealed class Service{{index}}
            {
                private readonly List<int> _values = new() { {{index}}, {{index + 1}} };

                // NOTE{{index % 64}}: synthetic service.
                public int Run{{index}}(IEnumerable<int> input)
                {
                    var total = Shared.Operation{{index % 64}}(Shared.Setting{{index % 8}});
                    foreach (var value in input.Where(value => value > {{index}}).Select(value => value * 2))
                        total += Shared.Operation{{(index * 7) % 64}}(value);
                    try
                    {
                        var payload = new Payload{{index % 8}}(total);
                        total += payload.Value + _values.Sum();
                    }
                    catch (Exception)
                    {
                        total = 0;
                    }
                    return string.Join(",", _values.Select(value => $"{value}:{total}")).Length;
                }
            }
            """;
}
