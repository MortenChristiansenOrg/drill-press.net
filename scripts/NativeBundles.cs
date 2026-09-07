#:project ../tools/DrillPress.Benchmarks/DrillPress.Benchmarks.csproj
#:property PublishAot=false

using System.IO.Abstractions;
using DrillPress.Benchmarks;

var fileSystem = new FileSystem();

// The runner reuses BundleVerification to publish and verify before benchmarking.
return await new BenchmarkApplication(fileSystem).RunAsync(args);
