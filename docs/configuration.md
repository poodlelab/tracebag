# Tracebag Configuration

Tracebag reads configuration from environment variables. The active product
uses only the `TRACEBAG_*` namespace; there are no Helfli, Monitoring, or
OtterScope compatibility aliases.

## Core settings

| Variable | Default | Purpose |
| --- | --- | --- |
| `TRACEBAG_STAGE` | `local` | Identifies this Tracebag instance in runner names and labels. |
| `TRACEBAG_PUBLIC_URL` | `http://localhost:9090` | External base URL used by product links. Set an `https://` URL for remote sessions. |
| `TRACEBAG_DATABASE_URL` | empty | PostgreSQL connection string. Empty enables development file fallback where supported. |
| `TRACEBAG_DATA_DIR` | `/data` | Data-protection keys and development fallback data. |
| `TRACEBAG_ARTIFACT_DIR` | `/artifacts` | Artifact files visible to the API container. |
| `TRACEBAG_ARTIFACT_VOLUME` | `tracebag_artifacts` | Docker volume mounted into temporary runners. |

The standalone Compose file additionally recognizes deployment-only values:

| Variable | Default | Purpose |
| --- | --- | --- |
| `TRACEBAG_IMAGE` | `tracebag:dev` | App image built or run by Compose. |
| `TRACEBAG_BIND_ADDRESS` | `127.0.0.1` | Host address for the only published port. Keep this local when using a reverse proxy. |
| `TRACEBAG_PORT` | `9090` | Local host port for the UI and API. |
| `TRACEBAG_DATA_VOLUME` | `tracebag_data` | Persistent keys and fallback-data volume name. |
| `TRACEBAG_POSTGRES_VOLUME` | `tracebag_postgres` | Persistent PostgreSQL volume name. |
| `TRACEBAG_POSTGRES_PASSWORD` | none | Required password used only between the app and its private PostgreSQL service. |

The published Compose file uses `restart: "no"` for session-first operation.
`deploy/compose.resident.yaml` overrides the application and database to
`restart: unless-stopped` when continuous collection is intentional. This is a
Compose deployment choice rather than an application environment variable.

## Authentication

| Variable | Default | Purpose |
| --- | --- | --- |
| `TRACEBAG_AUTH_ENABLED` | `true` | Enables cookie authentication. |
| `TRACEBAG_ADMIN_USER` | `admin` | Initial single-administrator username. |
| `TRACEBAG_ADMIN_PASSWORD_HASH` | empty | ASP.NET Core password hash. Required when authentication is enabled. |
| `TRACEBAG_AUTH_LOGIN_PERMIT_LIMIT` | `5` | Login requests allowed per client partition in one window; accepted range 2–100. |
| `TRACEBAG_AUTH_LOGIN_WINDOW_SECONDS` | `60` | Fixed login-limit window in seconds; accepted range 10–3,600. |
| `TRACEBAG_TRUSTED_PROXIES` | empty | Comma-separated literal proxy IPs or CIDR networks allowed to supply forwarded client and scheme headers. Loopback is always trusted. |

Create a compatible password hash locally with .NET:

```bash
dotnet run --project src/Tracebag.Api -- hash-password admin "choose-a-password"
```

For the Docker-only workflow, run `./scripts/generate-password-hash.sh admin`.
It reads the password without echoing it and sends it to the disposable app
container over standard input rather than placing it in shell history.

Disabling authentication is allowed only when `TRACEBAG_STAGE=local`; startup
fails for any other stage. Even in local mode, keep the port loopback-bound.
Login failures use the same public response for unknown users and wrong
passwords. The login body and credential fields are bounded, and a rejected
client receives HTTP 429 with a JSON error and `Retry-After` header once its
budget is exhausted.

Configure `TRACEBAG_TRUSTED_PROXIES` only when the proxy does not connect from
loopback. Broad networks let their members influence rate-limit attribution and
the HTTPS request scheme, so list only addresses controlled by the operator.

## Audit retention

| Variable | Default | Purpose |
| --- | --- | --- |
| `TRACEBAG_AUDIT_RETENTION_DAYS` | `30` | Maximum age of PostgreSQL audit events; accepted range 1–3,650 days. |
| `TRACEBAG_AUDIT_MAX_EVENTS` | `100000` | Maximum retained PostgreSQL audit-event count; accepted range 1,000–10,000,000. |
| `TRACEBAG_AUDIT_RETENTION_DELETE_BATCH_SIZE` | `1000` | Maximum rows deleted in one cleanup batch; accepted range 100–10,000. |
| `TRACEBAG_AUDIT_RETENTION_SCAN_SECONDS` | `300` | Delay between cleanup passes; accepted range 60–86,400 seconds. |

Audit cleanup uses the existing timestamp index, deletes oldest rows in bounded
batches, and applies both the age and count limits. PostgreSQL is the supported
release store; the file audit log is a local-development fallback and is not
managed by this retention worker.

## Durable diagnostic and incident retention

| Variable | Default | Purpose |
| --- | --- | --- |
| `TRACEBAG_DIAGNOSTIC_JOB_RETENTION_DAYS` | `30` | Maximum age of terminal, incident-unreferenced diagnostic jobs; accepted range 1–3,650 days. Job events are deleted with their job. |
| `TRACEBAG_DIAGNOSTIC_JOB_RETENTION_DELETE_BATCH_SIZE` | `100` | Maximum jobs removed in one indexed cleanup batch; accepted range 10–1,000. |
| `TRACEBAG_DURABLE_RETENTION_SCAN_SECONDS` | `300` | Delay between durable job-retention passes; accepted range 60–86,400 seconds. |
| `TRACEBAG_INCIDENT_MAX_COUNT` | `200` | Maximum retained incident workspaces; accepted range 10–100,000. Incidents never expire automatically. |

An incident owns its timeline, evidence summaries, findings, finding links, and
analysis runs. Those rows are deleted together only after an operator confirms
explicit incident deletion. Linked diagnostic jobs, counter recordings, and
artifacts are independent captures: they are protected while referenced, then
return to their normal age/count/size retention after the incident is deleted.
Incident creation stops at the configured count instead of silently discarding
an operator-owned investigation.

## Container discovery

| Variable | Default | Purpose |
| --- | --- | --- |
| `TRACEBAG_ALLOWED_LABEL` | `tracebag.enabled=true` | Required opt-in label expression. |
| `TRACEBAG_ENVIRONMENT_LABEL` | empty in the application; `tracebag.environment=production` in the release template | Additional discovery expression. Strongly recommended on shared Docker hosts. Existing labels such as `helfli.stage=test` are supported. |
| `TRACEBAG_RESTART_ENABLED` | `false` | Global gate for container restart. Targets also require their restart label. |

An empty environment-label setting means the allowed label is the only global
discovery filter. The System page reports this as needing attention because a
shared host may contain opted-in containers from unrelated Compose projects or
another Tracebag installation. See [labels.md](labels.md).

## Diagnostic runners

| Variable | Default | Purpose |
| --- | --- | --- |
| `TRACEBAG_RUNNER_IMAGE_DOTNET_8` | `tracebag-runner-dotnet-8:dev` | Local runner image for .NET 8. The release Compose file supplies the matching versioned GHCR image. |
| `TRACEBAG_RUNNER_IMAGE_DOTNET_9` | `tracebag-runner-dotnet-9:dev` | Local runner image for .NET 9. The release Compose file supplies the matching versioned GHCR image. |
| `TRACEBAG_RUNNER_IMAGE_DOTNET_10` | `tracebag-runner-dotnet-10:dev` | Local runner image for .NET 10. The release Compose file supplies the matching versioned GHCR image. |
| `TRACEBAG_RUNNER_DEFAULT_RUNTIME_MAJOR` | `8` | Fallback for an unlabeled target; explicit runtime labels are recommended. |
| `TRACEBAG_RUNNER_MEMORY_LIMIT_BYTES` | `1073741824` | Memory and memory-plus-swap ceiling applied to every temporary runner; accepted range 128 MiB–8 GiB. |
| `TRACEBAG_RUNNER_CPU_LIMIT_MILLICORES` | `1000` | CPU ceiling applied to every temporary runner; accepted range 100–8,000 millicores. |
| `TRACEBAG_RUNNER_PIDS_LIMIT` | `128` | Process ceiling applied to every temporary runner; accepted range 32–512. |
| `TRACEBAG_COUNTER_MAX_SECONDS` | `600` | Maximum live counter-session duration. |
| `TRACEBAG_DIAGNOSTIC_JOB_MAX_ACTIVE_GLOBAL` | `2` | Maximum concurrent snapshot/trace/dump jobs; each target is always limited to one. |
| `TRACEBAG_DIAGNOSTIC_JOB_DAILY_LIMIT` | `25` | Maximum capture jobs reserved per UTC day, including failed jobs. |
| `TRACEBAG_DIAGNOSTIC_JOB_MAX_DURATION_SECONDS` | `600` | Hard ceiling for server-owned job timeouts. Individual profiles are lower. |
| `TRACEBAG_FULL_DUMP_ENABLED` | `false` | Global full-memory-dump gate. The target label and per-request confirmation are also required. |
| `TRACEBAG_ANALYSIS_MAX_TRACE_BYTES` | `536870912` | Maximum registered `.nettrace` payload read by one local analysis component. |
| `TRACEBAG_ANALYSIS_MAX_STACK_BYTES` | `8388608` | Maximum registered stack snapshot read by one local analysis component. |
| `TRACEBAG_ANALYSIS_MAX_EVENTS` | `2000000` | Maximum EventPipe events processed from one trace before a bounded partial result is returned. |

All runner images and tool packages are pinned. Runtime selection uses the
target's `tracebag.dotnet.runtime` label and rejects unsupported explicit values.
Released installations download only the selected runtime runner on first use;
operators may pre-pull it when registry access is restricted during incidents.
Private-registry runners must be pre-pulled because Tracebag does not accept or
store registry credentials.
Every runner uses the same network-disabled, read-only, capability-free,
no-new-privileges baseline with init and bounded memory, CPU, and PIDs. The
defaults suit the bundled tools and demo; raise them only when a known large
heap or unusually constrained host makes a capture fail, and keep them below
the documented ceilings. Diagnostic jobs require PostgreSQL. See
[diagnostic jobs and artifacts](diagnostic-jobs.md) for fixed profiles,
lifecycle semantics, limits, manifests, and recovery behavior.

## Counter recordings

| Variable | Default | Purpose |
| --- | --- | --- |
| `TRACEBAG_COUNTER_RECORDING_ENABLED` | `true` | Enables persistent recordings. |
| `TRACEBAG_COUNTER_RECORDING_DEFAULT_INTERVAL_SECONDS` | `5` | Default interval; accepted requests currently allow 2, 5, or 10 seconds. |
| `TRACEBAG_COUNTER_RECORDING_MAX_DURATION_MINUTES` | `1440` | Maximum recording duration. |
| `TRACEBAG_COUNTER_RECORDING_MAX_ACTIVE_GLOBAL` | `3` | Maximum concurrent recordings across the instance. |
| `TRACEBAG_COUNTER_RECORDING_RETENTION_DAYS` | `7` | Retention for completed recording sessions. |

Persistent recording features require PostgreSQL.
See [counter profiling and recordings](counter-profiling.md) for presets,
rollups, recovery behavior, query caps, and 24-hour capacity targets.

## Persistent logs

| Variable | Default | Purpose |
| --- | --- | --- |
| `TRACEBAG_LOG_INGESTION_ENABLED` | `true` | Enables collectors for targets carrying `tracebag.logs.persist=true`. |
| `TRACEBAG_LOG_CHANNEL_CAPACITY` | `5000` | Global bounded queue capacity; accepted range 100–100,000. |
| `TRACEBAG_LOG_BATCH_SIZE` | `200` | Maximum entries written in one database batch. |
| `TRACEBAG_LOG_FLUSH_MILLISECONDS` | `1000` | Maximum low-volume batching delay. |
| `TRACEBAG_LOG_COLLECTOR_SCAN_SECONDS` | `5` | Docker target/recreation reconciliation interval. |
| `TRACEBAG_LOG_RETENTION_DAYS` | `7` | Global maximum log age; targets may request less. |
| `TRACEBAG_LOG_MAX_TOTAL_BYTES` | `1073741824` | Global retained raw-line byte cap. |
| `TRACEBAG_LOG_MAX_BYTES_PER_CONTAINER` | `268435456` | Default and maximum target byte cap. |
| `TRACEBAG_LOG_RETENTION_DELETE_BATCH_SIZE` | `1000` | Maximum records removed for one rule in a pass. |
| `TRACEBAG_LOG_RETENTION_SCAN_SECONDS` | `60` | Interval between retention passes. |
| `TRACEBAG_LOG_MAX_LINE_BYTES` | `262144` | Maximum retained characters for one decoded line. |

See [persistent log ingestion and search](log-ingestion.md) for target labels,
checkpoint behavior, overload handling, APIs, and operational cautions.

## Artifact retention

| Variable | Default | Purpose |
| --- | --- | --- |
| `TRACEBAG_ARTIFACT_RETENTION_HOURS` | `24` | Maximum default artifact age. |
| `TRACEBAG_ARTIFACT_MAX_COUNT` | `20` | Maximum retained artifact count. |
| `TRACEBAG_ARTIFACT_MAX_TOTAL_BYTES` | `2147483648` | Total artifact storage cap in bytes. |

## Precedence and secrets

ASP.NET Core configuration precedence applies, with environment variables used
for deployments. Do not commit real values. Keep hashes, connection strings,
provider keys, certificates, and proxy credentials in deployment secret
storage.

The app fails during startup if authentication is enabled without
`TRACEBAG_ADMIN_PASSWORD_HASH`. Compose also refuses interpolation when either
the admin hash or PostgreSQL password is missing. `.env.example` contains no
working credential.

## Health endpoints

| Endpoint | Meaning |
| --- | --- |
| `/health/live` | The ASP.NET Core process is running and can serve HTTP. It has no dependency probes. |
| `/health/ready` | PostgreSQL, Docker Engine access, and writable artifact storage are available. |

Both endpoints are intentionally unauthenticated for local container and proxy
health checks. Readiness failures return HTTP 503 with per-dependency status but
do not expose exception details or credentials.
