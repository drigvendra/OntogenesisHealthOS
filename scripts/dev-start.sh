#!/usr/bin/env bash
# Starts everything for manual testing: stack + migrations (dev-up.sh), core-api and admin-web.
# Logs: .run/core-api.log, .run/admin-web.log. Stop with: bash scripts/dev-stop.sh
set -euo pipefail
cd "$(dirname "$0")/.."

bash scripts/dev-stop.sh >/dev/null 2>&1 || true
bash scripts/dev-up.sh
set -a; . ./.env; set +a

export ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080
export Database__ConnectionString="Host=localhost;Port=5433;Database=healthos;Username=hos_app;Password=$HOS_APP_PASSWORD"
export Authentication__Oidc__ClientSecret="$HOS_BFF_CLIENT_SECRET"
export Authentication__Oidc__PublicOrigin="http://localhost:5173"

nohup dotnet .run/core-api/HealthOS.CoreApi.dll > .run/core-api.log 2>&1 &
echo $! > .run/core-api.pid
nohup pnpm --filter @healthos/admin-web dev > .run/admin-web.log 2>&1 &
echo $! > .run/admin-web.pid

for i in $(seq 60); do curl -fs http://localhost:5080/health/ready >/dev/null && break; sleep 2; done
for i in $(seq 60); do curl -fs http://localhost:5173/ >/dev/null && break; sleep 2; done
curl -fsS http://localhost:5080/health/ready >/dev/null || { echo "core-api did not become ready; see .run/core-api.log"; exit 1; }

# Authenticator apps expect the TOTP key in base32 (Keycloak uses the raw text secret).
TOTP_KEY=$(python3 -c "import base64,sys; print(base64.b32encode(sys.argv[1].encode()).decode().rstrip('='))" "$HOS_DEV_TOTP_SECRET")

cat <<EOF

HealthOS is running (synthetic data only).

  Open:      http://localhost:5173
  Sign in:   synthetic.admin   (password + one-time code: MFA)
             synthetic.user    (password only)
  Password:  $HOS_DEV_USER_PASSWORD
  One-time codes for synthetic.admin: add this setup key in any authenticator app
  (Google/Microsoft Authenticator → "enter a setup key", time-based):  $TOTP_KEY

  Become a platform operator (after signing in once as synthetic.admin):
             bash scripts/grant-operator.sh synthetic.admin@example.invalid
             then sign out and in again.

  Stop:      bash scripts/dev-stop.sh        (add --all to also stop the database and Keycloak)
EOF
