using System.IO.Abstractions;
using DrillPress.Cli;

var fileSystem = new FileSystem();

return (int)await new CliApplication(fileSystem, new ChildProcessRunner()).RunAsync(args);
