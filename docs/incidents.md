# Incidents and portable Tracebags

An incident is Tracebag's durable workspace for a serious production problem. It
correlates a bounded time window of Docker state, logs, runtime counters and
diagnostic artifacts. Capture continues on the server after the browser closes.

## Guided profiles

The API accepts one of five server-owned profiles. Browser input never becomes a
runner command.

| Profile | Counter window | Core snapshot | Optional trace |
| --- | --- | --- | --- |
| Frozen API | Thread pool | Managed stacks | Threading |
| High CPU | Runtime | Managed stacks | CPU |
| High Memory | GC pressure | GC dump | — |
| Request Timeouts | Request pressure | Managed stacks | Threading |
| Lock Contention | Contention | Managed stacks | Contention |

Capture duration is clamped to 10–120 seconds. Logs are pinned to the incident
window and limited to 200 entries. Counter evidence stores the recording link,
series summaries and peaks. Diagnostic artifacts remain in the checksummed
artifact store and cannot be deleted while an incident references them.

An application restart changes an unfinished incident to `partial` and preserves
everything already captured. Terminal statuses are `ready`, `partial`, `failed`
and `closed`.

## Retention and deletion

Incidents never expire automatically. Tracebag retains at most 200 by default
and rejects a new capture when that configurable capacity is full. The system
status page shows current usage, protected capture counts, eligible expired
jobs, and the latest cleanup result.

While an incident exists, referenced diagnostic jobs, counter recordings, and
artifacts cannot be removed by their normal retention or individual delete
operations. Explicit incident deletion permanently removes the incident,
timeline, evidence summaries, findings, finding links, and analysis runs in one
database operation. It does not immediately delete linked captures. Instead,
the links are released and each job, recording, or artifact becomes eligible
for its normal retention policy. This prevents an incident deletion from
silently deleting a reusable trace or recording.

Deletion is rejected while capture or analysis is active and requires the exact
incident ID in the `confirm` query parameter. Export the portable Tracebag first
when the investigation must be retained outside the live installation.

## Findings

Incident findings are deterministic local correlations, not automated diagnoses.
Every persisted finding has one or more evidence IDs. The incident UI uses those
IDs to select and scroll to the supporting snapshot. Local trace and stack
analysis deepens those findings without changing the evidence-first contract.

## Portable ZIP export

`GET /api/incidents/{id}/export` produces a self-contained ZIP with a README,
incident summary, timeline, findings, evidence summaries and a manifest. The
manifest records selection, redaction state and SHA-256 checksums.

The safe default includes a summary file for every evidence ID, so findings stay
resolvable offline, but omits every raw payload. Pinned raw logs require
`includePinnedLogs=true`. Raw artifacts require one exact `artifactId` query item
per selected incident artifact. Sensitive artifacts additionally require
`includeSensitiveArtifacts=true`. Tracebag never expands the log time range and
never discovers or adds artifacts implicitly; a full dump therefore cannot enter
a bundle silently.

Example:

```text
/api/incidents/inc-123/export?includePinnedLogs=true&artifactId=artifact-456
```

## API

- `GET /api/incidents/profiles`
- `POST /api/containers/{containerId}/incidents`
- `GET /api/incidents`
- `GET /api/incidents/{incidentId}`
- `PATCH /api/incidents/{incidentId}` for notes or closing
- `DELETE /api/incidents/{incidentId}?confirm={incidentId}`
- `GET /api/incidents/{incidentId}/export`
