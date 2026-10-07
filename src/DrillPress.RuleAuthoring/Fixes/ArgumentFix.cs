namespace DrillPress;

/// <summary>A selected argument ready for removal.</summary>
public sealed class ArgumentFix
{
    private readonly CodeArgument _argument;

    internal ArgumentFix(CodeArgument argument) => _argument = argument;

    /// <summary>Removes the explicit argument; receivers, omitted defaults and expanded params are not removable. Removal changes the selected overload, so declare it with <see cref="ArgumentRemoval.ExpectOverloadChange"/> and prove the removed value and evaluation.</summary>
    public ArgumentRemoval Remove() =>
        new(_argument.Source, _argument.Invocation.Operation.Syntax, _argument.Parameter.Name);
}
