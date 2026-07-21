#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${TRACEBAG_ENV_FILE:-${1:-${repository_root}/.env}}"

if [[ ! -f "${env_file}" ]]; then
  echo "Missing environment file: ${env_file}" >&2
  exit 1
fi

docker compose \
  --env-file "${env_file}" \
  --file "${repository_root}/deploy/compose.yaml" \
  --file "${repository_root}/deploy/compose.demo.yaml" \
  --profile traffic \
  down

echo "Tracebag and the demo stopped. Persistent Tracebag volumes were retained."
