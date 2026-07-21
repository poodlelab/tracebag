# Changelog

All notable Tracebag changes are recorded here. The project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html), and the format is
based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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

[0.1.1]: https://github.com/poodlelab/tracebag/releases/tag/v0.1.1
[0.1.0]: https://github.com/poodlelab/tracebag/releases/tag/v0.1.0
