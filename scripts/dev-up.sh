#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${1:-${repository_root}/.env}"

if [[ ! -f "${env_file}" ]]; then
  echo "Missing environment file: ${env_file}" >&2
  echo "Copy .env.example to .env and fill the required values first." >&2
  exit 1
fi

required_value() {
  local key="$1"
  local value
  value="$(sed -n "s/^${key}=//p" "${env_file}" | tail -n 1 | tr -d '\r')"
  if [[ -z "${value}" ]]; then
    echo "${key} must be set in ${env_file}." >&2
    exit 1
  fi
}

required_value TRACEBAG_POSTGRES_PASSWORD
required_value TRACEBAG_ADMIN_PASSWORD_HASH

"${repository_root}/scripts/build-images.sh" "${env_file}"

docker compose \
  --env-file "${env_file}" \
  --file "${repository_root}/deploy/compose.yaml" \
  up --detach --wait tracebag-postgres tracebag

echo "Tracebag is ready at the URL configured by TRACEBAG_PUBLIC_URL (http://localhost:9090 by default)."
