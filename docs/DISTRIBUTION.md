# Package distribution

Every `0.0.X` version is an alpha build and may break APIs, commands, or protocols
from any previous release. The numeric version is intentionally `0.0.X`, without
a NuGet prerelease suffix; NuGet does not require `--prerelease` to select it.
Pin versions explicitly. No compatibility with a different alpha version is
promised, even if its snapshot and response protocol numbers are unchanged.

## Packages and supported boundaries

| Package | Purpose | Drill Press dependencies |
| --- | --- | --- |
| `DrillPress.RuleAuthoring` | Public C# query, rule, analysis, and edit APIs | Manifest |
| `DrillPress.Engine` | `RuleApplication` and compiled bundle execution | RuleAuthoring, Manifest |
| `DrillPress.Testing` | Existing in-process consumer test workspace | Engine |
| `DrillPress.Manifest` | Snapshot, response, diagnostic, and edit contracts | None |
| `DrillPress.Cli` | .NET tool command `drillpress` | Bundled runtime files; no consumer package references |

Each library ships its XML API documentation. Its exported public types are the
alpha API surface. Roslyn syntax, symbol, compilation, operation, and metadata
types exposed by these APIs are deliberate public contracts; their NuGet
dependencies flow transitively. Use the Roslyn versions resolved with the SDK
instead of overriding them independently. Manifest DTOs are deliberate public
contracts where authoring and testing APIs expose captured compiler inputs.
Their serialized shape is an internal, version-checked process protocol, not a
durable storage format or an additional public diagnostic format.

Filesystem abstraction dependencies are implementation details. Public library
constructors compose real filesystems internally; consumers do not supply
`IFileSystem` or reference filesystem packages to use the SDK.

BuildHost is an implementation asset inside the tool, not a separate consumer
package. Its full publication output, including Roslyn's nested loader assets,
lives under `buildhost/` relative to the tool assembly. The CLI does not load
BuildHost, the engine, Roslyn, MSBuild, or rule assemblies into its own process.

## Minimal package consumer

In an ordinary directory outside this checkout, create `Rules.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DrillPress.Engine" Version="[0.0.1]" />
  </ItemGroup>
</Project>
```

The bracket syntax pins an exact NuGet version. Add `DrillPress.Testing` at the
same version when using the existing test workspace. Drill Press library
dependencies also require exact matching versions. Package installation does
not depend on this checkout's central package versions or build properties.

Add `Program.cs` using existing APIs:

```csharp
using DrillPress;
using DrillPress.Engine;
using DrillPress.Fixes;
using DrillPress.Semantics;

var rules = new RuleSet();
rules.For(CodeType.Of<string>().Member(nameof(string.Empty)).References)
    .Forbid("EMPTY", "Use an empty literal.", fix: EmptyStringFix.Create);
return (int)await new RuleApplication().RunAsync(rules, args);
```

Build rules and restore the separate target explicitly, then use the installed
local tool (or `drillpress` for a global installation):

```sh
dotnet build Rules.csproj -c Release
dotnet restore /path/to/Target.csproj
dotnet tool run drillpress -- check --rules bin/Release/net10.0/Rules.dll /path/to/Target.csproj
dotnet tool run drillpress -- fix --rules bin/Release/net10.0/Rules.dll /path/to/Target.csproj
```

Use OS-appropriate paths. CLI-supplied rule, target, and explicit BuildHost paths
are relative to the invocation's working directory. Packaged assets are always
relative to the installed tool, independent of that directory. Local tool
discovery follows the .NET tool manifest's normal directory search rules.
The CLI does not restore or build rules or targets. Managed execution needs no
native compiler; explicitly supplied native bundles remain supported.

## Compatibility and upgrades

`drillpress --version` identifies the package version and snapshot/response
protocol numbers. Snapshots and responses carry their producer's exact alpha
version; incompatible or missing versions fail before findings or edits are
accepted. Snapshot format 4 requires explicit project analysis scope; the response
format remains 2. Source preview bundles with earlier formats must be rebuilt.

Update the local tool with `dotnet tool update DrillPress.Cli --version 0.0.X`
(or add `--global` for a global installation), update all SDK references to
`[0.0.X]`, then rebuild every rules bundle. Commit the updated local manifest.
Replace `X` with the desired published release number. Read that release's
changes and adapt authoring code if required. Normal checks remain silent when
clean, with exit codes 0 clean, 1 findings, and 2 operational failure; version
errors go to stderr and produce no diagnostic stdout.

## Build and validate distribution artifacts

From the repository root:

```sh
dotnet tool restore
dotnet csharpier check .
dotnet build DrillPress.slnx -c Release
dotnet pack DrillPress.slnx -c Release -o artifacts/packages
dotnet test --solution DrillPress.slnx -c Release --no-build
```

`Directory.Build.props` defines the release version. For a different release,
change that value or supply the same `-p:Version=0.0.X` to build and pack; do not
override `PackageVersion` alone. Only the four SDK libraries and the CLI are
packable. Package validation checks reference/runtime API consistency inside
each library package. No previous-alpha API baseline is enforced because
breaking changes are allowed; wire version compatibility is tested separately.

The installed-package integration tests pack the actual nupkg files into a
temporary feed, use a fresh package cache and isolated tool home, compile an
external consumer with PackageReferences only, install locally and globally,
restore the pinned manifest, and run check/fix/recheck from another directory.
Package source mapping restricts `DrillPress.*` to that temporary feed; only
third-party dependencies come from nuget.org. The Windows x64 and Linux x64 CI
test jobs run this gate along with existing correctness and native parity tests.

To install your locally built artifact manually, add
`--add-source /absolute/path/to/artifacts/packages` to the tool installation
command and add that directory as a package source for your consumer project.
Use a fresh consumer/package cache when replacing an unpublished artifact with
the same version; published versions must never be overwritten.

## License and manual publication

The project and packages use the [MIT license](../LICENSE). The intended public
publication destination is **nuget.org**, using
`https://api.nuget.org/v3/index.json`. Building this repository does not publish
anything or establish ownership of package IDs on that feed. The maintainer
must have permission to publish the selected IDs before the first release.

After validating the five artifacts, publish them manually (POSIX shell):

```sh
dotnet nuget push 'artifacts/packages/*.nupkg' \
  --source https://api.nuget.org/v3/index.json \
  --api-key "$DRILLPRESS_NUGET_API_KEY"
```

In PowerShell use `$env:DRILLPRESS_NUGET_API_KEY` for the key. Supply it through
your shell's secret environment; do not commit it. Publish only the validated
version from a clean output directory. Publication automation and full
onboarding remain separate follow-up work.
