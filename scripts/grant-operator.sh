#!/usr/bin/env bash
# Makes an already signed-in user a platform operator (local development only).
# Usage: bash scripts/grant-operator.sh <email>
set -euo pipefail
cd "$(dirname "$0")/.."
[ $# -eq 1 ] || { echo "Usage: $0 <email>"; exit 2; }
set -a; . ./.env; set +a

Database__ConnectionString="Host=localhost;Port=5433;Database=healthos;Username=hos_app;Password=$HOS_APP_PASSWORD" \
Authentication__Oidc__ClientSecret="$HOS_BFF_CLIENT_SECRET" \
  dotnet .run/core-api/HealthOS.CoreApi.dll platform grant-operator "$1" \
  && echo "Done. Sign out and sign in again." \
  || echo "No user with that email has signed in yet. Sign in once, then retry."
