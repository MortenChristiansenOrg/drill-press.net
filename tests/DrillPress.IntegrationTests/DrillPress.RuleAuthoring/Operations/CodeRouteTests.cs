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

    [Fact]
    public void Routes_mapped_on_the_built_application_in_top_level_statements_have_paths()
    {
        var workspace = fixture.Workspace();
        workspace.AddProject(
            "A",
            [
                new(
                    "Program.cs",
                    """
                    using System;
                    using Microsoft.AspNetCore.Builder;
                    var builder = WebApplication.CreateBuilder(args);
                    var app = builder.Build();
                    app.MapGet("/orders", () => 1);
                    var api = app.MapGroup("/api");
                    api.MapGet("/items", () => 2);
                    Groups.Versioned(app).MapGet("/unknown", () => 3);
                    namespace Microsoft.AspNetCore.Builder {
                        public interface IEndpointRouteBuilder {}
                        public sealed class WebApplicationBuilder { public WebApplication Build() => new(); }
                        public sealed class WebApplication : IEndpointRouteBuilder {
                            public static WebApplicationBuilder CreateBuilder(string[] args) => new();
                        }
                        public static class EndpointRouteBuilderExtensions {
                            public static Microsoft.AspNetCore.Routing.RouteGroupBuilder MapGroup(this IEndpointRouteBuilder builder, string prefix) => new();
                            public static IEndpointRouteBuilder MapGet(this IEndpointRouteBuilder builder, string pattern, Func<int> handler) => builder;
                        }
                        public static class Groups {
                            public static Microsoft.AspNetCore.Routing.RouteGroupBuilder Versioned(IEndpointRouteBuilder app) => app.MapGroup("/v1");
                        }
                    }
                    namespace Microsoft.AspNetCore.Routing {
                        public sealed class RouteGroupBuilder : Microsoft.AspNetCore.Builder.IEndpointRouteBuilder {}
                    }
                    """
                ),
            ],
            outputKind: Microsoft.CodeAnalysis.OutputKind.ConsoleApplication
        );
        var solution = workspace.Analyze(TestContext.Current.CancellationToken);

        var routes = Code.Routes.In(solution).Select(route => route.Path ?? "unknown").ToArray();

        Assert.Equal(["/orders", "/api/items", "unknown"], routes);
    }
}
