# Release process

Tracebag uses semantic versions and publishes all product images from one signed
Git tag. A release version is immutable: never move or recreate a published
version tag.

## Published artifacts

For `vX.Y.Z`, the release workflow publishes multi-architecture Linux images:

- `ghcr.io/poodlelab/tracebag:X.Y.Z`
- `ghcr.io/poodlelab/tracebag-demo-api:X.Y.Z`
- `ghcr.io/poodlelab/tracebag-runner-dotnet-8:X.Y.Z`
- `ghcr.io/poodlelab/tracebag-runner-dotnet-9:X.Y.Z`
- `ghcr.io/poodlelab/tracebag-runner-dotnet-10:X.Y.Z`

Each manifest receives an attached SBOM and provenance attestation and is signed
with GitHub Actions' OIDC identity. Moving `X.Y` and `latest` tags are convenience
aliases; documentation and production installations use the complete version.
The release workflow scans both platform images at the exact published digest
and retains the resolved manifest, vulnerability reports, SBOM, provenance, and
digest as 90-day workflow evidence for every image.

The product-website workflow always builds and uploads its static artifact. Its
deployment job runs only when the repository variable
`TRACEBAG_PAGES_ENABLED=true`; set that variable only after GitHub Pages is
configured to use GitHub Actions. This keeps an unconfigured private repository
green without publishing the site accidentally.

The pipeline first proves that the event tag, checked-out commit, and `VERSION`
agree and that the commit is contained in `origin/main`. It then runs complete
source verification and both release Compose acceptance suites before it writes
an image. All five immutable manifests must pass digest verification, attached
attestation checks, and repository-identity signature verification. A pull-only
GHCR installation smoke test must then complete a real diagnostic and artifact
download before `X.Y`/`latest` aliases move or a GitHub Release is created.

## Maintainer checklist

1. Confirm CI and Compose E2E are green on the exact release commit.
2. Run `./scripts/release-check.sh` on a clean checkout with Docker available.
   Review the five JSON vulnerability reports and SPDX SBOMs produced by
   `./scripts/verify-images.sh` under `.tracebag/supply-chain-evidence`.
3. Rehearse clean install, backup, upgrade, restore, and rollback.
4. Review Docker socket warnings, supported scope, and configuration changes.
5. Add the version and UTC release date to `CHANGELOG.md`.
6. Ensure `VERSION`, the changelog, website quickstart, and Compose default agree.
7. Create an annotated, signed tag: `git tag -s vX.Y.Z -m "Tracebag vX.Y.Z"`.
8. Push the tag without force: `git push origin vX.Y.Z`.
9. Wait for all matrix images, scans, attestations, signatures, the pull-only
   published-image smoke, alias promotion, and the GitHub Release.
10. Make the GHCR packages public and confirm unauthenticated pulls.
11. Run the published-image smoke test on clean `linux/amd64` and `linux/arm64`
    hosts.
12. Verify the Pages deployment, download links, release notes, and screenshots.

## Verify a published image

Install Cosign, then verify the keyless signature is tied to this repository's
release workflow:

```bash
cosign verify \
  --certificate-identity-regexp \
    'https://github.com/poodlelab/tracebag/.github/workflows/release.yml@refs/tags/v[0-9]+\\.[0-9]+\\.[0-9]+' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com \
  ghcr.io/poodlelab/tracebag:0.1.0
```

Inspect the attached SBOM and provenance with an OCI-aware client before
promotion into a controlled environment.

Dependency, license, base-image pinning, scan failure, and temporary unfixed
upstream vulnerability rules are defined in
[`supply-chain-security.md`](supply-chain-security.md). No CVE is silently
suppressed.

## Failed release

Do not overwrite a partially published version. The workflow deliberately does
not create a GitHub Release when any image, signature, verification, or smoke job
fails. Document which immutable image tags escaped, fix the cause on `main`,
increment the patch version, and publish a new tag. Container registries and
downstream caches make replacing a version ambiguous even when the tag appears
editable.
