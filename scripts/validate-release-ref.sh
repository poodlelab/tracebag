#!/usr/bin/env bash
set -euo pipefail

tag_name="${1:?Usage: validate-release-ref.sh <tag> <commit> [main-ref]}"
expected_commit="${2:?Usage: validate-release-ref.sh <tag> <commit> [main-ref]}"
main_ref="${3:-origin/main}"

if [[ ! "${tag_name}" =~ ^v([0-9]+\.[0-9]+\.[0-9]+)$ ]]; then
  echo "Release tag must have the form vX.Y.Z: ${tag_name}" >&2
  exit 1
fi

version="${BASH_REMATCH[1]}"
version_file="$(tr -d '[:space:]' <VERSION)"
if [[ "${version}" != "${version_file}" ]]; then
  echo "Release tag ${tag_name} does not match VERSION (${version_file})." >&2
  exit 1
fi

tag_ref="refs/tags/${tag_name}"
git rev-parse --verify --quiet "${tag_ref}^{commit}" >/dev/null || {
  echo "Release tag is not available in the checkout: ${tag_ref}" >&2
  exit 1
}
git rev-parse --verify --quiet "${main_ref}^{commit}" >/dev/null || {
  echo "Mainline reference is not available in the checkout: ${main_ref}" >&2
  exit 1
}

resolved_expected="$(git rev-parse "${expected_commit}^{commit}")"
resolved_tag="$(git rev-parse "${tag_ref}^{commit}")"
resolved_head="$(git rev-parse 'HEAD^{commit}')"

if [[ "${resolved_tag}" != "${resolved_expected}" || "${resolved_head}" != "${resolved_expected}" ]]; then
  echo "The checked-out commit, release tag, and expected event commit must be identical." >&2
  echo "expected=${resolved_expected} tag=${resolved_tag} head=${resolved_head}" >&2
  exit 1
fi

if ! git merge-base --is-ancestor "${resolved_expected}" "${main_ref}"; then
  echo "Release commit ${resolved_expected} is not contained in ${main_ref}." >&2
  exit 1
fi

echo "Validated ${tag_name} at ${resolved_expected}; commit is contained in ${main_ref}."
