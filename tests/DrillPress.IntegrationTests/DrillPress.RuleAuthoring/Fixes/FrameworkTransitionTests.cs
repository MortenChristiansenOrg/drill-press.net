using DrillPress.Engine;
using DrillPress.IntegrationTests.TestInfrastructure;
using DrillPress.Manifest;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DrillPress.IntegrationTests.RuleAuthoring.Fixes;

public sealed class FrameworkTransitionTests : IntegrationTest
{
    [Theory]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public async Task Framework_transition_uses_actual_target_reference_assemblies(string framework)
    {
        var directory = CreateTemporaryDirectory("drillpress-framework-");
        var project = FileSystem.Path.Combine(directory.FullName, "Target.csproj");
        var snapshotPath = FileSystem.Path.Combine(directory.FullName, "snapshot.json");
        await FileSystem.File.WriteAllTextAsync(
            project,
            $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>{framework}</TargetFramework></PropertyGroup></Project>",
            TestContext.Current.CancellationToken
        );
        await FileSystem.File.WriteAllTextAsync(
            FileSystem.Path.Combine(directory.FullName, "A.cs"),
            "using System; using System.Linq; class C { object M(string[] values) => Enumerable.Distinct<string>(values, StringComparer.Ordinal); }",
            TestContext.Current.CancellationToken
        );
        await RestoreAsync(project);
        var export = await RunProcessAsync(
            "dotnet",
            [GetOutputPath("DrillPress.BuildHost"), "export", project, snapshotPath],
            RepositoryRoot,
            TestContext.Current.CancellationToken
        );
        var snapshot = await new CompilationSnapshotFile().ReadAsync(
            snapshotPath,
            TestContext.Current.CancellationToken
        );
        var rules = new RuleSet();
        var distinct = CodeType.Framework("System.Linq.Enumerable").Member("Distinct");
        var enumerable = CodeType.Framework("System.Collections.Generic.IEnumerable<>");
        var comparer = CodeType.Framework("System.Collections.Generic.IEqualityComparer<>");
        var ordinal = CodeType.Of<StringComparer>().Member("Ordinal");
        rules
            .For(ordinal.References.PassedAs("comparer").To(distinct))
            .Forbid(
                "REMOVE",
                "Use default comparer.",
                fix: argument =>
                    Fix.For(argument)
                        .Remove()
                        .ExpectOverloadChange(
                            distinct.WithParameters(enumerable, comparer),
                            distinct.WithParameters(enumerable)
                        )
                        .RequireRemovedValue(change => change.RemovedValue!.RefersTo(ordinal))
                        .RequireRemovedEvaluation(change => change.RemovedValue!.RefersTo(ordinal))
                        .SafeWhen(change =>
                            change.Expected.Before.TypeArguments
                                is [{ SpecialType: SpecialType.System_String }]
                        )
            );

        var result = await new AnalysisEngine().EvaluateAsync(
            rules,
            snapshot,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, export.ExitCode);
        Assert.Equal([framework], snapshot.Projects.Select(context => context.TargetFramework));
        Assert.Single(result.Contexts.Single().Findings);
        Assert.Single(result.Batches);
    }
}
