using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class EndpointRoutesTests(SdkFixture fixture) : IClassFixture<SdkFixture>
{
    [Fact]
    public void Routes_read_constant_group_paths_handlers_and_bound_configuration()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "A.cs",
                    """
                    using System;
                    using Microsoft.AspNetCore.Builder;
                    namespace Microsoft.AspNetCore.Builder {
                        public class Builder {}
                        public static class EndpointRouteBuilderExtensions {
                            public static Builder MapGroup(this Builder builder, string prefix) => builder;
                            public static Builder MapGet(this Builder builder, string pattern, Func<int> handler) => builder;
                        }
                        public static class Config {
                            public static Builder Mark(this Builder builder) => builder;
                        }
                    }
                    class A {
                        static int Handler() => 1;
                        void M(Builder app, string unknown) {
                            var group = app.MapGroup("/api").Mark();
                            group.MapGet("/items", Handler).Mark();
                            group.MapGet(unknown, () => 2);
                        }
                    }
                    """
                ),
            ]
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);
        var marker = CodeType.Named("Microsoft.AspNetCore.Builder.Config").Member("Mark");

        var routes = EndpointRoutes
            .All.In(solution)
            .Select(route =>
                (
                    route.Path,
                    route.Handler?.Name,
                    route.Calls(marker),
                    route.CallsOnRouteOrGroup(marker)
                )
            )
            .ToArray();
        var groups = EndpointRoutes.Groups.In(solution).Select(route => route.Path).ToArray();

        Assert.Equal(
            new (string?, string?, bool, bool)[]
            {
                ("/api/items", "Handler", true, true),
                ((string?)null, "", false, true),
            },
            routes
        );
        Assert.Equal(["/api"], groups);
    }
}
