#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${1:-${repository_root}/.env}"

if [[ -e "${env_file}" ]]; then
  echo "Refusing to overwrite existing file: ${env_file}" >&2
  exit 1
fi

if ! command -v openssl >/dev/null 2>&1; then
  echo "openssl is required to generate the PostgreSQL password." >&2
  exit 1
fi

postgres_password="$(openssl rand -hex 32)"
admin_password_hash="$("${repository_root}/scripts/generate-password-hash.sh" admin)"
temporary_file="$(mktemp "${env_file}.tmp.XXXXXX")"
trap 'rm -f "${temporary_file}"' EXIT

awk \
  -v postgres_password="${postgres_password}" \
  -v admin_password_hash="${admin_password_hash}" \
  '
    /^TRACEBAG_POSTGRES_PASSWORD=/ {
      print "TRACEBAG_POSTGRES_PASSWORD=" postgres_password
      next
    }
    /^TRACEBAG_ADMIN_PASSWORD_HASH=/ {
      print "TRACEBAG_ADMIN_PASSWORD_HASH=" admin_password_hash
      next
    }
    { print }
  ' "${repository_root}/.env.example" >"${temporary_file}"

chmod 0600 "${temporary_file}"
mv "${temporary_file}" "${env_file}"
trap - EXIT

echo "Created ${env_file} with a random database password and hashed admin password."
