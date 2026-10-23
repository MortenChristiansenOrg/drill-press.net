using System.IO.Abstractions.TestingHelpers;
using DrillPress.Benchmarks;
using Xunit;

namespace DrillPress.UnitTests.DrillPress.Benchmarks;

public sealed class BuildHostWorkloadTests
{
    private readonly MockFileSystem _fileSystem = new();

    [Fact]
    public void Writes_an_isolated_layered_solution_with_generator_inputs()
    {
        var directory = _fileSystem.Path.GetFullPath("workload");

        var solution = new BuildHostWorkload(_fileSystem).Write(directory);

        Assert.Equal(_fileSystem.Path.Combine(directory, "Workload.slnx"), solution);
        Assert.Equal(
            """
            <Solution>
              <Project Path="Core/Core.csproj" />
              <Project Path="Feature0/Feature0.csproj" />
              <Project Path="Feature1/Feature1.csproj" />
              <Project Path="Feature2/Feature2.csproj" />
              <Project Path="Feature3/Feature3.csproj" />
              <Project Path="App/App.csproj" />
              <Project Path="AppTests/AppTests.csproj" />
            </Solution>

            """.ReplaceLineEndings("\n"),
            _fileSystem.File.ReadAllText(solution)
        );
        Assert.Equal(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../App/App.csproj" />
                <ProjectReference Include="../Core/Core.csproj" />
              </ItemGroup>
            </Project>

            """.ReplaceLineEndings("\n"),
            _fileSystem.File.ReadAllText(
                _fileSystem.Path.Combine(directory, "AppTests", "AppTests.csproj")
            )
        );
        Assert.Equal(
            """
            using System.Text.RegularExpressions;

            namespace AppTests;

            public partial class Service7
            {
                public App.Service0? Upstream { get; init; }

                public string Name { get; init; } = "";

                public bool Matches(string value) => Pattern().IsMatch(value);

                [GeneratedRegex("^apptests-7-[a-z]+$")]
                private static partial Regex Pattern();
            }

            """.ReplaceLineEndings("\n"),
            _fileSystem.File.ReadAllText(
                _fileSystem.Path.Combine(directory, "AppTests", "Service7.cs")
            )
        );
        Assert.Equal(
            "<Project />\n",
            _fileSystem.File.ReadAllText(
                _fileSystem.Path.Combine(directory, "Directory.Packages.props")
            )
        );
        Assert.Equal(7 * (BuildHostWorkload.FilesPerProject + 2) + 4, _fileSystem.AllFiles.Count());
    }
}
