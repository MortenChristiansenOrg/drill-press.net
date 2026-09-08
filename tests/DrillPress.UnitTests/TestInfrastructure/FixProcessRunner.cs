using System.Text;
using DrillPress.Cli;
using DrillPress.Manifest;

namespace DrillPress.UnitTests.TestInfrastructure;

internal sealed class FixProcessRunner(FixFixture fixture) : ChildProcessRunner
{
    private readonly FixFixture _fixture = fixture;
    private int _exports;
    private CompilationSnapshot _snapshot = fixture.Snapshot;
    public List<(string Executable, string[] Arguments)> Calls { get; } = [];
    public bool FailRegeneration { get; set; }
    public bool FailRecheck { get; set; }
    public bool WithholdFixes { get; set; }
    public Action? OnRecheck { get; set; }

    public override async Task<ChildProcessResult> CaptureAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        Calls.Add((executable, arguments.ToArray()));
        if (executable == "host")
        {
            _exports++;
            if (_exports == 2 && FailRegeneration) return new(2, "", "regeneration failed\n");
            if (_exports == 2)
            {
                _snapshot = _snapshot with { Projects = _snapshot.Projects.Select(project => project with
                {
                    Documents = project.Documents.Select(document => SourceIdentity.Capture(document with
                    {
                        Text = _fixture.FileSystem.File.ReadAllText(document.Path),
                    }, _fixture.FileSystem.File.ReadAllBytes(document.Path), document.EncodingName, document.HasByteOrderMark)).ToArray(),
                }).ToArray() };
            }

            await new CompilationSnapshotFile(_fixture.FileSystem).WriteAsync(arguments[2], _snapshot, cancellationToken);
            return new(0, "", "");
        }

        if (_exports == 2) OnRecheck?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        if (_exports == 2 && FailRecheck) return new(2, "stale text must not escape", "recheck failed\n");
        var response = _exports == 1 ? _fixture.Response : new BundleResponse(1, _snapshot.RequestId,
            _snapshot.Projects.Select(project => new ContextEvaluation(project.ContextId, true, [])).ToArray(), []);
        if (WithholdFixes) response = response with { Batches = response.Batches.Select(batch => batch with { Validations = [] }).ToArray() };
        return new(response.Contexts.Any(context => context.Findings.Length > 0) ? 1 : 0,
            Encoding.UTF8.GetString(BundleResponseProtocol.Serialize(response)), "");
    }
}
