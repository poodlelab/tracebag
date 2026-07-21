# Contributing to Tracebag

Thank you for helping make production diagnostics more approachable for .NET
teams.

## Before starting

1. Read [docs/architecture.md](docs/architecture.md).
2. Read the support and security boundaries before changing Docker operations.
3. Read [AI_USAGE.md](AI_USAGE.md) and the repository rules in
   [AGENTS.md](AGENTS.md).
4. Open an issue for substantial behavior or architecture changes.

## Development setup

Install the .NET SDK selected by `global.json`, Node.js 22, npm, and Docker.
Then run:

```bash
dotnet tool restore
dotnet restore Tracebag.slnx
dotnet build Tracebag.slnx --configuration Release --no-restore
dotnet test Tracebag.slnx --configuration Release --no-build
npm ci --prefix src/Tracebag.Web
npm run build --prefix src/Tracebag.Web
npm ci --prefix website
npm run build --prefix website
```

Run `./scripts/verify.sh` for the complete local source verification.
Docker-backed acceptance tests are available in `./scripts/verify-runtime.sh`,
`./scripts/verify-diagnostics-runtime.sh`, `./scripts/verify-auth-runtime.sh`,
and `./scripts/verify-retention-runtime.sh`.

## Change expectations

- Add tests for behavior, validation, security boundaries, and failure paths.
- Keep browser input out of runner commands, paths, and Docker configuration.
- Use `TRACEBAG_*` for environment variables and `tracebag.*` for labels.
- Keep wire contracts stable and errors machine-readable.
- Document user-visible configuration and behavior.
- Avoid new external services unless the architecture explicitly requires one.
- Do not commit secrets, environment files, generated artifacts, or diagnostic
  captures.

## AI-assisted contributions

AI coding tools are welcome, but the contributor remains the author and owner of
the change. Treat generated output as a draft: review it, understand it, test it,
and be prepared to explain its design and failure modes during review.

Disclose significant AI assistance in the pull-request template. Include the
tools used, the affected areas, and the human verification performed. Incidental
completion and spelling assistance do not need detailed disclosure.

For materially assisted commits, use an `Assisted-by: <tool>` trailer rather
than naming an AI tool as a co-author. Do not submit generated issue comments or
review replies that you cannot independently support. The complete policy is in
[AI_USAGE.md](AI_USAGE.md).

## Pull requests

Keep pull requests focused and explain:

- the user or operational problem;
- the chosen design;
- security and compatibility implications;
- verification performed;
- documentation or migration requirements.
- significant AI assistance and the human verification performed.

All CI checks must pass. Reviews should pay particular attention to Docker
privileges, shell execution, path handling, data retention, authentication,
CSRF, cancellation, and runner cleanup.
