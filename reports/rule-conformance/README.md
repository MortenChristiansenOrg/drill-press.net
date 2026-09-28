# Five-rule conformance

This Linux x64 report uses SDK 10.0.111 and complements the
[compiler snapshot baseline](../compiler-conformance/README.md). The external
xUnit repository report and harness have been removed.

`native-linux-x64.json` records managed/NativeAOT byte equality for an independent
fixture with all five diagnostics and both expected corrections. The verifier
constructs the expected spans, messages, complete edits, and validations from
known source; it does not use managed rule output as its expected answer.
The response hash identifies this run's complete bytes, including its temporary
paths and request IDs. Release CI retains the raw `all-rules.*.stdout` files with
its native-bundle artifacts on both Windows and Linux.

Reproduce from the repository root (use new report directories):

```sh
dotnet run --file scripts/VerifyNativeBundles.cs -c Release -- --output artifacts/rule-native
```

Verification applies proposed edits only to its disposable fixture projects.
