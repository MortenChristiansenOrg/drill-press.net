#:project ../tools/DrillPress.Benchmarks/DrillPress.Benchmarks.csproj
#:property PublishAot=false

using DrillPress.Benchmarks;

// The runner reuses BundleVerification to publish and verify before benchmarking.
return await BenchmarkApplication.RunAsync(args);
