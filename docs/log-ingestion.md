# Persistent log ingestion and search

Tracebag can continuously ingest logs from explicitly opted-in Docker targets,
store them in PostgreSQL, search them after container recreation, and stream
new persisted entries to the browser.

## Enable a target

Discovery and persistence are separate opt-ins:

```yaml
labels:
  tracebag.enabled: "true"
  tracebag.logs.persist: "true"
  tracebag.logs.parser: "auto"
  tracebag.logs.retentionDays: "7"
  tracebag.logs.maxBytes: "268435456"
```

`tracebag.logs.parser` accepts `auto`, `json`, `serilog`, or `plain`. Unknown
values safely fall back to `auto`. Target retention and byte limits can only
reduce the global limits; labels cannot expand an administrator's storage cap.

## Pipeline and recovery

Each current Docker instance has one collector. Docker's multiplexed stdout and
stderr frames pass through an incremental UTF-8 line decoder, timestamp
normalizer, parser chain, and one global bounded channel. A single writer stores
batches and advances their checkpoints in the same database transaction.

Checkpoints use the exact Docker timestamp and current Docker ID. Resume starts
one second before the checkpoint because Docker's `since` boundary is inclusive.
Every entry has a SHA-256 fingerprint over Docker ID, stream, exact source
timestamp, and raw application line, so replay overlap is discarded by a unique
database index. A recreated Compose target receives a new Docker ID but keeps
the same logical target and searchable history.

If PostgreSQL is unavailable, the writer retains its current batch and retries.
New records fill the bounded channel; once full, new records are dropped rather
than allowing unbounded memory growth. Queue depth, dropped records, duplicates,
write errors, ingestion lag, and storage use are visible on **System status**
and from `GET /api/logs/status`.

## Parsing and indexing

Tracebag always retains the raw application line. The parser additionally
extracts normalized level, message, exception type, trace ID, application
timestamp, and JSON properties. The automatic parser recognizes generic JSON,
Microsoft JSON console output, Serilog compact fields, and common plain-text
levels and exception names.

PostgreSQL maintains a generated `tsvector` over message and raw line with a GIN
index. Searches can combine full text with logical container, level, stream,
exception-only, trace ID, and UTC time-range filters. Results use an opaque
timestamp/ID cursor rather than offset pagination.

API endpoints:

- `GET /api/containers/{id}/logs/search`
- `GET /api/logs/search` for cross-target searches
- `GET /api/containers/{id}/logs/live` for replayable SSE
- `GET /api/logs/status`

The live endpoint sends database entry IDs as SSE IDs and honors
`Last-Event-ID`, allowing EventSource to reconnect without silently skipping
persisted entries. Each subscriber has a bounded 1,000-entry buffer.

## Retention

Retention runs in the background and removes at most
`TRACEBAG_LOG_RETENTION_DELETE_BATCH_SIZE` records per target rule and global
storage rule in one pass. It enforces per-target age, per-target byte cap, and
the global byte cap, deleting the oldest timestamp/ID records first.

## Operational cautions

- Docker log labels and persisted log content may contain application secrets;
  configure applications not to log credentials or tokens.
- A line is truncated at `TRACEBAG_LOG_MAX_LINE_BYTES`; ingestion resumes at the
  following newline.
- The UI's **Download visible** action exports only records currently loaded and
  matching its in-view filter. It never silently downloads all retained logs.
- Current direct Docker tail endpoints remain available even when persistence is
  disabled, but indexed search requires PostgreSQL.
