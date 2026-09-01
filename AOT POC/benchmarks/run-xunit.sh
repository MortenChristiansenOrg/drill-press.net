#!/usr/bin/env bash

set -u
set -o pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
xunit_root="$repository_root/Benchmark Repositories/xunit"
artifacts="$repository_root/AOT POC/artifacts/benchmarks"
xunit_commit="6bbefaed1d0a995bc9970800384f9e8a1b9d2331"

mkdir -p "$repository_root/Benchmark Repositories" "$artifacts"
if [[ ! -d "$xunit_root/.git" ]]; then
  git clone --recurse-submodules https://github.com/xunit/xunit.git "$xunit_root"
fi

git -C "$xunit_root" fetch origin "$xunit_commit"
git -C "$xunit_root" checkout --detach "$xunit_commit"
git -C "$xunit_root" submodule update --init --recursive

dotnet build "$repository_root/AOT POC/DrillPress.Aot.slnx" -c Release --nologo
dotnet publish "$repository_root/AOT POC/src/DrillPress.SampleRules/DrillPress.SampleRules.csproj" \
  -c Release -r linux-x64 -o "$repository_root/AOT POC/artifacts/rules-linux-x64"

# xUnit currently reports two locked-package hash failures for its AOT runner
# projects. MSBuildWorkspace still creates all 94 compilations, so preserve the
# non-zero status but continue far enough to verify compiler fidelity and time
# the immutable manifest.
set +e
dotnet restore "$xunit_root/xunit.slnx" --locked-mode
restore_status=$?
/usr/bin/time -f 'export_wall=%e export_user=%U export_sys=%S export_max_rss_kb=%M' \
  dotnet "$repository_root/AOT POC/src/DrillPress.BuildHost/bin/Release/net10.0/DrillPress.BuildHost.dll" \
  export "$xunit_root/xunit.slnx" "$artifacts/xunit.drillpress.json"
export_status=$?
set -e

echo "restore_exit=$restore_status"
echo "build_host_exit=$export_status"
if command -v jq >/dev/null; then
  jq '{projects: (.projects|length), documents: ([.projects[].documents|length]|add), generatedDocuments: ([.projects[].documents[]|select(.isGenerated)]|length), compilerErrors: ([.projects[].compilerErrorCount]|add), workspaceMessages: (.messages|length)}' \
    "$artifacts/xunit.drillpress.json"
fi

set +e
/usr/bin/time -f 'analysis_wall=%e analysis_user=%U analysis_sys=%S analysis_max_rss_kb=%M' \
  "$repository_root/AOT POC/artifacts/rules-linux-x64/DrillPress.SampleRules" \
  check "$artifacts/xunit.drillpress.json" \
  | if command -v jq >/dev/null; then
      jq -s '{findings:length, fixable:([.[]|select(has("fixes"))]|length), byRule:(group_by(.rule)|map({rule:.[0].rule,count:length}))}'
    else
      wc -l
    fi
analysis_status=${PIPESTATUS[0]}
set -e

echo "rule_bundle_exit=$analysis_status"
[[ $analysis_status -eq 0 || $analysis_status -eq 1 ]]
