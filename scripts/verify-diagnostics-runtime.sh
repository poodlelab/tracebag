#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repository_root}"

if ! docker info >/dev/null 2>&1; then
  echo "Docker Engine is required for diagnostics acceptance testing." >&2
  exit 1
fi

suffix="$(date +%s)"
project="tracebag-diagnostics-${suffix}"
target_id="compose:${project}:tracebag-demo-api:1"
export COMPOSE_PROJECT_NAME="${project}"
export TRACEBAG_POSTGRES_PASSWORD="diagnostics-${suffix}-database-test-only"
export TRACEBAG_ADMIN_PASSWORD_HASH=diagnostics-auth-disabled-placeholder
export TRACEBAG_AUTH_ENABLED=false TRACEBAG_STAGE=local TRACEBAG_PORT=0 TRACEBAG_DEMO_PORT=0
export TRACEBAG_ARTIFACT_VOLUME="${project}-artifacts" TRACEBAG_DATA_VOLUME="${project}-data" TRACEBAG_POSTGRES_VOLUME="${project}-postgres"
export TRACEBAG_DEMO_DOTNET_TMP_VOLUME="${project}-dotnet-tmp" TRACEBAG_LOG_COLLECTOR_SCAN_SECONDS=1
export TRACEBAG_FULL_DUMP_ENABLED=true TRACEBAG_DEMO_FULL_DUMP_ENABLED=true
export TRACEBAG_DIAGNOSTIC_JOB_MAX_DURATION_SECONDS=30 TRACEBAG_COUNTER_MAX_SECONDS=30
export TRACEBAG_RUNNER_MEMORY_LIMIT_BYTES=1073741824 TRACEBAG_RUNNER_CPU_LIMIT_MILLICORES=1000 TRACEBAG_RUNNER_PIDS_LIMIT=128
compose=(docker compose --env-file /dev/null --file deploy/compose.yaml --file deploy/compose.demo.yaml)
scratch_dir="$(mktemp -d)"
bundle="${scratch_dir}/incident.tracebag.zip"

cleanup() {
  local exit_status=$?
  local runner_id
  if [[ "${exit_status}" -ne 0 ]]; then
    echo "Diagnostics acceptance failure details:" >&2
    if [[ -n "${incident_detail:-}" ]]; then
      jq -c '.analysis' <<<"${incident_detail}" >&2 || true
    fi
    "${compose[@]}" logs --tail 100 tracebag tracebag-demo-api >&2 || true
  fi
  while read -r runner_id; do
    [[ -n "${runner_id}" ]] || continue
    docker rm --force "${runner_id}" >/dev/null 2>&1 || true
  done < <(docker ps --all --quiet \
    --filter "label=com.docker.compose.project=${project}" \
    --filter "label=tracebag.runner=true")
  rm -rf "${scratch_dir}"
  "${compose[@]}" down --volumes --remove-orphans >/dev/null 2>&1 || true
}
trap cleanup EXIT

"${compose[@]}" --profile build build tracebag tracebag-runner-dotnet-8 tracebag-demo-api
"${compose[@]}" up --detach --wait tracebag-postgres tracebag tracebag-demo-api
tracebag_binding="$("${compose[@]}" port tracebag 8080)"
demo_binding="$("${compose[@]}" port tracebag-demo-api 8080)"
tracebag_url="http://127.0.0.1:${tracebag_binding##*:}"
demo_url="http://127.0.0.1:${demo_binding##*:}"

runner_for_label() {
  local label="$1"
  local runner_id=""
  for _ in $(seq 1 40); do
    runner_id="$(docker ps --quiet --filter "label=${label}" | head -n 1)"
    if [[ -n "${runner_id}" ]]; then
      printf '%s\n' "${runner_id}"
      return 0
    fi
    sleep 0.25
  done
  return 1
}

assert_runner_policy() {
  local runner_id="$1"
  local operation="$2"
  local artifact_mounts="$3"
  docker inspect "${runner_id}" | jq -e \
    --arg operation "${operation}" \
    --argjson artifact_mounts "${artifact_mounts}" \
    --argjson memory "${TRACEBAG_RUNNER_MEMORY_LIMIT_BYTES}" \
    --argjson nano_cpus "$((TRACEBAG_RUNNER_CPU_LIMIT_MILLICORES * 1000000))" \
    --argjson pids "${TRACEBAG_RUNNER_PIDS_LIMIT}" '
      .[0] as $runner
      | $runner.Config.Labels["tracebag.runner"] == "true"
        and $runner.Config.Labels["tracebag.internal"] == "true"
        and $runner.Config.Labels["tracebag.runnerOperation"] == $operation
        and $runner.HostConfig.NetworkMode == "none"
        and $runner.HostConfig.ReadonlyRootfs == true
        and $runner.HostConfig.Init == true
        and ($runner.HostConfig.CapDrop | index("ALL")) != null
        and ($runner.HostConfig.SecurityOpt | index("no-new-privileges:true")) != null
        and $runner.HostConfig.Memory == $memory
        and $runner.HostConfig.MemorySwap == $memory
        and $runner.HostConfig.NanoCpus == $nano_cpus
        and $runner.HostConfig.PidsLimit == $pids
        and ($runner.Mounts | length) == (1 + $artifact_mounts)
        and ([$runner.Mounts[] | select(.Destination == "/tmp" and .Type == "volume" and .RW == true)] | length) == 1
        and ([$runner.Mounts[] | select(.Destination == "/artifacts" and .Type == "volume" and .RW == true)] | length) == $artifact_mounts
        and ([$runner.Mounts[] | select(.Destination == "/var/run/docker.sock")] | length) == 0
    ' >/dev/null
}

wait_for_job_status() {
  local job_id="$1"
  local expected="$2"
  local detail=""
  for _ in $(seq 1 180); do
    detail="$(curl --fail --silent --show-error "${tracebag_url}/api/diagnostic-jobs/${job_id}")"
    if jq -e --arg expected "${expected}" '.status == $expected' <<<"${detail}" >/dev/null; then
      printf '%s\n' "${detail}"
      return 0
    fi
    if jq -e '.status | IN("failed", "cancelled", "timed_out", "target_exited")' <<<"${detail}" >/dev/null; then
      echo "Job ${job_id} reached unexpected terminal state: ${detail}" >&2
      return 1
    fi
    sleep 1
  done
  echo "Job ${job_id} did not reach ${expected}." >&2
  return 1
}

create_job() {
  local profile="$1"
  local duration="${2:-null}"
  local confirmation="${3:-null}"
  local body
  body="$(jq -nc --argjson process_id "${process_id}" --arg profile "${profile}" --argjson duration "${duration}" --argjson confirmation "${confirmation}" \
    '{processId:$process_id,profile:$profile,durationSeconds:$duration,confirmation:$confirmation}')"
  curl --fail --silent --show-error --request POST --header 'Content-Type: application/json' \
    --data "${body}" "${tracebag_url}/api/containers/${target_id}/diagnostic-jobs" | jq -r '.id'
}

assert_runner_removed() {
  local label="$1"
  local attempts="${2:-40}"
  for _ in $(seq 1 "${attempts}"); do
    if [[ -z "$(docker ps --all --quiet --filter "label=${label}" | head -n 1)" ]]; then
      return 0
    fi
    sleep 0.25
  done
  echo "Runner with label ${label} was not cleaned up." >&2
  return 1
}

processes="$(curl --fail --silent --show-error "${tracebag_url}/api/containers/${target_id}/dotnet/processes")"
process_id="$(sed -n 's/.*"pid":\([0-9][0-9]*\).*/\1/p' <<<"${processes}" | head -n 1)"
[[ -n "${process_id}" ]]
echo "Verified process discovery."

# Live counters: inspect the applied Docker policy, stream real samples, and
# stop the session explicitly.
counter_session="$(curl --fail --silent --show-error --request POST --header 'Content-Type: application/json' \
  --data "{\"processId\":${process_id},\"preset\":\"runtime\"}" \
  "${tracebag_url}/api/containers/${target_id}/dotnet/counters")"
counter_session_id="$(jq -r '.sessionId' <<<"${counter_session}")"
counter_runner="$(runner_for_label "tracebag.sessionId=${counter_session_id}")"
assert_runner_policy "${counter_runner}" live-counters 0
counter_stream="${scratch_dir}/counter-stream.txt"
if curl --fail --silent --show-error --max-time 18 "${tracebag_url}/api/diagnostics/sessions/${counter_session_id}/stream" >"${counter_stream}"; then
  :
else
  curl_status=$?
  [[ "${curl_status}" -eq 28 ]]
fi
grep -q 'event: counter' "${counter_stream}"
curl --fail --silent --show-error --request DELETE "${tracebag_url}/api/diagnostics/sessions/${counter_session_id}" >/dev/null
assert_runner_removed "tracebag.sessionId=${counter_session_id}"
sleep 5
echo "Verified live counters and cleanup."

# Persistent recording uses the same baseline without artifact access and
# flushes real samples before manual stop.
recording="$(curl --fail --silent --show-error --request POST --header 'Content-Type: application/json' \
  --data "{\"processId\":${process_id},\"preset\":\"runtime\",\"intervalSeconds\":2,\"maxDurationMinutes\":1,\"name\":\"Runner policy acceptance\"}" \
  "${tracebag_url}/api/containers/${target_id}/dotnet/recordings")"
recording_id="$(jq -r '.id' <<<"${recording}")"
recording_runner="$(runner_for_label "tracebag.recordingId=${recording_id}")"
assert_runner_policy "${recording_runner}" counter-recording 0
echo "Verified active recording runner policy."
sleep 18
curl --fail --silent --show-error --request POST "${tracebag_url}/api/dotnet/recordings/${recording_id}/stop" >/dev/null
assert_runner_removed "tracebag.recordingId=${recording_id}"
for _ in $(seq 1 20); do
  recording_detail="$(curl --fail --silent --show-error "${tracebag_url}/api/dotnet/recordings/${recording_id}")"
  if jq -e '.recording.sampleCount > 0 and .recording.status == "stopped"' <<<"${recording_detail}" >/dev/null; then break; fi
  sleep 1
done
if ! jq -e '.recording.sampleCount > 0 and .recording.status == "stopped"' <<<"${recording_detail}" >/dev/null; then
  echo "Recording did not flush a non-empty stopped series: ${recording_detail}" >&2
  exit 1
fi
sleep 2
echo "Verified persistent recording and cleanup."

# Exercise every durable operation. The trace runner remains alive long enough
# to inspect the engine-applied policy and artifact-only mount difference.
stack_job="$(create_job stack-snapshot)"
wait_for_job_status "${stack_job}" completed >/dev/null
assert_runner_removed "tracebag.diagnosticJobId=${stack_job}"
echo "Verified stack snapshot."

cpu_job="$(create_job cpu-trace 5)"
cpu_runner="$(runner_for_label "tracebag.diagnosticJobId=${cpu_job}")"
assert_runner_policy "${cpu_runner}" cpu-trace 1
wait_for_job_status "${cpu_job}" completed >/dev/null
assert_runner_removed "tracebag.diagnosticJobId=${cpu_job}"
echo "Verified CPU trace and applied runner policy."

threading_job="$(create_job threading-trace 5)"
wait_for_job_status "${threading_job}" completed >/dev/null
assert_runner_removed "tracebag.diagnosticJobId=${threading_job}"
echo "Verified threading trace."

contention_job="$(create_job contention-trace 5)"
wait_for_job_status "${contention_job}" completed >/dev/null
assert_runner_removed "tracebag.diagnosticJobId=${contention_job}"
echo "Verified contention trace."

curl --fail --silent --show-error --request POST "${demo_url}/demo/allocations?seconds=12&mbPerSecond=20" >/dev/null
gc_job="$(create_job gc-dump)"
wait_for_job_status "${gc_job}" completed >/dev/null
assert_runner_removed "tracebag.diagnosticJobId=${gc_job}"
echo "Verified GC dump."

full_confirmation="$(jq -Rn --arg value 'I understand this full dump may contain secrets and personal data' '$value')"
full_job="$(create_job full-dump null "${full_confirmation}")"
wait_for_job_status "${full_job}" completed >/dev/null
assert_runner_removed "tracebag.diagnosticJobId=${full_job}"
echo "Verified three-gate full dump."

# Cancellation removes the running trace immediately and persists its terminal
# state. A duration equal to the configured ceiling exercises server timeout.
cancel_job="$(create_job cpu-trace 30)"
cancel_runner="$(runner_for_label "tracebag.diagnosticJobId=${cancel_job}")"
assert_runner_policy "${cancel_runner}" cpu-trace 1
curl --fail --silent --show-error --request POST "${tracebag_url}/api/diagnostic-jobs/${cancel_job}/cancel" >/dev/null
wait_for_job_status "${cancel_job}" cancelled >/dev/null
assert_runner_removed "tracebag.diagnosticJobId=${cancel_job}"
echo "Verified cancellation cleanup."

timeout_job="$(create_job cpu-trace 30)"
timeout_runner="$(runner_for_label "tracebag.diagnosticJobId=${timeout_job}")"
assert_runner_policy "${timeout_runner}" cpu-trace 1
wait_for_job_status "${timeout_job}" timed_out >/dev/null
assert_runner_removed "tracebag.diagnosticJobId=${timeout_job}"
echo "Verified timeout cleanup."

# Stopping the target during capture is distinguished from tool failure, and
# both the runner and its staging output are cleaned before the target returns.
target_exit_job="$(create_job cpu-trace 30)"
target_exit_runner="$(runner_for_label "tracebag.diagnosticJobId=${target_exit_job}")"
assert_runner_policy "${target_exit_runner}" cpu-trace 1
"${compose[@]}" stop tracebag-demo-api >/dev/null
wait_for_job_status "${target_exit_job}" target_exited >/dev/null
assert_runner_removed "tracebag.diagnosticJobId=${target_exit_job}"
echo "Verified target-exit cleanup."
"${compose[@]}" up --detach --wait tracebag-demo-api >/dev/null
demo_binding="$("${compose[@]}" port tracebag-demo-api 8080)"
demo_url="http://127.0.0.1:${demo_binding##*:}"
for _ in $(seq 1 30); do
  if processes="$(curl --fail --silent --show-error "${tracebag_url}/api/containers/${target_id}/dotnet/processes" 2>/dev/null)"; then
    process_id="$(sed -n 's/.*"pid":\([0-9][0-9]*\).*/\1/p' <<<"${processes}" | head -n 1)"
    if [[ -n "${process_id}" ]]; then break; fi
  fi
  sleep 1
done
[[ -n "${process_id}" ]]

curl --fail --silent --show-error --request POST "${demo_url}/demo/cpu?seconds=30&workers=4" >/dev/null
created="$(curl --fail --silent --show-error --request POST --header 'Content-Type: application/json' \
  --data "{\"processId\":${process_id},\"profile\":\"high-cpu\",\"title\":\"CPU acceptance analysis\",\"reason\":\"Acceptance scenario\",\"captureSeconds\":10,\"includeTrace\":true}" \
  "${tracebag_url}/api/containers/${target_id}/incidents")"
incident_id="$(sed -n 's/.*"id":"\([^"]*\)".*/\1/p' <<<"${created}")"
[[ -n "${incident_id}" ]]

incident_detail=""
for _ in $(seq 1 180); do
  incident_detail="$(curl --fail --silent --show-error "${tracebag_url}/api/incidents/${incident_id}")"
  if grep -Eq '"status":"(ready|partial)"' <<<"${incident_detail}"; then break; fi
  if grep -q '"status":"failed"' <<<"${incident_detail}"; then echo "Incident failed: ${incident_detail}" >&2; exit 1; fi
  sleep 1
done

grep -Eq '"status":"(ready|partial)"' <<<"${incident_detail}"
grep -q '"envelopeVersion":1' <<<"${incident_detail}"
grep -q '"analyzerVersion":"tracebag-local/1"' <<<"${incident_detail}"
grep -q '"localOnly":true' <<<"${incident_detail}"
grep -q '"externalProvidersUsed":false' <<<"${incident_detail}"
grep -q '"name":"stack","status":"completed"' <<<"${incident_detail}"
grep -q '"name":"trace","status":"completed"' <<<"${incident_detail}"
grep -q '"name":"signals","status":"completed"' <<<"${incident_detail}"
grep -q '"code":"grouped-stacks"' <<<"${incident_detail}"
grep -q '"code":"cpu-hot-paths"' <<<"${incident_detail}"
grep -Eq '"evidenceIds":\["evidence-[0-9a-f]+' <<<"${incident_detail}"

rerun="$(curl --fail --silent --show-error --request POST "${tracebag_url}/api/incidents/${incident_id}/analysis")"
grep -Eq '"status":"(completed|partial)"' <<<"${rerun}"
grep -q '"schemaVersion":1' <<<"${rerun}"

curl --fail --silent --show-error --output "${bundle}" "${tracebag_url}/api/incidents/${incident_id}/export"
unzip -t "${bundle}" >/dev/null
bundle_list="$(unzip -l "${bundle}")"
grep -q 'analysis-envelope.json' <<<"${bundle_list}"
grep -q 'findings.json' <<<"${bundle_list}"
if grep -q 'artifacts/' <<<"${bundle_list}"; then echo "Default export silently included an artifact." >&2; exit 1; fi

cleanup
trap - EXIT
echo "Diagnostics acceptance test passed: hardened runner policy, process discovery, live counters, recording, every durable profile, cancellation, timeout, target exit, cleanup, local analysis, and export."
