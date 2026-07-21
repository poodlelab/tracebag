#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repository_root}"

for command in curl docker jq shasum; do
  command -v "${command}" >/dev/null 2>&1 || {
    echo "${command} is required for backup/restore acceptance testing." >&2
    exit 1
  }
done
if ! docker info >/dev/null 2>&1; then
  echo "Docker Engine is required for backup/restore acceptance testing." >&2
  exit 1
fi

suffix="$(date +%s)"
project="tracebag-backup-restore-${suffix}"
temp_dir="$(mktemp -d)"
env_file="${temp_dir}/acceptance.env"
cookie_jar="${temp_dir}/cookies.txt"
response_file="${temp_dir}/response.json"
download_file="${temp_dir}/artifact.txt"
admin_user="admin"
admin_password="backup-restore-${suffix}"
export COMPOSE_PROJECT_NAME="${project}"
export TRACEBAG_IMAGE="tracebag-backup-restore:${suffix}"
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
    compose logs --tail 120 tracebag tracebag-postgres tracebag-auth-proxy >&2 || true
  fi
  compose down --volumes --remove-orphans >/dev/null 2>&1 || true
  rm -rf "${temp_dir}"
}
trap cleanup EXIT

configure_trusted_proxy() {
  local proxy_id proxy_address
  proxy_id="$(compose ps --quiet tracebag-auth-proxy)"
  proxy_address="$(docker inspect --format '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "${proxy_id}")"
  [[ "${proxy_address}" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]
  sed -i.bak "s|^TRACEBAG_TRUSTED_PROXIES=.*|TRACEBAG_TRUSTED_PROXIES=${proxy_address}|" "${env_file}"
  rm -f "${env_file}.bak"
  compose up --detach --wait --no-deps --force-recreate tracebag
}

resolve_proxy() {
  local binding
  binding="$(compose port tracebag-auth-proxy 443)"
  proxy_port="${binding##*:}"
  base_url="https://tracebag.test:${proxy_port}"
  curl_options=(--insecure --silent --show-error --resolve "tracebag.test:${proxy_port}:127.0.0.1")
}

data_key_digest() {
  docker run --rm --volume "${TRACEBAG_DATA_VOLUME}:/source:ro" alpine:3.22 \
    sh -c 'find /source/data-protection-keys -type f -maxdepth 1 -exec sha256sum {} \; | sort | sha256sum' \
    | awk '{print $1}'
}

docker build --quiet --tag "${TRACEBAG_IMAGE}" --file Dockerfile .
admin_password_hash="$(
  printf '%s\n' "${admin_password}" \
    | docker run --rm --interactive --entrypoint dotnet "${TRACEBAG_IMAGE}" \
        Tracebag.Api.dll hash-password "${admin_user}"
)"
cp deploy/.env.release.example "${env_file}"
sed -i.bak \
  -e "s|^TRACEBAG_POSTGRES_PASSWORD=.*|TRACEBAG_POSTGRES_PASSWORD=backup-${suffix}|" \
  -e "s|^TRACEBAG_ADMIN_PASSWORD_HASH=.*|TRACEBAG_ADMIN_PASSWORD_HASH=${admin_password_hash}|" \
  -e "s|^TRACEBAG_ADMIN_USER=.*|TRACEBAG_ADMIN_USER=${admin_user}|" \
  -e 's|^TRACEBAG_STAGE=.*|TRACEBAG_STAGE=production|' \
  -e 's|^TRACEBAG_AUTH_ENABLED=.*|TRACEBAG_AUTH_ENABLED=true|' \
  -e 's|^TRACEBAG_AUTH_LOGIN_PERMIT_LIMIT=.*|TRACEBAG_AUTH_LOGIN_PERMIT_LIMIT=20|' \
  "${env_file}"
rm -f "${env_file}.bak"

compose up --detach --wait tracebag-postgres tracebag tracebag-auth-proxy
configure_trusted_proxy

compose stop tracebag
docker run --rm --volume "${TRACEBAG_ARTIFACT_VOLUME}:/target" alpine:3.22 \
  sh -c "mkdir -p /target/backup-fixture && printf '%s' 'retained-artifact-${suffix}' >/target/backup-fixture/payload.txt"
compose exec -T tracebag-postgres psql -U tracebag -d tracebag -v ON_ERROR_STOP=1 >/dev/null <<SQL
INSERT INTO artifacts
  (id, container_id, container_name, type, file_name, created_at, size, created_by, expires_at, state)
VALUES
  ('artifact-backup', 'target-backup', 'Backup target', 'stack-snapshot',
   'backup-fixture/payload.txt', now(), 28, 'admin', now() + interval '1 day', 'available');

INSERT INTO incidents
  (id, container_id, container_name, docker_id, process_id, title, profile, status, progress,
   created_by, created_at, window_start, window_end, completed_at, capture_options)
VALUES
  ('incident-backup', 'target-backup', 'Backup target', 'docker-backup', 42,
   'Retained backup incident', 'contention', 'closed', 100, 'admin', now(), now(), now(), now(), '{}'::jsonb);

INSERT INTO incident_evidence
  (id, incident_id, kind, title, captured_at, artifact_id, summary, payload,
   selected_by_default, sensitive, redaction_status)
VALUES
  ('evidence-backup', 'incident-backup', 'diagnostic-artifact', 'Retained artifact', now(),
   'artifact-backup', '{}'::jsonb, '{}'::jsonb, true, false, 'not-required');
SQL
compose up --detach --wait tracebag
resolve_proxy

login_payload="$(jq -cn --arg userName "${admin_user}" --arg password "${admin_password}" '{userName:$userName,password:$password}')"
curl "${curl_options[@]}" --fail --cookie-jar "${cookie_jar}" \
  --header 'Content-Type: application/json' --data "${login_payload}" \
  "${base_url}/api/auth/login" >"${response_file}"
csrf_token="$(jq -er '.csrfToken | select(length > 0)' "${response_file}")"
curl "${curl_options[@]}" --fail --cookie "${cookie_jar}" \
  "${base_url}/api/incidents/incident-backup" | jq -e '.incident.id == "incident-backup" and .evidence[0].artifactId == "artifact-backup"' >/dev/null
curl "${curl_options[@]}" --fail --cookie "${cookie_jar}" \
  "${base_url}/api/artifacts/artifact-backup/download" >"${download_file}"
grep -qx "retained-artifact-${suffix}" "${download_file}"
keys_before="$(data_key_digest)"
[[ -n "${keys_before}" ]]

compose stop tracebag
compose exec -T tracebag-postgres pg_dump -U tracebag -d tracebag --format=custom --no-owner >"${temp_dir}/postgres.dump"
docker run --rm --volume "${TRACEBAG_DATA_VOLUME}:/source:ro" --volume "${temp_dir}:/backup" \
  alpine:3.22 tar -C /source -czf /backup/data.tar.gz .
docker run --rm --volume "${TRACEBAG_ARTIFACT_VOLUME}:/source:ro" --volume "${temp_dir}:/backup" \
  alpine:3.22 tar -C /source -czf /backup/artifacts.tar.gz .
(cd "${temp_dir}" && shasum -a 256 postgres.dump data.tar.gz artifacts.tar.gz >SHA256SUMS)
(cd "${temp_dir}" && shasum -a 256 -c SHA256SUMS >/dev/null)

compose down --volumes --remove-orphans
docker volume create "${TRACEBAG_DATA_VOLUME}" >/dev/null
docker volume create "${TRACEBAG_ARTIFACT_VOLUME}" >/dev/null
docker run --rm --volume "${TRACEBAG_DATA_VOLUME}:/target" --volume "${temp_dir}:/backup:ro" \
  alpine:3.22 tar -C /target -xzf /backup/data.tar.gz
docker run --rm --volume "${TRACEBAG_ARTIFACT_VOLUME}:/target" --volume "${temp_dir}:/backup:ro" \
  alpine:3.22 tar -C /target -xzf /backup/artifacts.tar.gz

compose up --detach --wait tracebag-postgres
compose exec -T tracebag-postgres pg_restore -U tracebag -d tracebag --clean --if-exists --no-owner <"${temp_dir}/postgres.dump"
compose up --detach --wait tracebag tracebag-auth-proxy
configure_trusted_proxy
resolve_proxy

[[ "$(data_key_digest)" == "${keys_before}" ]]
curl "${curl_options[@]}" --fail --cookie "${cookie_jar}" \
  "${base_url}/api/auth/me" | jq -e '.authenticated == true and .user == "admin"' >/dev/null
curl "${curl_options[@]}" --fail --cookie "${cookie_jar}" \
  "${base_url}/api/incidents/incident-backup" | jq -e '.incident.id == "incident-backup" and .evidence[0].artifactId == "artifact-backup"' >/dev/null
curl "${curl_options[@]}" --fail --cookie "${cookie_jar}" \
  "${base_url}/api/artifacts/artifact-backup/download" >"${download_file}"
grep -qx "retained-artifact-${suffix}" "${download_file}"
curl "${curl_options[@]}" --fail --cookie "${cookie_jar}" --cookie-jar "${cookie_jar}" \
  --request POST --header "X-CSRF-TOKEN: ${csrf_token}" "${base_url}/api/auth/logout" \
  | jq -e '.authenticated == false' >/dev/null
curl "${curl_options[@]}" --fail --cookie "${cookie_jar}" \
  "${base_url}/api/auth/me" | jq -e '.authenticated == false' >/dev/null

cleanup
trap - EXIT
echo "Backup/restore acceptance passed for incidents, artifacts, data-protection keys, and session continuity."
