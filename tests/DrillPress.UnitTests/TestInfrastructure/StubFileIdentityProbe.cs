using DrillPress.Manifest;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class StubFileIdentityProbe : FileIdentityProbe
{
    public Dictionary<string, PhysicalFileIdentity?> Overrides { get; } = [];

    public override PhysicalFileIdentity? Read(string path) =>
        Overrides.TryGetValue(path, out var identity) ? identity : new(path, 1, true);
}
