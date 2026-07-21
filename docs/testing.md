# Testing strategy

Tracebag tests observable operator and security behavior at the narrowest layer
that can prove it. Unit tests cover bounded policies, parsing, state transitions,
and failure handling. Integration and acceptance tests use the real boundaries
where an in-memory substitute would hide important behavior.

## Source verification

Run the complete source gate with:

```bash
./scripts/verify.sh
```

This builds and tests the .NET solution, runs the Angular Vitest suite, builds
both web applications, audits dependencies and licenses, validates Compose, and
checks release invariants. The PostgreSQL integration project is present in the
solution but its Docker-backed test is skipped unless run through its dedicated
script.

## Docker-backed acceptance

The CI acceptance matrix runs these scripts independently:

```bash
./scripts/verify-http-postgres-runtime.sh
./scripts/verify-browser-runtime.sh
./scripts/verify-backup-restore-runtime.sh
./scripts/verify-runtime.sh
./scripts/verify-diagnostics-runtime.sh
./scripts/verify-auth-runtime.sh
./scripts/verify-retention-runtime.sh
```

The browser suite installs an authenticated fresh Compose environment and a
bounded demo target. It verifies login, container discovery, indexed log search,
live counters, a diagnostic artifact, download, logout, and representative phone
and desktop layouts. Failure traces, screenshots, and video stay under ignored
Playwright output directories.

The backup/restore suite removes its temporary volumes before restoring. It
proves that a real incident, evidence relationship, downloadable artifact,
data-protection key set, and authenticated cookie survive together; logout must
still terminate that restored browser session.

Every acceptance script uses unique temporary names and removes its containers,
networks, volumes, and credentials on exit. Do not point these scripts at an
existing Tracebag installation.

## Published-image acceptance

The release workflow repeats the release-install and diagnostics Compose suites
on the exact tag before publishing. After all five digests are scanned, signed,
and verified, it runs:

```bash
./scripts/verify-published-runtime.sh 0.1.0 ghcr.io/poodlelab
```

This path never builds source. It pulls all five exact `X.Y.Z` images, launches
the release Compose files, discovers the demo container and its .NET process,
captures a real stack snapshot with the published runner, and downloads the
resulting artifact. It uses isolated temporary volumes and removes them on exit.
