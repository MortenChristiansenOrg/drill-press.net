using DrillPress;
using DrillPress.IntegrationTests.TestInfrastructure;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Operations;

public sealed class CodeRouteTests(SdkFixture fixture) : IClassFixture<SdkFixture>
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

        var routes = Code
            .Routes.In(solution)
            .Select(route =>
                $"{route.Path}:{route.HandlerExpression?.Syntax}:{route.Handler?.MethodKind}:{route.IsConfiguredWith(marker)}:{route.IsConfiguredWith(marker, includeGroups: true)}"
            )
            .ToArray();
        var groups = Code
            .RouteGroups.In(solution)
            .Select(group =>
                $"{group.Path}:{group.HandlerExpression}:{group.IsConfiguredWith(marker)}"
            )
            .ToArray();

        Assert.Equal(
            ["/api/items:Handler:Ordinary:True:True", ":() => 2:AnonymousFunction:False:True"],
            routes
        );
        Assert.Equal(["/api::True"], groups);
    }
}
