# Rule-authoring agent skill

The `drillpress-rules` skill teaches a coding agent to write, change, fix and test
DrillPress rules in your repository without fetching online documentation. It holds:

| File | Content |
| --- | --- |
| `SKILL.md` | Workflow, rule shape, candidate selection, report locations, fixes, tests, project setup, running and pitfalls |
| `reference/queries.md` | Every `Code` root, filter, query operator and candidate member |
| `reference/fixes.md` | Fix builders, what each proof must establish, and custom edits |
| `reference/testing.md` | `RuleTestWorkspace` options, output assertions and coverage fakes |
| `examples/ExampleRules.cs`, `examples/ExampleRulesTests.cs` | Eight working rules with their tests |
| `examples/TemplateExtractionRules.cs`, `examples/TemplateExtractionRulesTests.cs` | A bounded template-extraction proof and tests for accepted and unsupported cases |

The agent reads `SKILL.md` first and opens the reference files only when it needs them.

## Install

The skill ships inside the `DrillPress.Cli` tool, so it always matches the tool and
SDK version you pin. From your repository root:

```sh
dotnet tool run drillpress install-skill
```

This writes `.claude/skills/drillpress-rules`, the project skill directory of Claude
Code, with the tool's version filled in. Pass another skills directory for a personal
installation or another agent that reads the `SKILL.md` format:

```sh
dotnet tool run drillpress install-skill ~/.claude/skills
dotnet tool run drillpress install-skill .agents/skills
```

For agents without skill support, reference
`.claude/skills/drillpress-rules/SKILL.md` from `AGENTS.md` or the agent's equivalent
instruction file.

Commit the installed directory so everyone uses the same guidance, and run the command
again after upgrading DrillPress. It overwrites the bundled files and keeps any other
files you add to the directory. The example `.cs` files are not compiled by your
projects because SDK-style projects ignore folders whose names start with a dot; when
installing into another folder under a project, exclude it with
`<Compile Remove="path/to/skills/**" />`.

## Use

Ask for the convention in your own words, for example "Add a DrillPress rule that
forbids `DateTime.Now` in `Shop.Domain`, with tests." The agent loads the skill when a
request concerns DrillPress rules, fixes or their tests. In Claude Code you can also
invoke it directly with `/drillpress-rules`.

Following the skill, the agent locates or creates the rule bundle and its test project,
writes a failing test with the complete expected output, registers the rule, adds a
fix only when it can prove the edit safe, and finishes by running the tests and the
`drillpress` CLI on your code.

## Maintaining the skill

The source lives in [`skills/drillpress-rules`](../skills/drillpress-rules/SKILL.md); its
`{{version}}` placeholders are filled in by `install-skill`. Every C# block in the skill
is compiled against the public SDK by `AgentSkillTests`, and blocks containing tests are
executed. The example rules and tests are compiled into the integration test project and
run as ordinary tests, so API changes that break the skill fail the build.
