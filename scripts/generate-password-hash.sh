#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
image="${TRACEBAG_IMAGE:-tracebag:dev}"
user="${1:-admin}"

if ! docker image inspect "${image}" >/dev/null 2>&1; then
  echo "Building ${image} first..." >&2
  docker build --tag "${image}" "${repository_root}" >&2
fi

read -r -s -p "Tracebag password: " password
echo >&2
read -r -s -p "Repeat password: " password_confirmation
echo >&2

if [[ -z "${password}" ]]; then
  echo "Password must not be empty." >&2
  exit 1
fi

if [[ "${password}" != "${password_confirmation}" ]]; then
  echo "Passwords do not match." >&2
  exit 1
fi

printf '%s\n' "${password}" \
  | docker run --rm --interactive --entrypoint dotnet "${image}" \
      Tracebag.Api.dll hash-password "${user}"
