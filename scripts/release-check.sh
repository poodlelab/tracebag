#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repository_root}"

version="$(tr -d '[:space:]' <VERSION)"
expected_tag="v${version}"
requested_tag="${1:-${expected_tag}}"

if [[ "${requested_tag}" != "${expected_tag}" ]]; then
  echo "Release tag ${requested_tag} does not match VERSION (${expected_tag})." >&2
  exit 1
fi

if [[ "${TRACEBAG_ALLOW_DIRTY:-false}" != "true" ]] && [[ -n "$(git status --porcelain --untracked-files=all)" ]]; then
  echo "Release checks require a clean worktree. Commit or stash all changes first." >&2
  exit 1
fi

if git rev-parse "${expected_tag}" >/dev/null 2>&1; then
  tagged_commit="$(git rev-list -n 1 "${expected_tag}")"
  head_commit="$(git rev-parse HEAD)"
  [[ "${tagged_commit}" == "${head_commit}" ]] || {
    echo "${expected_tag} already points to a different commit; never move a release tag." >&2
    exit 1
  }
fi

if ! git rev-parse --verify --quiet 'refs/remotes/origin/main^{commit}' >/dev/null; then
  echo "origin/main is unavailable; fetch it before running release checks." >&2
  exit 1
fi
if ! git merge-base --is-ancestor HEAD origin/main; then
  echo "The release commit is not contained in origin/main." >&2
  exit 1
fi
if git rev-parse --verify --quiet "refs/tags/${expected_tag}^{commit}" >/dev/null; then
  ./scripts/validate-release-ref.sh "${expected_tag}" HEAD origin/main
fi

./scripts/verify.sh
./scripts/verify-images.sh

if git grep -nE '(BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY|ghp_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})' -- . ':!archive' >.tracebag-secret-scan 2>/dev/null; then
  cat .tracebag-secret-scan >&2
  rm -f .tracebag-secret-scan
  echo "Potential credential material found in tracked files." >&2
  exit 1
fi
rm -f .tracebag-secret-scan

echo "Release ${expected_tag} passed local checks."
