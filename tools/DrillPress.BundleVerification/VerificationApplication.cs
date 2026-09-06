namespace DrillPress.BundleVerification;

public static class VerificationApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var output = args switch
            {
                [] => null,
                ["--output", var path] => path,
                _ => throw new ArgumentException("Usage: NativeBundles.cs [--output <new-directory>]"),
            };
            using var session = await VerificationSession.CreateAsync(output);
            Console.WriteLine($"Reports: {session.OutputDirectory}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"native-bundles: {exception.Message}");
            return 1;
        }
    }
}
