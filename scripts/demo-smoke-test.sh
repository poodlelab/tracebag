#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${TRACEBAG_ENV_FILE:-${1:-${repository_root}/.env}}"

if [[ ! -f "${env_file}" ]]; then
  echo "Missing environment file: ${env_file}" >&2
  exit 1
fi

compose=(
  docker compose
  --env-file "${env_file}"
  --file "${repository_root}/deploy/compose.yaml"
  --file "${repository_root}/deploy/compose.demo.yaml"
)

demo_binding="$("${compose[@]}" port tracebag-demo-api 8080)"
if [[ "${demo_binding}" != 127.0.0.1:* && "${demo_binding}" != \[::1\]:* ]]; then
  echo "Unsafe demo published address: ${demo_binding}" >&2
  exit 1
fi
demo_url="${TRACEBAG_DEMO_SMOKE_URL:-http://127.0.0.1:${demo_binding##*:}}"

health="$(curl --fail --silent --show-error "${demo_url}/health")"
grep -q '"status":"healthy"' <<<"${health}"

healthy="$(curl --fail --silent --show-error "${demo_url}/demo/healthy")"
grep -q '"status":"ok"' <<<"${healthy}"

curl --fail --silent --show-error --request POST \
  "${demo_url}/demo/exceptions?count=2" >/dev/null
sleep 1

status="$(curl --fail --silent --show-error "${demo_url}/demo/status")"
grep -q '"name":"exceptions"' <<<"${status}"

demo_container_id="$("${compose[@]}" ps --quiet tracebag-demo-api)"
if [[ -z "${demo_container_id}" ]]; then
  echo "The demo container is not running." >&2
  exit 1
fi

enabled_label="$(docker inspect --format '{{ index .Config.Labels "tracebag.enabled" }}' "${demo_container_id}")"
tmp_volume_label="$(docker inspect --format '{{ index .Config.Labels "tracebag.dotnet.tmpVolume" }}' "${demo_container_id}")"
if [[ "${enabled_label}" != "true" || -z "${tmp_volume_label}" ]]; then
  echo "The demo does not have the required Tracebag discovery labels." >&2
  exit 1
fi

docker logs "${demo_container_id}" 2>&1 | grep -q 'Demo exception'
curl --fail --silent --show-error --request POST "${demo_url}/demo/reset" >/dev/null

echo "Tracebag demo smoke test passed."
