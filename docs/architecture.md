# Architecture

Tracebag is an on-demand diagnostics console for Dockerized .NET applications.
An operator starts it for an investigation, works through an authenticated web
interface, and stops it afterward. During that session it discovers explicitly
opted-in containers, ingests their logs, records runtime counters, runs bounded
diagnostic captures, and correlates the evidence into incidents.

## System boundary

The supported deployment consists of the Tracebag application, PostgreSQL, a
local artifact volume, the Docker API, and short-lived runtime-specific runner
containers. The Angular application is compiled into the ASP.NET Core image and
is served from the same origin as the API.

```text
Browser
   |
   | HTTPS through an operator-managed reverse proxy
   v
Tracebag (ASP.NET Core API + Angular application)
   |             |                 |
   |             |                 +-- artifact volume
   |             +-------------------- PostgreSQL
   +---------------------------------- Docker API
                                          |
                              opted-in target containers
                                          |
                              temporary diagnostic runners
```

Tracebag is designed for a single trusted operator boundary. It binds to IPv4
loopback by default, authenticates with an administrator account, and expects a
trusted HTTPS reverse proxy when accessed remotely. PostgreSQL is never exposed
by the supplied Compose files. The default services do not restart
automatically; named volumes preserve authentication keys, database state, and
artifacts between sessions. An explicit Compose override enables resident
operation when continuous collection is required.

Targets are prepared independently of Tracebag. Their discovery labels and, for
.NET diagnostics, shared runtime-socket volume must exist when Docker creates
the target container. This lets an operator start Tracebag without recreating a
failing workload.

## Components

### ASP.NET Core application

`src/Tracebag.Api` owns the HTTP API, authentication, authorization, CSRF
validation, Docker integration, background workers, persistence, diagnostic job
coordination, local analysis, audit events, health checks, and static UI hosting.
Entity Framework Core migrations are applied during startup before readiness is
reported.

### Angular application

`src/Tracebag.Web` is a standalone-component Angular application. Feature areas
cover the overview, containers, logs, live metrics, recordings, diagnostics,
artifacts, incidents, and system status. NgRx Signal Store holds feature state;
the backend remains the source of truth for durable data.

### PostgreSQL

PostgreSQL stores container identities and instances, Docker events, log streams
and checkpoints, indexed log entries, counter recordings and rollups, diagnostic
jobs and events, artifact manifests, incidents, evidence, findings, analysis
runs, and audit events. Large diagnostic payloads are not stored in database
rows.

### Artifact storage

Traces, dumps, stacks, and exported bundles are written to a dedicated volume.
Database manifests record their size, media type, checksum, lifecycle, and
provenance. Downloads support streaming and byte ranges. Reconciliation and
retention services handle missing, untracked, expired, and over-capacity files.

### Diagnostic runners

Runtime-specific images under `runners/` contain the .NET diagnostic tools.
Tracebag selects a compatible image, starts it with a fixed operation profile,
and applies one operation-aware Docker policy to process discovery, counters,
recordings, and durable jobs. Every runner has no network, a read-only root
filesystem, no Linux capabilities, `no-new-privileges`, init, bounded memory,
CPU and PIDs, and only the target diagnostics volume. Durable jobs additionally
receive the artifact destination. Tracebag streams progress and removes the
runner after completion or cancellation. Browser input cannot provide commands,
executable paths, Docker flags, limits, or arbitrary mounts.

### Demo API

`demo/Tracebag.Demo.Api` provides bounded scenarios for normal traffic, CPU
pressure, allocation pressure, contention, ThreadPool starvation, slow calls,
exceptions, and downstream failures. Its limits make it suitable for a local
product tour and acceptance tests, not production workloads.

## Container discovery and identity

Tracebag ignores containers unless they carry `tracebag.enabled=true`. Labels
also describe display name, service, environment, runtime, diagnostic process
selection, and the permissions for restart or full dumps.

Compose project and service labels form a stable logical target identity. Each
container recreation becomes a new instance beneath that target, preserving log,
event, recording, and incident history across deployments.

## Data flows

### Logs

The ingestion coordinator follows stdout and stderr for opted-in containers,
decodes Docker framing, applies a parser chain for structured and plain-text
messages, and writes entries with a durable checkpoint. Search uses PostgreSQL
indexes and cursor pagination. Live tail replays committed entries before
switching to server-sent events so a reconnect does not silently create a gap.

### Operational metrics and counters

Docker supplies resource usage, health, OOM, restart, and lifecycle information.
.NET runners discover target processes and stream curated runtime counters.
Recordings persist normalized samples, generate one-minute rollups, and retain
notes and time links to surrounding logs.

### Diagnostic jobs

Stack snapshots, EventPipe traces, GC dumps, and process dumps use one durable job
model. A request is validated against labels, runtime compatibility, concurrency
limits, duration and size bounds, and operation-specific safety gates. Progress
and terminal status are persisted; clients receive updates over server-sent
events. Startup recovery reconciles interrupted jobs and recordings.
Terminal jobs and their event streams expire in indexed batches unless an
incident references the job.

### Incidents and local analysis

An incident groups a time window, target, notes, recordings, logs, diagnostic
jobs, evidence, and findings. Guided capture profiles create a bounded sequence
of diagnostics for common failure modes. Analysis executes locally and produces
versioned findings with confidence, limitations, and exact evidence links.
Portable Tracebag exports contain a manifest and checksums; large artifacts are
included only when explicitly selected.
Incidents do not expire automatically. A configured count ceiling stops new
creation, and confirmed deletion cascades through incident-owned rows while
releasing linked jobs, recordings, and artifacts to their independent policies.

## Security model

The Docker API is the strongest privilege in the system. The supplied deployment
therefore combines explicit label opt-in with fixed runner commands, localhost
binding, cookie authentication, CSRF protection, audited mutations, bounded
capture profiles, gated restart and full-dump operations, and no shell endpoint.
These controls reduce accidental and remote misuse but do not turn Docker access
into a low-privilege capability. The socket mount's `:ro` flag does not make
Docker API operations read-only. Session-first operation limits how long the
backend holds this authority; it does not reduce that authority while the stack
is running. Operators must protect the host, reverse proxy, credentials,
backups, retained volumes, and collected diagnostic data.

## Availability and retention

Readiness depends on required persistence and startup migrations. Optional
diagnostic capabilities can degrade without taking down log search or the UI.
Collectors use bounded buffers and retry from durable checkpoints. Retention
policies limit logs, recordings, jobs, audit events, and artifacts. Incidents
use an explicit count ceiling and operator-confirmed deletion so investigations
are never silently expired; storage caps prevent one capture type from consuming
the entire host.

The acceptance matrix exercises real HTTP/PostgreSQL behavior, the primary
browser journey, responsive layouts, clean installation, application
replacement, persistent restart, checksummed backup, incident and artifact
recovery, data-protection continuity, and readiness after recovery.

## Repository structure

```text
.github/workflows/        CI, release, and GitHub Pages automation
demo/                     Resource-bounded diagnostic target
deploy/                   Published Compose and environment templates
docs/                     Operator and contributor documentation
runners/                  Runtime-specific diagnostic runner images
scripts/                  Setup, verification, and lifecycle commands
src/Tracebag.Api/         Backend, workers, persistence, and static UI host
src/Tracebag.Web/         Angular operations console
tests/                    Backend, PostgreSQL, browser, and demo safety tests
website/                  Astro product and documentation site
```

## Design constraints

- Deployment is Docker Compose on one Docker host.
- The recommended lifecycle is an operator-started, operator-stopped diagnostic
  session; resident operation is explicit.
- PostgreSQL and local volumes are the only required persistence services.
- Only explicitly labeled containers are visible.
- Diagnostics are .NET-specific; log and Docker visibility are runtime-agnostic.
- Every capture is bounded by operation, time, size, concurrency, and storage.
- Collected evidence and analysis remain local unless the operator exports it.
- The browser can select approved operations but cannot construct commands.
