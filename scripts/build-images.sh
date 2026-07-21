#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${1:-${repository_root}/.env}"

if [[ ! -f "${env_file}" ]]; then
  echo "Missing environment file: ${env_file}" >&2
  echo "Copy .env.example to .env and fill the required values first." >&2
  exit 1
fi

docker compose \
  --env-file "${env_file}" \
  --file "${repository_root}/deploy/compose.yaml" \
  --profile build \
  build tracebag tracebag-runner-dotnet-8 tracebag-runner-dotnet-9 tracebag-runner-dotnet-10
