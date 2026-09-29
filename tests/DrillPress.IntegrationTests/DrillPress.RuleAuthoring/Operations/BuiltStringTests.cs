using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Operations;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class BuiltStringTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Constant_holes_preserve_provenance_and_url_policy_uses_ordered_suffixes()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Strings",
            [
                new(
                    "Strings.cs",
                    """
                    using System;
                    class C {
                        const string Url = "/api";
                        static void Send(string url) {}
                        static void Send(Uri url) {}
                        void M(Uri baseUri, string relativePath) {
                            Send($"{Url}/details"); Send(Url + "/details");
                            Send($"{Url}?next=/details"); Send(Url + "?page=2");
                            Send(new Uri(baseUri, relativePath)); Send(new Uri(baseUri, "/details"));
                            Send(new Uri("/details", UriKind.Relative));
                        }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var urls = OperationQueries
            .Invocations.Calling(CodeType.Named("C").Member("Send"))
            .ArgumentsFor("url")
            .SourceValues();

        var results = urls.In(solution)
            .Select(url =>
                (
                    url.ConstructorArgument("relativeUri")
                    ?? url.ConstructorArgument("uriString")
                    ?? url
                ).AsBuiltString()
            )
            .Select(text =>
                text?.LiteralParts.Any(part => part.Length > 0 && part[0] is not ('?' or '&'))
                == true
            )
            .ToArray();
        var first = urls.In(solution)[0];
        var parts = first
            .AsBuiltString()!
            .Parts.Select(part => $"{part.Kind}:{part.Text}:{part.Value?.Syntax}")
            .ToArray();
        var expanded = first.AsBuiltString(expandConstants: true)!.LiteralParts.ToArray();

        Assert.Equal([true, true, false, false, false, true, true], results);
        Assert.Equal(["Hole::Url", "Literal:/details:"], parts);
        Assert.Equal(["/api", "/details"], expanded);
    }

    [Fact]
    public void Formatting_is_exposed_and_user_defined_concatenation_stays_opaque()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "Strings",
            [
                new(
                    "Strings.cs",
                    """
                    class Weird { public static string operator +(Weird a, string b) => "changed"; }
                    class C {
                        static void Send(string value) {}
                        void M(int number, Weird value) { Send($"before{number,8:X2}after"); Send(value + "tail"); }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var texts = OperationQueries
            .Invocations.Calling(CodeType.Named("C").Member("Send"))
            .ArgumentsFor("value")
            .SourceValues()
            .In(solution)
            .Select(value => value.AsBuiltString()!)
            .ToArray();
        var parts = texts[0]
            .Parts.Select(part =>
                $"{part.Kind}:{part.Text}:{part.Value?.Syntax}:{part.Alignment?.Syntax}:{part.Format}"
            )
            .ToArray();
        var opaque = texts[1].Parts.Select(part => $"{part.Kind}:{part.Value?.Syntax}").ToArray();

        Assert.Equal(["Literal:before:::", "Hole::number:8:X2", "Literal:after:::"], parts);
        Assert.Equal(["Hole:value + \"tail\""], opaque);
    }
}
