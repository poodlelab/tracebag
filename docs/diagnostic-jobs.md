# Diagnostic jobs and artifacts

Tracebag uses one persisted job system for trace, stack, and dump captures.
Closing the browser does not stop a capture. Every accepted request,
state transition, outcome, failure and cancellation remains visible after the
request and is represented in the audit trail.

## Fixed profiles

The API accepts only these identifiers:

| Profile | Output | Default / maximum | Purpose |
| --- | --- | --- | --- |
| `stack-snapshot` | `.txt` | one shot / 30s | Managed stacks for hangs and starvation. |
| `cpu-trace` | `.nettrace` | 30s / 120s | CPU sampling with the pinned runner profile. |
| `threading-trace` | `.nettrace` | 30s / 120s | Fixed CLR threading events. |
| `contention-trace` | `.nettrace` | 30s / 120s | Fixed CLR contention events. |
| `gc-dump` | `.gcdump` | one shot / 120s | Managed heap graph. |
| `full-dump` | `.dmp` | one shot / 300s | All process memory; sensitive and disabled by default. |

The browser provides a profile identifier, positive PID, and bounded duration.
The server chooses the executable, arguments, providers, output name, runner
image, mounts, labels and Docker isolation. No endpoint accepts a command,
executable, provider string, output path, shell fragment, or Docker setting.

Every temporary runner has networking disabled, a read-only root filesystem,
all Linux capabilities dropped, `no-new-privileges`, init, and configured
memory, CPU, and PID ceilings. Only the target's named diagnostics `/tmp`
volume and, for durable jobs, the Tracebag artifact volume are mounted. Process
discovery, live counters, and recordings never receive the artifact volume.
All creation paths use this single operation-aware policy; there is no separate
legacy trace or dump path with weaker Docker settings.

Full dumps require all three gates:

1. `TRACEBAG_FULL_DUMP_ENABLED=true` on the Tracebag installation;
2. `tracebag.diagnostics.fullDump=true` on the target container;
3. the exact risk confirmation on that individual request.

## Lifecycle and cancellation

```text
queued -> validating -> starting -> running -> collecting -> completed
   |          |            |          |            |
   +----------+------------+----------+------------+-> stopping -> cancelled
                         active states -------------> failed / timed_out / target_exited
```

Transitions and progress events are committed to PostgreSQL. The SSE endpoint
replays events after `Last-Event-ID` and then follows new events, so reconnects
do not hide a terminal result. Cancellation is idempotent: repeated requests
return the same stopping or terminal job. The service always force-removes the
temporary runner in `finally`, including cancellation, timeout, failed tools,
target exit, and artifact-finalization failure.

Limits are reserved transactionally:

- one active expensive capture per logical target;
- `TRACEBAG_DIAGNOSTIC_JOB_MAX_ACTIVE_GLOBAL` active captures instance-wide;
- `TRACEBAG_DIAGNOSTIC_JOB_DAILY_LIMIT` accepted captures per UTC day.

`Idempotency-Key` can safely retry a create request. Reusing a key with
different normalized inputs returns HTTP 409.

## API

| Method and path | Result |
| --- | --- |
| `GET /api/diagnostic-jobs/profiles` | Fixed profile catalog and enabled state. |
| `POST /api/containers/{id}/diagnostic-jobs` | Persist and start a job; returns HTTP 202. |
| `GET /api/diagnostic-jobs?containerId=…` | Recent jobs and durable states. |
| `GET /api/diagnostic-jobs/{id}` | One job, tool versions, outcome and error. |
| `GET /api/diagnostic-jobs/{id}/events` | Replayable SSE progress/lifecycle stream. |
| `POST /api/diagnostic-jobs/{id}/cancel` | Idempotent cancellation and runner cleanup. |
| `GET /api/artifacts/{id}/download` | Streaming payload with HTTP range support. |
| `GET /api/artifacts/{id}/manifest` | Versioned JSON manifest. |

Example:

```bash
curl -X POST \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: investigate-checkout-20260720' \
  -d '{"processId":1,"profile":"cpu-trace","durationSeconds":30}' \
  http://localhost:9090/api/containers/CONTAINER/diagnostic-jobs
```

Authenticated deployments also require the normal Tracebag CSRF header.

## Artifact integrity and layout

The runner writes to a server-generated staging name. After successful exit,
Tracebag streams the file through SHA-256, moves it to the durable layout, and
atomically writes the manifest:

```text
/artifacts/yyyy/MM/dd/artifact-<id>/
  payload.nettrace
  manifest.json
```

The manifest records schema version, job/artifact/target IDs, profile, PID,
runtime major, pinned runner image and tool version, normalized inputs, outcome,
size, SHA-256, creator and timestamps. Artifact database rows expose
`available` or `missing`; download paths are resolved under the configured root
and traversal is rejected before a file is opened. Downloads use file streams
and range processing, so file size is not tied to ASP.NET heap size and clients
can resume interrupted transfers.

## Startup recovery and retention

On startup, Tracebag marks nonterminal jobs failed with `tracebag_restarted`,
removes all diagnostic-job runners belonging to the instance, marks database
artifacts whose payload disappeared as `missing`, and moves unrecognized files
to `/artifacts/quarantine/<timestamp>/`. Unknown files are quarantined rather
than silently deleted. Reconciliation actions are audited.

Terminal diagnostic job rows and their event streams are retained for 30 days
by default, then removed in indexed batches. A job used as incident evidence is
protected until that incident is explicitly deleted. The artifact row and files
have an independent lifecycle: existing artifact age, count, and total-byte
rules remove both payload and manifest unless an incident references them.

Operators should back up PostgreSQL and the artifact volume together; the
manifest hash detects an incomplete or changed payload but does not repair it.
