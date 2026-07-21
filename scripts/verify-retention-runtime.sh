#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repository_root}"

for command in curl docker jq; do
  command -v "${command}" >/dev/null 2>&1 || {
    echo "${command} is required for durable-retention acceptance testing." >&2
    exit 1
  }
done

if ! docker info >/dev/null 2>&1; then
  echo "Docker Engine is required for durable-retention acceptance testing." >&2
  exit 1
fi

suffix="$(date +%s)"
project="tracebag-retention-acceptance-${suffix}"
temp_dir="$(mktemp -d)"
env_file="${temp_dir}/acceptance.env"
body_file="${temp_dir}/body.json"
export COMPOSE_PROJECT_NAME="${project}"
export TRACEBAG_IMAGE="tracebag-retention-acceptance:${suffix}"
export TRACEBAG_POSTGRES_VOLUME="${project}-postgres"
export TRACEBAG_DATA_VOLUME="${project}-data"
export TRACEBAG_ARTIFACT_VOLUME="${project}-artifacts"
export TRACEBAG_PORT=0

compose() {
  docker compose \
    --project-directory "${repository_root}" \
    --env-file "${env_file}" \
    --file deploy/compose.release.yaml \
    "$@"
}

cleanup() {
  local exit_status=$?
  if [[ "${exit_status}" -ne 0 ]]; then
    compose logs --tail 120 tracebag tracebag-postgres >&2 || true
  fi
  compose down --volumes --remove-orphans >/dev/null 2>&1 || true
  rm -rf "${temp_dir}"
}
trap cleanup EXIT

cp deploy/.env.release.example "${env_file}"
sed -i.bak \
  -e "s|^TRACEBAG_POSTGRES_PASSWORD=.*|TRACEBAG_POSTGRES_PASSWORD=retention-acceptance-${suffix}|" \
  -e 's|^TRACEBAG_ADMIN_PASSWORD_HASH=.*|TRACEBAG_ADMIN_PASSWORD_HASH=acceptance-auth-disabled|' \
  -e 's|^TRACEBAG_AUTH_ENABLED=.*|TRACEBAG_AUTH_ENABLED=false|' \
  -e 's|^TRACEBAG_STAGE=.*|TRACEBAG_STAGE=local|' \
  -e 's|^TRACEBAG_DIAGNOSTIC_JOB_RETENTION_DAYS=.*|TRACEBAG_DIAGNOSTIC_JOB_RETENTION_DAYS=1|' \
  -e 's|^TRACEBAG_DIAGNOSTIC_JOB_RETENTION_DELETE_BATCH_SIZE=.*|TRACEBAG_DIAGNOSTIC_JOB_RETENTION_DELETE_BATCH_SIZE=10|' \
  -e 's|^TRACEBAG_DURABLE_RETENTION_SCAN_SECONDS=.*|TRACEBAG_DURABLE_RETENTION_SCAN_SECONDS=60|' \
  -e 's|^TRACEBAG_COUNTER_RECORDING_RETENTION_DAYS=.*|TRACEBAG_COUNTER_RECORDING_RETENTION_DAYS=1|' \
  -e 's|^TRACEBAG_INCIDENT_MAX_COUNT=.*|TRACEBAG_INCIDENT_MAX_COUNT=10|' \
  "${env_file}"
rm -f "${env_file}.bak"

docker build --quiet --tag "${TRACEBAG_IMAGE}" --file Dockerfile .
compose up --detach --wait tracebag-postgres tracebag

compose exec -T tracebag-postgres psql -U tracebag -d tracebag -v ON_ERROR_STOP=1 >/dev/null <<'SQL'
INSERT INTO diagnostic_jobs
  (id, container_id, container_name, docker_id, process_id, profile, status, progress,
   status_message, created_at, completed_at, deadline_at, created_by, request_fingerprint,
   inputs, runtime_major, runner_image, tool_version)
VALUES
  ('job-expired', 'target', 'Target', 'docker', 1, 'stack-snapshot', 'completed', 100,
   'completed', now() - interval '40 days', now() - interval '40 days', now() - interval '39 days',
   'admin', 'job-expired', '{}'::jsonb, 8, 'runner', 'test'),
  ('job-protected', 'target', 'Target', 'docker', 1, 'stack-snapshot', 'failed', 100,
   'failed', now() - interval '40 days', now() - interval '40 days', now() - interval '39 days',
   'admin', 'job-protected', '{}'::jsonb, 8, 'runner', 'test'),
  ('job-recent', 'target', 'Target', 'docker', 1, 'stack-snapshot', 'completed', 100,
   'completed', now() - interval '1 hour', now() - interval '30 minutes', now() + interval '1 hour',
   'admin', 'job-recent', '{}'::jsonb, 8, 'runner', 'test');

INSERT INTO diagnostic_job_events
  (job_id, timestamp, type, status, progress, message)
VALUES
  ('job-expired', now() - interval '40 days', 'completed', 'completed', 100, 'completed'),
  ('job-protected', now() - interval '40 days', 'completed', 'failed', 100, 'failed');

INSERT INTO counter_recording_sessions
  (id, container_id, container_name, process_id, preset, interval_seconds, max_duration_seconds,
   started_at, stopped_at, sample_count, status, created_by, runtime_major, runner_image, tool_version)
VALUES
  ('recording-protected', 'target', 'Target', 1, 'cpu', 5, 60,
   now() - interval '10 days', now() - interval '10 days', 0, 'completed', 'admin', 8, 'runner', 'test');

INSERT INTO artifacts
  (id, container_id, container_name, type, file_name, created_at, size, created_by, expires_at, state)
VALUES
  ('artifact-protected', 'target', 'Target', 'stack-snapshot', 'retention/payload.txt',
   now() - interval '10 days', 10, 'admin', now() - interval '9 days', 'available');

INSERT INTO incidents
  (id, container_id, container_name, docker_id, process_id, title, profile, status, progress,
   created_by, created_at, window_start, window_end, completed_at, capture_options)
VALUES
  ('inc-retention', 'target', 'Target', 'docker', 1, 'Retention fixture', 'high-cpu', 'closed', 100,
   'admin', now() - interval '40 days', now() - interval '40 days', now() - interval '40 days',
   now() - interval '40 days', '{}'::jsonb);

INSERT INTO incident_evidence
  (id, incident_id, kind, title, captured_at, source_id, artifact_id, summary, payload,
   selected_by_default, sensitive, redaction_status)
VALUES
  ('evidence-job', 'inc-retention', 'diagnostic-artifact', 'Job evidence', now() - interval '40 days',
   'job-protected', 'artifact-protected', '{}'::jsonb, '{}'::jsonb, true, false, 'not-redacted'),
  ('evidence-recording', 'inc-retention', 'counter-window', 'Recording evidence', now() - interval '40 days',
   'recording-protected', null, '{}'::jsonb, '{}'::jsonb, true, false, 'not-required');

INSERT INTO incident_timeline
  (incident_id, timestamp, type, severity, title, summary, evidence_id)
VALUES
  ('inc-retention', now() - interval '40 days', 'evidence', 'info', 'Evidence captured', 'Fixture', 'evidence-job');

INSERT INTO analysis_runs
  (id, incident_id, envelope_version, analyzer_version, status, created_by, created_at, completed_at)
VALUES
  ('analysis-retention', 'inc-retention', 1, 'tracebag-local/1', 'completed', 'admin',
   now() - interval '40 days', now() - interval '40 days');

INSERT INTO incident_findings
  (id, incident_id, analysis_run_id, code, severity, confidence, title, summary, created_at)
VALUES
  ('finding-retention', 'inc-retention', 'analysis-retention', 'fixture', 'info', 'high',
   'Fixture finding', 'Fixture', now() - interval '40 days');

INSERT INTO incident_finding_evidence (finding_id, evidence_id)
VALUES ('finding-retention', 'evidence-job');
SQL

compose up --detach --wait --no-deps --force-recreate tracebag

query() {
  compose exec -T tracebag-postgres psql -U tracebag -d tracebag -tAc "$1"
}

expired_job_count=1
for _ in {1..40}; do
  expired_job_count="$(query "SELECT count(*) FROM diagnostic_jobs WHERE id = 'job-expired';")"
  [[ "${expired_job_count}" == "0" ]] && break
  sleep 0.25
done
[[ "${expired_job_count}" == "0" ]]
[[ "$(query "SELECT count(*) FROM diagnostic_job_events WHERE job_id = 'job-expired';")" == "0" ]]
[[ "$(query "SELECT count(*) FROM diagnostic_jobs WHERE id IN ('job-protected', 'job-recent');")" == "2" ]]
[[ "$(query "SELECT count(*) FROM counter_recording_sessions WHERE id = 'recording-protected';")" == "1" ]]
[[ "$(query "SELECT count(*) FROM artifacts WHERE id = 'artifact-protected';")" == "1" ]]

compose exec -T tracebag-postgres psql -U tracebag -d tracebag -v ON_ERROR_STOP=1 >/dev/null <<'SQL'
INSERT INTO incidents
  (id, container_id, container_name, docker_id, process_id, title, profile, status, progress,
   created_by, created_at, window_start, capture_options)
VALUES
  ('inc-active', 'active-incident-target', 'Active incident target', 'docker-active-incident', 1,
   'Active fixture', 'high-cpu', 'collecting', 50, 'admin', now(), now(), '{}'::jsonb);
SQL

tracebag_binding="$(compose port tracebag 8080)"
base_url="http://127.0.0.1:${tracebag_binding##*:}"
status="$(curl --silent --show-error --output "${body_file}" --write-out '%{http_code}' \
  --request DELETE "${base_url}/api/incidents/inc-retention?confirm=wrong")"
[[ "${status}" == "400" ]]
jq -e '.error == "incident_delete_confirmation_required"' "${body_file}" >/dev/null

status="$(curl --silent --show-error --output "${body_file}" --write-out '%{http_code}' \
  --request DELETE "${base_url}/api/incidents/inc-active?confirm=inc-active")"
[[ "${status}" == "409" ]]
jq -e '.error == "incident_delete_active"' "${body_file}" >/dev/null

status="$(curl --silent --show-error --output "${body_file}" --write-out '%{http_code}' \
  --request DELETE "${base_url}/api/incidents/inc-retention?confirm=inc-retention")"
[[ "${status}" == "200" ]]
jq -e '.status == "deleted" and .deletedEvidence == 2 and .deletedFindings == 1 and .deletedAnalysisRuns == 1 and .releasedDiagnosticJobs == 1 and .releasedRecordings == 1 and .releasedArtifacts == 1' "${body_file}" >/dev/null

[[ "$(query "SELECT count(*) FROM incidents WHERE id = 'inc-retention';")" == "0" ]]
[[ "$(query "SELECT count(*) FROM incident_evidence WHERE incident_id = 'inc-retention';")" == "0" ]]
[[ "$(query "SELECT count(*) FROM incident_timeline WHERE incident_id = 'inc-retention';")" == "0" ]]
[[ "$(query "SELECT count(*) FROM incident_findings WHERE incident_id = 'inc-retention';")" == "0" ]]
[[ "$(query "SELECT count(*) FROM analysis_runs WHERE incident_id = 'inc-retention';")" == "0" ]]
[[ "$(query "SELECT count(*) FROM diagnostic_jobs WHERE id = 'job-protected';")" == "1" ]]
[[ "$(query "SELECT count(*) FROM counter_recording_sessions WHERE id = 'recording-protected';")" == "1" ]]
[[ "$(query "SELECT count(*) FROM artifacts WHERE id = 'artifact-protected';")" == "1" ]]

compose up --detach --wait --no-deps --force-recreate tracebag
released_count=3
for _ in {1..40}; do
  released_count="$(query "
    SELECT
      (SELECT count(*) FROM diagnostic_jobs WHERE id = 'job-protected') +
      (SELECT count(*) FROM counter_recording_sessions WHERE id = 'recording-protected') +
      (SELECT count(*) FROM artifacts WHERE id = 'artifact-protected');")"
  [[ "${released_count}" == "0" ]] && break
  sleep 0.25
done
[[ "${released_count}" == "0" ]]
[[ "$(query "SELECT count(*) FROM diagnostic_jobs WHERE id = 'job-recent';")" == "1" ]]

tracebag_binding="$(compose port tracebag 8080)"
base_url="http://127.0.0.1:${tracebag_binding##*:}"
curl --fail --silent --show-error "${base_url}/api/system/status" >"${body_file}"
jq -e '.dataRetention.status == "healthy" and .dataRetention.details.incidents == 1 and .dataRetention.details.incidentMaxCount == 10' "${body_file}" >/dev/null

cleanup
trap - EXIT
echo "Durable-retention acceptance test passed: indexed cleanup, reference protection, explicit incident deletion, cascades, release semantics, and status."
