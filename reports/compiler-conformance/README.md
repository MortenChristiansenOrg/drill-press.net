# Compiler snapshot conformance

Captured on Linux x64 with SDK 10.0.111. The fixture covers two frameworks,
aliased source and embedded interop metadata references, linked conditional
source, generator output, explicit test-project classification, unsafe/overflow
settings, and per-tree compiler severity. The integration suite repeats the
fixture on Linux during ordinary CI and on both Windows and Linux for releases.

The report compares resolved symbols (including overload parameter types),
expression conversions, declarations, compiler diagnostics, and complete
current-rule responses. There are no mismatches in this captured baseline.
Use the compiler conformance command in the root README to produce a fresh
report. The external xUnit repository reports and harness have been removed.
