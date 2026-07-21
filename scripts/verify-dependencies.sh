#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repository_root}"

scratch_dir="$(mktemp -d)"
trap 'rm -rf "${scratch_dir}"' EXIT
nuget_report="${scratch_dir}/nuget-vulnerabilities.json"

dotnet list Tracebag.slnx package \
  --vulnerable \
  --include-transitive \
  --format json \
  --output-version 1 >"${nuget_report}"

vulnerability_count="$(jq '[recurse | objects | .vulnerabilities? // empty | .[]] | length' "${nuget_report}")"
if [[ "${vulnerability_count}" -ne 0 ]]; then
  jq '[recurse | objects | select(has("vulnerabilities")) | {id, resolvedVersion, vulnerabilities}]' "${nuget_report}" >&2
  echo "NuGet vulnerability audit found ${vulnerability_count} advisory entries." >&2
  exit 1
fi

npm audit --omit=dev --audit-level=high --prefix src/Tracebag.Web
npm audit --omit=dev --audit-level=high --prefix website
npm audit --audit-level=high --prefix tests/browser

echo "Dependency vulnerability audits passed."
