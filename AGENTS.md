# Project

This project is a .NET lint rule engine focused on speed and ease of use.

The primary use case of this tool is for an LLM to find all the places where
it does not follow coding conventions and allow it to fix them in the most
efficient way. All other use cases are secondary to this. It is important that
the output of the tool is highly optimized to LLM consumption. This includes
minimizing the amount of text generated. Other use cases can be supported but
might require opt-in flags for more detailed information, etc.

# Code conventions

- Run the repository-local CSharpier on all changed C# and XML code before
  committing: `dotnet tool restore`, then `dotnet csharpier format <changed-paths>`.
  Use `dotnet csharpier format .` for the entire solution and
  `dotnet csharpier check .` to verify formatting. Accept its formatting; do not
  add ignore comments for stylistic preferences. Use a narrowly scoped
  `// csharpier-ignore - <reason>` (or a ranged ignore) only when source layout
  is itself an input to formatting-specific rule logic or validation.
- Clean up related obsolete code, documentation, and generated artifacts before completing a PR.
- Keep DTO-only records together; give each type with behavior its own file. Use
  descriptive domain names and typed enums for process outcomes.
- Document every public API under `src` with useful XML comments; omit comments
  that merely restate the code.
- Rely on nullable reference types for pure null checks, but still validate
  non-empty values. Omit explicit ordinal comparers when ordinal is the default.
- Split long methods along meaningful responsibilities and refine framework base
  types once at their boundary instead of scattering casts.
- Cover non-trivial production logic through public APIs. Mirror production
  projects and files in the unit-test layout; do not expose members for tests.
  Unit tests may configure internal dependency-injection constructors through
  friend-assembly access; assertions exercise public operations.
- Use in-memory filesystems for file-policy unit tests; reserve real filesystem
  and process I/O for integration tests. Share repeated setup and isolate resources.
- Structure tests as arrange, act, and assert groups separated by blank lines,
  without labels, conditional logic, or `try`/`finally`. Only `Assert.Throws` may
  appear in the act group.
- Assert complete output values, using raw string literals for multiline text.
- Keep package versions centralized and current.
- Prefix non-constant fields with `_`.
- Do not create interfaces with only a single non-test implementation. Instead
  of using interfaces to support test doubles, create fake implementations by
  subclassing production classes and override virtual members.

# Filesystem boundaries

- All active application-owned file/directory access uses an injected
  `System.IO.Abstractions.IFileSystem`, including snapshots, metadata assembly
  reads, temporary files, benchmark plans, publish inventories, and reports.
  Use its path API when resolving paths or depending on the current directory.
  Pure string path transformations need no filesystem dependency.
- Keep filesystem package types out of consumer-facing APIs and sample code.
  Public library constructors compose the real filesystem internally; internal
  constructors accept the shared injected dependencies. Tool entry points,
  BenchmarkDotNet setup, and integration fixtures also compose real filesystems.
  Require injection within implementations and avoid static mutable state.
- Internal exchange and storage contracts use path-based persistence APIs.
- External files are consumed by MSBuild, source generators, child processes,
  NativeAOT tooling, or OS probes. Our side still uses `IFileSystem`, but the
  external consumer requires real OS paths. Unit-test our policy with both a
  `MockFileSystem` and a fake external loader/runner; exercise real consumers,
  cancellation, platform behavior, and performance in integration tests.
- These conventions cover the root solution and its tools, scripts, samples,
  and tests.
