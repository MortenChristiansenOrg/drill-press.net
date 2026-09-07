using System.IO.Abstractions;
using DrillPress.BuildHost;

var fileSystem = new FileSystem();

return (int)await new BuildHostApplication(fileSystem, new MsBuildSnapshotLoader(fileSystem)).RunAsync(args);
