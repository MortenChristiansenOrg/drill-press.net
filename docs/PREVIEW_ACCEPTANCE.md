# Technical preview acceptance evidence

This is the evidence map for issues [#9](https://github.com/MortenChristiansenOrg/drill-press.net/issues/9)
through [#16](https://github.com/MortenChristiansenOrg/drill-press.net/issues/16).
The technical preview requires all rows below and successful Windows/Linux
jobs on the PR being accepted. A completed publication without native execution,
a partial performance report, or a finding-count match alone does not pass.

The [continuous native workflow](../.github/workflows/native-bundles.yml) runs
Release builds, all unit/integration tests, native execution/parity, real fixes,
and compiler-fixture conformance on every PR and main push. The
[repository workflow](../.github/workflows/repository-preview.yml) additionally
runs pinned xUnit conformance and the complete performance harness on both
platforms for relevant PRs and manual runs. OS-specific permission tests run on
their corresponding platform; the other platform skips that single test.

## Prior slice criteria

| Criteria | Evidence |
| --- | --- |
| s1.1–s1.3 | [Shared build settings](../Directory.Build.props), Release build with warnings as errors, and [CLI dependency-boundary test](../tests/DrillPress.IntegrationTests/DrillPress.Cli/CliTests.cs). |
| s1.4–s1.10 | [BuildHost tests](../tests/DrillPress.IntegrationTests/DrillPress.BuildHost/BuildHostTests.cs), [consumer tests](../tests/DrillPress.IntegrationTests/DrillPress.SampleRules/ConsumerTests.cs), [CLI unit orchestration](../tests/DrillPress.UnitTests/DrillPress.Cli/CliApplicationTests.cs), and real [clean/findings/failure CLI cases](../tests/DrillPress.IntegrationTests/DrillPress.Cli/CliTests.cs). |
| s2.1–s2.6 | [Native verification session](../tools/DrillPress.BundleVerification/VerificationSession.cs) builds, publishes, and executes managed/native clean, violating, invalid, linked, and inactive-context cases with exact bytes and exit codes; [warning policy](../tools/DrillPress.BundleVerification/PublishWarnings.cs) rejects unexplained AOT/trim warnings. |
| s2.7–s2.8 | [Native gate script](../scripts/NativeBundles.cs), retained `native-bundle-report-*` measurements, and [platform prerequisites and commands](TECHNICAL_PREVIEW.md). |
| s3.1–s3.4, s3.13, s3.15 | [Snapshot envelope and identity tests](../tests/DrillPress.UnitTests/DrillPress.Manifest/CompilationSnapshotFileTests.cs), [response protocol](../tests/DrillPress.UnitTests/DrillPress.Manifest/BundleResponseProtocolTests.cs), [complete response validation](../tests/DrillPress.UnitTests/DrillPress.Manifest/BundleResponseValidatorTests.cs), [source encodings/spans](../tests/DrillPress.UnitTests/DrillPress.Manifest/SourceIdentityTests.cs), and malformed-response CLI tests. |
| s3.5–s3.6, s3.8, s3.14 | [Response-validator tests](../tests/DrillPress.UnitTests/DrillPress.Manifest/BundleResponseValidatorTests.cs) cover reporting agreement, non-reporting affected contexts, inactive source, full-batch conflicts, and independent surviving fixes. |
| s3.7, s3.9–s3.12, s3.14 | [Renderer goldens](../tests/DrillPress.UnitTests/DrillPress.Manifest/CompactDiagnosticRendererTests.cs) cover culture, escaping, UTF-16 coordinates, same-line positions, ordering, and empty output. Native parity extends the internal/public goldens; benchmark reports retain public byte/token estimates. |
| s4.1–s4.2, s4.9–s4.11, s4.14 | [Target loader tests](../tests/DrillPress.IntegrationTests/DrillPress.BuildHost/TargetLoadingTests.cs), full [CLI failure cases](../tests/DrillPress.IntegrationTests/DrillPress.Cli/CliTargetTests.cs), [CLI property/validation forwarding](../tests/DrillPress.IntegrationTests/DrillPress.BuildHost/CompilerConformanceTests.cs), and [all six target shapes through managed/native CLI](../tools/DrillPress.BundleVerification/TargetCoverageCase.cs). |
| s4.3–s4.8, s4.12–s4.13, s4.15, s4.17 | [Compiler conformance fixture](../fixtures/CompilerSnapshot), [graph/generator tests](../tests/DrillPress.IntegrationTests/DrillPress.BuildHost/CompilerConformanceTests.cs), [fast/error/encoding cases](../tests/DrillPress.IntegrationTests/DrillPress.BuildHost/TargetLoadingTests.cs), and [reconstruction tests](../tests/DrillPress.UnitTests/DrillPress.Engine/AnalysisEngineTests.cs). |
| s4.12, s4.16 | [Single pinned repository preparer](../tools/DrillPress.Conformance/PinnedXunit.cs), [xUnit command](../scripts/XunitConformance.cs), and each platform's retained `conformance/conformance.json` and preparation/dependency-lock evidence. Every context comparison and rule parity must pass. |
| s5.1–s5.7, s5.17 | [Authoring API tests](../tests/DrillPress.UnitTests/DrillPress.RuleAuthoring), [generic/named identities](../tests/DrillPress.IntegrationTests/DrillPress.RuleAuthoring/CodeTypeTests.cs), [explicit sample registration](../samples/DrillPress.SampleRules), and [authoring examples](RULE_AUTHORING.md). |
| s5.8–s5.10, s5.15, s5.18 | [Test-body integration cases](../tests/DrillPress.IntegrationTests/DrillPress.RuleAuthoring/TestBodyTests.cs) cover blank lines, token contents, nested functions, inherited attributes, assertions, and exception cases. |
| s5.11, s5.15–s5.16, s5.19 | [Interface implementation tests](../tests/DrillPress.IntegrationTests/DrillPress.RuleAuthoring/InterfaceImplementationsTests.cs) cover generated/partial/generic/inherited types, source dependencies, evaluated test classification, and separate frameworks. |
| s5.12–s5.16, s5.20 | [Empty-string fix cases](../tests/DrillPress.IntegrationTests/DrillPress.RuleAuthoring/EmptyStringFixTests.cs), [ordinal-comparer cases](../tests/DrillPress.IntegrationTests/DrillPress.RuleAuthoring/OrdinalComparerFixTests.cs), and [all-rule managed/native contract](../tools/DrillPress.BundleVerification/RuleCoverageCase.cs). |
| s5.21 | [Conformance application](../tools/DrillPress.Conformance/ConformanceApplication.cs) compares live/reconstructed responses including all five rules and complete proposed batches, on both the compiler fixture and pinned xUnit solution. |
| s6.1–s6.5, s6.12–s6.13 | Shared [response validation](../tests/DrillPress.UnitTests/DrillPress.Manifest/BundleResponseValidatorTests.cs), [fix application](../tests/DrillPress.UnitTests/DrillPress.Manifest/FixPlanApplierTests.cs), and [file eligibility](../tests/DrillPress.UnitTests/DrillPress.Manifest/SourceFilePolicyTests.cs) cover whole-plan agreement, duplicate edits, conflicts, stale bytes, and preparation failures. |
| s6.6–s6.11, s6.16 | Real [CLI fix and sample rebuild](../tests/DrillPress.IntegrationTests/DrillPress.Cli/CliFixTests.cs), [native/managed fix verification](../tools/DrillPress.BundleVerification/FixVerificationCase.cs), encoding/UTF-16 tests, and the [write policy](FIXING.md). |
| s6.14–s6.15 | [Late replacement integration tests](../tests/DrillPress.IntegrationTests/DrillPress.Manifest/FixPlanApplierTests.cs), [CLI recheck/recovery tests](../tests/DrillPress.UnitTests/DrillPress.Cli/CliFixTests.cs), and [real cancellation cleanup](../tests/DrillPress.IntegrationTests/DrillPress.Cli/ProcessCancellationTests.cs). |
| s7.1–s7.4, s7.9–s7.10 | [Profiling guide and command](PROFILING.md), [stored pre-optimization evidence](../reports/performance), [profiling tests](../tests/DrillPress.UnitTests/DrillPress.Manifest/PipelineProfileTests.cs), [CLI stdout/profiling parity](../tests/DrillPress.IntegrationTests/DrillPress.Cli/CliProfilingTests.cs), and platform repository artifacts. |
| s7.5–s7.8 | [Member-index/composition parity tests](../tests/DrillPress.IntegrationTests/DrillPress.RuleAuthoring/MemberCandidateIndexTests.cs), [interface tests](../tests/DrillPress.IntegrationTests/DrillPress.RuleAuthoring/InterfaceImplementationsTests.cs), and all four modes in each complete repository report. |
| s7.11–s7.12 | [Signature normalization](../tests/DrillPress.UnitTests/DrillPress.Benchmarks/SignatureNormalizationTests.cs), [full result comparison tests](../tests/DrillPress.UnitTests/DrillPress.Benchmarks/ResultSignaturesTests.cs), [disposable-copy tests](../tests/DrillPress.UnitTests/DrillPress.Benchmarks/DisposableRepositoryTests.cs), and real isolated fix/recheck runs with complete compiler-input and response/plan/public signatures. |

## Final preview criteria

| Criteria | Gate |
| --- | --- |
| s8.1–s8.3 | Successful Release build, complete test suite, NativeAOT publication and actual managed/native byte parity on both native-workflow runners. |
| s8.4 | `TargetCoverageCase` runs `.sln`, `.slnx`, project, directory, C# file, and quoted glob through CLI → BuildHost → each bundle mode on both platforms. |
| s8.5–s8.6 | Compact-output goldens, real managed/native fixes, preserved source bytes, recheck-only stdout, and buildable sample solution. |
| s8.7 | [CLI permission integration tests](../tests/DrillPress.IntegrationTests/DrillPress.Cli/CliSnapshotPermissionsTests.cs) inspect live directory/snapshot access and cancellation cleanup. Existing CLI cases verify clean/findings/failure cleanup; unit tests verify permission failure starts no child. |
| s8.8, s8.14–s8.15 | Protocol, affected-context, encoding, target/SDK/restore, late replacement, and recheck cases above run in the shared Windows/Linux test suite. The guide states loaded-graph scope, per-file atomicity, single-pass behavior, and recovery. |
| s8.9–s8.11 | [Fresh-checkout guide](TECHNICAL_PREVIEW.md), [rule authoring](RULE_AUTHORING.md), [fix recovery](FIXING.md), and exact CLI help goldens. |
| s8.12, s8.16 | Both repository jobs reuse `PinnedXunit` and `XunitConformance.cs`, preserve complete reports and raw diagnostics for 90 days, and impose semantic parity rather than timing/count thresholds. The earlier native execution gate remains continuous. |
| s8.13 | All prior criteria are mapped above; both workflows must pass before declaring the preview accepted. |

The PR's validation record links the actual Windows/Linux workflow runs and
their artifacts. In repository artifacts, require `performance/report.json`
to have `complete: true`; failures retain their available evidence and fail the
job. The reports record revisions, SDKs, workload options, input signatures,
measurement scopes, and repetition counts so a later run can be compared without
turning one machine's result into a release contract.
