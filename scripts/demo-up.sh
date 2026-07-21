#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${TRACEBAG_ENV_FILE:-${repository_root}/.env}"
traffic_enabled=false

if [[ "${1:-}" == "--traffic" ]]; then
  traffic_enabled=true
elif [[ -n "${1:-}" ]]; then
  env_file="$1"
fi

if [[ "${2:-}" == "--traffic" ]]; then
  traffic_enabled=true
fi

if [[ ! -f "${env_file}" ]]; then
  echo "Missing environment file: ${env_file}" >&2
  echo "Run ./scripts/init-env.sh first." >&2
  exit 1
fi

compose=(
  docker compose
  --env-file "${env_file}"
  --file "${repository_root}/deploy/compose.yaml"
  --file "${repository_root}/deploy/compose.demo.yaml"
)

"${compose[@]}" --profile build build \
  tracebag tracebag-runner-dotnet-8 tracebag-demo-api
"${compose[@]}" up --detach --wait \
  tracebag-postgres tracebag tracebag-demo-api

if [[ "${traffic_enabled}" == "true" ]]; then
  "${compose[@]}" --profile traffic up --detach tracebag-demo-traffic
fi

tracebag_binding="$("${compose[@]}" port tracebag 8080)"
demo_binding="$("${compose[@]}" port tracebag-demo-api 8080)"
echo "Tracebag is ready at http://${tracebag_binding}."
echo "The bounded demo API is ready at http://${demo_binding}."
if [[ "${traffic_enabled}" == "true" ]]; then
  echo "Normal demo traffic is running in the background."
fi
