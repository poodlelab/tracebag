#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${1:-${repository_root}/.env}"

if [[ ! -f "${env_file}" ]]; then
  echo "Missing environment file: ${env_file}" >&2
  exit 1
fi

docker compose \
  --env-file "${env_file}" \
  --file "${repository_root}/deploy/compose.yaml" \
  down

echo "Tracebag stopped. Persistent volumes were retained."
