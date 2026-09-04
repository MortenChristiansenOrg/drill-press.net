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
- Keep filesystem and process I/O in integration tests. Share repeated test setup
  and give parallel tests isolated resources.
- Structure tests as arrange, act, and assert groups separated by blank lines,
  without labels, conditional logic, or `try`/`finally`. Only `Assert.Throws` may
  appear in the act group.
- Assert complete output values, using raw string literals for multiline text.
- Keep package versions centralized and current.
