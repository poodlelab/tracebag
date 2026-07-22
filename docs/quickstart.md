# Start a Tracebag debugging session

Tracebag is designed to be prepared once and started when an investigation
needs it. The published stack runs an authenticated web application and private
PostgreSQL database on one Docker host. Neither service restarts automatically.

The normal server workflow is:

1. prepare target containers before an incident;
2. configure Tracebag and an HTTPS address once;
3. start Tracebag for a debugging session;
4. use the browser to collect and export evidence;
5. stop Tracebag while preserving its named volumes.

## Requirements

- Docker Engine with Docker Compose v2
- Bash, `curl`, and OpenSSL for the setup commands below
- enough disk space for PostgreSQL, retained logs, and diagnostic artifacts
- an HTTPS reverse proxy for access over an untrusted network

While Tracebag is running, its backend has Docker administrator capabilities.
Install it only on a host you control and read [the security policy](../SECURITY.md).

## 1. Prepare a target once

Tracebag discovers only explicitly labeled containers. A .NET target also
shares its runtime diagnostic socket through a named `/tmp` volume so temporary
runner containers can use the standard `dotnet-*` tools.

Use this baseline for a .NET API with searchable logs:

```yaml
services:
  api:
    environment:
      DOTNET_EnableDiagnostics: "1"
    volumes:
      - api-dotnet-tmp:/tmp
    labels:
      tracebag.enabled: "true"
      tracebag.environment: "production"
      tracebag.displayName: "My API"
      tracebag.logs.persist: "true"
      tracebag.logs.parser: "auto"
      tracebag.logs.retentionDays: "7"
      tracebag.kind: "dotnet"
      tracebag.dotnet.runtime: "8"
      tracebag.dotnet.tmpVolume: "my_api_dotnet_tmp"

volumes:
  api-dotnet-tmp:
    name: my_api_dotnet_tmp
```

The environment value must match Tracebag's discovery scope. Use `test`,
`staging`, or another existing label instead of `production` when appropriate.

Docker labels and mounts are fixed when a container is created. Add this
configuration before an incident. Recreating a target during an investigation
can alter its state and discard the condition you wanted to inspect.

The options have separate effects:

| Configuration | Container list | Live logs | Stored log search | .NET diagnostics |
| --- | --- | --- | --- | --- |
| `tracebag.enabled=true` | yes | yes | no | no |
| plus `tracebag.logs.persist=true` | yes | yes | yes | no |
| plus the .NET labels and shared `/tmp` volume | yes | yes | yes | yes |

`tracebag.dotnet.tmpVolume` is the actual Docker volume name, not necessarily
the Compose key. Giving the volume an explicit `name`, as above, prevents
Compose from silently prefixing it. See the [complete label reference](labels.md)
for live-log only and non-.NET examples.

## 2. Set up Tracebag on the host

Download an explicit release rather than relying on a moving image tag:

```bash
mkdir tracebag && cd tracebag
export TRACEBAG_VERSION=0.1.3
curl -fsSLo compose.yaml \
  "https://raw.githubusercontent.com/poodlelab/tracebag/v${TRACEBAG_VERSION}/deploy/compose.release.yaml"
curl -fsSLo compose.resident.yaml \
  "https://raw.githubusercontent.com/poodlelab/tracebag/v${TRACEBAG_VERSION}/deploy/compose.resident.yaml"
curl -fsSLo .env.example \
  "https://raw.githubusercontent.com/poodlelab/tracebag/v${TRACEBAG_VERSION}/deploy/.env.release.example"
cp .env.example .env
```

Generate the PostgreSQL secret and administrator password hash. The hash command
uses the released image and requires neither the source tree nor a local .NET
SDK:

```bash
postgres_password="$(openssl rand -hex 32)"
sed -i.bak \
  "s|^TRACEBAG_POSTGRES_PASSWORD=.*|TRACEBAG_POSTGRES_PASSWORD=${postgres_password}|" \
  .env

read -rsp "Tracebag admin password: " admin_password; echo
password_hash="$(printf '%s\n' "${admin_password}" | \
  docker run --rm -i "ghcr.io/poodlelab/tracebag:${TRACEBAG_VERSION}" \
  hash-password admin)"
unset admin_password
sed -i.bak \
  "s|^TRACEBAG_ADMIN_PASSWORD_HASH=.*|TRACEBAG_ADMIN_PASSWORD_HASH=${password_hash}|" \
  .env
rm -f .env.bak
chmod 600 .env
```

Set the scope to the label used by the prepared targets:

```dotenv
TRACEBAG_ALLOWED_LABEL=tracebag.enabled=true
TRACEBAG_ENVIRONMENT_LABEL=tracebag.environment=production
```

Keep `.env` private. It contains a database credential and an administrator
password hash.

## 3. Choose how the browser reaches Tracebag

### Remote server with an HTTPS reverse proxy

This is the normal remote-server setup. Keep Tracebag bound to loopback and add
a route to the reverse proxy already serving the host:

```dotenv
TRACEBAG_BIND_ADDRESS=127.0.0.1
TRACEBAG_PORT=9090
TRACEBAG_PUBLIC_URL=https://tracebag.example.com
```

Configure the proxy once. It can remain running between sessions. While
Tracebag is stopped, the route has no backend; after Tracebag starts, the same
URL becomes available. Tested Caddy, Nginx, and Traefik configurations are in
the [reverse-proxy guide](reverse-proxy.md).

### Local machine

Keep the defaults and open `http://localhost:9090`.

### Private network or VPN

An internal HTTPS reverse proxy can publish Tracebag only inside a private
network or VPN. Keep the Tracebag port private and use a trusted certificate for
the internal hostname. Tracebag's authentication cookie is secure-only, so a
plain `http://` private address is not a supported remote login path.

An SSH port forward is an optional fallback when the server has no reverse
proxy, private route, or VPN. It is not required by Tracebag.

## 4. Start a debugging session

The first pull downloads Tracebag and PostgreSQL. Diagnostic runners are
downloaded only when their declared .NET runtime is first used.

```bash
docker compose --env-file .env -f compose.yaml pull tracebag-postgres tracebag
docker compose --env-file .env -f compose.yaml up -d --wait
```

Open the configured `TRACEBAG_PUBLIC_URL`, sign in as `admin`, and verify the
prepared application appears. These commands help diagnose startup problems:

```bash
curl --fail http://localhost:9090/health/ready
docker compose --env-file .env -f compose.yaml logs -f tracebag
```

The ready endpoint returns HTTP 200 only when PostgreSQL, Docker Engine access,
and artifact storage are available.

## 5. Know what the session can collect

- Live logs are available while Tracebag is running.
- `tracebag.logs.persist=true` enables ingestion and search. When a session
  starts, Tracebag can ingest logs Docker still retains for the current
  container. It cannot recover logs Docker has rotated or deleted.
- Counters, recordings, stack snapshots, traces, and Docker events are captured
  only while Tracebag is running.
- The first diagnostic for a runtime may take longer while its version-pinned
  runner image is pulled.

Download important artifacts or export the incident before applying a retention
or destructive cleanup operation.

## 6. End the debugging session

```bash
docker compose --env-file .env -f compose.yaml down
```

This removes the Tracebag and PostgreSQL containers and their Compose network.
The backend no longer holds Docker access. Named volumes preserve the database,
authentication keys, retained logs, incidents, and artifacts for a later
session. Runner images and application images remain in Docker's local image
cache.

To permanently delete the complete Tracebag data set:

```bash
docker compose --env-file .env -f compose.yaml down --volumes
```

Do not run that command unless deletion of the database, keys, logs, incidents,
and artifacts is intentional. Previously exported files and backups are not
affected.

## Optional continuous operation

Resident mode is for operators who need uninterrupted log ingestion, Docker
events, or long-running counter recordings. It keeps the same security model
but leaves Docker access active continuously:

```bash
docker compose --env-file .env \
  -f compose.yaml -f compose.resident.yaml \
  up -d --wait
```

Use both files for subsequent resident-mode upgrade and stop commands. Review
the [operations guide](operations.md), configure HTTPS, and maintain backups and
retention before choosing this mode.

## Add the bounded demo

Download the matching demo overlay and start it with the session:

```bash
curl -fsSLo compose.demo.yaml \
  "https://raw.githubusercontent.com/poodlelab/tracebag/v${TRACEBAG_VERSION}/deploy/compose.demo.release.yaml"
docker compose --env-file .env \
  -f compose.yaml -f compose.demo.yaml pull
docker compose --env-file .env \
  -f compose.yaml -f compose.demo.yaml \
  up -d --wait tracebag-postgres tracebag tracebag-demo-api
```

Continue with the [ten-minute guided tour](demo-tour.md). The demo publishes its
control API only on `127.0.0.1:9091` and bounds every synthetic workload.

## Calling the API directly

The login response includes a CSRF token. API clients must retain the login
cookie and send that token as `X-CSRF-TOKEN` on every mutating request other
than login. See the [API client guide](api.md) for a complete example.

## Build from source

Contributors can build the same components from a repository checkout:

```bash
./scripts/init-env.sh
./scripts/dev-up.sh
./scripts/smoke-test.sh
```

This path builds the application and all pinned diagnostic runners locally.
