# Tracebag Container Labels

Tracebag discovers only explicitly opted-in containers. New installations use
the `tracebag.*` namespace exclusively; no legacy aliases are supported.

## Minimal log-only target

```yaml
services:
  api:
    labels:
      tracebag.enabled: "true"
      tracebag.displayName: "Orders API"
```

This makes the container visible and enables current live-log workflows.

## .NET diagnostics target

```yaml
services:
  api:
    environment:
      DOTNET_EnableDiagnostics: "1"
    volumes:
      - api_dotnet_tmp:/tmp
    labels:
      tracebag.enabled: "true"
      tracebag.displayName: "Orders API"
      tracebag.kind: "dotnet"
      tracebag.dotnet.runtime: "8"
      tracebag.dotnet.tmpVolume: "orders_api_dotnet_tmp"
      tracebag.restart.enabled: "false"
      # Only add this deliberate opt-in if full process dumps are required:
      tracebag.diagnostics.fullDump: "false"

volumes:
  api_dotnet_tmp:
    name: orders_api_dotnet_tmp
```

The runner joins the target PID namespace and mounts the same `/tmp` volume so
the .NET diagnostics IPC socket is available. Tracebag runners have networking
disabled, use a read-only root filesystem and strict resource ceilings, and do
not receive the Docker socket. Only durable capture jobs mount the artifact
volume; process discovery and counter runners do not.

## Supported labels

| Label | Meaning |
| --- | --- |
| `tracebag.enabled` | Required discovery opt-in; normally `true`. |
| `tracebag.identity` | Optional explicit stable identity across recreation. Compose targets receive an automatic stable identity. |
| `tracebag.displayName` | Human-readable name shown in the UI. |
| `tracebag.kind` | Set to `dotnet` to enable .NET diagnostic actions. |
| `tracebag.logs.persist` | Set to `true` to opt into checkpointed PostgreSQL log ingestion and search. |
| `tracebag.logs.parser` | Parser selection: `auto`, `json`, `serilog`, or `plain`. |
| `tracebag.logs.retentionDays` | Per-target retention, capped by the global maximum. |
| `tracebag.logs.maxBytes` | Per-target stored-byte cap, capped by the global maximum. |
| `tracebag.dotnet.tmpVolume` | Docker volume shared at `/tmp` with diagnostic runners. |
| `tracebag.dotnet.runtime` | Supported runtime major (`8`, `9`, or `10`) used to select a compatible pinned runner. |
| `tracebag.diagnostics.fullDump` | Per-target full-memory-dump gate. It is effective only when the global gate is also enabled and the request confirms the data risk. |
| `tracebag.environment` | Optional environment value used with `TRACEBAG_ENVIRONMENT_LABEL`. |
| `tracebag.restart.enabled` | Per-target restart gate; the global setting must also be enabled. |
| `tracebag.databaseAllowed` | Allows an explicitly opted-in PostgreSQL container that policy would otherwise hide. |

## Reserved internal labels

These labels belong to Tracebag and must not be set on target containers:

- `tracebag.internal`
- `tracebag.runner`
- `tracebag.instance`
- `tracebag.runnerOperation`
- `tracebag.sessionId`
- `tracebag.recording`
- `tracebag.recordingId`
- `tracebag.diagnosticJob`
- `tracebag.diagnosticJobId`
- `tracebag.profile`
- `tracebag.targetContainer`
- `tracebag.runtimeMajor`
- `tracebag.toolVersion`

Containers marked internal or as runners are always excluded from discovery.

## Restart policy

Restart requires both:

```text
TRACEBAG_RESTART_ENABLED=true
tracebag.restart.enabled=true
```

The global setting remains disabled by default.

See [container identity and operational visibility](operational-visibility.md)
for precedence, normalization, uniqueness, instance history, and event privacy.
