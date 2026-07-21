#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repository_root}"

if ! docker info >/dev/null 2>&1; then
  echo "Docker Engine is required for release acceptance testing." >&2
  exit 1
fi

suffix="$(date +%s)"
project="tracebag-release-acceptance-${suffix}"
env_file="$(mktemp)"
backup_dir="$(mktemp -d)"
export COMPOSE_PROJECT_NAME="${project}"
export TRACEBAG_IMAGE="tracebag-acceptance:${suffix}"
export TRACEBAG_DEMO_IMAGE="tracebag-demo-acceptance:${suffix}"
export TRACEBAG_RUNNER_IMAGE_DOTNET_8="tracebag-runner-8-acceptance:${suffix}"
export TRACEBAG_RUNNER_IMAGE_DOTNET_9="tracebag-runner-9-acceptance:${suffix}"
export TRACEBAG_RUNNER_IMAGE_DOTNET_10="tracebag-runner-10-acceptance:${suffix}"
export TRACEBAG_POSTGRES_VOLUME="${project}-postgres"
export TRACEBAG_DATA_VOLUME="${project}-data"
export TRACEBAG_ARTIFACT_VOLUME="${project}-artifacts"
export TRACEBAG_DEMO_DOTNET_TMP_VOLUME="${project}-demo-tmp"
export TRACEBAG_STAGE=local
export TRACEBAG_PORT=0 TRACEBAG_DEMO_PORT=0

compose() {
  docker compose \
    --env-file "${env_file}" \
    --file deploy/compose.release.yaml \
    --file deploy/compose.demo.release.yaml \
    "$@"
}

cleanup() {
  local exit_status=$?
  if [[ "${exit_status}" -ne 0 ]]; then
    compose logs --tail 80 tracebag tracebag-postgres tracebag-demo-api >&2 || true
  fi
  compose down --volumes --remove-orphans >/dev/null 2>&1 || true
  rm -f "${env_file}"
  rm -rf "${backup_dir}"
}
trap cleanup EXIT

cp deploy/.env.release.example "${env_file}"
sed -i.bak \
  -e "s|^TRACEBAG_POSTGRES_PASSWORD=.*|TRACEBAG_POSTGRES_PASSWORD=acceptance-${suffix}|" \
  -e 's|^TRACEBAG_ADMIN_PASSWORD_HASH=.*|TRACEBAG_ADMIN_PASSWORD_HASH=acceptance-auth-disabled|' \
  -e 's|^TRACEBAG_AUTH_ENABLED=.*|TRACEBAG_AUTH_ENABLED=false|' \
  "${env_file}"
rm -f "${env_file}.bak"

docker build --quiet --tag "${TRACEBAG_IMAGE}" --file Dockerfile .
docker build --quiet --tag "${TRACEBAG_DEMO_IMAGE}" --file demo/Dockerfile .
docker build --quiet --tag "${TRACEBAG_RUNNER_IMAGE_DOTNET_8}" --file runners/dotnet-8/Dockerfile .

compose up --detach --wait tracebag-postgres tracebag tracebag-demo-api
tracebag_binding="$(compose port tracebag 8080)"
base_url="http://127.0.0.1:${tracebag_binding##*:}"
curl --fail --silent --show-error "${base_url}/health/ready" | grep -q '"status":"healthy"'

compose stop tracebag
compose up --detach --wait tracebag
tracebag_binding="$(compose port tracebag 8080)"
base_url="http://127.0.0.1:${tracebag_binding##*:}"
curl --fail --silent --show-error "${base_url}/health/ready" | grep -q '"status":"healthy"'

compose down
compose up --detach --wait tracebag-postgres tracebag
tracebag_binding="$(compose port tracebag 8080)"
base_url="http://127.0.0.1:${tracebag_binding##*:}"
curl --fail --silent --show-error "${base_url}/health/ready" | grep -q '"status":"healthy"'

compose exec -T tracebag-postgres psql -U tracebag -d tracebag -v ON_ERROR_STOP=1 \
  -c "CREATE TABLE tracebag_restore_marker (value text NOT NULL); INSERT INTO tracebag_restore_marker VALUES ('${suffix}');" >/dev/null
docker run --rm --volume "${TRACEBAG_DATA_VOLUME}:/target" alpine:3.22 \
  sh -c "printf '%s' '${suffix}' >/target/tracebag-restore-marker"
docker run --rm --volume "${TRACEBAG_ARTIFACT_VOLUME}:/target" alpine:3.22 \
  sh -c "printf '%s' '${suffix}' >/target/tracebag-restore-marker"

compose stop tracebag
compose exec -T tracebag-postgres pg_dump -U tracebag -d tracebag --format=custom --no-owner >"${backup_dir}/postgres.dump"
docker run --rm --volume "${TRACEBAG_DATA_VOLUME}:/source:ro" --volume "${backup_dir}:/backup" \
  alpine:3.22 tar -C /source -czf /backup/data.tar.gz .
docker run --rm --volume "${TRACEBAG_ARTIFACT_VOLUME}:/source:ro" --volume "${backup_dir}:/backup" \
  alpine:3.22 tar -C /source -czf /backup/artifacts.tar.gz .
(cd "${backup_dir}" && shasum -a 256 postgres.dump data.tar.gz artifacts.tar.gz >SHA256SUMS)
(cd "${backup_dir}" && shasum -a 256 -c SHA256SUMS >/dev/null)

compose down --volumes --remove-orphans
docker volume create "${TRACEBAG_DATA_VOLUME}" >/dev/null
docker volume create "${TRACEBAG_ARTIFACT_VOLUME}" >/dev/null
docker run --rm --volume "${TRACEBAG_DATA_VOLUME}:/target" --volume "${backup_dir}:/backup:ro" \
  alpine:3.22 tar -C /target -xzf /backup/data.tar.gz
docker run --rm --volume "${TRACEBAG_ARTIFACT_VOLUME}:/target" --volume "${backup_dir}:/backup:ro" \
  alpine:3.22 tar -C /target -xzf /backup/artifacts.tar.gz
docker run --rm --volume "${TRACEBAG_DATA_VOLUME}:/target:ro" alpine:3.22 \
  grep -qx "${suffix}" /target/tracebag-restore-marker
docker run --rm --volume "${TRACEBAG_ARTIFACT_VOLUME}:/target:ro" alpine:3.22 \
  grep -qx "${suffix}" /target/tracebag-restore-marker

compose up --detach --wait tracebag-postgres
compose exec -T tracebag-postgres pg_restore -U tracebag -d tracebag --clean --if-exists --no-owner <"${backup_dir}/postgres.dump"
compose up --detach --wait tracebag
compose exec -T tracebag-postgres psql -U tracebag -d tracebag -tAc \
  "SELECT value FROM tracebag_restore_marker LIMIT 1;" | grep -qx "${suffix}"
tracebag_binding="$(compose port tracebag 8080)"
base_url="http://127.0.0.1:${tracebag_binding##*:}"
curl --fail --silent --show-error "${base_url}/health/ready" | grep -q '"status":"healthy"'

cleanup
trap - EXIT
echo "Release acceptance test passed: install, update, restart, checksummed backup, and clean-volume restore."
