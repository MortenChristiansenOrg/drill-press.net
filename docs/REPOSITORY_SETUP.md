# Repository setup

Commit one small `drillpress.json` file so that developers, coding agents and CI
run the same check with a plain `drillpress check`. The
[repository-defaults sample](../samples/RepositoryDefaults/README.md) shows a complete
setup, and an integration test runs it with the packaged tool.

## The defaults file

```jsonc
{
  "rules": "eng/Rules",
  "target": "Shop.slnx",
  "properties": { "Configuration": "Debug" },
  "ignore": ["SHOP003"],
  "exclude": ["third_party/", "eng/"]
}
```

| Key | Value | Command-line option |
| --- | --- | --- |
| `rules` | The rules project, its directory, or a compiled bundle. | `--rules <path>` |
| `target` | The solution, project, directory, C# file or glob to check. | `<target>` |
| `properties` | MSBuild global properties, as an object of string values. | `--property Name=Value` |
| `select` | Rule IDs to report. Omit it to report every rule. | `--select <ids>` |
| `ignore` | Rule IDs that are never reported. | `--ignore <ids>` |
| `exclude` | Path globs of source files that report nothing. | `--exclude <glob>` |

Every key is optional. `rules` names the rules *bundle*; `select` and `ignore` name
rule *IDs*. The file is a JSON object; comments and trailing commas are allowed.
An unknown or repeated key, a value of the wrong type, an empty `select` list or a
blank entry fails before anything runs, with exit code 2 and one line such as
`drillpress: drillpress.json: unknown key 'rule'; use rules, target, properties, select, ignore or exclude.`

## Discovery, paths and precedence

- Without `--config`, the CLI uses the nearest `drillpress.json` in the current
  directory or one of its parents, so `check` works from any subdirectory.
  `--config <path>` names another file instead. The target's location plays no part.
- Relative paths in the file resolve against the file's directory. Paths given on
  the command line resolve against the current directory. Output paths stay
  relative to the current directory.
- A `--rules` option or a target on the command line replaces the file's value.
- `--property` assignments follow the file's `properties`, and the last assignment
  of a name wins. Loose-file and glob targets accept no MSBuild properties, so the
  file's properties apply only to solution, project and directory targets.
- `--select` replaces the file's `select` and `ignore`, so an explicit selection
  can show a rule that the file ignores. `--ignore` and `--exclude` add to the file's
  lists.
- Without a file, `--rules` and a target are required, as before.

## Rule selection

`select` and `ignore` choose rules by ID; `ignore` wins when both name one. The
built-in `drillpress-allow-unused` and `drillpress-allow-invalid` checks can be
selected and ignored like other rules. A rule ID that the bundle does not define
fails the run with exit code 2 and lists the IDs it does define.

Unselected rules are not evaluated, so a narrow selection is also faster. The
selection governs `check`, `fix` and `status` output and the findings exit code,
like `--fix-complexity`. An allowance comment that names an unselected rule is
neither used nor reported as unused.

## Reporting exclusions

`exclude` globs use the same rules as glob targets: `*` matches within a path
segment, `?` matches one character, `**` crosses segments (and `**/` also matches no
directory), and matching is case-sensitive on every platform. A pattern ending in
`/` covers everything below that directory. Patterns are matched against full file
paths after resolving them as described above.

Excluded files still load. They keep contributing compilations and semantic facts,
such as declarations, references, implementations and fix validation, so code
elsewhere is analysed and fixed exactly as before. Only what would land in an
excluded file is dropped:

- findings, including those that `ReportAt` moves into an excluded file;
- fixes that edit an excluded file or move a file into an excluded path, which are
  withheld (`--explain-fixes` says why);
- `drillpress-allow` comments in excluded files, which are not read;
- coverage collection for candidates that would report there, and for projects that
  `CollectCoverageIn` names when all their source is excluded.

A project-level finding reports at the project's first source file that is not
excluded, and a project whose files are all excluded reports nothing. A linked file
is excluded in every project that compiles it. Exclusions combine with the
project-name exclusions of `RuleCatalog.ExcludeProjects`.

Typical candidates are vendored or third-party source, the rules project and its
tests, and test fixtures that deliberately break conventions, such as
`"exclude": ["third_party/", "eng/", "tests/**/Fixtures/"]`.

## Exceptions

Choose the narrowest place for an exception:

- **In the rule.** Rule logic and parameters stay in C#; there is no separate rule
  language. Narrow the query with `Where`, `ExceptWhen` or place filters such as
  `InNonTestProjects()`, and exclude whole projects from every rule with
  `RuleCatalog.ExcludeProjects`. See [rule authoring](RULE_AUTHORING.md#excluding-projects).
- **In the repository.** `exclude` paths that follow other conventions, and `ignore`
  rules the repository does not enforce yet.
- **At one finding.** `// drillpress-allow RULE-ID: reason` accepts a single finding;
  see [allowing individual findings](RULE_AUTHORING.md#allowing-individual-findings).

Scoped and bulk suppression, such as file or range directives and generated
allowances for adopting a rule in an existing code base, is tracked in
[#28](https://github.com/MortenChristiansenOrg/drill-press.net/issues/28).

## Local and CI workflow

Pin the tool in `.config/dotnet-tools.json` and the SDK packages in the rules project
at the same exact version, then run:

```sh
dotnet tool restore                     # the pinned drillpress tool
dotnet test eng/Rules.Tests             # if your rules have tests
dotnet restore Shop.slnx                # the target; Drill Press does not restore it
dotnet tool run drillpress -- check     # builds eng/Rules, then checks Shop.slnx
```

`check` and `status` exit with 0 when there are no violations (review findings may
still print), 1 when there are violations, and 2 when the check could not run, such as
an invalid `drillpress.json`, a rules build error or a target that does not load.
Locally, or for an agent, `drillpress fix` applies the safe fixes and reports what
remains with the same exit codes; review its changes before committing.

In CI, run `check` and let a non-zero exit fail the job. To tell violations from
failures, branch on the code:

```yaml
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: 10.x
- run: dotnet tool restore
- run: dotnet restore Shop.slnx
- name: Check conventions
  run: |
    set +e
    dotnet tool run drillpress -- check
    code=$?
    if [ "$code" -eq 1 ]; then echo "::error::Convention violations; run drillpress fix locally."; fi
    exit "$code"
```
