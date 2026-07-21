# Container identity and operational visibility

Tracebag separates a logical target from its current Docker container. Docker
IDs change whenever Compose recreates a service; URLs, recordings, artifacts,
and event history now use the stable logical identity instead.

## Identity precedence

Tracebag resolves identities in this order:

1. `tracebag.identity`, normalized and prefixed with `custom:`;
2. Compose project, service, and replica number, prefixed with `compose:`;
3. container name, prefixed with `name:`;
4. Docker ID as the explicit unstable fallback, prefixed with `docker:`.

For example, replica 1 of service `api` in project `orders` becomes
`compose:orders:api:1`. Recreating it changes `dockerId`, but its logical `id`
and associated recording queries remain unchanged.

Use an explicit identity for containers whose names are not stable:

```yaml
labels:
  tracebag.enabled: "true"
  tracebag.identity: "orders-primary"
```

Identities must be unique on one Docker host. Do not assign the same explicit
identity to multiple live containers.

## Operational API

`GET /api/containers/{identity}/overview` returns:

- logical and current Docker identities;
- platform, storage driver, lifecycle timestamps, PID, and exit state;
- restart count and OOM-killed state;
- Docker health status, failing streak, and the five latest bounded outputs;
- one-shot CPU, memory, network, block-I/O, and process statistics;
- recent Docker lifecycle events and known instance count.

Statistics are snapshots, refreshed by the UI every three seconds. If Docker
cannot provide stats or the target is stopped, `resources.available` is false
with a safe reason. Container health does not control Tracebag's own readiness.

`GET /api/system/status` reports Docker, PostgreSQL, artifact storage, runner
image, event collector, target counts, version, and uptime without returning
connection strings, paths, credentials, or arbitrary Docker labels.

## Event history and privacy

The Docker event collector reconnects automatically and retains at most 500
events in memory and 2,000 events in PostgreSQL. Only explicitly opted-in
targets are recorded. Internal Tracebag containers and diagnostic runners are
discarded before storage.

Event attributes use a small allowlist: container name/image, exit or signal,
health state, Compose identity fields, and non-secret Tracebag identity/display
fields. Other labels are never persisted because Docker labels can contain
sensitive configuration.

## Recreation behavior

The `container_targets` table stores the logical identity and current instance.
`container_instances` records every observed Docker ID and closes the previous
instance when a replacement appears. Historical counter recordings and new
artifacts use the logical identity, so the replacement remains one target in
the interface.
