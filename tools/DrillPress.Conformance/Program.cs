using System.IO.Abstractions;
using DrillPress.Conformance;
using DrillPress.BuildHost;
using DrillPress.Engine;
using DrillPress.Manifest;

return (int)await new ConformanceApplication(new FileSystem(), new MsBuildSnapshotLoader(), new AnalysisEngine(), new CompilationSnapshotFile()).RunAsync(args);
