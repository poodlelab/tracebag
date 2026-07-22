#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repository_root}"

required_files=(
  "VERSION"
  "CHANGELOG.md"
  "AGENTS.md"
  "AI_USAGE.md"
  "THIRD_PARTY_NOTICES.md"
  "SECURITY.md"
  "SUPPORT.md"
  "docs/architecture.md"
  "docs/api.md"
  "docs/configuration.md"
  "docs/quickstart.md"
  "docs/operations.md"
  "docs/testing.md"
  "docs/supply-chain-security.md"
  "docs/releasing.md"
  "deploy/compose.release.yaml"
  "deploy/compose.resident.yaml"
  "deploy/compose.demo.release.yaml"
  "deploy/.env.release.example"
  "deploy/reverse-proxy/traefik/compose.traefik.yaml"
  ".github/workflows/ci.yml"
  ".github/workflows/release.yml"
  ".github/workflows/pages.yml"
  "website/package-lock.json"
  "tests/browser/package-lock.json"
  "website/src/pages/index.astro"
)

for required_file in "${required_files[@]}"; do
  [[ -f "${required_file}" ]] || {
    echo "Missing required repository file: ${required_file}" >&2
    exit 1
  }
done

version="$(tr -d '[:space:]' <VERSION)"
[[ "${version}" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || {
  echo "VERSION is not semantic: ${version}" >&2
  exit 1
}
grep -Fq "## [${version}]" CHANGELOG.md
grep -Fq "TRACEBAG_VERSION=${version}" deploy/.env.release.example
grep -Fq "export TRACEBAG_VERSION=${version}" website/src/pages/getting-started.astro
./scripts/test-release-ref-validation.sh

dotnet tool restore
dotnet restore Tracebag.slnx
dotnet build Tracebag.slnx --configuration Release --no-restore
dotnet test Tracebag.slnx --configuration Release --no-build
./scripts/verify-dependencies.sh
license_report="$(mktemp)"
node scripts/generate-license-report.mjs --output "${license_report}"
rm -f "${license_report}"

if rg -n 'new HostConfig' src/Tracebag.Api/Diagnostics --glob '!DiagnosticRunnerContainerPolicy.cs'; then
  echo "Diagnostic runner HostConfig must be constructed only by DiagnosticRunnerContainerPolicy." >&2
  exit 1
fi

npm ci --prefix src/Tracebag.Web
npm test --prefix src/Tracebag.Web
npm run build --prefix src/Tracebag.Web
if grep -Eq 'media="print"[^>]*onload=' src/Tracebag.Web/dist/tracebag-web/browser/index.html; then
  echo "Angular emitted an inline stylesheet loader that is blocked by Tracebag's CSP." >&2
  exit 1
fi
npm ci --prefix website
npm run build --prefix website
node scripts/check-website-links.mjs

if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
  TRACEBAG_POSTGRES_PASSWORD=compose-validation-only \
  TRACEBAG_ADMIN_PASSWORD_HASH=compose-validation-only \
    docker compose --env-file /dev/null --file deploy/compose.release.yaml config --quiet
  TRACEBAG_POSTGRES_PASSWORD=compose-validation-only \
  TRACEBAG_ADMIN_PASSWORD_HASH=compose-validation-only \
    docker compose --env-file /dev/null --file deploy/compose.release.yaml --file deploy/compose.resident.yaml config --quiet
  TRACEBAG_POSTGRES_PASSWORD=compose-validation-only \
  TRACEBAG_ADMIN_PASSWORD_HASH=compose-validation-only \
    docker compose --env-file /dev/null --file deploy/compose.release.yaml --file deploy/compose.demo.release.yaml config --quiet
  TRACEBAG_POSTGRES_PASSWORD=compose-validation-only \
  TRACEBAG_ADMIN_PASSWORD_HASH=compose-validation-only \
  TRACEBAG_HOSTNAME=tracebag.example.com \
    docker compose --env-file /dev/null --file deploy/compose.release.yaml --file deploy/reverse-proxy/traefik/compose.traefik.yaml config --quiet
else
  echo "Docker Compose unavailable; Compose validation skipped." >&2
fi

if rg -n 'restart: unless-stopped' deploy/compose.release.yaml deploy/compose.demo.release.yaml; then
  echo "Session-first Compose files must not restart services automatically." >&2
  exit 1
fi
rg -q 'restart: unless-stopped' deploy/compose.resident.yaml

for shell_script in scripts/*.sh runners/common/entrypoint.sh; do
  bash -n "${shell_script}"
done

rg -q 'linux/amd64,linux/arm64' .github/workflows/release.yml
rg -q 'sbom: true' .github/workflows/release.yml
rg -q 'cosign sign' .github/workflows/release.yml
rg -q 'validate-release-ref\.sh.*origin/main' .github/workflows/release.yml
rg -q 'verify-published-runtime\.sh' .github/workflows/release.yml
rg -q 'Refuse to overwrite an immutable version' .github/workflows/release.yml
if rg -n '^[[:space:]]*docker (build|compose([^#]*) build)' scripts/verify-published-runtime.sh; then
  echo "Published-image verification must pull registry artifacts, never build source." >&2
  exit 1
fi
rg -q 'actions/deploy-pages@[0-9a-f]{40}' .github/workflows/pages.yml
if rg -n 'uses: [^#[:space:]]+@v[0-9]' .github/workflows; then
  echo "GitHub Actions must be pinned to immutable full commit SHAs." >&2
  exit 1
fi
for dockerfile in Dockerfile demo/Dockerfile runners/dotnet-8/Dockerfile runners/dotnet-9/Dockerfile runners/dotnet-10/Dockerfile; do
  if rg -n '^FROM ' "${dockerfile}" | rg -v '@sha256:[0-9a-f]{64}([[:space:]]+AS|$)'; then
    echo "Every Docker base image must include an immutable digest: ${dockerfile}" >&2
    exit 1
  fi
done
rg -q 'aquasecurity/trivy-action@[0-9a-f]{40}' .github/workflows/ci.yml
rg -q 'format: spdx-json' .github/workflows/ci.yml
rg -Fq -- "--format '{{json .SBOM}}'" .github/workflows/release.yml
rg -Fq -- "--format '{{json .Provenance}}'" .github/workflows/release.yml
rg -q 'Human-directed, AI-assisted development' website/src/components/Footer.astro

if rg -n -i '\b(phase[[:space:]]*[0-9]+|mvp|release candidate|work in progress|wip)\b' \
  README.md CONTRIBUTING.md SECURITY.md CHANGELOG.md docs scripts .github \
  --glob '!verify.sh'; then
  echo "Internal development language remains in the public repository surface." >&2
  exit 1
fi

echo "Repository verification passed."
