namespace DrillPress;

/// <summary>A selected parameter-associated argument. Removal still requires value, evaluation and overload proofs.</summary>
public sealed class ArgumentFix
{
    private readonly CodeArgument _argument;

    internal ArgumentFix(CodeArgument argument) => _argument = argument;

    /// <summary>Selects the explicit argument for removal; receivers, defaults and expanded params remain ineligible.</summary>
    public ArgumentRemoval Remove() =>
        new(_argument.Source, _argument.Invocation.Operation.Syntax, _argument.Parameter.Name, []);
}
