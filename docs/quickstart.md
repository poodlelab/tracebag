# Standalone Docker quickstart

This installs a pinned Tracebag release from GitHub Container Registry, starts
PostgreSQL, applies database migrations, and publishes the UI on the local
machine only. A source-build workflow is included at the end for contributors.

## Requirements

- Docker Engine with Docker Compose v2
- Bash, `curl`, and OpenSSL
- enough disk space for PostgreSQL and diagnostic artifacts

The Docker socket gives Tracebag powerful access to the Docker host. Install it
only on a host you control and read [the security policy](../SECURITY.md).

## Install a released version

Download the Compose and environment templates for an explicit version:

```bash
mkdir tracebag && cd tracebag
export TRACEBAG_VERSION=0.1.1
curl -fsSLo compose.yaml \
  "https://raw.githubusercontent.com/poodlelab/tracebag/v${TRACEBAG_VERSION}/deploy/compose.release.yaml"
curl -fsSLo .env.example \
  "https://raw.githubusercontent.com/poodlelab/tracebag/v${TRACEBAG_VERSION}/deploy/.env.release.example"
cp .env.example .env
```

Fill the PostgreSQL secret and generate the administrator hash using the same
released image that will be installed. This is a Docker-only command; it does
not require the source tree or a local .NET SDK:

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

Pull and start the two long-running services:

```bash
docker compose --env-file .env -f compose.yaml pull tracebag-postgres tracebag
docker compose --env-file .env -f compose.yaml \
  up --detach --wait tracebag-postgres tracebag
```

Diagnostic runners are not part of the initial download. Tracebag pulls only
the runner matching a target's declared .NET runtime when diagnostics are first
used. The first diagnostic therefore takes longer than subsequent captures.

Open <http://localhost:9090> and sign in as `admin`. The default Compose mapping
is `127.0.0.1:9090`; it does not listen on every host interface.

Keep `TRACEBAG_VERSION` pinned. Review [the operations guide](operations.md)
before upgrading or changing volume names.

## Connect an application

The release template scopes discovery to
`tracebag.environment=production`. Use another value if appropriate, but keep
the expression in `.env` and the label on every target identical:

```dotenv
TRACEBAG_ALLOWED_LABEL=tracebag.enabled=true
TRACEBAG_ENVIRONMENT_LABEL=tracebag.environment=production
```

Use this complete baseline for a .NET API with searchable logs:

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

The labels have distinct effects:

| Configuration | Container list | Live logs | Stored log search | .NET diagnostics |
| --- | --- | --- | --- | --- |
| `tracebag.enabled=true` | yes | yes | no | no |
| plus `tracebag.logs.persist=true` | yes | yes | yes | no |
| plus the .NET labels and shared `/tmp` volume | yes | yes | yes | yes |

`tracebag.enabled=true` does **not** store logs. Persistence is a separate,
explicit opt-in because retained logs can contain sensitive data and consume
database storage.

The value of `tracebag.dotnet.tmpVolume` must be the actual Docker volume name,
not merely the Compose key. Compose commonly prefixes unnamed volumes with its
project name, so giving the volume an explicit `name` as above avoids guessing.
See [the complete label reference](labels.md).

## Operate the released stack

```bash
# Start or update to the images configured in .env
docker compose --env-file .env -f compose.yaml up -d --wait

# Verify readiness
curl --fail http://localhost:9090/health/ready

# Stop containers while retaining all data
docker compose --env-file .env -f compose.yaml down
```

`docker compose --env-file .env -f compose.yaml logs -f tracebag` shows
startup and migration logs. Data remains in the `tracebag_data`,
`tracebag_artifacts`, and `tracebag_postgres` volumes after shutdown.

Do not use `docker compose down --volumes` unless you intend to permanently
delete the installation's database, keys, and stored evidence.

## Add the bounded demo

Download the demo overlay for the same release, then start the base stack and
resource-bounded demo API together:

```bash
curl -fsSLo compose.demo.yaml \
  "https://raw.githubusercontent.com/poodlelab/tracebag/v${TRACEBAG_VERSION}/deploy/compose.demo.release.yaml"
docker compose --env-file .env \
  -f compose.yaml -f compose.demo.yaml pull
docker compose --env-file .env \
  -f compose.yaml -f compose.demo.yaml \
  up -d --wait tracebag-postgres tracebag tracebag-demo-api
```

From a source checkout, the equivalent command can also launch optional normal
traffic:

```bash
./scripts/demo-up.sh --traffic
```

Continue with the [ten-minute guided tour](demo-tour.md). The demo is published
only on `127.0.0.1:9091`, has CPU/memory/PID limits, and automatically stops or
resets every synthetic workload.

## Remote access

Keep the application bound to `127.0.0.1` and terminate HTTPS in a reverse
proxy. Follow [the Caddy and Nginx guide](reverse-proxy.md). A same-host proxy
using loopback needs no extra trust setting. If the proxy connects from another
address, set `TRACEBAG_TRUSTED_PROXIES` to only that literal IP or controlled
CIDR network. Do not expose port 9090 directly to a network.

## Calling the API directly

The login response includes a CSRF token. API clients must retain the login
cookie and send that token as `X-CSRF-TOKEN` on every `POST`, `PUT`, `PATCH`, or
`DELETE` request other than login. See the [API client guide](api.md) for a
complete `curl` example.

## Build from source

Contributors can build the same components from a repository checkout:

```bash
./scripts/init-env.sh
./scripts/dev-up.sh
./scripts/smoke-test.sh
```

This path builds the application and all pinned diagnostic runners locally.
