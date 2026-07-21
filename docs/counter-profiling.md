# Counter profiling and recordings

Tracebag supports two counter workflows. Live sessions are browser-owned and
bounded by `TRACEBAG_COUNTER_MAX_SECONDS`. Persistent recordings are server-owned:
closing the page does not stop them, and the configured timeout is enforced by
the backend.

## Runtime selection

Set `tracebag.dotnet.runtime` to `8`, `9`, or `10` on every .NET target. Tracebag
selects the matching pinned runner image and stores the runtime major, image,
and diagnostic-tool version with each recording. Targets without the label use
`TRACEBAG_RUNNER_DEFAULT_RUNTIME_MAJOR` (8 by default); unsupported explicit
values are rejected instead of trying an incompatible runner.

The built-in presets are server-owned and accept no provider or command text
from the browser:

| Preset | Intended evidence |
| --- | --- |
| Runtime | CPU, memory, exceptions, GC, allocation, and ThreadPool overview. |
| ASP.NET Core | Runtime plus request-rate and current/failed request signals. |
| Kestrel | Connections, queues, TLS handshakes, and upgraded requests. |
| Thread pool | Thread count, queue length, and completed work. |
| GC pressure | Heap generations, allocation rate, collection counts, and GC time. |
| Request pressure | Request throughput, concurrency, and failures. |
| Contention | Monitor contention plus ThreadPool queue/thread context. |
| Full web profile | Runtime, ASP.NET Core, and Kestrel providers. |

Live and recorded counter runners use the common hardened runner policy. They
join only the target PID namespace, mount only the target's named `/tmp`
diagnostics volume, have no network, and never receive the artifact volume.
Their root filesystem is read-only and memory, CPU, and process counts are
bounded by the installation settings.

## Recording lifecycle and recovery

Creation reserves one per-target slot and the configured global capacity in a
serializable PostgreSQL transaction before Docker is called. A partial unique
index independently prevents two active recordings for one logical target.
Lifecycle transitions are persisted as `starting`, `running`, `stopping`, and a
terminal state. Stop is idempotent at the persistence boundary.

On startup Tracebag marks jobs left active by a prior process as interrupted and
removes all recording runners carrying the same `tracebag.instance` label. This
prevents both false-running records and orphan diagnostic containers. Deletion
is refused for active recordings and requires the exact recording ID as API
confirmation after a UI confirmation.

## Samples, rollups, and query limits

The one shared backend parser normalizes both live and recorded CSV output.
Recording batches atomically append raw samples and update one-minute aggregates
containing average, minimum, maximum, and count. Historical series return a
summary with min/max/weighted-average, peak time, and sample count.

Resolution `auto` uses one-minute data for windows over two hours or whenever
the estimated raw result would exceed 50,000 points. Explicit raw reads are
capped at 50,000 points and clearly report truncation; one-minute reads are
capped at 250,000 points. CSV and JSON exports are capped at 1,000,000 raw rows.

Capacity targets for the default 24-hour maximum are deliberately conservative:

| Workload | Raw sample estimate | Storage budget | Query target on a local PostgreSQL 16 install |
| --- | ---: | ---: | --- |
| 20 series, 5-second interval | 345,600 | at most 100 MB | auto/1m response under 2 seconds |
| 40 series, 5-second interval | 691,200 | at most 200 MB | auto/1m response under 2 seconds |
| 40 series, 2-second interval | 1,728,000 | at most 500 MB | auto/1m response under 3 seconds |

These are operational budgets rather than universal benchmarks; storage varies
with PostgreSQL page fill and index state. Watch database volume growth and use
the 5- or 10-second interval for day-long captures. Completed recordings and
their raw samples/rollups are removed in bounded batches according to
`TRACEBAG_COUNTER_RECORDING_RETENTION_DAYS`.

## Investigation workflow

The recording page synchronizes a cursor across charts, exposes peaks and
min/average/max values, and links the cursor to persisted logs in a two-minute
window on either side. Names and investigation notes are durable. CSV is useful
for external analysis; JSON includes both recording provenance and samples.
