#!/usr/bin/env bash
# Starts the local stack (PostgreSQL, Keycloak, Jaeger, Mailpit), builds core-api and applies database
# roles + migrations. Synthetic data only. Used by Codespaces and by the CI e2e job.
set -euo pipefail
cd "$(dirname "$0")/.."

if [ ! -f .env ]; then
  r() { openssl rand -hex 24; }
  {
    echo "HOS_PG_SUPERUSER_PASSWORD=$(r)"
    echo "HOS_MIGRATOR_PASSWORD=$(r)"
    echo "HOS_APP_PASSWORD=$(r)"
    echo "HOS_KEYCLOAK_ADMIN_PASSWORD=$(r)"
    echo "HOS_BFF_CLIENT_SECRET=$(r)"
    echo "HOS_DEV_USER_PASSWORD=Dev-$(openssl rand -hex 6)"
    echo "HEALTHOS_AI_CONTEXT_SIGNING_KEY=$(openssl rand -hex 32)"
    echo "HOS_DEV_TOTP_SECRET=$(openssl rand -hex 10)"
  } > .env
  echo "Generated .env with random local secrets."
fi
grep -q '^HOS_DEV_TOTP_SECRET=' .env || echo "HOS_DEV_TOTP_SECRET=$(openssl rand -hex 10)" >> .env
set -a; . ./.env; set +a
if [ -n "${GITHUB_ACTIONS:-}" ]; then
  while IFS='=' read -r _ v; do echo "::add-mask::$v"; done < .env
fi

docker compose --env-file .env -f deploy/local/compose.yaml up -d --wait --wait-timeout 300

mkdir -p .run
if [ -z "${DEV_UP_SKIP_BUILD:-}" ] || [ ! -f .run/core-api/HealthOS.CoreApi.dll ]; then
  dotnet build services/core-api/src/HealthOS.CoreApi -c Release -v q -o .run/core-api
fi

PG="Host=localhost;Port=5433"
Database__AdminConnectionString="$PG;Database=postgres;Username=hos_superuser;Password=$HOS_PG_SUPERUSER_PASSWORD" \
Database__MigratorPassword="$HOS_MIGRATOR_PASSWORD" Database__AppPassword="$HOS_APP_PASSWORD" \
  dotnet .run/core-api/HealthOS.CoreApi.dll db bootstrap
Database__MigratorConnectionString="$PG;Database=healthos;Username=hos_migrator;Password=$HOS_MIGRATOR_PASSWORD" \
  dotnet .run/core-api/HealthOS.CoreApi.dll db migrate

echo "Stack is up and the database is migrated."
