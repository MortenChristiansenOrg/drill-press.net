using DrillPress;
using Microsoft.CodeAnalysis;

namespace MyRules;

/// <summary>The team's conventions. Program.cs passes <see cref="Create"/> to RuleApplication.</summary>
public static class ExampleRules
{
    public static RuleCatalog Create()
    {
        var rules = new RuleCatalog();
        Logging(rules);
        Naming(rules);
        Spelling(rules);
        Style(rules);
        return rules;
    }

    // Forbid: every selected candidate is a finding.
    private static void Logging(RuleCatalog rules)
    {
        var writeLine = CodeType.Named("System.Console").Member("WriteLine");
        rules
            .Rule("TEAM001", "Use the application logger instead of Console.WriteLine.")
            .For(Code.Calls.To(writeLine).InNonTestProjects())
            .Forbid();

        // Arguments report at the argument itself.
        rules
            .Rule("TEAM002", "Pass ConfigureAwait(false) in application code.")
            .For(
                Code.Calls.ToMethodsNamed("ConfigureAwait")
                    .InNonTestProjects()
                    .ArgumentsFor("continueOnCapturedContext")
                    .Where(argument => !argument.Is(false))
            )
            .Forbid();
    }

    // Require: selected candidates that fail the condition are findings.
    private static void Naming(RuleCatalog rules)
    {
        var handler = CodeType.Named("MyApp.MessageHandlerAttribute");
        rules
            .Rule("TEAM003", "Give asynchronous methods an Async suffix.")
            .For(
                Code.Methods.Where(method => method.IsAsync)
                    .ExceptWhen(method => method.HasAttribute(handler))
            )
            .Require(method => method.NameEndsWith("Async"));

        rules
            .Rule("TEAM004", "Document public types.")
            .For(Code.Types.InNonTestProjects().WithAccessibility(Accessibility.Public))
            .Require(type => type.HasDocumentationComment());
    }

    // A fix whose policy-specific equivalence the rule proves in SafeWhen.
    private static void Spelling(RuleCatalog rules)
    {
        var empty = CodeType.Of<string>().Member(nameof(string.Empty));
        rules
            .Rule("TEAM005", "Use \"\" instead of string.Empty.", RuleFixComplexity.Trivial)
            .For(empty.References.OutsideNameOf())
            .Forbid(fix: reference =>
                Fix.For(reference)
                    .ReplaceWithLiteral("")
                    .SafeWhen(change => change.Before.RefersTo(empty) && change.After.Is(""))
            );
    }

    // Library-proven fixes end in Propose(); ReportAt points at the exact part.
    private static void Style(RuleCatalog rules)
    {
        rules
            .Rule("TEAM006", "Add braces to if and else branches.")
            .For(Code.IfStatements.Branches().WithoutBraces())
            .Forbid(fix: branch => Fix.For(branch).AddBraces().Propose());

        rules
            .Rule("TEAM007", "Omit the default internal modifier on top-level types.")
            .For(Code.TypeDeclarations.TopLevel().WithExplicitModifier(Modifier.Internal))
            .ReportAt(type => type.ExplicitModifier(Modifier.Internal))
            .Forbid(fix: type => Fix.For(type).RemoveModifier(Modifier.Internal).Propose());

        rules
            .Rule("TEAM008", "Handle, log, or rethrow caught exceptions.")
            .For(Code.Catches.Where(handler => handler.IsEmpty))
            .Forbid();
    }
}
