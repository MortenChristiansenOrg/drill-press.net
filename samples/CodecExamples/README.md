# SDK example target

This deliberately imperfect codec library demonstrates the rules in
`../DrillPress.SampleRules/ShowcaseRules.cs`. It has blocking work below an async
entry point, no round-trip test counterparts, and a redundant accessibility
modifier with an eligible fix. The two codec implementations keep the older
single-implementation interface rule from obscuring these examples.

From the repository root:

```sh
dotnet build DrillPress.slnx
dotnet restore samples/CodecExamples/CodecExamples.csproj
dotnet src/DrillPress.Cli/bin/Debug/net10.0/DrillPress.Cli.dll check --build-host src/DrillPress.BuildHost/bin/Debug/net10.0/DrillPress.BuildHost.dll --rules samples/DrillPress.SampleRules/bin/Debug/net10.0/DrillPress.SampleRules.dll samples/CodecExamples/CodecExamples.csproj
```

The showcase policies deliberately apply only to projects whose names start
with `CodecExamples`. Their consumer tests supply positive and negative
snippets for all thirteen policies, including accepted-baseline input and
synthetic package facts. `ShowcaseRules.Create(acceptedBaseline)` enables the
optional baseline policy; ordinary CLI runs do not automatically load Git history.
