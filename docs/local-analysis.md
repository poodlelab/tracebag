# Local trace and stack analysis

A captured incident can be turned into deterministic findings without sending
data to an external service. Analysis runs automatically at the end of a guided
capture and can be rerun with `POST /api/incidents/{incidentId}/analysis`.

## Analysis envelope

Every run persists an `AnalysisEnvelope` with schema version `1` and analyzer
version `tracebag-local/1`. The envelope contains the incident window, bounded
sources, independently reported analyzer components, observations, cross-signal
correlations, limitations, and a disclosure block. Every run records
`localOnly=true`, `externalProvidersUsed=false`, and `rawPayloadsIncluded=false`.
This versioned envelope is also included in the default portable Tracebag ZIP.

Every observation includes its confidence and exact incident evidence IDs.
The UI uses those IDs to navigate back to the stack, trace, counter, Docker, or
log snapshot that supports the observation.

## Analyzers

- Stack snapshots are bounded by size, normalized to remove volatile addresses
  and async state-machine suffixes, and grouped into repeated call-path shapes.
- `.nettrace` files are parsed locally with EventPipe support. Sampled frames,
  contention, exceptions, GC activity, and thread-pool events are summarized.
- Counter peaks, bounded log errors, and Docker CPU, memory, health, and OOM
  facts produce signal observations.
- Correlations require compatible observations from at least two evidence
  sources, such as CPU samples plus Docker/runtime pressure.

Stack, trace, and signal components fail independently. An unreadable or
unsupported artifact is represented as a failed component and an explicit
limitation; successful observations remain available and the incident evidence
is never modified. Registered server-owned paths are used for artifact reads,
with configured byte and event limits.

## Interpretation

Findings are observations, not diagnoses. A stack snapshot is instantaneous,
counter peaks are bounded to the capture window, and trace symbol resolution is
best effort. The UI keeps these limitations next to the results so an operator
can distinguish direct evidence from a cross-signal inference.
