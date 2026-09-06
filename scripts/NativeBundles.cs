#:project ../tools/DrillPress.BundleVerification/DrillPress.BundleVerification.csproj
#:property PublishAot=false

using DrillPress.BundleVerification;

return await VerificationApplication.RunAsync(args);
