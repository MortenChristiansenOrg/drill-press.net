using DrillPress;
using DrillPress.Engine;

var rules = new RuleCatalog();
rules
    .Rule("SHOP001", "Use \"\" instead of string.Empty.", RuleFixComplexity.Trivial)
    .For(CodeType.Of<string>().Member(nameof(string.Empty)).References)
    .Forbid(fix: reference =>
        SourceChanges.Propose(
            [SourceChanges.Replace(reference.Source, reference.Syntax.Span, "\"\"")],
            context =>
                context
                    .Original.Sources.Where(source =>
                        source.Document.FileIdentity == reference.Source.Document.FileIdentity
                    )
                    .All(source =>
                        BindingProof.PreservesEnclosingExpressions(
                            source,
                            source
                                .Tree.GetRoot()
                                .FindNode(reference.Syntax.Span, getInnermostNodeForTie: true),
                            "\"\""
                        )
                    )
        )
    );
rules
    .Rule("SHOP002", "Inline locals that are read once.", RuleFixComplexity.Trivial)
    .For(Code.LocalVariables.WithSingleRead())
    .Forbid(fix: declaration => Fix.For(declaration).Inline().Propose());
rules
    .Rule("SHOP003", "Read the time from an injected TimeProvider.", RuleFixComplexity.Local)
    .For(CodeType.Of<DateTime>().Member(nameof(DateTime.Now)).References)
    .Forbid();
return (int)await new RuleApplication().RunAsync(rules, args);
