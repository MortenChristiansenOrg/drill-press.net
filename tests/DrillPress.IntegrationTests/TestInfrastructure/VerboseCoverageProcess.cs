using System.IO.Abstractions;
using DrillPress.Engine;

namespace DrillPress.IntegrationTests.TestInfrastructure;

internal sealed class VerboseCoverageProcess(
    IFileSystem fileSystem,
    string executable,
    bool hasTests
) : CoverageProcess
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly string _executable = executable;
    private readonly bool _hasTests = hasTests;
    public int Collections { get; private set; }

    internal override async Task<string> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string directory,
        CancellationToken cancellationToken
    )
    {
        if (arguments[0] == "tool")
            return await base.RunAsync(
                executable,
                arguments,
                _fileSystem.Path.GetDirectoryName(_executable)!,
                cancellationToken
            );
        if (arguments[0] == "msbuild")
        {
            if (arguments.Contains("-getProperty:TargetPath"))
                return _fileSystem.Path.ChangeExtension(arguments[1], ".dll");
            return await base.RunAsync(
                _executable,
                ["msbuild", arguments[1], _hasTests.ToString()],
                directory,
                cancellationToken
            );
        }
        if (arguments[0] == "collect")
        {
            Collections++;
            await base.RunAsync(_executable, ["collect"], directory, cancellationToken);
        }
        if (arguments[0] is "collect" or "merge")
        {
            var output = arguments[arguments.ToList().IndexOf("-o") + 1];
            await _fileSystem.File.WriteAllTextAsync(output, "<results />", cancellationToken);
        }
        return "";
    }
}
