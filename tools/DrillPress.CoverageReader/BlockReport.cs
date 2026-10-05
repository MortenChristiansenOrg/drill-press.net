using System.IO.Abstractions;
using System.Xml.Linq;
using Microsoft.CodeCoverage.Core;
using Microsoft.CodeCoverage.Core.Reports.Coverage;

namespace DrillPress.CoverageReader;

internal sealed class BlockReport(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem;

    internal async Task EnrichAsync(
        string binaryPath,
        string xmlPath,
        string version,
        CancellationToken cancellationToken
    )
    {
        var reader = new CoverageFileUtilityV2(
            new CoverageFileConfiguration
            {
                ReadModules = true,
                SkipInvalidData = false,
                FixCoverageBuffersMismatch = false,
            }
        );
        var report = await reader.ReadCoverageFileAsync(
            await _fileSystem.File.ReadAllBytesAsync(binaryPath, cancellationToken),
            cancellationToken
        );
        XDocument xml;
        using (var stream = _fileSystem.File.OpenRead(xmlPath))
            xml = XDocument.Load(stream);
        if (xml.Root?.Name != "results")
            throw new InvalidDataException("Unsupported coverage XML.");
        foreach (var module in xml.Descendants("module"))
            EnrichModule(module, report.Modules, version);
        using var output = _fileSystem.File.Create(xmlPath);
        xml.Save(output);
    }

    private static void EnrichModule(XElement xml, ModuleData[] modules, string version)
    {
        var matching = modules
            .Where(module =>
                module.IdString == (string?)xml.Attribute("id")
                && module.Name.Equals(
                    (string?)xml.Attribute("name"),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ToArray();
        if (matching.Length != 1)
            throw new InvalidDataException("Binary/XML module identities disagree.");
        var module = matching[0];
        var data = new XElement(
            "block_data",
            new XAttribute("collector_version", version),
            new XAttribute("buffer", Convert.ToBase64String(module.CoverageBuffer))
        );
        foreach (var function in module.Functions)
        {
            var method = new XElement(
                "method",
                new XAttribute("token", $"0x{function.MetadataToken:X}")
            );
            foreach (var point in function.LineData)
                method.Add(ReadPoint(module.CoverageBuffer, point));
            data.Add(method);
        }
        xml.Add(data);
    }

    private static XElement ReadPoint(byte[] buffer, MultiBlockLineData point)
    {
        var blocks = point.BlockIndexes.ToArray();
        if (blocks.Length == 0 || blocks.Any(index => index >= buffer.Length))
            throw new InvalidDataException("Coverage block index is outside its module buffer.");
        var covered = blocks.Count(index => buffer[index] != 0);
        var status =
            covered == blocks.Length ? CoverageStatus.yes
            : covered == 0 ? CoverageStatus.no
            : CoverageStatus.partial;
        if (point.CoverageStatus != status)
            throw new InvalidDataException("Coverage block hits disagree with their source range.");
        return new XElement(
            "point",
            new XAttribute("source_id", point.SourceId),
            new XAttribute("start_line", point.StartLine),
            new XAttribute("start_column", point.StartColumn),
            new XAttribute("end_line", point.EndLine),
            new XAttribute("end_column", point.EndColumn),
            blocks.Select(index => new XElement(
                "block",
                new XAttribute("index", index),
                new XAttribute("covered", buffer[index] == 0 ? "no" : "yes")
            ))
        );
    }
}
