namespace DrillPress.BundleVerification;

public static class BundleContract
{
    public static void Validate(BundleCase expected, ProcessOutput actual)
    {
        if (actual.ExitCode != (int)expected.Outcome ||
            !actual.StandardOutput.AsSpan().SequenceEqual(expected.StandardOutput) ||
            !actual.StandardError.AsSpan().SequenceEqual(expected.StandardError))
        {
            throw new InvalidOperationException($"{expected.Name}: bundle output differs from the exact byte/exit contract.");
        }
    }
}
