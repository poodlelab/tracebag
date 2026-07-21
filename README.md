<p align="center">
  <img src="src/Tracebag.Web/src/assets/brand/tracebag-mark.svg" alt="" width="80" height="80">
</p>

<h1 align="center">Tracebag</h1>

Tracebag is a self-hosted debugging workspace for Dockerized .NET applications.
It brings the logs, runtime counters, stack snapshots, traces, dumps, and notes
from one production problem into a single timeline on infrastructure you
control.

It is intended for the point where a service is unhealthy and logs alone no
longer explain why. Tracebag does not expose a shell or accept arbitrary Docker
commands, and it ignores every container that has not explicitly opted in.

![A Tracebag incident showing a correlated timeline and captured evidence](website/public/screenshots/demo-contention-overview.jpg)

## What it helps you do

- **Find the relevant window.** Search retained container logs, follow a live
  tail, and line events up with Docker health and resource changes.
- **Measure runtime pressure.** Inspect live or recorded .NET counters for CPU,
  allocation, GC, ThreadPool, contention, and memory behavior.
- **Capture evidence before it disappears.** Take a bounded stack snapshot,
  EventPipe trace, GC dump, or explicitly gated process dump and keep the result
  with the incident that caused it.

Tracebag 0.1.0 supports Docker Engine on Linux `amd64` and `arm64`. Runtime
diagnostics use dedicated runners for .NET 8, 9, and 10. The web application,
API, and PostgreSQL database are installed together with Docker Compose.

## Install

There is intentionally no one-line `docker compose up` command in this README.
A real installation needs a database secret and an administrator password hash
before it is safe to start.

The [Docker installation guide](docs/quickstart.md) walks through the complete
sequence:

1. Download the Compose and environment files for an explicit version.
2. Generate the two required secrets.
3. Pull the signed application and diagnostic-runner images from GHCR.
4. Start Tracebag on `127.0.0.1:9090` and verify its readiness endpoint.

The guide also covers upgrades, persistent volumes, HTTPS, backup, and restore.

## Connect a container

Tracebag cannot see a workload until the workload opts in:

```yaml
services:
  api:
    labels:
      tracebag.enabled: "true"
```

That label is enough for Docker state and logs. .NET process inspection and
profiling also require the runtime diagnostic socket to be shared through a
named `/tmp` volume. See the [container label reference](docs/labels.md) for the
complete, copyable configuration.

## Try it on the demo API

The repository includes a bounded .NET 8 workload that can generate CPU
pressure, allocations, lock contention, ThreadPool starvation, slow requests,
exceptions, and downstream failures without touching a real service.

```bash
./scripts/init-env.sh
./scripts/demo-up.sh --traffic
```

Open <http://localhost:9090>, then follow the
[ten-minute investigation](docs/demo-tour.md). Every synthetic workload has
server-owned duration and resource limits and can be reset.

## What Tracebag deliberately does not do

- It does not discover containers automatically; opt-in is required.
- It does not provide a browser shell or arbitrary Docker API access.
- It does not send captured evidence to a cloud analysis service.
- It does not hide the privilege of the Docker socket.
- It is not a Kubernetes or distributed tracing platform.

Access to the Docker socket is effectively host-level access. Keep Tracebag on
a host you control, leave its default localhost binding in place, and use a
trusted HTTPS reverse proxy for remote access. Read the
[security model](SECURITY.md) before putting it near production workloads.

## Documentation

- [Install and operate Tracebag](docs/quickstart.md)
- [Run the demo investigation](docs/demo-tour.md)
- [Configure container labels](docs/labels.md)
- [Understand diagnostics and artifacts](docs/diagnostic-jobs.md)
- [Upgrade, back up, and restore](docs/operations.md)
- [Review the architecture and trust boundaries](docs/architecture.md)

The full documentation index is available on the
[product website](https://poodlelab.github.io/tracebag/docs/).

## Development and contributions

Run the complete source, test, dependency, website, and repository checks with:

```bash
./scripts/verify.sh
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for the development setup and pull-request
expectations. Support and security reports follow [SUPPORT.md](SUPPORT.md) and
[SECURITY.md](SECURITY.md).

Tracebag is developed with substantial AI assistance. Product direction,
architecture, review, verification, security decisions, and releases remain the
maintainer's responsibility. The disclosure and contribution rules are in
[AI_USAGE.md](AI_USAGE.md), and the instructions supplied to coding agents are
public in [AGENTS.md](AGENTS.md).

## License

Apache License 2.0. See [LICENSE](LICENSE).
