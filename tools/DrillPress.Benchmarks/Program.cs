using System.IO.Abstractions;
using DrillPress.Benchmarks;

var fileSystem = new FileSystem();

return await new BenchmarkApplication(fileSystem).RunAsync(args);
