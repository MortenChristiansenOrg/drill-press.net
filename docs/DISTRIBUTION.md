# Package distribution

Release tags use `vMAJOR.MINOR.PATCH` or `vMAJOR.MINOR.PATCH-PRERELEASE`,
for example `v1.0.0` or `v1.0.0-rc.1`. NuGet package versions omit the leading
`v`. Prereleases retain their complete suffix. Numeric components cannot have
leading zeroes and must be 0–65534 to fit assembly metadata. Build metadata
(`+...`) is not accepted in release tags. Numeric prerelease identifiers cannot
have leading zeroes either.

All `0.0.X` versions remain experimental and may break compatibility. A NuGet
prerelease suffix explicitly identifies previews at any version. Pin the CLI
and all SDK packages to the same exact version and rebuild bundles on upgrade;
protocol compatibility requires the full version, including the prerelease suffix.

## Packages and supported boundaries

| Package | Purpose | Drill Press dependencies |
| --- | --- | --- |
| `DrillPress.RuleAuthoring` | Public C# query, rule, analysis, and edit APIs | Manifest |
| `DrillPress.Linq` | Optional maintained standard LINQ declaration facts | RuleAuthoring |
| `DrillPress.Engine` | `RuleApplication` and compiled bundle execution | RuleAuthoring, Manifest |
| `DrillPress.Testing` | Existing in-process consumer test workspace | Engine |
| `DrillPress.Manifest` | Snapshot, response, diagnostic, and edit contracts | None |
| `DrillPress.Cli` | .NET tool command `drillpress` | Bundled runtime files; no consumer package references |

Each library ships its XML API documentation. Its exported public types are the
public API surface. Roslyn syntax, symbol, compilation, operation, and metadata
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

Add `Program.cs` defining a consumer-owned spelling preference. This example
reports findings; add a fix with the [generic edit APIs](SDK_CAPABILITIES.md#safe-corrections)
when your bundle can prove the replacement safe:

```csharp
using DrillPress;
using DrillPress.Engine;
using DrillPress;

var rules = new RuleSet();
rules.For(CodeType.Of<string>().Member(nameof(string.Empty)).References)
    .Forbid("EMPTY", "Use an empty literal.");
return (int)await new RuleApplication().RunAsync(rules, args);
```

Build rules and restore the separate target explicitly, then use the installed
local tool (or `drillpress` for a global installation):

```sh
dotnet build Rules.csproj -c Release
dotnet restore /path/to/Target.csproj
dotnet tool run drillpress -- check --rules bin/Release/net10.0/Rules.dll /path/to/Target.csproj
```

Use OS-appropriate paths. CLI-supplied rule, target, and explicit BuildHost paths
are relative to the invocation's working directory. Packaged assets are always
relative to the installed tool, independent of that directory. Local tool
discovery follows the .NET tool manifest's normal directory search rules.
The CLI does not restore or build rules or targets. Managed execution needs no
native compiler; explicitly supplied native bundles remain supported.

## Compatibility and upgrades

`drillpress --version` identifies the package version and snapshot/response
protocol numbers. Snapshots and responses carry their producer's exact package
version; incompatible or missing versions fail before findings or edits are
accepted. Snapshot format 5 records explicit build overrides for coverage
collection; response format 8 carries per-occurrence evidence and optional rule fix
complexity alongside stable remediation. Source preview bundles with earlier formats must be rebuilt.

Update the local tool with `dotnet tool update DrillPress.Cli --version 1.0.0-rc.1`
(or add `--global` for a global installation), update all SDK references to
`[1.0.0-rc.1]`, then rebuild every rules bundle. Commit the updated local manifest.
Replace the example version with the desired published release. Read that release's
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

`Directory.Build.props` defines the default source-build version. Release CI
supplies the tag-derived `-p:Version` consistently to build and pack without
editing tracked files. For local validation, pass the same value to both commands,
for example `-p:Version=1.0.0-rc.1`; do not override `PackageVersion` alone.
Assembly versions use the numeric `MAJOR.MINOR.PATCH.0`; informational versions
retain the full package version without an appended commit hash. The pack gate
checks numeric assembly identity and a build version stamp to reject stale
builds, including a different prerelease with the same numeric version. The
release inventory additionally reads the actual informational version of every
Drill Press assembly, including those bundled with the CLI.

Only the four SDK libraries and the CLI are packable. Package validation checks
reference/runtime API consistency inside each library package. No previous API
baseline is enforced; wire version compatibility is tested separately.

The installed-package integration tests pack the actual nupkg files into a
temporary feed, use a fresh package cache and isolated tool home, compile an
external consumer with PackageReferences only, install locally and globally,
restore the pinned manifest, and run check/fix/recheck from another directory.
Package source mapping restricts `DrillPress.*` to that temporary feed; only
third-party dependencies come from nuget.org. Ordinary Linux CI runs this gate
with the full correctness suite. Releases additionally require Windows tests
and native parity on both platforms.

To install your locally built artifact manually, add
`--add-source /absolute/path/to/artifacts/packages` to the tool installation
command and add that directory as a package source for your consumer project.
Use a fresh consumer/package cache when replacing an unpublished artifact with
the same version; published versions must never be overwritten.

## Automatic publication

The project and packages use the [MIT license](../LICENSE). Publication targets
**nuget.org**. Before the first release:

Publishing uses [NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
with GitHub OIDC. No long-lived API key or `NUGET_API_KEY` GitHub secret is needed.

1. Ensure your NuGet account owns or can publish all six package IDs listed above.
2. Open the repository's **Settings → Environments** and create or select **nuget**.
   Under **Deployment branches and tags**, select **Selected branches and tags**
   and add a **Tag** rule matching `v*`. Leave required reviewers and wait timers
   disabled for unattended releases. Protect release tags against deletion or
   movement and limit who can create them.
3. In that environment's **Environment variables**, add **NUGET_USER** with your
   nuget.org account's profile name, not your email address or GitHub username.
   For an organization-owned policy, use the NuGet user who created the policy
   and ensure that user remains an active member of the NuGet organization.
4. On nuget.org, open your account menu → **Trusted Publishing** and add a GitHub
   policy with these values:

   | Policy field | Value |
   | --- | --- |
   | Repository Owner | `MortenChristiansenOrg` |
   | Repository | `drill-press.net` |
   | Workflow File | `release.yml` (filename only) |
   | Environment | `nuget` |

   Choose the NuGet user or organization that owns the packages as the policy
   owner. Allow **Push new packages and package versions** so the first release
   can create the package IDs. Set the package glob pattern to `DrillPress.*`.

If you already created a long-lived key using the previous setup instructions,
remove its GitHub secret and revoke that key on nuget.org. The workflow no longer
reads it. A policy shown as temporarily active must receive a successful publish
within its activation window; if it expires first, reactivate it in nuget.org.

Push a tag on the commit to release; no manual package edits or uploads are needed:

```sh
git tag -a v1.0.0-rc.1 -m "Release 1.0.0-rc.1"
git push origin v1.0.0-rc.1
# For a stable release, use v1.0.0 instead.
```

The `Publish release` workflow validates the exact tagged commit on Windows and
Linux: formatting, Release build, pack, complete package inventory, unit and
integration tests, and CLI/SDK installation from copies of the actual artifacts.
Formatting runs once on Linux. Separate native jobs require managed/NativeAOT
parity on both platforms before publication; they do not rerun the test suite
or collect performance measurements.

PRs and main-branch pushes build and pack the source version once on Linux and
run the full test suite against those packages, without publishing or retaining
package artifacts. Stable/prerelease version rules remain covered by unit tests;
the full OS/version Cartesian matrix is not repeated for ordinary changes.
NuGet dependencies are cached, and superseded CI runs are cancelled. There are
no scheduled jobs or external xUnit repository checks.

Malformed `v*` tags fail validation; other tags
are ignored. Only the tag workflow's publication job receives `id-token: write`
and permission to create GitHub releases. After validation and artifact download,
the pinned `NuGet/login` action exchanges the job's OIDC identity for a temporary
NuGet credential.
The publisher receives that action output through its `NUGET_API_KEY` process
environment variable; the value is not stored as a GitHub secret. NuGet credentials
last one hour and are requested immediately before the publication step.

After package validation and native parity pass on both platforms, CI downloads
the validated Linux package artifact, revalidates it, and publishes in dependency
order. It verifies that **all six**
packages are retrievable from nuget.org before creating or updating the GitHub
release with package links. Prereleases are marked as such and never promoted
to the latest stable GitHub release. Validated packages are retained as workflow
artifacts for 90 days.

Install a specific prerelease the same way as a stable release:

```sh
dotnet tool install DrillPress.Cli --version 1.0.0-rc.1
# In a consumer project:
dotnet add package DrillPress.Engine --version 1.0.0-rc.1
```

Use `[1.0.0-rc.1]` in SDK PackageReferences to pin exact versions. An explicit
prerelease version does not need an additional `--prerelease` switch.

### Failed releases and reruns

NuGet publication is not atomic. A failure can leave only some packages published.
Same-tag workflow runs are serialized and do not cancel a running publication.
Fix policy configuration or connectivity, then use **Re-run failed jobs** on the
original workflow run so publication reuses its validated artifacts and obtains
a fresh OIDC credential. For authentication failures, check `NUGET_USER`, the
policy owner and activation status, and the repository, workflow filename, and
environment values against the table above. An expired temporary credential also
requires rerunning the job; no stored key needs rotation. Do not move
the tag, rebuild modified source under the same version, or overwrite packages.

Before any upload, CI compares every already-published package's files with its
validated local artifact. NuGet's added `.signature.p7s` and ZIP container metadata
are excluded from comparison; package payload files must match exactly. Identical
packages are skipped and missing packages are uploaded. A content conflict fails
the run and requires investigation and a new version, never an overwrite. Indexing
can take time: CI waits up to ten minutes per package for availability. If this
expires, rerun after indexing completes. Transient HTTP failures also fail safely
and can be retried through the workflow.

If all packages were published but GitHub release creation failed, rerunning the
publication job verifies and skips them, then creates or updates the release.
If artifacts have expired, a complete rerun rebuilds the tagged source and still
requires exact payload matches for any existing packages; use a new version if
the rebuild differs. A failed run must not be interpreted as a complete release.

For local validation without any publication:

```sh
dotnet run --project tools/DrillPress.Release -- version v1.0.0-rc.1
dotnet run --project tools/DrillPress.Release -- validate v1.0.0-rc.1 artifacts/packages
```
