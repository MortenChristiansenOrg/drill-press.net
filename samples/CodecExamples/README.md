# SDK example target

This deliberately imperfect codec library demonstrates the rules in
`../DrillPress.SampleRules/ShowcaseRules.cs`. It has blocking work below an async
entry point, no round-trip test counterparts, and a redundant accessibility
modifier with an eligible fix. The two codec implementations keep the older
single-implementation interface rule from obscuring these examples.

`JsonCodec.SendWithRetryAsync` encodes a message and retries a timed-out send
once. Its backoff helper blocks a thread with `Thread.Sleep`; SDK2003 follows
the call path from the async entry point. An awaited `Task.Delay` would express
the nonblocking backoff this code intends.

`PooledUtf8Decoder.ReadDeferred` demonstrates a concrete ownership bug: it
returns a callback that reads a rented array, then returns the array to the pool
before the callback can run. SDK2010 identifies the `ArrayPool<T>.Rent` call and
the compiler's captured local, not a special variable name. Decode before
returning the lease, or copy to an owned buffer before creating the callback.
The rule deliberately bans even immediately invoked captures; it is an ownership
convention, not an escape/lifetime proof.

SDK2004 expects a genuine xUnit Fact/Theory named `<CodecName>RoundTrip` in a
referencing test project. A similarly named helper is not a test. Randomized
test inputs and test console output are excluded from production call policies.

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
