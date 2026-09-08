using System.IO.Abstractions;
using System.Text;

namespace DrillPress.BundleVerification;

internal sealed class FixVerificationCase(IFileSystem fileSystem, string root, string directory, string output)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    public async Task VerifyAsync(string cli, string buildHost, string managedBundle, string nativeBundle)
    {
        _fileSystem.Directory.CreateDirectory(directory);
        var first = _fileSystem.Path.Combine(directory, "A.cs");
        var second = _fileSystem.Path.Combine(directory, "B.cs");
        const string originalFirst = "public class A { public string Value => string.Empty; }\r\n";
        const string originalSecond = "using System;\nusing System.Linq;\r\npublic class B { public object Values => new[] { \"é😀\" }.Distinct(StringComparer.Ordinal); }\n";
        byte[] expectedFirst = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(originalFirst.Replace("string.Empty", "\"\""))];
        var expectedSecond = Encoding.UTF8.GetBytes(originalSecond.Replace("StringComparer.Ordinal", ""));
        foreach (var mode in Enum.GetValues<BundleMode>())
        {
            await _fileSystem.File.WriteAllTextAsync(first, originalFirst, new UnicodeEncoding(false, true, true));
            await _fileSystem.File.WriteAllTextAsync(second, originalSecond, new UTF8Encoding(false, true));
            var result = await ProcessRunner.RunAsync("dotnet",
                [cli, "fix", "--build-host", buildHost, "--rules", mode == BundleMode.Managed ? managedBundle : nativeBundle,
                    _fileSystem.Path.Combine(directory, "*.cs")], root);
            await _fileSystem.File.WriteAllBytesAsync(_fileSystem.Path.Combine(output, $"fix.{mode}.stdout"), result.StandardOutput);
            await _fileSystem.File.WriteAllBytesAsync(_fileSystem.Path.Combine(output, $"fix.{mode}.stderr"), result.StandardError);
            if (result.ExitCode != 0 || result.StandardOutput.Length != 0 || result.StandardError.Length != 0 ||
                !(await _fileSystem.File.ReadAllBytesAsync(first)).AsSpan().SequenceEqual(expectedFirst) ||
                !(await _fileSystem.File.ReadAllBytesAsync(second)).AsSpan().SequenceEqual(expectedSecond) ||
                _fileSystem.Directory.GetFiles(directory).Length != 2)
            {
                throw new InvalidDataException($"{mode} fix/recheck did not produce the exact clean sources and output.");
            }
        }
    }
}
