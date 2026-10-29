using System.Collections.Concurrent;
using System.IO.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DrillPress.Engine;

internal sealed class SemanticPreparation
{
    private readonly Dictionary<SyntaxTree, TaskCompletionSource<SemanticModel>> _models;
    private readonly TaskCompletionSource<bool> _errors = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly ConcurrentQueue<Exception> _failures = new();
    private readonly CompilationWithAnalyzers _driver;
    private readonly CancellationToken _cancellationToken;
    private readonly bool _prepareOperations;
    private readonly IFileSystem _fileSystem;

    internal SemanticPreparation(
        CompilationContext context,
        IFileSystem fileSystem,
        bool prepareOperations,
        CancellationToken cancellationToken
    )
    {
        _cancellationToken = cancellationToken;
        _fileSystem = fileSystem;
        _prepareOperations = prepareOperations;
        _models = context.Compilation.SyntaxTrees.ToDictionary(
            tree => tree,
            _ => new TaskCompletionSource<SemanticModel>(
                TaskCreationOptions.RunContinuationsAsynchronously
            )
        );
        _driver = context.Compilation.WithAnalyzers(
            [new SemanticPreparationAnalyzer(Prepared)],
            new CompilationWithAnalyzersOptions(
                new AnalyzerOptions([]),
                (exception, _, _) => _failures.Enqueue(exception),
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false
            )
        );
        Project = CreateProject(context.Snapshot);
        Completion = Task.Run(PrepareAsync, CancellationToken.None);
    }

    internal AnalysisProject Project { get; }

    internal Task Completion { get; }

    internal AnalysisProject CreateProject(DrillPress.Manifest.ProjectSnapshot snapshot) =>
        new(
            snapshot,
            (CSharpCompilation)_driver.Compilation,
            _fileSystem,
            _cancellationToken,
            tree => _models[tree].Task.GetAwaiter().GetResult(),
            new Lazy<bool>(() => _errors.Task.GetAwaiter().GetResult())
        );

    private async Task PrepareAsync()
    {
        try
        {
            var diagnostics = await _driver.GetAllDiagnosticsAsync(_cancellationToken);
            if (_failures.TryPeek(out var failure))
                throw failure;
            _errors.SetResult(
                diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            );
        }
        catch (Exception exception)
        {
            _errors.TrySetException(exception);
            foreach (var model in _models.Values)
                model.TrySetException(exception);
            throw;
        }
    }

    private void Prepared(SemanticModelAnalysisContext context)
    {
        _models[context.SemanticModel.SyntaxTree].TrySetResult(context.SemanticModel);
        if (_prepareOperations)
        {
            var source = Project.Sources.Single(source =>
                source.Tree == context.SemanticModel.SyntaxTree
            );
            if (!source.Document.IsGenerated)
                source.Operations.Prepare();
        }
    }
}
