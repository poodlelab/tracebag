# Supply-chain security

Tracebag treats dependency, container, and build provenance checks as release
gates. The source verification and CI workflows audit all transitive NuGet
packages and both production npm trees. A high-severity npm advisory or any
NuGet advisory fails the build.

Every one of the five published images is built from digest-pinned base images.
CI retains a complete Trivy JSON report and SPDX SBOM for each image. A critical
or high finding with an upstream fixed version fails immediately.
Local image verification defaults to `linux/amd64`; set
`TRACEBAG_SCAN_PLATFORM=linux/arm64` to repeat the same gate for the other
published architecture.
The release workflow scans both `linux/amd64` and `linux/arm64` from the exact
multi-architecture digest it has published. Only that digest is keylessly signed,
and a later aggregate job verifies all five digests, attached BuildKit SBOM and
provenance attestations, and Sigstore certificate identities before the release
can proceed.
The runtime-specific runner tags may share a newer diagnostic-tool train when
that train still targets `net8.0`, has been acceptance-tested against the target
runtime, and is required to pick up security-serviced transitive dependencies.

## Temporary upstream vulnerability exception

Operating-system findings for which the selected upstream base image publishes
no fixed package are a narrowly scoped temporary exception. They are not hidden:
the complete report is retained as CI evidence, the job reports their count,
and Dependabot checks the pinned base-image digest weekly. The exception ends
automatically when Trivy reports a fixed version, because that finding then
fails the gate. There are no hand-written CVE suppressions or severity
downgrades.

Maintainers must review the retained report before a release. If an unfixed
finding is reachable in Tracebag's execution context or cannot be acceptably
mitigated, the release is blocked even though the automated fixed-version gate
passes.

## Licenses

`scripts/generate-license-report.mjs` inventories the resolved NuGet graph, both
npm lockfiles, and pinned diagnostic tools. Missing license metadata and strong
copyleft or source-available license families fail verification. The report
records reviewed build-tool licenses such as LGPL and MPL separately; these
tools produce the static website but their native binaries are not included in
the published site.

Container operating-system packages and native runtime components are covered
by each image's retained SPDX SBOM. License reports are review evidence, not
legal advice; the maintainer remains responsible for notices and license
compliance when dependencies change.
