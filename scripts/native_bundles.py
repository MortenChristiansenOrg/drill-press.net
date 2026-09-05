#!/usr/bin/env python3
"""Publish and exercise managed/native bundles against identical BuildHost snapshots."""

import argparse
import json
import os
import platform
import re
import subprocess
import sys
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SAMPLE = ROOT / "samples/DrillPress.SampleRules/DrillPress.SampleRules.csproj"
WARNING = re.compile(r"\bwarning ([A-Z]+\d+): (.*)")

# Roslyn 5.9.0 (35d9211b841e7613c1d2f8f5af6d628ace696c4c) diagnostics.
# Match both code and full originating member, never a warning category alone.
# GetAssemblyLocation explicitly handles the empty AOT location as "<unknown>".
# Pooled delegates invoke the supplied factory, not a reflected TValue constructor.
# EnsureInitialized's import-tracking dictionary is exercised by the alias golden.
# No extra roots are needed by this engine; recheck this assumption as queries grow.
KNOWN_WARNINGS = {
    ("IL3000", "Microsoft.CodeAnalysis.CommonCompiler.GetAssemblyLocation(Type)"),
    ("IL2091", "Microsoft.CodeAnalysis.PooledObjects.PooledDelegates.GetPooledCreateValueCallback<TKey,TArg,TValue>(Func`3<TKey,TArg,TValue>,TArg,ConditionalWeakTable`2.CreateValueCallback<!!0,!!2>&)"),
    ("IL2091", "Microsoft.CodeAnalysis.PooledObjects.PooledDelegates.CreateValueCallbackWithBoundArgument`3"),
    ("IL2091", "Microsoft.CodeAnalysis.PooledObjects.PooledDelegates.CreateValueCallbackWithBoundArgument`3.CreateValueCallbackWithBoundArgument`3()"),
    ("IL2091", "Microsoft.CodeAnalysis.PooledObjects.PooledDelegates.CreateValueCallbackWithBoundArgument`3.Bind()"),
    ("IL2091", "Roslyn.Utilities.RoslynLazyInitializer.EnsureInitialized<T>(!!0&)"),
}


def validate_publish_warnings(output):
    for match in WARNING.finditer(output):
        origin = match[2].split(": ", 1)[0]
        if (match[1], origin) not in KNOWN_WARNINGS:
            raise RuntimeError(f"Unexplained publication warning: {match[0]}")


def run(command, expected=0, log=None):
    result = subprocess.run(
        [str(part) for part in command], cwd=ROOT, capture_output=True, timeout=600
    )
    if log is not None:
        log.write_bytes(result.stdout + result.stderr)
    if result.returncode != expected:
        raise RuntimeError(
            f"Command {command!r} exited {result.returncode}; expected {expected}.\n"
            + result.stdout.decode("utf-8", errors="replace")
            + result.stderr.decode("utf-8", errors="replace")
        )
    return result


def runtime_identifier():
    if (platform.machine().lower() not in ("x86_64", "amd64")
            or platform.system() not in ("Linux", "Windows")):
        raise RuntimeError("Supported runtime identifiers are linux-x64 and win-x64.")
    return {"Linux": "linux-x64", "Windows": "win-x64"}[platform.system()]


def build_and_publish(output, rid):
    run(["dotnet", "build", "DrillPress.slnx", "-c", "Release"], log=output / "build.log")
    publish = run([
        "dotnet", "publish", SAMPLE, "-c", "Release", "-r", rid,
        "-o", output / "native",
    ], log=output / "publish.log")
    log = publish.stdout + publish.stderr
    validate_publish_warnings(log.decode("utf-8", errors="replace"))
    return (
        ROOT / "samples/DrillPress.SampleRules/bin/Release/net10.0/DrillPress.SampleRules.dll",
        output / "native" / ("DrillPress.SampleRules.exe" if rid == "win-x64" else "DrillPress.SampleRules"),
    )


def create_cases(directory):
    project = directory / "Probe.csproj"
    source = directory / "Probe.cs"
    project.write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
        '<TargetFramework>net10.0</TargetFramework><LangVersion>14.0</LangVersion>'
        '</PropertyGroup></Project>', encoding="utf-8",
    )
    build_host = ROOT / "src/DrillPress.BuildHost/bin/Release/net10.0/DrillPress.BuildHost.dll"
    cases = []
    # An alias exercises Roslyn's import tracking, including lazy dictionary creation.
    for name, expression, exit_code in (("clean", '\"\"', 0), ("violating", "Text.Empty", 1)):
        source.write_text(
            'using Text = System.String;\n'
            'public static class Probe\n{\n'
            f'    public static string Value => {expression};\n}}\n', encoding="utf-8",
        )
        snapshot = directory / f"{name}.json"
        run(["dotnet", build_host, "export", project, snapshot])
        expected_output = b""
        if exit_code == 1:
            display_path = os.path.relpath(source, ROOT).replace("\\", "/")
            newline = "\r\n" if sys.platform == "win32" else "\n"
            expected_output = newline.join([
                'DP1004 Use the empty string literal "" instead of string.Empty.',
                display_path, "  4:35", "",
            ]).encode("utf-8")
        cases.append((name, snapshot, exit_code, expected_output, b""))

    invalid = directory / "invalid.json"
    invalid.write_text(json.dumps({
        "fileIdentifier": "drillpress-compilation", "formatVersion": -1, "projects": [],
    }), encoding="utf-8")
    newline = b"\r\n" if sys.platform == "win32" else b"\n"
    cases.append(("invalid", invalid, 2, b"",
                  b"drillpress-rules: Compilation snapshot format -1 is not supported; expected 1." + newline))
    return cases


def verify_cases(managed, native, cases, output):
    for name, snapshot, exit_code, stdout, stderr in cases:
        managed_result = run(["dotnet", managed, "check", snapshot], exit_code)
        native_result = run([native, "check", snapshot], exit_code)
        for mode, result in (("managed", managed_result), ("native", native_result)):
            (output / f"{name}.{mode}.stdout").write_bytes(result.stdout)
            (output / f"{name}.{mode}.stderr").write_bytes(result.stderr)
            if result.stdout != stdout or result.stderr != stderr:
                raise RuntimeError(f"{name}/{mode}: output differs from the exact byte golden.")
        print(f"{name}: managed/native bytes and exit code {exit_code} match")

    # The managed coordinator must also launch a native executable directly.
    _, snapshot, exit_code, stdout, stderr = cases[1]
    cli = ROOT / "src/DrillPress.Cli/bin/Release/net10.0/DrillPress.Cli.dll"
    build_host = ROOT / "src/DrillPress.BuildHost/bin/Release/net10.0/DrillPress.BuildHost.dll"
    result = run([
        "dotnet", cli, "check", "--build-host", build_host, "--rules", native,
        snapshot.parent / "Probe.csproj",
    ], exit_code)
    if result.stdout != stdout or result.stderr != stderr:
        raise RuntimeError("CLI/native: output differs from the exact byte golden.")
    print("CLI/native: complete BuildHost-to-native path matches")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, help="Fresh report directory (defaults to an isolated artifacts directory).")
    args = parser.parse_args()
    rid = runtime_identifier()
    artifacts = ROOT / "artifacts"
    artifacts.mkdir(exist_ok=True)
    output = args.output.resolve() if args.output else Path(tempfile.mkdtemp(prefix=f"native-{rid}-", dir=artifacts))
    if args.output:
        output.mkdir(parents=True, exist_ok=False)
    managed, native = build_and_publish(output, rid)
    with tempfile.TemporaryDirectory(prefix="drillpress-native-") as temporary:
        cases = create_cases(Path(temporary))
        verify_cases(managed, native, cases, output)
    print(f"Reports: {output}")


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, KeyError, subprocess.TimeoutExpired) as error:
        print(f"native-bundles: {error}", file=sys.stderr)
        sys.exit(1)
