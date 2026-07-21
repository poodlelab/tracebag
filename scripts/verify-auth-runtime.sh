#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repository_root}"

for command in curl docker jq; do
  command -v "${command}" >/dev/null 2>&1 || {
    echo "${command} is required for authentication acceptance testing." >&2
    exit 1
  }
done

if ! docker info >/dev/null 2>&1; then
  echo "Docker Engine is required for authentication acceptance testing." >&2
  exit 1
fi

suffix="$(date +%s)"
project="tracebag-auth-acceptance-${suffix}"
temp_dir="$(mktemp -d)"
env_file="${temp_dir}/acceptance.env"
cookie_jar="${temp_dir}/cookies.txt"
headers_file="${temp_dir}/headers.txt"
body_file="${temp_dir}/body.json"
admin_user="admin"
admin_password="auth-acceptance-${suffix}"
export COMPOSE_PROJECT_NAME="${project}"
export TRACEBAG_IMAGE="tracebag-auth-acceptance:${suffix}"
export TRACEBAG_POSTGRES_VOLUME="${project}-postgres"
export TRACEBAG_DATA_VOLUME="${project}-data"
export TRACEBAG_ARTIFACT_VOLUME="${project}-artifacts"
export TRACEBAG_PORT=0

compose() {
  docker compose \
    --project-directory "${repository_root}" \
    --env-file "${env_file}" \
    --file deploy/compose.release.yaml \
    --file tests/acceptance/compose.auth.yaml \
    "$@"
}

cleanup() {
  local exit_status=$?
  if [[ "${exit_status}" -ne 0 ]]; then
    compose logs --tail 100 tracebag tracebag-postgres tracebag-auth-proxy >&2 || true
  fi
  compose down --volumes --remove-orphans >/dev/null 2>&1 || true
  rm -rf "${temp_dir}"
}
trap cleanup EXIT

docker build --quiet --tag "${TRACEBAG_IMAGE}" --file Dockerfile .
admin_password_hash="$(
  printf '%s\n' "${admin_password}" \
    | docker run --rm --interactive --entrypoint dotnet "${TRACEBAG_IMAGE}" \
        Tracebag.Api.dll hash-password "${admin_user}"
)"

cp deploy/.env.release.example "${env_file}"
sed -i.bak \
  -e "s|^TRACEBAG_POSTGRES_PASSWORD=.*|TRACEBAG_POSTGRES_PASSWORD=auth-acceptance-${suffix}|" \
  -e "s|^TRACEBAG_ADMIN_PASSWORD_HASH=.*|TRACEBAG_ADMIN_PASSWORD_HASH=${admin_password_hash}|" \
  -e "s|^TRACEBAG_ADMIN_USER=.*|TRACEBAG_ADMIN_USER=${admin_user}|" \
  -e 's|^TRACEBAG_STAGE=.*|TRACEBAG_STAGE=production|' \
  -e 's|^TRACEBAG_AUTH_ENABLED=.*|TRACEBAG_AUTH_ENABLED=true|' \
  -e 's|^TRACEBAG_AUTH_LOGIN_PERMIT_LIMIT=.*|TRACEBAG_AUTH_LOGIN_PERMIT_LIMIT=6|' \
  -e 's|^TRACEBAG_AUTH_LOGIN_WINDOW_SECONDS=.*|TRACEBAG_AUTH_LOGIN_WINDOW_SECONDS=600|' \
  "${env_file}"
rm -f "${env_file}.bak"

compose up --detach --wait tracebag-postgres tracebag tracebag-auth-proxy
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
base_url="https://tracebag.test:${proxy_port}"
curl_options=(--insecure --silent --show-error --resolve "tracebag.test:${proxy_port}:127.0.0.1")

status="$(curl "${curl_options[@]}" --output "${body_file}" --write-out '%{http_code}' "${base_url}/api/system/status")"
[[ "${status}" == "401" ]]
status="$(curl "${curl_options[@]}" \
  --output "${body_file}" \
  --write-out '%{http_code}' \
  --request POST \
  "${base_url}/api/containers/not-a-container/restart")"
[[ "${status}" == "401" ]]

login_payload="$(jq -cn --arg userName "${admin_user}" --arg password "${admin_password}" '{userName: $userName, password: $password}')"
status="$(curl "${curl_options[@]}" \
  --cookie-jar "${cookie_jar}" \
  --dump-header "${headers_file}" \
  --output "${body_file}" \
  --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data "${login_payload}" \
  "${base_url}/api/auth/login")"
[[ "${status}" == "200" ]]
csrf_token="$(jq -er '.csrfToken | select(length > 0)' "${body_file}")"
grep -Eiq '^set-cookie: __Host-Tracebag=.*; path=/; secure; samesite=strict; httponly' "${headers_file}"

status="$(curl "${curl_options[@]}" \
  --cookie "${cookie_jar}" \
  --output "${body_file}" \
  --write-out '%{http_code}' \
  "${base_url}/api/auth/me")"
[[ "${status}" == "200" ]]
jq -e '.authenticated == true and .user == "admin"' "${body_file}" >/dev/null

status="$(curl "${curl_options[@]}" \
  --cookie "${cookie_jar}" \
  --output "${body_file}" \
  --write-out '%{http_code}' \
  --request POST \
  "${base_url}/api/containers/not-a-container/restart")"
[[ "${status}" == "400" ]]
jq -e '.error == "csrf_token_invalid"' "${body_file}" >/dev/null

status="$(curl "${curl_options[@]}" \
  --cookie "${cookie_jar}" \
  --output "${body_file}" \
  --write-out '%{http_code}' \
  --request POST \
  "${base_url}/api/auth/logout")"
[[ "${status}" == "400" ]]
jq -e '.error == "csrf_token_invalid"' "${body_file}" >/dev/null

status="$(curl "${curl_options[@]}" \
  --cookie "${cookie_jar}" \
  --output "${body_file}" \
  --write-out '%{http_code}' \
  --request POST \
  --header "X-CSRF-TOKEN: ${csrf_token}" \
  "${base_url}/api/auth/logout")"
[[ "${status}" == "200" ]]
jq -e '.authenticated == false' "${body_file}" >/dev/null

invalid_payload="$(jq -cn --arg userName "${admin_user}" '{userName: $userName, password: "wrong"}')"
for _ in 1 2 3 4 5; do
  status="$(curl "${curl_options[@]}" \
    --output "${body_file}" \
    --write-out '%{http_code}' \
    --header 'Content-Type: application/json' \
    --data "${invalid_payload}" \
    "${base_url}/api/auth/login")"
  [[ "${status}" == "401" ]]
  jq -e '.error == "invalid_login"' "${body_file}" >/dev/null
done

status="$(curl "${curl_options[@]}" \
  --dump-header "${headers_file}" \
  --output "${body_file}" \
  --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data "${invalid_payload}" \
  "${base_url}/api/auth/login")"
[[ "${status}" == "429" ]]
jq -e '.error == "login_rate_limited"' "${body_file}" >/dev/null
grep -Eiq '^retry-after: [1-9][0-9]*' "${headers_file}"

curl "${curl_options[@]}" --dump-header "${headers_file}" --output /dev/null "${base_url}/health/live"
grep -Fiq "content-security-policy: default-src 'self'" "${headers_file}"
grep -Fiq 'strict-transport-security: max-age=31536000; includeSubDomains' "${headers_file}"
grep -Fiq 'x-frame-options: DENY' "${headers_file}"
grep -Fiq 'x-content-type-options: nosniff' "${headers_file}"

# Restarting exercises immediate relational audit retention and resets the
# in-memory limiter so the Kestrel body-limit check remains independent from the
# deliberately exhausted proxy partition.
compose exec -T tracebag-postgres psql -U tracebag -d tracebag -v ON_ERROR_STOP=1 \
  -c "INSERT INTO audit_events (id, timestamp, \"user\", action, result) VALUES ('00000000-0000-0000-0000-000000000001', '2000-01-01T00:00:00Z', 'acceptance', 'acceptance.expired', 'success');" \
  >/dev/null
compose up --detach --wait --no-deps --force-recreate tracebag
expired_audit_count=1
for _ in {1..40}; do
  expired_audit_count="$(compose exec -T tracebag-postgres psql -U tracebag -d tracebag -tAc \
    "SELECT count(*) FROM audit_events WHERE action = 'acceptance.expired';")"
  [[ "${expired_audit_count}" == "0" ]] && break
  sleep 0.25
done
[[ "${expired_audit_count}" == "0" ]]

tracebag_binding="$(compose port tracebag 8080)"
direct_url="http://127.0.0.1:${tracebag_binding##*:}"
oversized_password="$(head -c 5000 /dev/zero | tr '\0' p)"
oversized_payload="$(jq -cn --arg userName "${admin_user}" --arg password "${oversized_password}" '{userName: $userName, password: $password}')"
unset oversized_password
status="$(curl --silent --show-error \
  --output "${body_file}" \
  --write-out '%{http_code}' \
  --header 'Content-Type: application/json' \
  --data "${oversized_payload}" \
  "${direct_url}/api/auth/login")"
[[ "${status}" == "413" ]]
jq -e '.error == "request_too_large"' "${body_file}" >/dev/null
unset oversized_payload

cleanup
trap - EXIT
echo "Authentication acceptance test passed: HTTPS proxying, cookies, CSRF, authorization, request bounds, audit retention, headers, and login limiting."
