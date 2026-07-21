#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repository_root}"

if ! docker info >/dev/null 2>&1; then
  echo "Docker Engine is required for PostgreSQL HTTP integration testing." >&2
  exit 1
fi

suffix="$(date +%s)"
container="tracebag-postgres-http-${suffix}"
password="postgres-http-${suffix}"

cleanup() {
  docker rm --force "${container}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker run --detach --rm \
  --name "${container}" \
  --publish 127.0.0.1::5432 \
  --env POSTGRES_DB=tracebag_test \
  --env POSTGRES_USER=tracebag \
  --env "POSTGRES_PASSWORD=${password}" \
  postgres:16-alpine >/dev/null

for _ in {1..60}; do
  if docker exec "${container}" pg_isready -U tracebag -d tracebag_test >/dev/null 2>&1; then
    break
  fi
  sleep 0.25
done
docker exec "${container}" pg_isready -U tracebag -d tracebag_test >/dev/null
binding="$(docker port "${container}" 5432/tcp)"
port="${binding##*:}"
export TRACEBAG_TEST_DATABASE_URL="Host=127.0.0.1;Port=${port};Database=tracebag_test;Username=tracebag;Password=${password};Pooling=false"

dotnet test tests/Tracebag.IntegrationTests/Tracebag.IntegrationTests.csproj \
  --configuration Release \
  --filter 'Category=PostgreSqlIntegration'

echo "PostgreSQL HTTP integration tests passed against the real middleware pipeline."
