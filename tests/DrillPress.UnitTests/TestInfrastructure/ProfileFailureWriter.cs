namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class ProfileFailureWriter : StringWriter
{
    public override void WriteLine(string? value) =>
        throw new IOException("profile sink unavailable");
}
