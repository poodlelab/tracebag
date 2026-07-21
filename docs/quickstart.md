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
export TRACEBAG_VERSION=0.1.0
curl -fsSLo compose.yaml \
  "https://raw.githubusercontent.com/poodlelab/tracebag/v${TRACEBAG_VERSION}/deploy/compose.release.yaml"
curl -fsSLo .env.example \
  "https://raw.githubusercontent.com/poodlelab/tracebag/v${TRACEBAG_VERSION}/deploy/.env.release.example"
cp .env.example .env
```

Fill the PostgreSQL secret and generate the administrator hash using the same
released image that will be installed:

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

Pull the application and all three runtime runner images, then start only the
long-running services:

```bash
docker compose --env-file .env -f compose.yaml --profile runners pull
docker compose --env-file .env -f compose.yaml \
  up --detach --wait tracebag-postgres tracebag
```

Open <http://localhost:9090> and sign in as `admin`. The default Compose mapping
is `127.0.0.1:9090`; it does not listen on every host interface.

Keep `TRACEBAG_VERSION` pinned. Review [the operations guide](operations.md)
before upgrading or changing volume names.

## Make an application detectable

All containers are invisible by default. Add this label to any container whose
logs Tracebag may read:

```yaml
services:
  api:
    labels:
      tracebag.enabled: "true"
```

For .NET diagnostics, opt in explicitly and share a named `/tmp` volume so the
temporary runner can reach the runtime diagnostic socket:

```yaml
services:
  api:
    environment:
      DOTNET_EnableDiagnostics: "1"
    volumes:
      - tracebag-dotnet-tmp:/tmp
    labels:
      tracebag.enabled: "true"
      tracebag.kind: dotnet
      tracebag.dotnet.tmpVolume: my-api-tracebag-dotnet-tmp

volumes:
  tracebag-dotnet-tmp:
    name: my-api-tracebag-dotnet-tmp
```

The label must contain the actual Docker volume name, not merely its Compose
key. See [the complete label reference](labels.md).

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

## Build from source

Contributors can build the same components from a repository checkout:

```bash
./scripts/init-env.sh
./scripts/dev-up.sh
./scripts/smoke-test.sh
```

This path builds the application and all pinned diagnostic runners locally.
