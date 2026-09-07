namespace DrillPress.BundleVerification;

public sealed record ProcessOutput(int ExitCode, byte[] StandardOutput, byte[] StandardError);

public sealed record BundleCase(
    string Name, string[] Arguments, BundleOutcome Outcome, byte[] StandardOutput, byte[] StandardError);

public enum BundleOutcome
{
    Clean = 0,
    Findings = 1,
    Failure = 2,
}

public enum BundleMode
{
    Managed,
    Native,
}
