#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
validator="${repository_root}/scripts/validate-release-ref.sh"
scratch="$(mktemp -d)"

cleanup() {
  rm -rf "${scratch}"
}
trap cleanup EXIT

git -C "${scratch}" init --quiet --initial-branch=main
git -C "${scratch}" config user.name "Tracebag release test"
git -C "${scratch}" config user.email "release-test@invalid.example"
printf '1.2.3\n' >"${scratch}/VERSION"
git -C "${scratch}" add VERSION
git -C "${scratch}" commit --quiet -m "release fixture"
main_commit="$(git -C "${scratch}" rev-parse HEAD)"
git -C "${scratch}" tag v1.2.3

(
  cd "${scratch}"
  "${validator}" v1.2.3 "${main_commit}" main >/dev/null
)

git -C "${scratch}" switch --quiet --create outside-main
printf '1.2.4\n' >"${scratch}/VERSION"
git -C "${scratch}" add VERSION
git -C "${scratch}" commit --quiet -m "non-main fixture"
outside_commit="$(git -C "${scratch}" rev-parse HEAD)"
git -C "${scratch}" tag v1.2.4

if (
  cd "${scratch}"
  "${validator}" v1.2.4 "${outside_commit}" main >/dev/null 2>&1
); then
  echo "Validator accepted a tag whose commit is not contained in main." >&2
  exit 1
fi

git -C "${scratch}" switch --quiet main
if (
  cd "${scratch}"
  "${validator}" release-1.2.3 "${main_commit}" main >/dev/null 2>&1
); then
  echo "Validator accepted a non-semantic release tag." >&2
  exit 1
fi

if (
  cd "${scratch}"
  "${validator}" v1.2.3 "${outside_commit}" main >/dev/null 2>&1
); then
  echo "Validator accepted an event commit that differs from the tag commit." >&2
  exit 1
fi

echo "Release reference validation tests passed."
