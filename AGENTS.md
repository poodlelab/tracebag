# Tracebag agent instructions

These instructions apply to the entire repository. More specific `AGENTS.md`
files may add subsystem rules but must not weaken the security and verification
requirements below.

Read `README.md`, `docs/architecture.md`, `SECURITY.md`, `CONTRIBUTING.md`, and
`AI_USAGE.md` before making a substantial change.

## Product intent

Tracebag is a self-hosted diagnostics console for Dockerized .NET applications.
It combines explicitly opted-in container discovery, durable logs, Docker and
.NET runtime signals, bounded diagnostic captures, incidents, artifacts, and
local evidence-linked analysis.

Prefer a small, dependable operator surface over a broad remote-administration
surface. Tracebag is not a shell, container orchestrator, or arbitrary command
runner.

## Non-negotiable security boundaries

- A container is invisible unless it explicitly opts in with
  `tracebag.enabled=true` and satisfies the configured label policy.
- Never accept commands, executables, arguments, provider strings, output paths,
  Docker flags, mounts, or image names directly from browser input.
- Diagnostic runners use server-owned, fixed operation profiles with bounded
  duration, output size, concurrency, mounts, and cleanup behavior.
- Preserve authentication, CSRF validation, audit events, localhost defaults,
  full-dump gates, and restart gates.
- Treat logs, traces, dumps, incidents, artifacts, and exports as sensitive.
- Do not commit or expose credentials, real diagnostic captures, customer data,
  environment files, server configuration, or private deployment tooling.
- Do not send private source, production evidence, credentials, or customer data
  to an external AI service.

If a requested change conflicts with these boundaries, stop and explain the
conflict instead of weakening the boundary.

## Repository map

- `src/Tracebag.Api/`: ASP.NET Core API, workers, Docker integration,
  persistence, diagnostics, incidents, analysis, and static UI hosting.
- `src/Tracebag.Web/`: Angular operations console using standalone components
  and NgRx Signal Store.
- `demo/Tracebag.Demo.Api/`: resource-bounded synthetic failure scenarios.
- `runners/`: runtime-specific .NET diagnostic runner images and entrypoint.
- `tests/`: backend and demo safety tests.
- `deploy/`: public Compose and reverse-proxy examples.
- `website/`: Astro product and documentation website.
- `docs/`: maintained architecture, installation, and operator documentation.
- `scripts/`: setup, lifecycle, source verification, and acceptance tests.

The ignored `archive/` and `deploy/server/` directories are local-only material
and are not part of the public product. Do not make public code depend on them.

## Standard workflow

1. Inspect the existing behavior, tests, configuration, and documentation.
2. State assumptions when the intended behavior is not evident.
3. Make the smallest coherent change that solves the user or operator problem.
4. Add or update tests for behavior, validation, security boundaries, recovery,
   and failure paths.
5. Update public documentation and examples when behavior or configuration
   changes.
6. Run verification proportional to the risk and report exactly what ran.

The complete source verification command is:

```bash
./scripts/verify.sh
```

Docker-backed acceptance tests are:

```bash
./scripts/verify-runtime.sh
./scripts/verify-diagnostics-runtime.sh
./scripts/verify-auth-runtime.sh
./scripts/verify-retention-runtime.sh
```

Do not claim that a build, test, deployment, browser flow, or runtime scenario
passed unless it was actually executed and its result was inspected.

## Backend conventions

- Use `TRACEBAG_*` environment variables and document every public setting.
- Use `tracebag.*` labels and keep label parsing explicit and deny-by-default.
- Keep wire contracts stable, errors machine-readable, and mutations auditable.
- Apply database changes through reviewed EF Core migrations.
- Use bounded queues, reads, retention, durations, counts, and payload sizes.
- Make cancellation and cleanup reliable on success, failure, timeout, restart,
  and target exit.
- Preserve stable logical target identity across container recreation.
- Never construct a shell command from request data.

## Frontend and website conventions

- Keep the backend as the source of truth for durable state.
- Prefer typed API contracts and feature-local state over duplicated global
  state.
- Keep components accessible, responsive, and usable at phone widths.
- Use the shared Tracebag brand assets and established design tokens.
- Avoid decorative dashboards, fabricated data, and controls without working
  behavior.
- Verify material visual changes in a real browser at mobile and desktop sizes.

## Test and review quality

- Tests must assert externally meaningful behavior, not implementation trivia.
- Do not generate tests by mechanically restating the code under test.
- Include negative and recovery cases for privileged or destructive operations.
- Keep changes reviewable and remove unused scaffolding, placeholder prose,
  duplicated abstractions, and dead code.
- Explain security, compatibility, migration, retention, and operational
  consequences in the pull request.
- Prefer primary documentation when researching dependencies or platform
  behavior.

## AI-assisted contributions

AI assistance is welcome under `AI_USAGE.md`. Treat generated output as a draft,
understand every accepted change, and preserve human accountability.

For significant assistance, complete the AI section in the pull-request
template. Use an `Assisted-by: <tool>` commit trailer when an AI tool materially
shaped a commit; do not name the tool as a co-author.
