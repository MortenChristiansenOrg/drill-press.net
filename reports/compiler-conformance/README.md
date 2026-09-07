# Compiler snapshot conformance

Captured on Linux x64 with SDK 10.0.111. The fixture covers two frameworks,
aliased source and embedded interop metadata references, linked conditional
source, generator output, explicit test-project classification, unsafe/overflow
settings, and per-tree compiler severity. CI repeats the fixture on Windows and
Linux and retains its report with the native-bundle artifacts.

The xUnit report covers 94 evaluated contexts and 2,260,924 semantic/diagnostic
probes at source revision `6bbefaed1d0a995bc9970800384f9e8a1b9d2331`. Both reports
compare resolved symbols (including overload parameter types), expression
conversions, declarations, compiler diagnostics, and complete current-rule
responses. There are no mismatches. Machine-specific target roots are replaced
with `<xunit>` in the checked-in report.

Run `scripts/XunitConformance.cs` as documented in the root README to recreate
the checkout/submodules, dependency lockfiles, preparation log, and report. Its
59 separately generated dependency lockfiles remain under the checkout's `obj`
directories and are copied to the report directory. The checked-in hash manifest
identifies this run's dependency baseline without duplicating upstream package
lock content. The harness leaves upstream tracked files unchanged. Its source
pin and dependency baseline are shared inputs for later rule conformance and
performance work; these reports make no performance claim.
