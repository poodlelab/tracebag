# Support

Tracebag is an open-source, self-hosted project maintained on a best-effort
basis. It is not currently backed by a commercial support agreement or an
availability SLA.

## Where to ask

- Use GitHub Discussions for installation questions, operational advice, and
  design conversations.
- Use the bug-report template for reproducible defects.
- Use the feature-request template for proposed product changes.
- Use GitHub's private vulnerability reporting channel for security issues.

Search existing issues and documentation before opening a new report. Include
the Tracebag version, Docker and Compose versions, host architecture, relevant
configuration with secrets removed, and the smallest safe reproduction.

## Supported scope

The first release supports:

- Docker Engine with Docker Compose v2 on Linux hosts;
- the published `linux/amd64` and `linux/arm64` Tracebag images;
- diagnostics for opted-in .NET 8, .NET 9, and .NET 10 containers;
- PostgreSQL 16 as supplied by the release Compose file;
- the latest patch of the latest released Tracebag minor version.

Docker Desktop may be useful for evaluation, but host namespace and diagnostic
socket behavior can differ from native Linux. Kubernetes, Podman, containerd
without Docker compatibility, Windows containers, Docker Swarm, and remote
multi-user tenancy are outside the initial supported scope.

## Data and incidents

Never attach production logs, dumps, traces, credentials, database contents,
or exported incident bundles to a public issue. Create a synthetic reproduction
or privately report the problem.
