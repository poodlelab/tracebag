#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repository_root}"

version="${1:?Usage: verify-published-runtime.sh <X.Y.Z> [registry]}"
registry="${2:-ghcr.io/poodlelab}"
[[ "${version}" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || {
  echo "Published smoke requires an exact X.Y.Z version: ${version}" >&2
  exit 1
}

docker info >/dev/null 2>&1 || {
  echo "Docker Engine is required for published-image smoke testing." >&2
  exit 1
}

suffix="${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}-$$"
project="tracebag-published-${suffix}"
scratch="$(mktemp -d)"
export COMPOSE_PROJECT_NAME="${project}"
export TRACEBAG_VERSION="${version}"
export TRACEBAG_REGISTRY="${registry}"
export TRACEBAG_IMAGE="${registry}/tracebag:${version}"
export TRACEBAG_DEMO_IMAGE="${registry}/tracebag-demo-api:${version}"
export TRACEBAG_RUNNER_IMAGE_DOTNET_8="${registry}/tracebag-runner-dotnet-8:${version}"
export TRACEBAG_RUNNER_IMAGE_DOTNET_9="${registry}/tracebag-runner-dotnet-9:${version}"
export TRACEBAG_RUNNER_IMAGE_DOTNET_10="${registry}/tracebag-runner-dotnet-10:${version}"
export TRACEBAG_POSTGRES_PASSWORD="published-${suffix}"
export TRACEBAG_ADMIN_PASSWORD_HASH=published-smoke-auth-disabled
export TRACEBAG_AUTH_ENABLED=false
export TRACEBAG_STAGE=local
export TRACEBAG_PORT=0
export TRACEBAG_DEMO_PORT=0
export TRACEBAG_POSTGRES_VOLUME="${project}-postgres"
export TRACEBAG_DATA_VOLUME="${project}-data"
export TRACEBAG_ARTIFACT_VOLUME="${project}-artifacts"
export TRACEBAG_DEMO_DOTNET_TMP_VOLUME="${project}-dotnet-tmp"
export TRACEBAG_LOG_COLLECTOR_SCAN_SECONDS=1
export TRACEBAG_DIAGNOSTIC_JOB_MAX_DURATION_SECONDS=60

compose=(
  docker compose
  --env-file /dev/null
  --file deploy/compose.release.yaml
  --file deploy/compose.demo.release.yaml
)
target_docker_id=""

cleanup() {
  local exit_status="${1:-0}"
  local resource_id
  if [[ "${exit_status}" -ne 0 ]]; then
    "${compose[@]}" logs --tail 100 tracebag tracebag-postgres tracebag-demo-api >&2 || true
  fi
  if [[ -n "${target_docker_id}" ]]; then
    while read -r resource_id; do
      [[ -n "${resource_id}" ]] || continue
      docker rm --force "${resource_id}" >/dev/null 2>&1 || true
    done < <(docker ps --all --quiet \
      --filter 'label=tracebag.runner=true' \
      --filter "label=tracebag.targetContainer=${target_docker_id}")
  fi
  "${compose[@]}" down --volumes --remove-orphans >/dev/null 2>&1 || true
  while read -r resource_id; do
    [[ -n "${resource_id}" ]] || continue
    docker rm --force "${resource_id}" >/dev/null 2>&1 || true
  done < <(docker ps --all --quiet --filter "label=com.docker.compose.project=${project}")
  for resource_id in \
    "${TRACEBAG_POSTGRES_VOLUME}" \
    "${TRACEBAG_DATA_VOLUME}" \
    "${TRACEBAG_ARTIFACT_VOLUME}" \
    "${TRACEBAG_DEMO_DOTNET_TMP_VOLUME}"; do
    docker volume rm "${resource_id}" >/dev/null 2>&1 || true
  done
  docker network rm "${project}_default" >/dev/null 2>&1 || true
  rm -rf "${scratch}"
}
trap 'cleanup $?' EXIT

# This is intentionally pull-only. A build in this test would defeat the
# guarantee that the published GHCR artifacts are what operators can install.
# The .NET 8 runner is intentionally absent: the first process request must
# exercise Tracebag's on-demand runner pull.
"${compose[@]}" pull tracebag tracebag-demo-api tracebag-postgres

for image in \
  "${TRACEBAG_IMAGE}" \
  "${TRACEBAG_DEMO_IMAGE}"; do
  docker image inspect "${image}" >/dev/null
done

"${compose[@]}" up --detach --wait tracebag-postgres tracebag tracebag-demo-api
for service in tracebag tracebag-postgres tracebag-demo-api; do
  container_id="$("${compose[@]}" ps --quiet "${service}")"
  [[ "$(docker inspect --format '{{.HostConfig.RestartPolicy.Name}}' "${container_id}")" == "no" ]] || {
    echo "Published session service ${service} restarts automatically." >&2
    exit 1
  }
done
tracebag_binding="$("${compose[@]}" port tracebag 8080)"
demo_binding="$("${compose[@]}" port tracebag-demo-api 8080)"
tracebag_url="http://127.0.0.1:${tracebag_binding##*:}"
demo_url="http://127.0.0.1:${demo_binding##*:}"

curl --fail --silent --show-error "${tracebag_url}/health/ready" | grep -q '"status":"healthy"'
curl --fail --silent --show-error "${demo_url}/health" >/dev/null

containers='[]'
target_id=""
for _ in $(seq 1 60); do
  containers="$(curl --fail --silent --show-error "${tracebag_url}/api/containers")"
  target_id="$(jq -r --arg project "${project}" '.[] | select(.projectName == $project and .serviceName == "tracebag-demo-api") | .id' <<<"${containers}" | head -n 1)"
  if [[ -n "${target_id}" ]]; then
    break
  fi
  sleep 1
done
[[ -n "${target_id}" ]] || {
  echo "Published install did not discover the labeled demo API: ${containers}" >&2
  exit 1
}
target_docker_id="$(jq -r --arg id "${target_id}" '.[] | select(.id == $id) | .dockerId' <<<"${containers}" | head -n 1)"
[[ -n "${target_docker_id}" ]]

processes="$(curl --fail --silent --show-error "${tracebag_url}/api/containers/${target_id}/dotnet/processes")"
docker image inspect "${TRACEBAG_RUNNER_IMAGE_DOTNET_8}" >/dev/null
process_id="$(jq -r '.[0].pid // empty' <<<"${processes}")"
[[ -n "${process_id}" ]] || {
  echo "Published runner could not discover the demo .NET process: ${processes}" >&2
  exit 1
}

created="$(curl --fail --silent --show-error --request POST --header 'Content-Type: application/json' \
  --data "{\"processId\":${process_id},\"profile\":\"stack-snapshot\"}" \
  "${tracebag_url}/api/containers/${target_id}/diagnostic-jobs")"
job_id="$(jq -r '.id // empty' <<<"${created}")"
[[ -n "${job_id}" ]]

job=''
for _ in $(seq 1 90); do
  job="$(curl --fail --silent --show-error "${tracebag_url}/api/diagnostic-jobs/${job_id}")"
  status="$(jq -r '.status' <<<"${job}")"
  if [[ "${status}" == completed ]]; then
    break
  fi
  if [[ "${status}" =~ ^(failed|cancelled|timed_out|target_exited)$ ]]; then
    echo "Published diagnostic reached ${status}: ${job}" >&2
    exit 1
  fi
  sleep 1
done

artifact_id="$(jq -r 'select(.status == "completed") | .artifactId // empty' <<<"${job}")"
[[ -n "${artifact_id}" ]] || {
  echo "Published diagnostic did not complete with an artifact: ${job}" >&2
  exit 1
}
curl --fail --silent --show-error --output "${scratch}/artifact" \
  "${tracebag_url}/api/artifacts/${artifact_id}/download"
test -s "${scratch}/artifact"

for _ in $(seq 1 30); do
  if ! docker ps --all --quiet \
    --filter 'label=tracebag.runner=true' \
    --filter "label=tracebag.targetContainer=${target_docker_id}" | grep -q .; then
    break
  fi
  sleep 1
done
if docker ps --all --quiet \
  --filter 'label=tracebag.runner=true' \
  --filter "label=tracebag.targetContainer=${target_docker_id}" | grep -q .; then
  echo "Published diagnostic runner remained after the capture completed." >&2
  exit 1
fi

"${compose[@]}" down
if docker ps --all --quiet --filter "label=com.docker.compose.project=${project}" | grep -q .; then
  echo "Published session left Compose containers after shutdown." >&2
  exit 1
fi
for volume in \
  "${TRACEBAG_POSTGRES_VOLUME}" \
  "${TRACEBAG_DATA_VOLUME}" \
  "${TRACEBAG_ARTIFACT_VOLUME}"; do
  docker volume inspect "${volume}" >/dev/null || {
    echo "Published session shutdown removed evidence volume ${volume}." >&2
    exit 1
  }
done

cleanup 0
trap - EXIT
echo "Published-image smoke passed for ${registry} at ${version}, including the on-demand .NET 8 runner pull."
