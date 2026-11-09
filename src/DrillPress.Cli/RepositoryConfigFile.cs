using System.IO.Abstractions;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DrillPress.Cli;

/// <summary>
/// Finds and validates committed repository defaults. Without an explicit path, the nearest drillpress.json in the
/// current directory or one of its parents applies. Relative paths in the file resolve against its directory.
/// </summary>
internal sealed class RepositoryConfigFile(IFileSystem fileSystem)
{
    internal const string FileName = "drillpress.json";
    private const string KeyList = "rules, target, properties, select, ignore or exclude";

    private readonly IFileSystem _fileSystem = fileSystem;

    /// <summary>Reads <paramref name="path"/>, or the discovered file when it is null; null when none exists.</summary>
    /// <exception cref="FileNotFoundException">The explicit file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not valid JSON or has an unknown key or invalid value.</exception>
    internal RepositoryConfig? Load(string? path)
    {
        var file = path is null ? Discover() : _fileSystem.Path.GetFullPath(path);
        if (file is null)
            return null;
        if (!_fileSystem.File.Exists(file))
            throw new FileNotFoundException($"Config file '{path}' was not found.", file);
        var display = Escape(Relative(file));
        try
        {
            using var document = JsonDocument.Parse(
                _fileSystem.File.ReadAllText(file),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                }
            );
            return Read(document.RootElement, _fileSystem.Path.GetDirectoryName(file)!, display);
        }
        // Strings with unpaired surrogates fail when read rather than when parsed.
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw Invalid(display, $"invalid JSON: {Escape(exception.Message.TrimEnd('.'))}");
        }
    }

    /// <summary>Turns a command-line exclusion glob, relative to the current directory, into a full-path glob.</summary>
    internal string ExclusionGlob(string pattern) =>
        FullGlob(_fileSystem.Directory.GetCurrentDirectory(), pattern);

    private string? Discover()
    {
        for (
            var directory = _fileSystem.DirectoryInfo.New(
                _fileSystem.Directory.GetCurrentDirectory()
            );
            directory is not null;
            directory = directory.Parent
        )
        {
            var candidate = _fileSystem.Path.Combine(directory.FullName, FileName);
            if (_fileSystem.File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private RepositoryConfig Read(JsonElement root, string directory, string display)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw Invalid(display, "expected a JSON object");
        var config = new RepositoryConfig(display, null, null, [], null, [], []);
        var seen = new HashSet<string>();
        foreach (var property in root.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw Invalid(display, $"'{Escape(property.Name)}' appears more than once");
            var value = property.Value;
            config = property.Name switch
            {
                "rules" => config with
                {
                    Rules = ResolvePath(
                        directory,
                        value.ValueKind == JsonValueKind.Array
                            ? throw Invalid(
                                display,
                                "'rules' is the rules project or bundle path; list rule IDs in 'select'"
                            )
                            : Text(value, display, "'rules' must be a path")
                    ),
                },
                "target" => config with
                {
                    Target = ResolvePath(
                        directory,
                        Text(value, display, "'target' must be a path")
                    ),
                },
                "properties" => config with { Properties = Properties(value, display) },
                "select" => config with
                {
                    Select = Texts(value, display, "'select' must be a non-empty array of rule IDs")
                        is { Length: > 0 } selected
                        ? selected
                        : throw Invalid(display, "'select' must be a non-empty array of rule IDs"),
                },
                "ignore" => config with
                {
                    Ignore = Texts(value, display, "'ignore' must be an array of rule IDs"),
                },
                "exclude" => config with
                {
                    Exclude = Texts(value, display, "'exclude' must be an array of path globs")
                        .Select(pattern => FullGlob(directory, pattern))
                        .ToArray(),
                },
                _ => throw Invalid(
                    display,
                    $"unknown key '{Escape(property.Name)}'; use {KeyList}"
                ),
            };
        }

        return config;
    }

    private static KeyValuePair<string, string>[] Properties(JsonElement value, string display)
    {
        const string message = "'properties' must map MSBuild property names to string values";
        if (value.ValueKind != JsonValueKind.Object)
            throw Invalid(display, message);
        return value
            .EnumerateObject()
            .Select(property =>
                property.Name.Length == 0
                || property.Name.Any(character =>
                    character == '=' || char.IsWhiteSpace(character) || char.IsControl(character)
                )
                    ? throw Invalid(
                        display,
                        $"'properties' has an invalid name '{Escape(property.Name)}'"
                    )
                : property.Value.ValueKind == JsonValueKind.String
                    ? KeyValuePair.Create(property.Name, property.Value.GetString()!)
                : throw Invalid(display, message)
            )
            .ToArray();
    }

    private static string[] Texts(JsonElement value, string display, string message) =>
        value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => Text(item, display, message)).ToArray()
            : throw Invalid(display, message);

    private static string Text(JsonElement value, string display, string message) =>
        value.ValueKind == JsonValueKind.String
        && value.GetString() is { } text
        && !string.IsNullOrWhiteSpace(text)
        && !text.Any(char.IsControl)
            ? text
            : throw Invalid(display, message);

    // Rules and target paths stay relative to the current directory, so messages and output stay short.
    private string ResolvePath(string directory, string path) =>
        Relative(_fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(directory, path)));

    // A trailing separator, either / or \, selects everything below the directory, including the root.
    private string FullGlob(string directory, string pattern)
    {
        var full = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(directory, pattern));
        return pattern.EndsWith('/') || pattern.EndsWith('\\')
            ? full.TrimEnd('/', '\\') + _fileSystem.Path.DirectorySeparatorChar + "**"
            : full;
    }

    private string Relative(string path) =>
        _fileSystem.Path.GetRelativePath(_fileSystem.Directory.GetCurrentDirectory(), path);

    // Keeps every message on one line, as JSON-escaped text.
    private static string Escape(string text) =>
        text.Any(char.IsControl)
            ? JsonEncodedText.Encode(text, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString()
            : text;

    private static InvalidDataException Invalid(string display, string message) =>
        new($"{display}: {message}.");
}
