# Applying safe fixes

```sh
dotnet run --project src/DrillPress.Cli -c Release -- \
  fix --build-host src/DrillPress.BuildHost/bin/Release/net10.0/DrillPress.BuildHost.dll \
  --rules samples/DrillPress.SampleRules/bin/Release/net10.0/DrillPress.SampleRules.dll \
  'Sample Solution/DrillPress.SampleTarget.slnx'
```

Use the same target and options as `check`. Restore SDK projects first; BuildHost
loads their current compilation without restoring or building. Rule bundles are
trusted local executable code. Review the bundle before running it.

`fix` analyzes every loaded context, validates the bundle response, and applies
all retained safe edits once. Reporting contexts must agree on each complete
batch, and every affected context must validate it, including contexts with no
finding. Duplicate edits are written once. Conflicts withhold whole batches;
unrelated safe batches still apply. Generated, foreign, and non-editable targets
cannot be written even if a fix factory proposes them from another document.

DP1004 replaces proven `string.Empty` references with `""`. DP1005 removes
`StringComparer.Ordinal` only from the proven `Enumerable.Distinct<string>`
call shapes described in [rule authoring](RULE_AUTHORING.md). Other findings
remain for manual correction. The CLI never invents edits.

Before changing any original, the CLI validates every edit and original byte
sequence, computes all new contents, and writes every output to a temporary
file in its source directory. Original encoding, byte-order mark policy, and
unchanged line endings are preserved. Invalid surrogate spans and replacements
that cannot be encoded losslessly fail preparation. Unix temporary files start
with owner-only permissions; replacements preserve the target's Unix mode.
Windows replacement uses the OS replacement operation and its metadata policy.

Automatic writes require an ordinary writable file whose physical identity can
be established on Linux or Windows. Hard links, symbolic links, reparse points,
and loaded paths aliasing the same file are withheld. An unknown source identity
withholds all edits because that path could alias another loaded source. Stable
snapshot path identities remain separate from OS inode/volume/file identifiers.

Stop concurrent writers, formatters, and save-on-focus actions before running
`fix`. The CLI checks source bytes, physical identity, and eligibility again
immediately before each replacement, but these checks and replacement cannot
form a transaction with another process. Each file replacement is atomic;
the multi-file operation is not a filesystem-wide transaction or a durability
guarantee against machine failure.

A preparation failure leaves all originals untouched. A later replacement
failure or cancellation stops further writes, retains earlier replacements,
cleans unused temporary files, and exits 2. Stderr lists `changed`, `failed`,
and `pending` paths for recovery; paths are escaped when necessary. Inspect
those current files, resolve the failure, and rerun `check` or `fix`. The CLI
does not roll back over possible user changes. A cleanup failure names its
remaining temporary file.

After any successful writes, BuildHost regenerates the same target with the
same options and the rules run once more. Only the remaining compact diagnostics
reach stdout. If regeneration or recheck fails, changed files remain in place,
the CLI reports that verification failed, emits no stale diagnostics, and
exits 2. Cancellation during verification lists the changed files and reaches
the CLI cancellation handler, which reports cancellation and exits 2. If no
changes survive, the initial completed analysis supplies the
output without another export. There is no automatic iteration loop.

Exit 0 means a completed clean check, 1 means remaining findings, and 2 means an
operational failure. Routine successful fixes produce no progress messages.
