using Microsoft.CodeAnalysis;

namespace DrillPress.IntegrationTests.TestInfrastructure;

public sealed class SdkFixture
{
    private readonly MetadataReference[] _references;

    public SdkFixture()
    {
        var workspace = new RuleTestWorkspace();
        _references = workspace
            .AddProject("References", [new("References.cs", "class ReferenceAnchor { }")])
            .Compilation.References.ToArray();
    }

    public RuleTestWorkspace Workspace() => new(_references);
}
