# Changelog

All notable Tracebag changes are recorded here. The project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html), and the format is
based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.1.3] - 2026-07-22

### Changed

- The Tracebag API, demo API, and their test projects now target .NET 10.
- EF Core, ASP.NET Core testing, and Npgsql dependencies now use
  their .NET 10 release lines.
- Application and demo images now use pinned .NET 10 SDK and ASP.NET Core base
  images, while runtime-specific diagnostic runners retain their independently
  validated tool-host baselines.

### Added

- A live runtime-counter screenshot captured against the bounded demo workload.

## [0.1.2] - 2026-07-22

### Added

- An optional resident Compose override for operators who deliberately need
  continuous log, counter, and Docker-event collection.
- A Traefik reverse-proxy overlay alongside the existing Caddy and Nginx
  examples.

### Changed

- The published stack is now session-first and does not restart Tracebag,
  PostgreSQL, or the demo automatically.
- The README, installation guide, security model, and product website now
  separate one-time target preparation from starting and stopping a debugging
  session.
- Remote-server guidance now treats an existing HTTPS reverse proxy as the
  normal browser path and SSH forwarding as an optional fallback.
- Docker socket documentation now states explicitly that the read-only mount
  flag does not make Docker API operations read-only.

## [0.1.1] - 2026-07-21

### Added

- On-demand downloads for the selected .NET diagnostic runner, avoiding three
  large runner pulls during initial installation.
- A visible System-page warning when container discovery has no environment
  scope.
- Direct API client documentation for cookie login and CSRF-protected writes.

### Changed

- The release template now scopes discovery to
  `tracebag.environment=production` by default.
- Installation and label guides now distinguish discovery, live logs,
  persisted log search, and .NET diagnostics with a copy-ready baseline.
- The shared .NET `/tmp` volume documentation now emphasizes that labels need
  the exact Docker volume name.

### Fixed

- Primary action text is visible on product documentation pages.

## [0.1.0] - 2026-07-21

### Added

- Explicit-label Docker container discovery and stable logical identities.
- Docker resource, event, restart, health, and OOM visibility.
- Checkpointed structured log ingestion, search, live tail, and retention.
- Runtime-aware .NET counters and durable counter recordings.
- Bounded stack, EventPipe, GC dump, and gated full-dump diagnostic jobs.
- Checksummed artifacts with storage limits, retention, and range downloads.
- Incident timelines, guided captures, notes, and portable Tracebag exports.
- Fully local stack and trace analysis with evidence-linked findings.
- Resource-bounded demo API for CPU, memory, contention, starvation, latency,
  exception, and downstream-failure scenarios.
- Docker Compose distribution, multi-architecture GHCR publishing, SBOM and
  provenance attestations, image signing, and a GitHub Pages product site.

[0.1.3]: https://github.com/poodlelab/tracebag/releases/tag/v0.1.3
[0.1.2]: https://github.com/poodlelab/tracebag/releases/tag/v0.1.2
[0.1.1]: https://github.com/poodlelab/tracebag/releases/tag/v0.1.1
[0.1.0]: https://github.com/poodlelab/tracebag/releases/tag/v0.1.0
