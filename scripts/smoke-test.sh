#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${1-${repository_root}/.env}"
compose=(docker compose --file "${repository_root}/deploy/compose.yaml")

if [[ -n "${env_file}" ]]; then
  if [[ ! -f "${env_file}" ]]; then
    echo "Missing environment file: ${env_file}" >&2
    exit 1
  fi
  compose+=(--env-file "${env_file}")
fi

"${compose[@]}" up --detach --wait tracebag-postgres tracebag

published_port="$("${compose[@]}" port tracebag 8080)"
if [[ "${published_port}" != 127.0.0.1:* && "${published_port}" != \[::1\]:* ]]; then
  echo "Unsafe published address: ${published_port}" >&2
  exit 1
fi

published_port_number="${published_port##*:}"
base_url="${TRACEBAG_SMOKE_URL:-http://127.0.0.1:${published_port_number}}"

live_response="$(curl --fail --silent --show-error "${base_url}/health/live")"
ready_response="$(curl --fail --silent --show-error "${base_url}/health/ready")"

for expected in '"status":"healthy"'; do
  grep -q "${expected}" <<<"${live_response}"
  grep -q "${expected}" <<<"${ready_response}"
done

for expected_check in database docker artifact-storage; do
  grep -q "\"${expected_check}\"" <<<"${ready_response}"
done

unauthenticated_status="$(curl --silent --output /dev/null --write-out '%{http_code}' "${base_url}/api/containers")"
if [[ "${unauthenticated_status}" != "401" ]]; then
  echo "Expected unauthenticated API request to return 401, got ${unauthenticated_status}." >&2
  exit 1
fi

migration_count="$(
  "${compose[@]}" exec -T tracebag-postgres \
    psql -U tracebag -d tracebag -tAc 'SELECT COUNT(*) FROM "__EFMigrationsHistory";' \
    | tr -d '[:space:]'
)"
if [[ ! "${migration_count}" =~ ^[1-9][0-9]*$ ]]; then
  echo "No applied EF Core migration was found." >&2
  exit 1
fi

echo "Tracebag installation smoke test passed (${migration_count} migration(s) applied)."
