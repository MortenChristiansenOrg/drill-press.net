# Five-rule conformance

These Linux x64 reports use SDK 10.0.111 and the shared xUnit source pin
`6bbefaed1d0a995bc9970800384f9e8a1b9d2331`. They extend the
[compiler snapshot baseline](../compiler-conformance/README.md), with the same
checkout, submodules, and separate dependency lockfiles.

`xunit-linux-x64.json` records 94 evaluated contexts, 2,260,924 compiler probes,
and exact live/reconstructed equality of complete five-rule bundle responses.
The workload produced 2,020 context findings and 46 proposed batches; these
counts describe this run and are not release thresholds. The repository has
no allowlisted Distinct comparer correction, so the focused native fixture
exercises that fix explicitly.

`native-linux-x64.json` records managed/NativeAOT byte equality for an independent
fixture with all five diagnostics and both expected corrections. The verifier
constructs the expected spans, messages, complete edits, and validations from
known source; it does not use managed rule output as its expected answer.
The response hash identifies this run's complete bytes, including its temporary
paths and request IDs. CI retains the raw `all-rules.*.stdout` files with its
native-bundle artifacts on both Windows and Linux.

Reproduce from the repository root (use new report directories):

```sh
dotnet run --file scripts/XunitConformance.cs -c Release --no-cache -- /path/to/xunit artifacts/rule-conformance
dotnet run --file scripts/NativeBundles.cs -c Release --no-cache -- --output artifacts/rule-native
```

Use a checkout outside the Drill Press repository so its MSBuild configuration
cannot inherit this repository's centralized package settings. Neither command
applies proposed edits to the shared xUnit checkout.
