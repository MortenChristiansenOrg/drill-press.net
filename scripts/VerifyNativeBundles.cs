#:project ../tools/DrillPress.BundleVerification/DrillPress.BundleVerification.csproj
#:property PublishAot=false

using System.IO.Abstractions;
using DrillPress.BundleVerification;

if (args is not ["--output", var output] || string.IsNullOrWhiteSpace(output))
{
    Console.Error.WriteLine("Usage: VerifyNativeBundles.cs --output <new-directory>");
    return 2;
}

try
{
    using var session = await VerificationSession.CreateAsync(new FileSystem(), output);
    Console.WriteLine($"Reports: {session.OutputDirectory}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"native-bundles: {exception.Message}");
    return 1;
}
