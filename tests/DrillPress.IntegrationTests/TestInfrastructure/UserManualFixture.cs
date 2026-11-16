using System.Net;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DrillPress.IntegrationTests.TestInfrastructure;

public sealed class UserManualFixture : IntegrationTest
{
    private readonly MetadataReference[] _references = new RuleTestWorkspace()
        .AddProject("References", [new("References.cs", "class ReferenceAnchor { }")])
        .Compilation.References.ToArray();

    public static TheoryData<string, string, string> ReadExamples()
    {
        var examples = new TheoryData<string, string, string>();
        foreach (var (name, kind, code) in Examples())
            examples.Add(name, kind, code);
        return examples;
    }

    public string[] Compile(string name, string kind, string code) =>
        Compilation(name, kind, code)
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();

    // Runs a library example's rule factory, so the manual's code is exercised as written.
    public RuleCatalog CreateRules(string name, string factoryType)
    {
        var (_, kind, code) = Examples().Single(example => example.Name == name);
        using var image = new MemoryStream();
        var emitted = Compilation(name, kind, code)
            .Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join('\n', emitted.Diagnostics));
        image.Position = 0;
        var assembly = new AssemblyLoadContext(name).LoadFromStream(image);
        return (RuleCatalog)
            assembly
                .GetType(factoryType, throwOnError: true)!
                .GetMethod("Create")!
                .Invoke(null, null)!;
    }

    private CSharpCompilation Compilation(string name, string kind, string code)
    {
        var options = new CSharpParseOptions(LanguageVersion.CSharp15);
        return CSharpCompilation.Create(
            "ManualExample",
            [
                CSharpSyntaxTree.ParseText(
                    """
                    global using System;
                    global using System.Collections.Generic;
                    global using System.IO;
                    global using System.Linq;
                    global using System.Threading;
                    global using System.Threading.Tasks;
                    """,
                    options
                ),
                // The .NET 10 test host lacks the runtime types C# 15 unions need; the manual shows this polyfill.
                CSharpSyntaxTree.ParseText(
                    """
                    namespace System.Runtime.CompilerServices
                    {
                        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
                        public sealed class UnionAttribute : Attribute;

                        public interface IUnion
                        {
                            object? Value { get; }
                        }
                    }
                    """,
                    options
                ),
                CSharpSyntaxTree.ParseText(code, options, name),
            ],
            _references,
            new CSharpCompilationOptions(
                kind == "program"
                    ? OutputKind.ConsoleApplication
                    : OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
    }

    public string[] BrokenLinks()
    {
        var broken = new List<string>();
        foreach (var path in Pages())
        {
            var html = FileSystem.File.ReadAllText(path);
            foreach (Match match in Regex.Matches(html, """(?:href|src)="([^"]+)"""))
            {
                var link = WebUtility.HtmlDecode(match.Groups[1].Value);
                if (Uri.TryCreate(link, UriKind.Absolute, out _))
                    continue;
                var parts = link.Split('#', 2);
                var destination = FileSystem.Path.GetFullPath(
                    FileSystem.Path.Combine(FileSystem.Path.GetDirectoryName(path)!, parts[0])
                );
                if (parts[0].Length == 0)
                    destination = path;
                if (!FileSystem.File.Exists(destination))
                {
                    broken.Add(path + " -> " + link);
                }
                else if (
                    parts.Length == 2
                    && !FileSystem.File.ReadAllText(destination).Contains("id=\"" + parts[1] + "\"")
                )
                {
                    broken.Add(path + " -> missing anchor " + link);
                }
            }
        }
        return broken.ToArray();
    }

    private static IEnumerable<(string Name, string Kind, string Code)> Examples() =>
        Pages()
            .SelectMany(path =>
                Regex
                    .Matches(
                        FileSystem.File.ReadAllText(path),
                        """<pre[^>]*data-check="(program|library)"[^>]*id="([^"]+)"[^>]*><code>([\s\S]*?)</code></pre>"""
                    )
                    .Select(match =>
                        (
                            FileSystem.Path.GetFileName(path) + "#" + match.Groups[2].Value,
                            match.Groups[1].Value,
                            WebUtility.HtmlDecode(match.Groups[3].Value)
                        )
                    )
            );

    private static IEnumerable<string> Pages() =>
        FileSystem.Directory.EnumerateFiles(RepositoryPath("user-docs"), "*.html").Order();
}
