#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repository_root}"

for command in docker trivy node; do
  command -v "${command}" >/dev/null 2>&1 || {
    echo "${command} is required for image verification." >&2
    exit 1
  }
done
docker info >/dev/null 2>&1 || {
  echo "Docker Engine is required for image verification." >&2
  exit 1
}

evidence_dir="${TRACEBAG_SUPPLY_CHAIN_EVIDENCE_DIR:-${repository_root}/.tracebag/supply-chain-evidence}"
platform="${TRACEBAG_SCAN_PLATFORM:-linux/amd64}"
mkdir -p "${evidence_dir}"
suffix="$(date +%s)"

images=(tracebag tracebag-demo-api tracebag-runner-dotnet-8 tracebag-runner-dotnet-9 tracebag-runner-dotnet-10)
dockerfiles=(Dockerfile demo/Dockerfile runners/dotnet-8/Dockerfile runners/dotnet-9/Dockerfile runners/dotnet-10/Dockerfile)

for index in "${!images[@]}"; do
  image="${images[${index}]}"
  dockerfile="${dockerfiles[${index}]}"
  local_ref="tracebag-supply-chain/${image}:${suffix}"
  echo "Building ${image} from ${dockerfile} for ${platform}."
  docker build --pull --platform "${platform}" --file "${dockerfile}" --tag "${local_ref}" .
  trivy image --quiet --scanners vuln --severity HIGH,CRITICAL --format json \
    --output "${evidence_dir}/${image}-vulnerabilities.json" "${local_ref}"
  node scripts/evaluate-trivy-report.mjs "${evidence_dir}/${image}-vulnerabilities.json" "${image}"
  trivy image --quiet --format spdx-json \
    --output "${evidence_dir}/${image}.spdx.json" "${local_ref}"
done

echo "Five-image verification passed for ${platform}. Evidence: ${evidence_dir}"
