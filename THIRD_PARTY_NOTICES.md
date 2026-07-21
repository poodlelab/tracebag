# Third-party software notices

Tracebag is built with open-source .NET, npm, container, and diagnostic-tool
dependencies. Those components remain under their respective licenses; the
Apache License 2.0 for Tracebag does not replace them.

The exact resolved inventory is generated from `Tracebag.slnx`, both npm
lockfiles, and the runner definitions with:

```bash
node scripts/generate-license-report.mjs --output third-party-licenses.json
```

CI retains this report together with per-image SPDX SBOMs and vulnerability
reports. The review policy and the treatment of build-only LGPL/MPL components
are documented in `docs/supply-chain-security.md`.

Prominent directly used projects include .NET and ASP.NET Core (MIT), Angular
(MIT), NgRx (MIT), Astro (MIT), Entity Framework Core (MIT), Npgsql
(PostgreSQL), Docker.DotNet (Apache-2.0), xUnit.net (Apache-2.0), and the .NET
diagnostic tools (MIT). Refer to the generated inventory and package metadata
for the complete dependency graph and copyright notices.
