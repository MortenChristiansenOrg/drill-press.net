using Microsoft.CodeAnalysis;

namespace DrillPress;

/// <summary>Recognizes the framework's task types by semantic identity: <c>Task</c>, <c>Task&lt;T&gt;</c>, <c>ValueTask</c> and <c>ValueTask&lt;T&gt;</c>.</summary>
public static class TaskTypes
{
    private static readonly CodeType[] _tasks =
    [
        CodeType.Of<Task>(),
        CodeType.Framework("System.Threading.Tasks.Task<>"),
        CodeType.Of<ValueTask>(),
        CodeType.Framework("System.Threading.Tasks.ValueTask<>"),
    ];

    /// <summary>Whether the type is a framework task, with or without a result. Other awaitable types are not inferred.</summary>
    public static bool IsTask(this ITypeSymbol? type) =>
        type is not null && _tasks.Any(task => task.Matches(type));

    /// <summary>The result type of <c>Task&lt;T&gt;</c> or <c>ValueTask&lt;T&gt;</c>, such as <c>int</c> for <c>Task&lt;int&gt;</c>; otherwise the type itself.</summary>
    public static ITypeSymbol UnwrapTask(this ITypeSymbol type) =>
        type is INamedTypeSymbol { TypeArguments: [var result] } && type.IsTask() ? result : type;
}
