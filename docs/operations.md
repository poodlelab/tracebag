# Session lifecycle, continuous operation, upgrade, and recovery

This guide applies to the published Docker Compose distribution. Commands are
run from the installation directory containing `compose.yaml` and `.env`.

The default stack is session-first: its services do not restart automatically.
Start it for an investigation and use `down` afterward. Named volumes preserve
state between sessions:

```bash
docker compose --env-file .env -f compose.yaml up -d --wait
docker compose --env-file .env -f compose.yaml down
```

For continuous log ingestion and Docker-event history, download
`compose.resident.yaml` from the same release and include it in every lifecycle
command:

```bash
compose=(docker compose --env-file .env \
  -f compose.yaml -f compose.resident.yaml)
"${compose[@]}" up -d --wait
```

Resident mode leaves Tracebag's Docker-administrator access active continuously.
Use it only when complete collection history is worth that longer exposure.

Unless a section says otherwise, the remaining examples use the session-first
Compose file:

```bash
compose=(docker compose --env-file .env -f compose.yaml)
```

## Before every upgrade

1. Read the target release notes and note database or configuration changes.
2. Confirm that `TRACEBAG_VERSION` contains an explicit semantic version, not
   `latest`.
3. Verify that the current stack is healthy.
4. Create and verify a backup as described below.
5. Preserve the current `.env`, Compose file, version, and backup checksums.

## Upgrade

Edit only `TRACEBAG_VERSION` in `.env`, then pull every image before changing
the running containers:

```bash
compose=(docker compose --env-file .env -f compose.yaml)
"${compose[@]}" --profile runners pull
"${compose[@]}" up -d --wait tracebag-postgres tracebag
curl --fail http://localhost:9090/health/ready
```

Tracebag applies forward database migrations during startup. Inspect startup
logs and the system status page before deleting the pre-upgrade backup.

## Rollback

Do not point an older Tracebag image at a database that a newer release has
migrated. Schema downgrades are not automatic and may corrupt or strand data.

The safe rollback procedure is:

1. Stop the upgraded stack.
2. Restore the pre-upgrade backup into new, empty Docker volumes.
3. Restore the previous `.env` and set its previous `TRACEBAG_VERSION`.
4. Start that version against the restored volumes.
5. Retain the upgraded volumes until the rollback is verified.

This volume-swap approach keeps both states recoverable and avoids destructive
in-place downgrades.

## Backup

A complete backup contains all three persistent stores:

- a PostgreSQL custom-format dump;
- `tracebag_data`, including data-protection keys;
- `tracebag_artifacts`, containing collected evidence.

Stop only the Tracebag application while the backup is captured. PostgreSQL
remains available long enough to create a consistent dump.

```bash
backup_dir="tracebag-backup-$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -m 700 "$backup_dir"

compose=(docker compose --env-file .env -f compose.yaml)
"${compose[@]}" stop tracebag
"${compose[@]}" exec -T tracebag-postgres \
  pg_dump -U tracebag -d tracebag --format=custom >"$backup_dir/postgres.dump"

docker run --rm \
  -v tracebag_data:/source:ro \
  -v "$PWD/$backup_dir":/backup \
  alpine:3.22 tar -C /source -czf /backup/data.tar.gz .

docker run --rm \
  -v tracebag_artifacts:/source:ro \
  -v "$PWD/$backup_dir":/backup \
  alpine:3.22 tar -C /source -czf /backup/artifacts.tar.gz .

cp .env compose.yaml VERSION "$backup_dir/" 2>/dev/null || true
(cd "$backup_dir" && sha256sum postgres.dump data.tar.gz artifacts.tar.gz >SHA256SUMS)
"${compose[@]}" start tracebag
curl --fail http://localhost:9090/health/ready
```

Treat the backup as sensitive. It contains authentication keys and potentially
raw application data. Encrypt it at rest and test restoration regularly.

## Restore without overwriting the current installation

First verify the backup and make a separate environment file:

```bash
backup_dir=/absolute/path/to/tracebag-backup-YYYYMMDDTHHMMSSZ
(cd "$backup_dir" && sha256sum --check SHA256SUMS)

cp "$backup_dir/.env" .env.restore
sed -i.bak 's/^TRACEBAG_DATA_VOLUME=.*/TRACEBAG_DATA_VOLUME=tracebag_restore_data/' .env.restore
sed -i.bak 's/^TRACEBAG_ARTIFACT_VOLUME=.*/TRACEBAG_ARTIFACT_VOLUME=tracebag_restore_artifacts/' .env.restore
sed -i.bak 's/^TRACEBAG_POSTGRES_VOLUME=.*/TRACEBAG_POSTGRES_VOLUME=tracebag_restore_postgres/' .env.restore
rm -f .env.restore.bak
```

Create and populate the new application volumes:

```bash
docker volume create tracebag_restore_data
docker volume create tracebag_restore_artifacts

docker run --rm \
  -v tracebag_restore_data:/target \
  -v "$backup_dir":/backup:ro \
  alpine:3.22 tar -C /target -xzf /backup/data.tar.gz

docker run --rm \
  -v tracebag_restore_artifacts:/target \
  -v "$backup_dir":/backup:ro \
  alpine:3.22 tar -C /target -xzf /backup/artifacts.tar.gz
```

Use a different Compose project name and localhost port for the rehearsal, then
restore PostgreSQL and launch Tracebag:

```bash
export COMPOSE_PROJECT_NAME=tracebag-restore-test
export TRACEBAG_PORT=19090
restore=(docker compose --env-file .env.restore -f compose.yaml)

"${restore[@]}" up -d --wait tracebag-postgres
"${restore[@]}" exec -T tracebag-postgres \
  pg_restore -U tracebag -d tracebag --clean --if-exists <"$backup_dir/postgres.dump"
"${restore[@]}" up -d --wait tracebag
curl --fail http://localhost:19090/health/ready
```

Verify login, incidents, artifacts, and data-protection continuity. Shut down the
rehearsal without `--volumes` until the restore has been accepted.

## Retention and storage controls

Tracebag applies independent bounds rather than treating disk space as an
unlimited queue:

| Data | Primary settings | Default |
| --- | --- | --- |
| Logs | `TRACEBAG_LOG_RETENTION_DAYS` | 7 days |
| Logs globally | `TRACEBAG_LOG_MAX_TOTAL_BYTES` | 1 GiB |
| Logs per container | `TRACEBAG_LOG_MAX_BYTES_PER_CONTAINER` | 256 MiB |
| Counter recordings | `TRACEBAG_COUNTER_RECORDING_RETENTION_DAYS` | 7 days |
| Terminal diagnostic jobs and events | `TRACEBAG_DIAGNOSTIC_JOB_RETENTION_DAYS` | 30 days |
| Incident workspaces | `TRACEBAG_INCIDENT_MAX_COUNT` | 200; explicit deletion only |
| Audit events by age | `TRACEBAG_AUDIT_RETENTION_DAYS` | 30 days |
| Audit events by count | `TRACEBAG_AUDIT_MAX_EVENTS` | 100,000 |
| Artifacts by age | `TRACEBAG_ARTIFACT_RETENTION_HOURS` | 24 hours |
| Artifacts by count | `TRACEBAG_ARTIFACT_MAX_COUNT` | 20 |
| Artifacts globally | `TRACEBAG_ARTIFACT_MAX_TOTAL_BYTES` | 2 GiB |

Retention is not a backup system and removal cannot be undone. Set shorter
periods when logs or diagnostics contain regulated or sensitive information.
Audit cleanup runs in bounded batches and uses both the age and count limit.
Diagnostic job cleanup is also indexed and batched. Incident references protect
their jobs, recordings, and artifacts; exporting and explicitly deleting an
incident releases those captures to their ordinary retention policies. Monitor
the durable-retention card on the system page and Docker volume growth
independently of Tracebag.

## Disaster-recovery rehearsal

At least once per release line, rehearse this sequence on a separate host or
isolated Docker project:

1. Install the currently released Compose bundle on an empty machine.
2. Capture a demo incident and download one artifact.
3. Back up all three stores and verify checksums.
4. Upgrade to the new release and validate health and evidence access.
5. Restore the pre-upgrade backup into new volumes.
6. Launch the previous version and verify login, incident, and artifact access.
7. Record versions, architectures, elapsed time, and deviations in the release
   notes.
