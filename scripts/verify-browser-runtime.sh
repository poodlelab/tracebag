#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repository_root}"

for command in curl docker npm; do
  command -v "${command}" >/dev/null 2>&1 || {
    echo "${command} is required for browser acceptance testing." >&2
    exit 1
  }
done
if ! docker info >/dev/null 2>&1; then
  echo "Docker Engine is required for browser acceptance testing." >&2
  exit 1
fi

suffix="$(date +%s)"
project="tracebag-browser-${suffix}"
temp_dir="$(mktemp -d)"
env_file="${temp_dir}/browser.env"
admin_user="admin"
admin_password="browser-acceptance-${suffix}"
website_port="$((20000 + suffix % 10000))"
website_pid=""
export COMPOSE_PROJECT_NAME="${project}"
export TRACEBAG_IMAGE="tracebag-browser:${suffix}"
export TRACEBAG_DEMO_IMAGE="tracebag-demo-browser:${suffix}"
export TRACEBAG_RUNNER_IMAGE_DOTNET_8="tracebag-runner-8-browser:${suffix}"
export TRACEBAG_POSTGRES_VOLUME="${project}-postgres"
export TRACEBAG_DATA_VOLUME="${project}-data"
export TRACEBAG_ARTIFACT_VOLUME="${project}-artifacts"
export TRACEBAG_DEMO_DOTNET_TMP_VOLUME="${project}-demo-tmp"
export TRACEBAG_PORT=0 TRACEBAG_DEMO_PORT=0

compose() {
  docker compose \
    --project-directory "${repository_root}" \
    --env-file "${env_file}" \
    --file deploy/compose.release.yaml \
    --file deploy/compose.demo.release.yaml \
    --file tests/acceptance/compose.auth.yaml \
    "$@"
}

cleanup() {
  local exit_status=$?
  if [[ "${exit_status}" -ne 0 ]]; then
    compose logs --tail 120 tracebag tracebag-postgres tracebag-demo-api tracebag-auth-proxy >&2 || true
  fi
  if [[ -n "${website_pid}" ]]; then
    kill "${website_pid}" >/dev/null 2>&1 || true
    wait "${website_pid}" >/dev/null 2>&1 || true
  fi
  compose down --volumes --remove-orphans >/dev/null 2>&1 || true
  rm -rf "${temp_dir}"
}
trap cleanup EXIT

docker build --quiet --tag "${TRACEBAG_IMAGE}" --file Dockerfile .
docker build --quiet --tag "${TRACEBAG_DEMO_IMAGE}" --file demo/Dockerfile .
docker build --quiet --tag "${TRACEBAG_RUNNER_IMAGE_DOTNET_8}" --file runners/dotnet-8/Dockerfile .
admin_password_hash="$(
  printf '%s\n' "${admin_password}" \
    | docker run --rm --interactive --entrypoint dotnet "${TRACEBAG_IMAGE}" \
        Tracebag.Api.dll hash-password "${admin_user}"
)"

cp deploy/.env.release.example "${env_file}"
sed -i.bak \
  -e "s|^TRACEBAG_POSTGRES_PASSWORD=.*|TRACEBAG_POSTGRES_PASSWORD=browser-${suffix}|" \
  -e "s|^TRACEBAG_ADMIN_PASSWORD_HASH=.*|TRACEBAG_ADMIN_PASSWORD_HASH=${admin_password_hash}|" \
  -e "s|^TRACEBAG_ADMIN_USER=.*|TRACEBAG_ADMIN_USER=${admin_user}|" \
  -e 's|^TRACEBAG_STAGE=.*|TRACEBAG_STAGE=production|' \
  -e 's|^TRACEBAG_AUTH_ENABLED=.*|TRACEBAG_AUTH_ENABLED=true|' \
  -e 's|^TRACEBAG_AUTH_LOGIN_PERMIT_LIMIT=.*|TRACEBAG_AUTH_LOGIN_PERMIT_LIMIT=20|' \
  -e 's|^TRACEBAG_AUTH_LOGIN_WINDOW_SECONDS=.*|TRACEBAG_AUTH_LOGIN_WINDOW_SECONDS=600|' \
  -e 's|^TRACEBAG_LOG_COLLECTOR_SCAN_SECONDS=.*|TRACEBAG_LOG_COLLECTOR_SCAN_SECONDS=1|' \
  -e 's|^TRACEBAG_COUNTER_MAX_SECONDS=.*|TRACEBAG_COUNTER_MAX_SECONDS=30|' \
  -e 's|^TRACEBAG_DIAGNOSTIC_JOB_MAX_DURATION_SECONDS=.*|TRACEBAG_DIAGNOSTIC_JOB_MAX_DURATION_SECONDS=30|' \
  "${env_file}"
rm -f "${env_file}.bak"

compose up --detach --wait tracebag-postgres tracebag tracebag-demo-api tracebag-auth-proxy
proxy_container_id="$(compose ps --quiet tracebag-auth-proxy)"
proxy_address="$(docker inspect --format '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "${proxy_container_id}")"
[[ "${proxy_address}" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]
sed -i.bak \
  "s|^TRACEBAG_TRUSTED_PROXIES=.*|TRACEBAG_TRUSTED_PROXIES=${proxy_address}|" \
  "${env_file}"
rm -f "${env_file}.bak"
compose up --detach --wait --no-deps --force-recreate tracebag

proxy_binding="$(compose port tracebag-auth-proxy 443)"
proxy_port="${proxy_binding##*:}"
export TRACEBAG_BROWSER_BASE_URL="https://tracebag.test:${proxy_port}"
export TRACEBAG_BROWSER_ADMIN_USER="${admin_user}"
export TRACEBAG_BROWSER_ADMIN_PASSWORD="${admin_password}"
export TRACEBAG_WEBSITE_URL="http://127.0.0.1:${website_port}"

npm --prefix website ci >/dev/null
SITE_URL="${TRACEBAG_WEBSITE_URL}" BASE_PATH=/ npm --prefix website run build >/dev/null
SITE_URL="${TRACEBAG_WEBSITE_URL}" BASE_PATH=/ \
  npm --prefix website run preview -- --host 127.0.0.1 --port "${website_port}" >"${temp_dir}/website.log" 2>&1 &
website_pid=$!
for _ in {1..40}; do
  if curl --fail --silent "${TRACEBAG_WEBSITE_URL}" >/dev/null 2>&1; then
    break
  fi
  sleep 0.25
done
curl --fail --silent "${TRACEBAG_WEBSITE_URL}" >/dev/null

npm --prefix tests/browser ci >/dev/null
sleep 5
npm --prefix tests/browser test

echo "Browser acceptance passed for the operator journey and responsive application/product pages."
