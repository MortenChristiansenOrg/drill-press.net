#:project ../benchmarks/DrillPress.Benchmarks/DrillPress.Benchmarks.csproj
#:property PublishAot=false

using DrillPress.Benchmarks;

return await BenchmarkApplication.RunAsync(args);
