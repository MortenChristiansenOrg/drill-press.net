# Project

This project is a .NET lint rule engine focused on speed and ease of use.

The primary use case of this tool is for an LLM to find all the places where
it does not follow coding conventions and allow it to fix them in the most
efficient way. All other use cases are secondary to this. It is important that
the output of the tool is highly optimized to LLM consumption. This includes
minimizing the amount of text generated. Other use cases can be supported but
might require opt-in flags for more detailed information, etc.

# Code conventions

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
- Use in-memory filesystems for file-policy unit tests; reserve real filesystem
  and process I/O for integration tests. Share repeated setup and isolate resources.
- Structure tests as arrange, act, and assert groups separated by blank lines,
  without labels, conditional logic, or `try`/`finally`. Only `Assert.Throws` may
  appear in the act group.
- Assert complete output values, using raw string literals for multiline text.
- Keep package versions centralized and current.

# Filesystem boundaries

- All active application-owned file/directory access uses an injected
  `System.IO.Abstractions.IFileSystem`, including snapshots, metadata assembly
  reads, temporary files, benchmark plans, publish inventories, and reports.
  Use its path API when resolving paths or depending on the current directory.
  Pure string path transformations need no filesystem dependency.
- Create `FileSystem` only at executable entry points, BenchmarkDotNet setup
  (a separate process entry), and integration-test fixtures. Require injection
  elsewhere; do not add hidden real-filesystem defaults or static global state.
- Internal files are our own exchange/storage contracts, not a reason for a
  second stream API. Expose one path-based persistence API using the injected
  filesystem. Keep genuine streams for compiler-generated metadata images,
  console/process pipes, and private serialization internals.
- External files are consumed by MSBuild, source generators, child processes,
  NativeAOT tooling, or OS probes. Our side still uses `IFileSystem`, but the
  external consumer requires real OS paths. Unit-test our policy with both a
  `MockFileSystem` and a fake external loader/runner; exercise real consumers,
  cancellation, platform behavior, and performance in integration tests.
- These conventions cover the active root solution and its tools, scripts,
  samples, and tests. `AOT POC/` is the historical standalone proof of concept,
  not maintained production code; apply these conventions when porting from it.
